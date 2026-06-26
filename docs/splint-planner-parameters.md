# Splint Planner — Parameter Reference

## SEQUENCE

### Maxilla-first / Mandible-first splint

Determines which jaw is the *mobile* segment in the surgical plan. This is **labelling only** — it does not change splint geometry. The geometry depends on the actual positions of the upper/lower meshes at generation time.

- **Maxilla-first** (default): The intermediate wafer indexes the repositioned maxilla to the unmoved mandible. The final wafer indexes both jaws in planned occlusion.
- **Mandible-first**: The intermediate wafer indexes the repositioned mandible to the unmoved maxilla.

---

## UPPER ARCH

### Upper crown envelope (−5 to +10 mm, default +2)

How far the splint body extends **above** the upper arch curve. Controls the depth of the maxillary tooth pocket.

- **+2 mm (default):** The splint wraps 2 mm up around the maxillary crowns. Deep tooth pocket, good retention.
- **0 mm:** The top surface is flush with the arch curve at the cusp tips. Minimal pocket.
- **−2 mm:** The top is trimmed 2 mm inside the cusp line. Produces a thin bite-rim; pockets only exist where the SDF dilation still covers.

**Pipeline effect:** Sets the upper Z-clip plane to `NearestUpperZ + value`. The tooth pocket itself is always carved by the 0.1mm-offset tooth SDF — this slider only controls how much vertical *body* the splint has above the arch.

---

## LOWER ARCH

### Lower crown envelope (−5 to +10 mm, default +2)

How far the splint body extends **below** the lower arch curve. Controls the depth of the mandibular tooth pocket. Same semantics as the upper slider, mirrored.

- **+2 mm (default):** The splint wraps 2 mm down around the mandibular crowns.
- **0 mm:** Flush with the lower cusp tips.

**Pipeline effect:** Sets the lower Z-clip plane to `NearestLowerZ − value`.

---

## GENERAL

### Labio-lingual width (2 to 20 mm, default 8)

Total thickness of the splint from the lingual (tongue-side) surface to the buccal (cheek-side) surface, measured perpendicular to the arch tangent.

- **8 mm (default):** Standard surgical wafer. Fully covers the occlusal table of most arches.
- **4 mm:** Narrow wafer — may not engage all cusps on wide arches.
- **12+ mm:** Wide splint with thick buccal and lingual walls — for cases needing extra rigidity or fixation hardware.

**Pipeline effect:** Sets the XY clip-pad radius around the arch curve. The SDF blank and MC bounding box only extend this far from the arch center in the labio-lingual direction. The arch curve itself defines the centerline; half this width extends in each direction.

### Lingual-Buccal bias (−5 to +5 mm, default 0)

Shifts the splint center **buccally** (+) or **lingually** (−) while keeping the total labio-lingual width constant. The wall on the biased side gets thicker and the opposite side gets thinner by the same amount.

- **0 mm (default):** Splint is centered on the arch curve.
- **+2 mm:** 2 mm more material on the buccal side, 2 mm less on the lingual side. Useful when the buccal wall needs extra thickness for screw fixation holes.
- **−2 mm:** 2 mm more lingual material. Useful for lingual-bonded splints.

**Pipeline effect:** Offsets the arch centerline in the XY plane by `biasMm * normalDirection` before computing the clip bounds.

### Vestibular trim (−5 to +10 mm, default 0)

Controls how far the vestibular (buccal) wall extends beyond the arch outline. This is a two-purpose control — it clips both the PHASE 1 blank and, when one-sided mode is on, the post-closing bridge material.

- **0 (default):** The wall extends to the natural footprint (half-width + bias + 1mm dilation + 3mm base margin for molar anatomy). Bracket wrapping that protrudes beyond the tooth outline gets trimmed.
- **−3:** Trim 3 mm more aggressively — removes bracket bumps on the buccal wall.
- **+2:** Allow 2 mm wider vestibular walls — for wider buccal flanges or screw fixation.

**Pipeline effect (PHASE 1):** The XY clip checks perpendicular distance from the nearest arch point. Voxels beyond `halfWidth + |bias| + 1mm + 3mm + vestibularTrimMm` from *both* arches are set to air.

### One-sided vestibular clip — checkbox (default OFF)

When ON, an additional XY clip runs **after** morphological closing (PHASE 2) but **before** blur (PHASE 2b). Unlike the PHASE 1 clip which is symmetric (both buccal and lingual sides, both arches), this clip only removes material on the **buccal side** of the **more-anterior arch**.

The algorithm auto-detects which arch is more vestibular by comparing how far each arch's points extend in their own outward-normal direction from the combined centroid. Typically this is the upper (maxillary) arch.

