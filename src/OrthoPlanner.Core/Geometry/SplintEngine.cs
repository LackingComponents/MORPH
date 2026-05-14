using System;
using System.Collections.Generic;

namespace OrthoPlanner.Core.Geometry;

// ═══════════════════════════════════════════════════════════
//  ARCH CURVE  —  Catmull-Rom spline through user points
// ═══════════════════════════════════════════════════════════
public class ArchCurve
{
    private readonly List<(float x, float y, float z)> _ctrl = new();

    public int ControlPointCount => _ctrl.Count;

    public void AddPoint(float x, float y, float z)
    {
        _ctrl.Add((x, y, z));
        SortByAngle();
    }

    public void RemoveLast()
    {
        if (_ctrl.Count > 0) _ctrl.RemoveAt(_ctrl.Count - 1);
    }

    public void Clear() => _ctrl.Clear();

    /// <summary>Sort control points around their centroid by azimuth (XY plane) so
    /// the spline always sweeps the arch in a consistent left→right direction.</summary>
    private void SortByAngle()
    {
        if (_ctrl.Count < 2) return;
        float cx = 0, cy = 0;
        foreach (var p in _ctrl) { cx += p.x; cy += p.y; }
        cx /= _ctrl.Count; cy /= _ctrl.Count;
        _ctrl.Sort((a, b) => MathF.Atan2(a.y - cy, a.x - cx)
                            .CompareTo(MathF.Atan2(b.y - cy, b.x - cx)));
    }

    /// <summary>Sample the Catmull-Rom spline at <paramref name="n"/> evenly-spaced
    /// parameter values. Requires ≥ 2 control points.</summary>
    public List<(float x, float y, float z)> Sample(int n = 200)
    {
        var result = new List<(float, float, float)>(n);
        if (_ctrl.Count < 2) return result;

        // Pad with phantom endpoints for open Catmull-Rom
        var pts = new List<(float x, float y, float z)>(_ctrl.Count + 2);
        pts.Add(Mirror(_ctrl[0], _ctrl[1]));
        pts.AddRange(_ctrl);
        pts.Add(Mirror(_ctrl[^1], _ctrl[^2]));

        int segs = _ctrl.Count - 1;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1) * segs;
            int seg = Math.Clamp((int)t, 0, segs - 1);
            float u = t - seg;

            var p0 = pts[seg];
            var p1 = pts[seg + 1];
            var p2 = pts[seg + 2];
            var p3 = pts[seg + 3];

            result.Add(CatmullRom(p0, p1, p2, p3, u));
        }
        return result;
    }

    private static (float, float, float) Mirror((float x, float y, float z) a, (float x, float y, float z) b)
        => (2 * a.x - b.x, 2 * a.y - b.y, 2 * a.z - b.z);

    private static (float x, float y, float z) CatmullRom(
        (float x, float y, float z) p0, (float x, float y, float z) p1,
        (float x, float y, float z) p2, (float x, float y, float z) p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        float x = 0.5f * ((2 * p1.x) + (-p0.x + p2.x) * t
                + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2
                + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3);
        float y = 0.5f * ((2 * p1.y) + (-p0.y + p2.y) * t
                + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2
                + (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3);
        float z = 0.5f * ((2 * p1.z) + (-p0.z + p2.z) * t
                + (2 * p0.z - 5 * p1.z + 4 * p2.z - p3.z) * t2
                + (-p0.z + 3 * p1.z - 3 * p2.z + p3.z) * t3);
        return (x, y, z);
    }
}

// ═══════════════════════════════════════════════════════════
//  SPLINT ENGINE  —  horseshoe solid + tooth projection
// ═══════════════════════════════════════════════════════════
public static class SplintEngine
{
    /// <summary>
    /// Check if a triangle-soup mesh (flat float[], stride 9) is approximately
    /// watertight by counting unmatched edges. Returns the fraction of
    /// boundary edges (0 = fully closed, 1 = fully open).
    /// </summary>
    public static float WatertightScore(float[] mesh)
    {
        if (mesh == null || mesh.Length < 9) return 1f;
        var edgeCounts = new Dictionary<(long, long), int>();

        long Key(float ax, float ay, float az)
            => ((long)Math.Round(ax, 1) * 397L ^ (long)Math.Round(ay, 1)) * 397L
             ^ (long)Math.Round(az, 1);

        for (int i = 0; i + 8 < mesh.Length; i += 9)
        {
            long v0 = Key(mesh[i],     mesh[i+1], mesh[i+2]);
            long v1 = Key(mesh[i+3],   mesh[i+4], mesh[i+5]);
            long v2 = Key(mesh[i+6],   mesh[i+7], mesh[i+8]);

            foreach (var e in new[] { (Math.Min(v0,v1), Math.Max(v0,v1)),
                                      (Math.Min(v1,v2), Math.Max(v1,v2)),
                                      (Math.Min(v2,v0), Math.Max(v2,v0)) })
            {
                edgeCounts.TryGetValue(e, out int c);
                edgeCounts[e] = c + 1;
            }
        }

        int boundary = 0, total = edgeCounts.Count;
        foreach (var kv in edgeCounts)
            if (kv.Value != 2) boundary++;

        return total == 0 ? 1f : (float)boundary / total;
    }

