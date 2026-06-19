---
name: main-viewport-centering
description: "Complete architecture and behavior of the main 3D viewport, camera centering, model transforms, and how everything ties together (updated for visual-only NHP and baked VolumePivot)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 808f7843-213e-48fa-a6c4-b59d5af8afc0
---

# Main Viewport Centering & Model Behavior

**Last updated:** 2026-06-18
**Status:** Phase 0 & 1 stable. Visual-only NHP with oblique MPR slices.

---

## 1. The Viewport (XAML)

**File:** `MainWindow.xaml`

```xml
<hx:Viewport3DX ShowCameraTarget="False" x:Name="Viewport3D"
    BackgroundColor="#FF0C1018"
    RotateAroundMouseDownPoint="False"          <-- ROTATES AROUND FixedRotationPoint
    CameraRotationMode="Turnball"
    ModelUpDirection="0,0,1"                     <-- Z is UP in model space
    ZoomExtentsWhenLoaded="False"
    ...>

    <hx:PerspectiveCamera x:Name="MainCamera"
        Position="0,-300,0" LookDirection="0,1,0"
        UpDirection="0,0,1" />
```

- **Key setting:** `RotateAroundMouseDownPoint="False"` means the camera orbits around `FixedRotationPoint`, NOT the mouse cursor.
- This is why `FixedRotationPoint` is managed programmatically to prevent the model from drifting.

---

## 2. What is Rendered in the Viewport

The 3D scene contains:

| Element | Source |
|---|---|
| Live Preview Mesh | `LivePreviewGeometry` / `LivePreviewMaterial` (Marching Cubes bone preview) |
| Volume Rendering | `VolumeTextureModel3D` (direct volume rendering) |
| All Segments | `ItemsModel3D ItemsSource="{ NoBinding Segments}"` |
| All Imported Meshes | `ItemsModel3D ItemsSource="{Binding ImportedMeshes}"` |

Each item is rendered as:
```xml
<hx:MeshGeometryModel3D
    Geometry="{Binding Geometry}"
    Material="{Binding Material}"
    IsRendering="{Binding IsVisible}"
    Transform="{Binding Transform}"
    IsTransparent="{Binding IsTransparent}" />
```

**NOT rendered in the main viewport:**
- `LoadedOcclusions` — these appear in the surgical movement/occlusion planner, not the main 3D view.
- The occlusion `Transform` is still updated by `UpdateNhpTransform()` but it is not displayed here.

---

## 3. The Transform Stack (Updated for Visual-Only NHP)

Each model in the viewport has a `Transform` property. Here is how it is computed:

### 3.1 Segments (Segments collection)

```
seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform)
```

- `_nhpTransform`: the **total** current NHP values (NOT a delta from baseline). Applied uniformly to ALL segments, meshes, and occlusions.
- `seg.SurgicalTransform`: per-segment movement from surgical sliders. Unaffected by NHP commit.
- `ComposeTransforms(first, second)` builds a `Transform3DGroup`: applies `first`, then `second`.

### 3.2 Imported Meshes

```
mesh.Transform = _nhpTransform
```

Imported STLs do NOT have a surgical transform. They move only with NHP.

### 3.3 Named Models (HardTissueModel, SoftTissueModel, DentalModel)

These are the "original" segment models (created by segmentation). They only get `_nhpTransform`:

```csharp
HardTissueModel.Transform = _nhpTransform;
SoftTissueModel.Transform = _nhpTransform;
DentalModel.Transform     = _nhpTransform;
```

They do NOT have `SurgicalTransform`.

---

## 4. The `BoneOnlyBounds` and `ModelCenter` System

**File:** `MainViewModel.cs` — `RefreshCombinedModel()`

This is the SINGLE most important method for viewport centering.

### What it does:
1. Computes `newBounds` as the **entire DICOM volume** (not the union of segment bounds):
   ```csharp
   newBounds = new Rect3D(0, 0, 0,
       Volume.Width  * Volume.Spacing[0],
       Volume.Height * Volume.Spacing[1],
       Volume.Depth  * Volume.Spacing[2]);
   ```
2. Compares with the existing `BoneOnlyBounds`.
3. If **unchanged**, it just calls `UpdateNhpTransform()` andRaises `OnPropertyChanged(nameof(BoneOnlyBounds))` and returns.
4. If **changed**, it:
   - Sets `BoneOnlyBounds = newBounds`
   - Computes `ModelCenter` from `VolumePivot` (when set) or the volume geometric center
   - Fires `OnPropertyChanged(nameof(BoneOnlyBounds))`
   - Calls `UpdateNhpTransform()`

### Why this matters:
- **It prevents the camera from jumping** when individual segments are toggled visible/hidden.
- The camera always orbits around the **volume center**, not the center of whatever happens to be visible.
- `BoneOnlyBounds` is the canonical "world box" of the entire scene.

### Phase 0 Addition: Baked `VolumePivot`

On DICOM load, `VolumePivot` is computed once and never changes:

```csharp
VolumePivot = new Point3D(
    Volume.Width * Volume.Spacing[0] / 2.0,
    Volume.Height * Volume.Spacing[1] / 2.0,
    Volume.Depth * Volume.Spacing[2] / 2.0);
```

