using System.Globalization;

namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>
/// Automatic extraction of the anterior soft-tissue profile line from a profile photo or a lateral
/// radiograph, using classical image processing only. Every failure returns an explicit reason.
/// </summary>
public static partial class ProfileExtractor
{
    /// <summary>Longest side of the internal working image.</summary>
    public const int WorkingLongestSide = 1200;

    private const double EdgeSearchRadius = 4.0;
    private const double ChainSmoothingSigma = 2.0;
    private const double ResampleStep = 1.0;
    private const double MinComponentFraction = 0.03;
    private const double MinChainFractionOfHeight = 0.15;
    private const double MinProminenceFraction = 0.02;
    private const int FacingPeakCount = 3;
    private const double AmbiguousProminenceRatio = 1.25;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Front-chain candidate for one facing direction. Score = sum of the top protrusions (nose, lips, chin).</summary>
    private sealed record SideCandidate(bool FacingRight, List<(double X, double Y)> Chain, double Score);

    /// <summary>Geometry shared by both pipelines once the working images are ready.</summary>
    /// <param name="Excluded">Points here get confidence 0 (ruler band, image frame).</param>
    /// <param name="FrameAt">Chain ends running along an image frame are trimmed.</param>
    private sealed record WorkingFrame(int OrigW, int OrigH, int W, int H, float[] GradMag,
                                       Func<double, double, bool>? Excluded, Func<double, double, bool>? FrameAt);

    private static string F(double v, string fmt = "F2") => v.ToString(fmt, Inv);

    private static string SideName(bool facingRight) => facingRight ? "right (+X)" : "left (-X)";

