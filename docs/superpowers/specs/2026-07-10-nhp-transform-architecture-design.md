# NHP & All-Transforms Architecture — Design Spec

**Date:** 2026-07-10
**Status:** Design — pending implementation plan
**Branch:** `experimental` (target); prior art on `origin/experimental_Lore`
**Supersedes:** the "vertex-baking + cumulative-matrix + NHP ledger" model documented in `memory/nhp-function.md`
**Reads alongside:** `docs/cranium-mandible-split.md` (P1–P7), `docs/cranium-mandible-split-redesign.md`, `memory/nhp-function.md`

---

## 0. Goal & non-goals

Make the displacement architecture **absolutely solid**: every piece's pose is reconstructible from two stored, never-mutated-by-pose inputs, applied exactly once, from one source each. NHP is never lost or broken by subsequent wizard passes. Multiple NHPs act as geometric checkpoints. NHP translations become functional (visual only; MPR resamples but does not reslice). Legacy `.orthoplan` files auto-migrate via a severable, removal-pending block.

**Non-goals:** the surgical occlusion system (`OcclusionCheckerWindow`, ICP1/ICP2 surgical bite, `Maxilla/MandibleOcclusionTransform`), the manual landmark-align windows, and the cranium/mandible *split* algorithm itself — those are separate concerns. This spec touches them only where their transform storage converges onto the one formula.

---

## 1. Conceptual model — one formula

Vertices live in the **source DICOM frame** forever. A piece's visible pose is computed, never stored as geometry:

```
piece.Transform = Compose(NhpShared, piece.LocalTransform)
```

- `NhpShared` — one shared `Matrix3D`, built once from the 6 absolute NHP sliders, the same matrix for every piece. Absolute-from-source (zeros = original un-NHP volume).
- `piece.LocalTransform` — the per-piece, persisted, displacement record: `Identity` for an unperturbed piece; a surgical movement (LeFort/BSSO/ramus/chin) for operated segments; a cast-fit / registration state for imports; the parent jaw's transform for a derived splint.
- `Compose(A, B) = Transform3DGroup{A, B}` = "apply A then B" (existing `ComposeTransforms`, WPF row-vector convention, unchanged).

**Layer diagram:**

```
   source DICOM frame      (vertices live here permanently — never moved by pose)
        │
        │  NhpShared         one 4×4 from the 6 absolute sliders (= active NHP profile)
        ▼
   NHP-posed frame         (the foundation; never stored, recomputed at draw)
        │
        │  piece.LocalTransform    per-piece surgical / registration / derived
        ▼
   world (viewport)        piece.Transform = Compose(NhpShared, LocalTransform)
```

NHP and surgical are **two independent checkpoint layers**: NHP provides the foundation orientation; surgical displacements are decided in the active NHP frame and compose on top. They are modular — neither is bundled into the other.

**What this replaces** (the current `experimental` model): `_nhpTransform` (delta), `_cumulativeNhpMatrix` (product of committed deltas), the NHP ledger's cumulative-bake on `CollectionChanged`, and `CommitNhp`'s vertex/landmark/`VolumePivot` bake. All gone.

---

## 2. Data model — fields kept / added / deleted

| Piece type | Kept | Added | Deleted |
|---|---|---|---|
| `SegmentViewModel` | `Vertices` (now source space), `Transform` (derived), `NhpBaked`→removed, `DerivedFrom`, `SurgicalTransform` (= its `LocalTransform`), `SurgicalBaked`/`BaseVerticesBackup` (splint-wizard snapshot/restore — stays) | — | `_nhpTransform`/`_cumulativeNhpMatrix` coupling (segments read NhpShared, not a delta field) |
| `MeshViewModel` / occlusion | `Vertices` (source space), `Transform` (derived) | `LocalTransform` (field; `Identity` default; splint sets it to parent jaw's) | `NhpBaked` |
| `MainViewModel` | `VolumePivot` (constant, source space), `ModelCenter` (now fed from constant `VolumePivot`, decoupled from NHP), `BoneOnlyBounds` | `NhpProfiles` (`ObservableCollection<NhpProfileViewModel>`, from prior art), active-profile machinery | `_nhpTransform`, `_cumulativeNhpMatrix`, `_cLat.._cYaw` baseline (vestigial — `EnsureDefaultNhpProfile` guarantees one), the ledge-bake branch of `On*ChangedForNhp` |

`NhpProfileViewModel` (from `origin/experimental_Lore`, taken verbatim as the data layer): `Name`, `IsSelected`, `IsCommitted`, `IsLatest`, and six plain doubles `Lateral/Anteroposterior/Vertical/Roll/Pitch/Yaw`. A profile is a **named checkpoint of the 6 absolute values** plus flags — no geometry.

**The per-piece guarantee the user required:** the ledger's *purpose* (track each piece's transform) is retained as `piece.LocalTransform` — a persisted per-piece field — while the ledger's *mechanism* (bake-on-add) is dropped. Full pose is always reconstructible from `(NhpShared, piece.LocalTransform)`.

