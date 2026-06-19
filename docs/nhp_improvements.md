# NHP Function — Safety, Efficacy & Performance Improvements

Analysis based on the full NHP code path: [NhpViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs), [DicomViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs) (slice generation, inverse transform, bounds), [MainWindow.xaml.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs) (crosshairs, click navigation), and [VolumeData.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Imaging/VolumeData.cs) (oblique sampling).

Items are grouped by category and rated by impact.

---

## 🔴 Safety

### 1. Slider ranges don't adapt to NHP bounds — out-of-anatomy navigation

**Severity: HIGH**

`AxialMax`, `CoronalMax`, `SagittalMax` are set once at DICOM load and **never updated** when NHP is active:

```csharp
// DicomViewModel.cs, LoadDicomAsync:
AxialMax    = Volume.Depth  - 1;   // e.g., 299
CoronalMax  = Volume.Height - 1;   // e.g., 511
SagittalMax = Volume.Width  - 1;   // e.g., 511
```

When NHP rotates the volume, the AABB extends (e.g., Z range becomes `[-30mm, 280mm]`). But `AxialIndex` still ranges `0..299`, so `zMm = AxialIndex * Spacing[2]` only covers `0..149.5mm` — the user **cannot scroll to slices in the extended NHP region** (approximately the first and last 10–20% of the rotated volume depending on the rotation angle).

Conversely, some indices map to NHP-space positions that are entirely **outside the rotated anatomy** (showing only black), which wastes slider range and confuses the user.

**Proposed fix:** When NHP is active, dynamically recalculate `AxialMax` etc. based on NHP bounds:
```csharp
AxialMax    = (int)Math.Ceiling((maxZ - minZ) / Volume.Spacing[2]);
CoronalMax  = (int)Math.Ceiling((maxY - minY) / Volume.Spacing[1]);
SagittalMax = (int)Math.Ceiling((maxX - minX) / Volume.Spacing[0]);
```
And the slice position calculation becomes `zMm = minZ + AxialIndex * Spacing[2]` instead of `zMm = AxialIndex * Spacing[2]`.

---

### 2. `invNhp.Transform(uAxisNhp)` transforms a vector as a point

**Severity: HIGH**

