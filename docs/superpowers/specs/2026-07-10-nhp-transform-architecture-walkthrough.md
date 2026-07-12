# NHP Transform Architecture — Onboarding Walkthrough

> Deliverable (j) for the `experimental` branch's NHP redesign. This is the operator's manual for the **lazy transform-stack** model that replaced the old vertex-baking model. Read top to bottom; each section ends with the file:line where the claim lives so you can verify it.

Branch `experimental`, base `a813ffb`. Implementation commits `a813ffb..c19df35` (Tasks 1–7).

---

## 1. The one formula

Vertices live in the **source DICOM frame** forever — nothing moves geometry as a "pose." A piece's visible pose is computed at draw time, never stored:

```
piece.Transform = Compose(NhpShared, piece.LocalTransform)
```

- **`NhpShared`** — one shared `Matrix3D`, built once from the six **absolute** NHP sliders of the active profile. Absolute-from-source: zeros = the original un-NHP volume. Rigid (rotation + translation, no scale). Held in `_nhpShared` ([NhpViewModel.cs:46]) and exposed as `NhpSharedTransform` (a `MatrixTransform3D`, [:49]).
- **`piece.LocalTransform`** — the per-piece, **persisted** displacement record. `Identity` for an unperturbed piece/imported cast/splint; a surgical movement for an operated segment. For `SegmentViewModel` it is an alias of the existing `SurgicalTransform` ([MainViewModel.cs:246-250]); for `MeshViewModel` it is a plain `Transform3D` property ([MainViewModel.cs:319], `Identity` default).
- **`Compose(A, B)`** = `Transform3DGroup{A, B}` = "apply A then B" (WPF row-vector convention). `ComposeTransforms` fast-paths to `first` when `second` is Identity ([NhpViewModel.cs:309]).

**Layer diagram:**

```
   source DICOM frame      (vertices live here permanently — pose never moves them)
        │
        │  NhpShared         one 4×4 from the 6 absolute sliders of the active profile
        ▼
   NHP-posed frame         (the foundation; never stored, recomputed at draw)
        │
        │  piece.LocalTransform    per-piece surgical / registration / Identity
        ▼
   world (viewport)        piece.Transform = Compose(NhpShared, LocalTransform)
```

NHP and surgical are **two independent checkpoint layers**: NHP orients the head (foundation); surgical displacements are decided in that NHP frame and compose on top. Neither is bundled into the other — you can commit an NHP, then reoperate the jaw, then switch NHP profiles, and each is a clean layer swap.

**What this replaced** (the old `experimental` bake model, all deleted): `_nhpTransform` (the per-commit delta), `_cumulativeNhpMatrix` (product of committed deltas), the NHP ledger's cumulative-bake on `CollectionChanged`, and `CommitNhp`'s vertex/landmark/`VolumePivot` bake. The `NhpBaked` flag was a zero-reader dead field and is gone too.

---

## 2. The recompute site — one function, one timing rule

There is **one** place that sets every piece's `Transform`:

```
RecomputeAllTransforms():
    NhpShared = BuildAbsoluteNhpPreviewMatrix()        // the active profile's six → one 4×4
    foreach piece in (NamedModels ∪ Segments ∪ ImportedMeshes ∪ LoadedOcclusions):
        piece.Transform = Compose(NhpShared, piece.LocalTransform)
    ModelCenter = VolumePivot                            // constant, source space (INV7 — see §4)
```

Lives at [NhpViewModel.cs:255-281]. A DEBUG `AssertFormulaHolds` ([:284]) pins the formula — it asserts every piece's `.Transform` still equals `Compose(NhpShared, .LocalTransform)` immediately after, so a future edit that writes a `.Transform` by hand trips the build-time/run-time gate (INV1). `piece.Transform` is a **derived value, never stored state**.

Recompute is triggered on: any NHP slider change (debounced), any surgical slider change, profile switch (loads a new six into sliders), profile add/delete, project load, and after commit. One call path; no per-collection bake.