- **OFF (default):** Only the PHASE 1 all-around clip is active.
- **ON:** After closing fills the bridge gap, any material that extends too far on the buccal side of the more-anterior arch is removed. The lingual wall is untouched. Uses the same vestibular trim margin.

**Pipeline effect (PHASE 2a):** Computes *signed* perpendicular distance to the more-vestibular arch (positive = buccal, negative = lingual). Voxels with `signedDist > maxVest` are set to air. Lingual-side voxels (negative signed distance) are never clipped.

### Extra bridge thickness (0 to 10 mm, default 0)

Additional material added to the bridge region — the gap between the upper and lower arch portions where neither dilated tooth mesh reaches.

- **0 mm (default):** Only the base SDF depth (see Bridge SDF depth below) — the bridge is as thin as the closing and blur allow.
- **+2 mm:** Adds 2 mm of SDF depth in the bridge region. Use when the standard bridge would be too fragile for handling or screw retention.
- **+5 mm:** Very thick bridge — for cases with wide surgical gaps.

**Pipeline effect:** Bridge voxels (filled by closing but originally air) are written at `−(bridgeSdfBaseMm + bridgeThicknessMm)` instead of the natural SDF value. This pushes the iso-surface outward, adding material only in the bridge region. Original blank voxels are untouched.

### Closing erosion (10% to 100%, default 45%)

How aggressively the morphological closing step erodes after dilation. The closing operation is always *dilate then erode*; this slider controls the erosion radius as a fraction of the dilation radius.

- **100% (symmetric):** Outer surfaces are cleaned back to their original shape, but the thin bridge region is eaten away aggressively. Use when the bridge is thick enough to survive.
- **45% (default, asymmetric):** The erosion only removes 45% as much material as the dilation added. The bridge stays thicker because less material is taken back. Outer walls are slightly rounder.
- **20%:** Very light erosion — fat bridge, very round outer walls. Almost no material removed after dilation.

**Pipeline effect:** `erodeRadius = closeR × (sliderValue / 100)`. Lower values preserve more of the dilated material in the bridge region.

### Bridge SDF depth (0 to 5 mm, default 0.5)

How deeply "inside" the splint the bridge voxels are written in the SDF grid. Deeper values make the bridge more resistant to the smoothing blur.

The smoothing blur averages each SDF voxel with its neighbors. Shallow bridge voxels (SDF near zero) get averaged with surrounding air (SDF = 1.0) and can be dragged positive, making the iso-surface disappear → holes in the bridge. Deeper values have more interior margin and survive the blur.

- **0.5 mm (default):** Minimal interior margin. Bridge stays thin but may develop pinholes with aggressive smoothing.
- **1.0–2.0 mm:** Bridge survives more blur passes. The splint thickens slightly in the bridge region only.
- **3+ mm:** Very deep — bridge is robust but the inter-arch gap fills with material; outer walls are unaffected (tooth pockets carve them back).

**Pipeline effect:** Bridge voxels are written at `−bridgeSdfBaseMm` base depth (plus any extra bridge thickness). This only affects voxels that were air before closing and got filled by the closing operation — original blank voxels keep their natural SDF gradient.

---

## CLINICAL FIT

### Block out undercuts — checkbox (default OFF)

When **ON**, forces every tooth pocket to go vertical past the engagement depth, eliminating undercuts that would prevent the splint from seating or being removed. When **OFF**, pockets follow natural tooth anatomy (including undercuts).

- **OFF (default):** Pockets follow the actual tooth surface as defined by the 0.1mm-offset SDF. Maximum anatomical fidelity and retention, but insertion/removal may require flex or a specific path.
- **ON:** Pockets are clamped to vertical below the engagement plane. The splint seats and releases on a straight pull path. Required for rigid, non-flex splints used intraoperatively.

**Pipeline effect (PHASE 3):** For each inside-voxel that is inside the tooth SDF and beyond `engagementDepthMm` from the arch-Z plane: force the SDF value to `−0.1` (splint material) before pocket subtraction runs.

### Engagement depth (0 to 5 mm, default 1.5)

Only effective when "Block out undercuts" is ON. How far **past the arch-curve Z plane** the pocket is allowed to engage the tooth geometry before being blocked out to vertical.

- **1.5 mm (default):** Pockets engage 1.5 mm past the height of contour (the arch curve), then go vertical. Good compromise — some undercut retention, still seatable.
- **0 mm:** No undercut engagement at all — pockets are vertical from the arch plane down. Maximum seatability, minimum retention.
- **3 mm:** Deep undercut engagement before blockout kicks in. High retention, but insertion requires more force or a hinged path.

