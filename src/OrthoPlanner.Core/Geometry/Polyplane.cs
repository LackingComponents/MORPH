using System;
using System.Collections.Generic;
using System.Linq;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Represents an interactive cutting surface (Polyplane) defined by a sequence
/// of 3D control points, an extrusion direction, and an "up" orientation.
/// </summary>
public class Polyplane
{
    public List<(double X, double Y, double Z)> ControlPoints { get; set; } = new();
    
    // Extrusion direction (e.g., [0, 1, 0] for an anterior-posterior Y-axis extrusion)
    public double[] ExtrusionDir { get; set; } = new double[] { 0, 1, 0 }; 
    
    // The "Up" vector to define which side is "Above" (e.g., [0, 0, 1] for Z-axis up)
    public double[] UpVector { get; set; } = new double[] { 0, 0, 1 };

    public Polyplane() { }

    /// <summary>
    /// Evaluates if a given point is strictly "Above" the polyplane.
    /// Projects the point to the space normal to the extrusion direction,
    /// finds the closest point on the 3D spline, and checks its relation to the UpVector.
    /// </summary>
    public bool IsAbove(double[] point)
    {
        if (ControlPoints.Count < 2) return true;

        var closestPt = EvaluateClosestPointOnSpline(point);
        
        double dx = point[0] - closestPt.X;
        double dy = point[1] - closestPt.Y;
        double dz = point[2] - closestPt.Z;
        
        double dot = dx * UpVector[0] + dy * UpVector[1] + dz * UpVector[2];
        return dot >= 0;
    }

    private (double X, double Y, double Z) EvaluateClosestPointOnSpline(double[] pt)
    {
        var curve = SplineHelper.ComputeCatmullRom3D(ControlPoints, 20);
        
        // Normalize the extrusion direction just in case
        double len = Math.Sqrt(ExtrusionDir[0]*ExtrusionDir[0] + ExtrusionDir[1]*ExtrusionDir[1] + ExtrusionDir[2]*ExtrusionDir[2]);
        if (len == 0) len = 1;
        double ex = ExtrusionDir[0] / len;
        double ey = ExtrusionDir[1] / len;
        double ez = ExtrusionDir[2] / len;

        double minDistSq = double.MaxValue;
        var bestPoint = curve[0];
        
        for (int i = 0; i < curve.Count; i++)
        {
            double dx = pt[0] - curve[i].X;
            double dy = pt[1] - curve[i].Y;
            double dz = pt[2] - curve[i].Z;
            
            // remove component along ExtrusionDir
            double dotExtrusion = dx * ex + dy * ey + dz * ez;
            dx -= dotExtrusion * ex;
            dy -= dotExtrusion * ey;
            dz -= dotExtrusion * ez;
            
            double distSq = dx * dx + dy * dy + dz * dz;
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                bestPoint = curve[i];
            }
        }
        return bestPoint;
    }

    /// <summary>
    /// Generate a triangle-soup mesh (List of float[3]) representing the surface,
    /// suitable for rendering in the UI. Extrudes by +/- width/2 along ExtrusionDir.
    /// </summary>
    public List<float[]> GenerateMesh(double totalWidth = 100.0)
    {
        var curve = SplineHelper.ComputeCatmullRom3D(ControlPoints, 10);
        var verts = new List<float[]>();
        if (curve.Count < 2) return verts;
        
        double hw = totalWidth / 2.0;

        // Extrude each segment
        for (int i = 0; i < curve.Count - 1; i++)
        {
            var p0 = curve[i];
            var p1 = curve[i+1];
            
            var p0A = new float[] { (float)(p0.X + ExtrusionDir[0]*hw), (float)(p0.Y + ExtrusionDir[1]*hw), (float)(p0.Z + ExtrusionDir[2]*hw) };
            var p0B = new float[] { (float)(p0.X - ExtrusionDir[0]*hw), (float)(p0.Y - ExtrusionDir[1]*hw), (float)(p0.Z - ExtrusionDir[2]*hw) };
            var p1A = new float[] { (float)(p1.X + ExtrusionDir[0]*hw), (float)(p1.Y + ExtrusionDir[1]*hw), (float)(p1.Z + ExtrusionDir[2]*hw) };
            var p1B = new float[] { (float)(p1.X - ExtrusionDir[0]*hw), (float)(p1.Y - ExtrusionDir[1]*hw), (float)(p1.Z - ExtrusionDir[2]*hw) };

            // Triangle 1: p0B, p1A, p0A
            verts.Add(p0B); verts.Add(p1A); verts.Add(p0A);
            // Triangle 2: p0B, p1B, p1A
            verts.Add(p0B); verts.Add(p1B); verts.Add(p1A);
        }
        
        return verts;
    }
}
