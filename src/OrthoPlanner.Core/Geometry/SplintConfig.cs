using System;
using System.Collections.Generic;

namespace OrthoPlanner.Core.Geometry;

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  Splint clinical configuration + result contracts
//
//  The generator is POSE-AGNOSTIC: it builds a wafer in the gap between whatever
//  upper and lower surfaces it is handed, at their current positions. The clinical
//  meaning — which jaw is mobile, whether this is the intermediate or final wafer,
//  single- vs bi-maxillary — is decided UPSTREAM (the caller positions the mobile
//  arch before generation) and carried here purely as configuration + labelling.
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>Which wafer in the surgical sequence this splint represents.</summary>
public enum SplintType
{
    /// <summary>Intermediate wafer: indexes the first-operated jaw to the un-operated one.</summary>
    Intermediate,
    /// <summary>Final wafer: indexes both jaws in their planned post-operative occlusion.</summary>
    Final
}

/// <summary>Which jaw is repositioned by the surgery (the mobile segment).</summary>
public enum MobileJaw { Maxilla, Mandible }

/// <summary>Surgical scope — drives clinical labelling, not wafer geometry (a wafer
/// always seats between both arches regardless of how many jaws are operated).</summary>
public enum JawScope { Bimaxillary, MaxillaOnly, MandibleOnly }

/// <summary>Bounding box around a condyle in model coordinates.</summary>
public sealed record CondyleBox(
    float CenterX, float CenterY, float CenterZ,
    float HalfExtentX, float HalfExtentY, float HalfExtentZ)
{
    public (float x, float y, float z) Center => (CenterX, CenterY, CenterZ);
}

/// <summary>
/// A drilled hole the user wants in the splint, as a first-class protected void.
/// Position is the entry point in model space; the hole is a cylinder of the given
/// diameter running along (DirX,DirY,DirZ). Protected holes are never closed by
/// min-thickness enforcement or manifold repair.
/// </summary>
public sealed record SplintHole(
    HoleKind Kind,
    float X, float Y, float Z,
    float DiameterMm,
    float DirX, float DirY, float DirZ,
    float DepthMm = 0f)   // 0 = drill fully through the body
{
    public (float x, float y, float z) Position => (X, Y, Z);

    public (float x, float y, float z) Direction
    {
        get
        {
            float len = MathF.Sqrt(DirX * DirX + DirY * DirY + DirZ * DirZ);
            return len < 1e-6f ? (0, 0, 1) : (DirX / len, DirY / len, DirZ / len);
        }
    }
}

/// <summary>Clinical role of a protected hole — affects default diameter and labelling.</summary>
public enum HoleKind
{
    /// <summary>Screw hole through a buccal flange for rigid fixation.</summary>
    Fixation,
    /// <summary>Window to verify seating / occlusal contact.</summary>
    Inspection,
    /// <summary>Channel for saline irrigation / debris clearance.</summary>
    Irrigation,
    /// <summary>Anterior fenestration for tongue/airway or intra-op visibility.</summary>
    AnteriorFenestration
}

/// <summary>
/// Complete configuration for one splint generation. All geometric defaults match
/// the previous loose-parameter behaviour so existing call sites are unchanged in
/// spirit; new clinical/feature fields are opt-in.
/// </summary>
public sealed record SplintConfig
{
    // ── Clinical (labelling + upstream-resolved pose) ──────────────────────
    public SplintType Type          { get; init; } = SplintType.Final;
    public MobileJaw  FirstOperated { get; init; } = MobileJaw.Maxilla;
    public JawScope   Scope         { get; init; } = JawScope.Bimaxillary;

    /// <summary>True when the dental surfaces came from registered intraoral scans
    /// (clinical-grade) rather than the CT bone fallback.</summary>
    public bool FromIntraoralScans { get; init; } = true;