**Pipeline effect:** The Z-plane threshold is `archZ − engagementDepthMm` for upper teeth and `archZ + engagementDepthMm` for lower teeth. Voxels inside the tooth beyond this plane are forced to splint material.

### Buccal flange depth (0 to 15 mm, default 0)

Apical extension of the splint skirt down (or up) the buccal wall of the jaw.

- **0 mm (default):** No flange — the splint covers only the occlusal table.
- **5 mm:** A 5 mm tall buccal skirt extending apically from the splint body. Provides surface area for screw fixation through the flange.
- **10+ mm:** Long flange approaching the vestibule depth — for rigid fixation splints.

**Flange target:** Use the **"on upper"** / **"on lower"** radio buttons to select which arch's buccal wall gets the flange.

> **Note:** Flange geometry is not yet implemented in the SDF-CSG pipeline. This slider is a placeholder that stores the intended value for future implementation.

---

## How the algorithm works (plain-language explanation)

Think of the splint as a wafer — a thin piece of plastic that sits between the upper and lower teeth, with pockets (holes) carved into it so each tooth nests into place. The algorithm builds it in stages:

### PHASE 1 — Pour the clay between the teeth

Imagine filling the entire space between the two dental arches with a solid block of clay. Then, wherever a tooth exists, subtract it — carve a pocket so the clay wraps around the tooth anatomy. What's left is a rough splint shape with tooth-shaped indentations on the top and bottom.

More precisely: the algorithm creates a 3D grid (think: Minecraft blocks, but much smaller — 0.2mm each). Each grid cell stores a number: negative means "inside the splint", positive means "outside (air)", and zero is the surface itself. This is a signed distance field (SDF). The blank is formed by taking the *minimum* of the upper-tooth SDF and lower-tooth SDF — the splint is solid wherever *either* arch's 1mm-dilated tooth surface says it should be.

The Z-clip cuts off the top and bottom of the block at the arch surfaces (plus whatever penetration the user chose). The vestibular XY-clip trims any material that extends too far from the arch centerline. The posterior clip cuts off anything behind the last molar.

**The arch normals:** At every point along the arch curve, the algorithm computes a "normal" — an arrow pointing outward (away from the center of the horseshoe, toward the cheek/buccal side). These normals are used for all XY clipping: the perpendicular distance from any voxel to the arch tells us whether that voxel is within the intended splint wall.

### PHASE 1b — Remove floaters

Sometimes tiny disconnected blobs of solid appear far from the real splint (artifacts from the SDF sampling). A flood-fill from the arch midpoint kills any solid region that isn't connected to the main body.

### PHASE 2 — Bridge the gap (morphological closing)

The problem: between the upper and lower arches there's empty space where no tooth surface exists. The block of "clay" has a hole there. Morphological closing fills it:

1. **Dilate** — inflate all solid regions outward (imagine the clay swelling up by a few voxels in every direction). The upper and lower halves grow toward each other and merge, closing the gap.
2. **Erode** — shrink everything back inward by a smaller radius. This restores the outer surfaces to roughly their original shape.

Because the erosion uses a *smaller* radius than the dilation (the "Closing erosion" slider, default 45%), not all the swollen material gets taken back. The bridge — the gap between the two halves — stays thicker than it would with symmetric 100% erosion.

**Why asymmetric?** Symmetric closing (100% erosion) looks great on the outer walls but eats through the thin bridge region. The bridge is fragile: it's a narrow connection that the erosion removes just as aggressively as it removes the puffy outer walls. Asymmetric erosion preserves the bridge at the cost of slightly rounder outer surfaces.

**Bridge SDF depth:** When writing the newly-bridged voxels back into the fine grid, they get an SDF value of `−(bridgeSdfBaseMm + bridgeThicknessMm)`. A deeper (more negative) value means the voxel is "more inside" the splint. The smoothing blur in the next step averages voxels with their neighbors — shallow interior values get averaged away with surrounding air, punching holes. Deeper values survive the blur.

### PHASE 2a — One-sided vestibular clip (optional)

When "One-sided (buccal only)" is checked, the algorithm goes back through the grid and removes any material that sticks out too far on the *buccal side* of the more-anterior arch. The lingual side (toward the tongue) is never touched.

This runs **after** closing (which can deposit material beyond the arch outline on the vestibular side) and **before** blur (so the clipped edges get smoothed too).

The algorithm auto-detects the more-vestibular arch by measuring which arch's points, on average, extend farther outward in their own normal direction from the combined centroid of both arches. Usually this is the maxilla.

### PHASE 2b — Smooth the surface (blur)

