# Hybrid NHP Plan — Critical Review

## Verdict: The plan is architecturally sound. Six issues found — all correctable.

The core design (bake meshes on commit, keep DICOM original, track cumulative matrix) is the right approach. It solves all three correctness gaps (wizard coordinates, rotation centers, ICP alignment) without touching the volume. Below are the issues I found, ordered by severity.

---

## 🔴 Issue 1: Matrix Multiplication Order is Backwards

**Plan says (Phase 2, step 6):**
```csharp
_cumulativeNhpMatrix = delta_matrix * _cumulativeNhpMatrix  // WRONG
```

**Should be:**
```csharp
_cumulativeNhpMatrix = _cumulativeNhpMatrix * delta_matrix
```

**Why:** WPF `Matrix3D` uses row-vector convention: `point * Matrix = transformedPoint`. So `A * B` means "apply A first, then B."

- `cumulative` transforms DICOM → currently-baked space
- `delta` transforms currently-baked → newly-baked space
- `new_cumulative = cumulative * delta` → "DICOM → old baked → new baked" ✅

The wrong order would work fine for a single commit (same result when cumulative starts as Identity), but would **silently produce wrong results on the second commit** — the exact scenario that's hardest to catch in testing.

**Same fix needed in Phase 3** (`GetInverseNhpTransform`):
```csharp
var total = _cumulativeNhpMatrix * _nhpTransform.Value;  // cumulative first, then delta
```

---

## 🔴 Issue 2: Double-Bake Risk in Ledger Auto-Bake (Phase 4)

The plan's `OnSegmentsChangedForNhp` would bake `_cumulativeNhpMatrix` into every newly-added segment's vertices. But segments are also added during:

1. **Undo/Redo restore** — `RestoreStateSnapshot` calls `Segments.Clear()` then `Segments.Add(s)` for each restored segment. Restored segments **already have baked vertices** from the snapshot. The handler would **double-bake** them.

2. **Project load** — `OpenProjectAsync` adds segments that were saved in baked space. Same double-bake.

**Fix:** Add a suppression flag:
```csharp
private bool _suppressLedgerBake = false;
```

Set `true` before `RestoreStateSnapshot` and during project load, clear after. The ledger handlers check:
```csharp
if (_suppressLedgerBake) { /* still apply visual _nhpTransform, but skip bake */ }
```

---

## 🟡 Issue 3: `VolumePivot` Must Be Baked on Commit (Missing from Plan)

After baking, all vertices are in NHP-oriented space. But `VolumePivot` (the rotation center) stays in DICOM space. The next NHP adjustment would rotate around the wrong world-space point.

**Why it matters:** The delta transform is built as "translate to origin, rotate, translate back + offset." The center point transforms to `center + translation_offset` under this. If VolumePivot isn't updated, the second NHP commit would build a delta centered on the old DICOM-space pivot — but vertices are now in baked space. The rotation would orbit a phantom point.

**Fix:** In `CommitNhp()`, after baking:
```csharp
if (VolumePivot.HasValue)
    VolumePivot = delta.Transform(VolumePivot.Value);
```

---

## 🟡 Issue 4: Undo Snapshot Must Also Capture NHP Cumulative State

The current `StateSnapshot` stores segment/mesh lists. After Phase 0 adds deep-cloned vertices, we also need:

```csharp
private class StateSnapshot
{
    // ... existing ...
    public Matrix3D CumulativeNhpMatrix { get; init; }
    public Point3D? VolumePivot { get; init; }
    public (double X, double Y, double Z)? LeftCondyleCenter { get; init; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; init; }
    public (double X, double Y, double Z)? DentalMidlinePoint { get; init; }
    public double CBaseLat { get; init; }  // NHP baseline values
    // ... all 6 baseline fields ...
}
```

Without this, undo after commit would restore pre-bake vertices but leave `_cumulativeNhpMatrix` in post-bake state. The MPR (which uses `cumulative * delta`) would be wrong, and the next commit would re-bake an already-reverted state.

`RestoreStateSnapshot` must restore all of these, with `_suppressLedgerBake = true` during the segment re-add.

---

## 🟡 Issue 5: Named Models May Overlap with Segments Collection

