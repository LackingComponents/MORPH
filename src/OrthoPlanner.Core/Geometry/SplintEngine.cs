using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using ManifoldNET;

namespace OrthoPlanner.Core.Geometry;

// ── Arch Curve (Catmull-Rom) ──────────────────────────────────────────────
public class ArchCurve
{
    private readonly List<(float x, float y, float z)> _ctrl = new();
    public int ControlPointCount => _ctrl.Count;

    public void AddPoint(float x, float y, float z) => _ctrl.Add((x, y, z));
    public void RemoveLast()  { if (_ctrl.Count > 0) _ctrl.RemoveAt(_ctrl.Count - 1); }
    public void RemoveAt(int i) { if (i >= 0 && i < _ctrl.Count) _ctrl.RemoveAt(i); }
    public void Clear() => _ctrl.Clear();
    public (float x,float y,float z) GetPoint(int i) => _ctrl[i];
    public void UpdatePoint(int i, float x, float y, float z)
    {
        if (i >= 0 && i < _ctrl.Count) _ctrl[i] = (x, y, z);
    }

    public List<(float x, float y, float z)> Sample(int n = 200)
    {
        var result = new List<(float, float, float)>(n);
        if (_ctrl.Count < 2) return result;

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
            result.Add(CatmullRom(pts[seg], pts[seg+1], pts[seg+2], pts[seg+3], u));
        }
        return result;
    }

    private static (float, float, float) Mirror((float x,float y,float z) a,(float x,float y,float z) b)
        => (2*a.x-b.x, 2*a.y-b.y, 2*a.z-b.z);

    private static (float x,float y,float z) CatmullRom(
        (float x,float y,float z) p0,(float x,float y,float z) p1,
        (float x,float y,float z) p2,(float x,float y,float z) p3, float t)
    {
        float t2=t*t, t3=t2*t;
        return (
            0.5f*((2*p1.x)+(-p0.x+p2.x)*t+(2*p0.x-5*p1.x+4*p2.x-p3.x)*t2+(-p0.x+3*p1.x-3*p2.x+p3.x)*t3),
            0.5f*((2*p1.y)+(-p0.y+p2.y)*t+(2*p0.y-5*p1.y+4*p2.y-p3.y)*t2+(-p0.y+3*p1.y-3*p2.y+p3.y)*t3),
            0.5f*((2*p1.z)+(-p0.z+p2.z)*t+(2*p0.z-5*p1.z+4*p2.z-p3.z)*t2+(-p0.z+3*p1.z-3*p2.z+p3.z)*t3));
    }
}

// ── Splint Engine ─────────────────────────────────────────────────────────
public static class SplintEngine
{
    // ── Watertight check ─────────────────────────────────────────────────
    public static float WatertightScore(float[] mesh)
    {
        if (mesh == null || mesh.Length < 9) return 1f;
        var edgeCounts = new Dictionary<(long,long),int>();
        long Key(float v) => (long)Math.Round(v * 10.0);
        long Vtx(float x,float y,float z) => Key(x)*1_000_003L ^ Key(y)*997L ^ Key(z);
        for (int i = 0; i+8 < mesh.Length; i += 9)
        {
            long v0=Vtx(mesh[i],mesh[i+1],mesh[i+2]);
            long v1=Vtx(mesh[i+3],mesh[i+4],mesh[i+5]);
            long v2=Vtx(mesh[i+6],mesh[i+7],mesh[i+8]);
            foreach (var e in new[]{(Math.Min(v0,v1),Math.Max(v0,v1)),
                                    (Math.Min(v1,v2),Math.Max(v1,v2)),
                                    (Math.Min(v2,v0),Math.Max(v2,v0))})
            { edgeCounts.TryGetValue(e,out int c); edgeCounts[e]=c+1; }
        }
        int boundary=0;
        foreach (var kv in edgeCounts) if (kv.Value!=2) boundary++;
        return edgeCounts.Count==0 ? 1f : (float)boundary/edgeCounts.Count;
    }

