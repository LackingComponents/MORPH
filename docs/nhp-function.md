---
name: nhp-function
description: "Canonical spec for the Natural Head Position (NHP) function — architecture, vulnerabilities, hardening roadmap, and viewport behavior (Hybrid NHP: Bake-on-Commit model with NhpBaked ledger guard)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 808f7843-213e-48fa-a6c4-b59d5af8afc0
---

# NHP (Natural Head Position) Function — Complete Context

**Last updated:** 2026-06-25
**Status:** Hybrid NHP Architecture fully implemented. Bake-on-commit, Deep-clone Undo/Redo, Cumulative Matrix, Wizard Auto-Commit Guards, and `NhpBaked` ledger guard deployed.

---

## 1. What NHP Is

NHP is the 6-DOF alignment step that orients the patient's head before any surgical planning begins. It is the *foundation* of the virtual surgical planning. If NHP is wrong, everything downstream (osteotomies, movement simulations, splints, etc.) is wrong.

The 6 parameters:
- **Translations (mm):** Lateral (X), Anteroposterior (Y), Vertical (Z)
- **Rotations (°):** Pitch (X-axis), Roll (Y-axis), Yaw (Z-axis)

**Hard-wall limits (enforced):** ±200 mm translation, ±45° rotation. Impossible to exceed from any code path.

---

## 2. Current Architecture: Hybrid NHP (Bake-on-Commit)

As of 2026-06-19, the NHP system operates on a **Hybrid Model**:
- The visual NHP sliders represent a **preview delta** from the last committed baseline.
- On commit, this delta is physically **baked** into the vertex arrays of all mesh segments, imported meshes, and occlusion meshes.
- The underlying DICOM volume remains untouched in its original coordinate space.
- A `_cumulativeNhpMatrix` tracks the entire history of baked NHP transforms, acting as the bridge between the baked mesh space and the raw DICOM space.

### 2.1 Files Involved

| File | Role |
|---|---|
| `ViewModels/NhpViewModel.cs` | 6 slider properties, `_cumulativeNhpMatrix`, `_nhpTransform` (delta), `CommitNhp` (bakes vertices, VolumePivot, landmarks), `UpdateNhpTransform`. **NHP Ledger:** `CollectionChanged` handlers auto-bake new objects, checking `seg.DerivedFrom` lineage and `seg.NhpBaked` to prevent double-baking. `InvertMatrix` helper for CondyleSplit. |
| `ViewModels/DicomViewModel.cs` | Oblique MPR slice generation. Uses `GetInverseNhpTransform` and `GetNhpVolumeBounds`, which now compute the **total** transform (`_cumulativeNhpMatrix * _nhpTransform.Value`). |
| `ViewModels/MainViewModel.cs` | `RefreshCombinedModel()`, `BoneOnlyBounds`, `VolumePivot`, `LeftCondyleCenter`, `RightCondyleCenter`, `DentalMidlinePoint`, `SavedCephLandmarks`. `SegmentViewModel.NhpBaked` and `DerivedFrom` properties (see §2.3). |
| `ViewModels/UndoRedoViewModel.cs` | **Deep-clone** undo system. Captures `_cumulativeNhpMatrix`, `VolumePivot`, NHP baseline, and deep copies of vertex arrays. Uses `SuppressLedgerBake` during restore to avoid double-baking. |
| `ViewModels/ProjectViewModel.cs` | Persists/restores `CumulativeNhpMatrix` and landmarks. Provides backward compatibility (computes cumulative matrix from old visual-only baselines). Uses `SuppressLedgerBake` on load. |
| `ViewModels/OsteotomyViewModel.cs` | `EnsureNhpCommittedForWizard()` guard. All wizard-produced segments set `DerivedFrom = parentSeg` for lineage-based ledger bake inference. Passes inverse NHP matrix to CondyleSplitWindow. |
| `CondyleSplitWindow.xaml.cs` | Accepts optional `inverseNhpMatrix`. Transforms baked-space plane/condyle coordinates to DICOM space before voxel split. Output meshes are DICOM-space (via `ExtractSegmentMesh`) and get baked by the ledger on `Segments.Add()`. |

### 2.2 Transform Stack & Math

```csharp
// Visual representation
seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
```

- `_nhpTransform`: The **DELTA** from the committed baseline. It is applied to meshes for visual preview. After a commit, it resets to `Identity`.
- `_cumulativeNhpMatrix`: The **HISTORY** of all commits.
- **Matrix Order (Row-Vector Convention):**
  - Next Cumulative = `_cumulativeNhpMatrix * deltaMatrix`
  - Total Transform (for MPR) = `_cumulativeNhpMatrix * deltaMatrix`

### 2.3 Commit & Ledger Flow

**On Commit (`CommitNhp`):**
1. Compute the delta matrix (`BuildNhpMatrix`).
2. Save deep-clone state to `UndoRedoViewModel`.
3. Iterate all `Segments`, `ImportedMeshes`, and `LoadedOcclusions` to physically bake the delta into their `float[] Vertices`.
4. Bake `VolumePivot`, Condyle Centers, Dental Midline, and Ceph 3D coordinates.
5. `_cumulativeNhpMatrix *= deltaMatrix`.
6. Reset delta (`_nhpTransform = Identity`) and set `IsNhpDirty = false`.

**NHP Ledger (`OnSegmentsChangedForNhp` / `OnMeshesChangedForNhp` / `OnOcclusionsChangedForNhp`):**
When an object is added to a tracked collection, the ledger determines whether to bake the cumulative NHP transform using a three-level guard:

