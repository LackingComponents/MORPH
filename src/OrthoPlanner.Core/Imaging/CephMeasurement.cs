namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// A 2D point in DRR image-pixel coordinates.
/// </summary>
public record struct CephPoint(double X, double Y);

public enum CephPlaneKind
{
    Manual,
    FrankfortHorizontal,
    PlanningHeadPositionMsp,
    ClinicalMidfaceMsp
}

public record struct CephPoint3D(double X, double Y, double Z);

/// <summary>
/// A completed cephalometric measurement: point, line, angle, or distance.
/// </summary>
public class CephMeasurement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Label { get; set; } = "";
    public CephTool ToolType { get; set; }
    public List<CephPoint> Points { get; set; } = new();
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public byte ColorR { get; set; } = 255;
    public byte ColorG { get; set; } = 255;
    public byte ColorB { get; set; } = 255;
    public double Opacity { get; set; } = 1.0;
    public CephPlaneKind PlaneKind { get; set; } = CephPlaneKind.Manual;
    public CephPoint3D? PlaneOrigin3D { get; set; }
    public CephPoint3D? PlaneNormal3D { get; set; }
    public CephPoint3D? PlaneAxisU3D { get; set; }
    public CephPoint3D? PlaneAxisV3D { get; set; }
    public string ConstructionNote { get; set; } = "";

    /// <summary>Visibility toggle: when false the measurement is hidden from the DRR canvas.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Reference to another measurement (for AnglePlanes, DistancePointPlane).</summary>
    public string? RefMeasurementId1 { get; set; }

    /// <summary>Second reference measurement (for AnglePlanes).</summary>
    public string? RefMeasurementId2 { get; set; }
}
