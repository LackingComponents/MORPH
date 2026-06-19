# NHP Improvements — Implementation Plan

## Items to implement (ordered by dependency)

### 1. VolumePivot sentinel fix (Item 4)
**File:** `MainViewModel.cs`
- Change `_volumePivot` to `Point3D?` (nullable)
- Update all `VolumePivot == new Point3D(0,0,0)` checks to `VolumePivot == null` (NhpViewModel.cs, MainViewModel.cs)
- Update ProjectViewModel save/load to handle nullable

### 2. Slider ranges adapt to NHP bounds (Item 1)
**File:** `DicomViewModel.cs`
- In `UpdateAllSlices()`, update `AxialMax`, `CoronalMax`, `SagittalMax` when NHP is active
- In `UpdateAxialSlice()` NHP branch: `zMm = nhpMinZ + AxialIndex * Spacing[2]`
- In `UpdateCoronalSlice()` NHP branch: `yMm = nhpMinY + CoronalIndex * Spacing[1]`
- In `UpdateSagittalSlice()` NHP branch: `xMm = nhpMinX + SagittalIndex * Spacing[0]`
- Expose NHP bounds min values for crosshair/click use

**File:** `MainWindow.xaml.cs`
- Update `UpdateCrosshairs()` to use NHP-offset positions
- Update `UpdateSliceFromClick()` to use NHP-offset for index conversion

### 3. Debounce slider → slice updates (Item 6)
**File:** `NhpViewModel.cs`
- Replace direct `UpdateAllSlices()` calls with debounced version
- Keep `UpdateNhpTransform()` immediate (3D model transforms are fast)

### 4. Parallelize oblique sampling (Item 7)
**File:** `VolumeData.cs`
- Wrap outer `row` loop in `Parallel.For` for all GetObliqueSlice* methods

### 5. NHP ledger — auto-apply on collection add (Item 3)
**File:** `MainViewModel.cs` or `NhpViewModel.cs`
- Subscribe to `Segments.CollectionChanged`, `ImportedMeshes.CollectionChanged`, `LoadedOcclusions.CollectionChanged`
- On item add: apply current `_nhpTransform`
- For segments: compose with SurgicalTransform
- For meshes/occlusions: apply NHP directly

### 6. Reset NHP + per-parameter zeroing (Item 10)
**File:** `NhpViewModel.cs`
- Add `ResetNhpCommand` (reset all to committed baseline)
- Add `ZeroNhpParamCommand(string param)` (zero individual slider)

### 7. Silent-disable feedback (Item 3 from risk register)
**File:** `NhpViewModel.cs`
- Add StatusText message when BoneOnlyBounds.IsEmpty blocks NHP

## Item 2 (Vector3D transform): ✅ Confirmed safe, no fix needed
WPF `Matrix3D.Transform(Vector3D)` uses w=0, translation is excluded.

## Item 5 (rotation order): Skipped by design
