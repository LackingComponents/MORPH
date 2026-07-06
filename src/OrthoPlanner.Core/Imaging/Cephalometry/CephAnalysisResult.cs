namespace OrthoPlanner.Core.Imaging.Cephalometry;

/// <summary>
/// The full set of measurement results produced by one cephalometric analysis.
/// </summary>
public sealed class CephAnalysisResult
{
    /// <summary>Which analysis protocol produced these results.</summary>
    public CephAnalysisType AnalysisType { get; }

    /// <summary>Measurement results, in the canonical order for this analysis.</summary>
    public IReadOnlyList<CephMeasurementResult> Measurements { get; }

    public CephAnalysisResult(CephAnalysisType analysisType, IReadOnlyList<CephMeasurementResult> measurements)
    {
        AnalysisType = analysisType;
        Measurements = measurements;
    }
}
