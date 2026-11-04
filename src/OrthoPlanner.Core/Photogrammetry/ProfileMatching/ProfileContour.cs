namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>How the facing direction of the profile is decided.</summary>
public enum FacingOverride
{
    /// <summary>Detect from the strongest protrusion (nose).</summary>
    Auto,
    /// <summary>Nose points to +X.</summary>
    Right,
    /// <summary>Nose points to -X.</summary>
    Left,
}

/// <summary>
/// Anterior soft-tissue profile line (forehead to neck) extracted from a photo or a radiograph.
/// </summary>
/// <param name="Points">ORIGINAL image pixel coordinates, ordered top to bottom, uniform arc length.</param>
/// <param name="Confidence">Per-point 0..1 (normalised local gradient magnitude); same length as Points.</param>
/// <param name="FacingRight">True when the nose points to +X.</param>
/// <param name="Mirrored">Reserved for registration; always false from the extractor.</param>
/// <param name="Diagnostics">Thresholds, working scale, component sizes and warnings.</param>
public sealed record ProfileContour(
    IReadOnlyList<(double X, double Y)> Points,
    IReadOnlyList<double> Confidence,
    bool FacingRight,
    bool Mirrored,
    IReadOnlyList<string> Diagnostics);

/// <summary>Outcome of a profile extraction. On failure <see cref="FailureReason"/> is always set.</summary>
public sealed record ProfileExtractionResult(
    bool Success,
    string? FailureReason,
    ProfileContour? Contour,
    IReadOnlyList<string> Diagnostics)
{
    public static ProfileExtractionResult Ok(ProfileContour contour) =>
        new(true, null, contour, contour.Diagnostics);

    public static ProfileExtractionResult Fail(string reason, IEnumerable<string> diagnostics) =>
        new(false, reason, null, diagnostics.ToList().AsReadOnly());
}
