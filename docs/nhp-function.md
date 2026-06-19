---
name: nhp-function
description: "Canonical spec for the Natural Head Position (NHP) function — architecture, vulnerabilities, hardening roadmap, and viewport behavior (Hybrid NHP: Bake-on-Commit model complete)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 808f7843-213e-48fa-a6c4-b59d5af8afc0
---

# NHP (Natural Head Position) Function — Complete Context

**Last updated:** 2026-06-19
**Status:** Hybrid NHP Architecture fully implemented. Bake-on-commit, Deep-clone Undo/Redo, Cumulative Matrix, and Wizard Auto-Commit Guards deployed.

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
| `ViewModels/NhpViewModel.cs` | 6 slider properties, `_cumulativeNhpMatrix`, `_nhpTransform` (delta), `CommitNhp` (bakes vertices, VolumePivot, landmarks), `UpdateNhpTransform`. Ledger `CollectionChanged` auto-bakes new objects. |
| `ViewModels/DicomViewModel.cs` | Oblique MPR slice generation. Uses `GetInverseNhpTransform` and `GetNhpVolumeBounds`, which now compute the **total** transform (`_cumulativeNhpMatrix * _nhpTransform.Value`). |
| `ViewModels/MainViewModel.cs` | `RefreshCombinedModel()`, `BoneOnlyBounds`, `VolumePivot`, `LeftCondyleCenter`, `RightCondyleCenter`, `DentalMidlinePoint`, `SavedCephLandmarks`. |
| `ViewModels/UndoRedoViewModel.cs` | **Deep-clone** undo system. Captures `_cumulativeNhpMatrix`, `VolumePivot`, NHP baseline, and deep copies of vertex arrays. Uses `SuppressLedgerBake` during restore to avoid double-baking. |
| `ViewModels/ProjectViewModel.cs` | Persists/restores `CumulativeNhpMatrix` and landmarks. Provides backward compatibility (computes cumulative matrix from old visual-only baselines). Uses `SuppressLedgerBake` on load. |
| `ViewModels/OsteotomyViewModel.cs` | `EnsureNhpCommittedForWizard()` guard. Auto-commits dirty NHP before opening wizards. Bakes CondyleSplit wizard results through `_cumulativeNhpMatrix` to align DICOM-space output with baked meshes. |

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

**NHP Ledger:**
When an object is added to the scene:
- If `SuppressLedgerBake == false`, `_cumulativeNhpMatrix` is baked into its raw vertices.
- `_nhpTransform` (the current delta preview) is applied to its `Transform` property.

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
- **Phase 0:** Undo vertex aliasing resolved via deep-clone in `UndoRedoViewModel`.
- **Phase 4:** Reset/Zero commands explicitly force UI/3D updates.

### ⏳ REMAINING VULNERABILITIES
- **V-2.2:** No `VolumePivot` Validation on Project Load — verify restored pivot is within 2× volume bounds.
- **V-3.2:** `IsNhpDirty` Threshold Too Coarse — reduce from 0.01 to 0.001.
- **V-3.4:** `ModelCenter` Staleness After Project Load — re-sync `ModelCenter = VolumePivot`.
- **Phase 4 Features:** NHP Snapshots (named snapshot collection).

---

*This file is the canonical spec for the NHP function. Do not modify the code without updating this file.*
