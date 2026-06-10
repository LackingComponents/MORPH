# MORPH — Vibe-Coding Risk Register

This document flags problems observed during a full codebase audit (June 2026).  
These are NOT build errors — the code compiles. They are **latent risks** that could  
cause bugs, data loss, or difficult-to-debug behavior in future sessions.

*Last updated: June 2026 — audit + two fix sessions.*

Each issue has a severity rating:
- 🔴 **HIGH** — likely to cause a real bug or data loss
- 🟡 **MEDIUM** — fragile, could break under certain conditions
- 🟢 **LOW** — technical debt, unclear/inconsistent, but currently harmless

---

## 🔴 HIGH — Segment Names Are Magic Strings Throughout the Codebase

**Files affected:** `SurgeryViewModel.cs`, `OsteotomyViewModel.cs`, `SegmentationViewModel.cs`, `StlViewModel.cs`, `ProjectViewModel.cs`

**Problem:** Surgical roles (which segment is the mandible, maxilla, chin, ramus) are resolved at runtime by string matching on `SegmentViewModel.Name`. For example:

```csharp
var mandible = Segments.LastOrDefault(s => s.Name.Contains("Mandible")
    && !s.Name.Contains("Cranium") && !s.Name.StartsWith("Ramus") && s.IsVisible);
```

If a future AI session renames a segment (even to fix a typo), surgical transforms silently apply to the wrong bone.

**When this bites you:** A surgical movement slider that "does nothing" or moves the wrong segment.

**Mitigation (not yet done):** Introduce an enum `SegmentRole` on `SegmentViewModel` and assign it during creation. Use roles for lookup, names for display only.

---

## 🔴 HIGH — Undo Snapshots Are Shallow (Mutated Vertices Are Not Protected)

**File:** `ViewModels/UndoRedoViewModel.cs`

**Problem:** `CreateStateSnapshot()` copies collection references, not deep clones:
```csharp
Segments = Segments.ToList(),  // List of the same SegmentViewModel objects
```
If vertices are mutated in-place after a snapshot is taken (e.g., by `PerformPhysicalResliceAsync`, which loops over `seg.Vertices[i]` directly), the snapshot will reflect the mutation. Pressing Undo after a vertex-mutating NHP commit could crash or silently produce garbage vertices.

**When this bites you:** Undo after "Commit NHP" or any operation that mutates `.Vertices` directly.

**Mitigation (not yet done):** Deep-clone vertices in `CreateStateSnapshot()`, or exclude vertex-mutating operations from the undo scope and document this clearly.

---

## 🔴 HIGH — `_segVolume` Is Not Saved, But Cranium/Mandible Split Depends on It

**File:** `ViewModels/OsteotomyViewModel.cs` (`SplitCraniumMandibleAsync`)

**Problem:** The Condyle Split wizard requires a valid `_boneOnlySegVolume`. On project load (`OpenProjectAsync`), `_segVolume` and `_boneOnlySegVolume` are both reset to `null`. After loading a saved project, the user cannot use the Condyle Split wizard until re-running bone segmentation.

The code handles this case with a prompt to recompute, but the user may be confused. Worse, if the NHP transform was committed before saving, the re-segmentation will happen on the NHP-corrected volume — which is correct behavior, but not obvious.

**Mitigation (partial):** There is already a fallback recompute in `SplitCraniumMandibleAsync`. Document this in the UI (the status bar message currently does this, but it could be more prominent).

---

## 🔴 HIGH — `ConvertToMatrix3D` Has a Transposition Bug Risk

**File:** `ViewModels/SurgeryViewModel.cs`, line 829–835

**Problem:** `IcpAligner` uses column-vector convention (`result[row][col]`). WPF `Matrix3D` uses row-vector convention. The conversion:

```csharp
private System.Windows.Media.Media3D.Matrix3D ConvertToMatrix3D(double[,] m) =>
    new Matrix3D(
        m[0,0], m[1,0], m[2,0], m[3,0],
        m[0,1], m[1,1], m[2,1], m[3,1],
        ...
    );
```

