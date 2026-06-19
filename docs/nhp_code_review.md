# NHP Code Review — Architecture, Efficiency & Correctness

**Date:** 2026-06-19  
**Scope:** All NHP-touching files on the `experimental` branch, cross-referenced against [nhp-function.md](file:///c:/Users/Mirko/Documents/Orthoplanner/docs/nhp-function.md)

---

## 1. Architecture Assessment

### ✅ What's Well Done

| Aspect | Verdict |
|---|---|
| **Partial class separation** | NHP logic is cleanly isolated in [NhpViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs) — the right boundary |
| **Visual-only NHP** | Eliminates the entire class of bugs from physical reslicing (volume replacement, camera jumps, undo corruption) |
| **Ledger pattern** | `CollectionChanged` handlers in [InitNhpLedger()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L213-L218) guarantee new objects get NHP transform on arrival |
| **Debounced MPR** | 60ms `DispatcherTimer` prevents UI freeze during slider drag while keeping 3D model updates instant |
| **Safety clamping** | Hard-wall limits (±200mm / ±45°) enforced at every entry point: `ClampNhp`, `AdjustNhp`, `OnChanged`, project load |
| **Reset/Zero bypass** | Direct field writes in [ResetNhp()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L99-L124) avoid 6× cascading `OnChanged` handlers — correct pattern |

### ⚠️ Architectural Concerns

#### A-1: `_nhpTransform` is a shared mutable field across 3 partial classes

`_nhpTransform` is declared in `NhpViewModel.cs`, **written** only there, but **read** in `DicomViewModel.cs` (line 575, 613, 618) and `SurgeryViewModel.cs` (line 234). Because these are all the same `MainViewModel` partial class, there are no access control boundaries.

**Risk:** Any future partial class could accidentally write to `_nhpTransform` and break the invariant that only `UpdateNhpTransform()` sets it.

**Recommendation:** Consider making `_nhpTransform` a property with a `private set` (or at least add a code comment `// WRITE ONLY IN UpdateNhpTransform()`).

#### A-2: `ComposeTransforms` creates a new `Transform3DGroup` on every call

Every call to [ComposeTransforms](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L248-L257) allocates a `Transform3DGroup` with 2 children. This is called once per segment per NHP update. With N segments, that's N allocations per slider tick.

**Impact:** Low for typical segment counts (5–10), but worth noting for future scaling.

---

## 2. Efficiency Issues

### E-1: Duplicate NHP bounds computation in crosshairs ⭐ (Medium)

[UpdateCrosshairs()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs#L651-L710) calls `GetMprPhysicalBounds` **5 times** per crosshair update (2 for NHP offset extraction at lines 669–676, plus 3 for the actual axial/coronal/sagittal bounds). Since `GetMprPhysicalBounds` just reads cached `_nhpBoundsMin*` fields, this is cheap — **no fix needed**, the method is essentially a struct-return.

### E-2: Redundant `GetMprPhysicalBounds` call in crosshairs

Lines 671–672 call `GetMprPhysicalBounds(Coronal)` to get `tmpMaxZ`, but `tmpMaxZ` is never used. Then lines 675–676 call it again to get `tmpMinZ`:

```csharp
VM.GetMprPhysicalBounds(MprOrientation.Coronal,
    out _, out _, out double tmpMaxZ, out _);  // tmpMaxZ UNUSED
VM.GetMprPhysicalBounds(MprOrientation.Coronal,
    out _, out _, out _, out double tmpMinZ);   // Only this is used
```

**Fix:** Remove lines 671–672 entirely. Only the second call is needed.

### E-3: `outW`/`outH` recomputed in each slice method (spec V-1.2) ⭐ (Low)

Each of `UpdateAxialSlice`, `UpdateCoronalSlice`, `UpdateSagittalSlice` independently computes `outW`/`outH` from the cached `_nhpBounds*` fields. The computation is trivial (one `Math.Ceiling` + one `Math.Min`), so the actual CPU cost is negligible. The spec flags this as V-1.2 but **I'd deprioritize it** — the real bottleneck is the oblique sampling, which is already parallelized.

### E-4: `UpdateNhpTransform()` builds a 5-child `Transform3DGroup` every call

[UpdateNhpTransform()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L143-L181) constructs a new `Transform3DGroup` with 5 children (translate-to-origin, 3 rotations, translate-back+offset) on every slider change. This is called immediately (not debounced).

**Impact:** WPF `Transform3DGroup` is lightweight — the actual matrix multiplication is deferred. **No fix needed.**

### E-5: `ZeroNhpParam` triggers both OnChanged handler AND explicit UpdateNhpTransform

When [ZeroNhpParam](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L128-L141) sets e.g. `NhpLateral = 0`, the generated setter fires `OnNhpLateralChanged`, which calls `UpdateNhpTransform()` + `ScheduleDebouncedSliceUpdate()`. Then lines 139–140 explicitly call `UpdateNhpTransform()` and `UpdateAllSlices()` again.

**Result:** `UpdateNhpTransform()` runs **twice**, and both a debounced AND an immediate `UpdateAllSlices()` are scheduled. The immediate one wins (runs first), and the debounced one fires 60ms later as a redundant no-op.

**Fix:** Use direct field write like `ResetNhp` does:
```csharp
#pragma warning disable MVVMTK0034
if (param.Contains("Lat")) _nhpLateral = 0;
// ...
#pragma warning restore MVVMTK0034
OnPropertyChanged(nameof(NhpLateral)); // or whichever was changed
OnPropertyChanged(nameof(IsNhpDirty));
_mprDebounceTimer?.Stop();
UpdateNhpTransform();
UpdateAllSlices();
```

---

## 3. Correctness Issues

### C-1: Crosshair Z-offset uses wrong coronal bound ⭐⭐ (High)

In [UpdateCrosshairs()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs#L674-L679), the Z position is computed as:

```csharp
VM.GetMprPhysicalBounds(MprOrientation.Coronal,
    out _, out _, out _, out double tmpMinZ);  // vMax from Coronal
zMm = tmpMinZ + VM.AxialIndex * vol.Spacing[2];
```

The comment says "vMax from Coronal = minZ" — this is correct because coronal V-axis is flipped (vMin=maxZ, vMax=minZ). **However**, in [UpdateSliceFromClick()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/MainWindow.xaml.cs#L540-L541), the Z-offset is:

```csharp
VM.GetMprPhysicalBounds(MprOrientation.Coronal,
    out _, out _, out _, out zOff); // vMax = minZ for coronal
```

Both use `vMax` (4th out param) as `minZ`. This is **internally consistent** — ✅ correct. But it's fragile: the convention `vMax = minZ` is established purely by the coronal flip in `GetMprPhysicalBounds`, and any future change to that flip logic would silently break both crosshairs and click-nav.

**Recommendation:** Add a helper method like `GetNhpMinZ()` to encapsulate this convention.

### C-2: No NaN/Infinity validation on project load (spec V-2.1) ⭐⭐ (High)

[ProjectViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs#L279-L284) reads NHP values with `GetDouble()` and then clamps them. But `Math.Clamp(NaN, -200, 200)` returns `NaN` in C# — it passes straight through.

A corrupt `.orthoplan` file with `"Lat": NaN` would propagate NaN through the entire transform pipeline, producing a black viewport with no error message.

**Fix:** After reading each value, check `double.IsNaN || double.IsInfinity` → reset to 0 with warning.

### C-3: Osteotomy wizards ignore NHP dirty state (spec V-3.3) ⭐⭐ (High)

[OsteotomyViewModel.cs](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs) has **zero** references to `IsNhpDirty`. When a wizard opens (e.g. `PlanLeFort1`, `PlanBsso`), it receives DICOM-space vertices while the user sees NHP-rotated models. Uncommitted NHP changes → **misaligned cutting planes**.

**Fix:** At the top of each wizard command:
```csharp
if (IsNhpDirty)
{
    CommitNhp();
    StatusText = "NHP auto-committed before osteotomy.";
}
```

### C-4: Occlusion alignment resets NHP transform (Medium)

In [AlignOcclusions()](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L598), after alignment:
```csharp
occlusion.Transform = Transform3D.Identity;
```
This overwrites whatever NHP transform the ledger had applied. After alignment completes, the occlusion sits in DICOM-space while everything else is in NHP-space.

Similarly, [RealignOcclusionAsync](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L598) also resets to Identity.

**Fix:** After setting Identity, re-apply NHP: `occlusion.Transform = _nhpTransform;`

### C-5: `ModelCenter` staleness after project load (spec V-3.4) (Low)

In `OpenProjectAsync`, the load sequence is:
1. Volume loads → `RefreshCombinedModel()` → `ModelCenter = bounds-center`
2. `VolumePivot` restored from JSON → `ModelCenter` is **NOT** re-synced

So `ModelCenter` stays at the bounds-center rather than the restored `VolumePivot` until the next `UpdateNhpTransform()` call (which does set `ModelCenter = nhp.Transform(center)`). Since `UpdateNhpTransform()` is called at line 328 during project restore, this is **effectively fixed** — the staleness window is sub-millisecond.

**Verdict:** Non-issue in practice.

---

## 4. Spec vs. Code Discrepancies

| Spec claim | Code reality | Verdict |
|---|---|---|
| "V-1.1 resolved: 60ms debounce" | ✅ Implemented at line 55 | Correct |
| "V-3.1 resolved: nullable Point3D?" | ✅ `[ObservableProperty] private Point3D? _volumePivot` | Correct |
| "V-1.2 remaining: triple GetNhpVolumeBounds" | Actually **fixed** — `UpdateAllSlices()` calls `GetNhpVolumeBounds` once (line 322) and caches results in `_nhpBounds*` fields | **Spec is stale — V-1.2 is resolved** |
| "Ledger auto-apply on addition" | ✅ `CollectionChanged` handlers check `Action.Add` | Correct, but **doesn't handle `Reset` action** (see below) |
| "ResetNhp forces immediate update" | ✅ Direct field writes + explicit `UpdateNhpTransform` + `UpdateAllSlices` | Correct |
| "ZeroNhpParam forces immediate update" | ⚠️ Double-fires `UpdateNhpTransform` (see E-5) | Functionally correct but wasteful |

### Ledger gap: `CollectionChanged` only handles `Add`

The ledger handlers in [OnSegmentsChangedForNhp](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L220-L227) only handle `NotifyCollectionChangedAction.Add`. If segments are re-added after a `Clear()` + bulk re-add (e.g., during Undo/Redo or project load), the `Add` action would fire for each re-added item — this is **correct** behavior. The `Reset` action (from `Clear()`) doesn't need handling since cleared items don't need NHP transforms.

**Verdict:** Current implementation is correct for the existing usage patterns.

---

## 5. Prioritized Recommendations

| Priority | Issue | Effort | Impact |
|---|---|---|---|
| 🔴 **P0** | **C-2:** NaN/Infinity validation on project load | 15 min | Prevents silent data corruption |
| 🔴 **P0** | **C-3:** Auto-commit NHP before osteotomy wizards | 20 min | Prevents misaligned surgical plans |
| 🟡 **P1** | **C-4:** Re-apply NHP after occlusion alignment | 5 min | Prevents occlusion floating in wrong space |
| 🟡 **P1** | **E-5:** ZeroNhpParam double-fires UpdateNhpTransform | 10 min | Eliminates redundant work |
| 🟢 **P2** | **E-2:** Remove unused `GetMprPhysicalBounds` call in crosshairs | 2 min | Code hygiene |
| 🟢 **P2** | **A-1:** Document `_nhpTransform` write invariant | 2 min | Future-proofing |
| ℹ️ **Info** | **Spec update:** V-1.2 is actually resolved | 2 min | Spec accuracy |

---

## 6. Summary

The NHP implementation is **architecturally sound** and **functionally correct** for its primary use case (visual-only 6-DOF alignment with debounced MPR). The safety hardening (clamps, matrix checks, MPR caps) covers the crash surface well.

The two highest-priority gaps are:
1. **NaN/Infinity on project load** — a corrupt file can silently poison the entire session
2. **Osteotomy wizard guard** — uncommitted NHP = wrong cutting planes = wrong surgical plan

Both are quick fixes with outsized safety impact.