In [UpdateAxialSlice](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs#L375-L377):

```csharp
var origin = invNhp.Transform(originNhp);       // ← Point3D (correct)
var uAxis  = invNhp.Transform(uAxisNhp);         // ← Vector3D
var vAxis  = invNhp.Transform(vAxisNhp);          // ← Vector3D
```

`Matrix3D.Transform(Vector3D)` in WPF applies the **full affine transform including translation**. For directional vectors (axis directions), translation should NOT be applied — only the rotation component. Currently this works because the transform is built as translate-rotate-translate, so the net translation of a small direction vector gets partially cancelled, but the result is subtly wrong: the u/v step vectors pick up a translational offset that scales with the vector magnitude.

For `uAxisNhp = (0.5, 0, 0)` the error is small (sub-voxel shift per pixel). But for larger spacings or extreme rotations, this introduces a systematic skew in the oblique sampling grid.

**Proposed fix:** Use `TransformVector` semantics — extract the rotation/scale submatrix:
```csharp
var uAxis = invNhp.Transform(originNhp + uAxisNhp) - invNhp.Transform(originNhp);
var vAxis = invNhp.Transform(originNhp + vAxisNhp) - invNhp.Transform(originNhp);
```
Or compute:
```csharp
var uAxis = new Vector3D(
    invNhp.M11 * uAxisNhp.X + invNhp.M21 * uAxisNhp.Y + invNhp.M31 * uAxisNhp.Z,
    invNhp.M12 * uAxisNhp.X + invNhp.M22 * uAxisNhp.Y + invNhp.M32 * uAxisNhp.Z,
    invNhp.M13 * uAxisNhp.X + invNhp.M23 * uAxisNhp.Y + invNhp.M33 * uAxisNhp.Z);
```

> [!WARNING]
> This is a correctness issue that may produce subtle image distortion under NHP rotation. The current code is "accidentally correct" only because `Vector3D` overloads do apply translation in WPF. Need to verify WPF `Matrix3D.Transform(Vector3D)` behavior — if WPF already excludes translation for vectors (unlike `Point3D`), this is a non-issue. **Needs verification.**

---

### 3. `BoneOnlyBounds.IsEmpty` guard silently disables NHP

**Severity: MEDIUM**

[UpdateNhpTransform](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L77) returns early when `BoneOnlyBounds.IsEmpty`:

```csharp
private void UpdateNhpTransform()
{
    if (BoneOnlyBounds.IsEmpty) return;  // ← silently does nothing
```

If the user loads a DICOM, adjusts NHP sliders **before running bone segmentation**, the sliders move but nothing happens — no visual feedback, no error, no status message. The user thinks the feature is broken.

**Proposed fix:** Show a status message when this guard fires:
```csharp
if (BoneOnlyBounds.IsEmpty) 
{
    StatusText = "⚠ Segment bone first to enable NHP adjustment";
    return;
}
```

---

## 🟡 Correctness

### 4. `VolumePivot == new Point3D(0,0,0)` is an unreliable sentinel

**Severity: MEDIUM**

The [pivot check](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L81):

```csharp
var center = VolumePivot == new Point3D(0, 0, 0)
    ? /* fallback to bounds center */
    : VolumePivot;
```

Uses `(0,0,0)` as a sentinel meaning "not yet set." But `Point3D` equality uses exact double comparison — if `VolumePivot` was ever computed as `(-0.0, 0.0, 1e-17)` (floating point artifacts), the sentinel check fails and a near-origin pivot is used instead of the bounds center, causing the rotation to orbit around the wrong point.

**Proposed fix:** Use a nullable `Point3D?` or a separate `bool HasVolumePivot` flag.

---

### 5. `NhpPitch` / `NhpRoll` / `NhpYaw` rotation order is hardcoded

**Severity: LOW (design choice, not a bug)**

The [rotation composition](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L91-L93) applies rotations in the order: **Pitch (X) → Roll (Y) → Yaw (Z)**. This is one of 6 possible Euler angle conventions. The choice affects how combined rotations behave — e.g., pitching 20° then yawing 10° produces a different result than yawing 10° then pitching 20°.

For clinical NHP, the typical convention is **Yaw → Pitch → Roll** (rotate to face forward first, then tilt). The current order may produce unintuitive combined rotations when the user adjusts multiple axes simultaneously.

**Not a bug**, but worth documenting and potentially reordering if users report that combined rotations feel wrong.

---

## ⚡ Performance

### 6. Three full oblique slice renders per slider tick — no debouncing

**Severity: HIGH**

Every NHP slider change triggers [UpdateAllSlices()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L45-L50):
```csharp
partial void OnNhpLateralChanged(double value) { 
    ... UpdateNhpTransform(); UpdateAllSlices(); 
}
```

`UpdateAllSlices` renders **three oblique bitmaps** (axial + coronal + sagittal), each requiring a full trilinear-interpolated scan of the volume. For a 512×512×300 volume with NHP-expanded output (up to 4× per axis), each slice could sample 500K+ voxels with 8 trilinear lookups each. Three of these per slider tick while the user is dragging = severe UI lag.

**Proposed fix:** Debounce `UpdateAllSlices` with a 50–100ms timer:
```csharp
private System.Windows.Threading.DispatcherTimer? _mprDebounce;

private void ScheduleSliceUpdate()
{
    _mprDebounce?.Stop();
    _mprDebounce ??= new() { Interval = TimeSpan.FromMilliseconds(80) };
    _mprDebounce.Tick += (_, _) => { _mprDebounce.Stop(); UpdateAllSlices(); };
    _mprDebounce.Start();
}
```
Then call `ScheduleSliceUpdate()` instead of `UpdateAllSlices()` in the slider change handlers.

---

### 7. Oblique sampling is single-threaded with no SIMD

**Severity: MEDIUM**

[GetObliqueSliceBgra](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Imaging/VolumeData.cs#L428-L471) uses a nested `for (row) for (col)` loop with per-pixel trilinear interpolation. Each pixel does:
- 3 multiplications + 3 additions for world-to-voxel mapping
- 8 array lookups + 7 multiplications + 7 additions for trilinear
- Window/level + threshold check
- 4 byte writes

This is embarrassingly parallel and has no data dependencies between pixels.

**Proposed fix (low-hanging fruit):** Parallelize the outer `row` loop:
```csharp
Parallel.For(0, outHeight, row => {
    for (int col = 0; col < outWidth; col++) { ... }
});
```

**Proposed fix (high impact):** Use `System.Numerics.Vector<T>` or precompute the origin+row offset outside the inner loop to remove redundant multiplications. The inner loop only needs `origin + row*vAxis + col*uAxis`, where `row*vAxis` is constant per row.

---

### 8. Redundant NHP bounds computation on every slider change

**Severity: LOW**

`GetNhpVolumeBounds()` transforms 8 corner points and takes min/max — this is fast (~50ns), but `UpdateNhpTransform()` also applies the transform to every segment/mesh model. The combined overhead of `UpdateNhpTransform() + UpdateAllSlices()` on every slider tick adds up.

If debouncing (item 6) is implemented, this becomes negligible.

---

## 🎯 UX / Efficacy

### 9. No visual indicator of the NHP rotation center

**Severity: MEDIUM**

The rotation center is computed from `VolumePivot` or `BoneOnlyBounds` centroid but never shown in the 3D viewport or MPR views. When the user adjusts pitch/roll/yaw, they can't see what point the rotation orbits around. If the pivot is wrong (e.g., due to the sentinel issue in item 4, or because `BoneOnlyBounds` includes non-cranial anatomy), the rotation appears to "fly away."

**Proposed fix:** Draw a small sphere or crosshair at the pivot point in the 3D viewport when the NHP panel is open.

---

### 10. No "Reset NHP" button

**Severity: LOW**

There's a `CommitNhp` button to lock the current values as baseline, but no quick way to reset all 6 sliders to 0 (or to the committed baseline). The user must manually drag each slider back. This is tedious during iterative NHP adjustment.

**Proposed fix:** Add a `ResetNhp` relay command:
```csharp
[RelayCommand]
private void ResetNhp()
{
    NhpLateral = _cLat; NhpAnteroposterior = _cAnt; NhpVertical = _cVert;
    NhpRoll = _cRoll; NhpPitch = _cPitch; NhpYaw = _cYaw;
}
```

---

## Summary Table

| # | Category | Issue | Impact | Effort |
|---|----------|-------|--------|--------|
| 1 | 🔴 Safety | Slider ranges don't adapt to NHP bounds | High | Medium |
| 2 | 🔴 Safety | Vector transform includes translation | High | Low |
| 3 | 🔴 Safety | Silent NHP disable without feedback | Medium | Trivial |
| 4 | 🟡 Correctness | VolumePivot sentinel is fragile | Medium | Low |
| 5 | 🟡 Correctness | Rotation order may be unintuitive | Low | Low |
| 6 | ⚡ Performance | No debouncing on slider → 3 slice renders per tick | High | Low |
| 7 | ⚡ Performance | Oblique sampling is single-threaded | Medium | Medium |
| 8 | ⚡ Performance | Redundant bounds computation | Low | N/A (solved by #6) |
| 9 | 🎯 UX | No visual indicator of rotation center | Medium | Low |
| 10 | 🎯 UX | No "Reset NHP" button | Low | Trivial |

> [!IMPORTANT]
> Items **1**, **2**, and **6** are the highest-value fixes. Item 1 directly affects clinical usability (can't reach some slices), item 2 is a correctness risk that needs verification, and item 6 is the biggest performance win for interactive use.
