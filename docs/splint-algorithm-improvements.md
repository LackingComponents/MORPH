# Splint Algorithm Improvements: Guided Bridge, Vestibular Trim Bias, Efficiency

## Context

The current splint generation pipeline works well for typical cases but breaks down when mandibular autorotation opens a large inter-arch gap. Morphological closing (PHASE 2) struggles to bridge large gaps via isotropic sphere dilation — the radius must grow proportionally to the gap, making it expensive and unreliable.

Additionally, the vestibular trim slider's zero-point is poorly anchored: the blue horseshoe ribbon in the viewport represents the true labio-lingual footprint (`half + |bias|` from the arch centre), but the current clip formula adds `Dil1 + VestBaseMargin = 4mm` on top of that, so the default `−3.2` trims back from an invisible 4mm overhang rather than from the horseshoe line itself.

**Three changes:**
1. Replace unreliable morphological-gap-bridging with directed per-column corridor fill (guided bridge)
2. Recentre the vestibular trim slider: `0` = trim at the horseshoe outer edge; negative = trim more inward; positive = extra vestibular wall
3. Optimize the per-voxel nearest-arch lookup from O(n) to O(1)

---

## 1. Guided Bridge (Per-Column Corridor Fill)

**Problem:** PHASE 2 morphological closing uses isotropic sphere dilation to bridge the inter-arch gap. For large gaps, `closeR` scales to `ceil(maxGap * 0.55 / 0.5)` (max 30), requiring O(closeR³) work per voxel. Even then, asymmetric erosion may not preserve a thick enough bridge, and the subsequent blur (5 passes) can punch through thin bridge regions.

**Solution:** Bake the bridge directly into PHASE 1's SDF grid. For every voxel that is:
- Between the Z-clip planes (inside the splintable vertical range)
- Within the labio-lingual corridor of the nearest upper **or** lower arch point
- Inside the posterior limit
- Currently classified as "air" by the tooth SDFs (i.e., `blankSdf > 0`)

→ Set it to `bridgeSdfBias` (a negative constant, firmly inside the material surface) instead of leaving it as air.

This guarantees Z-connectivity by construction — each arch sample point's column connects upper to lower. No isotropic dilation required.

### 1.1 Corridor geometry

The corridor follows the same construction as the horseshoe body's cross-section:

```
For a voxel at (px, py):
  signed_perp = ((px − archX) * normalX + (py − archY) * normalY)
  in_corridor = |signed_perp − lingualBuccalBiasMm| <= half
```

Where `half = labiolingualMm * 0.5f`. This matches the horseshoe body's wall placement exactly (TI/TO inner/outer boundaries), ensuring the bridge aligns with the tooth-pocket walls.

### 1.2 Code changes in PHASE 1

After the existing tooth SDF evaluation ([SplintEngine.cs:670–672](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L670)), add the bridge fill:

```csharp
float upVal1 = uPerpDist <= maxVest ? (float)upImpl1.Value(ref pt) : 1.0f;
float loVal1 = lPerpDist <= maxVest ? (float)loImpl1.Value(ref pt) : 1.0f;
float blankSdf = MathF.Min(upVal1, loVal1);

// ─── NEW: Guided bridge ──────────────────────────────────────────────────────
// If this voxel is air but lies within the labio-lingual corridor of either
// arch, fill it with bridgeSdfBias to guarantee Z-connectivity by construction.
if (blankSdf > 0f)
{
    // uSigned / lSigned: signed perpendicular distance along the arch normal.
    // uPerpDist is |uSigned|; recompute the signed version to check corridor side.
    float uSigned = ((float)px - uax) * unx + ((float)py - uay) * uny;
    float lSigned = ((float)px - lax) * lnx + ((float)py - lay) * lny;
    bool inUpperCorridor = MathF.Abs(uSigned - lingualBuccalBiasMm) <= half;
    bool inLowerCorridor = MathF.Abs(lSigned - lingualBuccalBiasMm) <= half;
    if (inUpperCorridor || inLowerCorridor)
        blankSdf = bridgeSdfBias;   // = −(BridgeSdfBaseMm + BridgeThicknessMm)
}
bakedGrid[vIdx] = blankSdf;
```

`uax, uay, unx, uny, lax, lay, lnx, lny` are already computed above for the vestibular clip — no extra nearest-arch lookup needed.

**File:** [SplintEngine.cs:670–672](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L670)

---

## 2. PHASE 2 Reduction (Morphological Closing → Light Smoothing)

With the guided bridge guaranteeing connectivity, morphological closing only needs to smooth the junction between the tooth blanks and the bridge corridor. Change `closeR` from the gap-dependent expression to a small fixed value:

```csharp
// Was: int closeR = (int)MathF.Ceiling(maxGap * 0.55f / coarseVS);
// Now: guided bridge handles connectivity; closing only smooths junctions
int closeR = 2;
```

