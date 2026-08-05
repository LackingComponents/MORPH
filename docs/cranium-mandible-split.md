# Cranium / Mandible Split — Function Report

> Scope: how the cranium/mandible separation works today, its place in the workflow, and a triaged
> list of problems with concrete failure scenarios. Read alongside `docs/CODEBASE_CONTEXT.md` §7, §8, §11.

---

## 1. Where it sits in the workflow

The split is the **first osteotomy step** and the only one that requires the voxel CT mask (every
other wizard — LeFort, BSSO, Genioplasty — operates purely on triangle meshes). It produces two
things the rest of the app depends on:

1. **Two new segments**: `"Cranium (Split)"` and `"Mandible (Split)"`, added to `MainViewModel.Segments`
   with the original whole-bone `HardTissueModel` hidden.
2. **Anatomical landmarks** stored on `MainViewModel` and consumed everywhere downstream:
   - `LeftCondyleCenter` / `RightCondyleCenter` — rotation pivots for the **Ramus** segments
     ([SurgeryViewModel.cs:423-434](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L423-L434)).
   - `DentalMidlinePoint` — pivot for **Maxilla/Mandible** complex movements
     ([SurgeryViewModel.cs:355](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L355)).
   - `LeftCondyleHalfExtents` / `RightCondyleHalfExtents` — the condyle bounding-box sizes, consumed
     by the **splint engine** as `CondyleBox` for mandible autorotation geometry
     ([SplintViewModel.cs:100-105](../src/OrthoPlanner.App/ViewModels/SplintViewModel.cs#L100-L105)).

These landmarks are persisted in `project.json` → `CondyleCenters`
([ProjectViewModel.cs:82-89](../src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs#L82-L89)),
snapshot/restored by undo ([UndoRedoViewModel.cs:42-46](../src/OrthoPlanner.App/ViewModels/UndoRedoViewModel.cs#L42-L46)),
and **re-baked through the NHP delta on every commit**
([NhpViewModel.cs:133-135](../src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L133-L135)).

### Two entry points, same window

`CondyleSplitWindow` is opened in two modes by a `landmarkOnlyMode` flag:

| Caller | Mode | Behaviour |
|---|---|---|
| `OsteotomyViewModel.SplitCraniumMandibleAsync` ([L400](../src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs#L400)) | `false` (full split) | 5-point pick → plane + condyle boxes → voxel split → 2 meshes + landmarks |
| `SurgeryViewModel.EnsureCondyleFulcrum` ([L245](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L245)) | `true` (landmarks only) | 2-point pick (just condyles) → boxes → landmarks only, **no mesh output** |

The landmarks-only mode is a fallback so the surgeon can define (or correct) the ramus-rotation
pivot even without re-running the full voxel split — it's triggered lazily the first time a Ramus
rotation slider is touched and the fulcrum is missing
([SurgeryViewModel.cs:209-219](../src/OrthoPlanner.App/ViewModels/SurgeryViewModel.cs#L209-L219)).

There is also a **second split algorithm**: `SeedSplitWindow`, launched from the wizard's
"Seed Split…" button. It is a seed-based competitive region grow in MPR slices (no cut plane), kept
as a rescue path when the geometric split can't separate a fused TMJ.

---

## 2. The full-split algorithm, step by step

### Step 1 — `OsteotomyViewModel.SplitCraniumMandibleAsync` ([L328-L480](../src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs#L328))

1. **Validate** a bone model exists (`HardTissueModel.Vertices.Length >= 100`).
2. **Build a pristine bone-only mask on demand** (`GetValidSplitTargetVolume`).
   A ponytail change removed a 100 MB always-resident `_boneOnlySegVolume` in favour of a single
   linear pass over `_segVolume` copying only voxels equal to `_boneLabel`. The working-tree diff
   also **removed a fast path** that returned `_segVolume` directly, because a prior
   seed-split-preview session can have clobbered `_segVolume` with labels 1/2/3 — so the mask is now
   *always* rebuilt (~50 ms on 512³).
3. If the mask is stale (e.g. after an NHP reslice changed volume dimensions), it **auto re-runs the
   bone segmentation** (`RunSegmentInternalAsync`) and retries once.
4. Opens `CondyleSplitWindow` with: bone vertices (for picking/display), `Volume` + the bone mask +
   `boneLabel` + `BoneMinHU` (for the voxel split), and the **inverse cumulative-NHP matrix** so the
   split can map pick-space back to DICOM space.
5. On accept: saves undo, copies the two meshes into new segments, copies the landmarks onto
   `MainViewModel`, hides the original bone, refreshes.

### Step 2 — `CondyleSplitWindow` interaction ([CondyleSplitWindow.xaml.cs](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs))

- **Pick 5 points** (sequence: Right Condyle, Right Posterior, Interincisal, Left Posterior, Left
  Condyle). Camera auto-snaps to a sensible view after each placement. Right-click deletes a point.
- **Plane** is built from only **points 1, 2, 3** (the two posterior teeth + interincisal) — i.e. the
  *occlusal plane*. The two condyle points (0, 4) seed the boxes, they are **not** on the plane
  ([ComputePlane L277-297](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L277-L297)).
- Each condyle box starts at the clicked point then gets a fixed **±10 mm medial shift** toward the
  midline and default half-extents `{15, 10, 10}` mm. In Step 2 the surgeon can drag-move (face) /
  drag-resize (lateral corner sphere) / right-click-reset each box.

### Step 3 — `SplitVoxelMask` ([L582-770](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L582-L770))

The actual separation runs on a **clone** of the bone mask (not the shared `_segVolume`). Using
throwaway magic labels:

1. **Pristine-ize**: for every bone-label voxel, if its raw HU `< max(100, BoneMinHU)` drop it
   (removes morphological-closing bridges that aren't real bone). Then tag every remaining bone
   voxel: **below plane → `mandBodyLabel` (202)**, **above plane → `unassignedAboveLabel` (203)**.
2. **Seed** the condyle flood-fill at each condyle *anchor* (the click point, snapped to the nearest
   203/202 voxel within a 17³ search radius). Seeds become `mandLabel (201)`.
3. **Flood grow** 6-connected from the seeds, claiming only `unassignedAboveLabel` (above-plane)
   voxels. Two gates:
   - **Bridge rejection**: a candidate voxel is only claimed if it has **≥10 non-zero neighbours in a
     3×3×3** kernel. This is meant to stop the grow bleeding through thin soft-tissue bridges.
   - **Hard Separation (optional)**: if enabled, the grow is additionally confined to an **ellipsoid
     inscribed in whichever condyle box** the current voxel is nearer to (rescue mode for fused TMJs).
4. **Finalize**: merge grown condyles (`201`) + body-below-plane (`202`) → `finalMandibleLabel (205)`,
   call `KeepLargestComponent` on 205, then everything else still labelled bone → `cranLabel (200)`.
5. **Extract** both meshes with full-res marching cubes (`ExtractSegmentMesh`, step 1) and return.

### Coordinate-space handling (the subtle part)

- The bone *vertices* passed in and picked on live in **baked/NHP space** (wherever the rest of the
  scene currently sits).
- The voxel split must operate in **raw DICOM space** (`x*spacing`). So the wizard transforms the two
  condyle centers, the two anchors, and recomputes the plane normal+D through `inverseNhpMatrix`
  ([L497-515](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L497-L515)).
- The returned meshes are in **DICOM space**; the wizard applies `_nhpDisplayTransform`
  (DICOM→baked = the *forward* cumulative NHP) only as a display `Transform` so the preview lines up
  with the baked input bone.
- The returned **landmarks** (`LeftCondyleCenter` etc.) are in **baked space** (built from `leftC`,
  the pre-transform values), which is correct — surgical pivots must match the baked segment vertices.

---

## 3. Problems found (most → least severe)

### P1 — Coordinate-space mismatch on the returned meshes after an NHP commit

`CraniumResult`/`MandibleResult` are DICOM-space vertices. `OsteotomyViewModel` assigns them
**directly** to the new segments' `Vertices` ([L429/L445](../src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs#L429))
with no NHP bake. When NHP is identity (the common path: split runs *before* any NHP commit) this is
fine — DICOM == baked. But if the surgeon commits NHP **first** and then splits, every *other*
segment's vertices are baked into NHP space while `"Cranium (Split)"`/`"Mandible (Split)"` sit in
raw DICOM space. They will render offset/rotated relative to the rest of the skull, and any
subsequent surgical planning against them is wrong.

The wizard already has `_nhpDisplayTransform` — the fix is to **bake that forward transform into the
returned vertices** instead of applying it as a view-only `Transform`. One matrix multiply over the
vertex array in `PerformSplit` before exposing `CraniumResult`/`MandibleResult`. Note the landmarks
are already in baked space, so only the meshes need this.

*(Caveat: if the intended usage order is strictly "split → then NHP", document/enforce that instead.
But the data model allows NHP-before-split with no guard, so this is a live trap.)*

### P2 — `KeepLargestComponent` can drop a legitimate condyle fragment

The mandible finalization merges the *grown condyles* (above plane, from the seed flood) with the
*body below the plane* (pre-tagged `202`) **regardless of connectivity**, then keeps only the single
largest connected component of `205`
([L750](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L750)).

Failure scenario: if the condyle flood-fill fails to break through a fused/low-contrast TMJ, the
above-plane condyle portion stays small/disconnected from the large body. `KeepLargestComponent`
keeps the body and **silently deletes the condyle fragment**, leaving the mandible without its
condylar head — and the surgeon sees a "successful" split with no warning. Inverse failure (plane too
low, body swallowed into cranium) symmetrically loses mandible.

Mitigation ideas: report the kept/discarded component sizes to the surgeon in the Step-3 status line;
or keep the union of components connected to either seed rather than the global largest; or only
apply largest-component pruning to the *body* portion, not the merged set.

### P3 — "Bridge rejection" counts all non-zero labels, so it doesn't actually constrain growth

The ≥10-bone-neighbours gate
([L715-732](../src/OrthoPlanner.App/CondyleSplitWindow.xaml.cs#L715-L732)) counts *every* non-zero
label in the 3×3×3 kernel — including the `mandLabel`/`mandBodyLabel` voxels the grow itself just
wrote, and any `cranLabel`/cranium bone adjacent. In dense skull base this is essentially always ≥10,
so the "soft separation" path has **no geometric containment above the plane**. The grow can escape
upward into the cranial base / zygomatic arch if the condyle anchor is slightly off, carving a chunk
out of the cranium with no ellipsoid to stop it. Hard Separation mode is the only real containment,
and it's оп-in ("rescue for fused joints").

### P4 — Condyle half-extents are not NHP-aware

`CommitNhp` bakes `Left/RightCondyleCenter` and `DentalMidlinePoint` through the delta matrix
([NhpViewModel.cs:133-135](../src/OrthoPlanner.App/ViewModels/NhpViewModel.cs#L133-L135)) but **not**
the half-extents. The half-extents are axis-aligned box half-sizes in mm; under a rotated NHP frame
the box they describe in DICOM space is no longer aligned to the condyle's true axes. Effect is small
for typical small pitch/roll but means the splint/autorotation `CondyleBox` is slightly mis-shaped
after NHP commit. Either bake the extents by transforming the 8 box corners and recomputing the
half-extents along the rotated local axes, or rebuild the box from the (already-baked) center on
demand.

### P5 — Re-segment fallback produces a mask that may not match the displayed bone

When the bone mask is stale, `SplitCraniumMandibleAsync` silently re-runs `RunSegmentInternalAsync`
with `EnhanceSegmentation` and overwrites. The user is shown the *original* baked bone model but the
split operates on a *freshly regenerated* mask — which can differ in thin-bone recovery, closing
iterations, and component keeping. The split result may then not match the silhouette the surgeon
planned against. At minimum surface "bone mask regenerated for current NHP volume" already exists as
status text; consider forcing an explicit user confirmation or regenerating the *visible* model too.

### P6 — Architectural / maintainability

- **1247-line code-behind** for the wizard (mixes DX rendering, mouse hit-testing, plane math, voxel
  flood-fill, and marching-cubes orchestration). Listed as pending refactor #3 in
  `CODEBASE_CONTEXT.md` §18. The voxel split in particular (`SplitVoxelMask`) belongs in
  `OrthoPlanner.Core` next to `SegmentationEngine`, parameterized and unit-testable.
- **Magic labels 200–205** are allocated inside the throwaway clone so they don't collide with the
  live `Segments` collection — but they're undocumented magic numbers scattered across the method.
  A small `const` block with a comment would make the state machine legible.
- `SeedSplitBtn` is visible in **both** modes including `landmarkOnlyMode`, where `_ctVolume` is null
  so clicking it only ever shows a warning. It should collapse in landmark-only mode (the XAML has no
  visibility binding to `_landmarkOnlyMode`).
- `IsInBox` / `Clone` helpers — `Clone(float[])` appears unreferenced (dead). Minor.

### P7 — UX fragility (lower priority)

- **Plane ignores condyle height**: only posterior/interincisal points define it; condyle clicks are
  used solely for box seeding. A surgeon who lowers the condyle points to "hug" the condylar head
  expects the plane to track; it doesn't. Worth a one-line hint in Step-1 instructions.
- **No undo inside the wizard**: dragging a box badly or mis-clicking points can only be fixed by
  right-click or Cancel-to-Step-1. Single-click point placement without confirmation is quick but
  error-prone on a dense 3D mesh hit-test (the 5 px snap radius is tight).
- **Hard Separation is discoverable only by tooltip.** Since P3 shows the soft path is unreliable on
  fused joints, the rescue mode should arguably be more prominent.

---

## 4. What I'd touch first

If the goal is "improve the split", the highest-leverage, lowest-risk changes:

1. **P1** (space mismatch) — bake `_nhpDisplayTransform` into the returned vertices. Small diff,
   removes a silent wrong-result trap.
2. **P2** (component pruning) — stop globally `KeepLargestComponent`-ing the merged mandible; at
   least log kept/dropped voxel counts. Directly addresses "silent loss of condyle".
3. **P3** (uncontained grow) — gate the bridge test on *original bone* neighbours only (exclude the
   grow's own written labels), and/or default to a soft geometric cap when Hard Separation is off.

Discuss before doing: **P4** (extent baking) has cross-module consequences for the splint engine and
needs the box's local-axis convention pinned down first.
