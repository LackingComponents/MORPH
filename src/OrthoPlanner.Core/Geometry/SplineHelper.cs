using System.Collections.Generic;

namespace OrthoPlanner.Core.Geometry;

public static class SplineHelper
{
    /// <summary>
    /// Computes a 3D Catmull-Rom spline through the given control points.
    /// </summary>
    public static List<(double X, double Y, double Z)> ComputeCatmullRom3D(
        List<(double X, double Y, double Z)> points, int stepsPerSegment = 20)
    {
        if (points == null || points.Count < 2)
            return points ?? new List<(double X, double Y, double Z)>();

        var cp = new List<(double X, double Y, double Z)>();
        cp.Add(points[0]);
        cp.AddRange(points);
        cp.Add(points[points.Count - 1]);

        var curve = new List<(double X, double Y, double Z)>();

        for (int i = 1; i < cp.Count - 2; i++)
        {
            var p0 = cp[i - 1]; var p1 = cp[i]; var p2 = cp[i + 1]; var p3 = cp[i + 2];

            for (int tStep = 0; tStep < stepsPerSegment; tStep++)
            {
                double t = tStep / (double)stepsPerSegment;
                double t2 = t * t, t3 = t2 * t;

                double x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t +
                    (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                    (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t +
                    (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                double z = 0.5 * ((2 * p1.Z) + (-p0.Z + p2.Z) * t +
                    (2 * p0.Z - 5 * p1.Z + 4 * p2.Z - p3.Z) * t2 +
                    (-p0.Z + 3 * p1.Z - 3 * p2.Z + p3.Z) * t3);

                curve.Add((x, y, z));
            }
        }
        curve.Add(cp[cp.Count - 2]);
        return curve;
    }
}
