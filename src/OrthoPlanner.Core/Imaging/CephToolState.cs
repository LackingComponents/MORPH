namespace OrthoPlanner.Core.Imaging;

public enum CephTool
{
    Select,
    CustomPoint,
    Line,
    InfinitePlane,
    AnglePlanes,
    Angle3Points,
    DistancePoints,
    DistancePointPlane
}

/// <summary>
/// Tracks the active cephalometric tool, pending multi-click points, and all completed measurements.
/// </summary>
public class CephToolState
{
    public CephTool ActiveTool { get; set; } = CephTool.Select;
    public List<CephPoint> PendingPoints { get; } = new();
    public List<CephMeasurement> Measurements { get; } = new();
    public CephMeasurement? SelectedMeasurement { get; set; }

    private int _lineCounter;
    private int _planeCounter;
    private int _angleCounter;
    private int _distCounter;

    public void SetTool(CephTool tool)
    {
        ActiveTool = tool;
        PendingPoints.Clear();
    }

    public void CancelPending()
    {
        PendingPoints.Clear();
    }

    /// <summary>
    /// Returns the next auto-label for the given tool type.
    /// For CustomPoint: uses "CP{n}" where n = max existing CP index + 1.
    /// This handles deletions correctly — the next number is always max+1,
    /// never a simple count that would repeat after a delete.
    /// </summary>
    public string NextLabel(CephTool tool) => tool switch
    {
        CephTool.CustomPoint => NextCustomPointLabel(),
        CephTool.Line => $"Line {++_lineCounter}",
        CephTool.InfinitePlane => $"Plane {++_planeCounter}",
        CephTool.AnglePlanes or CephTool.Angle3Points => $"Angle {++_angleCounter}",
        CephTool.DistancePoints or CephTool.DistancePointPlane => $"Dist {++_distCounter}",
        _ => "Measurement"
    };

    /// <summary>
    /// Computes CP{n} where n = max existing CP index + 1.
    /// Scans Measurements for labels matching "CP" followed by an integer.
    /// If none exist, returns "CP1". Safe against deletions and reloads.
    /// </summary>
    private string NextCustomPointLabel()
    {
        int maxIndex = 0;
        foreach (var m in Measurements)
        {
            if (m.ToolType == CephTool.CustomPoint &&
                m.Label != null &&
                m.Label.StartsWith("CP", StringComparison.Ordinal) &&
                int.TryParse(m.Label.AsSpan(2), out int n))
            {
                if (n > maxIndex) maxIndex = n;
            }
        }
        return $"CP{maxIndex + 1}";
    }
}