    // ── Per-ring outward normals (perpendicular to arch tangent in XY) ───
    private static (float x,float y,float z)[] ComputeNormals(
        List<(float x,float y,float z)> curve, int n)
    {
        // Arch centroid in XY — used to ensure normals point AWAY from the arch center
        float cx = 0, cy = 0;
        foreach (var p in curve) { cx += p.x; cy += p.y; }
        cx /= curve.Count; cy /= curve.Count;

        var result = new (float x,float y,float z)[n];
        for (int i = 0; i < n; i++)
        {
            int prev=Math.Max(0,i-1), next=Math.Min(n-1,i+1);
            float tx=curve[next].x-curve[prev].x, ty=curve[next].y-curve[prev].y;
            float len=MathF.Sqrt(tx*tx+ty*ty);
            if (len<1e-6f){tx=1;ty=0;} else {tx/=len;ty/=len;}

            // N = T × Z = (ty, -tx, 0)
            float nx=ty, ny=-tx;

            // Flip if pointing toward centroid instead of away
            float dcx=curve[i].x-cx, dcy=curve[i].y-cy;
            if (nx*dcx + ny*dcy < 0) { nx=-nx; ny=-ny; }

            result[i]=(nx,ny,0f);
        }
        return result;
    }

    // ── Flat ribbon mesh — shows labio-lingual footprint on arch surface ──
    /// <summary>Returns a thin flat ribbon along the arch showing the LL width.
    /// Used for live preview before Generate is clicked.</summary>
    public static float[] GenerateRibbonMesh(
        List<(float x,float y,float z)> archCurve, float labiolingualMm)
    {
        int n = archCurve.Count;
        if (n < 2) return Array.Empty<float>();
        float half = labiolingualMm * 0.5f;
        var nor = ComputeNormals(archCurve, n);

        var outer = new (float x,float y,float z)[n];
        var inner = new (float x,float y,float z)[n];
        for (int i=0; i<n; i++)
        {
            var p = archCurve[i]; var N = nor[i];
            outer[i] = (p.x+N.x*half, p.y+N.y*half, p.z+0.3f);
            inner[i] = (p.x-N.x*half, p.y-N.y*half, p.z+0.3f);
        }

        var tris = new List<float>(n*12);
        for (int i=0; i<n-1; i++)
        {
            // top face
            AddQuad(tris, inner[i],outer[i],outer[i+1],inner[i+1]);
            // back face (so it's visible from below too)
            AddQuad(tris, outer[i],inner[i],inner[i+1],outer[i+1]);
        }
        return tris.ToArray();
    }

    // ── Horseshoe solid ───────────────────────────────────────────────────
    /// <summary>
    /// Generates a clean horseshoe-shaped splint solid.
    /// The top surface follows the upper arch curve (tooth contact).
    /// The bottom surface is offset downward by thicknessMm.
    /// Width is labiolingualMm.
    /// </summary>