This is used as the rotation center in `UpdateNhpTransform()`, ensuring the model rotates around a stable point regardless of NHP state.

---

## 5. Camera Auto-Centering on `BoneOnlyBounds` Change

**File:** `MainWindow.xaml.cs` — `OnLoaded`

```csharp
VM.PropertyChanged += (s, args) =>
{
    switch (args.PropertyName)
    {
        case nameof(ViewModels.MainViewModel.BoneOnlyBounds):
            if (VM.IsSplitting) return; // Do not hijack camera during SplitCraniumMandibleAsync

            var b = VM.BoneOnlyBounds;
            var centroid = new Point3D(
                b.X + b.SizeX / 2,
                b.Y + b.SizeY / 2,
                b.Z + b.SizeZ / 2);
            Viewport3D.FixedRotationPointEnabled = true;
            Viewport3D.FixedRotationPoint = centroid;

            // V-0.4: Removed dead IsNhpCommitInProgress guard.
            // Visual-only NHP never changes BoneOnlyBounds, so this handler
            // only fires on DICOM load or project open — camera snap is always correct.

            // Robust centering: wait briefly for HelixScene mapping, then snap to Anterior View
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(250);
                CenterCamera(new System.Windows.Media.Media3D.Vector3D(0, 1, 0));
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            break;
    }
};
```

### The `IsSplitting` guard:
Prevents camera hijacking during `SplitCraniumMandibleAsync` (which triggers many `CollectionChanged` events that would otherwise snap the camera repeatedly).

### ~~The `IsNhpCommitInProgress` guard~~ — REMOVED in Phase 0
This guard was dead code (the flag was never set to `true`). Removed along with the `[ObservableProperty]` declaration. Visual-only NHP never changes `BoneOnlyBounds`, so the camera snap handler only fires on DICOM load or project open — no special NHP guard is needed.

---

## 6. `CenterCamera()` Method

**File:** `MainWindow.xaml.cs`

```csharp
private void CenterCamera(Vector3D? lookDirection = null)
{
    // 1. Pivot = ModelCenter (from BoneOnlyBounds or VolumePivot)
    var pivot = VM.ModelCenter;

    // 2. Look direction (default: current camera direction)
    var dir = lookDirection ?? Viewport3D.Camera.LookDirection;
    dir.Normalize();

    // 3. Distance = diagonal of bounding box * 0.75 (fills view)
    var b = VM.BoneOnlyBounds;
    var diagonal = Math.Sqrt(b.SizeX*b.SizeX + b.SizeY*b.SizeY + b.SizeZ*b.SizeZ);
    var distance = diagonal * 0.75;
    if (distance < 10) distance = 300;

    // 4. Camera position = pivot - dir * distance
    //    LookDirection length = distance (SharpDX uses Position + LookDirection as look-at point)
    Viewport3D.Camera.Position      = pivot - dir * distance;
    Viewport3D.Camera.LookDirection = dir * distance;
    Viewport3D.Camera.UpDirection = lookDirection.HasValue ? new Vector3D(0,0,1) : currentUp;
}
```

### Three buttons call this:
- **Center Camera (circle-cross icon)**: `CenterCamera()` — keeps current look direction, recenters on model.
- **Anterior View (front face icon)**: `CenterCamera(new Vector3D(0,1,0))` — looks from front (Y- toward Y+).
- **Right Profile (side head icon)**: `CenterCamera(new Vector3D(1,0,0))` — looks from right (X- toward X+).

---

## 7. `UpdateNhpTransform()` — The Glue (Updated)

**File:** `ViewModels/NhpViewModel.cs`

This is called every time an NHP slider changes.

### Phase 0 Changes:
1. **Uses baked `VolumePivot`** for the rotation center (stable across reslices and NHP commits).
2. **Applies the FULL current NHP values** (not just delta from baseline):
   ```csharp
   // TOTAL NHP transform: apply the full current NHP values
   var nhp = new Transform3DGroup();
   nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
   nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0), NhpPitch)));
   nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0), NhpRoll)));
   nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1), NhpYaw)));
   nhp.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));
   ```
3. **Updates `ModelCenter`** dynamically:
   ```csharp
   ModelCenter = nhp.Transform(center);
   ```
   This allows freehand rotation pivot to follow the NHP-transformed center.

### Key Insight:
Because NHP is now **visual-only** (no physical reslicing):
- **No more camera jumps on commit.** The volume stays the same, only the visual transform changes.
- **No more undo/redo loss.** Commit just updates the baseline fields.
- **No more surgical slider reset.** Surgery transforms persist across NHP commits.

---

## 8. Headlamp

**File:** `MainWindow.xaml.cs` — `OnHeadlampRendering`

Runs every composition render frame:
```csharp
MainHeadlamp.Direction = new Vector3D(-dir.X, -dir.Y, -dir.Z);   // From camera, opposite to look dir
MainBacklamp.Direction  = new Vector3D( dir.X,  dir.Y,  dir.Z);   // From behind the model
```

