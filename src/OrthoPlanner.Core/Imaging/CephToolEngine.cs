namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Pure computation methods for cephalometric measurements.
/// All distances use DICOM spacing for mm calibration; no manual calibration needed.
/// </summary>
public static class CephToolEngine
{
    /// <summary>Euclidean distance in mm using per-axis DRR spacing.</summary>
    public static double DistanceMm(CephPoint a, CephPoint b, double spacingX, double spacingY)
    {
        double dx = (b.X - a.X) * spacingX;
        double dy = (b.Y - a.Y) * spacingY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Angle at vertex between rays vertexÔåÆA and vertexÔåÆB, in degrees.</summary>
    public static double Angle3Pts(CephPoint a, CephPoint vertex, CephPoint b,
                                   double spacingX, double spacingY)
    {
        double vAx = (a.X - vertex.X) * spacingX;
        double vAy = (a.Y - vertex.Y) * spacingY;
        double vBx = (b.X - vertex.X) * spacingX;
        double vBy = (b.Y - vertex.Y) * spacingY;

        double dot = vAx * vBx + vAy * vBy;
        double magA = Math.Sqrt(vAx * vAx + vAy * vAy);
        double magB = Math.Sqrt(vBx * vBx + vBy * vBy);
        if (magA < 1e-10 || magB < 1e-10) return 0;

        double cos = Math.Clamp(dot / (magA * magB), -1, 1);
        return Math.Acos(cos) * (180.0 / Math.PI);
    }

    /// <summary>Acute angle between two directed lines, in degrees.</summary>
    public static double AngleLines(CephPoint l1a, CephPoint l1b,
                                    CephPoint l2a, CephPoint l2b,
                                    double spacingX, double spacingY)
    {
        double d1x = (l1b.X - l1a.X) * spacingX;
        double d1y = (l1b.Y - l1a.Y) * spacingY;
        double d2x = (l2b.X - l2a.X) * spacingX;
        double d2y = (l2b.Y - l2a.Y) * spacingY;

        double dot = d1x * d2x + d1y * d2y;
        double mag1 = Math.Sqrt(d1x * d1x + d1y * d1y);
        double mag2 = Math.Sqrt(d2x * d2x + d2y * d2y);
        if (mag1 < 1e-10 || mag2 < 1e-10) return 0;

        double cos = Math.Clamp(Math.Abs(dot) / (mag1 * mag2), 0, 1);
        return Math.Acos(cos) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Perpendicular distance from a point to an infinite line defined by two points.
    /// Returns the foot point (closest point on the line) and distance in mm.
    /// </summary>
    public static (CephPoint Foot, double DistMm) PerpendicularToLine(
        CephPoint point, CephPoint lineA, CephPoint lineB,
        double spacingX, double spacingY)
    {
        double px = (point.X - lineA.X) * spacingX;
        double py = (point.Y - lineA.Y) * spacingY;
        double dx = (lineB.X - lineA.X) * spacingX;
        double dy = (lineB.Y - lineA.Y) * spacingY;

        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20)
            return (lineA, DistanceMm(point, lineA, spacingX, spacingY));

        double t = (px * dx + py * dy) / lenSq;
        var foot = new CephPoint(
            lineA.X + t * (lineB.X - lineA.X),
            lineA.Y + t * (lineB.Y - lineA.Y));

        return (foot, DistanceMm(point, foot, spacingX, spacingY));
    }

    /// <summary>Intersection of two infinite lines (image-pixel coords). Null if parallel.</summary>
    public static CephPoint? LineIntersection(CephPoint a1, CephPoint a2,
                                              CephPoint b1, CephPoint b2)
    {
        double d1x = a2.X - a1.X, d1y = a2.Y - a1.Y;
        double d2x = b2.X - b1.X, d2y = b2.Y - b1.Y;

        double denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < 1e-10) return null;

        double t = ((b1.X - a1.X) * d2y - (b1.Y - a1.Y) * d2x) / denom;
        return new CephPoint(a1.X + t * d1x, a1.Y + t * d1y);
    }
}
