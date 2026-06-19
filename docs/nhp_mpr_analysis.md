# NHP MPR Analysis: Crosshairs & Scaling Bugs

## Summary

Two distinct issues in the current NHP resampling implementation:

| # | Bug | Severity |
|---|-----|----------|
| 1 | Crosshairs only draw correctly in **Axial** view; Coronal & Sagittal show no crosshairs when NHP is active | **High** |
| 2 | Cranium size differs across the three views (non-uniform scaling when NHP is set) | **Medium** |

---

## Bug 1: Crosshairs Broken in Coronal & Sagittal Views

### What you see
From the screenshot: the axial view shows blue (vertical) and green (horizontal) crosshairs correctly. The coronal and sagittal views show **no crosshairs at all**.

### Root Cause

The crosshair code in [UpdateCrosshairs()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner/App/MainWindow.xaml.cs#L638-L701) computes the physical coordinate of each crosshair position using **original DICOM voxel indices × spacing**:

```csharp
double xMm = VM.SagittalIndex * vol.Spacing[0];   // line 652
double yMm = VM.CoronalIndex  * vol.Spacing[1];   // line 653
double zMm = VM.AxialIndex    * vol.Spacing[2];    // line 654
```

These coordinates are in the **original DICOM space** (range: `0` to `Width×Spacing`, etc).

But when NHP is active, [GetMprPhysicalBounds()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs#L274-L301) returns the **NHP-rotated AABB bounds** — which are in a completely different coordinate system (e.g., bounds might span `[-30, 280]` instead of `[0, 250]`).

The result: for the **coronal view**, `DrawCrosshairPhysical` receives:
- `hPhys = xMm` = e.g. `125.0` (DICOM-space)
- `hMin, hMax` = e.g. `-35.0, 285.0` (NHP-space)

For axial, this **happens to work** because the NHP bounds for X and Y include the original range, so the crosshair fraction `(xMm - hMin) / (hMax - hMin)` still falls within `[0,1]`. But for coronal/sagittal, the **V axis is Z**, and the `zMm` value from the original voxel index does not align with the NHP-rotated Z bounds. The crosshair line either falls completely outside the visible image area, or maps to the wrong position, causing it to clip out.

> [!IMPORTANT]
> The fundamental issue is **coordinate mismatch**: crosshair positions are in DICOM-space while the MPR bounds (when NHP-padded) are in NHP-space. This needs a transform.

### Proposed Fix

When NHP is active, transform the crosshair physical coordinates through the NHP transform to get NHP-space positions:

```csharp
// In UpdateCrosshairs(), after computing xMm/yMm/zMm:
if (VM.IsNhpPadded)
{
    // Transform the slice-plane position from DICOM-space to NHP-space
    var nhpMatrix = VM.GetNhpForwardTransform(); // need to expose _nhpTransform.Value
    var nhpPt = nhpMatrix.Transform(new Point3D(xMm, yMm, zMm));
    xMm = nhpPt.X;
    yMm = nhpPt.Y;
    zMm = nhpPt.Z;
}
```

This requires:
1. Exposing the NHP forward transform (currently `_nhpTransform` is private in [NhpViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L27))
2. Transforming the 3 physical coordinates before passing them to `DrawCrosshairPhysical`

**Similarly**, the click-to-navigate code in [UpdateSliceFromClick()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs#L475-L545) has the reverse problem: it converts canvas position → physical coordinate (in NHP-space) and then divides by spacing to get voxel index — but doesn't apply the inverse NHP transform first. This means clicking in coronal/sagittal views will set wrong slice indices when NHP is active.

---

## Bug 2: Non-Uniform Cranium Scaling Across Views

### What you see
From the screenshot: the axial view shows the cranium filling most of the horizontal width, but in coronal and sagittal views the cranium appears significantly smaller (more black padding around it).

### Root Cause

The display height ratios are set only at **DICOM load time** in [LoadDicomAsync()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs#L229-L233):

```csharp
AxialDisplayHeight    = new GridLength(Volume.Height * Volume.Spacing[1], Star);  // phys Y extent
CoronalDisplayHeight  = new GridLength(Volume.Depth  * Volume.Spacing[2], Star);  // phys Z extent
SagittalDisplayHeight = new GridLength(Volume.Depth  * Volume.Spacing[2], Star);  // phys Z extent
```

These are **never updated when NHP changes**. When NHP rotates the volume, the NHP-padded AABB has **different aspect ratios** than the original volume axes. The bitmap output sizes in [UpdateCoronalSlice()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs#L403-L468) and [UpdateSagittalSlice()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs#L471-L537) use the NHP bounds:

```csharp
outW = (maxX - minX) / Spacing[0];   // NHP-space width
outH = (maxZ - minZ) / Spacing[2];   // NHP-space height
```

But the grid row heights still reflect original DICOM proportions. So the `Image Stretch="Uniform"` letterboxes differently per view — the cranium no longer appears at consistent scale.

### Proposed Fix

**Update `DisplayHeight` whenever NHP changes.** After `UpdateAllSlices()` computes the NHP bounds, recalculate the display heights:

```csharp
// In UpdateAllSlices(), after computing NHP bounds:
if (IsNhpPadded)
{
    double nhpExtentY = _nhpBoundsMaxY!.Value - _nhpBoundsMinY!.Value;  // Axial V extent
    double nhpExtentZ = _nhpBoundsMaxZ!.Value - _nhpBoundsMinZ!.Value;  // Coronal/Sagittal V extent
    AxialDisplayHeight    = new GridLength(nhpExtentY, GridUnitType.Star);
    CoronalDisplayHeight  = new GridLength(nhpExtentZ, GridUnitType.Star);
    SagittalDisplayHeight = new GridLength(nhpExtentZ, GridUnitType.Star);
}
else
{
    // Restore original proportions
    AxialDisplayHeight    = new GridLength(Volume.Height * Volume.Spacing[1], GridUnitType.Star);
    CoronalDisplayHeight  = new GridLength(Volume.Depth  * Volume.Spacing[2], GridUnitType.Star);
    SagittalDisplayHeight = new GridLength(Volume.Depth  * Volume.Spacing[2], GridUnitType.Star);
}
```

However, this alone won't guarantee **uniform mm-per-pixel across views**, because each view has a different horizontal extent (axial: X span, coronal: X span, sagittal: Y span). For truly uniform cranium size, you'd also need to ensure the **column width** constraint makes each panel the same physical-mm-per-pixel.

Since all three views share the same column width in the XAML grid, and images use `Stretch="Uniform"`, the actual mm-per-pixel for each view is:

```
mm_per_pixel = max(hRange / panelWidth, vRange / panelHeight)
```

To make cranium size consistent, all three views need the **same mm_per_pixel**. This means computing a global `maxMmPerPixel` from all three views and then either:
- Padding the bitmaps to a common scale, or
- Adjusting the `DisplayHeight` values to enforce a common scale

---

## Recommended Implementation Order

1. **Fix crosshairs first** (Bug 1) — expose NHP forward transform, transform crosshair coords
2. **Fix click navigation** — apply inverse NHP transform before converting to voxel index
3. **Fix display height scaling** (Bug 2) — update `DisplayHeight` dynamically when NHP bounds change

> [!NOTE]
> The click navigation fix (step 2) is entangled with the crosshair fix since both share the same coordinate-space mismatch.

## Files Involved

| File | What to change |
|------|----------------|
| [NhpViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs) | Expose NHP forward transform matrix as public property |
| [DicomViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/DicomViewModel.cs) | Update `DisplayHeight` in `UpdateAllSlices()`; add NHP-aware bounds for crosshairs |
| [MainWindow.xaml.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs) | Transform crosshair coords through NHP in `UpdateCrosshairs()`; transform click coords through inverse NHP in `UpdateSliceFromClick()` |
