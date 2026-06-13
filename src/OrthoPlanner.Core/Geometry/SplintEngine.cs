using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace OrthoPlanner.Core.Geometry;

// Ã¢â€â‚¬Ã¢â€â‚¬ Arch Curve (Catmull-Rom) Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

// Ã¢â€â‚¬Ã¢â€â‚¬ Splint Engine Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
public static class SplintEngine
{
    // Ã¢â€â‚¬Ã¢â€â‚¬ Watertight check Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

    // Ã¢â€â‚¬Ã¢â€â‚¬ Per-ring outward normals (perpendicular to arch tangent in XY) Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    private static (float x,float y,float z)[] ComputeNormals(
        List<(float x,float y,float z)> curve, int n)
    {
        // Arch centroid in XY Ã¢â‚¬â€ used to ensure normals point AWAY from the arch center
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

            // N = T Ãƒâ€” Z = (ty, -tx, 0)
            float nx=ty, ny=-tx;

            // Flip if pointing toward centroid instead of away
            float dcx=curve[i].x-cx, dcy=curve[i].y-cy;
            if (nx*dcx + ny*dcy < 0) { nx=-nx; ny=-ny; }

            result[i]=(nx,ny,0f);
        }
        return result;
    }

    // Ã¢â€â‚¬Ã¢â€â‚¬ Flat ribbon mesh Ã¢â‚¬â€ shows labio-lingual footprint on arch surface Ã¢â€â‚¬Ã¢â€â‚¬
    /// <summary>Returns a thin flat ribbon along the arch showing the LL width.
    /// Used for live preview before Generate is clicked.</summary>
    public static float[] GenerateRibbonMesh(
        List<(float x,float y,float z)> archCurve, float labiolingualMm,
        float lingualBuccalBiasMm = 0f)
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
            // Bias shifts the center outward (+) or inward (-) while keeping total width constant
            float biasedCx = p.x + N.x * lingualBuccalBiasMm;
            float biasedCy = p.y + N.y * lingualBuccalBiasMm;
            outer[i] = (biasedCx+N.x*half, biasedCy+N.y*half, p.z+0.3f);
            inner[i] = (biasedCx-N.x*half, biasedCy-N.y*half, p.z+0.3f);
        }

        var tris = new List<float>(n*12);
        for (int i=0; i<n-1; i++)
        {
            // top face
            AddQuad(tris, inner[i],outer[i],outer[i+1],inner[i+1]);
            // back face (so it's visible from below too)
            AddQuad(tris, outer[i],inner[i],inner[i+1],outer[i+1]);
        }

        // ── Semicircular end-caps on the ribbon ──
        float rcx = 0, rcy = 0;
        for (int i = 0; i < n; i++) { rcx += archCurve[i].x; rcy += archCurve[i].y; }
        rcx /= n; rcy /= n;
        const int rCapSegs = 8;
        for (int capEnd = 0; capEnd < 2; capEnd++)
        {
            int idx = capEnd == 0 ? 0 : n - 1;
            var center = ((outer[idx].x+inner[idx].x)*0.5f, (outer[idx].y+inner[idx].y)*0.5f, outer[idx].z);
            float dxD = outer[idx].x - inner[idx].x, dyD = outer[idx].y - inner[idx].y;
            float dL = MathF.Sqrt(dxD*dxD + dyD*dyD);
            if (dL < 1e-6f) continue;
            float cR = dL * 0.5f, ux2 = dxD/dL, uy2 = dyD/dL;
            float ppx = capEnd == 0 ? uy2 : -uy2, ppy = capEnd == 0 ? -ux2 : ux2;
            float tmx = rcx - center.Item1, tmy = rcy - center.Item2;
            if (ppx*tmx + ppy*tmy > 0) { ppx = -ppx; ppy = -ppy; }
            for (int s = 0; s < rCapSegs; s++)
            {
                float a0 = MathF.PI*s/rCapSegs, a1 = MathF.PI*(s+1)/rCapSegs;
                float ox0=cR*(ux2*MathF.Cos(a0)+ppx*MathF.Sin(a0)), oy0=cR*(uy2*MathF.Cos(a0)+ppy*MathF.Sin(a0));
                float ox1=cR*(ux2*MathF.Cos(a1)+ppx*MathF.Sin(a1)), oy1=cR*(uy2*MathF.Cos(a1)+ppy*MathF.Sin(a1));
                tris.Add(center.Item1);tris.Add(center.Item2);tris.Add(center.Item3);
                tris.Add(center.Item1+ox0);tris.Add(center.Item2+oy0);tris.Add(center.Item3);
                tris.Add(center.Item1+ox1);tris.Add(center.Item2+oy1);tris.Add(center.Item3);
                tris.Add(center.Item1);tris.Add(center.Item2);tris.Add(center.Item3);
                tris.Add(center.Item1+ox1);tris.Add(center.Item2+oy1);tris.Add(center.Item3);
                tris.Add(center.Item1+ox0);tris.Add(center.Item2+oy0);tris.Add(center.Item3);
            }
        }

        return tris.ToArray();
    }

    // ── Horseshoe solid ─────────────────────────────────────────────────────────────────────────────────────────────
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

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  GENERATE SPLINT — refined pipeline
    //
    //  Step A  Dilate upper mesh 1mm  → closed solid
    //  Step B  Dilate lower mesh 1mm  → closed solid
    //  Step C  horseshoe ∪ upper1mm ∪ lower1mm  → raw blank
    //  Step D  Per-voxel Z-clip at exact user-defined horseshoe surfaces:
    //          Upper cut: archCurveZ(x,y) - upperPenetrationMm  (+ = deeper into teeth)
    //          Lower cut: archCurveZ(x,y) + lowerPenetrationMm  (+ = deeper into teeth)
    //  Step E  Posterior limit: curved boundary matching horseshoe end-caps
    //  Step F  Subtract 0.1mm-dilated tooth surfaces → tooth pockets
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ── Public entry: config-driven, manifold-gated. Returns a printable result. ──
    public static SplintResult GenerateSplint(
        List<(float x,float y,float z)> upperCurve,
        List<(float x,float y,float z)> lowerCurve,
        SplintConfig config,
        float[]? upperMesh = null,
        float[]? lowerMesh = null)
    {
        if (config == null) return SplintResult.Empty("No splint configuration supplied.");
        if (upperCurve == null || lowerCurve == null ||
            upperCurve.Count < 2 || lowerCurve.Count < 2)
            return SplintResult.Empty("Need at least two points on each arch curve.");

        var warnings = new List<string>();
        if (!config.FromIntraoralScans)
            warnings.Add("Dental surfaces are the CT-bone fallback, not registered intraoral scans — "
                       + "seating accuracy is reduced. Import upper/lower intraoral scans for a clinical splint.");

        float[] soup = GenerateSplintSoup(upperCurve, lowerCurve, config, upperMesh, lowerMesh);
        if (soup.Length < 9)
        {
            warnings.Add("Splint generation produced no geometry.");
            return new SplintResult(Array.Empty<float>(), false, 1f, 0, warnings);
        }

        if (!config.GuaranteeManifold)
        {
            float open = WatertightScore(soup);
            if (open > 1e-4f)
                warnings.Add($"Output is not watertight (open-edge fraction {open:P1}); manifold guarantee is off.");
            return new SplintResult(soup, open <= 1e-4f, open, 0, warnings);
        }

        var rep = SplintMeshRepair.Repair(soup);
        if (!rep.IsManifold)
            warnings.Add($"Mesh repair could not fully close the splint "
                       + $"(open-edge fraction {rep.OpenEdgeFraction:P1}); review before printing.");
        else if (rep.BoundaryLoopsFilled > 0)
            warnings.Add($"Sealed {rep.BoundaryLoopsFilled} stray boundary loop(s) during manifold repair.");

        return new SplintResult(rep.Vertices, rep.IsManifold, rep.OpenEdgeFraction, 0, warnings);
    }

    // ── Core geometry: pose-agnostic triangle-soup builder (no manifold guarantee). ──
    private static float[] GenerateSplintSoup(
        List<(float x,float y,float z)> upperCurve,
        List<(float x,float y,float z)> lowerCurve,
        SplintConfig config,
        float[]? upperMesh = null,
        float[]? lowerMesh = null)
    {
        // Map config → the local names the geometry body below already uses.
        float labiolingualMm      = config.LabiolingualMm;
        float upperPenetrationMm  = config.UpperPenetrationMm;   // + = deeper into teeth (cut moves caudally)
        float lowerPenetrationMm  = config.LowerPenetrationMm;   // + = deeper into teeth (cut moves cranially)
        float lingualBuccalBiasMm = config.LingualBuccalBiasMm;  // + = buccal, − = lingual
        float bridgeThicknessMm   = config.BridgeThicknessMm;    // extra thickness on the bridge connection
        int   sampleCount         = config.SampleCount;

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

        // Apply lingual-buccal bias: shift center along normal, keep total width
        var TO = new (float x,float y,float z)[n]; var TI = new (float x,float y,float z)[n];
        var BO = new (float x,float y,float z)[n]; var BI = new (float x,float y,float z)[n];
        for (int i = 0; i < n; i++)
        {
            var u=upper[i]; var nu=norU[i]; var l=lower[i]; var nl=norL[i];
            float uCx = u.x + nu.x * lingualBuccalBiasMm;
            float uCy = u.y + nu.y * lingualBuccalBiasMm;
            float lCx = l.x + nl.x * lingualBuccalBiasMm;
            float lCy = l.y + nl.y * lingualBuccalBiasMm;
            TO[i]=(uCx+nu.x*half, uCy+nu.y*half, u.z); TI[i]=(uCx-nu.x*half, uCy-nu.y*half, u.z);
            BO[i]=(lCx+nl.x*half, lCy+nl.y*half, l.z); BI[i]=(lCx-nl.x*half, lCy-nl.y*half, l.z);
        }

        // ── Build horseshoe body (closed by construction) ─────────────────────
        var horseshoeTris = new List<float>(n * 6 * 2 * 9);
        for (int i = 0; i < n-1; i++)
        {
            int j=i+1;
            AddQuad(horseshoeTris, TO[i], TO[j], BO[j], BO[i]);   // outer wall
            AddQuad(horseshoeTris, TI[j], TI[i], BI[i], BI[j]);   // inner wall
            AddQuad(horseshoeTris, TO[i], TI[i], TI[j], TO[j]);   // top face (normal +Z)
            AddQuad(horseshoeTris, BO[i], BO[j], BI[j], BI[i]);   // bottom face
        }

        // Arch midpoint (for cap orientation)
        float archMidX = (upper[n/2].x + lower[n/2].x) * 0.5f;
        float archMidY = (upper[n/2].y + lower[n/2].y) * 0.5f;

        // ── Semicircular end-caps ──
        const int capSegs = 8;
        for (int capEnd = 0; capEnd < 2; capEnd++)
        {
            int idx = capEnd == 0 ? 0 : n - 1;
            var tCenter = ((TO[idx].x+TI[idx].x)*0.5f, (TO[idx].y+TI[idx].y)*0.5f, TO[idx].z);
            var bCenter = ((BO[idx].x+BI[idx].x)*0.5f, (BO[idx].y+BI[idx].y)*0.5f, BO[idx].z);
            float dxDir = TO[idx].x - TI[idx].x, dyDir = TO[idx].y - TI[idx].y;
            float dLen = MathF.Sqrt(dxDir*dxDir + dyDir*dyDir);
            if (dLen < 1e-6f) continue;
            float capR = dLen * 0.5f, ux = dxDir/dLen, uy = dyDir/dLen;
            float px_ = capEnd == 0 ? uy : -uy, py_ = capEnd == 0 ? -ux : ux;
            float toMidX = archMidX - tCenter.Item1, toMidY = archMidY - tCenter.Item2;
            if (px_*toMidX + py_*toMidY > 0) { px_ = -px_; py_ = -py_; }

            var topPts = new (float x,float y,float z)[capSegs+1];
            var botPts = new (float x,float y,float z)[capSegs+1];
            for (int s = 0; s <= capSegs; s++)
            {
                float angle = MathF.PI * s / capSegs;
                float offX = capR*(ux*MathF.Cos(angle) + px_*MathF.Sin(angle));
                float offY = capR*(uy*MathF.Cos(angle) + py_*MathF.Sin(angle));
                topPts[s] = (tCenter.Item1+offX, tCenter.Item2+offY, tCenter.Item3);
                botPts[s] = (bCenter.Item1+offX, bCenter.Item2+offY, bCenter.Item3);
            }
            for (int s = 0; s < capSegs; s++)
            {
                horseshoeTris.Add(tCenter.Item1);horseshoeTris.Add(tCenter.Item2);horseshoeTris.Add(tCenter.Item3);
                if (capEnd==0) { horseshoeTris.Add(topPts[s+1].x);horseshoeTris.Add(topPts[s+1].y);horseshoeTris.Add(topPts[s+1].z); horseshoeTris.Add(topPts[s].x);horseshoeTris.Add(topPts[s].y);horseshoeTris.Add(topPts[s].z); }
                else { horseshoeTris.Add(topPts[s].x);horseshoeTris.Add(topPts[s].y);horseshoeTris.Add(topPts[s].z); horseshoeTris.Add(topPts[s+1].x);horseshoeTris.Add(topPts[s+1].y);horseshoeTris.Add(topPts[s+1].z); }
                horseshoeTris.Add(bCenter.Item1);horseshoeTris.Add(bCenter.Item2);horseshoeTris.Add(bCenter.Item3);
                if (capEnd==0) { horseshoeTris.Add(botPts[s].x);horseshoeTris.Add(botPts[s].y);horseshoeTris.Add(botPts[s].z); horseshoeTris.Add(botPts[s+1].x);horseshoeTris.Add(botPts[s+1].y);horseshoeTris.Add(botPts[s+1].z); }
                else { horseshoeTris.Add(botPts[s+1].x);horseshoeTris.Add(botPts[s+1].y);horseshoeTris.Add(botPts[s+1].z); horseshoeTris.Add(botPts[s].x);horseshoeTris.Add(botPts[s].y);horseshoeTris.Add(botPts[s].z); }
                if (capEnd==0) AddQuad(horseshoeTris, topPts[s+1], topPts[s], botPts[s], botPts[s+1]);
                else AddQuad(horseshoeTris, topPts[s], topPts[s+1], botPts[s+1], botPts[s]);
            }
        }
        float[] horseshoeFlat = horseshoeTris.ToArray();

        if (upperMesh == null || upperMesh.Length < 9 ||
            lowerMesh == null || lowerMesh.Length < 9)
            return horseshoeFlat;

        try
        {
            const double VS_SDF  = 0.2;   // SDF grid resolution
            const double VS_MC   = 0.1;   // MC sampling resolution
            const double CrownMm = 10.0;
            const double Dil1    = 1.0;
            const double Dil01   = 0.1;

            System.Diagnostics.Debug.WriteLine($"[Splint] upperPen={upperPenetrationMm:F1} lowerPen={lowerPenetrationMm:F1} bias={lingualBuccalBiasMm:F1}");

            // BoundedImplicitFunction3d wrapper that shifts the iso by 'Offset'
            BoundedImplicitFunction3d Offset(BoundedImplicitFunction3d a, double off)
                => new OffsetImpl(a, off);

            // Convert flat triangle soup to indexed DMesh3
            DMesh3 ToMesh(float[] s)
            {
                var dm=new DMesh3(); var vm=new Dictionary<(int,int,int),int>();
                int GetV(float x,float y,float z){var k=((int)MathF.Round(x*100),(int)MathF.Round(y*100),(int)MathF.Round(z*100));if(!vm.TryGetValue(k,out int vi)){vi=dm.AppendVertex(new Vector3d(x,y,z));vm[k]=vi;}return vi;}
                for(int i=0;i+8<s.Length;i+=9){int a=GetV(s[i],s[i+1],s[i+2]),b2=GetV(s[i+3],s[i+4],s[i+5]),c2=GetV(s[i+6],s[i+7],s[i+8]);if(a!=b2&&b2!=c2&&a!=c2)dm.AppendTriangle(a,b2,c2);}
                return dm;
            }

            // Pre-clip meshes to crown+horseshoe region before SDF (avoids full-skull BVH)
            float mnX=float.MaxValue,mnY=float.MaxValue,mxX=float.MinValue,mxY=float.MinValue;
            foreach(var p in upper){if(p.x<mnX)mnX=p.x;if(p.x>mxX)mxX=p.x;if(p.y<mnY)mnY=p.y;if(p.y>mxY)mxY=p.y;}
            foreach(var p in lower){if(p.x<mnX)mnX=p.x;if(p.x>mxX)mxX=p.x;if(p.y<mnY)mnY=p.y;if(p.y>mxY)mxY=p.y;}
            float upperZ = upper.Max(p => p.z);
            float lowerZ = lower.Min(p => p.z);
            float clipPad=(float)(labiolingualMm*0.5+Math.Abs(lingualBuccalBiasMm)+Dil1+6);
            float[] Crop(float[] s){
                var r=new List<float>();
                for(int i=0;i+8<s.Length;i+=9){
                    float cx=(s[i]+s[i+3]+s[i+6])/3f,cy=(s[i+1]+s[i+4]+s[i+7])/3f,cz=(s[i+2]+s[i+5]+s[i+8])/3f;
                    if(cx>=mnX-clipPad&&cx<=mxX+clipPad&&cy>=mnY-clipPad&&cy<=mxY+clipPad
                       &&cz>=lowerZ-(float)(CrownMm+Dil1+1)&&cz<=upperZ+(float)(CrownMm+Dil1+1))
                        for(int k=0;k<9;k++)r.Add(s[i+k]);
                }
                return r.ToArray();
            }

            // Compute SDF ONCE per mesh (at 0.2mm)
            BoundedImplicitFunction3d? BaseSdf(float[] soup, double band = -1){
                if(soup.Length<9) return null;
                var m=ToMesh(soup); if(m.TriangleCount==0) return null;
                double b = band > 0 ? band : Dil1 + VS_SDF*5;
                System.Diagnostics.Debug.WriteLine($"[Splint] SDF {m.TriangleCount} tris @ {VS_SDF}mm band={b:F1}");
                var spatial = new DMeshAABBTree3(m, autoBuild:true);
                var sdf=new MeshSignedDistanceGrid(m,(float)VS_SDF){
                    Spatial              = spatial,
                    NarrowBandMaxDistance= b,
                    ExactBandWidth       = (int)Math.Ceiling(b/VS_SDF)+4,
                    ComputeSigns         = true,
                    ComputeMode          = MeshSignedDistanceGrid.ComputeModes.NarrowBand_SpatialFloodFill,
                    InsideMode           = MeshSignedDistanceGrid.InsideModes.ParityCount,
                    UseParallel          = true
                };
                sdf.Compute();
                System.Diagnostics.Debug.WriteLine($"[Splint] SDF done grid={sdf.Grid.size}");
                return new DenseGridTrilinearImplicit(sdf.Grid,sdf.GridOrigin,(float)VS_SDF);
            }

            // ── Build a KD-like fast nearest-arch-Z lookup from both curves ──
            // For each (x,y) query, find nearest point on the arch curve and return its Z
            float NearestUpperZ(double px, double py){
                float bestD=float.MaxValue, bestZ=0f;
                foreach(var pt in upper){float dx=(float)px-pt.x,dy=(float)py-pt.y,d=dx*dx+dy*dy;if(d<bestD){bestD=d;bestZ=pt.z;}}
                return bestZ;
            }
            float NearestLowerZ(double px, double py){
                float bestD=float.MaxValue, bestZ=0f;
                foreach(var pt in lower){float dx=(float)px-pt.x,dy=(float)py-pt.y,d=dx*dx+dy*dy;if(d<bestD){bestD=d;bestZ=pt.z;}}
                return bestZ;
            }

            // ── Posterior limit: compute the curved boundary from horseshoe end-caps ──
            float Cap0Cx = (TO[0].x + TI[0].x + BO[0].x + BI[0].x) * 0.25f;
            float Cap0Cy = (TO[0].y + TI[0].y + BO[0].y + BI[0].y) * 0.25f;
            float CapNCx = (TO[n-1].x + TI[n-1].x + BO[n-1].x + BI[n-1].x) * 0.25f;
            float CapNCy = (TO[n-1].y + TI[n-1].y + BO[n-1].y + BI[n-1].y) * 0.25f;

            float cap0DirX = TI[0].x - TO[0].x, cap0DirY = TI[0].y - TO[0].y;
            float cap0NX = -cap0DirY, cap0NY = cap0DirX;
            if (cap0NX*(archMidX-Cap0Cx) + cap0NY*(archMidY-Cap0Cy) < 0) { cap0NX=-cap0NX; cap0NY=-cap0NY; }

            float capNDirX = TI[n-1].x - TO[n-1].x, capNDirY = TI[n-1].y - TO[n-1].y;
            float capNNX = -capNDirY, capNNY = capNDirX;
            if (capNNX*(archMidX-CapNCx) + capNNY*(archMidY-CapNCy) < 0) { capNNX=-capNNX; capNNY=-capNNY; }

            bool IsAnteriorToPosteriorLimit(double px, double py)
            {
                float d0 = cap0NX*((float)px-Cap0Cx) + cap0NY*((float)py-Cap0Cy);
                float dN = capNNX*((float)px-CapNCx) + capNNY*((float)py-CapNCy);
                return d0 >= -0.5f && dN >= -0.5f;
            }

            float[] upCrop=Crop(upperMesh), loCrop=Crop(lowerMesh);
            System.Diagnostics.Debug.WriteLine($"[Splint] Cropped tris: up={upCrop.Length/9} lo={loCrop.Length/9}");
            if(upCrop.Length<9||loCrop.Length<9) return horseshoeFlat;

            var upBase    = BaseSdf(upCrop);
            var loBase    = BaseSdf(loCrop);
            if(upBase==null||loBase==null) return horseshoeFlat;

            var upImpl1   = Offset(upBase,  -Dil1);
            var loImpl1   = Offset(loBase,  -Dil1);
            var upImpl01  = Offset(upBase,  -Dil01);
            var loImpl01  = Offset(loBase,  -Dil01);

            // ── PHASE 1: Bake blank SDF (upper_1mm ∪ lower_1mm, Z-clipped at arch surfaces) ──
            float mcPad = (float)(labiolingualMm*0.5 + Math.Abs(lingualBuccalBiasMm) + 4.0);
            float zPadTop = Math.Max(upperPenetrationMm, 0f) + 2.0f;
            float zPadBot = Math.Max(lowerPenetrationMm, 0f) + 2.0f;
            var mcBounds = new AxisAlignedBox3d(
                new Vector3d(mnX-mcPad, mnY-mcPad, lowerZ-zPadBot-1.0),
                new Vector3d(mxX+mcPad, mxY+mcPad, upperZ+zPadTop+1.0));
            int gnx=(int)Math.Ceiling(mcBounds.Width /VS_MC)+1;
            int gny=(int)Math.Ceiling(mcBounds.Height/VS_MC)+1;
            int gnz=(int)Math.Ceiling(mcBounds.Depth /VS_MC)+1;
            float gox=(float)mcBounds.Min.x, goy=(float)mcBounds.Min.y, goz=(float)mcBounds.Min.z;
            var bakedGrid = new float[gnx*gny*gnz];
            System.Diagnostics.Debug.WriteLine($"[Splint] PHASE1 Baking blank {gnx}x{gny}x{gnz}={gnx*gny*gnz/1_000_000}M voxels...");
            System.Threading.Tasks.Parallel.For(0, gnz, iz => {
                for(int iy=0;iy<gny;iy++) for(int ix=0;ix<gnx;ix++) {
                    double px = gox+ix*VS_MC, py = goy+iy*VS_MC, pz = goz+iz*VS_MC;
                    int vIdx = iz*gnx*gny+iy*gnx+ix;

                    // Z-clip at exact user-defined horseshoe surfaces (per-point Z following arch contour)
                    float upperCutZ = NearestUpperZ(px, py) - upperPenetrationMm;
                    float lowerCutZ = NearestLowerZ(px, py) + lowerPenetrationMm;
                    if ((float)pz > upperCutZ || (float)pz < lowerCutZ)
                    { bakedGrid[vIdx] = 1.0f; continue; }

                    // Posterior limit
                    if (!IsAnteriorToPosteriorLimit(px, py))
                    { bakedGrid[vIdx] = 1.0f; continue; }

                    // Raw blank = upper_1mm ∪ lower_1mm (NO horseshoe SDF)
                    var pt = new Vector3d(px, py, pz);
                    float upVal1 = (float)upImpl1.Value(ref pt);
                    float loVal1 = (float)loImpl1.Value(ref pt);
                    bakedGrid[vIdx] = MathF.Min(upVal1, loVal1);
                }
            });

            // ── PHASE 1b: Remove floaters BEFORE closing (BFS from arch centroid) ──
            {
                int totalVox = gnx*gny*gnz;
                int insideCount = 0;
                for (int i = 0; i < totalVox; i++) if (bakedGrid[i] < 0) insideCount++;
                System.Diagnostics.Debug.WriteLine($"[Splint] PHASE1b {insideCount} inside voxels before floater removal");
                if (insideCount == 0) return horseshoeFlat;

                var visited = new bool[totalVox];
                var queue = new Queue<int>();
                float seedX = archMidX, seedY = archMidY, seedZ = (upperZ + lowerZ) * 0.5f;
                int bestSeed = -1; float bestSeedD = float.MaxValue;
                for (int iz2 = 0; iz2 < gnz; iz2++) for (int iy2 = 0; iy2 < gny; iy2++) for (int ix2 = 0; ix2 < gnx; ix2++) {
                    int idx2 = iz2*gnx*gny+iy2*gnx+ix2;
                    if (bakedGrid[idx2] >= 0) continue;
                    float dx2 = gox+ix2*(float)VS_MC-seedX, dy2 = goy+iy2*(float)VS_MC-seedY, dz2 = goz+iz2*(float)VS_MC-seedZ;
                    float d = dx2*dx2+dy2*dy2+dz2*dz2;
                    if (d < bestSeedD) { bestSeedD = d; bestSeed = idx2; }
                }
                if (bestSeed < 0) return horseshoeFlat;
                queue.Enqueue(bestSeed); visited[bestSeed] = true;
                int mainSize = 0;
                int[] dxN={1,-1,0,0,0,0}, dyN={0,0,1,-1,0,0}, dzN={0,0,0,0,1,-1};
                while (queue.Count > 0) {
                    int ci = queue.Dequeue(); mainSize++;
                    int cz = ci/(gnx*gny), crem = ci%(gnx*gny), cy = crem/gnx, cx = crem%gnx;
                    for (int d = 0; d < 6; d++) {
                        int nx2=cx+dxN[d], ny2=cy+dyN[d], nz2=cz+dzN[d];
                        if (nx2<0||nx2>=gnx||ny2<0||ny2>=gny||nz2<0||nz2>=gnz) continue;
                        int ni = nz2*gnx*gny+ny2*gnx+nx2;
                        if (!visited[ni] && bakedGrid[ni] < 0) { visited[ni]=true; queue.Enqueue(ni); }
                    }
                }
                int removed = 0;
                for (int i = 0; i < totalVox; i++) {
                    if (bakedGrid[i] < 0 && !visited[i]) { bakedGrid[i] = 1.0f; removed++; }
                }
                System.Diagnostics.Debug.WriteLine($"[Splint] PHASE1b Kept {mainSize} voxels, removed {removed} floaters");
            }

            // ── PHASE 2: GPU morphological closing on PADDED coarse grid ──
            // Padding by closeR on all faces ensures dilation never hits the volume
            // boundary, so erosion fully retracts — no boundary artifacts.
            float maxGap = 0;
            for (int i = 0; i < n; i++) {
                float gap = MathF.Abs(upper[i].z - lower[i].z);
                if (gap > maxGap) maxGap = gap;
            }
            const float coarseVS = 0.5f;
            int closeR = (int)MathF.Ceiling(maxGap * 0.55f / coarseVS);
            closeR = Math.Clamp(closeR, 2, 30);
            System.Diagnostics.Debug.WriteLine($"[Splint] PHASE2 GPU closing: maxGap={maxGap:F1}mm closeR={closeR} coarseVS={coarseVS}");

            // Coarse grid dimensions WITH padding
            int pad = closeR + 1; // padding on each side
            int cnxBase = (int)Math.Ceiling(mcBounds.Width  / coarseVS) + 1;
            int cnyBase = (int)Math.Ceiling(mcBounds.Height / coarseVS) + 1;
            int cnzBase = (int)Math.Ceiling(mcBounds.Depth  / coarseVS) + 1;
            int cnx = cnxBase + 2*pad;
            int cny = cnyBase + 2*pad;
            int cnz = cnzBase + 2*pad;
            var coarse = new byte[cnx * cny * cnz]; // all zeros = outside (padding is empty)
            System.Diagnostics.Debug.WriteLine($"[Splint] PHASE2 padded coarse grid {cnx}x{cny}x{cnz} (pad={pad}), {cnx*cny*cnz/1000}K voxels");

            // Fill interior (offset by pad) from fine SDF
            for (int ciz = 0; ciz < cnzBase; ciz++) for (int ciy = 0; ciy < cnyBase; ciy++) for (int cix = 0; cix < cnxBase; cix++) {
                int fix = Math.Clamp((int)(cix * coarseVS / VS_MC), 0, gnx-1);
                int fiy = Math.Clamp((int)(ciy * coarseVS / VS_MC), 0, gny-1);
                int fiz = Math.Clamp((int)(ciz * coarseVS / VS_MC), 0, gnz-1);
                if (bakedGrid[fiz*gnx*gny + fiy*gnx + fix] < 0)
                    coarse[(ciz+pad)*cnx*cny + (ciy+pad)*cnx + (cix+pad)] = 1;
            }

            // GPU: close (bridge the gap between arches)
            byte[] closed;
            using (var gpu = new GpuMorphology3D()) {
                closed = gpu.Close(coarse, cnx, cny, cnz, closeR);
                // Extra thickness?
                if (bridgeThicknessMm > 0.01f) {
                    int dilateR = (int)MathF.Ceiling(bridgeThicknessMm / coarseVS);
                    closed = gpu.Dilate(closed, cnx, cny, cnz, dilateR);
                }
            }
            System.Diagnostics.Debug.WriteLine("[Splint] PHASE2 GPU closing done");

            // Apply closed result back to fine grid (reading from padded coordinates)
            System.Threading.Tasks.Parallel.For(0, gnz, iz => {
                for (int iy = 0; iy < gny; iy++) for (int ix = 0; ix < gnx; ix++) {
                    int vIdx = iz*gnx*gny + iy*gnx + ix;
                    if (bakedGrid[vIdx] < 0) continue; // already inside
                    // Map fine voxel to padded coarse grid
                    int cix = Math.Clamp((int)(ix * VS_MC / coarseVS), 0, cnxBase-1) + pad;
                    int ciy = Math.Clamp((int)(iy * VS_MC / coarseVS), 0, cnyBase-1) + pad;
                    int ciz = Math.Clamp((int)(iz * VS_MC / coarseVS), 0, cnzBase-1) + pad;
                    if (closed[ciz*cnx*cny + ciy*cnx + cix] == 1)
                        bakedGrid[vIdx] = -0.5f;
                }
            });

            // ── PHASE 2b: SDF smoothing (separable box blur) ──
            // Smooth the blank SDF BEFORE pocket subtraction so pockets stay precise.
            // 3 passes of box blur with radius 2 ≈ Gaussian σ≈2.4 voxels.
            {
                int blurR = 2, blurPasses = 3;
                System.Diagnostics.Debug.WriteLine($"[Splint] PHASE2b SDF smoothing: blurR={blurR} passes={blurPasses}");
                var temp = new float[bakedGrid.Length];

                for (int pass = 0; pass < blurPasses; pass++) {
                    // Blur X
                    System.Threading.Tasks.Parallel.For(0, gnz, iz => {
                        for (int iy = 0; iy < gny; iy++) {
                            int rowBase = iz*gnx*gny + iy*gnx;
                            for (int ix = 0; ix < gnx; ix++) {
                                float sum = 0; int cnt = 0;
                                int lo = Math.Max(0,ix-blurR), hi = Math.Min(gnx-1,ix+blurR);
                                for (int k = lo; k <= hi; k++) { sum += bakedGrid[rowBase+k]; cnt++; }
                                temp[rowBase+ix] = sum / cnt;
                            }
                        }
                    });
                    // Blur Y
                    System.Threading.Tasks.Parallel.For(0, gnz, iz => {
                        for (int ix = 0; ix < gnx; ix++) {
                            for (int iy = 0; iy < gny; iy++) {
                                float sum = 0; int cnt = 0;
                                int lo = Math.Max(0,iy-blurR), hi = Math.Min(gny-1,iy+blurR);
                                for (int k = lo; k <= hi; k++) { sum += temp[iz*gnx*gny+k*gnx+ix]; cnt++; }
                                bakedGrid[iz*gnx*gny+iy*gnx+ix] = sum / cnt;
                            }
                        }
                    });
                    // Blur Z
                    System.Threading.Tasks.Parallel.For(0, gny, iy => {
                        for (int ix = 0; ix < gnx; ix++) {
                            for (int iz = 0; iz < gnz; iz++) {
                                float sum = 0; int cnt = 0;
                                int lo = Math.Max(0,iz-blurR), hi = Math.Min(gnz-1,iz+blurR);
                                for (int k = lo; k <= hi; k++) { sum += bakedGrid[k*gnx*gny+iy*gnx+ix]; cnt++; }
                                temp[iz*gnx*gny+iy*gnx+ix] = sum / cnt;
                            }
                        }
                    });
                    // Copy temp → bakedGrid for next pass
                    Array.Copy(temp, bakedGrid, bakedGrid.Length);
                }
                System.Diagnostics.Debug.WriteLine("[Splint] PHASE2b SDF smoothing done");
            }

            // (Floater removal already done in PHASE 1b, before closing)

            // ── PHASE 4: Subtract tooth pockets (0.1mm offset) ──
            System.Diagnostics.Debug.WriteLine("[Splint] PHASE4 Subtracting tooth pockets...");
            System.Threading.Tasks.Parallel.For(0, gnz, iz => {
                for(int iy=0;iy<gny;iy++) for(int ix=0;ix<gnx;ix++) {
                    int vIdx = iz*gnx*gny+iy*gnx+ix;
                    float blankVal = bakedGrid[vIdx];
                    if (blankVal >= 0.5f) continue;
                    double px = gox+ix*VS_MC, py = goy+iy*VS_MC, pz = goz+iz*VS_MC;
                    var pt = new Vector3d(px, py, pz);
                    float upVal01 = (float)upImpl01.Value(ref pt);
                    float loVal01 = (float)loImpl01.Value(ref pt);
                    bakedGrid[vIdx] = MathF.Max(blankVal, MathF.Max(-upVal01, -loVal01));
                }
            });

            // ── PHASE 5: MC surface extraction ──
            int finalInside = bakedGrid.Count(v => v < 0f);
            System.Diagnostics.Debug.WriteLine($"[Splint] PHASE5 {finalInside} inside voxels → MC...");
            if (finalInside == 0) return horseshoeFlat;
            var bakedDense = new DenseGrid3f(gnx,gny,gnz,float.MaxValue);
            for(int i=0;i<bakedGrid.Length;i++) bakedDense[i]=bakedGrid[i];
            var bakedImpl  = new DenseGridTrilinearImplicit(bakedDense, new Vector3f(gox,goy,goz), (float)VS_MC);
            var mc = new MarchingCubes{Implicit=bakedImpl, Bounds=mcBounds, CubeSize=VS_MC, ParallelCompute=true};
            mc.Generate();
            System.Diagnostics.Debug.WriteLine($"[Splint] MC → {mc.Mesh?.TriangleCount} tris");

            if(mc.Mesh==null||mc.Mesh.TriangleCount==0) return horseshoeFlat;
            var dm=mc.Mesh;

            // ── PHASE 5b: Mesh-level floater removal — keep only largest component ──
            {
                var cc = new MeshConnectedComponents(dm);
                cc.FindConnectedT();
                if (cc.Count > 1) {
                    // Find largest component
                    int bestIdx = 0, bestSize = 0;
                    for (int ci = 0; ci < cc.Count; ci++) {
                        if (cc.Components[ci].Indices.Length > bestSize) {
                            bestSize = cc.Components[ci].Indices.Length;
                            bestIdx = ci;
                        }
                    }
                    // Remove all triangles NOT in the largest component
                    int removedTris = 0;
                    for (int ci = 0; ci < cc.Count; ci++) {
                        if (ci == bestIdx) continue;
                        foreach (int tid in cc.Components[ci].Indices) {
                            dm.RemoveTriangle(tid);
                            removedTris++;
                        }
                    }
                    dm.CompactInPlace();
                    System.Diagnostics.Debug.WriteLine($"[Splint] PHASE5b Removed {removedTris} floater triangles ({cc.Count-1} small components), kept {dm.TriangleCount} tris");
                }
            }

            var res=new float[dm.TriangleCount*9]; int ri=0;
            foreach(int tid in dm.TriangleIndices()){var t=dm.GetTriangle(tid);var va=dm.GetVertex(t.a);var vb=dm.GetVertex(t.b);var vc=dm.GetVertex(t.c);res[ri++]=(float)va.x;res[ri++]=(float)va.y;res[ri++]=(float)va.z;res[ri++]=(float)vb.x;res[ri++]=(float)vb.y;res[ri++]=(float)vb.z;res[ri++]=(float)vc.x;res[ri++]=(float)vc.y;res[ri++]=(float)vc.z;}
            return res.Length>=9?res:horseshoeFlat;
        }
        catch(Exception ex){
            string msg=$"[Splint ERROR] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try{System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"splint_error.txt"),msg);}catch{}
            return horseshoeFlat;
        }

    }

    private static float Sq(float v) => v * v;

    // â”€â”€ Isotropic mesh offset via geometry3Sharp â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// <summary>
    /// Offsets every vertex of the triangle soup by <paramref name="offsetMm"/> mm

    /// along its area-weighted vertex normal, using geometry3Sharp for robust
    /// per-vertex normal computation on non-watertight meshes.
    /// </summary>
    private static float[] OffsetMeshVertices(float[] mesh, float offsetMm)
    {
        if (mesh == null || mesh.Length < 9) return mesh ?? Array.Empty<float>();

        // Ã¢â€â‚¬Ã¢â€â‚¬ Build a DMesh3 (indexed) from the flat triangle soup Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Compute per-vertex normals (area-weighted, handles open meshes) Ã¢â€â‚¬
        MeshNormals.QuickCompute(dm);

        // Ã¢â€â‚¬Ã¢â€â‚¬ Offset each vertex along its normal Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        foreach (int vid in dm.VertexIndices())
        {
            Vector3d pos = dm.GetVertex(vid);
            Vector3f nor = dm.GetVertexNormal(vid);
            dm.SetVertex(vid, pos + new Vector3d(nor.x, nor.y, nor.z) * offsetMm);
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Rebuild flat triangle soup Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

    // Ã¢â€â‚¬Ã¢â€â‚¬ Tooth pocket: crop + side-wall prisms Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

/// <summary>
/// Wraps a BoundedImplicitFunction3d and shifts its value by 'Offset'.
/// Negative offset = dilation (expands the iso=0 surface outward).
/// This makes ImplicitOffset behave as a BoundedImplicitFunction3d so it
/// can be used as A/B in g3's ImplicitUnion3d / ImplicitDifference3d.
/// </summary>
internal sealed class OffsetImpl : BoundedImplicitFunction3d
{
    private readonly BoundedImplicitFunction3d _inner;
    private readonly double _offset;
    public OffsetImpl(BoundedImplicitFunction3d inner, double offset) { _inner=inner; _offset=offset; }
    public double Value(ref Vector3d pt) => _inner.Value(ref pt) + _offset;

    public AxisAlignedBox3d Bounds()
    {
        var b = _inner.Bounds();
        double expand = _offset < 0 ? -_offset : 0;
        b.Expand(expand + 1.0); // +1mm safety margin
        return b;
    }
}