```
 NhpProfiles ─select──► active profile
                           │ 6 absolute values
                           ▼
                      Nhp sliders ──► BuildNhpMatrix ──► NhpShared
                           │                              │
                           │  piece.LocalTransform        │
                           └────────► Compose ◄───────────┘
                                         │
                               piece.Transform  (re-render; no vertex move)
```

---

## 3. The source-space rule for picks, landmarks & pivots

Surgical transforms rotate *around anatomy*: the interincisal midline, the right/left condyle centers. **Landmarks live permanently in source space** — never re-baked. The surgical `LocalTransform` is built from a **source-space pivot**:

```
LocalTransform = T(−pivotSource) ∘ Rot(...) ∘ T(pivotSource + displacement)
```

`NhpShared` maps it at recompute, so the effective visible pivot is `NhpShared ∘ pivotSource` — the jaw swings about the condylar head wherever NHP moved it. Order is **NhpShared first, LocalTransform second** (the existing `ComposeTransforms` order, preserved) — so surgical displacement is decided and rendered in the NHP-rotated frame (rotate yaw → "maxilla forward" becomes a rotated world direction).

### Pick-in-NHP, persist-in-source (the wizard interaction rule)

Every **persistent** anatomical point — condylar axis, interincisal midline, the entire cephalometric set — is picked while the surgeon looks at the **NHP-posed** scene, but what is *stored* is its **CT/source coordinate** = the world hit un-posed at save via `× NhpShared⁻¹`. Changes to NHP then render the point at `NhpShared ∘ pointSource`, so it follows the anatomy; **no re-bake on commit.**

| Path | Implementation |
|---|---|
| Split wizard picks | already routed through `inverseNhpMatrix` → source (`CondyleSplitWindow.xaml.cs:497-515`) |
| Ceph 2D-DRR picks | `Project2DTo3D` is already source — the DRR is generated from the raw volume with no NHP, so a 2D pick projects back to a source coordinate |
| Ceph 3D-viewport picks | **the Task-5 fix** — `CephalometryOverlay.xaml.cs` pick stores `ToSourceSpace(worldHit) = world × NhpShared⁻¹`; the sphere renders posed (`sphere.Transform = NhpSharedTransform`); a one-shot `NhpSharedTransform` `PropertyChanged` listener calls `Repose3DLandmarks()` (Transform-only, no rebuild, keeps toggle state) |

At NHP = source (sliders zero, `NhpShared = Identity`), `ToSourceSpace` is a no-op — the common no-NHP workflow is byte-identical to before.

### Corrected payoff (re-projection, NOT rigid carry)

