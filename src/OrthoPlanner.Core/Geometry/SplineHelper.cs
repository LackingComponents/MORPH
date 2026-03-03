using System;
using System.Collections.Generic;

namespace OrthoPlanner.Core.Geometry;

public static class SplineHelper
{
    /// <summary>
    /// Computes a Catmull-Rom spline from a list of 2D control points.
    /// Returns an array of (X, Y) points representing the dense smooth curve.
    /// To ensure the curve passes from the first to the last point, the endpoints are duplicated internally.
    /// </summary>
    /// <param name="points">List of 2D control points (X, Y)</param>
    /// <param name="stepsPerSegment">Number of interpolation steps between each control point pair.</param>
    /// <returns>A dense list of (X, Y) points forming the smooth curve.</returns>
    public static List<(double X, double Y)> ComputeCatmullRom2D(List<(double X, double Y)> points, int stepsPerSegment = 20)
    {
        if (points == null || points.Count < 2)
            return points ?? new List<(double X, double Y)>();

        // Duplicate first and last points so the curve actually touches the user's extreme landmarks.
        var controlPoints = new List<(double X, double Y)>();
        controlPoints.Add(points[0]);
        controlPoints.AddRange(points);
        controlPoints.Add(points[points.Count - 1]);

        var curve = new List<(double X, double Y)>();

        for (int i = 1; i < controlPoints.Count - 2; i++)
        {
            var p0 = controlPoints[i - 1];
            var p1 = controlPoints[i];
            var p2 = controlPoints[i + 1];
            var p3 = controlPoints[i + 2];

            for (int tStep = 0; tStep < stepsPerSegment; tStep++)
            {
                double t = tStep / (double)stepsPerSegment;
                double t2 = t * t;
                double t3 = t2 * t;

                double x = 0.5 * ((2.0 * p1.X) +
                                 (-p0.X + p2.X) * t +
                                 (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * t2 +
                                 (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * t3);

                double y = 0.5 * ((2.0 * p1.Y) +
                                 (-p0.Y + p2.Y) * t +
                                 (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * t2 +
                                 (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * t3);

                curve.Add((x, y));
            }
        }

        // Add the exact final point
        curve.Add(controlPoints[controlPoints.Count - 2]);

        return curve;
    }
}
