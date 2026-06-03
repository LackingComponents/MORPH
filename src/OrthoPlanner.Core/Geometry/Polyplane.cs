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
    public double[] ExtrusionDir { get; set; } = new double[] { 0, 1, 0 }; 
    public double[] UpVector { get; set; } = new double[] { 0, 0, 1 };

    public List<float[]> MeshVertices { get; set; } = new List<float[]>();
    
    // For barrier classification
    public double MaxInfluenceDistSq = 5.0 * 5.0; // Default: 5mm barrier
    
    public struct PPlane { public float A, B, C, D; }
    private List<PPlane> _planes = new List<PPlane>();

    public Polyplane() { }

    public Polyplane(double maxInfluenceDist)
    {
        MaxInfluenceDistSq = maxInfluenceDist * maxInfluenceDist;
    }

    public void AddPlane(float[] pA, float[] pB, float[] pC)
    {
        float vx = pB[0]-pA[0], vy = pB[1]-pA[1], vz = pB[2]-pA[2];
        float ux = pC[0]-pA[0], uy = pC[1]-pA[1], uz = pC[2]-pA[2];
        float nx = vy*uz - vz*uy, ny = vz*ux - vx*uz, nz = vx*uy - vy*ux;
        float len = (float)Math.Sqrt(nx*nx + ny*ny + nz*nz);
        if(len > 0) { nx/=len; ny/=len; nz/=len; }
        _planes.Add(new PPlane { A=nx, B=ny, C=nz, D= -(nx*pA[0] + ny*pA[1] + nz*pA[2]) });
    }

    public void SetMeshFromQuads(List<(float[], float[], float[], float[])> quads)
    {
        MeshVertices.Clear(); _planes.Clear();
        foreach (var q in quads)
        {
            MeshVertices.Add(q.Item1); MeshVertices.Add(q.Item2); MeshVertices.Add(q.Item3);
            AddPlane(q.Item1, q.Item2, q.Item3);
            MeshVertices.Add(q.Item1); MeshVertices.Add(q.Item3); MeshVertices.Add(q.Item4);
            AddPlane(q.Item1, q.Item3, q.Item4);
        }
    }

    /// <summary>
    /// Returns the plane index if the given point is strictly "Above" the polyplane 
    /// (or within the influence distance for barrier bounds), else null.
    /// </summary>
    public int? IsAbove(double[] point)
    {
        // 1. If we have explicit mesh geometry, use exact triangle proximity as the barrier
        if (MeshVertices.Count > 0)
        {
            double minDistSq = double.MaxValue;
            for (int i = 0; i + 2 < MeshVertices.Count; i += 3)
            {
                double[] ptA = { MeshVertices[i][0], MeshVertices[i][1], MeshVertices[i][2] };
                double[] ptB = { MeshVertices[i+1][0], MeshVertices[i+1][1], MeshVertices[i+1][2] };
                double[] ptC = { MeshVertices[i+2][0], MeshVertices[i+2][1], MeshVertices[i+2][2] };
                
                double dSq = DistancePointToTriangleSq(point, ptA, ptB, ptC);
                if (dSq < minDistSq) minDistSq = dSq;
            }
            if (minDistSq <= MaxInfluenceDistSq) return 0; // 0 = "hit kerf barrier"
            return null; // outside influence barrier
        }

        // 2. Original spline logic
        if (ControlPoints.Count < 2) return 0;

        var closestPt = EvaluateClosestPointOnSpline(point);
        double dx = point[0] - closestPt.X;
        double dy = point[1] - closestPt.Y;
        double dz = point[2] - closestPt.Z;
        
        double dot = dx * UpVector[0] + dy * UpVector[1] + dz * UpVector[2];
        return dot >= 0 ? 0 : (int?)null;
    }

    private (double X, double Y, double Z) EvaluateClosestPointOnSpline(double[] pt)
    {
        var curve = SplineHelper.ComputeCatmullRom3D(ControlPoints, 20);
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

    public List<float[]> GenerateMesh(double totalWidth = 100.0)
    {
        var curve = SplineHelper.ComputeCatmullRom3D(ControlPoints, 10);
        var verts = new List<float[]>();
        if (curve.Count < 2) return verts;
        
        double hw = totalWidth / 2.0;

        for (int i = 0; i < curve.Count - 1; i++)
        {
            var p0 = curve[i]; var p1 = curve[i+1];
            
            var p0A = new float[] { (float)(p0.X + ExtrusionDir[0]*hw), (float)(p0.Y + ExtrusionDir[1]*hw), (float)(p0.Z + ExtrusionDir[2]*hw) };
            var p0B = new float[] { (float)(p0.X - ExtrusionDir[0]*hw), (float)(p0.Y - ExtrusionDir[1]*hw), (float)(p0.Z - ExtrusionDir[2]*hw) };
            var p1A = new float[] { (float)(p1.X + ExtrusionDir[0]*hw), (float)(p1.Y + ExtrusionDir[1]*hw), (float)(p1.Z + ExtrusionDir[2]*hw) };
            var p1B = new float[] { (float)(p1.X - ExtrusionDir[0]*hw), (float)(p1.Y - ExtrusionDir[1]*hw), (float)(p1.Z - ExtrusionDir[2]*hw) };

            verts.Add(p0B); verts.Add(p1A); verts.Add(p0A);
            verts.Add(p0B); verts.Add(p1B); verts.Add(p1A);
        }
        return verts;
    }

    // Retained for LeFort1 exact API compat
    public void GenerateLeFort1Mesh(List<List<(double X, double Y, double Z)>> gridPts) 
    { 
        if(gridPts.Count == 0 || gridPts[0].Count == 0) return;
        int midRow = gridPts[0].Count / 2;
        var pts = new List<(double X, double Y, double Z)>();
        foreach(var col in gridPts) { if(col.Count > midRow) pts.Add(col[midRow]); }
        
        ControlPoints = pts;
        ExtrusionDir = new double[]{ 0, 1, 0 }; // Extrude back-to-front
        UpVector = new double[]{ 0, 0, 1 }; // Above is superior
        MeshVertices = GenerateMesh(100.0);
    }

    // --- Math Utilities for Closest Point on Triangle ---

    private double DistanceSq(double[] a, double[] b)
    {
        double dx = a[0]-b[0], dy=a[1]-b[1], dz=a[2]-b[2];
        return dx*dx + dy*dy + dz*dz;
    }

    private double DistancePointToTriangleSq(double[] p, double[] a, double[] b, double[] c)
    {
        double[] ab = { b[0]-a[0], b[1]-a[1], b[2]-a[2] };
        double[] ac = { c[0]-a[0], c[1]-a[1], c[2]-a[2] };
        double[] ap = { p[0]-a[0], p[1]-a[1], p[2]-a[2] };

        double d1 = ab[0]*ap[0] + ab[1]*ap[1] + ab[2]*ap[2];
        double d2 = ac[0]*ap[0] + ac[1]*ap[1] + ac[2]*ap[2];
        
        if (d1 <= 0.0 && d2 <= 0.0) return DistanceSq(p, a);

        double[] bp = { p[0]-b[0], p[1]-b[1], p[2]-b[2] };
        double d3 = ab[0]*bp[0] + ab[1]*bp[1] + ab[2]*bp[2];
        double d4 = ac[0]*bp[0] + ac[1]*bp[1] + ac[2]*bp[2];
        
        if (d3 >= 0.0 && d4 <= d3) return DistanceSq(p, b);

        double vc = d1*d4 - d3*d2;
        if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0) {
            double v = d1 / (d1 - d3);
            return DistanceSq(p, new double[] { a[0] + v*ab[0], a[1] + v*ab[1], a[2] + v*ab[2] });
        }

        double[] cp = { p[0]-c[0], p[1]-c[1], p[2]-c[2] };
        double d5 = ab[0]*cp[0] + ab[1]*cp[1] + ab[2]*cp[2];
        double d6 = ac[0]*cp[0] + ac[1]*cp[1] + ac[2]*cp[2];
        
        if (d6 >= 0.0 && d5 <= d6) return DistanceSq(p, c);

        double vb = d5*d2 - d1*d6;
        if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0) {
            double w = d2 / (d2 - d6);
            return DistanceSq(p, new double[] { a[0] + w*ac[0], a[1] + w*ac[1], a[2] + w*ac[2] });
        }

        double va = d3*d6 - d5*d4;
        if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0) {
            double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return DistanceSq(p, new double[] { b[0] + w*(c[0]-b[0]), b[1] + w*(c[1]-b[1]), b[2] + w*(c[2]-b[2]) });
        }

        double denom = 1.0 / (va + vb + vc);
        double v_final = vb * denom;
        double w_final = vc * denom;
        return DistanceSq(p, new double[] { a[0] + ab[0]*v_final + ac[0]*w_final, a[1] + ab[1]*v_final + ac[1]*w_final, a[2] + ab[2]*v_final + ac[2]*w_final });
    }

    // ─── Parametric Möller-Trumbore: returns t ∈ [0,1] or NaN ───────────────────

    /// <summary>Boolean wrapper kept for BFS compatibility.</summary>
    public bool SegmentIntersects(double[] p1, double[] p2) =>
        !double.IsNaN(SegmentIntersectT(p1, p2));

    /// <summary>
    /// Returns the first intersection parameter t along segment (p→q) where it crosses
    /// any triangle of the polyplane mesh, or double.NaN if no intersection.
    /// </summary>
    public double SegmentIntersectT(double[] p, double[] q)
    {
        double best = double.NaN;
        for (int i = 0; i + 2 < MeshVertices.Count; i += 3)
        {
            double t = SegmentTriangleT(p, q, MeshVertices[i], MeshVertices[i+1], MeshVertices[i+2]);
            if (!double.IsNaN(t) && (double.IsNaN(best) || t < best))
                best = t;
        }
        return best;
    }

    private static double SegmentTriangleT(double[] p, double[] q, float[] a, float[] b, float[] c)
    {
        double[] dir    = { q[0]-p[0], q[1]-p[1], q[2]-p[2] };
        double[] edge1  = { b[0]-a[0], b[1]-a[1], b[2]-a[2] };
        double[] edge2  = { c[0]-a[0], c[1]-a[1], c[2]-a[2] };

        double[] h = { dir[1]*edge2[2] - dir[2]*edge2[1],
                       dir[2]*edge2[0] - dir[0]*edge2[2],
                       dir[0]*edge2[1] - dir[1]*edge2[0] };

        double aDot = edge1[0]*h[0] + edge1[1]*h[1] + edge1[2]*h[2];
        if (aDot > -1e-9 && aDot < 1e-9) return double.NaN;

        double f = 1.0 / aDot;
        double[] s = { p[0]-a[0], p[1]-a[1], p[2]-a[2] };
        double u = f * (s[0]*h[0] + s[1]*h[1] + s[2]*h[2]);
        if (u < 0.0 || u > 1.0) return double.NaN;

        double[] qv = { s[1]*edge1[2] - s[2]*edge1[1],
                        s[2]*edge1[0] - s[0]*edge1[2],
                        s[0]*edge1[1] - s[1]*edge1[0] };
        double v = f * (dir[0]*qv[0] + dir[1]*qv[1] + dir[2]*qv[2]);
        if (v < 0.0 || u + v > 1.0) return double.NaN;

        double t = f * (edge2[0]*qv[0] + edge2[1]*qv[1] + edge2[2]*qv[2]);
        return (t >= 0.0 && t <= 1.0) ? t : double.NaN;
    }

    // ─── Side test via parity (ray-casting) ──────────────────────────────────────
    /// <summary>
    /// Returns true if point <paramref name="pt"/> is on the same side as a reference
    /// point that is known to be "above" the polyplane (e.g. the highest-Z seed).
    /// Uses parity of ray-polyplane crossings: even = same side, odd = opposite.
    /// </summary>
    public bool SameSideAs(double[] pt, double[] reference)
    {
        // Shoot a segment from pt to reference; count polyplane triangle crossings.
        // Even → same side. Odd → opposite side.
        int crossings = 0;
        for (int i = 0; i + 2 < MeshVertices.Count; i += 3)
        {
            double t = SegmentTriangleT(pt, reference,
                MeshVertices[i], MeshVertices[i+1], MeshVertices[i+2]);
            if (!double.IsNaN(t)) crossings++;
        }
        return (crossings % 2) == 0;
    }

    // ─── Fast side test via nearest-plane signed distance ────────────────────────

    private float[]? _refSigns; // cached sign of reference point per plane

    /// <summary>
    /// Pre-caches the signed distance of the reference point to every plane in the
    /// polyplane. Must be called once before using <see cref="SameSideAsFast"/>.
    /// </summary>
    public void CacheReferenceSide(double[] reference)
    {
        _refSigns = new float[_planes.Count];
        for (int i = 0; i < _planes.Count; i++)
        {
            var pl = _planes[i];
            _refSigns[i] = pl.A*(float)reference[0] + pl.B*(float)reference[1]
                         + pl.C*(float)reference[2] + pl.D;
        }
    }

    /// <summary>
    /// Fast side classification: finds the polyplane triangle nearest to the vertex
    /// (by point-to-triangle distance) and checks whether the vertex is on the same
    /// side of that triangle's plane as the reference. Much faster than ray-casting
    /// because it avoids Möller-Trumbore per triangle.
    /// <para>Requires <see cref="CacheReferenceSide"/> to have been called first.</para>
    /// </summary>
    public bool SameSideAsFast(float[] v)
    {
        if (_refSigns == null || _planes.Count == 0) return true;

        double px = v[0], py = v[1], pz = v[2];
        double[] pt = { px, py, pz };
        double bestDist = double.MaxValue;
        int bestPlane = 0;

        int nTri = MeshVertices.Count / 3;
        for (int i = 0; i < nTri; i++)
        {
            var a = MeshVertices[i*3]; var b = MeshVertices[i*3+1]; var c = MeshVertices[i*3+2];
            double[] da = {a[0],a[1],a[2]}, db = {b[0],b[1],b[2]}, dc = {c[0],c[1],c[2]};
            double d = DistancePointToTriangleSq(pt, da, db, dc);
            if (d < bestDist) { bestDist = d; bestPlane = i; }
        }

        var pl = _planes[bestPlane];
        float sign = pl.A*v[0] + pl.B*v[1] + pl.C*v[2] + pl.D;
        return (sign >= 0) == (_refSigns[bestPlane] >= 0);
    }
}
