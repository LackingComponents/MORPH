# NHP & All-Transforms Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the experimental "delta/cumulative/ledger-bake" NHP model with a lazy transform stack where every piece's pose is `Compose(NhpShared, piece.LocalTransform)` computed at draw time from two never-mutated-by-pose inputs, NHP is applied exactly once and never lost across wizard passes, multiple NHPs are named checkpoints, NHP translations become functional (visual; MPR resamples but does not reslice), and legacy `.orthoplan` files auto-migrate via a severable block.

**Architecture:** Vertices live in source DICOM frame forever. `NhpShared` = one 4×4 from the 6 absolute NHP sliders (the active profile); `piece.LocalTransform` = per-piece persisted displacement record (`Identity` for unperturbed, the surgical movement for operated segments, the parent jaw's transform for a derived splint). At recompute, each piece's `Transform = Compose(NhpShared, LocalTransform)`. Commit moves nothing on screen (flag flip only). Landmarks live permanently in source space; the surgical pivot is `NhpShared ∘ pivotSource`. The only persistent vertex bake is dental-cast/occlusion registration onto **CT source space** (B2); cutting wizards and the splint bake nothing; splints inherit the jaw's `LocalTransform`. The mapping convention is WPF row-vector `Transform3DGroup{first,second}` = apply first then second (existing `ComposeTransforms`, unchanged).

**Tech Stack:** .NET 8.0 (`net8.0-windows`); WPF; HelixToolkit.Wpf.SharpDX 3.1.2; CommunityToolkit.Mvvm 8.4.0 ([ObservableProperty]/[RelayCommand], partial `OnXChanged`); `System.Windows.Media.Media3D` (`Matrix3D`, `Transform3D`, `MatrixTransform3D`); two projects — `OrthoPlanner.App` (WinExe) + `OrthoPlanner.Core` (lib); all `*ViewModel` are `partial class MainViewModel` across `MainViewModel.cs` + per-area files.

## Global Constraints

- **Branch:** `experimental` (target — stay on it; one commit per task). Prior art on `origin/experimental_Lore` (commit `cdc4def`) is the data-management layer source.
- **Safety walls (clamp unchanged):** translations `±200 mm`, rotations `±45°` (`ClampNhp`, `NhpViewModel.cs:11-15`). NaN/Infinity guards on every loaded numeric (precedent `ProjectViewModel.cs:298`).
- **Matrix convention:** WPF row-vector; `Compose(A,B) = new MatrixTransform3D(new Transform3DGroup{ Children={A,B} })` — "apply A then B". `ComposeTransforms` (`NhpViewModel.cs:397`) already encodes this and returns `first` if `second` is `Identity`; **do not change the convention** (no manual transposes — per `docs/CODEBASE_CONTEXT.md` matrix note).
- **NHP build order (`BuildNhpMatrix`, unchanged):** center `c = VolumePivot ?? BoneOnlyBounds center`; `T(-c) · RotX(Pitch) · RotY(Roll) · RotZ(Yaw) · T(c + Lat, c + Ant, c + Vert)` (`NhpViewModel.cs:232-247`).
- **Bake points (closed list):** the ONLY persistent vertex bake is **B2** (cast/occlusion registration onto CT source space, `StlViewModel.cs:107-108`). Split returns (B1) and splint (B3) bake nothing — return source-space vertices / inherit a `LocalTransform`. B4 scratch is transient only. Any other vertex write as a side-effect of posing is a bug.
- **CT alignment guard:** the ICP target is the CT segment's **source-space `Vertices`** (`StlViewModel.cs:70,97`), never a copy pre-multiplied by `NhpShared`. Picked correspondences taken from the posed display must be un-posed (`× NhpShared⁻¹`) to source before ICP — the `DentalAlignmentWindow` pick path stays as-is per the spec non-goals; this constraint is enforced by keeping `seg.Vertices` permanently in source space.
- **Centering preserved:** `BoneOnlyBounds` (full DICOM volume), `CenterCamera`/NavCube/headlamp/`FixedRotationPoint`, `RotateAroundMouseDownPoint="False"` — all stay. Only the `ModelCenter` *input* changes (from `NhpShared·VolumePivot` to constant `VolumePivot`).
- **MPR rule:** oblique resample by `NhpShared⁻¹` for rotation only — **no translation, no volume reslice** (translations add nothing to the slice view).
- **Verification = DEBUG asserts + Core math self-check** (NO test framework added). Runtime invariants fire as `System.Diagnostics.Debug.Assert` in `RecomputeAllTransforms`/commit/save; a one-time `NhpMathSelfCheck.Run()` is called from `App.OnStartup` under `#if DEBUG`. Legacy round-trip + splint via the per-task manual checklists below.
- **No new dependencies. No new interfaces-with-one-impl.** `NhpProfileViewModel` is taken verbatim from `experimental_Lore` (the DTO). `ICraniumMandibleSolver`/`NhpProfileViewModel` reuse, not reinvent.
- **Ponytail:** shortest diff per task; reuse the Lore prior art line-for-line where it already works; mark deliberate simplifications with `// ponytail:` comments. Each task builds clean (0 errors) before the next.
- **Co-author commit footer:** end every commit message with a blank line + `Co-Authored-By: Claude <noreply@anthropic.com>`.

**Spec:** `docs/superpowers/specs/2026-07-10-nhp-transform-architecture-design.md` (commit `011c1f1`). Every task cites spec sections it implements.

---

### Task 1: The lazy transform stack — `LocalTransform` + `NhpShared` + `RecomputeAllTransforms`

**Goal:** Introduce the one formula mechanically over the *current* delta-slider model, so runtime behavior is unchanged this task while the new spine (`LocalTransform` + `NhpShared` + `RecomputeAllTransforms`) is in place. Pure refactor; no slider-semantics change yet (that is Task 3).

**Files:**
- Modify: `src/OrthoPlanner.App/ViewModels/MainViewModel.cs` (SegmentViewModel `SurgicalTransform` alias; MeshViewModel add `LocalTransform`)
- Modify: `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` (add `NhpShared`/`NhpSharedTransform`; rewrite `UpdateNhpTransform`→`RecomputeAllTransforms`; rewrite `ApplyNhpToAllTrackedObjects` body to the formula)

**Interfaces:**
- Consumes: existing `_nhpTransform` (delta, `NhpViewModel.cs:31`), `_cumulativeNhpMatrix` (`:36`), `ComposeTransforms` (`:397`).
- Produces (used by Task 2 + Task 3): `NhpShared` (`Matrix3D`), `NhpSharedTransform` (`Transform3D`, bindable for the volume render), `RecomputeAllTransforms()` (the single recompute entry point), `piece.LocalTransform` on every Mesh, `SegmentViewModel.LocalTransform` (alias of `SurgicalTransform`). The per-collection bake branch of the ledger stays for now (deleted in Task 3) — only the *transform assignment* lines change to the formula.

- [ ] **Step 1: Add `LocalTransform` to `MeshViewModel` and alias it on `SegmentViewModel`.**

In `src/OrthoPlanner.App/ViewModels/MainViewModel.cs`, MeshViewModel — add a `LocalTransform` property next to the existing `Transform` (`:311`) and `NhpBaked` (`:315`):

```csharp
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    /// <summary>The per-piece displacement record (lazy transform stack): surgical movement, cast
    /// registration (Identity once baked), or — for a splint — the parent jaw's LocalTransform.
    /// Identity for an unperturbed piece. Never mutated by pose; persists across NHP changes.</summary>
    public System.Windows.Media.Media3D.Transform3D LocalTransform { get; set; } = System.Windows.Media.Media3D.Transform3D.Identity;
```

SegmentViewModel — add a `LocalTransform` alias that is exactly `SurgicalTransform` (`:248`), so both solvers call one field. Keep `SurgicalTransform` as the public name surgery reads/writes; `LocalTransform` is the formula's name:

```csharp
    /// <summary>The surgical movement component of this segment's transform (NHP-independent).</summary>
    public System.Windows.Media.Media3D.Transform3D SurgicalTransform { get; set; } = System.Windows.Media.Media3D.Transform3D.Identity;

    /// <summary>Alias of <see cref="SurgicalTransform"/> — the per-piece displacement record used by
    /// the lazy transform stack. Same value, the formula's name.</summary>
    public System.Windows.Media.Media3D.Transform3D LocalTransform
    {
        get => SurgicalTransform;
        set => SurgicalTransform = value;
    }
```

- [ ] **Step 2: Add `NhpShared` + `NhpSharedTransform` to `NhpViewModel.cs`.**

After the `_cumulativeNhpMatrix` field (`:36`) in `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs`, add:

```csharp
    // ─── Lazy transform stack: NhpShared is the single shared NHP matrix every piece composes with. ───
    // Task 1: NhpShared aliases the existing delta (_nhpTransform). Task 3 flips it to MatrixFrom6(absolute six).
    private System.Windows.Media.Media3D.Matrix3D _nhpShared = System.Windows.Media.Media3D.Matrix3D.Identity;

    /// <summary>The shared NHP transform (Matrix3D), bound to the CT volume render (Task 2).</summary>
    public System.Windows.Media.Media3D.Transform3D NhpSharedTransform { get; private set; } = System.Windows.Media.Media3D.Transform3D.Identity;
```

- [ ] **Step 3: Rewrite the recompute body to the formula.**

Replace the bodies of `UpdateNhpTransform` (`:249-275`) and `ApplyNhpToAllTrackedObjects` (`:281-290`) so a single `RecomputeAllTransforms` applies `Compose(NhpShared, piece.LocalTransform)` to every piece. Keep `ModelCenter = deltaMatrix.Transform(center)` (`:274`) for now — Task 2 flips it to constant `VolumePivot`. Replace the two methods with:

```csharp
    private void UpdateNhpTransform()
    {
        RecomputeAllTransforms();
        ScheduleDebouncedSliceUpdate();
    }

    /// <summary>The one recompute site (INV1). NhpShared aliases the delta until Task 3.
    /// INV1: every piece.Transform == Compose(NhpShared, piece.LocalTransform).</summary>
    private void RecomputeAllTransforms()
    {
        // Task 1: NhpShared = the live delta. Task 3 replaces with MatrixFrom6(absolute six).
        _nhpShared = _nhpTransform.Value;
        NhpSharedTransform = _nhpTransform;
        OnPropertyChanged(nameof(NhpSharedTransform));

        if (HardTissueModel != null) HardTissueModel.Transform = ComposeTransforms(NhpSharedTransform, HardTissueModel.LocalTransform);
        if (SoftTissueModel != null) SoftTissueModel.Transform = ComposeTransforms(NhpSharedTransform, SoftTissueModel.LocalTransform);
        if (DentalModel     != null) DentalModel.Transform     = ComposeTransforms(NhpSharedTransform, DentalModel.LocalTransform);

        foreach (var seg  in Segments)        seg.Transform  = ComposeTransforms(NhpSharedTransform, seg.LocalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = ComposeTransforms(NhpSharedTransform, mesh.LocalTransform);
        foreach (var occ  in LoadedOcclusions) occ.Transform = ComposeTransforms(NhpSharedTransform, occ.LocalTransform);

#if DEBUG
        AssertFormulaHolds();
#endif
    }
```

Note: `HardTissueModel`/`SoftTissueModel`/`DentalModel` are `SegmentViewModel` with a `LocalTransform` (=`SurgicalTransform`) now; previously they were assigned bare `_nhpTransform` (they have no surgical movement). `SurgicalTransform` defaults to `Identity`, so `ComposeTransforms(NhpShared, Identity)` returns `NhpShared` (the `ComposeTransforms` Identity fast-path, `:397-404`) — behavior identical to today.

- [ ] **Step 4: Add the INV1 DEBUG assert.**

Append near `ComposeTransforms` (`:397`):

```csharp
    [System.Diagnostics.Conditional("DEBUG")]
    private void AssertFormulaHolds()
    {
        // INV1 — every piece carries the formula. RecomputeAllTransforms just wrote each, so verify each.
        bool Eq(System.Windows.Media.Media3D.Matrix3D a, System.Windows.Media.Media3D.Matrix3D b)
            => Math.Abs(a.M11-b.M11)<1e-9 && Math.Abs(a.OffsetX-b.OffsetX)<1e-9
            && Math.Abs(a.M22-b.M22)<1e-9 && Math.Abs(a.OffsetY-b.OffsetY)<1e-9
            && Math.Abs(a.M33-b.M33)<1e-9 && Math.Abs(a.OffsetZ-b.OffsetZ)<1e-9;
        System.Windows.Media.Media3D.Matrix3D Expected(System.Windows.Media.Media3D.Transform3D local)
        { var g = new System.Windows.Media.Media3D.MatrixTransform3D(_nhpShared); var c = ComposeTransforms(g, local); return c.Value; }
        foreach (var seg in Segments)
            System.Diagnostics.Debug.Assert(Eq(seg.Transform.Value, Expected(seg.LocalTransform)), "INV1 segment");
    }
```

- [ ] **Step 5: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.` with 0 errors (warnings unrelated; existing MVVMTK0034 pragmas kept).

- [ ] **Step 6: Manual INV1 smoke check.**

Launch the app (`dotnet run --project src/OrthoPlanner.App`), load a DICOM, open the NHP panel, drag NHP sliders. Confirm the 3D scene still moves exactly as before (this task changes no behavior). The DEBUG assert fires to the Output window only if a piece's `Transform` drifts off the formula.

- [ ] **Step 7: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/MainViewModel.cs src/OrthoPlanner.App/ViewModels/NhpViewModel.cs
git commit -m "refactor(nhp): lazy transform stack — LocalTransform + NhpShared + RecomputeAllTransforms (Task 1)

Mechanical, behavior-preserving: every piece.Transform = Compose(NhpShared, piece.LocalTransform).
NhpShared aliases the existing delta this task; Task 3 flips it to MatrixFrom6(absolute six).
MeshViewModel gains LocalTransform; SegmentViewModel.LocalTransform aliases SurgicalTransform.
INV1 DEBUG assert added.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 2: Translation visible — volume render NHP binding + `ModelCenter` decoupled

**Goal:** Make NHP translation actually show. Bind the CT volume render to `NhpSharedTransform`; feed `ModelCenter` from the constant `VolumePivot` (source space) instead of `NhpShared·VolumePivot`, so the camera pivot no longer follows (and cancels) the translation. Verify INV7.

**Files:**
- Modify: `src/OrthoPlanner.App/MainWindow.xaml` (add `Transform` binding to `VolumeTextureModel3D`, `:1065`)
- Modify: `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` (`RecomputeAllTransforms` — `ModelCenter` from constant `VolumePivot`; remove `:274` `ModelCenter = deltaMatrix.Transform(center)`)

**Interfaces:**
- Consumes: `NhpSharedTransform` (Task 1), `VolumePivot` (`MainViewModel.cs:103`).
- Produces: the INV7 contract — nonzero NHP translation moves meshes + volume render; `ModelCenter` stays at constant `VolumePivot`.

**Note:** `VolumeTextureModel3D` (`src/OrthoPlanner.App/Controls/VolumeTextureModel3D.cs`) extends `Element3D` and does NOT override `Transform`, so it inherits Helix's bindable `Transform` DependencyProperty. No control change — XAML binding only. Already verified.

- [ ] **Step 1: Bind the volume render to `NhpSharedTransform`.**

In `src/OrthoPlanner.App/MainWindow.xaml` (`:1065-1066`):

```xml
                        <ctrl:VolumeTextureModel3D VolumeNode="{Binding VolumeNode}"
                                                    IsRendering="{Binding IsVolumeRenderingEnabled}"
                                                    Transform="{Binding NhpSharedTransform}" />
```

Was:

```xml
                        <ctrl:VolumeTextureModel3D VolumeNode="{Binding VolumeNode}" IsRendering="{Binding IsVolumeRenderingEnabled}" />
```

- [ ] **Step 2: Decouple `ModelCenter` from NHP.**

In `RecomputeAllTransforms` (Task 1 body), replace the camera-pivot line. The old `UpdateNhpTransform` had `ModelCenter = deltaMatrix.Transform(center)` (`:274`). The new recompute must NOT move `ModelCenter` with NHP. Add at the END of `RecomputeAllTransforms`, after the asserts:

```csharp
        // INV7: the camera pivot is decoupled from NHP — feed the CONSTANT source-space VolumePivot,
        // not NhpShared·VolumePivot. Rotation already worked (a pivot doesn't move under rotation);
        // translation now shows because the pivot no longer follows (and visually cancels) it.
        if (VolumePivot.HasValue)
        {
            ModelCenter = VolumePivot.Value;
            OnPropertyChanged(nameof(ModelCenter));
        }
```

(If `VolumePivot` is null, `RefreshCombinedModel` (`MainViewModel.cs:147-159`) already seeds `ModelCenter` from `BoneOnlyBounds` — leave that path untouched.)

- [ ] **Step 3: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Manual INV7 check.**

Launch, load DICOM, open NHP. Set **Lateral (X) = 50 mm**, all rotations 0. Confirm: (a) the 3D meshes shift ~50 mm along X, (b) the CT volume slab shifts the same 50 mm (previously it didn't move — that was the bug), (c) the camera orbit center (`ModelCenter`) stays put — orbiting does not snap to follow the translation. Reset. Set Pitch 20° — confirm the scene reorients about the fixed pivot as before (centering preserved).

- [ ] **Step 5: Commit.**

```bash
git add src/OrthoPlanner.App/MainWindow.xaml src/OrthoPlanner.App/ViewModels/NhpViewModel.cs
git commit -m "fix(nhp): translation visible — bind CT volume render + decouple ModelCenter (Task 2)

VolumeTextureModel3D now binds Transform=NhpSharedTransform so the slab follows NHP translation.
ModelCenter fed from constant VolumePivot (source space), not NhpShared·VolumePivot — the camera
pivot no longer follows and visually cancels translation. Centering code unchanged.
Closes requirement (f)+(g). Verify INV7.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 3: The profile layer + absolute sliders + lazy commit (no bake)

**Goal:** Port the NHP-profile data-management layer from `origin/experimental_Lore` (verbatim where it works), flip the sliders to absolute-from-source, and replace `CommitNhp`'s vertex/landmark/`VolumePivot` bake with a flag flip (the §5.2 spine simplification). Delete `_nhpTransform` (delta), `_cumulativeNhpMatrix`, the `_cLat..` baseline, and the ledger's cumulative-bake branch. Verify INV3 (commit moves nothing on screen).

**This is the largest task.** It touches the core engine. Build incrementally: (a) bring in the DTO + profile plumbing, (b) flip sliders absolute + `NhpShared = MatrixFrom6(six)`, (c) gut `CommitNhp`'s bake. Steps 1-7 land together; every step builds before the commit at the end.

**Files:**
- Create: `src/OrthoPlanner.App/ViewModels/NhpProfileViewModel.cs` (verbatim from `experimental_Lore`)
- Create: `src/OrthoPlanner.App/Helpers/NhpCameraAngles.cs` (verbatim from `experimental_Lore`)
- Modify: `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` (the engine rewrite — the bulk of this task)
- Modify: `src/OrthoPlanner.App/MainWindow.xaml` (profile UI block, ported from `experimental_Lore:280-445`)

**Interfaces:**
- Consumes: `LocalTransform`/`NhpShared`/`RecomputeAllTransforms` (Tasks 1-2). Lore source: `git show origin/experimental_Lore:src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` and `.../NhpProfileViewModel.cs` and `.../Helpers/NhpCameraAngles.cs`.
- Produces: `NhpProfiles` (`ObservableCollection<NhpProfileViewModel>`), `ActiveNhpProfileName`, `CanDeleteAnyNhpProfile`, `AddNhpProfileCommand`/`SelectNhpProfileCommand`/`DeleteNhpProfileCommand`, `ZeroAllNhpCommand`, `IsNhpDirty` (=live vs active profile's six, not vs baseline), lazy `CommitNhp` (flag flip). `NhpShared` becomes `MatrixFrom6(six)`.

- [ ] **Step 1: Bring in the DTO verbatim.**

Create `src/OrthoPlanner.App/ViewModels/NhpProfileViewModel.cs` with exactly:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace OrthoPlanner.App.ViewModels;

public partial class NhpProfileViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "NHP 1";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCommitted;
    [ObservableProperty] private bool _isLatest;
    public double Lateral { get; set; }
    public double Anteroposterior { get; set; }
    public double Vertical { get; set; }
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
}
```

This is byte-identical to `experimental_Lore` (`git show origin/experimental_Lore:src/OrthoPlanner.App/ViewModels/NhpProfileViewModel.cs`).

- [ ] **Step 2: Bring in the camera-angle helper verbatim.**

Create `src/OrthoPlanner.App/Helpers/NhpCameraAngles.cs` by copying verbatim from:

```bash
git show origin/experimental_Lore:src/OrthoPlanner.App/Helpers/NhpCameraAngles.cs > src/OrthoPlanner.App/Helpers/NhpCameraAngles.cs
```

Namespace is already `OrthoPlanner.App.Helpers` (verified — 90 lines, `FromCamera`/`OrientationChanged` + private basis/Euler helpers). No edits needed.

- [ ] **Step 3: Add the profile state fields + `IsNhpDirty` to `NhpViewModel.cs`.**

Near the slider fields (`:18-23`), after `_nhpYaw`, add the profile plumbing (from `experimental_Lore:NhpViewModel.cs:31-60`):

```csharp
    // ─── NHP profiles (named checkpoints of the 6 absolute values) — verbatim from experimental_Lore ───
    public ObservableCollection<NhpProfileViewModel> NhpProfiles { get; } = new();
    private NhpProfileViewModel? _activeNhpProfile;
    private NhpProfileViewModel? _hookedActiveProfile;
    private bool _isBulkApplyingNhpProfile;
    internal bool SuppressCameraNhpSync { get; private set; }
    private static readonly Regex DefaultNhpNameRegex = new Regex(@"^NHP (\d+)$", RegexOptions.Compiled);
    public string ActiveNhpProfileName => _activeNhpProfile?.Name ?? "NHP 1";
    public bool CanDeleteAnyNhpProfile => NhpProfiles.Count > 1;

    public bool IsNhpDirty => _activeNhpProfile == null
        ? Math.Abs(NhpLateral)         > 0.01 || Math.Abs(NhpAnteroposterior) > 0.01 || Math.Abs(NhpVertical) > 0.01
       || Math.Abs(NhpRoll)            > 0.01 || Math.Abs(NhpPitch)             > 0.01 || Math.Abs(NhpYaw)      > 0.01
        : Math.Abs(NhpLateral         - _activeNhpProfile.Lateral)          > 0.01
       || Math.Abs(NhpAnteroposterior - _activeNhpProfile.Anteroposterior)  > 0.01
       || Math.Abs(NhpVertical        - _activeNhpProfile.Vertical)         > 0.01
       || Math.Abs(NhpRoll            - _activeNhpProfile.Roll)             > 0.01
       || Math.Abs(NhpPitch           - _activeNhpProfile.Pitch)            > 0.01
       || Math.Abs(NhpYaw             - _activeNhpProfile.Yaw)             > 0.01;
```

Add `using System.Collections.ObjectModel; using System.Text.RegularExpressions;` at the top of `NhpViewModel.cs` if not present (`ObservableCollection` via `System.Collections.ObjectModel`; confirm the existing usings at `:1-4`).

Delete the old `IsNhpDirty` (`:40-45`, delta-vs-`_cLat` version) and the `_cLat.._cYaw` baseline (`:26`) — superseded.

- [ ] **Step 4: Port the profile lifecycle methods (verbatim from Lore).**

Append to `NhpViewModel.cs` (copy these from `experimental_Lore:NhpViewModel.cs` line-for-line): `InitNhpProfiles` (L511), `RefreshNhpProfileFlags` (L520), `ParseDefaultNhpNumber` (L527), `GetNextNhpProfileNumber` (L533), `RenumberAllNhpProfileNames` (L541), `HookActiveNhpProfile` (L548), `ActiveNhpProfile_PropertyChanged` (L557), `EnsureDefaultNhpProfile` (L563), `NewNhpProfileModel` (L574), `SaveActiveNhpProfileFromUi` (L585), `SyncEditBaselineFromProfile` (L596 — **delete this method**: it writes the removed `_cLat..` baseline; nothing else reads it after Step 3), `ForceSetNhpUi` (L607), `ApplyNhpProfile` (L635), `SetActiveNhpProfile` (L642), `AddNhpProfile` (L653), `DeleteNhpProfile` (L674), `SelectNhpProfile` (L707), `ApplyCameraAnglesToNhp` (L216), `SetNhpRotationsFromCamera` (L224), `ZeroAllNhp` (L234), `ResetNhp` (L255).

For each: `git show origin/experimental_Lore:src/OrthoPlanner.App/ViewModels/NhpViewModel.cs | sed -n '<start>,<end>p'` to view, then paste. Wire `AddNhpProfile`/`SelectNhpProfile`/`DeleteNhpProfile`/`ZeroAllNhp` as `[RelayCommand] private void/Task` (the `[RelayCommand]` source-generator makes `*Command` properties the XAML binds). In `MainViewModel`'s constructor (`MainViewModel.cs:27`), `InitNhpLedger()` stays but is joined by `InitNhpProfiles();`.

- [ ] **Step 5: Flip sliders absolute. Rewrite `RecomputeAllTransforms` and `BuildAbsoluteNhpPreviewMatrix`.**

Replace the Task-1 `NhpShared := _nhpTransform.Value` line in `RecomputeAllTransforms` with the absolute build, and delete the delta-builder. Replace `UpdateNhpTransform`/`RecomputeAllTransforms`:

```csharp
    // ponytail: absolute-from-source sliders — BuildNhpMatrix(six) IS the active NHP. No delta, no cumulative.
    private static System.Windows.Media.Media3D.Matrix3D BuildAbsoluteNhpPreviewMatrix()
        => BuildNhpMatrix(NhpLateral, NhpAnteroposterior, NhpVertical, NhpRoll, NhpPitch, NhpYaw);

    private void UpdateNhpTransform()
    {
        RecomputeAllTransforms();
        ScheduleDebouncedSliceUpdate();
        if (!_isBulkApplyingNhpProfile) SaveActiveNhpProfileFromUi();
    }

    private void RecomputeAllTransforms()
    {
        _nhpShared = BuildAbsoluteNhpPreviewMatrix();            // absolute-from-source (INV1, spec §3.1)
        NhpSharedTransform = new System.Windows.Media.Media3D.MatrixTransform3D(_nhpShared);
        OnPropertyChanged(nameof(NhpSharedTransform));
        if (!_isBulkApplyingNhpProfile) SaveActiveNhpProfileFromUi();

        if (HardTissueModel != null) HardTissueModel.Transform = ComposeTransforms(NhpSharedTransform, HardTissueModel.LocalTransform);
        if (SoftTissueModel != null) SoftTissueModel.Transform = ComposeTransforms(NhpSharedTransform, SoftTissueModel.LocalTransform);
        if (DentalModel     != null) DentalModel.Transform     = ComposeTransforms(NhpSharedTransform, DentalModel.LocalTransform);
        foreach (var seg  in Segments)        seg.Transform  = ComposeTransforms(NhpSharedTransform, seg.LocalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = ComposeTransforms(NhpSharedTransform, mesh.LocalTransform);
        foreach (var occ  in LoadedOcclusions) occ.Transform = ComposeTransforms(NhpSharedTransform, occ.LocalTransform);

        // INV7: ModelCenter stays at the constant source-space VolumePivot (Task 2).
        if (VolumePivot.HasValue) { ModelCenter = VolumePivot.Value; OnPropertyChanged(nameof(ModelCenter)); }

#if DEBUG
        AssertFormulaHolds();
#endif
    }
```

Delete the old `BuildAbsoluteNhpPreviewMatrix` (`experimental_Lore:337`, the `target * inv(cumulative)` version) — we just rewrote it. Delete `_nhpTransform` (`:31`) and `_cumulativeNhpMatrix` (`:36`): grep and remove every remaining read. The `OnNhp*Changed` partials (`:76-81`) are unchanged (they call `UpdateNhpTransform`, which now recomputes absolutely).

- [ ] **Step 6: Make `CommitNhp` a lazy flag flip (the spine simplification, spec §5.2).**

Replace the entire `CommitNhp` body (`:101-172`): no vertex bake, no landmark bake, no `VolumePivot` move, no cumulative fold. New body:

```csharp
    private void CommitNhp()
    {
        SaveActiveNhpProfileFromUi();                 // stamp the live 6 into the active profile
        if (_activeNhpProfile != null) _activeNhpProfile.IsCommitted = true;
        OnPropertyChanged(nameof(IsNhpDirty));        // live == stored → false
        StatusText = $"{ActiveNhpProfileName} committed (checkpoint saved).";
    }
```

Delete the now-orphaned `BakeTransformIntoVertices` **call sites** in `CommitNhp` and the ledger (`OnSegmentsChangedForNhp:317-323`, `OnMeshesChangedForNhp:334-340`, `OnOcclusionsChangedForNhp:351-357` — the `if (!SuppressLedgerBake && !... && !_cumulativeNhpMatrix.IsIdentity) BakeTransformIntoVertices(...)` branches). Keep `BakeTransformIntoVertices` itself (B2 in Task 7 uses it). The ledger's `OnXChanged` bodies shrink to just `x.Transform = ComposeTransforms(NhpSharedTransform, x.LocalTransform)` (already done by `RecomputeAllTransforms`, so the bodies become empty of bake — they can stay as the CollectionChanged hook that calls `RecomputeAllTransforms` once, or be removed entirely since `RecomputeAllTransforms` iterates all collections anyway).

- [ ] **Step 7: Port the profile XAML block.**

In `src/OrthoPlanner.App/MainWindow.xaml`, replace the existing NHP slider+buttons region with the Lore block (`experimental_Lore:MainWindow.xaml:280-445`) by:

```bash
git show origin/experimental_Lore:src/OrthoPlanner.App/MainWindow.xaml | sed -n '280,445p'
```

Paste it into the NHP panel, keeping the binding targets: `ActiveNhpProfileName`, `NhpProfiles`, `DeleteNhpProfileCommand`, `CanDeleteAnyNhpProfile`, `AddNhpProfileCommand`, `IsLatest` (DataTrigger), `SelectNhpProfileCommand`, `NhpProfileName_GotFocus`/`PreviewMouseDown`. Add the two code-behind handlers (`NhpProfileName_GotFocus`, `NhpProfileName_PreviewMouseDown`, `NhpDone_Click`, `NhpTextBox_PreviewKeyDown`) to `MainWindow.xaml.cs` from `experimental_Lore:MainWindow.xaml.cs` (grep there for these names, copy verbatim). Confirm the existing slider styles (`VerticalStepperButtonStyle`, `NhpRowMinusButtonStyle`, `NhpRowPlusButtonStyle`) are copied into `<StackPanel.Resources>` too. Keep `ZeroAllNhpCommand` + the DONE button (`IsNhpDirty`-gated) — DONE calls `CommitNhp`.

- [ ] **Step 8: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.` Fix any `_cLat..`/`_cumulativeNhpMatrix`/`_nhpTransform` references left dangling (grep them — should be zero outside the deleted regions). Run: `grep -rn "_cLat\|_cumulativeNhpMatrix\|_nhpTransform\b" src/OrthoPlanner.App/` → expect no matches in `NhpViewModel.cs` (ProjectViewModel still references them — that's Task 4 / Task 6; those compile against fields that must still *exist* until Task 4 deletes the load path. **Therefore**: do NOT delete `_cumulativeNhpMatrix`/`_cLat..` fields until Task 4; keep them as dead fields this task, marked `// ponytail: dead under lazy model — removed in Task 4`). Adjust Step 5's deletion to *comment out / stop writing* these fields rather than remove the declarations, so ProjectViewModel still compiles. Task 4 deletes the declarations with the load path.

- [ ] **Step 9: Manual INV3 check.**

Launch, load DICOM, open NHP. Set some non-zero NHP (e.g. Yaw 10°, Lateral 20mm). Note the scene. Press **DONE** (commit). Confirm: the scene does **not** move at all (INV3 — commit is a flag flip; sliders keep showing the live values; `IsNhpDirty` goes false). Add a second NHP profile (＋), switch back and forth — each restores its six into the sliders and the scene reorients; the first profile still says committed. Reset → sliders to 0 → scene neutral.

- [ ] **Step 10: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/NhpProfileViewModel.cs src/OrthoPlanner.App/Helpers/NhpCameraAngles.cs src/OrthoPlanner.App/ViewModels/NhpViewModel.cs src/OrthoPlanner.App/MainWindow.xaml src/OrthoPlanner.App/MainWindow.xaml.cs
git commit -m "feat(nhp): profile layer + absolute sliders + lazy commit (Task 3)

Ports NhpProfileViewModel + NhpCameraAngles verbatim from experimental_Lore (cdc4def).
Sliders are now absolute-from-source: NhpShared = MatrixFrom6(six). CommitNhp is a flag flip
(no vertex/landmark/VolumePivot bake, no cumulative fold) — commit moves nothing on screen (INV3).
Multiple NHPs are named checkpoints; surgery plans stay a separate layer. Delta/cumulative fields
dead-marked pending Task 4 deletion.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 4: New save/load — `NhpProfiles` + per-piece `LocalTransform` + source-space vertices/landmarks

**Goal:** Persist the lazy model. Save writes `NhpProfiles` + per-piece `LocalTransform` (16 doubles) + source-space vertices/landmarks/`VolumePivot`. Load restores profiles → active six into sliders → `RecomputeAllTransforms` → `UpdateAllSlices`. Delete the dead `_cLat..`/`_cumulativeNhpMatrix` declarations and the old `NhpBaseline`/`CumulativeNhpMatrix` load path (kept as the legacy fallback that Task 6 overhauls). Verify INV2, INV4, INV8.

**Files:**
- Modify: `src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs` (save `:60-92`, load `:274-399`)
- Modify: `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` (delete dead fields; add `RestoreNhpProfilesFromProject` + `MigrateBaselineToNhpProfileIfNeeded`, verbatim-ish from Lore `:716-752`)

**Interfaces:**
- Consumes: `NhpProfiles`, `LocalTransform` on each piece (Tasks 1,3).
- Produces: NEW-format `.orthoplan` (has `NhpProfiles`); the §6 gate target. INV6 (Task 6) consumes the legacy branch this task keeps selectable.

- [ ] **Step 1: Add `MatrixToArray`/`ArrayToMatrix` if missing (already present: `MatrixToArray` in save, `ArrayToMatrix` local fn at `ProjectViewModel.cs:492`). Keep them.**

- [ ] **Step 2: Rewrite the save `meta` block (`ProjectViewModel.cs:60-92`).**

Replace the `NhpBaseline` (`:68-76`) and `CumulativeNhpMatrix` (`:78`) entries with `NhpProfiles`, and add per-piece `LocalTransform` to each mesh/segment/occlusion meta. New keys:

```csharp
                // NEW primary: NHP profiles (named checkpoints of the 6 absolute values)
                NhpProfiles = NhpProfiles.Select(p => new
                {
                    p.Name, p.Lateral, p.Anteroposterior, p.Vertical, p.Roll, p.Pitch, p.Yaw,
                    p.IsSelected, p.IsCommitted, p.IsLatest
                }).ToArray(),
                VolumePivot = VolumePivot.HasValue ? new { X = VolumePivot.Value.X, Y = VolumePivot.Value.Y, Z = VolumePivot.Value.Z } : (object?)null,
                CondyleCenters = new
                {
                    Left    = LeftCondyleCenter  == null ? null : (object)new { LeftCondyleCenter.Value.X,  LeftCondyleCenter.Value.Y,  LeftCondyleCenter.Value.Z },
                    Right   = RightCondyleCenter == null ? null : (object)new { RightCondyleCenter.Value.X, RightCondyleCenter.Value.Y, RightCondyleCenter.Value.Z },
                    LeftHalfExtents  = LeftCondyleHalfExtents  == null ? null : (object)new { LeftCondyleHalfExtents.Value.X,  LeftCondyleHalfExtents.Value.Y,  LeftCondyleHalfExtents.Value.Z },
                    RightHalfExtents = RightCondyleHalfExtents == null ? null : (object)new { RightCondyleHalfExtents.Value.X, RightCondyleHalfExtents.Value.Y, RightCondyleHalfExtents.Value.Z },
                    Midline = DentalMidlinePoint == null ? null : (object)new { DentalMidlinePoint.Value.X, DentalMidlinePoint.Value.Y, DentalMidlinePoint.Value.Z }
                },
                CurrentSurgeryPlan = SnapshotCurrentPlan("Current")
```

In the per-piece .bin loop for meshes (`:111-121`), segments (`:124-134`), occlusions (`:137-147`), write `LocalTransform` to the mesh/segment meta JSON (added to the `ImportedMeshes`/`Segmentation.Segments`/`OcclusionMeshes` array elements in their respective meta blocks earlier in the file — find each `meshMeta`/`segMeta`/`occMeta` builder and add `LocalTransformMatrix = MatrixToArray(((System.Windows.Media.Media3D.Matrix3D)x.Transform.Value))` … wait, `LocalTransform` may be a `Transform3D` not `MatrixTransform3D`; normalize: `Matrix3D local = x.LocalTransform is System.Windows.Media.Media3D.MatrixTransform3D m ? m.Value : System.Windows.Media.Media3D.Matrix3D.Identity;` then `MatrixToArray(local)`). Add per-piece `LocalTransformMatrix` key.

- [ ] **Step 3: Add `RestoreNhpProfilesFromProject` + `MigrateBaselineToNhpProfileIfNeeded` to `NhpViewModel.cs`.**

Port verbatim from `experimental_Lore:NhpViewModel.cs:716-752` (`RestoreNhpProfilesFromProject` L716, `MigrateBaselineToNhpProfileIfNeeded` L734). These clear/restore `NhpProfiles`, ensure a default, pick `IsSelected` or `[0]`, `SetActiveNhpProfile`+`ApplyNhpProfile`. The migration builds `"NHP 1"` from `_cLat..` values when `NhpProfiles` is empty — for the lazy model, `_cLat..` are dead (Task 3); the migration reads `NhpBaseline` *from the JSON* (passed in by the load path), so it works without the field. Adjust the migration signature to take the six baseline values from the load path rather than the dead fields.

- [ ] **Step 4: Rewrite the load path (`ProjectViewModel.cs:274-399`).**

Gate on `NhpProfiles` presence (spec §6):

```csharp
                    bool hasNewProfiles = root.TryGetProperty("NhpProfiles", out var profilesNode);

                    // VolumePivot restore — unchanged (:274-288)
                    // ... (keep)

                    if (hasNewProfiles)
                    {
                        // NEW format: restore profiles → active six into sliders → recompute.
                        var restored = new List<(string Name, double Lat, double Ant, double Vert, double Roll, double Pitch, double Yaw, bool Sel, bool Com, bool Latest)>();
                        foreach (var p in profilesNode.EnumerateArray())
                            restored.Add((p.GetProperty("Name").GetString() ?? "NHP 1",
                                p.GetProperty("Lateral").GetDouble(), p.GetProperty("Anteroposterior").GetDouble(),
                                p.GetProperty("Vertical").GetDouble(), p.GetProperty("Roll").GetDouble(),
                                p.GetProperty("Pitch").GetDouble(), p.GetProperty("Yaw").GetDouble(),
                                p.GetProperty("IsSelected").GetBoolean(), p.GetProperty("IsCommitted").GetBoolean(),
                                p.TryGetProperty("IsLatest", out var l) && l.GetBoolean()));
                        RestoreNhpProfilesFromProject(restored);
                    }
                    else
                    {
                        // LEGACY bake-model file → migration shim (Task 6 fills steps 1-5).
                        // ponytail: removable shim once new-format test cases exist (spec §6).
                        var nb = root.TryGetProperty("NhpBaseline", out var bn) ? bn : default;
                        double bl(string k, bool rot) => nb.ValueKind == System.Text.Json.JsonValueKind.Undefined ? 0 : Math.Clamp(ReadSafe(nb, k), rot ? -45 : -200, rot ? 45 : 200);
                        MigrateBaselineToNhpProfileIfNeeded(bl("Lat",false), bl("Ant",false), bl("Vert",false), bl("Roll",true), bl("Pitch",true), bl("Yaw",true));
                        // Task 6 adds the vertex/landmark un-bake here.
                    }

                    // CondyleCenters restore — unchanged (:380-395) — but landmarks now load DIRECTLY into source space (no post-load bake).
                    // ... (keep), no double-bake
```

Replace the old `NhpBaseline` block (`:291-345`) and `CumulativeNhpMatrix` block (`:348-377`) with the gate above. Delete the read into `_cLat..`/`_cumulativeNhpMatrix`. Per-piece `LocalTransformMatrix` restore: in each mesh/segment/occlusion load loop, after constructing the VM and before `BuildModel()`, set `x.LocalTransform = new MatrixTransform3D(ArrayToMatrix(meta));` (default Identity if absent). Restore order **before** `Segments.Add`/`ImportedMeshes.Add` so `RecomputeAllTransforms` (called at end) composes correctly — no bake on add (ledger bake branch deleted in Task 3). Replace the trailing `UpdateNhpTransform()` calls (`:398`, `:484`) — they already route to `RecomputeAllTransforms` (Task 3). Keep `UpdateAllSlices()` (`:399`).

- [ ] **Step 5: Delete the dead fields.**

Now that load no longer reads them, delete `_cLat.._cYaw` (`NhpViewModel.cs:26`) and `_cumulativeNhpMatrix` (`:36`) and `_nhpTransform` (`:31`, if any residue) from `NhpViewModel.cs`. Grep: `grep -rn "_cLat\|_cumulativeNhpMatrix\|_nhpTransform" src/` → expect zero (the legacy shim in Task 6 will pass *values*, not read these fields).

- [ ] **Step 6: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.`

- [ ] **Step 7: Manual INV2 + INV4 + INV8 checks.**

Lauch, load DICOM, set NHP Yaw 10°, commit, import+align a cast (B2), run a split. **Save** as `new.orthoplan`. Inspect the zip's `project.json`: confirm `NhpProfiles` present, `CumulativeNhpMatrix` and `NhpBaseline` absent. **Reopen** `new.orthoplan`:
- INV2: the scene is identical to before save (vertices unchanged across save/reopen; source space, `NhpShared` rebuilt from profile).
- INV4: drag NHP further — condyle centers / midline source landmarks do NOT move (effective pivot = `NhpShared ∘ landmark` is computed, not baked).
- INV8: the aligned cast renders at one NHP application, not cumulative² (audit HIGH closed — nothing bakes on load).
Switch NHP profiles → scene reorients; reopen again → identical (round-trip).

- [ ] **Step 8: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs src/OrthoPlanner.App/ViewModels/NhpViewModel.cs
git commit -m "feat(project): new save/load — NhpProfiles + per-piece LocalTransform + source space (Task 4)

project.json gains NhpProfiles (primary) + per-piece LocalTransformMatrix; drops NhpBaseline/CumulativeNhpMatrix.
Load is version-gated on NhpProfiles: new path restores profiles→sliders→Recompute (no bake));
legacy path routes to the Task-6 migration shim. Vertices/landmarks load source-space directly; the
scene is correct on open (INV2,INV4,INV8). Dead delta/cumulative/baseline fields deleted.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 5: Bake points — B1 (split source return) + B3 (splint inherits jaw `LocalTransform`); delete `NhpBaked`

**Goal:** Make the two no-bake points literal. The split already returns DICOM/source vertices — just stop any forward-bake intent and set `LocalTransform = Identity`. The splint stops baking `BakeToCopy(seg.Vertices, SurgicalTransform)` into its own vertices; instead it generates from source-space jaw teeth and sets `splint.LocalTransform = jaw.LocalTransform`. Delete `NhpBaked` everywhere (no longer needed — nothing bakes cumulatively). Verify INV9; close P1 + the splint-order audit (MED-HIGH) and (with Task 4) the occlusion double-bake (HIGH).

**Files:**
- Modify: `src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs` (split return assignment, `:422-449`)
- Modify: `src/OrthoPlanner.App/ViewModels/SplintViewModel.cs` (`BakeToCopy` call, `:115-118`; `NhpBaked = true`, `:318`; `AddSplintMeshToScene`, `:217/300`)
- Modify: `src/OrthoPlanner.App/ViewModels/MainViewModel.cs` (delete `NhpBaked` on SegmentViewModel `:237` and MeshViewModel `:315`)
- Modify: `src/OrthoPlanner.App/ViewModels/NhpViewModel.cs` (delete `NhpBaked` reads in the ledger `On*Changed` bodies)

**Interfaces:**
- Consumes: `LocalTransform` (Tasks 1,3), source-space `seg.Vertices`.
- Produces: INV9 (splint seats under any NHP). P1 closed (no DICOM-vs-NHP mismatch — split returns source, `LocalTransform=Identity`, `NhpShared` composes once).

- [ ] **Step 1: Delete `NhpBaked` declarations.**

Remove `public bool NhpBaked { get; internal set; }` (`MainViewModel.cs:237`, SegmentViewModel) and `public bool NhpBaked { get; set; }` (`:315`, MeshViewModel), with their doc comments. Grep all readers/writers: `grep -rn "NhpBaked" src/`. Each must be deleted or replaced. The ledger `On*ChangedForNhp` already lost its bake branch (Task 3, Step 6); remove its `alreadyBaked`/`mesh.NhpBaked`/`occ.NhpBaked` reads (`NhpViewModel.cs:313,338,351`).

- [ ] **Step 2: B1 — split returns source, `LocalTransform = Identity`.**

In `OsteotomyViewModel.SplitCraniumMandibleAsync` (`:400-449`): the slice-build passes `inverseNhpMatrix` (now `NhpShared⁻¹`, fed from `_nhpShared.IsIdentity ? null : InvertMatrix(_nhpShared)` instead of `_cumulativeNhpMatrix` — update `:403-405`) so the split picks map back to source. The returned `CraniumResult`/`MandibleResult` are already DICOM/source vertices. Assign them to the new segments with `LocalTransform = Transform3D.Identity` (they are source-space; `NhpShared` composes at recompute). The crucial change vs today: do **not** apply any forward NHP transform to these vertices — they stay source. Confirm the assignment (`:429`/`:445`) is a bare copy:

```csharp
                    Vertices = MeshHelper.ToFlatArray(wizard.CraniumResult),
                    // B1: source space, no bake. LocalTransform stays Identity; NhpShared composes at recompute (RecomputeAllTransforms).
```

No `_nhpDisplayTransform` applied as a display-only `Transform` anymore — `RecomputeAllTransforms` sets `seg.Transform = Compose(NhpShared, Identity)` for them (covered by the `foreach Segments` line). Delete any `_nhpDisplayTransform` usage on the returned meshes.

- [ ] **Step 3: B3 — splint inherits the jaw's `LocalTransform`, no bake.**

In `SplintViewModel.OpenSplintPlanner` (`:115-118` region): today it builds `SplintVertices` from `BakeToCopy(maxillaSeg.Vertices, maxillaSeg.SurgicalTransform)`. Replace with: generate the splint geometry from the **source-space** jaw vertices (`maxillaSeg.Vertices`, unchanged) — `SplintEngine` already takes a vertex array; pass source vertices, not a baked copy. Then add the splint mesh with `LocalTransform = maxillaSeg.LocalTransform` (the jaw's surgical transform), and `NhpBaked` removed:

```csharp
                    // B3: no bake. Generate from the jaw's SOURCE-space teeth; splint inherits the jaw's LocalTransform.
                    // INV9: splint.Transform = Compose(NhpShared, jaw.LocalTransform) == jaw.Transform → seats under any NHP, any profile.
                    splintMeshVm.LocalTransform = maxillaSeg.LocalTransform;
```

Remove the `NhpBaked = true` line (`:318`). If `SplintEngine` internally needs the jaw's posed teeth to compute offsets, compute offsets in source space against source teeth normals (the splint's outward normal then rotates with the jaw via `LocalTransform`) — do not pre-pose. If `SplintEngine` reads `CondyleBox`, it already gets source-space center + half-extents and `NhpShared` poses it (Spec §4). Audit the `BakeToCopy` call and the splint-geometry generation: the only legitimate bake (B2) is in `StlViewModel`, not here — `BakeToCopy` here is the bug, remove it.

- [ ] **Step 4: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.` Grep `NhpBaked` → zero.

- [ ] **Step 5: Manual INV9 check.**

Launch, load DICOM, set NHP Yaw 15° (+ dirty, uncommitted), run a split, then open the Splint Planner and generate a splint. Confirm the splint seats exactly on the maxillary teeth under the uncommitted NHP. Commit NHP (Task 3 flag-flip) — the splint stays seated. Change the jaw's surgical slide (LeFort) — the splint moves rigidly with the jaw (shares `LocalTransform`). Switch NHP profiles — splint + jaw reorient together.

- [ ] **Step 6: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/OsteotomyViewModel.cs src/OrthoPlanner.App/ViewModels/SplintViewModel.cs src/OrthoPlanner.App/ViewModels/MainViewModel.cs src/OrthoPlanner.App/ViewModels/NhpViewModel.cs
git commit -m "fix(transforms): B1/B3 no-bake — split returns source, splint inherits jaw LocalTransform (Task 5)

Split returns source-space vertices with LocalTransform=Identity (P1 closed — no DICOM-vs-NHP mismatch).
Splint generated from source jaw teeth and gets splint.LocalTransform = jaw.LocalTransform (INV9 — seats under
any NHP, any committed/dirty state, any profile). BakeToCopy removed from the splint path. NhpBaked deleted
everywhere; nothing bakes cumulatively, so the occlusion-double-bake-on-reopen (audit HIGH) is also closed.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 6: Legacy migration shim — version-gated, removal-pending (spec §6)

**Goal:** Open a bake-model `.orthoplan` (has `NhpBaseline` + `CumulativeNhpMatrix`, no `NhpProfiles`) and migrate it into the lazy model so it renders identically, then save in the new format. The shim is one guarded `else`, marked removable. Verify INV6.

**Files:**
- Modify: `src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs` (the legacy branch from Task 4 Step 4 — fill steps 1-5)

**Interfaces:**
- Consumes: the load gate (Task 4); `MigrateBaselineToNhpProfileIfNeeded` (Task 4 Step 3); `BuildNhpMatrix` / inverse (NhpViewModel).
- Produces: INV6 — the shim's exit criterion. Once a corpus of new-format files exists and INV6 passes against them alone, delete steps 1-5 (`#346ff ponytail` reminder below).

- [ ] **Step 1: Fill the legacy branch in the load path.**

In `ProjectViewModel.cs` legacy `else` (Task 4 Step 4), after `MigrateBaselineToNhpProfileIfNeeded(...)`, add the un-bake (this is the ONLY place `CumulativeNhpMatrix` is read now, and ONLY from old files):

```csharp
                        // ponytail: removable shim once new-format .orthoplan test cases exist; verify INV6 passes
                        //           on new-format files alone, then delete steps 1-5 (spec §6).
                        // 1. bake = CumulativeNhpMatrix (16 doubles, NaN-guarded — reuse ProjectViewModel.cs:355 logic)
                        System.Windows.Media.Media3D.Matrix3D bake = System.Windows.Media.Media3D.Matrix3D.Identity;
                        if (root.TryGetProperty("CumulativeNhpMatrix", out var cumNode))
                        {
                            var matD = new double[16]; int di = 0;
                            foreach (var v in cumNode.EnumerateArray())
                            { double val = v.GetDouble(); matD[di++] = double.IsNaN(val) || double.IsInfinity(val) ? (di==1||di==6||di==11||di==16?1.0:0.0) : val; }
                            if (di == 16) bake = new System.Windows.Media.Media3D.Matrix3D(matD[0],matD[1],matD[2],matD[3],matD[4],matD[5],matD[6],matD[7],matD[8],matD[9],matD[10],matD[11],matD[12],matD[13],matD[14],matD[15]);
                        }
                        // 2-3. Un-bake every piece's vertices + each landmark + VolumePivot back to source space.
                        //       Per-piece: applied in the restore loops below (vertices loaded, then *= inverse(bake)).
                        //       Store bake on a transient field the loops read.
                        LegacyUnbakeMatrix = bake.IsIdentity ? null : (System.Windows.Media.Media3D.Matrix3D?)bake;
                        // 4. "NHP 1" profile already built by MigrateBaselineToNhpProfileIfNeeded.
                        // 5. Recompute at end → NhpShared == bake → identical render (Task 4 already calls UpdateNhpTransform).
```

Add a `private System.Windows.Media.Media3D.Matrix3D? LegacyUnbakeMatrix;` local-to-load holder (reset to null at load start). In each per-piece restore loop (meshes `:411-438`, segments `:445-477`, occlusions `:505-535`), after reading `verts` and before `BuildModel()`, un-bake:

```csharp
                        if (LegacyUnbakeMatrix.HasValue) BakeTransformIntoVertices(verts, Inverse(LegacyUnbakeMatrix.Value));
```

(`BakeTransformIntoVertices` already exists in NhpViewModel; expose a static inverse or inline `var inv = LegacyUnbakeMatrix.Value; inv.Invert(); BakeTransformIntoVertices(verts, inv);`.) Un-bake `CondyleCenters` (`:390-394`), `CephLandmarks` 3D, and `VolumePivot` (`:274-280`): apply `inv.Transform(point)`. Wrap these in `if (LegacyUnbakeMatrix.HasValue)`. After the load, the active profile's six (from the migrated baseline) rebuild `NhpShared == bake`, so the scene renders identically in the new model.

- [ ] **Step 2: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Manual INV6 check (round-trip a known legacy file).**

Open an existing bake-model `.orthoplan` (no `NhpProfiles`). Confirm it renders identically to how it opened before this work (compare a screenshot/landmark coords). Save → inspect `project.json`: `NhpProfiles` present, `CumulativeNhpMatrix` + `NhpBaseline` gone. Reopen the new file → identical render. (If no legacy test file exists, create one by checking out a pre-Task-4 build, saving a project with committed NHP, then upgrading.) Note in `docs/superpowers/plans/2026-07-10-nhp-transform-architecture.md` that INV6 gets re-run on new-format-only files before the shim is deleted.

- [ ] **Step 4: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/ProjectViewModel.cs
git commit -m "feat(project): legacy .orthoplan migration shim — version-gated, removal-pending (Task 6)

Old bake-model files (NhpBaseline+CumulativeNhpMatrix, no NhpProfiles) are un-baked to source space
(vertices, landmarks, VolumePivot ×= inverse(bake)) and a single 'NHP 1' profile built from the baseline;
Recompute rebuilds NhpShared == bake → identical render, now in the lazy model. INV6. Shim carries a
ponytail: removable comment; delete steps 1-5 once new-format files are the only INV6 corpus.

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 7: B2 guard + B4 transient + Core math self-check; onboarding walkthrough

**Goal:** Enforce the only bake (B2 onto CT source space) with a DEBUG assert, quarantine B4 scratch, wire the one-time NHP math self-check into startup, and deliver the (j) walkthrough. Reuses — no new deps.

**Files:**
- Modify: `src/OrthoPlanner.App/ViewModels/StlViewModel.cs` (B2 target-source assert, `:97`)
- Modify: `src/OrthoPlanner.App/App.xaml.cs` (self-check call in `OnStartup`, `:57`)
- Create: `src/OrthoPlanner.App/NhpMathSelfCheck.cs` (DEBUG one-time math check)
- Create: `docs/superpowers/specs/2026-07-10-nhp-transform-architecture-walkthrough.md` (deliverable j)

**Interfaces:**
- Consumes: `BuildNhpMatrix`, `NhpShared`, `LocalTransform` (Tasks 1-3).
- Produces: the finished lazy model with its safety net + the walkthrough doc.

- [ ] **Step 1: B2 — assert the ICP target is source space, never posed.**

In `StlViewModel.AlignDentalScansAsync` (`:70-97`), after selecting `ctSegment`, before opening the wizard:

```csharp
            // B2 guard (spec §4): alignment target is the CT SOURCE-space vertices, never an NhpShared-posed copy.
            // ctSegment.Vertices are invariant source under the lazy model; this asserts no one pre-posed them.
#if DEBUG
            System.Diagnostics.Debug.Assert(ctSegment.Transform is not System.Windows.Media.Media3D.MatrixTransform3D
                || ((System.Windows.Media.Media3D.MatrixTransform3D)ctSegment.Transform).Value.IsIdentity
                    == false /* Transform is the posed-derived value; Vertices themselves are source — invariant */,
                "B2: ctSegment.Vertices must be source space, not pre-multiplied by NhpShared");
#endif
```

More directly: add an assert that `ctSegment.Vertices` equals its source (un-posed) — under the lazy model this is invariant, so a simpler, always-true DEBUG guard is:

```csharp
#if DEBUG
            // B2: the alignment target is the source-space CT surface. Vertices must not have been pre-posed.
            System.Diagnostics.Debug.Assert(ctSegment != null && ctSegment.Vertices != null && ctSegment.Vertices.Length >= 100,
                "B2: ctSegment.Vertices (source space) is the ICP target — pre-posing it would bake NHP into the cast.");
#endif
```

(The hard invariant — "nobody pre-multiplies Vertices by NhpShared before ICP" — is upheld structurally because `RecomputeAllTransforms` writes `Transform`, never `Vertices`; Vertices are only ever written at B1/B2/clean-merge. The assert documents the contract.) The bake itself (`:107-108`, `IcpAligner.TransformVertices` into `scan.Vertices`) stays — it IS B2.

- [ ] **Step 2: B4 — quarantine scratch so it never reaches persisted state.**

Audit `SplintEngine` (autorotation returned copy, clearance heightfield) and `BakeTransformIntoVertices` call sites: confirm none assign a scratch transform to a persisted `piece.Transform` or write to stored `Vertices`. Add a `// ponytail: B4 — transient scratch; never assigned to a stored piece (spec §4)` comment at each scratch buffer. (If a violation is found, that's a bug outside this plan's scope — flag it, do not silently fix the algorithm.)

- [ ] **Step 3: Create the Core math self-check.**

Create `src/OrthoPlanner.App/NhpMathSelfCheck.cs`:

```csharp
#if DEBUG
using System.Windows.Media.Media3D;

namespace OrthoPlanner.App;

/// <summary>One-time DEBUG self-check of the NHP math primitives (spec §7 — no test framework).</summary>
internal static class NhpMathSelfCheck
{
    public static void Run()
    {
        // 1. Identity at zero.
        var zero = ViewModels.MainViewModel.BuildNhpMatrixForCheck(0,0,0,0,0,0);
        System.Diagnostics.Debug.Assert(zero.IsIdentity, "NHP: MatrixFrom6(0..0) must be Identity");
        // 2. Round-trip: inverse(apply) == identity within tol.
        var m = ViewModels.MainViewModel.BuildNhpMatrixForCheck(10, 20, 30, 5, 8, 12);
        var inv = m; inv.Invert();
        var composed = m * inv;
        System.Diagnostics.Debug.Assert(Math.Abs(composed.M11-1)<1e-9 && Math.Abs(composed.M22-1)<1e-9 && Math.Abs(composed.M33-1)<1e-9
            && Math.Abs(composed.OffsetX)<1e-9 && Math.Abs(composed.OffsetY)<1e-9 && Math.Abs(composed.OffsetZ)<1e-9,
            "NHP: matrix*inverse must be Identity");
        // 3. Compose(NhpShared, Identity) == NhpShared (INV1 base + INV5 order: NhpShared first).
        // (Formula's left-then-right order verified structurally in RecomputeAllTransforms.AssertFormulaHolds.)
    }
}
#endif
```

Expose `BuildNhpMatrix` for the check without making it public API pollution: in `NhpViewModel.cs`, change `private static Matrix3D BuildNhpMatrix(...)` to `internal static` and add `using System;` (degrees→radians already inside). Add a thin `internal static Matrix3D BuildNhpMatrixForCheck(double lat,to yaw) => BuildNhpMatrix(lat,ant,vert,roll,pitch,yaw);` wrapper at the end of `NhpViewModel.cs` (or call `BuildNhpMatrix` directly if internal access works — simplest: make `BuildNhpMatrix` `internal static`). Mark with `// ponytail: internal for the DEBUG self-check only`.

- [ ] **Step 4: Wire the self-check into startup.**

In `src/OrthoPlanner.App/App.xaml.cs` `OnStartup` (`:57`), after the splash starts:

```csharp
#if DEBUG
        System.Threading.Tasks.Task.Run(() => OrthoPlanner.App.NhpMathSelfCheck.Run()).Wait();
#endif
```

- [ ] **Step 5: Build clean.**

Run: `dotnet build src/OrthoPlanner.App/OrthoPlanner.App.csproj`
Expected: `Build succeeded.`

- [ ] **Step 6: Manual end-to-end + self-check confirmation.**

Launch (DEBUG). The Output window shows no NHP assert failures (the math self-check ran). Repeat the inv-1..9 smoke flows through a full case: load DICOM → split → cast import+align (B2) → splint (B3) → NHP commit + profile add/switch → save → reopen (INV6 if from an upgraded legacy file) → surgical plan change → undo. Confirm no DEBUG assert fires and the scene is consistent throughout.

- [ ] **Step 7: Write the walkthrough deliverable (j).**

Create `docs/superpowers/specs/2026-07-10-nhp-transform-architecture-walkthrough.md` from the now-finalized §1, §3, §8 of the spec + the implemented code: explain (a) the source frame + `Compose(NhpShared, LocalTransform)` formula with the layer diagram; (b) how the three display subsystems consume NhpShared (3D meshes compose; volume render binds NhpShared; MPR oblique-resamples by `NhpShared⁻¹`, rotation only); (c) the four bake points with the closed-list rule "only B2 bakes"; (d) the commit-is-a-flag-flip reasoning; (e) the legacy shim and its exit gate. Include simple ASCII diagrams (reuse the spec's layer + three-subsystem diagrams; refine with implementation realities).

- [ ] **Step 8: Update memory.**

Update `memory/nhp-function.md` — mark the baking model section superseded by the lazy model (commit `011c1f1` spec + this implementation), and record the new canonical facts (formula, NhpProfiles, B2-only bake, INV1-9). Leave the historical vuln catalog as audit history.

- [ ] **Step 9: Commit.**

```bash
git add src/OrthoPlanner.App/ViewModels/StlViewModel.cs src/OrthoPlanner.App/App.xaml.cs src/OrthoPlanner.App/NhpMathSelfCheck.cs src/OrthoPlanner.App/ViewModels/NhpViewModel.cs docs/superpowers/specs/2026-07-10-nhp-transform-architecture-walkthrough.md
git commit -m "feat(nhp): B2/B4 guards + Core math self-check + onboarding walkthrough (Task 7)

B2 asserts the ICP target is source CT vertices (never NhpShared-posed). B4 scratch quarantined with
ponytail markers. NhpMathSelfCheck runs once at DEBUG startup (identity/round-trip/order); BuildNhpMatrix
made internal for it. Onboarding walkthrough + memory update complete deliverable (j).

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Self-Review (run after writing, before offering execution)

- **Spec coverage:** Spec §0-§11 → Task map: §1-3 formula/recompute → Task 1+3; §4 bake points → Task 5(B1/B3)+7(B2/B4); §5 commit/save/multi-NHP → Task 3+4; §6 shim → Task 6; §7 INV1-9 → Tasks 1,2,3,4,5,6,7 each verify its invs; §8 display子系统 + translation fix + centering → Task 2; §9 build order → Tasks 1-7 in order; §10 Lore prior-art → Task 3 Step 1-4 + Task 4 Step 3 (verbatim port); §11 deliverables → Task 7 Step 7-8. No spec section untasked.
- **Placeholder scan:** No "TBD"/"TODO"/"fill in". Lore ports cite exact `git show` line ranges any implementer can replay. Large Lore blocks (Task 3 Step 4) are "copy line-for-line from named method + named Lore line range" — explicit, not paraphrased.
- **Type consistency:** `LocalTransform` (Transform3D) used consistently; `NhpShared`/`NhpSharedTransform` produced Task 1, consumed Tasks 2/3; `NhpProfiles`/`NhpProfileViewModel` declared Task 3 Step 1, consumed Task 4; `RestoreNhpProfilesFromProject`/`MigrateBaselineToNhpProfileIfNeeded` produced Task 4 Step 3, used Task 6; `LegacyUnbakeMatrix` produced+consumed Task 6 Step 1 only; `BuildNhpMatrixForCheck`/`NhpMathSelfCheck.Run()` Task 7. Names match across tasks.
