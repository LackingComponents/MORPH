using System.Diagnostics;
using System.Globalization;

namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>
/// Outcome of <see cref="SkinSilhouette.Build"/>. On failure <see cref="FailureReason"/> is always set.
/// Silhouette image: column = antero-posterior coordinate (mesh Y), row = vertical coordinate (mesh Z, superior =
/// row 0). Pixel (c, r) centre is at Y = <see cref="OriginYMm"/> + c·MmPerPixel, Z = <see cref="OriginZMm"/> − r·MmPerPixel.
/// </summary>
public sealed record SkinSilhouetteResult(
    bool Success,
    string? FailureReason,
    ProfileContour? Contour,
    float[]? Silhouette,
    int Width,
    int Height,
    double MmPerPixel,
    double OriginYMm,
    double OriginZMm,
    int TriangleCount,
    long ElapsedMs,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Lateral silhouette of a skin mesh and its anterior profile line, used as a reference in true mm for the photo
/// overlay (port of the validated prototype). The mesh is projected along its X axis (mediolateral, the same
/// direction as the lateral DRR); rotation in the sagittal plane is left to the registration.
/// </summary>
public static class SkinSilhouette
{
    private const double MarginMm = 5.0;
    private const double FovCutToleranceMm = 0.8;
    private const double MinFrontChainMm = 40.0;
    private const double ChainSmoothingSigmaPx = 2.0;
    private const double ResampleStepPx = 1.0;
    private const int FacingPeakCount = 3;
    private const long MaxMaskPixels = 60_000_000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double v, string fmt = "F2") => v.ToString(fmt, Inv);

    /// <param name="vertices">Triangle soup: 3 vertices per triangle, <paramref name="vertexStride"/> floats per
    /// vertex (x, y, z first), in mm.</param>
    /// <param name="affine4x4">Optional row-major 4×4 for column vectors: x' = a[0]x + a[1]y + a[2]z + a[3], etc.</param>
    /// <param name="mmPerPixel">Raster resolution of the silhouette.</param>
    public static SkinSilhouetteResult Build(float[] vertices, int vertexStride = 3, double[]? affine4x4 = null,
                                             double mmPerPixel = 0.1)
    {
        var sw = Stopwatch.StartNew();
        var diag = new List<string>();
        SkinSilhouetteResult Fail(string reason, int tris = 0) =>
            new(false, reason, null, null, 0, 0, mmPerPixel, 0, 0, tris, sw.ElapsedMilliseconds, diag.AsReadOnly());

        if (vertices is null || vertices.Length == 0) return Fail("skin mesh is empty");
        if (vertexStride < 3) return Fail($"invalid vertex stride {vertexStride} (at least 3 floats per vertex)");
        if (!double.IsFinite(mmPerPixel) || mmPerPixel <= 0) return Fail($"invalid raster resolution {mmPerPixel} mm/px");
        if (affine4x4 is not null && affine4x4.Length != 16) return Fail($"affine must have 16 values, got {affine4x4.Length}");
        int tris = vertices.Length / (3 * vertexStride);
        if (tris == 0) return Fail("skin mesh has no complete triangle");

        try
        {
            var (ys, zs) = Project(vertices, vertexStride, tris, affine4x4);
            return BuildCore(ys, zs, tris, mmPerPixel, sw, diag, Fail);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail($"skin silhouette crashed: {ex.GetType().Name}: {ex.Message}", tris);
        }
    }

    // Antero-posterior (Y) and vertical (Z) coordinates of every vertex after the optional affine.
    private static (double[] Y, double[] Z) Project(float[] v, int stride, int tris, double[]? a)
    {
        int n = tris * 3;
        var ys = new double[n];
        var zs = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = v[i * stride], y = v[i * stride + 1], z = v[i * stride + 2];
            if (a is null) { ys[i] = y; zs[i] = z; continue; }
            ys[i] = a[4] * x + a[5] * y + a[6] * z + a[7];
            zs[i] = a[8] * x + a[9] * y + a[10] * z + a[11];
        }
        return (ys, zs);
    }

    private static SkinSilhouetteResult BuildCore(double[] ys, double[] zs, int tris, double res, Stopwatch sw,
        List<string> diag, Func<string, int, SkinSilhouetteResult> fail)
    {
        double yMin = double.MaxValue, yMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue;
        for (int i = 0; i < ys.Length; i++)
        {
            if (!double.IsFinite(ys[i]) || !double.IsFinite(zs[i])) return fail($"skin mesh has a non-finite vertex (#{i})", tris);
            yMin = Math.Min(yMin, ys[i]); yMax = Math.Max(yMax, ys[i]);
            zMin = Math.Min(zMin, zs[i]); zMax = Math.Max(zMax, zs[i]);
        }
        double originY = yMin - MarginMm, originZ = zMax + MarginMm;
        int w = (int)Math.Ceiling((yMax - yMin + 2 * MarginMm) / res) + 1;
        int h = (int)Math.Ceiling((zMax - zMin + 2 * MarginMm) / res) + 1;
        if ((long)w * h > MaxMaskPixels)
            return fail($"silhouette too large ({w}x{h} px at {F(res, "F2")} mm/px; mesh {F(yMax - yMin, "F0")} x {F(zMax - zMin, "F0")} mm)", tris);
        diag.Add($"mesh {tris} triangles, AP {F(yMin)}..{F(yMax)} mm, vertical {F(zMin)}..{F(zMax)} mm; raster {w}x{h} px at {F(res, "F2")} mm/px");

        var mask = Rasterize(ys, zs, tris, w, h, (y => (y - originY) / res), (z => (originZ - z) / res));
        var (comp, size, count) = ProfileImageOps.LargestComponent(mask, w, h);
        if (size == 0) return fail("silhouette mask is empty (degenerate mesh)", tris);
        diag.Add($"silhouette: {count} component(s), largest {size} px ({F(size * res * res / 100.0, "F1")} cm2)");

        var loop = ProfileBoundary.TraceOuter(comp, w, h);
        double cutTop = (originZ - (zMax - FovCutToleranceMm)) / res;     // rows above this are on the top cut
        double cutBottom = (originZ - (zMin + FovCutToleranceMm)) / res;  // rows below this are on the bottom cut
        var chain = ChooseFrontChain(loop, cutTop, cutBottom, res, diag, out bool facingRight, out string? why);
        if (chain is null) return fail(why!, tris);

        var smoothed = ProfileRefinement.Smooth(chain, ChainSmoothingSigmaPx);
        var pts = ProfileCurveTools.Resample(smoothed, ResampleStepPx);
        var conf = Enumerable.Repeat(1.0, pts.Count).ToList();
        diag.Add($"front chain {F(pts.Count * res, "F1")} mm ({pts.Count} pts), facing {(facingRight ? "right (+X)" : "left (-X)")}");

        var contour = new ProfileContour(pts.AsReadOnly(), conf.AsReadOnly(), facingRight, false, diag.ToList().AsReadOnly());
        var lum = new float[w * h];
        for (int i = 0; i < lum.Length; i++) lum[i] = comp[i] ? 1f : 0f;
        return new SkinSilhouetteResult(true, null, contour, lum, w, h, res, originY, originZ, tris,
                                        sw.ElapsedMilliseconds, diag.AsReadOnly());
    }

    /// <summary>Filled scanline rasterisation: a pixel is set when its centre lies in a projected triangle.</summary>
    private static bool[] Rasterize(double[] ys, double[] zs, int tris, int w, int h,
                                    Func<double, double> col, Func<double, double> row)
    {
        var mask = new bool[w * h];
        Span<double> cx = stackalloc double[3];
        Span<double> cy = stackalloc double[3];
        for (int t = 0; t < tris; t++)
        {
            for (int k = 0; k < 3; k++) { cx[k] = col(ys[3 * t + k]); cy[k] = row(zs[3 * t + k]); }
            int r0 = Math.Max(0, (int)Math.Ceiling(Math.Min(cy[0], Math.Min(cy[1], cy[2])) - 1e-9));
            int r1 = Math.Min(h - 1, (int)Math.Floor(Math.Max(cy[0], Math.Max(cy[1], cy[2])) + 1e-9));
            for (int r = r0; r <= r1; r++)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (int k = 0; k < 3; k++)
                {
                    int j = (k + 1) % 3;
                    double ya = cy[k], yb = cy[j];
                    if (Math.Abs(yb - ya) < 1e-12)
                    {
                        if (Math.Abs(r - ya) < 1e-9) { lo = Math.Min(lo, Math.Min(cx[k], cx[j])); hi = Math.Max(hi, Math.Max(cx[k], cx[j])); }
                        continue;
                    }
                    double s = (r - ya) / (yb - ya);
                    if (s < -1e-9 || s > 1 + 1e-9) continue;
                    double x = cx[k] + s * (cx[j] - cx[k]);
                    lo = Math.Min(lo, x); hi = Math.Max(hi, x);
                }
                if (lo > hi) continue;
                int c0 = Math.Max(0, (int)Math.Ceiling(lo - 1e-9)), c1 = Math.Min(w - 1, (int)Math.Floor(hi + 1e-9));
                for (int c = c0; c <= c1; c++) mask[r * w + c] = true;
            }
        }
        return mask;
    }

    /// <summary>
    /// Splits the boundary at the field-of-view cuts, then for each side takes the segment holding that side's most
    /// protruding point, trimmed at its top and bottom points. The side with the stronger protrusions (nose, lips,
    /// chin) wins. Returned chain is ordered top to bottom.
    /// </summary>
    private static List<(double X, double Y)>? ChooseFrontChain(List<(int X, int Y)> loop, double cutTop, double cutBottom,
        double res, List<string> diag, out bool facingRight, out string? failure)
    {
        facingRight = false;
        failure = null;
        int n = loop.Count;
        if (n < 3) { failure = "silhouette boundary is too short"; return null; }
        var onCut = loop.Select(p => p.Y < cutTop || p.Y > cutBottom).ToArray();
        int cutCount = onCut.Count(c => c);
        diag.Add($"boundary {n} px, {cutCount} px on the CT field-of-view cuts (±{F(FovCutToleranceMm, "F1")} mm)");
        if (cutCount == n) { failure = "the whole silhouette boundary lies on the CT field-of-view cuts"; return null; }

        List<(double X, double Y)>? best = null;
        double bestScore = double.NegativeInfinity, minLenPx = MinFrontChainMm / res;
        var eligible = LongRuns(loop, onCut, minLenPx);
        foreach (bool right in new[] { false, true })
        {
            var seg = SideSegment(loop, onCut, eligible, right);
            double len = seg.Count < 2 ? 0 : ProfileCurveTools.ArcLength(seg)[^1];
            int yMin = seg.Count == 0 ? 0 : (int)seg.Min(p => p.Y), yMax = seg.Count == 0 ? 0 : (int)seg.Max(p => p.Y);
            var top = ProfileBoundary.Protrusions(seg, right, yMin, yMax).Take(FacingPeakCount).ToList();
            double score = top.Sum(p => p.Prominence);
            diag.Add($"side {(right ? "right" : "left")}: chain {F(len * res, "F1")} mm, protrusion score {F(score * res, "F1")} mm");
            if (len < minLenPx || score <= bestScore) continue;
            best = seg;
            bestScore = score;
            facingRight = right;
        }
        if (best is null) failure = $"no front chain longer than {F(MinFrontChainMm, "F0")} mm on the skin silhouette";
        return best;
    }

    // Boundary run (between FOV cuts) through the most protruding point of one side, cut at its top and bottom points.
    private static List<(double X, double Y)> SideSegment(List<(int X, int Y)> loop, bool[] onCut, bool[] eligible, bool right)
    {
        int n = loop.Count, sign = right ? 1 : -1, tip = -1;
        for (int i = 0; i < n; i++)
            if (eligible[i] && (tip < 0 || sign * loop[i].X > sign * loop[tip].X)) tip = i;
        if (tip < 0) return new List<(double X, double Y)>();

        // Walk both ways from the tip until a cut (or the full loop).
        int back = 0, fwd = 0;
        while (back < n - 1 && !onCut[Mod(tip - back - 1, n)]) back++;
        while (fwd < n - 1 - back && !onCut[Mod(tip + fwd + 1, n)]) fwd++;
        var run = new List<(int X, int Y)>(back + fwd + 1);
        for (int k = -back; k <= fwd; k++) run.Add(loop[Mod(tip + k, n)]);
        int tipIdx = back;

        // Keep only the part between the run's topmost point and the bottommost point on the other side of the tip.
        int iTop = ArgExtreme(run, 0, run.Count - 1, topmost: true);
        int iBot = iTop <= tipIdx
            ? ArgExtreme(run, tipIdx, run.Count - 1, topmost: false)
            : ArgExtreme(run, 0, tipIdx, topmost: false);
        int from = Math.Min(iTop, iBot), to = Math.Max(iTop, iBot);
        var seg = new List<(double X, double Y)>(to - from + 1);
        for (int i = from; i <= to; i++) seg.Add((run[i].X, run[i].Y));
        if (seg.Count > 1 && seg[0].Y > seg[^1].Y) seg.Reverse();
        return seg;
    }

    // Points that belong to a run between FOV cuts at least minLenPx long (short slivers near a cut never hold the tip).
    private static bool[] LongRuns(List<(int X, int Y)> loop, bool[] onCut, double minLenPx)
    {
        int n = loop.Count;
        var eligible = new bool[n];
        int start = Array.IndexOf(onCut, true);
        if (start < 0) { Array.Fill(eligible, true); return eligible; }   // no cut: one closed run
        var run = new List<int>();
        for (int k = 1; k <= n; k++)
        {
            int i = (start + k) % n;
            if (!onCut[i]) { run.Add(i); continue; }
            double len = 0;
            for (int j = 1; j < run.Count; j++)
                len += Math.Sqrt(Math.Pow(loop[run[j]].X - loop[run[j - 1]].X, 2) + Math.Pow(loop[run[j]].Y - loop[run[j - 1]].Y, 2));
            if (len >= minLenPx) foreach (int j in run) eligible[j] = true;
            run.Clear();
        }
        return eligible;
    }

    // Index of the smallest (topmost) or largest Y in run[from..to]; first one on ties.
    private static int ArgExtreme(List<(int X, int Y)> run, int from, int to, bool topmost)
    {
        int best = from;
        for (int i = from + 1; i <= to; i++)
            if (topmost ? run[i].Y < run[best].Y : run[i].Y > run[best].Y) best = i;
        return best;
    }

    private static int Mod(int i, int n) => ((i % n) + n) % n;
}