The plan lists baking `HardTissueModel`, `SoftTissueModel`, `DentalModel` separately from `Segments`. But these are `SegmentViewModel` references that **may also be in the `Segments` collection**. Iterating `Segments` and then baking the named models separately would **double-transform** their vertices.

**Fix:** Build a `HashSet<SegmentViewModel>` of already-baked segments, skip named models that are already in it:
```csharp
var baked = new HashSet<SegmentViewModel>();
foreach (var seg in Segments) { BakeVertices(seg, delta); baked.Add(seg); }
if (HardTissueModel != null && !baked.Contains(HardTissueModel)) BakeVertices(HardTissueModel, delta);
// ... etc
```

---

## 🟢 Issue 6: Backward Compatibility Migration Edge Case (Phase 5)

The plan says: "if `CumulativeNhpMatrix` is absent but `NhpBaseline` has values, compute the cumulative matrix from the baseline values and bake it into all loaded segment/mesh/occlusion vertices."

**Edge case:** Old projects saved with visual-only NHP store vertices in DICOM space and `NhpBaseline` as committed slider values. Recomputing the cumulative matrix from baseline and baking is correct — but the matrix must be built with the **restored `VolumePivot`** as center, not the current one. The plan should specify:

1. Restore `VolumePivot` first
2. Then compute migration matrix from baseline using `VolumePivot` as center
3. Then bake into all loaded vertices

---

## ✅ Aspects That Are Correct

| Plan Element | Verdict |
|---|---|
| Visual-only → hybrid transition (bake meshes, keep DICOM) | ✅ Sound architecture |
| `_nhpTransform` becomes delta (current - baseline) | ✅ Clean separation |
| Deep-clone undo as prerequisite | ✅ Correctly identified as P0 |
| `BakeTransformIntoVertices` using `float[] + Point3D` | ✅ Minimal precision loss |
| Baking anatomical landmarks on commit | ✅ Fixes rotation center bug |
| Cephalometric immutable record rebuild | ✅ Correct approach |
| MPR uses `cumulative × delta` | ✅ Correct (with order fix) |
| Auto-bake new objects from ledger | ✅ Correct (with suppression fix) |
| Phased implementation order | ✅ Dependencies are right |

---

## Revised Commit Flow (with all fixes applied)

```
CommitNhp():
  1. Compute delta_matrix from BuildNhpTransform(slider - baseline, VolumePivot)
  2. SaveStateForUndo()  // deep-clones vertices + cumulative + pivot + landmarks
  3. Bake all vertices:
     a. HashSet<SegmentViewModel> baked
     b. foreach seg in Segments → BakeVertices(seg, delta), baked.Add(seg)
     c. if HardTissueModel not in baked → BakeVertices
     d. if SoftTissueModel not in baked → BakeVertices
     e. if DentalModel not in baked → BakeVertices
     f. foreach mesh in ImportedMeshes → BakeVertices
     g. foreach occ in LoadedOcclusions → BakeVertices
  4. Bake landmarks:
     DentalMidlinePoint = Transform(delta)
     LeftCondyleCenter  = Transform(delta)
     RightCondyleCenter = Transform(delta)
  5. Bake VolumePivot:
     VolumePivot = delta.Transform(VolumePivot.Value)
  6. Bake ceph 3D coords:
     Rebuild SavedCephLandmarks with transformed (X3D, Y3D, Z3D)
  7. Update cumulative:
     _cumulativeNhpMatrix = _cumulativeNhpMatrix * delta_matrix  // CORRECT ORDER
  8. New baseline = current slider values
  9. _nhpTransform = Identity (delta = 0)
 10. RefreshCombinedModel() + UpdateAllSlices()
```

---

## Should We Implement?

**Yes, with these corrections.** The plan is well-structured and the phased approach is right. I recommend:

1. Start with **Phase 0** (deep-clone undo) — this is the safety net for everything else
2. Then **Phase 1 + 2 + 3** together — they're tightly coupled (delta refactor + bake + MPR)
3. Then **Phase 4** (auto-bake) with the suppression flag
4. Then **Phase 5** (persistence) — can be tested independently
5. **Phase 6 + 7** last (ceph + docs)

**Estimated total:** ~250–300 lines of code changes across 6 files.

> [!IMPORTANT]
> This is a large refactor touching the core transform pipeline. Each phase should be verified with a build check and manual test before proceeding. Given the complexity, I recommend using `/goal` to ensure thorough execution.