    /// <summary>
    /// Build a horseshoe-shaped splint solid connecting the two arch curves,
    /// then project its occlusal faces onto the respective tooth meshes to
    /// imprint the tooth surface.
    /// </summary>
    /// <param name="upperCurve">Arch curve sampled on the maxillary occlusal surface.</param>
    /// <param name="lowerCurve">Arch curve sampled on the mandibular occlusal surface.</param>
    /// <param name="labiolingualMm">Width of the splint band (labio-lingual), mm.</param>
    /// <param name="penetrationMm">How far the splint faces dig into the teeth, mm.</param>
    /// <param name="upperMesh">Upper tooth mesh (flat float[], stride 9) for imprinting.</param>
    /// <param name="lowerMesh">Lower tooth mesh (flat float[], stride 9) for imprinting.</param>
    /// <param name="sampleCount">Number of cross-section rings.</param>
    public static float[] GenerateSplint(
        List<(float x, float y, float z)> upperCurve,
        List<(float x, float y, float z)> lowerCurve,
        float labiolingualMm = 8f,
        float penetrationMm  = 3f,
        float[]? upperMesh   = null,
        float[]? lowerMesh   = null,
        int sampleCount      = 160)
    {
        int n = Math.Min(sampleCount, Math.Min(upperCurve.Count, lowerCurve.Count));
        if (n < 2) return Array.Empty<float>();

        float half = labiolingualMm * 0.5f;

        // ── 1. Compute outward normals for each ring ─────────────────────
        // Outward normal = cross(tangent, up=0,0,1) normalised in XY plane.
        var tangents = new (float x, float y, float z)[n];
        var outward  = new (float x, float y, float z)[n];

        for (int i = 0; i < n; i++)
        {
            int prev = Math.Max(0, i - 1), next = Math.Min(n - 1, i + 1);
            float tx = upperCurve[next].x - upperCurve[prev].x;
            float ty = upperCurve[next].y - upperCurve[prev].y;
            float tz = upperCurve[next].z - upperCurve[prev].z;
            float len = MathF.Sqrt(tx*tx + ty*ty + tz*tz);
            if (len < 1e-6f) { tx = 1; ty = 0; tz = 0; } else { tx /= len; ty /= len; tz /= len; }
            tangents[i] = (tx, ty, tz);
            // Outward = tangent × Z  →  (ty, -tx, 0)  pointing away from arch
            float ox = ty, oy = -tx;
            float ol = MathF.Sqrt(ox*ox + oy*oy);
            if (ol < 1e-6f) { ox = 1; oy = 0; }
            else { ox /= ol; oy /= ol; }
            outward[i] = (ox, oy, 0f);
        }

        // ── 2. Build the 4 corner strips ─────────────────────────────────
        // UO = upper outer, UI = upper inner, LO = lower outer, LI = lower inner
        var uo = new (float x,float y,float z)[n];
        var ui = new (float x,float y,float z)[n];
        var lo = new (float x,float y,float z)[n];
        var li = new (float x,float y,float z)[n];

        for (int i = 0; i < n; i++)
        {
            var u = upperCurve[i]; var l = lowerCurve[i];
            var o = outward[i];
            // Upper face: push into teeth by penetrationMm upward (+Z)
            float uz = u.z + penetrationMm;
            float lz = l.z - penetrationMm;
            uo[i] = (u.x + o.x * half, u.y + o.y * half, uz);
            ui[i] = (u.x - o.x * half, u.y - o.y * half, uz);
            lo[i] = (l.x + o.x * half, l.y + o.y * half, lz);
            li[i] = (l.x - o.x * half, l.y - o.y * half, lz);
        }

        // ── 3. Project upper/lower faces onto tooth surfaces ──────────────
        if (upperMesh != null && upperMesh.Length >= 9)
            ProjectFaceOntoMesh(uo, upperMesh, penetrationMm, +1f);
        if (upperMesh != null && upperMesh.Length >= 9)
            ProjectFaceOntoMesh(ui, upperMesh, penetrationMm, +1f);
        if (lowerMesh != null && lowerMesh.Length >= 9)
            ProjectFaceOntoMesh(lo, lowerMesh, penetrationMm, -1f);
        if (lowerMesh != null && lowerMesh.Length >= 9)
            ProjectFaceOntoMesh(li, lowerMesh, penetrationMm, -1f);

        // ── 4. Triangulate the solid ──────────────────────────────────────
        var tris = new List<float>(n * 6 * 2 * 9);

        void AddQuad((float x,float y,float z) a, (float x,float y,float z) b,
                     (float x,float y,float z) c, (float x,float y,float z) d)
        {
            // Triangle 1: a-b-c
            tris.Add(a.x); tris.Add(a.y); tris.Add(a.z);
            tris.Add(b.x); tris.Add(b.y); tris.Add(b.z);
            tris.Add(c.x); tris.Add(c.y); tris.Add(c.z);
            // Triangle 2: a-c-d
            tris.Add(a.x); tris.Add(a.y); tris.Add(a.z);
            tris.Add(c.x); tris.Add(c.y); tris.Add(c.z);
            tris.Add(d.x); tris.Add(d.y); tris.Add(d.z);
        }

        for (int i = 0; i < n - 1; i++)
        {
            int j = i + 1;
            // Outer wall
            AddQuad(uo[i], uo[j], lo[j], lo[i]);
            // Inner wall (reversed winding)
            AddQuad(ui[i], li[i], li[j], ui[j]);
            // Upper face
            AddQuad(ui[i], uo[i], uo[j], ui[j]);
            // Lower face (reversed)
            AddQuad(li[i], li[j], lo[j], lo[i]);
        }

        // End caps
        void AddCap((float x,float y,float z) uo_, (float x,float y,float z) ui_,
                    (float x,float y,float z) lo_, (float x,float y,float z) li_, bool flip)
        {
            if (!flip) { AddQuad(uo_, ui_, li_, lo_); }
            else       { AddQuad(ui_, uo_, lo_, li_); }
        }
        AddCap(uo[0],   ui[0],   lo[0],   li[0],   false);
        AddCap(uo[n-1], ui[n-1], lo[n-1], li[n-1], true);

        return tris.ToArray();
    }