Switching NHP keeps `LocalTransform` fixed and swaps `NhpShared`:
- **Un-operated pieces** (`LocalTransform = Identity`) reorient **rigidly** by `NhpShared_B ∘ NhpShared_A⁻¹`.
- **Operated pieces** are **re-projected** into the new head frame — *not* rigidly carried (a pure translation doesn't commute with the rotation). This NHP-relativity is the clinical property the design preserves, and the reason multiple NHP profiles are useful for exploring plans.

---

## 4. The three display subsystems

```
                 ┌─ 3D meshes (seg/cast/occ)  ─► Compose(NhpShared, Local)  reorient + translate
 NhpShared ─────┼─ CT volume render (slab)   ─► NhpShared                  reorient + translate
                 └─ MPR 2D slices            ─► oblique resample by NhpShared⁻¹   rotation only; no translate; no reslice
```

- **3D meshes** — `piece.Transform = Compose(NhpShared, LocalTransform)`; reorient + translate (Task 1).
- **CT volume render (3D slab)** — binds `Transform="{Binding NhpSharedTransform}"` so the slab follows all of NHP, **including translation** (Task 2; `MainWindow.xaml` `VolumeTextureModel3D`). This was the missing binding that made translations look broken.
- **MPR 2D slices** — oblique-resample for **rotation only** by `NhpShared⁻¹`; **no translation, no volume reslice** (translations add nothing to the slice view). Existing inverse-transform plumbing stays; AABB sizing stays.

### The translation fix (requirements f + g)

The old "translations don't work" had two causes, both fixed:
- **(a)** the volume render had no NHP binding → bound it (above).
- **(b)** `ModelCenter = NhpShared·VolumePivot` made the camera pivot *follow* the translation and cancel it visually. Fix: feed `ModelCenter` from the **constant** `VolumePivot` (source space) at the tail of `RecomputeAllTransforms` ([:278], INV7). The camera pivot is thereby **decoupled from NHP** — NHP reorients/translate the scene around a fixed pivot.

### Centering preserved (requirement g)

`BoneOnlyBounds` (full DICOM volume, camera-frame lock), `CenterCamera`/NavCube/headlamp/`FixedRotationPoint`, `RotateAroundMouseDownPoint="False"` — all stay. **Only the `ModelCenter` input changed** (from `NhpShared·VolumePivot` to constant `VolumePivot`). The existing centering code is reviewed-but-kept-working.

---

## 5. The bake points — the closed list

**General rule:** no wizard writes vertices as a pose. Only persistent vertex write is the cast/occlusion registration onto CT **source** space; everything else that looks like a "pose bake" is a bug. `RecomputeAllTransforms` writes `piece.Transform`, never `Vertices` — so nothing downstream can re-pose geometry.

| # | Bake point | Rule (as shipped) |
|---|---|---|
| **B1** | Split wizard returns + **all landmark/ceph picks** | **No bake.** Output in source space; `LocalTransform = Identity`. Every persistent anatomical point is picked in NHP-posed view but stored as its source coordinate (`× NhpShared⁻¹`). **P1** (DICOM-vs-NHP mismatch) dies for free — there is no forward-bake to forget. |
| **B2** | Dental-cast / occlusion registration onto CT (`AlignDentalScansAsync`) — **the only persistent bake** | One-time ICP rigid baked into the imported mesh's `Vertices` → CT **source** space; `LocalTransform = Identity` after. The alignment **target is `ctSegment.Vertices`** (source, never a NhpShared-posed copy) — asserted at DEBUG ([StlViewModel.cs:79]). |
| **B3** | Splint spawned from a jaw | **No bake.** Splint verts are surgical-frame-baked via `BakeToCopy` at generation (a *content* write, not a pose — the wafer sits between the already-moved and unmoved arches). The splint mesh keeps **`LocalTransform = Identity`**, and `RecomputeAllTransforms` composes `NhpShared` on top uniformly for every piece — so the wafer tracks **both** arches under any NHP, final/intermediate/bimaxillary/single-jaw alike. *(The plan's literal "set splint.LocalTransform = jaw.LocalTransform" was unimplementable — the splint mesh is created from verts+config only — and would double-apply surgical onto already-baked verts; the uniform-compose rule is the minimal correct resolution. See `SplintViewModel.AddSplintMeshToScene` doc.)* |
| **B4** | Scratch / computation-time (autorotation returned copy, clearance heightfield) | Transient buffers — never assigned to a persisted `Transform`, never written to stored `Vertices`. Quarantined with `// ponytail: B4 …` markers ([SplintEngine.cs:343]). The old `BakeTransformIntoVertices` helper was deleted in Tasks 1–3 — no scratch→stored sites remain. |

**Gone with the bake:** the `CommitNhp` vertex/landmark/`VolumePivot` bake; the ledger's cumulative-bake on add; the occlusion double-bake on reopen (audit HIGH) — occlusions load as source vertices with `LocalTransform = Identity`, registered once at B2, and `NhpShared` applies exactly once; the half-extents-not-NHP-baked bug (P4) — half-extents live in source space and `NhpShared` maps the whole box at recompute.

---

## 6. Commit — a flag flip, not a bake

Under the lazy model **commit moves nothing.** `CommitNhp` flips the active profile's `IsCommitted`/`IsLatest` flags and (if relevant) seeds the next profile from the current six — no vertex touch, no landmark touch, no matrix fold. Why this is safe: `NhpShared` is already the rendered pose (the preview *is* the committed target), and committing just records "these six are now baseline." There is no `cumulative` to maintain because there is no bake to be cumulative *of*.

Consequences (the "absolutely solid" contract — invariants INV1–INV9):
- **INV1** formula holds — DEBUG assert.
- **INV2** pose never mutates vertices.
- **INV3** commit moves nothing.
- **INV4** landmarks are source; follow anatomy through NHP/commit.
- **INV5** surgical composes NHP-relative (NhpShared first).
- **INV6** legacy files render identical to the original (see §7).
- **INV7** translation visible; pivot fixed (ModelCenter constant).
- **INV8** occlusion single-NHP, not cumulative².
- **INV9** splint seats under any NHP (B3 uniform compose).

### Undo — geometry never moves, so undo never restores vertices

`SaveStateForUndo` runs only before surgical ops, never before an NHP commit (commit is a flag flip, no bake). The live six sliders are **uncaptured** across an undo boundary — the NHP pose is identical across it, and reverting the six on undo-of-surgery would "lose NHP along the way" (req c). `StateSnapshot` keeps `VolumePivot` (the re-pose anchor) and the source-space landmarks; restored pieces re-pose via `RecomputeAllTransforms`. `DeepCloneMesh` copies `LocalTransform` (lazy-model correctness for undo of surgical/splint-aligned meshes).

### Save / load — the new file shape

New-format saves persist `NhpProfiles` (the named checkpoints: `Name/Lat/Ant/Vert/Roll/Pitch/Yaw/IsSelected/IsCommitted/IsLatest`) plus per-piece `LocalTransformMatrix` (`MatrixToArray(LocalTransform.Value)` on Segments/ImportedMeshes/OcclusionMeshes). Vertices and landmarks are stored in **source space**. `NhpBaseline`/`CumulativeNhpMatrix` are dropped. Load restores profiles & the active six, restores per-piece `LocalTransform` before each `Add`, then the tail `RefreshCombinedModel` rebuilds `_nhpShared` from the sliders → INV2/INV4/INV8 on reopen. Round-trip is symmetric (`ArrayToMatrix` ↔ `MatrixToArray`, row-major).

---

## 7. The legacy severable shim — and its exit gate

Legacy `.orthoplan` files (saved under the old bake model) are **double-posed** by the lazy model if opened raw: their stored vertices/landmarks are *already* baked by `CumulativeNhpMatrix`, and the freshly-rebuilt `NhpShared` (from the migrated baseline six) would compose on top → `NhpShared_load·(Cumulative·source)` ≈ `Cumulative²·source`. The migration shim un-bakes them.

**What the shim does** (ProjectViewModel.cs load tail, one contiguous `if`-block, search "Task 6 legacy shim"):
1. Gate: only files carrying `CumulativeNhpMatrix` enter (new-format drops that key → skipped).
2. Invert the stored matrix (`cum.Invert()`, guarded `!IsIdentity && HasInverse`).
3. Un-bake **vertices** of every piece — `foreach Segments/ImportedMeshes/LoadedOcclusions` via an inlined `UnbakeVerts(v, m)` (float[] stride-3, `Matrix3D.Transform` on `Point3D`, mutate in place + `BuildModel()`). The Segments loop covers named models (HardTissueModel etc. are *refs into* `Segments`, same `Vertices` array).
4. Un-bake **points**: condyle **centers** + `DentalMidlinePoint` (`Matrix3D.Transform`).
5. Un-bake **ceph 3D** coords (X3D/Y3D/Z3D); the 2D DRR projections are already source → untouched.

After the un-bake, the load tail `RefreshCombinedModel` re-applies a **single** `NhpShared` pose → source verts render posed once = INV6 (renders identical to the original baked view, since `NhpShared_load` is rebuilt from the migrated six ≈ `CumulativeNhpMatrix`).

**Deliberately left untouched:**
- **Condyle half-extents** — old commits baked centers only, so saved extents are already source; consistent with the migrated centers.
- **`VolumePivot` → `ModelCenter`** — this is the camera/orbit anchor, **not** NHP-tracked geometry. Leaving it in its saved/posed space poses it at the legacy volume's *baked* center = `NhpShared_load·source_center` = the displayed center, so the migrated view stays centered on the same on-screen point the legacy user saw (INV6). Un-baking it would re-anchor the camera on source and make the once-centered legacy volume drift — a gratuitous view jump, not a fix; the volume **render** still follows `NhpSharedTransform` (§4) so translation stays visible.

**Severability:** the block is one contiguous `if` carrying header/footer markers and a `// ponytail: removable once legacy bake-model files are out of the wild` note. Deleting it makes the `NhpBaseline` read and `MigrateBaselineToNhpProfileIfNeeded` dead with it — wholesale removable once no legacy files remain. The `MigrateBaselineToNhpProfileIfNeeded` call (the `else`-branch above the shim) reads the legacy `NhpBaseline` six and seeds a single "NHP 1" profile from them, so the live sliders — and thus `NhpShared_load` — match the legacy committed pose.

---

## 8. The DEBUG safety net (Task 7)

- **B2 guard** — `StlViewModel.AlignDentalScansAsync` DEBUG-asserts `ctSegment.Vertices` is the source-space ICP target; trips if a future change pre-poses verts onto a cast before alignment.
- **B4 quarantine** — splint autorotation scratch is marked `// ponytail: B4` at the return; scratch is returned-copy/clearance-probe only, never assigned to a persisted `piece.Transform` or stored `Vertices`.
- **Core math self-check** — `NhpMathSelfCheck.Run()` ([src/OrthoPlanner.App/NhpMathSelfCheck.cs]) runs once at DEBUG startup (App.xaml.cs `OnStartup`, after splash): asserts identity-at-zero, an invertible round-trip (`matrix × inverse = Identity`), and **rigidity** (the upper-left 3×3 is a pure rotation, det = 1, no scale) — the property INV4 (ceph-sphere radius preserved) and INV9 (wafer seating) rely on. `BuildNhpMatrix` was made `internal static` (center passed in) so the check needs no loaded volume; the instance overload delegates with the `VolumePivot`/bone-bounds center.

The manual end-to-end GUI confirms (INV2/4/7/8/9, the B2/B3/B4 paths, the legacy INV6 reopen) require a Dev WPF launch and are flagged pending — they cannot run headless.

---

## Quick reference — the contract at a glance

| Question | Answer |
|---|---|
| Where do vertices live? | Source DICOM frame, forever. |
| What gets stored? | `NhpProfiles` (six each) + per-piece `LocalTransform`; vertices & landmarks in source space. |
| What's derived? | `NhpShared` (from the active profile's six) and every `piece.Transform`. |
| Where's the one recompute? | `RecomputeAllTransforms` (NhpViewModel.cs:246). |
| What does commit do? | Flips profile flags. Moves nothing. |
| Where is geometry allowed to bake? | B2 only — cast/occlusion registration onto CT source. |
| How do picks survive NHP change? | Stored as `world × NhpShared⁻¹`; rendered posed. |
| How do legacy files render right? | Un-baked by `inv(CumulativeNhpMatrix)` once at load; then single-pose via the lazy stack. |

---

*Walkthrough for commit `c19df35`. Source: design spec §1/§3/§8 + the implemented Tasks 1–7. Where the shipped code diverged from the plan's literal (B3 splint `LocalTransform`, legacy `VolumePivot`), this doc reflects the shipped resolution, not the plan.*