    /// <summary>
    /// Re-samples a curve at <paramref name="n"/> uniformly-spaced arc-length positions.
    /// This ensures upper[i] and lower[i] correspond to the same fractional position
    /// along the arch, regardless of where the user placed control points.
    /// </summary>
    private static List<(float x,float y,float z)> ResampleByArcLength(
        List<(float x,float y,float z)> curve, int n)
    {
        int m = curve.Count;
        if (m == 0) return new();
        if (m == 1) return Enumerable.Repeat(curve[0], n).ToList();

        // Build cumulative arc-length table
        var lens = new float[m];
        for (int i = 1; i < m; i++)
        {
            float dx=curve[i].x-curve[i-1].x, dy=curve[i].y-curve[i-1].y, dz=curve[i].z-curve[i-1].z;
            lens[i] = lens[i-1] + MathF.Sqrt(dx*dx + dy*dy + dz*dz);
        }
        float total = lens[m-1];
        if (total < 1e-6f) return Enumerable.Repeat(curve[0], n).ToList();
        for (int i = 0; i < m; i++) lens[i] /= total;  // normalize to [0,1]

        var result = new List<(float,float,float)>(n);
        int seg = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Math.Max(n-1, 1);
            while (seg < m-2 && lens[seg+1] < t) seg++;
            float span = lens[seg+1] - lens[seg];
            float u = span < 1e-8f ? 0f : Math.Clamp((t - lens[seg]) / span, 0f, 1f);
            var a=curve[seg]; var b=curve[Math.Min(seg+1,m-1)];
            result.Add((a.x+u*(b.x-a.x), a.y+u*(b.y-a.y), a.z+u*(b.z-a.z)));
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GENERATE SPLINT — correct closed-solid boolean pipeline
    //
    //  Step A  Dilate upper mesh 1mm, clip below upperZ, cap at upperZ → closed solid
    //  Step B  Same for lower (clip above lowerZ, cap at lowerZ)          → closed solid
    //  Step C  horseshoe ∪ solidUpper1mm ∪ solidLower1mm (all closed)    → splint blank
    //  Step D  Dilate 0.1mm versions, clip+cap → closed subtraction tools
    //  Step E  blank − upper0.1mm − lower0.1mm                           → final splint
    // ═══════════════════════════════════════════════════════════════════════
    public static float[] GenerateSplint(
        List<(float x,float y,float z)> upperCurve,
        List<(float x,float y,float z)> lowerCurve,
        float labiolingualMm     = 8f,
        float upperPenetrationMm = 0f,   // UI only — not used here
        float lowerPenetrationMm = 0f,   // UI only — not used here
        float[]? upperMesh       = null,
        float[]? lowerMesh       = null,
        int sampleCount          = 160)
    {
        if (upperCurve.Count < 2 || lowerCurve.Count < 2) return Array.Empty<float>();

        var upper = ResampleByArcLength(upperCurve, sampleCount);
        var lower = ResampleByArcLength(lowerCurve, sampleCount);
        int n = upper.Count;
        if (n < 2) return Array.Empty<float>();

        // Align direction
        {
            var u0=upper[0]; var u1=upper[n-1]; var l0=lower[0]; var l1=lower[n-1];
            if (Sq(u0.x-l1.x)+Sq(u0.y-l1.y)+Sq(u1.x-l0.x)+Sq(u1.y-l0.y) <
                Sq(u0.x-l0.x)+Sq(u0.y-l0.y)+Sq(u1.x-l1.x)+Sq(u1.y-l1.y)) lower.Reverse();
        }

        float half = labiolingualMm * 0.5f;
        var norU = ComputeNormals(upper, n);
        var norL = ComputeNormals(lower, n);

        var TO = new (float x,float y,float z)[n]; var TI = new (float x,float y,float z)[n];
        var BO = new (float x,float y,float z)[n]; var BI = new (float x,float y,float z)[n];
        for (int i = 0; i < n; i++)
        {
            var u=upper[i]; var nu=norU[i]; var l=lower[i]; var nl=norL[i];
            TO[i]=(u.x+nu.x*half, u.y+nu.y*half, u.z); TI[i]=(u.x-nu.x*half, u.y-nu.y*half, u.z);
            BO[i]=(l.x+nl.x*half, l.y+nl.y*half, l.z); BI[i]=(l.x-nl.x*half, l.y-nl.y*half, l.z);
        }

        // ── Build horseshoe (closed by construction) ─────────────────────────
        var horseshoeTris = new List<float>(n * 6 * 2 * 9);
        for (int i = 0; i < n-1; i++)
        {
            int j=i+1;
            AddQuad(horseshoeTris, TO[i], TO[j], BO[j], BO[i]);   // outer wall
            AddQuad(horseshoeTris, TI[j], TI[i], BI[i], BI[j]);   // inner wall
            AddQuad(horseshoeTris, TI[i], TO[i], TO[j], TI[j]);   // top face
            AddQuad(horseshoeTris, BO[i], BO[j], BI[j], BI[i]);   // bottom face
        }
        AddQuad(horseshoeTris, TO[0],   TI[0],   BI[0],   BO[0]);
        AddQuad(horseshoeTris, TI[n-1], TO[n-1], BO[n-1], BI[n-1]);
        float[] horseshoeFlat = horseshoeTris.ToArray();

        if (upperMesh == null || upperMesh.Length < 9 ||
            lowerMesh == null || lowerMesh.Length < 9)
            return horseshoeFlat;

        try
        {
            float upperZ = upper.Max(p => p.z);
            float lowerZ = lower.Min(p => p.z);
            const float CrownMm = 15f;   // crown depth to include
            const float Dil1mm  = 1.0f;  // outer skin dilation
            const float Dil01mm = 0.1f;  // pocket clearance

            // ── Z-clip ─────────────────────────────────────────────────────
            float[] ClipZ(float[] mesh, float zMin, float zMax)
            {
                var r = new List<float>(mesh.Length / 3);
                for (int i = 0; i+8 < mesh.Length; i += 9)
                {
                    float cz = (mesh[i+2]+mesh[i+5]+mesh[i+8]) / 3f;
                    if (cz >= zMin && cz <= zMax)
                        for (int k=0; k<9; k++) r.Add(mesh[i+k]);
                }
                return r.ToArray();
            }

            // ── Cap open Z-cut boundary with centroid-fan triangulation ─────
            // This seals ONLY the cut plane boundary so the mesh becomes a
            // closed solid. It does NOT fill the tooth pockets — those are
            // created later by boolean subtraction.
            float[] CapAtZ(float[] mesh, float capZ)
            {
                const float SnapEps = 0.6f;
                var arr = (float[])mesh.Clone();
                for (int i=2; i<arr.Length; i+=3)
                    if (MathF.Abs(arr[i]-capZ) < SnapEps) arr[i] = capZ;

                // Build directed half-edge sets
                var rev = new HashSet<long>();
                var vpos = new Dictionary<long,(float x,float y)>();
                var allFwd = new List<(long a, long b, int triBase, int edgeIdx)>();

                long Qk(float x,float y,float z) => unchecked(
                    (long)Math.Round(x*200)*1_000_000_007L ^
                    (long)Math.Round(y*200)*999_999_937L   ^
                    (long)Math.Round(z*200));
                long Ek(long a,long b) => unchecked(a*1_000_000_007L ^ b);

                for (int i=0; i+8<arr.Length; i+=9)
                {
                    long[] ks = { Qk(arr[i],arr[i+1],arr[i+2]),
                                  Qk(arr[i+3],arr[i+4],arr[i+5]),
                                  Qk(arr[i+6],arr[i+7],arr[i+8]) };
                    for (int v=0; v<3; v++)
                        vpos[ks[v]] = (arr[i+v*3], arr[i+v*3+1]);
                    for (int e=0; e<3; e++)
                    {
                        allFwd.Add((ks[e], ks[(e+1)%3], i, e));
                        rev.Add(Ek(ks[(e+1)%3], ks[e]));
                    }
                }

                // Collect boundary edges at capZ (no matching reverse edge)
                var boundNext = new Dictionary<long,long>();
                foreach (var (a, b, triBase, edgeIdx) in allFwd)
                {
                    if (rev.Contains(Ek(a,b))) continue;  // interior edge
                    float za = arr[triBase + edgeIdx*3 + 2];
                    float zb = arr[triBase + ((edgeIdx+1)%3)*3 + 2];
                    if (MathF.Abs(za-capZ) < 0.01f && MathF.Abs(zb-capZ) < 0.01f)
                        boundNext[a] = b;
                }

                var capTris = new List<float>();
                var visited = new HashSet<long>();
                foreach (var startKey in boundNext.Keys)
                {
                    if (visited.Contains(startKey)) continue;
                    var loop = new List<long>();
                    long cur = startKey;
                    while (!visited.Contains(cur) && boundNext.TryGetValue(cur, out long nxt))
                    { visited.Add(cur); loop.Add(cur); cur = nxt; }
                    if (loop.Count < 3) continue;

                    float cx=0, cy=0;
                    foreach (var k in loop) { cx+=vpos[k].x; cy+=vpos[k].y; }
                    cx /= loop.Count; cy /= loop.Count;

                    for (int i=0; i<loop.Count; i++)
                    {
                        var va = vpos[loop[i]]; var vb = vpos[loop[(i+1)%loop.Count]];
                        capTris.Add(cx);   capTris.Add(cy);   capTris.Add(capZ);
                        capTris.Add(va.x); capTris.Add(va.y); capTris.Add(capZ);
                        capTris.Add(vb.x); capTris.Add(vb.y); capTris.Add(capZ);
                    }
                }
                return arr.Concat(capTris).ToArray();
            }

            // ── Convert flat triangle soup to ManifoldNET Manifold ──────────
            // Manifold.Create() internally merges coincident vertices and
            // validates the mesh — no separate CloseHoles needed.
            Manifold ToManifold(float[] flat)
            {
                int triCount = flat.Length / 9;
                // vertProperties: interleaved x,y,z for each vertex (unindexed)
                // ManifoldNET will weld coincident vertices internally
                var verts = new float[triCount * 3 * 3];
                var tris  = new uint[triCount * 3];
                for (int i=0; i<triCount; i++)
                {
                    int b = i*9;
                    for (int k=0; k<9; k++) verts[i*9+k] = flat[b+k];
                    tris[i*3]   = (uint)(i*3);
                    tris[i*3+1] = (uint)(i*3+1);
                    tris[i*3+2] = (uint)(i*3+2);
                }
                // Use MeshGLData wrapper — passes numProp as uint, tangents as null (no smoothing)
                var data   = new MeshGLData(verts, tris, 3u, null!);
                var meshGL = new MeshGL(data);
                return Manifold.Create(meshGL);
            }

            // ── Convert Manifold result back to flat triangle soup ──────────
            float[] FromManifold(Manifold m)
            {
                var mg    = m.MeshGL;
                var vp    = mg.VerticesProperties; // x,y,z,x,y,z,...
                var tv    = mg.TriangleVertices;   // i0,i1,i2,...
                int nTri  = (int)mg.TriangleNumber;
                var r     = new float[nTri * 9];
                int numProp = (int)mg.PropertiesNumber;
                for (int i=0; i<nTri; i++)
                    for (int v=0; v<3; v++)
                    {
                        int vi = (int)tv[i*3+v];
                        r[i*9+v*3]   = vp[vi*numProp];
                        r[i*9+v*3+1] = vp[vi*numProp+1];
                        r[i*9+v*3+2] = vp[vi*numProp+2];
                    }
                return r;
            }

            // Build a closed solid: dilate mesh → clip at capZ → cap the cut boundary
            float[] MakeSolid(float[] mesh, float dilMm, float capZ, bool keepBelow)
            {
                float[] dilated = OffsetMeshVertices(mesh, dilMm);
                float[] clipped = keepBelow
                    ? ClipZ(dilated, capZ - CrownMm, capZ + 0.1f)
                    : ClipZ(dilated, capZ - 0.1f,    capZ + CrownMm);
                if (clipped.Length < 9) return Array.Empty<float>();
                return CapAtZ(clipped, capZ);
            }

            // ── Step A/B: closed 1mm-dilated solids (outer skin) ───────────
            float[] solidUpper1  = MakeSolid(upperMesh, Dil1mm,  upperZ, keepBelow:true);
            float[] solidLower1  = MakeSolid(lowerMesh, Dil1mm,  lowerZ, keepBelow:false);
            if (solidUpper1.Length < 9 || solidLower1.Length < 9) return horseshoeFlat;

            // ── Step C: union via ManifoldNET (fast, guaranteed manifold) ──
            var mHorse  = ToManifold(horseshoeFlat);
            var mUp1    = ToManifold(solidUpper1);
            var mLo1    = ToManifold(solidLower1);
            var blank   = Manifold.Union(Manifold.Union(mHorse, mUp1), mLo1);
            if (blank.Status != ManifoldError.NoError) return horseshoeFlat;

            // ── Step D/E: 0.1mm tools → subtract to create pockets ─────────
            float[] solidUpper01 = MakeSolid(upperMesh, Dil01mm, upperZ, keepBelow:true);
            float[] solidLower01 = MakeSolid(lowerMesh, Dil01mm, lowerZ, keepBelow:false);

            if (solidUpper01.Length >= 9)
            {
                var tool = ToManifold(solidUpper01);
                var sub  = Manifold.Difference(blank, tool);
                if (sub.Status == ManifoldError.NoError) blank = sub;
            }
            if (solidLower01.Length >= 9)
            {
                var tool = ToManifold(solidLower01);
                var sub  = Manifold.Difference(blank, tool);
                if (sub.Status == ManifoldError.NoError) blank = sub;
            }

            return FromManifold(blank);
        }
        catch { return horseshoeFlat; }
    }

    private static float Sq(float v) => v * v;


    // ── Isotropic mesh offset via geometry3Sharp ──────────────────────────
    /// <summary>
    /// Offsets every vertex of the triangle soup by <paramref name="offsetMm"/> mm
    /// along its area-weighted vertex normal, using geometry3Sharp for robust
    /// per-vertex normal computation on non-watertight meshes.
    /// </summary>
    private static float[] OffsetMeshVertices(float[] mesh, float offsetMm)
    {
        if (mesh == null || mesh.Length < 9) return mesh ?? Array.Empty<float>();

        // ── Build a DMesh3 (indexed) from the flat triangle soup ──────────
        var dm = new DMesh3(MeshComponents.VertexNormals);
        var vmap = new Dictionary<Vector3f, int>();

        int GetV(float x, float y, float z)
        {
            var key = new Vector3f(x, y, z);
            if (!vmap.TryGetValue(key, out int vi))
            {
                vi = dm.AppendVertex(new Vector3d(x, y, z));
                vmap[key] = vi;
            }
            return vi;
        }

        int triCount = mesh.Length / 9;
        for (int i = 0; i < triCount; i++)
        {
            int b = i * 9;
            int a_ = GetV(mesh[b],   mesh[b+1], mesh[b+2]);
            int b_ = GetV(mesh[b+3], mesh[b+4], mesh[b+5]);
            int c_ = GetV(mesh[b+6], mesh[b+7], mesh[b+8]);
            if (a_ != b_ && b_ != c_ && a_ != c_)
                dm.AppendTriangle(a_, b_, c_);
        }

        // ── Compute per-vertex normals (area-weighted, handles open meshes) ─
        MeshNormals.QuickCompute(dm);

        // ── Offset each vertex along its normal ───────────────────────────
        foreach (int vid in dm.VertexIndices())
        {
            Vector3d pos = dm.GetVertex(vid);
            Vector3f nor = dm.GetVertexNormal(vid);
            dm.SetVertex(vid, pos + new Vector3d(nor.x, nor.y, nor.z) * offsetMm);
        }

        // ── Rebuild flat triangle soup ────────────────────────────────────
        var result = new float[triCount * 9];
        int ri = 0;
        foreach (int tid in dm.TriangleIndices())
        {
            Index3i tri = dm.GetTriangle(tid);
            Vector3d va = dm.GetVertex(tri.a);
            Vector3d vb = dm.GetVertex(tri.b);
            Vector3d vc = dm.GetVertex(tri.c);
            result[ri++]=(float)va.x; result[ri++]=(float)va.y; result[ri++]=(float)va.z;
            result[ri++]=(float)vb.x; result[ri++]=(float)vb.y; result[ri++]=(float)vb.z;
            result[ri++]=(float)vc.x; result[ri++]=(float)vc.y; result[ri++]=(float)vc.z;
        }
        // Trim if some degenerate tris were skipped
        if (ri < result.Length) Array.Resize(ref result, ri);
        return result;
    }

    // ── Tooth pocket: crop + side-wall prisms ────────────────────────────
    /// <summary>
    /// Collects triangles from the offset tooth mesh that fall inside the
    /// horseshoe XY footprint AND within penetrationMm of the horseshoe
    /// reference surface, then caps BOUNDARY edges only with prism walls.
    /// </summary>
    private static void BuildToothPocket(
        float[] offsetMesh,
        List<(float x,float y,float z)> archCurve,
        float llWidth, float penetrationMm, bool isUpper,
        List<float> tris)
    {
        float half  = llWidth * 0.5f;
        float half2 = half * half;

        float NearestZ(float px, float py)
        {
            float bestD=float.MaxValue, bestZ=0f;
            foreach(var pt in archCurve)
            { float dx=px-pt.x,dy=py-pt.y,d=dx*dx+dy*dy; if(d<bestD){bestD=d;bestZ=pt.z;} }
            return bestZ;
        }
        bool InFootprint(float px, float py)
        {
            float bestD=float.MaxValue;
            foreach(var pt in archCurve){ float dx=px-pt.x,dy=py-pt.y,d=dx*dx+dy*dy; if(d<bestD)bestD=d; }
            return bestD<=half2;
        }

        // edge key: symmetric (undirected), stores first-seen directed edge + zRef for wall winding
        var edgeMap = new Dictionary<long,(int count,float x1,float y1,float z1,float x2,float y2,float z2,float zRef)>();

        long EKey(float x1,float y1,float z1,float x2,float y2,float z2)
        {
            long a = (long)Math.Round(x1*50)*1_000_033L ^ (long)Math.Round(y1*50)*999_983L ^ (long)Math.Round(z1*50)*1_000_003L;
            long b = (long)Math.Round(x2*50)*1_000_033L ^ (long)Math.Round(y2*50)*999_983L ^ (long)Math.Round(z2*50)*1_000_003L;
            return a < b ? a*31337L ^ b : b*31337L ^ a;
        }
        void TrackEdge(float x1,float y1,float z1,float x2,float y2,float z2,float zRef)
        {
            long k = EKey(x1,y1,z1,x2,y2,z2);
            edgeMap.TryGetValue(k, out var prev);
            edgeMap[k] = (prev.count+1, x1,y1,z1, x2,y2,z2, zRef);
        }

        for(int i=0; i+8<offsetMesh.Length; i+=9)
        {
            float ax=offsetMesh[i],   ay=offsetMesh[i+1], az=offsetMesh[i+2];
            float bx=offsetMesh[i+3], by=offsetMesh[i+4], bz=offsetMesh[i+5];
            float cx_=offsetMesh[i+6],cy=offsetMesh[i+7], cz=offsetMesh[i+8];
            float pcx=(ax+bx+cx_)/3f, pcy=(ay+by+cy)/3f, pcz=(az+bz+cz)/3f;

            if(!InFootprint(pcx,pcy)) continue;

            float zRef = NearestZ(pcx,pcy);
            if(isUpper)
            {
                // Maxilla: keep triangles ABOVE arch line up to penetrationMm
                if(pcz < zRef || pcz > zRef + penetrationMm) continue;
            }
            else
            {
                // Mandible: keep triangles BELOW arch line down to penetrationMm
                if(pcz > zRef || pcz < zRef - penetrationMm) continue;
            }

            // Add tooth-surface triangle with cavity-facing winding
            if(isUpper)
            {
                tris.Add(ax);tris.Add(ay);tris.Add(az);
                tris.Add(cx_);tris.Add(cy);tris.Add(cz);
                tris.Add(bx);tris.Add(by);tris.Add(bz);
            }
            else
            {
                tris.Add(ax);tris.Add(ay);tris.Add(az);
                tris.Add(bx);tris.Add(by);tris.Add(bz);
                tris.Add(cx_);tris.Add(cy);tris.Add(cz);
            }

            // Track all three edges for boundary detection
            TrackEdge(ax,ay,az, bx,by,bz, zRef);
            TrackEdge(bx,by,bz, cx_,cy,cz, zRef);
            TrackEdge(cx_,cy,cz, ax,ay,az, zRef);
        }

        // Emit prism walls ONLY for boundary edges (count == 1)
        foreach(var kv in edgeMap)
        {
            if(kv.Value.count != 1) continue;
            var (_,x1,y1,z1, x2,y2,z2, zRef) = kv.Value;
            var p1r=(x1,y1,zRef); var p2r=(x2,y2,zRef);
            if(isUpper)
                AddQuad(tris,(x2,y2,z2),(x1,y1,z1),p1r,p2r);
            else
                AddQuad(tris,(x1,y1,z1),(x2,y2,z2),p2r,p1r);
        }
    }

    private static void AddQuad(List<float> t,
        (float x,float y,float z) a,(float x,float y,float z) b,
        (float x,float y,float z) c,(float x,float y,float z) d)
    {
        t.Add(a.x);t.Add(a.y);t.Add(a.z);
        t.Add(b.x);t.Add(b.y);t.Add(b.z);
        t.Add(c.x);t.Add(c.y);t.Add(c.z);
        t.Add(a.x);t.Add(a.y);t.Add(a.z);
        t.Add(c.x);t.Add(c.y);t.Add(c.z);
        t.Add(d.x);t.Add(d.y);t.Add(d.z);
    }

    public static float[] CurveToLineStrip(List<(float x,float y,float z)> curve)
    {
        if (curve.Count < 2) return Array.Empty<float>();
        var buf = new float[(curve.Count-1)*6];
        for (int i=0; i<curve.Count-1; i++)
        {
            int b=i*6;
            buf[b]=curve[i].x;buf[b+1]=curve[i].y;buf[b+2]=curve[i].z;
            buf[b+3]=curve[i+1].x;buf[b+4]=curve[i+1].y;buf[b+5]=curve[i+1].z;
        }
        return buf;
    }
}
