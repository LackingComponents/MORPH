namespace OrthoPlanner.Core.Imaging.Cephalometry;

/// <summary>
/// Identifies a cephalometric analysis protocol.
/// New protocols (e.g. McNamara, Arnett) are added here and given a matching
/// branch in <see cref="CephAnalysisEngine.Compute"/>; existing protocols are unaffected.
/// </summary>
public enum CephAnalysisType
{
    Steiner,
    Tweed,
    Ricketts
}