This creates a classic 3-point lighting: ambient + front key light + back rim light.

---

## 9. Orthographic Toggle

**File:** `MainWindow.xaml.cs` — `OnProjectionChanged`

Swaps the camera between `PerspectiveCamera` and `OrthographicCamera`:
- Preserves `Position`, `LookDirection`, `UpDirection`.
- `NavCube` only works with `PerspectiveCamera`, so it is disconnected during ortho mode.
- Default `OrthographicCamera.Width = 300` mm.

---

## 10. NavCube

**File:** `MainWindow.xaml`, `MainWindow.xaml.cs`
- A custom `NavCubeControl` is overlaid on the viewport.
- Clicking a face snaps the camera to that cardinal view via `NavCubeFaceSnap()`.
- Rotation (drag on cube) orbits the camera around the current `FixedRotationPoint`.
- **Important:** NavCube only supports `PerspectiveCamera`. It is set to `null` in orthographic mode.

---

## 11. How Everything Changes During NHP Commit (Updated)

### Before Commit (Visual Preview Mode):
- `_nhpTransform` is the full NHP transform computed from current slider values.
- All models move visually via their `Transform` property.
- `ModelCenter` is updated dynamically to follow the visual rotation.
- Camera `FixedRotationPoint` stays at the current `ModelCenter` (which follows NHP).
- No re-rendering of `Geometry` or `Vertices`.

### After Commit (Visual-Only):
1. `CommitNhp()` simply copies slider values to the baseline fields (`_cLat = NhpLateral`, etc.).
2. `_nhpTransform` is **not** reset — it still applies the same transform because the slider values haven't changed.
3. **No volume replacement.** `BoneOnlyBounds` does NOT change.
4. **No camera snap.** The `BoneOnlyBounds` PropertyChanged handler does not fire (bounds didn't change).
5. `IsNhpDirty` becomes `false` because the live values match the baseline.
6. All `SurgicalTransform` properties are preserved.

### Contrast with Old Physical Reslice Behavior:
| Aspect | Old (Physical Reslice) | New (Visual-Only) |
|---|---|---|
| Volume replacement | Yes (new padded volume) | No |
| Camera jump on commit | Yes (snaps to Anterior) | No (BoneOnlyBounds unchanged → handler never fires) |
|.axml | No |
| Undo/redo cleared | Yes | No |
| Surgical sliders zeroed | Yes | No |
| `_boneOnlySegVolume` | Nulled | Preserved |
| MPR slices | From resliced volume | Oblique sampling from original DICOM |

---

## 12. Summary Table: What Updates What (Updated)

| Event | Changes | Trigger | Result |
|---|---|---|---|
| DICOM Load | `Volume` + `VolumePivot` set | `LoadDicomAsync` | `RefreshCombinedModel` → new `BoneOnlyBounds` → camera centers |
| Segmentation | Segment added to `Segments` | `GenerateSegmentMeshAsync` | `RefreshCombinedModel` → no camera jump if same volume |
| NHP Slider | `NhpPitch` etc. | User interaction | `UpdateNhpTransform` → visual transform only, no camera jump |
| NHP Commit | Baseline fields updated | `CommitNhp` | `IsNhpDirty = false`, no camera snap (BoneOnlyBounds unchanged) |
| Project Load | `Volume` + `NhpBaseline` loaded | `OpenProjectAsync` | `RefreshCombinedModel` → `UpdateNhpTransform` + `UpdateAllSlices` |
| Visibility Toggle | `seg.IsVisible` | User | `OnVisibilityChanged` → `RefreshCombinedModel` → no camera jump if bounds same |
| Surgical Slider | `SurgMaxillaLat` etc. | User interaction | `UpdateSurgeryTransform` → visual transform only |
| Ortho Toggle | Camera type | User | `OnProjectionChanged` → swap camera, preserve orientation |

---

## 13. Files Involved in Viewport Centering

| File | Role |
|---|---|
| `MainWindow.xaml` | `Viewport3DX`, camera XAML, `ItemsModel3D` bindings |
| `MainWindow.xaml.cs` | `CenterCamera`, `NavCubeFaceSnap`, `OnHeadlampRendering`, `OnProjectionChanged`, `PropertyChanged` handler for `BoneOnlyBounds`, `IsSplitting` guard |
| `ViewModels/MainViewModel.cs` | `RefreshCombinedModel`, `BoneOnlyBounds`, `ModelCenter`, `VolumePivot` |
| `ViewModels/NhpViewModel.cs` | `UpdateNhpTransform`, `_nhpTransform`, applies full transforms to all models, uses `VolumePivot` for stable center |
| `ViewModels/SurgeryViewModel.cs` | `UpdateSurgeryTransform`, `BuildSurgeryTransform`, surgical per-segment transforms |
| `ViewModels/DicomViewModel.cs` | `UpdateAllSlices`, `GetInverseNhpTransform`, `GetNhpVolumeBounds` — oblique MPR generation |

---

*This file documents the exact centering and transform behavior. Any change to NHP, segmentation loading, or camera logic must be checked against this behavior to avoid breaking the viewport.*