A box blur (3-voxel radius, 5 passes) smooths the SDF grid. This removes the voxel stair-stepping — without it, the 3D-printed splint has visible ridges where each 0.2mm grid layer steps in or out.

The blur averages each voxel with its neighbors, so thick regions (deep negative SDF) stay solid, but thin or borderline regions (SDF near zero) can get dragged toward positive → the iso-surface disappears there → holes. This is why the bridge SDF depth matters.

**Pockets stay sharp:** The blur only smooths the *blank* (the outer walls of the splint). Pockets are carved *after* the blur (PHASE 4), so tooth anatomy is preserved at full resolution.

### PHASE 3 — Block out undercuts (optional)

If "Block out undercuts" is ON: for each voxel that is inside a tooth and below the engagement-depth plane, force it to be splint material instead. This makes pockets go vertical below the engagement depth, eliminating undercuts that would trap the splint on the teeth.

### PHASE 4 — Carve the tooth pockets

For every voxel in the grid, subtract the tooth if the 0.1mm-cleared tooth SDF says this point is inside a tooth: `sdfValue = Max(blankSDF, Max(−upper_0.1mm, −lower_0.1mm))`. The negation flips "inside the tooth" to "outside the splint" — wherever a tooth exists, the splint has a pocket.

The 0.1mm clearance means the printed splint has a tiny gap around each tooth — just enough for seating without being loose.

### PHASE 5 — Extract the mesh (Marching Cubes)

The SDF grid is converted to a triangle mesh using Marching Cubes at the zero-crossing (where `sdfValue = 0`). Only the largest connected component is kept (tiny disconnected fragments are discarded). The result is the final splint mesh ready for 3D printing.

---

## Pipeline overview (for developers)

```
User places arch points on 3D models
        │
        ▼
PHASE 1: Bake blank SDF
   Min(upper_1mm, lower_1mm)
   + Z-clip (upper/lower penetration)
   + vestibular XY-clip (both arches, both sides)
   + posterior clip (behind last molar)
        │
        ▼
PHASE 1b: BFS floater removal (from arch-midpoint seed)
        │
        ▼
PHASE 2: GPU morphological closing
   Dilate(closeR) → Erode(erodeR)
   erodeR = closeR × CloseErodeFraction
   Bridge voxels written at −(BridgeSdfBaseMm + BridgeThicknessMm)
        │
        ▼
PHASE 2a: One-sided vestibular clip (if enabled)
   Signed perpendicular distance to more-vestibular arch
   Only buccal side clipped; lingual untouched
        │
        ▼
PHASE 2b: SDF smoothing
   Separable box blur: r=3, 5 passes
   Blank walls smooth; pockets carved later stay sharp
        │
        ▼
PHASE 3: Undercut blockout (if enabled)
   Force SDF = −0.1 below engagement-depth plane
        │
        ▼
PHASE 4: Pocket subtraction
   Max(blankSDF, Max(−upper_0.1mm, −lower_0.1mm))
        │
        ▼
PHASE 5: Marching Cubes + largest-component cleanup
   Extract triangle mesh at iso-zero
        │
        ▼
Final splint mesh (triangle soup, ready for export/3D print)
```

### Key data flows

| Data | Source | Used by |
|------|--------|---------|
| Arch points `(x, y, z)` | User click-on-mesh | Z-clip, XY-clip, ribbon preview |
| Arch normals `(nx, ny)` | `ComputeNormals()` — perpendicular to arch tangent, pointing outward from centroid | XY-clip (signed distance), lingual-buccal bias, horseshoe ribbon |
| Tooth SDF (1mm dilated) | Mesh → SDF | PHASE 1 blank |
| Tooth SDF (0.1mm cleared) | Same mesh, less dilation | PHASE 4 pocket subtraction |
| Coarse binary grid | Downsampled from PHASE 1 SDF | PHASE 2 GPU morphological closing |
| Closed coarse grid | `GpuMorphology3D.Close()` | Written back to fine grid as bridge SDF bias |

### GPU morphological operations

`GpuMorphology3D` uses a **sphere** structuring element (`dx² + dy² + dz² ≤ r²`) for both dilation and erosion. This avoids the axis-aligned corner artifacts that a cube structuring element would imprint on the splint surface.

- **Dilation:** A voxel becomes solid if *any* voxel within the sphere is solid. Grows regions outward.
- **Erosion:** A voxel stays solid only if *all* voxels within the sphere are solid. Shrinks regions inward.
- **Closing (dilate→erode):** Fills small gaps. Bridges the inter-arch gap.
- **Opening (erode→dilate):** Removes thin protrusions. Not currently used.