---

## 3. The recompute site + the source-space landmark / pivot rule

### 3.1 One function, one timing rule

```
RecomputeAllTransforms():
    NhpShared = MatrixFrom6(NhpLateral, NhpAnteroposterior, NhpVertical,
                           NhpRoll, NhpPitch, NhpYaw)        // absolute-from-source
    foreach piece in (Segments ∪ NamedModels ∪ ImportedMeshes ∪ Occlusions ∪ VolumeRender):
        piece.Transform = Compose(NhpShared, piece.LocalTransform)
```

`piece.Transform` is a **derived value, never stored state.** Recompute is triggered on: any NHP slider change (debounced), any surgical slider change, profile switch (loads new 6 values into sliders), profile add/delete, project load, and after commit. One call path; no per-collection bake.

```
 NhpProfiles ─select──► active profile
                           │ 6 absolute values
                           ▼
                      Nhp sliders ──► MatrixFrom6 ──► NhpShared
                           │                              │
                           │  piece.LocalTransform        │
                           └────────► Compose ◄────────────┘
                                         │
                               piece.Transform  (re-render; no vertex move)
```

### 3.2 The one subtle rule — surgical pivots & landmarks in source space

Surgical transforms rotate around anatomy: `DentalMidlinePoint`, `R/L CondyleCenter`. **Landmarks live permanently in source space**, never re-baked. The surgical `LocalTransform` is built from a **source-space pivot**:

```
LocalTransform = T(−pivotSource) ∘ Rot(...) ∘ T(pivotSource + displacement)
```

`NhpShared` maps it at recompute, so the effective visible pivot is `NhpShared ∘ pivotSource` — the jaw swings about the condylar head wherever NHP moved it. One matrix product; landmark and surgical pivot stay locked to the same anatomy through any NHP change or profile switch. NHP order is **NhpShared first, LocalTransform second** — so surgical displacement is decided and rendered in the NHP-rotated frame (rotate yaw → "maxilla forward" is a rotated world direction). This is the existing `ComposeTransforms(_nhpTransform, seg.SurgicalTransform)` order, preserved; only the bake beneath it is removed.

### 3.3 Corrected payoff (re-projection, not rigid carry)

