namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Physical-space bounds and inverse NHP transform used to generate a DRR
/// in the current head pose (same convention as oblique MPR sampling).
/// </summary>
public sealed class DrrProjectionParams
{
    public NhpTransform InverseNhp { get; init; } = NhpTransform.Identity;
    public double MinX { get; init; }
    public double MaxX { get; init; }
    public double MinY { get; init; }
    public double MaxY { get; init; }
    public double MinZ { get; init; }
    public double MaxZ { get; init; }

    public static DrrProjectionParams FromVolume(VolumeData volume)
    {
        return new DrrProjectionParams
        {
            InverseNhp = NhpTransform.Identity,
            MinX = 0,
            MaxX = (volume.Width - 1) * volume.Spacing[0],
            MinY = 0,
            MaxY = (volume.Height - 1) * volume.Spacing[1],
            MinZ = 0,
            MaxZ = (volume.Depth - 1) * volume.Spacing[2],
        };
    }

    public bool UsesNhpSampling => !InverseNhp.IsIdentity;
}
