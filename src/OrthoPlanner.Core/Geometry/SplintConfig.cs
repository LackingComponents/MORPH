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
    public float LabiolingualMm     { get; init; } = 7.5f;
    public float UpperPenetrationMm { get; init; } = 1.5f;   // + = deeper into upper teeth
    public float LowerPenetrationMm { get; init; } = 1.5f;   // + = deeper into lower teeth
    public float LingualBuccalBiasMm{ get; init; } = 1f;   // + buccal, − lingual
    public float BridgeThicknessMm  { get; init; } = 0f;
    public int   SampleCount        { get; init; } = 160;

    // ── Step 4: engagement depth / undercut blockout ───────────────────────
    /// <summary>How far past each tooth's height of contour the pocket is allowed to
    /// engage (i.e. how deep the wafer wraps over the crowns for retention). The
    /// pocket below this is blocked out so the wafer can seat. This controls CROWN
    /// WRAP / retention only — the fit clearance is set by <see cref="IntaglioOffsetMm"/>.</summary>
    public float EngagementDepthMm  { get; init; } = 1.5f;
    public bool  BlockoutUndercuts  { get; init; } = true;

    /// <summary>Explicit uniform clearance (gap) between the intaglio (impression)
    /// surface and the teeth, applied identically on occlusal contacts and walls —
    /// equivalent to a CAD "offset shell". 0 = zero-gap tight contact; ~0.15–0.3 mm
    /// mimics commercial milled/printed wafers (KLS-style). Replaces the old
    /// relief-via-engagement behaviour.</summary>
    public float IntaglioOffsetMm   { get; init; } = 0.2f;

    // ── Mesh resolution / surface treatment ────────────────────────────────
    /// <summary>Voxel size (mm) for the signed-distance field and marching-cubes
    /// grids. Smaller = finer cusp detail but more memory/time. 0.2 mm is the
    /// balanced default; 0.1 mm is high detail.</summary>
    public float VoxelSizeMm        { get; init; } = 0.2f;
    /// <summary>Number of box-blur passes applied to the voxel field before meshing
    /// (smooths facets). 0 = none.</summary>
    public int   SmoothingPasses    { get; init; } = 2;
    /// <summary>When true, the smoothing pass is skipped on the tooth-pocket
    /// (intaglio) voxels so cusp tips and fissures stay crisp while the outer wafer
    /// surface is still smoothed.</summary>
    public bool  PreserveIntaglioDetail { get; init; } = true;

    /// <summary>Trim this many millimetres of arc length off each posterior end of
    /// the wafer, shortening the footprint toward a commercial-style outline so an
    /// over-retentive splint is easier to seat/remove. 0 = no trim.</summary>
    public float PosteriorTrimMm    { get; init; } = 0f;

    /// <summary>Target edge length (mm) for quadric decimation of the exported mesh.
    /// Quadric reduction keeps triangles where curvature is high (cusps) and
    /// simplifies flat regions, giving sane STL file sizes without losing detail.
    /// 0 = no decimation (full marching-cubes density).</summary>
    public float ExportDecimateEdgeMm { get; init; } = 0.3f;

    /// <summary>Recommended validated print material for the produced wafer
    /// (informational; STL carries no material metadata).</summary>
    public string RecommendedPrintMaterial { get; init; } =
        "Class IIa biocompatible photopolymer (e.g. NextDent / Dental LT clear, light-cured acrylate)";

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
