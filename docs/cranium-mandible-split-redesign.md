# Cranium / Mandible Split — Redesign Proposal

> Continuation of [cranium-mandible-split.md](cranium-mandible-split.md). That report catalogues P1–P7;
> this doc redesigns the whole function. Decisions already taken with you:
>
> - **Architecture C** — Core solvers behind an interface, one unified wizard shell.
> - **Cast stage** — fold cast alignment *into the wizard*, feeding the split (plane + midline) only.
>
> The P1–P7 fixes stay set aside except where one is the spine fix itself, or folds in for free.

---

## 1. Goals / non-goals

| # | Goal |
|---|---|
| G1 | Preserve the **same inputs and outputs** the downstream workflow already depends on. Nothing the further steps need may be skipped to prepare a case. |
| G2 | Fix the **spine problem**: the infinite occlusal plane carves the cervical spine inconsistently — partly into the mandible (below-plane body tag), partly dropped via `KeepLargestComponent`, partly polluting the cranium (above-plane). No clean boundary. |
| G3 | Make **seed-region-grow a fallback with more granular control** — "theoretically better but slower," opt-in, not the primary path. |
| G4 | Optionally **combine cast alignment** into the wizard, so the occlusal plane / midline can come from real dental anatomy instead of CT-bone picks. |

Non-goals (explicitly out of scope for this redesign):

- The surgical occlusion system — `OcclusionCheckerWindow`, the ICP1/ICP2 surgical-bite flow, `MaxillaOcclusionTransform`/`MandibleOcclusionTransform`, the manual landmark-align windows. Cast alignment reuses the *ICP algorithm*, not the occlusion-checking UI.
- **P4** (half-extents not NHP-baked) — keep the half-extents contract as-is; flagged separately. P1 (space mismatch) *does* fold in because it's the same NHP-bake mechanism and the new wizard already holds the forward transform.

---

## 2. The output contract (this is what "same inputs" means)