This transposes the rotation submatrix. The inverse `ToDoubleMatrix(Matrix3D m)` transposes it back. **Both conversions must stay consistent with each other.** If a future session "fixes" one without fixing the other, alignment results will be silently wrong (the bone will move to the mirror position).

**Mitigation:** Add a unit test or at minimum document the convention clearly (this file is done).

---

## 🟡 MEDIUM — `HasLeFort1Maxilla` Is a Derived Property with Manual Notification

**File:** `ViewModels/MainViewModel.cs` (constructor) + `OsteotomyViewModel.cs`

**Problem:** `HasLeFort1Maxilla` is a computed property with no backing `[ObservableProperty]`. It is updated via:
1. `Segments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLeFort1Maxilla))` (in constructor)
2. Explicit `OnPropertyChanged(nameof(HasLeFort1Maxilla))` calls scattered in `OsteotomyViewModel.cs`

If a new code path adds/removes a LeFort1 segment without going through the collection's normal change event (e.g., directly mutating `seg.IsVisible`), the UI button that gates sub-osteotomies will not update.

**Note:** `HasLeFort1Maxilla` also checks `s.IsVisible`, so hiding the segment will make sub-osteotomy buttons disappear — this may be intentional but is surprising.

---

## 🟡 MEDIUM — `UpdateSurgeryTransform` Uses `LastOrDefault`, Not `FirstOrDefault`

**File:** `ViewModels/SurgeryViewModel.cs`

**Problem:** For segment lookup:
```csharp
var maxilla = Segments.LastOrDefault(s => s.Name.Contains("Maxilla") && s.IsVisible);
```
`LastOrDefault` means it picks the most recently added segment matching the name. This is intentional (after a 2-piece sagittal cut, both "Maxilla Left" and "Maxilla Right" contain "Maxilla" — but neither should be the maxilla driver anymore, the original "Maxilla (LeFort 1 Separated)" is hidden). However, if any new segment with "Maxilla" in the name is accidentally created, it becomes the surgical driver silently.

**Mitigation:** Prefer explicit name matching (equals) over Contains wherever possible.

---

## 🟡 MEDIUM — `PerformPhysicalResliceAsync` Does Not Update `_boneOnlySegVolume`

**File:** `ViewModels/DicomViewModel.cs`

**Problem:** After `CommitNhpAsync`, the `_segVolume` is reset to a fresh empty `SegmentationVolume` for the new resliced volume dimensions. But `_boneOnlySegVolume` is set to `null` implicitly (it keeps its old value, which now has wrong dimensions). There is a dimension check in `SplitCraniumMandibleAsync` (`HasUsableBoneMask`) that catches this and triggers re-segmentation, but this means every NHP commit invalidates the bone mask and forces the user to re-run segmentation before splitting.

---

## 🟡 MEDIUM — `SplitConnectedComponents` Has a Variable Shadowing Bug

**File:** `OrthoPlanner.Core/Segmentation/SegmentationEngine.cs`, line 387

```csharp
int nIdx = cx + n[0], cy_new = cy + n[1], cz_new = cz + n[2];
```

Here `nIdx` is used as if it's the X coordinate of the neighbor, but it's named `nIdx`, which is confusing (in every other method it means a flat array index). The actual flat index is computed as:
```csharp
int flatIdx = nIdx + cy_new * w + cz_new * w * h;
```
This is correct (nIdx = nx here), but the naming is misleading and could cause errors if this code is ever refactored.

---

## 🟡 MEDIUM — `KeepTopPercentageComponents` Uses 1D Offset BFS — Wrong at Volume Boundaries

**File:** `OrthoPlanner.Core/Segmentation/SegmentationEngine.cs`

```csharp
int[] n6 = { 1, -1, w, -w, w * h, -w * h }; // 1D offsets for speed
```

This BFS does not check XYZ boundary conditions. Neighbor `curr + 1` may wrap to the next row at the right edge of the volume. For dental segmentation (which uses this function), this can cause incorrect component labeling at volume edges. For typical CT data this is unlikely to matter (the edges are air), but it's not correct.

---

## 🟡 MEDIUM — `MorphologicalClosing` Skips Volume Boundary Voxels (z=0, z=Depth-1, etc.)

**File:** `OrthoPlanner.Core/Segmentation/SegmentationEngine.cs`