    /// <summary>Traces a component and keeps the boundary arc facing one side, with its nose prominence.</summary>
    private static SideCandidate? BuildCandidate(bool[] comp, int w, int h, bool facingRight, List<string> diag)
    {
        int yMin = int.MaxValue, yMax = -1;
        for (int i = 0; i < comp.Length; i++)
        {
            if (!comp[i]) continue;
            int y = i / w;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
        if (yMax < 0) return null;

        var loop = ProfileBoundary.TraceOuter(comp, w, h);
        var chain = ProfileBoundary.FrontChain(loop, facingRight, w, h);
        var top = ProfileBoundary.Protrusions(chain, facingRight, yMin, yMax).Take(FacingPeakCount).ToList();
        double score = top.Sum(p => p.Prominence);
        diag.Add($"side {SideName(facingRight)}: boundary {loop.Count} px, front chain {chain.Count} px, rows {yMin}..{yMax}, " +
                 $"top protrusions [{string.Join(", ", top.Select(p => $"{F(p.Prominence, "F0")} px @ row {p.Row}"))}], " +
                 $"score {F(score, "F0")}");
        return new SideCandidate(facingRight, chain, score);
    }

    /// <summary>Picks the facing side (auto or forced) among candidates. Returns null with a reason on failure.</summary>
    private static SideCandidate? ChooseSide(IReadOnlyList<SideCandidate> candidates, FacingOverride facing,
                                            int componentHeight, List<string> diag, out string? reason)
    {
        reason = null;
        if (facing != FacingOverride.Auto)
        {
            bool right = facing == FacingOverride.Right;
            var forced = candidates.FirstOrDefault(c => c.FacingRight == right);
            if (forced is null) reason = $"no front chain found on the forced side {SideName(right)}";
            else diag.Add($"facing forced: {SideName(right)}");
            return forced;
        }

        var ordered = candidates.OrderByDescending(c => c.Score).ToList();
        var best = ordered.FirstOrDefault();
        double minProm = Math.Max(3.0, MinProminenceFraction * componentHeight);
        if (best is null || best.Score < minProm)
        {
            reason = $"cannot detect the facing side: protrusion score below {F(minProm, "F1")} working px " +
                     "on both sides; pass FacingOverride.Left or FacingOverride.Right";
            return null;
        }
        diag.Add($"facing auto: {SideName(best.FacingRight)}");
        if (ordered.Count == 1)
            diag.Add("WARNING: only one side produced a silhouette, facing could not be cross-checked; verify or force the side");
        if (ordered.Count > 1 && best.Score < AmbiguousProminenceRatio * ordered[1].Score)
            diag.Add($"WARNING: facing ambiguous (scores {F(best.Score, "F1")} vs " +
                     $"{F(ordered[1].Score, "F1")}); check the result or force the side");
        return best;
    }

    /// <summary>Sub-pixel refinement, confidence, smoothing, resampling and mapping back to original pixels.</summary>
    private static ProfileExtractionResult Finish(SideCandidate side, WorkingFrame frame, List<string> diag)
    {
        var chain = TrimFrameEnds(side.Chain, frame, diag);
        int minChain = (int)(MinChainFractionOfHeight * frame.H);
        if (chain.Count < minChain)
            return ProfileExtractionResult.Fail(
                $"front profile too short ({chain.Count} px, need at least {minChain} working px)", diag);

        var (refined, grad, moved) = ProfileRefinement.RefineToEdge(
            chain, frame.GradMag, frame.W, frame.H, EdgeSearchRadius);
        diag.Add($"sub-pixel refinement: {moved}/{refined.Count} points moved (search ±{F(EdgeSearchRadius, "F0")} px)");

        var confidence = NormaliseConfidence(grad, refined, frame, diag);
        var smoothed = ProfileRefinement.Smooth(refined, ChainSmoothingSigma);
        var (pts, conf) = ProfileRefinement.Resample(smoothed, confidence, ResampleStep);

        double kx = (double)frame.OrigW / frame.W, ky = (double)frame.OrigH / frame.H;
        var original = pts.Select(p => ((p.X + 0.5) * kx - 0.5, (p.Y + 0.5) * ky - 0.5)).ToList();
        diag.Add($"output: {original.Count} points, mean confidence {F(conf.DefaultIfEmpty(0).Average())}");

        var contour = new ProfileContour(original.AsReadOnly(), conf.AsReadOnly(), side.FacingRight, false,
                                         diag.ToList().AsReadOnly());
        return ProfileExtractionResult.Ok(contour);
    }

    // Drops leading/trailing points that run along an image frame (e.g. a white border around an exported X-ray).
    private static List<(double X, double Y)> TrimFrameEnds(List<(double X, double Y)> chain, WorkingFrame frame,
                                                            List<string> diag)
    {
        if (frame.FrameAt is null || chain.Count == 0) return chain;
        int s = 0, e = chain.Count - 1;
        while (s <= e && frame.FrameAt(chain[s].X, chain[s].Y)) s++;
        while (e >= s && frame.FrameAt(chain[e].X, chain[e].Y)) e--;
        if (s > 0 || e < chain.Count - 1)
            diag.Add($"trimmed {s} leading and {chain.Count - 1 - e} trailing points running along the image frame");
        return s > e ? new List<(double X, double Y)>() : chain.GetRange(s, e - s + 1);
    }

    // Gradient / 95th percentile, clamped to [0,1]; zero on the image border and in excluded zones.
    private static List<double> NormaliseConfidence(List<double> grad, List<(double X, double Y)> pts,
                                                    WorkingFrame frame, List<string> diag)
    {
        var positive = grad.Where(g => g > 0).ToList();
        double p95 = positive.Count > 0 ? ProfileImageOps.Percentile(positive, 95) : 0;
        int zeroed = 0;
        var conf = new List<double>(grad.Count);
        for (int i = 0; i < grad.Count; i++)
        {
            bool invalid = ProfileBoundary.OnBorder(pts[i], frame.W, frame.H) ||
                           (frame.Excluded?.Invoke(pts[i].X, pts[i].Y) ?? false);
            if (invalid) zeroed++;
            conf.Add(invalid || p95 <= 0 ? 0 : Math.Clamp(grad[i] / p95, 0, 1));
        }
        diag.Add($"confidence: gradient p95 = {F(p95, "F4")}, {zeroed} points zeroed (image border or excluded zone)");
        return conf;
    }

    private static int ComponentHeight(bool[] comp, int w)
    {
        int first = Array.IndexOf(comp, true), last = Array.LastIndexOf(comp, true);
        return first < 0 ? 0 : last / w - first / w + 1;
    }

    private static List<double> StripValues(float[] img, int w, int h, bool rightSide, Func<int, int, bool>? skip)
    {
        int strip = Math.Max(2, (int)Math.Ceiling(0.03 * w));
        int x0 = rightSide ? w - strip : 0, x1 = rightSide ? w : strip;
        var values = new List<double>(strip * h);
        for (int y = 0; y < h; y++)
            for (int x = x0; x < x1; x++)
                if (skip is null || !skip(x, y)) values.Add(img[y * w + x]);
        return values;
    }
}