Every split path — geometric primary *and* seed fallback *and* landmarks-only — must emit one
`SplitResult` so [`OsteotomyViewModel.SplitCraniumMandibleAsync`](../src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs#L328)
can always do the same thing downstream, regardless of which solver ran:

| Field | Space | Consumed by |
|---|---|---|
| `CraniumVertices`, `MandibleVertices` (mesh) | **DICOM** (split operates in `x*spacing`) | the wizard bakes the forward NHP into them before exposing → `OsteotomyViewModel` assigns baked vertices to `Segments` (fixes P1) |
| `Left/RightCondyleCenter` | **baked/NHP** | Ramus rotation pivots — [SurgeryViewModel.cs:423](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L423) / [:430](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L430) |
| `DentalMidlinePoint` | **baked/NHP** | maxilla/mandible complex pivot — [SurgeryViewModel.cs:355](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L355) |
| `Left/RightCondyleHalfExtents` | baked box scalars | splint `CondyleBox` — [SplintViewModel.cs:100](../src/OrthoPlanner.App/ViewModels/SplintViewModel.cs#L100) |

**The one contract violation today:** the seed split
([SeedSplitWindow](../src/OrthoPlanner.App/SeedSplitWindow.xaml.cs)) emits only two meshes and
**no landmarks** — so any case routed through it silently loses the Ramus fulcrum and the splint
box, which is exactly why "the steps cannot be skipped." Factoring landmark emission into a
solver-independent estimator that both solvers call is what restores G1.

---

## 3. Architecture C

```
OrthoPlanner.Core/Segmentation/CraniumMandible/   (NEW, guarded — refactor target #3 from CODEBASE_CONTEXT §18)
├─ SplitRequest.cs        // solver-agnostic input (volume, bone mask, plane, condyle boxes, midline, knobs)
├─ SplitResult.cs         // solver-agnostic output (see §2) + voxel-count diagnostics
├─ CondyleBox.cs         // Center + HalfExtents (System.Numerics.Vector3)
├─ ICraniumMandibleSolver // SplitResult Solve(SplitRequest, CancellationToken)
├─ CraniumMandibleLandmarks.cs   // LandmarkEstimator: boxes→centers/extents, midline pick→DentalMidlinePoint (baked)
├─ GeometricBoundedSolver.cs     // ← current SplitVoxelMask body, with §4 spine fix
└─ SeedRegionGrowSolver.cs       // ← current SeedSplitWindow Compute body, now also calling LandmarkEstimator

OrthoPlanner.App/
├─ CondyleSplitWindow.xaml.cs    // slims to UI + orchestration; ~1247 → ~400. Calls solver via interface.
└─ ViewModels/OsteotomyViewModel.cs  // passes inverseNhpMatrix; receives SplitResult; assigns baked meshes (P1 closed here)
```

Boundaries follow the existing contracts:

- **Core** uses `System.Numerics.Vector3` (already the convention — see
  [IcpAligner.cs:1](../src/OrthoPlanner.Core/Geometry/IcpAligner.cs#L1)). WPF `Vector3D`/`Matrix3D`
  mapping happens once, at the wizard boundary, reusing the existing
  `ToDoubleMatrix` / `ConvertToMatrix3D` helpers — **no manual transpose** (per the matrix-convention note in CODEBASE_CONTEXT).
- Solver DTOs are `record`s/`sealed class`es — **no interface-with-one-impl**: there are two
  solvers, so `ICraniumMandibleSolver` is justified. No factory (the wizard picks the solver from
  the stage UI), no config object (knobs live on `SplitRequest`).

---

## 4. The spine fix (the centerpiece, primary solver)

### Root cause, from the live code

In [`CondyleSplitWindow.xaml.cs:624-627`](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L624):

```csharp
double dist = planeNormal.X*vx + planeNormal.Y*vy + planeNormal.Z*vz + planeD;
if (dist < 0) segVol.Labels[idx] = mandBodyLabel;       // 202  ← EVERY below-plane bone voxel
else          segVol.Labels[idx] = unassignedAboveLabel; // 203
```

The occlusal plane is infinite. The cervical spine crosses it. So:

1. Below-plane spine → tagged `mandBodyLabel(202)` as a mandible-body candidate, **regardless of connectivity to the actual mandible body**.
2. [:743-747](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L743) merges all `mandLabel(201, grown condyles)` **and** all `mandBodyLabel(202)` into `205`, union-by-label, not union-by-connectivity.
3. [:750](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L750) `KeepLargestComponent(205)` keeps magnitude, drops the rest.

The result is the inconsistent carve you described: a spine fragment below the plane that isn't
osseous-bridge-connected to the mandible body survives *or* doesn't, depending on whether a bone
bridge exists — sometimes it rides along in the mandible (spine tail), sometimes it's silently
dropped, and the above-plane cervical pieces meanwhile fall into the cranium. Three different outcomes for the same anatomy, none anatomy-aware.

**The insight:** the seed split is "theoretically better" not because seeds are magic — because it
has **no infinite plane**. The plane is the spine's worst enemy. So the fix is: demote the plane
from a knife to a **guide** and let **connectivity from a mandible seed** decide membership.

### Fix — plane-as-guide + symphysis seed (~30 lines in the new `GeometricBoundedSolver`)

1. Tag step unchanged in spirit but reframed: below-plane bone → `202` (**body candidate**, not body-final), above-plane → `203` (ramus/condyle candidate).
2. **Add a symphysis seed.** The wizard already picks the interincisal point (pick index 2, the plane-defining midline point). Derive the symphysis seed by dropping ~15 mm inferior from the interincisal point, then snap to the nearest `202` voxel in the same 17³ radius the condyle anchors already use
   ([L646-660](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L646)).
3. **Flood from three seeds, not two**: both condyle anchors (grow through `203`, the ramus, downward toward the plane) **and** the symphysis seed (grow through `202`, the body). The mandible is *one bone*: condyle→ramus→angle→body→symphysis are contiguous, so the condyle flood and the symphysis flood meet through the ramus/angle that crosses the plane. Union → the connected mandible, **by anatomy**.
4. **P3 folds in for free**: the bridge-rejection gate
   ([L715-732](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L715)) counts every non-zero label
   including its own writes and adjacent cranium — so in dense skull base it is always ≥10 and
   constrains nothing. Keep a `pristineBone[bool]` snapshot (true iff the *input mask* labelled bone
   there) and count only those. Now the gate means "stay inside dense original bone," which is what it intended.
5. **Finalize, anatomy-aware:**
   - Grown mandible (`201` from condyle flood + `202` claimed by the symphysis flood) → `205`. THIS is connected-by-construction (the flood is the connectivity), so `KeepLargestComponent` becomes a safety net, not the decision-maker — and it now rarely prunes, because the spine was never claimed.
   - **Unclaimed** `202` (below-plane bone the floods never reached — i.e. the cervical spine, hyoid, etc.) → `cranLabel(200)`, **not dropped**. The whole spine goes to the cranium as one consistent home, instead of being split three ways.
   - Everything else leftover bone → cranium, as today.

Net effect on the spine: unreachable from any mandible seed ⇒ never tagged mandible ⇒ lands in the
cranium wholesale. No infinite plane bisects it; no magnitude heuristic decides its fate. The plane
now only biases *where condyle/ramus vs body candidates pool* — it stopped being the cut.

### Why this is "small diff, lazy"

It reuses the existing flood, the existing 17³ anchor-snap, the existing label scheme, and the
existing boxes. The only structural change at the algorithm level is: one extra seed, one extra
claim-set in the flood, one extra finalize line (unclaimed 202 → cranium instead of drop), and a
pristine-bone snapshot for the gate. ~30 lines moved into Core, where it's unit-testable against a
512³ fixture with a known spine.

---

## 5. The two solvers

### `GeometricBoundedSolver` (primary)
The §4 algorithm above — plane-as-guide + 3-seed flood. Default path, ~50 ms on 512³, matches
current performance. `HardSeparation` (the ellipsoid rescue,
[L691-712](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L691)) stays as an opt-in knob on the
`SplitRequest` for fused/low-contrast TMJs where connectivity alone bleeds.

### `SeedRegionGrowSolver` (fallback)
The current [`SeedSplitWindow`](../src/OrthoPlanner.App/SeedSplitWindow.xaml.cs) `Compute` body,
moved to Core: partition → HQ bone mask → `CompetitiveGrowLabelsWithinMask` + `FillNearestLabelWithinMask` → mesh. Two changes only:

- **Emits landmarks** (via the shared `LandmarkEstimator`) — closes the G1 contract violation.
- **Granular control** (G3): expose its existing knobs on the wizard's fallback pane and add the
  ones a rescue path needs — seed-placement per coronal slice (already there), grow iteration cap,
  HQ-HU threshold, and a connectivity-floor "stop growing when the candidate frontier drops below N
  contiguous voxels" (the lever the geometric path gets from its seeds). Single MPR-seeding stays
  first; bilateral auto-seed from the condyle boxes can be offered as a one-click fallback-of-the-fallback.

It's slower (slice sweep + competitive grow) and that's fine — it's the rescue, and the wizard surfaces the cost in the status line so the surgeon knows why.

### `LandmarkEstimator` (both call it)
Pure function of the condyle boxes + midline pick (both already in `SplitRequest`):
`Left/RightCondyleCenter` ← box centers (baked), `Left/RightCondyleHalfExtents` ← box extents,
`DentalMidlinePoint` ← midline pick (baked). Identical to what the wizard builds today — just extracted
so the seed path shares it. No new anatomy inference.

---

## 6. NHP: P1 folds in, P4 stays out

- **P1 (mesh space mismatch):** the wizard already holds `_nhpDisplayTransform` (forward cumulative NHP). Bake it into the returned mesh vertices in `PerformSplit` *before* exposing `SplitResult.CraniumVertices/MandibleVertices` — one matrix multiply over each vertex array, in the one place that already has the matrix. Then `OsteotomyViewModel` assigns baked vertices and there is no "DICOM vs NHP" divergence after a split-follows-commit. (Landmarks are already built in baked space from `leftC`/the midline pick, so only the two meshes need this.)
- **P4 (half-extents under rotated NHP):** leave aside. The splint `CondyleBox` is its own subsystem; "trace the flow end to end first" applies. Flagged in the report, not folded in.

---

## 7. Cast stage (optional, feeds plane + midline only)

`IcpAligner` is the reusable surface — [`Align`](../src/OrthoPlanner.Core/Geometry/IcpAligner.cs#L36)
(trim ICP, already used by STL→CT in
[StlViewModel.AlignDentalScansAsync:97](../src/OrthoPlanner.App/ViewModels/StlViewModel.cs#L97)),
[`AlignRobust`](../src/OrthoPlanner.Core/Geometry/IcpAligner.cs#L143) (occlusion ICP), and
[`ComputeLandmarkTransform`](../src/OrthoPlanner.Core/Geometry/IcpAligner.cs#L306) for an initial
guess. Cast import/classify already lives in
[`ImportStlAsync`](../src/OrthoPlanner.App/ViewModels/StlViewModel.cs#L11) +
`StlClassificationDialog`, and `CleanMergeCastAsync` already enforces "separated jaw segments exist
first" ([StlViewModel.cs:163-175](../src/OrthoPlanner.App/ViewModels/StlViewModel.cs#L163)).

### What the wizard stage does (skip if no casts)
1. **Import + classify** — reuse `ImportStlAsync`'s classify dialog inline; load Upper/Lower.
2. **Align الى CT** — `IcpAligner.ComputeLandmarkTransform` from a few picked pairs (optional;
   identity otherwise) → `IcpAligner.Align` with CT dental/bone surface as target. Bake the rigid
   transform onto the cast vertices. This is the *algorithm only* — not `DentalAlignmentWindow`'s
   dual-viewport landmark UI (776 lines), which stays as the separate manual path.
3. **Derive plane + midline off the aligned cast** — because real crown anatomy is artifact-free vs CT teeth.
4. The cast stage's output reduces the manual picks in the next stage to confirmations.

### What stays untouched (the boundary)
`OcclusionCheckerWindow`, the ICP1/ICP2 surgical bite flow in
[`SurgeryViewModel.AlignOcclusions`](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L727),
`Maxilla/MandibleOcclusionTransform` persistence. The cast stage never writes those — it only
informs the split. The existing `DentalAlignmentWindow` + `CleanMergeCastAsync` keep working for the
full clean-merge flow; the wizard's lightweight align is its *in-wizard* sibling, not its
replacement.

---

## 8. Wizard stage flow (one shell, always-on landmark stage)

| Stage | Action | Skippable? |
|---|---|---|
| **0. Cast (opt)** | import + classify + ICP-align casts; derive occlusal plane + midline if casts present | yes — no casts / user declines → manual plane picks in Stage 1 |
| **1. Plane + seeds** | pick occlusal plane (3 pts on CT, *or* accept cast-derived plane); auto-place condyle boxes + symphysis seed; drag/adjust boxes | produces `SplitRequest` |
| **2. Landmarks** | **always on** — shows + lets you correct the condyle centers, midline, half-extents the estimator derived | no — emits the full `SplitResult` contract even if no solver runs |
| **3. Solve** | run `GeometricBoundedSolver`; show kept/dropped voxel counts (the P2 report-back, for free now) | if result good, accept |
| **4. Rescue** | stuck? switch to `SeedRegionGrowSolver` with granular knobs (G3); re-run, re-use the same landmarks | always available, never the default |

The **`landmarkOnlyMode`** second entry point
([SurgeryViewModel.EnsureCondyleFulcrum:245](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L245))
collapses to "accept at Stage 2 without running Stage 3" — the landmarks are always reconciled in the same Stage-2 UI, so there's one code path for both callers.

---

## 9. My doubts — the genuine forks I want your call on

1. **Cast-derived occlusal plane height.** PCA on the whole arch biases the plane toward the
   gingival base, not the cusp line. Levers: (a) centroid PCA (cheap, vertically off), (b) top-percentile offset along the PCA normal (better cusp height), (c) computed + let the surgeon nudge.

2. **Cast dental-midline from mirror symmetry** is unreliable on asymmetric casts and heavy. Recommend: cast gains the *plane*, the *midline stays a confirmed one-click* on the cast (or CT) — not an auto-fit. That's the part of the cast integration I'm least sure pays for itself.

3. **Symphysis seed offset.** ~15 mm inferior from the interincisal point isn't universal (steep
   plane, deep bite, edentulous). Recommend making it a draggable seed like the condyle boxes
   rather than a magic offset — same UX, consistent. 

4. **Spine → cranium vs spine → exclude-both.** Routing unreachable below-plane bone to the
   cranium lumps cervical spine into "Cranium (Split)". Some surgeons want the spine *out* of both
   resection models. (a) spine→cranium is zero-diff (matches "everything not mandible is cranium");
   (b) keep unreachable below-plane bone labelled out (no segment). I have (a) pencilled in §4.

5. **`landmarkOnlyMode`** — collapse into the always-on Stage-2 path, or keep the explicit second
   entry point for `EnsureCondyleFulcrum`? I recommend collapse for one code path; flag if you want
   the SurgeryViewModel fallback to stay structurally distinct.

6. **P4** — confirm it stays out (my recommendation). Folding it in needs the splint engine's box local-axis convention pinned first.

---

## 10. Suggested build order (smallest-risk first)

1. **Spine fix in the existing `SplitVoxelMask`, in place, before the refactor.** ~30 lines, the
   single highest-leverage change, no Core move, no contract change. Verifiable today on a real scan.
2. **Extract `LandmarkEstimator` + `SplitRequest/Result` + `ICraniumMandibleSolver`**, move
   `SplitVoxelMask` → `GeometricBoundedSolver` and `SeedSplitWindow.Compute` →
   `SeedRegionGrowSolver`. Wizard keeps working, same outputs, now two solvers behind one
   interface. (P2 voxel-count reporting drops in here.)
3. **Slim the wizard** to UI + orchestration; collapse `landmarkOnlyMode` per doubt #5.
4. **P1 NHP mesh bake** — one multiply in `PerformSplit`.
5. **Cast stage** — optional Stage 0; reuse `IcpAligner` + classify dialog; derive plane/midline per your call on doubts #1–#2.
6. **`SeedRegionGrowSolver` granular knobs** — last, the rescue path.

Each step builds clean before the next (per CODEBASE_CONTEXT). Step 1 is deployable on its own; the
rest is structural and reviewable independent of the spine result.