The dilation/erosion loops start at `z=1` and end at `z=d-1`. Bone voxels on the first/last slice, or first/last row/column, are never processed. For most CT scans this is harmless (boundary is always air), but for small/cropped volumes it could leave artifacts.

---

## ✅ FIXED — `CephalometryOverlay` Had No Persistence and No Public API

**File:** `Views/CephalometryOverlay.xaml.cs`

**What was fixed:** Landmarks are now saved to `project.json` via `SavedCephLandmarks` on `MainViewModel` and restored in `RestoreLandmarkData()` on `SetVolume()`.
A public API was added (`GetMeasurements()`, `SetMeasurementVisible()`, `DeleteMeasurementFromTree()`, `MeasurementsChanged` event) to allow `MainWindow` to drive the new Measurements tab tree.

**Still pending:** `CephalometryOverlay.xaml.cs` is still ~2100 lines with no dedicated ViewModel. The class is functional but not data-bindable from XAML.

---

## ✅ FIXED — `OcclusionAlignmentWindow` Alignment Was Lost on Project Save

**File:** `OcclusionAlignmentWindow.xaml.cs` / `ViewModels/ProjectViewModel.cs`

**What was fixed:** `ProjectViewModel.SaveProject()` now writes each occlusion STL's vertex data to `occlusions/N_Name.bin` and serializes the corresponding `MaxillaOcclusionTransform` and `MandibleOcclusionTransform` as a 16-element JSON array in `project.json` under `"OcclusionTransforms"`. `OpenProjectAsync()` restores both. Project version bumped to `2.1`.

**Note:** Only the final applied transform and vertices are saved. Intermediate landmark pairs from the manual alignment window are not persisted (they are discarded once the window closes).

---

## 🟡 MEDIUM — Cephalometric Measurements Are Not Saved to the Project File

**File:** `ViewModels/ProjectViewModel.cs`

**Problem:** Only cephalometric **landmarks** (the named anatomical points) are saved in `project.json`. The user-drawn **measurements** (`CephMeasurement` list — custom points, lines, planes, angles, distances) are held only in `CephToolState.Measurements` in memory. Closing and reopening the project loses all drawn measurements.

**When this bites you:** The user draws 10 cephalometric measurements, saves the project, reopens it — all measurements are gone. Landmarks are restored, but the user must redo all measurements.

**Mitigation (not yet done):** Add `CephMeasurement` JSON serialization to `SaveProject` (similar to how `CephLandmarks` is saved) and deserialize + push into `_toolState.Measurements` on `OpenProjectAsync` via a new public method on `CephalometryOverlay`.

---

## 🟢 LOW — `Segments` Collection Has Byte Label (Max 255 Segments)

**File:** `ViewModels/MainViewModel.cs`

`SegmentViewModel.Label` is a `byte` (0–255). Label 0 = unlabeled. So maximum 255 distinct labeled segments. In practice, after repeated osteotomy+undo+redo cycles, labels can accumulate. The assignment `label = (byte)(Segments.Count + 1)` can overflow or collide if the count exceeds 254.

---

## 🟢 LOW — Spurious Root-Level Files

Files at the repository root that should not be there:
- `ViewCube.cs`, `ViewCubeVisual3D.cs` — leftover exploration
- `extract.cs`, `patch.cs`, `probe*.cs` — one-off scripts
- `RefactorUI/`, `RestoreUI/` — old refactoring artifacts
- `OrthoPlanner.App_*.csproj` in the App folder — WPF temp project files from build tooling (these are auto-generated and benign but visually noisy)

---

## 🟢 LOW — `build_errors.txt` and `build_out.txt` in Repository Root

These are build log snapshots likely committed by accident. They contain absolute paths specific to the original developer's machine and are not useful as tracked files. They could be added to `.gitignore`.

---

## 🟢 LOW — `DentistPanelViewModel` / `SplintViewModel` — Minimal Implementation

`SplintViewModel.cs` (4.9 KB) exists but appears to be a stub. The `SplintPlannerWindow` (27 KB) likely still contains most of its business logic in code-behind.

---

*Last updated: June 2026 — full audit of all ViewModels, Core, and Infrastructure.*