`erodeR` stays proportional: `erodeR = Math.Max(1, (int)MathF.Round(closeR * config.CloseErodeFraction))`.

**File:** [SplintEngine.cs:742–743](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L742)

---

## 3. Vestibular Trim Bias Recentring

### 3.1 Current situation (problem)

Slider: **"Vestibular trim"**, `Minimum=−5`, `Maximum=10`, `Value=−3.2` (XAML default).

PHASE 1 clip formula:
```
maxVest = half + |lingualBuccalBiasMm| + Dil1 + VestBaseMargin + vestibularTrimMm
```

`VestBaseMargin = 3.0f`, `Dil1 = 1.0`. At `vestibularTrimMm = 0`, the clip allows `half + |bias| + 4mm` — 4mm beyond the horseshoe outer edge. The blue horseshoe ribbon sits at exactly `half + |bias|`. So `0` currently permits a 4mm vestibular overhang beyond the visible ribbon.

The default `−3.2` is an arbitrary offset that approximates trimming back to the horseshoe, but it is opaque and leaves a residual `~0.8mm` overhang (since `Dil1 + VestBaseMargin − 3.2 = 0.8`).

### 3.2 New behaviour

The horseshoe ribbon is the reference. `0` = trim at the horseshoe outer edge.

| Slider value | `maxVest` | Effect |
|---|---|---|
| `0` (default) | `half + \|bias\|` | Clip at horseshoe outer edge — splint matches the ribbon |
| `−2` | `half + \|bias\| − 2mm` | 2mm inside horseshoe — trims aggressively |
| `+3` | `half + \|bias\| + 3mm` | 3mm extra vestibular wall — for buccal flanges |

### 3.3 Engine formula change

**[SplintEngine.cs:658](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L658):**

```csharp
// Was:
float maxVest = half + MathF.Abs(lingualBuccalBiasMm) + (float)Dil1 + VestBaseMargin + vestibularTrimMm;

// New:
// VestBaseMargin and Dil1 are removed from the clip — at trimBias=0 the clip sits
// exactly at the horseshoe outer edge (half + |lingualBias|), matching the ribbon.
float maxVest = half + MathF.Abs(lingualBuccalBiasMm) + vestibularTrimBiasMm;
```

The same change applies to the PHASE 2a one-sided clip formula at [SplintEngine.cs:826](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L826):
```csharp
// Was:
float oneSidedMaxVest = half + MathF.Abs(lingualBuccalBiasMm) + (float)Dil1 + VestBaseMargin + vestibularTrimMm;
// New:
float oneSidedMaxVest = half + MathF.Abs(lingualBuccalBiasMm) + vestibularTrimBiasMm;
```

Also update the variable read at [SplintEngine.cs:371](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L371):
```csharp
float vestibularTrimBiasMm = config.VestibularTrimBiasMm;
```

### 3.4 Config rename

