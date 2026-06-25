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

### Extra bridge thickness (0 to 10 mm, default 0)

Additional material added to the bridge region — the gap between the upper and lower arch portions where neither dilated tooth mesh reaches. After GPU morphological closing bridges this gap, this slider applies an extra dilation pass to thicken the bridge.

- **0 mm (default):** Only the standard closing radius — the bridge is as thin as the closing kernel allows (typically 1–2 mm).
- **+2 mm:** Adds 2 mm of material around the closed gap region. Use when the standard bridge would be too fragile for handling, screw retention, or when the inter-arch gap is large and the closing alone produces a thin web.
- **+5 mm:** Very thick bridge — for cases with wide surgical gaps or when a thick occlusal splint is planned.

**Pipeline effect:** After `GpuMorphology3D.Close()`, applies `GpuMorphology3D.Dilate(closed, dilateR)` where `dilateR = ceiling(bridgeThicknessMm / coarseVS)`.

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

## Pipeline overview (for developers)

The SDF-CSG pipeline runs in this order:

1. **PHASE 1** — Bake blank SDF: `Min(upper_1mm, lower_1mm)` with Z-clip (upper/lower penetration sliders) and posterior clip. Continuous SDF values.
2. **PHASE 1b** — BFS floater removal from arch-midpoint seed.
3. **PHASE 2** — GPU morphological closing (bridges inter-arch gap). Bridge thickness slider → extra dilation.
4. **PHASE 2b** — SDF smoothing (r=2, 3-pass box blur).
5. **PHASE 3** — Optional undercut blockout (engagement depth slider → Z-plane clamp). Only when Block out undercuts = ON.
6. **PHASE 4** — Pocket subtraction: `Max(blankVal, Max(−upper_0.1mm, −lower_0.1mm))`.
7. **PHASE 5** — Marching Cubes at 0.2mm + largest-component cleanup + optional manifold repair.
