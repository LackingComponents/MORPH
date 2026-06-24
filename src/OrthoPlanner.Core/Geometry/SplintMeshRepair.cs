using System;
using System.Collections.Generic;
using g3;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Turns a marching-cubes triangle soup into a closed, single-component manifold
/// suitable for printing. A printable splint must be a watertight solid; intended
/// tunnels (fixation/inspection/irrigation holes) stay manifold because they are
/// carved as through-tunnels in voxel space, not as surface boundaries.
///
/// This stage welds coincident vertices, keeps the largest connected component,
/// closes stray boundary loops left by voxel clipping, and reports the residual
/// open-edge fraction so the UI can gate printing.
/// </summary>
public static class SplintMeshRepair
{
    public readonly record struct RepairReport(
        float[] Vertices, bool IsManifold, float OpenEdgeFraction, int BoundaryLoopsFilled);

    /// <summary>Welds a flat (stride-9) triangle soup into an indexed DMesh3,
    /// snapping vertices to a 0.01 mm grid so shared edges become topological.</summary>
    public static DMesh3 BuildWelded(float[] soup)
    {
        var dm = new DMesh3();
        var vmap = new Dictionary<(int, int, int), int>(soup.Length / 9 * 3);

        int GetV(float x, float y, float z)
        {
            var k = ((int)MathF.Round(x * 100), (int)MathF.Round(y * 100), (int)MathF.Round(z * 100));
            if (!vmap.TryGetValue(k, out int vi))
            {
                vi = dm.AppendVertex(new Vector3d(x, y, z));
                vmap[k] = vi;
            }
            return vi;
        }

        for (int i = 0; i + 8 < soup.Length; i += 9)
        {
            int a = GetV(soup[i],     soup[i + 1], soup[i + 2]);
            int b = GetV(soup[i + 3], soup[i + 4], soup[i + 5]);
            int c = GetV(soup[i + 6], soup[i + 7], soup[i + 8]);
            if (a != b && b != c && a != c)
                dm.AppendTriangle(a, b, c);   // non-manifold appends are skipped by g3
        }
        return dm;
    }

    /// <summary>Flattens an indexed DMesh3 back to a stride-9 triangle soup.</summary>
    public static float[] ToSoup(DMesh3 dm)
    {
        var res = new float[dm.TriangleCount * 9];
        int ri = 0;
        foreach (int tid in dm.TriangleIndices())
        {
            Index3i t = dm.GetTriangle(tid);
            Vector3d va = dm.GetVertex(t.a), vb = dm.GetVertex(t.b), vc = dm.GetVertex(t.c);
            res[ri++] = (float)va.x; res[ri++] = (float)va.y; res[ri++] = (float)va.z;
            res[ri++] = (float)vb.x; res[ri++] = (float)vb.y; res[ri++] = (float)vb.z;
            res[ri++] = (float)vc.x; res[ri++] = (float)vc.y; res[ri++] = (float)vc.z;
        }
        if (ri < res.Length) Array.Resize(ref res, ri);
        return res;
    }

    /// <summary>
    /// Full repair pass. Returns the repaired soup plus a manifold report. On any
    /// failure it falls back to the input soup so generation never hard-fails here.
    /// </summary>
    public static RepairReport Repair(float[] soup)
    {
        if (soup == null || soup.Length < 9)
            return new RepairReport(soup ?? Array.Empty<float>(), false, 1f, 0);

        try
        {
            var dm = BuildWelded(soup);
            if (dm.TriangleCount == 0)
                return new RepairReport(soup, false, 1f, 0);

            KeepLargestComponent(dm);
            int filled = FillBoundaryLoops(dm);
            dm.CompactInPlace();

            float openFrac = OpenEdgeFraction(dm);
            bool manifold = dm.IsClosed() && openFrac <= 1e-6f;
            return new RepairReport(ToSoup(dm), manifold, openFrac, filled);
        }
        catch
        {
            return new RepairReport(soup, false, SplintEngine.WatertightScore(soup), 0);
        }
    }

    private static void KeepLargestComponent(DMesh3 dm)
    {
        var cc = new MeshConnectedComponents(dm);
        cc.FindConnectedT();
        if (cc.Count <= 1) return;

        int best = 0, bestSize = 0;
        for (int i = 0; i < cc.Count; i++)
            if (cc.Components[i].Indices.Length > bestSize)
            { bestSize = cc.Components[i].Indices.Length; best = i; }

        for (int i = 0; i < cc.Count; i++)
        {
            if (i == best) continue;
            foreach (int tid in cc.Components[i].Indices)
                if (dm.IsTriangle(tid)) dm.RemoveTriangle(tid);
        }
        dm.CompactInPlace();
    }

    private static int FillBoundaryLoops(DMesh3 dm)
    {
        int filled = 0;
        try
        {
            var loops = new MeshBoundaryLoops(dm);
            foreach (var loop in loops)
            {
                try
                {
                    var filler = new SimpleHoleFiller(dm, loop);
                    if (filler.Fill()) filled++;
                }
                catch { /* leave this loop open; reported via OpenEdgeFraction */ }
            }
        }
        catch { /* boundary extraction failed — report via OpenEdgeFraction */ }
        return filled;
    }

    private static float OpenEdgeFraction(DMesh3 dm)
    {
        int total = 0, boundary = 0;
        foreach (int eid in dm.EdgeIndices())
        {
            total++;
            if (dm.IsBoundaryEdge(eid)) boundary++;
        }
        return total == 0 ? 1f : (float)boundary / total;
    }
}
