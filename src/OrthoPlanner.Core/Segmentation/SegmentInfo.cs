namespace OrthoPlanner.Core.Segmentation;

/// <summary>
/// Represents a named segment (label) in the segmentation volume.
/// Each segment has an ID, name, color, and visibility state.
/// </summary>
public class SegmentInfo
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
    public byte ColorR { get; set; }
    public byte ColorG { get; set; }
    public byte ColorB { get; set; }
    public byte ColorA { get; set; } = 200;
    public bool IsVisible { get; set; } = true;

    // Common presets for orthognathic surgery
    public static SegmentInfo Maxilla => new()
        { Id = 1, Name = "Maxilla", ColorR = 230, ColorG = 180, ColorB = 140 };
    public static SegmentInfo Mandible => new()
        { Id = 2, Name = "Mandible", ColorR = 200, ColorG = 160, ColorB = 120 };
    public static SegmentInfo Teeth => new()
        { Id = 3, Name = "Teeth", ColorR = 245, ColorG = 245, ColorB = 230 };
    public static SegmentInfo SoftTissue => new()
        { Id = 4, Name = "Soft Tissue", ColorR = 255, ColorG = 200, ColorB = 180 };
    public static SegmentInfo Airway => new()
        { Id = 5, Name = "Airway", ColorR = 100, ColorG = 180, ColorB = 255 };
}