1. **`SuppressLedgerBake` (global, coarse):** When `true`, *all* auto-baking is suppressed. Used by undo/redo restore and project load, where restored segments already carry the correct baked vertices.
2. **`seg.DerivedFrom` (lineage):** If set and the parent's `NhpBaked` is `true`, the child inherits that state — its vertices are already in NHP-baked space (osteotomy children).
3. **`seg.NhpBaked` (direct flag):** Managed by the ledger — set to `true` after a segment is processed (either baked or inherited). Do not set manually.

The decision flow per added segment:
```
bool alreadyBaked = seg.NhpBaked || (seg.DerivedFrom?.NhpBaked == true);
if (!SuppressLedgerBake && !alreadyBaked && seg.Vertices != null && !_cumulativeNhpMatrix.IsIdentity)
    → BakeTransformIntoVertices(seg.Vertices, _cumulativeNhpMatrix)
seg.NhpBaked = true   // mark as baked regardless of path
seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform)
```

**Segment lineage tree (DerivedFrom):**
```
HardTissueModel (Bone)
├── Cranium (Split)          DerivedFrom = boneSegment
│   ├── Cranium (LeFort Upper)    DerivedFrom = cranium
│   └── Maxilla (LeFort 1 Sep.)   DerivedFrom = cranium
│       ├── Maxilla Left (2/3-Piece)   DerivedFrom = maxSeg
│       ├── Maxilla Right (2/3-Piece)  DerivedFrom = maxSeg
│       └── Premaxilla (3-Piece)       DerivedFrom = maxSeg
└── Mandible (Split)         DerivedFrom = boneSegment
    ├── Ramus Left            DerivedFrom = inputSeg (Mandible)
    ├── Ramus Right           DerivedFrom = inputSeg (Mandible)
    ├── Mandible (distal)     DerivedFrom = inputSeg
    │   ├── Mandible (Chin Removed)  DerivedFrom = targetSeg
    │   └── Chin Segment             DerivedFrom = targetSeg
```

The osteotomy code only declares `DerivedFrom = parentSegment` — the ledger does the rest. This eliminates the need for manual `NhpBaked = true` at every wizard call site and makes the parent-child relationship explicit in the data model.

---

## 3. NHP ↔ MPR Slice Interaction

When NHP values are non-zero (or when history exists in `_cumulativeNhpMatrix`), the MPR slices are **oblique** — they sample the original DICOM volume using the inverse of the total transform (`cumulative * delta`).

- **Dynamic Bounds:** `GetNhpVolumeBounds` uses the total transform, shared across all 3 slice updates to avoid redundant calculations.
- **Crosshairs:** Logic in `MainWindow.xaml.cs` explicitly offsets physical coordinates by `nhpBoundsMin` so interactions remain accurate in the expanded slider space.

---

## 4. Undo/Redo & Project Persistence

- **Deep-Clone Undo:** Because `CommitNhp` mutates vertices, `StateSnapshot` performs a deep array copy of all vertices and saves the exact `_cumulativeNhpMatrix` and `VolumePivot`.
- **Project Persistence:** `project.json` stores the 16-element `CumulativeNhpMatrix`. On load, `SuppressLedgerBake` is enabled while segments are instantiated.
- **Backward Compatibility:** If an old project is loaded without `CumulativeNhpMatrix`, the matrix is dynamically calculated from the stored `NhpBaseline` values so new geometry added later gets baked correctly.

---

## 5. Vulnerability Catalog

### ✅ RESOLVED
- **V-0.1:** Clamp NHP values.
- **V-0.2:** Cap `outW`/`outH` at 4× original dimension.
- **V-0.3:** NaN/Infinity checks on inverse matrix.
- **V-1.1 (Performance):** Implemented 60ms `DispatcherTimer` debounce.
- **V-1.2 (Performance):** Triple `GetNhpVolumeBounds` call resolved — bounds now cached across slice updates in `DicomViewModel`.
- **V-2.1 (Safety):** NHP NaN/Infinity Validation on Project Load resolved — guard code implemented during `ReadNhpDouble` and matrix restoration.
- **V-3.1 (Correctness):** Replaced fragile `VolumePivot == (0,0,0)` with robust nullable `Point3D?`.
- **V-3.3 (Safety):** Osteotomy Wizards Ignore NHP Dirty State resolved — `EnsureNhpCommittedForWizard()` guard implemented.
- **V-3.5 (Correctness):** NHP Ledger Double-Bake & CondyleSplit Coordinate Mismatch resolved. Two issues: (a) Osteotomy-derived segments (cut-plane splits) whose vertices are subsets of an already-baked parent were double-baked by the ledger — fixed via `DerivedFrom` lineage on `SegmentViewModel`; ledger inherits bake state from parent. (b) CondyleSplit wizard regenerates meshes from raw DICOM volume (`ExtractSegmentMesh` outputs `x*spacing` coordinates) but used baked-space plane/condyle coordinates for the voxel split — fixed by passing `inverseNhpMatrix` to the wizard, which transforms spatial parameters back to DICOM space before the split. Output meshes are correctly baked by the ledger on `Segments.Add()`.
- **Phase 0:** Undo vertex aliasing resolved via deep-clone in `UndoRedoViewModel`.
- **Phase 4:** Reset/Zero commands explicitly force UI/3D updates.

### ⏳ REMAINING VULNERABILITIES
- **V-2.2:** No `VolumePivot` Validation on Project Load — verify restored pivot is within 2× volume bounds.
- **V-3.2:** `IsNhpDirty` Threshold Too Coarse — reduce from 0.01 to 0.001.
- **V-3.4:** `ModelCenter` Staleness After Project Load — re-sync `ModelCenter = VolumePivot`.
- **Phase 4 Features:** NHP Snapshots (named snapshot collection).

---

*This file is the canonical spec for the NHP function. Do not modify the code without updating this file.*