    // ── Core wafer geometry (previously loose parameters) ──────────────────
    public float LabiolingualMm     { get; init; } = 8f;
    public float UpperPenetrationMm { get; init; } = 0f;   // + = deeper into upper teeth
    public float LowerPenetrationMm { get; init; } = 0f;   // + = deeper into lower teeth
    public float LingualBuccalBiasMm{ get; init; } = 0f;   // + buccal, − lingual
    public float BridgeThicknessMm  { get; init; } = 0f;
    /// <summary>Erosion radius as fraction of dilation radius in morphological closing.
    /// 1.0 = symmetric (sharp outer walls, thin bridge). Lower = thicker bridge, puffier walls.</summary>
    public float CloseErodeFraction  { get; init; } = 0.45f;
    /// <summary>Base SDF depth for bridge voxels (mm). Deeper = bridge survives more blur
    /// but the splint gets thicker. Shallow = blur can punch holes in the bridge.</summary>
    public float BridgeSdfBaseMm     { get; init; } = 0.5f;
    /// <summary>Extra vestibular clip margin (mm). 0 = clip at natural footprint
    /// (halfWidth + |bias| + 1mm dilation). Negative = trim more (removes bracket
    /// wrapping). Positive = allow wider vestibular walls.</summary>
    public float VestibularTrimMm   { get; init; } = 0f;
    /// <summary>When true, an additional one-sided XY clip runs after PHASE 2 closing
    /// (before blur) that removes splint material on the buccal side of the more-
    /// anterior arch only. Uses the same VestibularTrimMm threshold.</summary>
    public bool  VestibularOneSided { get; init; } = false;
    public int   SampleCount        { get; init; } = 160;

    // ── Step 4: engagement depth / undercut blockout ───────────────────────
    /// <summary>How far past each tooth's height of contour the pocket is allowed to
    /// engage. The pocket below this is blocked out so the wafer can seat.</summary>
    public float EngagementDepthMm  { get; init; } = 1.5f;
    public bool  BlockoutUndercuts  { get; init; } = false;

    // ── Incidental-perforation policy ──────────────────────────────────────
    // Splints may intentionally include holes/windows, so thickness is not
    // enforced by default. These fields remain only for compatibility with
    // older project/config data.
    public float MinThicknessMm     { get; init; } = 0f;
    public bool  EnforceMinThickness{ get; init; } = false;
    public bool  FlagIncidentalPerforations { get; init; } = true;

    // ── Condylar autorotation ──────────────────────────────────────────────
    /// <summary>When true, rotate the mandible open around the condylar axis before
    /// generating the wafer so the inter-arch space can receive the requested splint.</summary>
    public bool EnableAutorotation { get; init; } = true;
    /// <summary>Optional target clearance between sampled upper/lower arch curves.
    /// Values <= 0 disable automatic clearance enforcement.</summary>
    public float AutorotationMinClearanceMm { get; init; } = 0f;
    /// <summary>Safety cap for automatic mandibular opening.</summary>
    public float AutorotationMaxDegrees { get; init; } = 8f;
    public CondyleBox? LeftCondyleBox { get; init; }
    public CondyleBox? RightCondyleBox { get; init; }

    // ── Step 6: buccal flange + fixation/protected holes ───────────────────
    /// <summary>Apical depth of the buccal flange skirt; 0 = no flange.</summary>
    public float BuccalFlangeDepthMm{ get; init; } = 0f;
    /// <summary>True = flange on the maxillary (upper) buccal wall, false = mandibular.</summary>
    public bool  FlangeOnUpper      { get; init; } = true;
    public IReadOnlyList<SplintHole> Holes { get; init; } = Array.Empty<SplintHole>();

    // ── Step 7: manifold guarantee ─────────────────────────────────────────
    public bool  GuaranteeManifold  { get; init; } = true;

    /// <summary>Human-readable name used for the produced mesh and window title.</summary>
    public string DisplayName
    {
        get
        {
            string kind = Type == SplintType.Intermediate ? "Intermediate" : "Final";
            string scope = Scope switch
            {
                JawScope.MaxillaOnly  => " (Maxilla)",
                JawScope.MandibleOnly => " (Mandible)",
                _ => string.Empty
            };
            string seq = FirstOperated == MobileJaw.Maxilla ? " [Maxilla-first]" : " [Mandible-first]";
            return $"{kind} Splint{scope}{seq}";
        }
    }
}

/// <summary>
/// Output of a splint generation: the watertight (where possible) triangle soup
/// plus diagnostics the UI surfaces to the surgeon before printing.
/// </summary>
public sealed record SplintResult(
    float[] Vertices,
    bool    IsManifold,
    float   OpenEdgeFraction,
    int     IncidentalPerforations,
    IReadOnlyList<string> Warnings)
{
    public int TriangleCount => Vertices.Length / 9;

    public static SplintResult Empty(string reason) =>
        new(Array.Empty<float>(), false, 1f, 0, new[] { reason });
}