Switching NHP keeps `LocalTransform` fixed and swaps `NhpShared`:
- **Un-operated pieces** (`LocalTransform = Identity`) reorient **rigidly** by `NhpShared_B ∘ NhpShared_A⁻¹`.
- **Operated pieces** are **re-projected** into the new head frame — *not* rigidly carried (a pure translation doesn't commute with the rotation). This NHP-relativity is the clinical property the design preserves, and the reason multiple NHPs are useful for exploring plans.

---

## 4. The bake points (the closed list of where geometry is allowed to move)

General rule: **no wizard writes vertices as a pose.** The only *persisted* vertex write is the cast/occlusion registration bake onto CT source space (B2); everything else that looks like a "pose bake" is a bug. The cutting wizards and the splint wizard bake nothing — they return source-space geometry or set a `LocalTransform`. Source-space content edits (clean-merge, split returns) are writes, but not pose bakes.

| # | Bake point | Rule | Bug it kills |
|---|---|---|---|
| **B1** | Split wizard returns (`Cranium/MandibleVertices`) | **No bake.** Output in **source space** (the split already operates in `x*spacing` DICOM = source); `LocalTransform = Identity`. | **P1** (DICOM-vs-NHP mismatch) dies for free — there is no forward-bake to forget. |
| **B2** | Dental-cast / occlusion registration onto CT (`AlignDentalScansAsync` → `IcpAligner.Align`; occlusion import). **The only persistent bake.** | One-time bake of the ICP rigid into the imported mesh's vertices → **CT source space**; `LocalTransform = Identity` after. The alignment **target is the CT segment's source-space `Vertices`** (`ctSegment.Vertices`, e.g. `StlViewModel.cs:70,97`) — **never** a copy pre-multiplied by `NhpShared`. Registration, not a pose. The clean-merge write into `ctSegment.Vertices` (`StlViewModel.cs:113`) is the same source-space content write. | Cast-from-own-frame vs CT-source consistency; the cast sits in CT source space, NHP composes on top once. |
| **B3** | Splint spawned from the jaw | **No bake.** Generate the splint geometry directly from the jaw's **source-space** teeth vertices (the lazy model keeps `seg.Vertices` invariant), and set `splint.LocalTransform = jaw.LocalTransform`. The splint inherits the jaw's effective pose by construction — no posed-space generation, no inverse mapping. (Mirrored-model cases, if any, follow the same inherit rule in source space.) | **Splint-order mismatch (audit MED-HIGH)** — splint seats under any NHP, committed or dirty, any profile: same `NhpShared`, same `LocalTransform` → same `Compose`. |
| **B4** | Scratch / computation-time bakes (autorotation returned copy, splint clearance heightfield) | Transient buffers only — never assigned to a persisted `Transform`, never written to stored vertices. | Keeps wizard-local scratch out of persisted state. |

`BakeTransformIntoVertices` stays; the **only persisted call site is B2** (cast/occlusion registration onto CT). B4's transient scratch never touches a stored piece. The cutting wizards and the splint wizard never call it. `CondyleBox` is read at solve-time from *source-space* landmark + half-extents, then posed by `NhpShared` inside `SplintEngine` — its internal local-axis convention is its own; it's fed source-space inputs now.

**Gone with the bake:** the `CommitNhp` vertex/landmark/`VolumePivot` bake; the ledger's cumulative-bake on add; the **occlusion double-bake on reopen (audit HIGH)** — on load, occlusions are source-space vertices with `LocalTransform = Identity` (their registration was baked once at B2), and `NhpShared` is applied exactly once; **P4** (half-extents not NHP-baked) — half-extents live in source space and `NhpShared` maps the whole box at recompute, no per-commit re-bake to forget.

---

## 5. Commit / undo / save & the multi-NHP checkpoint

### 5.1 Profiles × surgery-plan tree = "it's both"

The prior art from `origin/experimental_Lore` (`NhpProfileViewModel`, `NhpProfiles`, `AddNhpProfile`/`SelectNhpProfile`/`DeleteNhpProfile`/`CommitNhp`, camera-angle capture, `RestoreNhpProfilesFromProject`, `MigrateBaselineToNhpProfileIfNeeded`) is taken **verbatim as the data-management layer.** What changes is the engine under it: bake, cumulative matrix, delta, and ledger-bake leave.

- **Multiple NHPs** = `NhpProfiles` (the foundation layer).
- **Multiple surgical plans** = the *existing* surgery-plan tree (`CurrentSurgeryPlan` / `AddPlan`/`SelectPlan`/`SavePlan`, SurgeryViewModel:545+), orthogonal to NHP, untouched.
- They combine freely: "plan A under NHP-2" = select NHP-2 × select surgery plan A. Nothing bundles; `LocalTransform` is per-piece, independent of profiles.

A profile is now a named snapshot of the 6 absolute-from-source slider values + flags — nothing geometric. The 6 live sliders are the single source of `NhpShared`; sliders and the active profile's stored values stay in sync (`SaveActiveNhpProfileFromUi` on settle, `ForceSetNhpUi` on load). `IsCommitted` = "user pressed DONE on this checkpoint" — pure UI/persistence flag, gates no geometry. This **is** the geometric checkpoint.

### 5.2 Commit — lazy (the spine simplification)

```
CommitNhp():
    SaveActiveNhpProfileFromUi()        // stamp live 6 → active profile
    activeProfile.IsCommitted = true
    StatusText = "{name} committed (checkpoint saved)."
```

No vertex bake, no landmark bake, no `VolumePivot` move, no cumulative fold. **"Sliders must show the current matrix's values, not reset to zero"** is satisfied for free: the sliders ARE the absolute values; commit never touches them. `IsNhpDirty` goes false because the stamp made live == stored.

### 5.3 Undo — geometry never moves, so undo never restores vertices

| Edit | Undo restores |
|---|---|
| NHP slider drag | previous 6 sliders → recompute (re-render) |
| Surgical slider drag | piece's previous `LocalTransform` → re-render |
| Profile switch/add/delete | previous profile list + active + its 6 values |
| Commit | the `IsCommitted` flag alone |

The audit's occlusion double-bake and splint order mismatch both trace to bake-coupled undo/snapshot; with no bake, that class disappears. Undo = restore 6 numbers and a few matrices, re-render — correct by construction.

### 5.4 Save / load — the new file shape

**Save** (`project.json`):

| Key | vs today | Content |
|---|---|---|
| `NhpProfiles` | **NEW primary** | list of `{ Name, Lateral, Anteroposterior, Vertical, Roll, Pitch, Yaw, IsSelected, IsCommitted, IsLatest }` |
| per-piece `LocalTransform` | **NEW** | 16 doubles per segment/mesh/occlusion (broadens today's surgery-plan-stored surgical transform to the per-piece record) |
| segment/mesh/occlusion `Vertices` | **source space now** | identical bytes, different frame |
| `CondyleCenters` (centers+extents+midline), `CephLandmarks` 3D | **source space now** (was baked) | same JSON, different frame |
| `VolumePivot`, `CurrentSurgeryPlan` | unchanged | |

**Dropped:** `NhpBaseline`, `CumulativeNhpMatrix` (superseded by `NhpProfiles`).

**Load (new format):** restore `NhpProfiles` → `RestoreNhpProfilesFromProject` → active profile's 6 values into sliders → `RecomputeAllTransforms` → `UpdateAllSlices`. Vertices/landmarks load directly into source space; the scene is correct immediately because `NhpShared` is rebuilt from the profile. No double-bake possible — there is no bake.

---

## 6. The severable legacy-migration shim (\$5.5, removal-pending)

Load is **version-gated** on the presence of `NhpProfiles`:

- **Has `NhpProfiles` → new format.** Take §5.4; skip the block below.
- **Has `NhpBaseline` + `CumulativeNhpMatrix` but no `NhpProfiles` → legacy bake-model file.** Run the migration block:
  1. `bake = CumulativeNhpMatrix` (16 doubles, NaN-guarded — reuse the existing `ProjectViewModel.cs:355` logic).
  2. Un-bake every piece's vertices: `vertices *= inverse(bake)` → source space.
  3. Un-bake each landmark (`CondyleCenters`, ceph 3D coords) and `VolumePivot`: `point = inverse(bake)·point`.
  4. Build `"NHP 1"` profile from `NhpBaseline`'s 6 values, `IsCommitted = any(non-zero)` — **exactly** the existing `MigrateBaselineToNhpProfileIfNeeded` (prior art `NhpViewModel.cs:734-745`), reused.
  5. Set active profile, load its 6 values into sliders → `NhpShared` rebuilt == `bake` → the scene renders **identically** to how it opened, now in the lazy model.

The block is one guarded `else`, marked:
```csharp
// ponytail: removable shim once new-format .orthoplan test cases exist; verify INV6 still passes on them alone, then delete steps 1–5.
```
Exit criterion: once a corpus of new-format files exist and INV6 passes against them alone, delete steps 1–5 — the gate then only ever finds new files.

---

## 7. Invariants & minimal tests (the "absolutely solid" contract)

| # | Invariant | What breaks it | Minimal check |
|---|---|---|---|
| **INV1** | Every piece's `Transform == Compose(NhpShared, piece.LocalTransform)` after any NHP/surgical/profile change. One source of NHP, one per-piece source of displacement. | A wizard writing `_nhpTransform` to a piece directly; setting `Transform` outside the formula; skipping recompute. | Assert over all pieces after each slider drag, profile switch, commit. |
| **INV2** | Pose never mutates vertices. `piece.Vertices` is byte-identical across NHP drag, commit, profile switch, surgical drag, save/round-trip. Only the four §4 bake points exempt. | Bake leaking back into a pose path; the old ledge-bake; commit touching vertices. | Hash vertex arrays; assert unchanged across each op (exempt creation/registration). |
| **INV3** | Commit moves nothing on screen. With sliders unchanged, commit leaves every `piece.Transform` byte-identical (flag flip). | Commit recalculating `NhpShared` to anything but `MatrixFrom6(same 6)`. | Capture all transforms, commit, recompute, assert equal. |
| **INV4** | Landmarks live in **source space**, unchanged across NHP; effective pivot = `NhpShared ∘ landmark`. | A bake of a landmark on commit/profile switch. | Assert tuples unchanged across NHP ops; assert surgical pivot used = `NhpShared ∘ pivot_source`. |
| **INV5** | Surgical displacement is **NHP-relative**: stored `+Δ` along source-Y renders under yaw ψ as advance along rotated Y. Changing NHP with `LocalTransform` fixed re-projects operated pieces (non-rigid); un-operated reorient rigidly. | Reversing compose order; pre-baking surgical into world. | `LocalTransform = T(0,Δ,0)`, yaw 0 vs 30° — assert world advance direction rotates 30°; assert `LocalTransform` unchanged across profile switch. |
| **INV6** | Legacy round-trip: open bake-model `.orthoplan` → rendered pose equals old; save → new format (`NhpProfiles` present, `CumulativeNhpMatrix` gone); reopen → identical. | Shim un-bake wrong direction; residual `VolumePivot` move. | Migrate, render, compare known-good; save, assert keys; reopen, compare. |
| **INV7** | Translation visible: nonzero NHP translation (identity rotation) moves meshes/volume-render by it, while `ModelCenter` stays at constant `VolumePivot`. | `ModelCenter = Nhp·VolumePivot` creeping back; volume render missing NHP binding. | Translation 50mm — assert piece centroid shifts 50mm world; `ModelCenter` unchanged; volume render moved. |
| **INV8** | Occlusion renders at one NHP application, not cumulative² (audit HIGH). | Load bakes occlusions; restore path mutating vertices. | Reopen a committed-then-saved occlusion project — assert pose == single-NHP. |
| **INV9** | Splint seats under any NHP, committed or not (audit MED-HIGH): splint `LocalTransform == parent jaw.LocalTransform`; splint + jaw share effective pose. | Splint generated with `_nhpTransform` applied as its own transform. | Generate splint at dirty NHP — assert `LocalTransform` equal and poses coincide. |

INV1+INV2+INV3 are the spine: NHP applied exactly once, from one source, commit a no-op on geometry — a wizard re-run recomposes the same formula and changes nothing it didn't author. INV6 is the shim's exit criterion.

---

## 8. The three display subsystems + the translation fix + centering

```
                 ┌─ 3D meshes (seg/cast/occ)  ─► Compose(NhpShared, Local)  reorient + translate
 NhpShared ─────┼─ CT volume render (slab)    ─► NhpShared                  reorient + translate  ★ NEW binding
                 └─ MPR 2D slices             ─► oblique resample by NhpShared⁻¹  rotation only; no translate; no reslice
```

- **3D meshes** — `piece.Transform = Compose(NhpShared, LocalTransform)`; reorient + translate. (Existing for meshes; segments lose the old delta.)
- **CT volume render (3D slab, "Bone Diffused View")** — **today has no NHP binding** (`MainWindow.xaml:1065` binds only `VolumeNode` + `IsRendering`). **Add `Transform="{Binding NhpSharedTransform}"`** so the slab follows all of NHP, including translation. ★ the fix point.
- **MPR 2D slices** — oblique resample for rotation by `NhpShared⁻¹`; **no translation, no volume reslice** (translations add nothing to the slice view). Existing `GetInverseNhpTransform` keeps its `cumulative∘delta` → here `NhpShared⁻¹`; AABB sizing stays.

**Translation fix (requirement f + g):** the "translations don't work" root cause is double — (a) the volume render had no binding, (b) `ModelCenter = Nhp·VolumePivot` (NhpViewModel:274) made the camera pivot follow the translation and cancel it visually. Fix: bind the volume render (above) **and** feed `ModelCenter` from the constant `VolumePivot` (source space) instead of `NhpShared·VolumePivot`. The camera pivot is thereby **decoupled from NHP** — NHP reorients/translate the scene around a fixed pivot, so rotation already worked visually (pivots don't move under rotation) and translation now shows (pivot no longer cancels it).

**Centering preserved (requirement g):** `BoneOnlyBounds` = full DICOM volume (camera-frame lock), `CenterCamera`/NavCube/headlamp/`FixedRotationPoint`, `RotateAroundMouseDownPoint="False"` around `FixedRotationPoint` — all stay. Only the `ModelCenter` *input* changes (from `NhpShared·VolumePivot` to constant `VolumePivot`). The existing centering code is reviewed-but-kept-working.

---

## 9. Build order (minimal; writing-plans fills detail)

1. Add `LocalTransform` to Mesh/Occlusion; `SegmentViewModel.SurgicalTransform` aliases as `LocalTransform`; add `RecomputeAllTransforms` + `NhpShared`; wire `piece.Transform` binding. Verify INV1 on the unchanged vertex path.
2. Bind CT volume render `Transform` to NhpShared; flip `ModelCenter` to constant `VolumePivot`. Verify INV7.
3. Port the profile layer from `experimental_Lore` (DTO, `NhpProfiles`, add/select/delete/commit, camera capture) over the lazy engine — commit becomes the §5.2 no-op. Verify INV3.
4. New save/load (`NhpProfiles`, per-piece `LocalTransform`, source-space vertices/landmarks). Verify INV2, INV4, INV8 on reopen.
5. Bake points: B1 split-source return, B3 splint-tracks-jaw `LocalTransform`. Verify INV9; P1 + splint-order bug closed.
6. Legacy migration shim (§6), gated. Verify INV6.
7. B2 cast-registration bake; B4 scratch localization.

Each step builds clean before the next (per `CODEBASE_CONTEXT`); each is verifiable independent of the rest.

---

## 10. Prior-art alignment (`origin/experimental_Lore`, commit cdc4def)

Taken verbatim: `NhpProfileViewModel` (name + `IsSelected`/`IsCommitted`/`IsLatest` + 6 absolute doubles), `NhpProfiles`, `InitNhpProfiles`/`RefreshNhpProfileFlags`/`EnsureDefaultNhpProfile`, `AddNhpProfile` (zeroes a fresh profile), `SelectNhpProfile` (save old + load new), `DeleteNhpProfile`, `CommitNhp` UI flow, `ApplyCameraAnglesToNhp`/`SetNhpRotationsFromCamera`, `SuppressCameraNhpSync`, `RestoreNhpProfilesFromProject`, `MigrateBaselineToNhpProfileIfNeeded`, `NhpCameraAngles` helper.

**The one substantive change under that layer:** that branch's `CommitNhp` still says "bake the current absolute target pose into all mesh vertices and landmarks" (`NhpViewModel.cs:124`) and runs the full vertex/landmark/`VolumePivot` bake + `_cumulativeNhpMatrix` fold. The lazy commit (§5.2) replaces the bake with a flag flip; `_cumulativeNhpMatrix` and `_nhpTransform` are deleted; the `BuildAbsoluteNhpPreviewMatrix` (target ∘ inverse(cumulative)) collapses to `MatrixFrom6(six)` since there's no cumulative to invert. Slider values stay absolute-from-source (that branch already moved sliders to this convention — `NhpViewModel.cs:126` comment) — so the UX of "commit keeps the sliders showing the active pose" is already the prior-art behavior; we only stop the bake beneath it.

---

## 11. Deliverables

1. This spec → implementation plan (writing-plans) → refactor on `experimental`.
2. Post-implementation: a standalone **onboarding walkthrough** ("how 3D viewports represent this; the transform stack; how NHP, surgical, and the three display subsystems interact") with simple diagrams — the foundation is §1, §3, §8 above; the polished doc comes after the code lands.
