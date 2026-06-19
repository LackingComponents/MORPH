---
name: nhp-function
description: "Canonical spec for the Natural Head Position (NHP) function — architecture, vulnerabilities, hardening roadmap, and viewport behavior (Phase 0, 1, 3(partial), and 4(partial) complete)."
metadata: 
  node_type: memory
  type: project
  originSessionId: 808f7843-213e-48fa-a6c4-b59d5af8afc0
---

# NHP (Natural Head Position) Function — Complete Context

**Last updated:** 2026-06-19
**Status:** Crash guards deployed. Dynamic MPR slider ranges, NHP Ledger system, 60ms debouncing, Parallel oblique sampling, and Reset/Zero buttons implemented.

---

## 1. What NHP Is

NHP is the 6-DOF alignment step that orients the patient's head before any surgical planning begins. It is the *foundation* of the virtual surgical planning. If NHP is wrong, everything downstream (osteotomies, movement simulations, splints, etc.) is wrong.

The 6 parameters:
- **Translations (mm):** Lateral (X), Anteroposterior (Y), Vertical (Z)
- **Rotations (°):** Pitch (X-axis), Roll (Y-axis), Yaw (Z-axis)

**Hard-wall limits (enforced):** ±200 mm translation, ±45° rotation. Impossible to exceed from any code path.

---

## 2. Current Code Architecture (experimental branch, 2026-06-19)

### 2.1 Files Involved

| File | Role |
|---|---|
| `ViewModels/NhpViewModel.cs` | 6 slider properties, committed baseline (`_cLat`…`_cYaw`), `IsNhpDirty`, `AdjustNhpCommand`, `CommitNhp`, `UpdateNhpTransform`, `ClampNhp()`. Ledger `CollectionChanged` handlers. Debounce timer. Reset/Zero commands. |
| `ViewModels/DicomViewModel.cs` | Oblique MPR slice generation using `GetInverseNhpTransform` + `GetNhpVolumeBounds`. Dynamic `AxialMax`/`CoronalMax`/`SagittalMax` based on NHP AABB. |
| `ViewModels/MainViewModel.cs` | `RefreshCombinedModel()`, `BoneOnlyBounds`, `ModelCenter`, `VolumePivot` (now nullable `Point3D?`). Wires up Ledger via `InitNhpLedger()`. |
| `ViewModels/ProjectViewModel.cs` | Persist/restore `NhpBaseline` + `VolumePivot` in `.orthoplan` files. Clamps restored NHP values with warning dialog. Updated for nullable `VolumePivot`. |
| `ViewModels/SurgeryViewModel.cs` | Surgical movement sliders. Composes with `_nhpTransform`. `LoadedOcclusions` collection. |
| `MainWindow.xaml` & `.cs` | NHP popup panel with 6 rows of zero buttons, TextBoxes, RepeatButtons, and a RESET button. Crosshairs and click navigation offset logic using NHP physical bounds. |
| `Core/Imaging/VolumeData.cs` | Parallelized (`Parallel.For`) oblique slice sampling methods. |

### 2.2 Current Transform Stack (Visual-Only NHP)

```
seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform)
```

- `_nhpTransform`: **total** current NHP values. Applied uniformly to ALL segments, meshes, and occlusions via the **NHP Ledger**.
- **NHP Ledger:** `MainViewModel` sets up `CollectionChanged` events for `Segments`, `ImportedMeshes`, and `LoadedOcclusions` to auto-apply `_nhpTransform` to any newly added objects.

**On Commit (`CommitNhp`):**
1. Lock the current slider values as the new baseline (`_cLat = NhpLateral`, etc.).
2. Set `IsNhpDirty = false`.

**Reset & Zero:**
1. `ResetNhpCommand`: Uses direct field writes to reset all 6 sliders to baseline, then forces immediate UI + 3D + MPR updates without waiting for debounce.
2. `ZeroNhpParamCommand`: Zeros a specific parameter and forces immediate updates.

