namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Automatic profile landmarks (initialisation only, never clicked), in ORIGINAL image pixels.</summary>
public sealed record ProfileLandmarks((double X, double Y) Nasion, (double X, double Y) Pronasale, (double X, double Y) Menton);

/// <summary>Quality of one registration direction. All residuals in millimetres.</summary>
/// <param name="PhotoPixelsPerMm">Photo original pixels per X-ray millimetre found by this solution.</param>
/// <param name="RotationDeg">Rotation of the X-ray relative to the photo (degrees, counter-clockwise in image axes).</param>
/// <param name="RmsBest80Mm">RMS of the best 80% residuals.</param>
/// <param name="MatchedPoints">Correspondences used for the residuals (end-of-curve matches excluded).</param>
/// <param name="ZoneMedianMm">Median residual in the upper / middle / lower third of the X-ray ROI by arc length
/// (roughly nose, lips, chin); NaN when a zone has no matches.</param>
public sealed record SolutionReport(
    double PhotoPixelsPerMm,
    double RotationDeg,
    double RmsBest80Mm,
    double MedianMm,
    double P95Mm,
    int MatchedPoints,
    IReadOnlyList<double> ZoneMedianMm);

/// <summary>
/// Outcome of the photo-cephalogram profile registration. On failure <see cref="FailureReason"/> is set and
/// <see cref="PhotoPixelsPerMm"/> is NaN; the solutions computed so far (if any) are kept for diagnosis.
/// </summary>
public sealed record ProfileRegistrationResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>FINAL value (symmetric solution), photo ORIGINAL pixels per mm. NaN on failure.</summary>
    public double PhotoPixelsPerMm { get; init; } = double.NaN;

    /// <summary>
    /// Rotation of the X-ray relative to the photo (symmetric solution), degrees, measured in the internal frame where
    /// both profiles face +X (for a mirrored photo the sign in raw photo coordinates is opposite).
    /// </summary>
    public double RotationDeg { get; init; } = double.NaN;

    /// <summary>True when photo and X-ray face opposite sides (the photo was mirrored internally).</summary>
    public bool PhotoMirrored { get; init; }

    /// <summary>
    /// 2x3 affine, X-ray ORIGINAL px -> photo ORIGINAL px (mirror included), row-major [a b tx; c d ty].
    /// From the symmetric solution. Kept when only a quality gate failed (to show the bad overlay), empty when the
    /// registration could not be computed. Never use it for measurements when <see cref="Success"/> is false.
    /// </summary>
    public IReadOnlyList<double> XrayToPhotoMatrix { get; init; } = Array.Empty<double>();

    public SolutionReport? PhotoToXray { get; init; }
    public SolutionReport? XrayToPhoto { get; init; }
    public SolutionReport? Symmetric { get; init; }

    /// <summary>(XrayToPhoto scale / PhotoToXray scale - 1) * 100.</summary>
    public double ScaleDifferenceAvsBPercent { get; init; } = double.NaN;

    /// <summary>X-ray ROI points mapped by A (X-ray to photo) then by B (photo to X-ray): distance from start.</summary>
    public double RoundTripMeanMm { get; init; } = double.NaN;
    public double RoundTripMaxMm { get; init; } = double.NaN;

    /// <summary>Block-bootstrap 95% interval of the symmetric scale.</summary>
    public double ScaleCi95LowPxPerMm { get; init; } = double.NaN;
    public double ScaleCi95HighPxPerMm { get; init; } = double.NaN;

    public ProfileLandmarks? PhotoLandmarks { get; init; }
    public ProfileLandmarks? XrayLandmarks { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
