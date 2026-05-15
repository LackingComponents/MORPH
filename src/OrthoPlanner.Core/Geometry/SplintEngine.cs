using System;
using System.Collections.Generic;
using System.Linq;
using g3;

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

    public static float[] GenerateSplint(
        List<(float x,float y,float z)> upperCurve,
        List<(float x,float y,float z)> lowerCurve,
        float labiolingualMm = 8f,
        float penetrationMm  = 3f,
        float[]? upperMesh   = null,
        float[]? lowerMesh   = null,
        int sampleCount      = 160)
    {
        if (upperCurve.Count < 2 || lowerCurve.Count < 2) return Array.Empty<float>();

        // Re-sample both curves at the SAME arc-length positions so
        // upper[i] and lower[i] correspond to the same fractional position
        // along the arch — this is what makes the horseshoe connect cleanly.
        var upper = ResampleByArcLength(upperCurve, sampleCount);
        var lower = ResampleByArcLength(lowerCurve, sampleCount);
        int n = upper.Count;
        if (n < 2) return Array.Empty<float>();

        // ── Direction alignment ──────────────────────────────────────────────
        // The upper arch is viewed from BELOW (camera flipped), so the user
        // traverses it in the opposite anatomical direction to the lower arch.
        // Compare endpoint distances to detect and correct the inversion.
        {
            var u0=upper[0]; var u1=upper[n-1];
            var l0=lower[0]; var l1=lower[n-1];
            float dxS=(u0.x-l0.x), dyS=(u0.y-l0.y);  // start-to-start
            float dxE=(u1.x-l1.x), dyE=(u1.y-l1.y);  // end-to-end
            float distSame = dxS*dxS+dyS*dyS + dxE*dxE+dyE*dyE;

            float dxSR=(u0.x-l1.x), dySR=(u0.y-l1.y); // start-to-end
            float dxER=(u1.x-l0.x), dyER=(u1.y-l0.y); // end-to-start
            float distRev  = dxSR*dxSR+dySR*dySR + dxER*dxER+dyER*dyER;

            if (distRev < distSame) lower.Reverse();
        }

        float half = labiolingualMm * 0.5f;

        // Per-ring outward normals from each curve
        var norU = ComputeNormals(upper, n);
        var norL = ComputeNormals(lower, n);

        // 4 corner strips:
        //   TO = top-outer  (upper arch, labial/buccal side)
        //   TI = top-inner  (upper arch, lingual/palatal side)
        //   BO = bot-outer  (lower arch, labial/buccal side)
        //   BI = bot-inner  (lower arch, lingual/palatal side)
        var TO = new (float x,float y,float z)[n];
        var TI = new (float x,float y,float z)[n];
        var BO = new (float x,float y,float z)[n];
        var BI = new (float x,float y,float z)[n];

        for (int i = 0; i < n; i++)
        {
            var u = upper[i]; var nu = norU[i];
            var l = lower[i]; var nl = norL[i];
            TO[i] = (u.x + nu.x*half, u.y + nu.y*half, u.z);
            TI[i] = (u.x - nu.x*half, u.y - nu.y*half, u.z);
            BO[i] = (l.x + nl.x*half, l.y + nl.y*half, l.z);
            BI[i] = (l.x - nl.x*half, l.y - nl.y*half, l.z);
        }

        var tris = new List<float>(n * 6 * 2 * 9);

        for (int i = 0; i < n - 1; i++)
        {
            int j = i + 1;
            AddQuad(tris, TO[i], TO[j], BO[j], BO[i]);   // outer wall
            AddQuad(tris, TI[j], TI[i], BI[i], BI[j]);   // inner wall
            // Top/bottom flat faces — these will be partially replaced by tooth pockets
            // but we keep them here as base; the pocket surface is additive (union visual)
            AddQuad(tris, TI[i], TO[i], TO[j], TI[j]);   // top face
            AddQuad(tris, BO[i], BO[j], BI[j], BI[i]);   // bottom face
        }
        // End caps
        AddQuad(tris, TO[0],   TI[0],   BI[0],   BO[0]);
        AddQuad(tris, TI[n-1], TO[n-1], BO[n-1], BI[n-1]);

        // ── Tooth pockets ───────────────────────────────────────────────────
        // 0.1 mm isotropic clearance offset + crop to horseshoe region + prism walls
        const float ClearanceMm = 0.1f;
        if (upperMesh != null && upperMesh.Length >= 9)
        {
            var offU = OffsetMeshVertices(upperMesh, ClearanceMm);
            BuildToothPocket(offU, upper, labiolingualMm, penetrationMm,
                             isUpper: true, tris);
        }
        if (lowerMesh != null && lowerMesh.Length >= 9)
        {
            var offL = OffsetMeshVertices(lowerMesh, ClearanceMm);
            BuildToothPocket(offL, lower, labiolingualMm, penetrationMm,
                             isUpper: false, tris);
        }

        return tris.ToArray();
    }

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
    /// reference surface, then caps each boundary edge with a prism wall
    /// down (upper) or up (lower) to the horseshoe reference Z.
    /// </summary>
    private static void BuildToothPocket(
        float[] offsetMesh,
        List<(float x,float y,float z)> archCurve,
        float llWidth, float penetrationMm, bool isUpper,
        List<float> tris)
    {
        float half = llWidth * 0.5f;
        float half2 = half * half;

        // For a given XY, find nearest arch point and return its Z
        float NearestZ(float px, float py)
        {
            float bestD=float.MaxValue, bestZ=0f;
            foreach(var pt in archCurve)
            {
                float dx=px-pt.x, dy=py-pt.y, d=dx*dx+dy*dy;
                if(d<bestD){bestD=d;bestZ=pt.z;}
            }
            return bestZ;
        }
        bool InFootprint(float px, float py)
        {
            float bestD=float.MaxValue;
            foreach(var pt in archCurve){ float dx=px-pt.x,dy=py-pt.y,d=dx*dx+dy*dy; if(d<bestD)bestD=d; }
            return bestD<=half2;
        }

        // Track boundary edges → each edge key maps to occurrence count
        var edgeCounts = new Dictionary<(int,int),int>();
        // Collected triangle vertices
        var patchTris = new List<(float ax,float ay,float az,
                                  float bx,float by,float bz,
                                  float cx_,float cy,float cz,
                                  float refZ)>();

        for(int i=0;i+8<offsetMesh.Length;i+=9)
        {
            float ax=offsetMesh[i],   ay=offsetMesh[i+1], az=offsetMesh[i+2];
            float bx=offsetMesh[i+3], by=offsetMesh[i+4], bz=offsetMesh[i+5];
            float cx_=offsetMesh[i+6],cy=offsetMesh[i+7], cz=offsetMesh[i+8];
            float pcx=(ax+bx+cx_)/3f, pcy=(ay+by+cy)/3f, pcz=(az+bz+cz)/3f;

            if(!InFootprint(pcx,pcy)) continue;

            float zRef = NearestZ(pcx,pcy);
            if(isUpper)
            {
                // Maxilla: arch line is the BOTTOM of the upper teeth (occlusal surface).
                // Penetration wraps ABOVE the arch line (into the tooth body).
                // Keep triangles where zRef ≤ pcz ≤ zRef + penetrationMm
                if(pcz < zRef || pcz > zRef + penetrationMm) continue;
            }
            else
            {
                // Mandible: arch line is the TOP of the lower teeth (occlusal surface).
                // Penetration wraps BELOW the arch line (into the tooth body).
                // Keep triangles where zRef - penetrationMm ≤ pcz ≤ zRef
                if(pcz > zRef || pcz < zRef - penetrationMm) continue;
            }

            // Add tooth-surface triangle.
            // For upper: tooth is ABOVE arch line, cavity face points DOWN → reverse winding
            // For lower: tooth is BELOW arch line, cavity face points UP → keep winding
            if(isUpper)
            {
                // Reversed: face the interior of the splint (downward)
                tris.Add(ax);tris.Add(ay);tris.Add(az);
                tris.Add(cx_);tris.Add(cy);tris.Add(cz);
                tris.Add(bx);tris.Add(by);tris.Add(bz);
            }
            else
            {
                // Normal winding: face the interior (upward)
                tris.Add(ax);tris.Add(ay);tris.Add(az);
                tris.Add(bx);tris.Add(by);tris.Add(bz);
                tris.Add(cx_);tris.Add(cy);tris.Add(cz);
            }

            // Track edges for boundary detection (using quantised vertex hash as int index)
            int VI(float x,float y,float z)
                => (int)(((long)Math.Round(x*10))*99991L ^ ((long)Math.Round(y*10))*999983L
                        ^ ((long)Math.Round(z*10))*9999991L) & 0x7FFFFFFF;
            patchTris.Add((ax,ay,az, bx,by,bz, cx_,cy,cz, zRef));
        }

        // For each collected triangle, build prism walls on its boundary edges.
        // An edge is "boundary" if it appears in only one triangle of the patch.
        // We use a simplified approach: for each triangle, add prism walls for
        // all three edges (double-counted interior edges will cancel by winding).
        // This is visually correct for the first iteration.
        foreach(var(ax,ay,az, bx,by,bz, cx_,cy,cz, zRef) in patchTris)
        {
            // Side wall: project outer edge vertex to horseshoe ref Z
            void WallEdge((float x,float y,float z) p1,(float x,float y,float z) p2)
            {
                // Projected versions at horseshoe reference Z
                var p1r = (p1.x, p1.y, zRef);
                var p2r = (p2.x, p2.y, zRef);
                if(isUpper)
                    // Tooth above arch; wall goes from tooth DOWN to zRef
                    AddQuad(tris, p2, p1, p1r, p2r);
                else
                    // Tooth below arch; wall goes from tooth UP to zRef
                    AddQuad(tris, p1, p2, p2r, p1r);
            }
            WallEdge((ax,ay,az),(bx,by,bz));
            WallEdge((bx,by,bz),(cx_,cy,cz));
            WallEdge((cx_,cy,cz),(ax,ay,az));
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