### 2.3 Current Data Flow

```
User adjusts sliders
        │
        ▼
OnNhp*Changed ──► ClampNhp() (if out-of-range via TextBox, re-clamp)
        │
        ├──► UpdateNhpTransform() ──► apply _nhpTransform to all 3D models (Ledger guarantees coverage)
        │                                    │
        │                                    ▼
        │                             ModelCenter = nhp.Transform(center)
        │
        └──► ScheduleDebouncedSliceUpdate() (60ms DispatcherTimer)
                 └──► UpdateAllSlices()
                            │
                            ▼
                  GetInverseNhpTransform()
                            │
                  Oblique slice sampling (Parallel.For) via VolumeData
```

---

## 3. NHP ↔ MPR Slice Interaction

When NHP values are non-zero, the MPR slices are **oblique** — they sample the original DICOM volume using the inverse NHP transform.

### Dynamic NHP Bounds and MPR Indexing
- The `AxialMax`, `CoronalMax`, and `SagittalMax` sliders expand dynamically to cover the **entire fully-rotated AABB**.
- Slice offsets incorporate the NHP bound minimums, meaning index 0 starts at the edge of the rotated volume, preventing clipping.
- **Crosshairs and Click-to-Navigate:** Logic in `MainWindow.xaml.cs` explicitly offsets physical coordinates by `nhpBoundsMin` so interactions remain accurate in the expanded slider space.
- **Parallel Sampling:** `VolumeData.cs` uses `Parallel.For` for the outer row loop in oblique slice methods, yielding ~2-4x speedups.

---

## 4. NHP ↔ Viewport Interaction

### The Transform Applied to 3D Models
`UpdateNhpTransform()` applies the **full** current NHP transform to every model:
**Rotation order: Intrinsic X(Pitch) → Y(Roll) → Z(Yaw)**
**Translation:** Applied in the rotated (world) frame, NOT the body frame.

### `VolumePivot` (Rotation Center)
- Refactored from a fragile `Point3D(0,0,0)` sentinel to a robust nullable `Point3D?`.
- If null, falls back to `BoneOnlyBounds` center.

---

## 5. Vulnerability Catalog — Current Status

### ✅ RESOLVED

- **V-0.1:** Clamp NHP values.
- **V-0.2:** Cap `outW`/`outH` at 4× original dimension.
- **V-0.3:** NaN/Infinity checks on inverse matrix.
- **V-0.4:** Removed dead `IsNhpCommitInProgress`.
- **V-1.1 (Performance):** Implemented 60ms `DispatcherTimer` debounce for `UpdateAllSlices()`.
- **V-3.1 (Correctness):** Replaced fragile `VolumePivot == (0,0,0)` with robust nullable `Point3D?`.
- **Phase 4 (Reset/Zero):** Added `ResetNhpCommand` and `ZeroNhpParamCommand` with explicit UI update forcing.
- **UI Feedback:** Added warning "⚠ Segment bone first to enable NHP adjustment" when bone bounds are empty.

### ⏳ REMAINING VULNERABILITIES

- **V-1.2:** Triple `GetNhpVolumeBounds` Call — still called 3 times per frame.
- **V-2.1:** No NHP NaN/Infinity Validation on Project Load — check `double.IsNaN` / `double.IsInfinity` on load.
- **V-2.2:** No `VolumePivot` Validation on Project Load — verify restored pivot is within 2× volume bounds.
- **V-3.2:** `IsNhpDirty` Threshold Too Coarse — reduce from 0.01 to 0.001.
- **V-3.3:** Osteotomy Wizards Ignore NHP Dirty State — auto-commit NHP + show warning before opening wizards.
- **V-3.4:** `ModelCenter` Staleness After Project Load — re-sync `ModelCenter = VolumePivot`.
- **Phase 4 Features:** NHP Snapshots (named snapshot collection).

---

*This file is the canonical spec for the NHP function. Do not modify the code without updating this file.*
