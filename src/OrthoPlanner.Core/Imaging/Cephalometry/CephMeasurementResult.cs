namespace OrthoPlanner.Core.Imaging.Cephalometry;

/// <summary>Unit of a cephalometric measurement value.</summary>
public enum CephUnit
{
    Degrees,
    Millimeters
}

/// <summary>Computation state of a single measurement.</summary>
public enum CephMeasurementStatus
{
    /// <summary>All required landmarks were placed; <see cref="CephMeasurementResult.Value"/> is populated.</summary>
    Computed,

    /// <summary>One or more required landmarks are not yet placed; the value is null.</summary>
    MissingLandmarks
}

/// <summary>
/// Result of a single named cephalometric measurement (e.g. SNA, FMA, Convexity),
/// including its reference norm. Immutable; construct via <see cref="Computed"/> or <see cref="Missing"/>.
/// </summary>
public sealed class CephMeasurementResult
{
    /// <summary>Full measurement name (e.g. "SNA", "Interincisal angle").</summary>
    public string Name { get; }

    /// <summary>Short label for compact UI (e.g. "U1-NA°").</summary>
    public string Abbreviation { get; }

    /// <summary>Whether the value is an angle or a linear distance.</summary>
    public CephUnit Unit { get; }

    /// <summary>Measured value; null when <see cref="Status"/> is <see cref="CephMeasurementStatus.MissingLandmarks"/>.</summary>
    public double? Value { get; }

    /// <summary>Reference mean for this measurement (e.g. 82 for SNA).</summary>
    public double NormMean { get; }

    /// <summary>Half-width of the normal range (e.g. 2 → 82°±2°).</summary>
    public double NormTolerance { get; }

    /// <summary>Whether the measurement was computed or is awaiting landmarks.</summary>
    public CephMeasurementStatus Status { get; }

    /// <summary>
    /// Names of the landmarks that are missing. Populated only when
    /// <see cref="Status"/> is <see cref="CephMeasurementStatus.MissingLandmarks"/>; otherwise empty.
    /// </summary>
    public IReadOnlyList<string> MissingLandmarkNames { get; }

    /// <summary>
    /// True when the value lies within <see cref="NormMean"/> ± <see cref="NormTolerance"/>;
    /// null when the measurement could not be computed.
    /// </summary>
    public bool? IsWithinNorm =>
        Value is { } v ? Math.Abs(v - NormMean) <= NormTolerance : null;

    private CephMeasurementResult(
        string name, string abbreviation, CephUnit unit, double? value,
        double normMean, double normTolerance,
        CephMeasurementStatus status, IReadOnlyList<string> missingLandmarkNames)
    {
        Name = name;
        Abbreviation = abbreviation;
        Unit = unit;
        Value = value;
        NormMean = normMean;
        NormTolerance = normTolerance;
        Status = status;
        MissingLandmarkNames = missingLandmarkNames;
    }

    /// <summary>Creates a successfully computed result.</summary>
    public static CephMeasurementResult Computed(
        string name, string abbreviation, CephUnit unit,
        double value, double normMean, double normTolerance) =>
        new(name, abbreviation, unit, value, normMean, normTolerance,
            CephMeasurementStatus.Computed, Array.Empty<string>());

    /// <summary>Creates a result flagged as missing the given landmarks.</summary>
    public static CephMeasurementResult Missing(
        string name, string abbreviation, CephUnit unit,
        double normMean, double normTolerance, IReadOnlyList<string> missingLandmarkNames) =>
        new(name, abbreviation, unit, null, normMean, normTolerance,
            CephMeasurementStatus.MissingLandmarks, missingLandmarkNames);
}
