using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.Core.Segmentation;

/// <summary>
/// Label volume — a byte array parallel to VolumeData where each voxel
/// can be assigned a segment label (0 = unlabeled, 1-255 = segment IDs).
/// </summary>
public class SegmentationVolume
{
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public byte[] Labels { get; }
    public Dictionary<byte, SegmentInfo> Segments { get; } = new();

    public SegmentationVolume(VolumeData volume)
    {
        Width = volume.Width;
        Height = volume.Height;
        Depth = volume.Depth;
        Labels = new byte[Width * Height * Depth];
    }

    public byte GetLabel(int x, int y, int z)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return 0;
        return Labels[x + y * Width + z * Width * Height];
    }

    public void SetLabel(int x, int y, int z, byte label)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth) return;
        Labels[x + y * Width + z * Width * Height] = label;
    }

    /// <summary>
    /// Add or update a segment definition.
    /// </summary>
    public void AddSegment(SegmentInfo segment)
    {
        Segments[segment.Id] = segment;
    }

    /// <summary>
    /// Count the number of voxels for a given label.
    /// </summary>
    public long CountVoxels(byte label)
    {
        long count = 0;
        for (int i = 0; i < Labels.Length; i++)
            if (Labels[i] == label) count++;
        return count;
    }

    /// <summary>
    /// Clear all labels (set everything to 0).
    /// </summary>
    public void ClearAll()
    {
        Array.Clear(Labels, 0, Labels.Length);
    }

    /// <summary>
    /// Clear only a specific label.
    /// </summary>
    public void ClearLabel(byte label)
    {
        for (int i = 0; i < Labels.Length; i++)
            if (Labels[i] == label) Labels[i] = 0;
    }
}