    /// <summary>
    /// For each vertex in <paramref name="strip"/>, find the nearest triangle
    /// vertex on <paramref name="mesh"/> and move the strip vertex to the
    /// mesh vertex offset by <paramref name="penetrationMm"/> in <paramref name="dir"/> (+1 = up, -1 = down).
    /// Uses a KD-tree on the mesh vertex centroids.
    /// </summary>
    private static void ProjectFaceOntoMesh(
        (float x, float y, float z)[] strip,
        float[] mesh, float penetrationMm, float dir)
    {
        // Build KD-tree from mesh triangle centroids
        int triCount = mesh.Length / 9;
        var pts = new List<float[]>(triCount * 3);
        for (int i = 0; i + 8 < mesh.Length; i += 9)
        {
            pts.Add(new[] { mesh[i],   mesh[i+1], mesh[i+2] });
            pts.Add(new[] { mesh[i+3], mesh[i+4], mesh[i+5] });
            pts.Add(new[] { mesh[i+6], mesh[i+7], mesh[i+8] });
        }

        if (pts.Count == 0) return;

        var kd = new KdTree();
        kd.Build(pts);

        for (int i = 0; i < strip.Length; i++)
        {
            var (ni, _) = kd.FindNearest(strip[i].x, strip[i].y, strip[i].z);
            var (nx, ny, nz) = kd.GetPoint(ni);
            // Move the strip vertex Z to the mesh surface + penetration in direction
            strip[i] = (strip[i].x, strip[i].y, nz + dir * penetrationMm);
        }
    }

    /// <summary>Generate a preview line strip (flat float[], pairs of x,y,z)
    /// suitable for a LineGeometryModel3D.</summary>
    public static float[] CurveToLineStrip(List<(float x, float y, float z)> curve)
    {
        if (curve.Count < 2) return Array.Empty<float>();
        var buf = new float[(curve.Count - 1) * 6];
        for (int i = 0; i < curve.Count - 1; i++)
        {
            int b = i * 6;
            buf[b]   = curve[i].x;   buf[b+1] = curve[i].y;   buf[b+2] = curve[i].z;
            buf[b+3] = curve[i+1].x; buf[b+4] = curve[i+1].y; buf[b+5] = curve[i+1].z;
        }
        return buf;
    }
}