**[SplintConfig.cs:109](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintConfig.cs#L109):**

```csharp
// Was:
/// <summary>Extra vestibular clip margin (mm). 0 = clip at natural footprint ...</summary>
public float VestibularTrimMm   { get; init; } = 0f;

// New:
/// <summary>Vestibular trim bias (mm). 0 = clip at horseshoe outer edge (half + |lingualBias|).
/// Negative = trim more inward. Positive = extra vestibular wall beyond horseshoe.</summary>
public float VestibularTrimBiasMm { get; init; } = 0f;
```

### 3.5 UI changes

**[SplintPlannerWindow.xaml:169–172](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/SplintPlannerWindow.xaml#L169):**

```xml
<!-- Was: Text="Vestibular trim:" ... Value="-3.2" -->
<TextBlock Text="Vestibular trim bias:" FontSize="11" Foreground="#AABBCC"
    ToolTip="Bias relative to the horseshoe outer edge (the blue ribbon in the viewport).&#x0a;0 (default): trim exactly at the horseshoe outline — splint wall matches the ribbon.&#x0a;Negative: trim more inward (e.g. −2 = 2 mm inside the ribbon).&#x0a;Positive: extra vestibular wall beyond the ribbon (e.g. +3 mm for screw-fixation flanges)."/>
<Slider x:Name="VestibularTrimSlider" Minimum="-5" Maximum="10" Value="0"
        Width="170" SmallChange="0.25" ValueChanged="VestibularTrimSlider_ValueChanged"/>
<TextBlock x:Name="VestibularTrimLabel" Text="0.0 mm" FontSize="11" Foreground="#8CF"
           Margin="8,0,0,0" VerticalAlignment="Center"/>
```

**[SplintPlannerWindow.xaml.cs:638](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/SplintPlannerWindow.xaml.cs#L638):**

```csharp
// Was:
VestibularTrimMm    = (float)VestibularTrimSlider.Value,
// New:
VestibularTrimBiasMm = (float)VestibularTrimSlider.Value,
```

**[ArchSnapshot record (SplintPlannerWindow.xaml.cs:76)](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/SplintPlannerWindow.xaml.cs#L76):**

```csharp
// Was:  double VestibularTrim,
// New:  double VestibularTrimBias,
// Update all usages: preload line 116, snapshot capture line 84
```

---

## 4. Efficiency: O(1) Nearest-Arch Lookup

**Problem:** `NearestUpperInfo` and `NearestLowerInfo` do O(n) linear scans (n = SampleCount = 160) for every voxel. ~160M comparisons per arch per phase for a 1M-voxel grid.

**Solution:** Pre-compute two 2D lookup tables mapping each `(ix, iy)` grid cell → nearest arch sample index. Build once before PHASE 1.

```csharp
var upperIdxGrid = new int[gnx, gny];
var lowerIdxGrid = new int[gnx, gny];
for (int iy = 0; iy < gny; iy++)
    for (int ix = 0; ix < gnx; ix++)
    {
        float px = gox + ix * (float)VS_MC;
        float py = goy + iy * (float)VS_MC;
        upperIdxGrid[ix, iy] = FindNearestSample(upper, px, py);
        lowerIdxGrid[ix, iy] = FindNearestSample(lower, px, py);
    }
```

Inside the voxel loop, replace `NearestUpperInfo(px, py)` with:
```csharp
int ui = upperIdxGrid[ix, iy];
float uax = upper[ui].x, uay = upper[ui].y;
float unx = norU[ui].x,  uny = norU[ui].y;
// similarly for lower with lowerIdxGrid[ix, iy]
```

Cost: `gnx * gny * 160` ≈ 16K comparisons for a 100×100 grid — trivial one-time cost.
Savings: ~160× fewer per-voxel comparisons in PHASE 1.

**File:** [SplintEngine.cs:559–571](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L559) — Keep lambdas as fallback for PHASE 2a (no IX/IY indices there).

---

## 5. Minor Optimizations

| Change | File / Line | Why |
|--------|-------------|-----|
| Reduce blur passes 5 → 3 | [SplintEngine.cs:502](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.Core/Geometry/SplintEngine.cs#L502) | Guided bridge is smooth by construction |
| Remove `CrownMm=10.0` from Crop Z bounds; use arch Z ± penetration + Dil1 | [SplintEngine.cs:532](file:///c:/Users/Mirko/Documents/Orthoplanner/src/OrthoPlanner.App/SplintPlannerWindow.xaml.cs#L532) | Avoids including far-off skull triangles in the BVH |

---

## 6. Documentation Updates Required

**In [splint-planner-parameters.md](file:///c:/Users/Mirko/Documents/Orthoplanner/docs/splint-planner-parameters.md):**

- Rename "Vestibular trim (−5 to +10 mm, default 0)" → "Vestibular trim bias (−5 to +10 mm, default 0)"
- Rewrite description: `0` = horseshoe outer edge; negative = more inward; positive = extra wall
- Rewrite "Pipeline effect" to show new formula: `maxVest = half + |bias| + vestibularTrimBiasMm`
- Add new "PHASE 1 (guided bridge)" plain-language subsection
- Update PHASE 2 description: closing now only smooths junctions, `closeR = 2` (fixed)
- Update pipeline overview diagram to show guided bridge as step inside PHASE 1

---

## 7. Verification Checklist

1. **Vestibular trim bias = 0**: Generate a splint and view it from below. The buccal wall should align with the horseshoe ribbon in the arch placement viewport. No protrusion past the ribbon line.
2. **Vestibular trim bias = +2**: Buccal wall extends 2mm beyond ribbon. Ribbon is visibly narrower than the splint on the buccal side.
3. **Vestibular trim bias = −2**: Buccal wall sits 2mm inside the ribbon. No tooth anatomy outside that boundary is included.
4. **Guided bridge no-hole test**: Autorotation ≥6°. Generate splint. Bridge region has no holes — full Z-connectivity.
5. **Timing**: `splint_trace.txt` before/after — PHASE 2 `closeR` trivial. PHASE 1 nearest-arch ~160× faster.
6. **Regression**: Normal case (small gap) same visual quality as before.

---

## Resolved Design Decisions

1. **`BridgeSdfBaseMm` default stays at `0.6 mm`.** Adequate for most cases; can be raised later if bridge holes appear in extreme-gap cases.

2. **PHASE 2a (one-sided buccal clip) runs unchanged.** Guided bridge voxels are constrained to the corridor (`|signed_perp − bias| <= half`), so they are already within the horseshoe. PHASE 2a clips residual SDF-dilation spill-over only and does not need to be aware of guided bridge voxels.
