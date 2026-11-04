namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

public static partial class ProfileExtractor
{
    private const double XraySmoothSigma = 1.5;
    private const double RulerBandFractionOfWidth = 0.03;
    private const double RulerExtensionFractionOfHeight = 0.05;
    private const float PaddingWhiteLevel = 0.97f;
    private const double PaddingGrowFraction = 0.01;

    /// <summary>
    /// Extracts the anterior soft-tissue profile from a lateral radiograph.
    /// </summary>
    /// <param name="luminance">Row-major [0,1], bone bright (as CephXrayImage.Luminance).</param>
    /// <param name="rulerStart">Ruler calibration endpoint in original pixels, if known (excluded from the search).</param>
    public static ProfileExtractionResult ExtractFromXray(float[] luminance, int width, int height,
                                                          (double X, double Y)? rulerStart, (double X, double Y)? rulerEnd,
                                                          FacingOverride facing = FacingOverride.Auto)
    {
        var diag = new List<string>();
        if (luminance is null) return ProfileExtractionResult.Fail("radiograph luminance buffer is null", diag);
        if (width < 16 || height < 16) return ProfileExtractionResult.Fail($"radiograph too small ({width}x{height})", diag);
        if (luminance.Length != (long)width * height)
            return ProfileExtractionResult.Fail(
                $"radiograph buffer has {luminance.Length} values, expected {width}x{height} = {(long)width * height}", diag);

        try
        {
            return ExtractFromXrayCore(luminance, width, height, rulerStart, rulerEnd, facing, diag);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ProfileExtractionResult.Fail($"radiograph profile extraction crashed: {ex.GetType().Name}: {ex.Message}", diag);
        }
    }

    private static ProfileExtractionResult ExtractFromXrayCore(float[] luminance, int width, int height,
        (double X, double Y)? rulerStart, (double X, double Y)? rulerEnd, FacingOverride facing, List<string> diag)
    {
        var (w, h) = ProfileImageOps.WorkingSize(width, height, WorkingLongestSide);
        double kx = (double)w / width, ky = (double)h / height;
        diag.Add($"radiograph {width}x{height} -> working {w}x{h} (scale {F(kx, "F4")}), Gaussian sigma {F(XraySmoothSigma, "F1")}");

        var raw = ProfileImageOps.AreaDownscale(luminance, width, height, w, h);
        var lum = ProfileImageOps.GaussianBlur(raw, w, h, XraySmoothSigma);
        var (invalid, frameMask) = BuildInvalidMask(raw, w, h, rulerStart, rulerEnd, width, height, kx, ky, diag);

        var grad = ProfileImageOps.GradientMagnitude(lum, w, h);
        var sides = facing == FacingOverride.Auto ? new[] { false, true } : new[] { facing == FacingOverride.Right };
        var candidates = new List<SideCandidate>();
        var edgeThresholds = new Dictionary<bool, double>();
        var failures = new List<string>();
        int compHeight = 0;
        foreach (bool right in sides)
        {
            var comp = XrayTissueMaskForSide(lum, grad, invalid, w, h, right, diag, out double edgeT, out string? sideFailure);
            if (comp is null) { failures.Add($"{(right ? "right" : "left")}: {sideFailure}"); continue; }
            edgeThresholds[right] = edgeT;
            compHeight = Math.Max(compHeight, ComponentHeight(comp, w));
            var cand = BuildCandidate(comp, w, h, right, diag);
            if (cand is not null) candidates.Add(cand);
        }
        if (candidates.Count == 0)
            return ProfileExtractionResult.Fail($"no soft-tissue silhouette found ({string.Join("; ", failures)})", diag);

        var chosen = ChooseSide(candidates, facing, compHeight, diag, out string? reason);
        if (chosen is null) return ProfileExtractionResult.Fail(reason!, diag);
        string? lowContrast = CheckProfileContrast(chosen.Chain, grad, w, h, edgeThresholds[chosen.FacingRight], diag);
        if (lowContrast is not null) return ProfileExtractionResult.Fail(lowContrast, diag);

        var nearInvalid = ProfileImageOps.Dilate(invalid, w, h, 2);
        var nearFrame = ProfileImageOps.Dilate(frameMask, w, h, 2);
        return Finish(chosen, new WorkingFrame(width, height, w, h, grad,
                                               (x, y) => At(nearInvalid, w, h, x, y), (x, y) => At(nearFrame, w, h, x, y)), diag);
    }

    private static bool At(bool[] mask, int w, int h, double x, double y) =>
        mask[Math.Clamp((int)Math.Round(y), 0, h - 1) * w + Math.Clamp((int)Math.Round(x), 0, w - 1)];

    /// <summary>
    /// Working pixels never used as tissue or air: white frames connected to the border (grown over their soft edge)
    /// and the ruler band. Also returns the frame part alone.
    /// </summary>
    private static (bool[] Invalid, bool[] Frame) BuildInvalidMask(float[] raw, int w, int h, (double X, double Y)? rulerStart,
                                           (double X, double Y)? rulerEnd, int width, int height,
                                           double kx, double ky, List<string> diag)
    {
        var padding = ProfileImageOps.BorderConnectedAtLeast(raw, w, h, PaddingWhiteLevel);
        int padCount = padding.Count(p => p);
        bool[] invalid = padding;
        if (padCount > 0)
        {
            // Grow the frame so its soft (anti-aliased) edge is not mistaken for tissue or air.
            int grow = Math.Max(2, (int)Math.Ceiling(PaddingGrowFraction * w));
            invalid = ProfileImageOps.Dilate(padding, w, h, grow);
            diag.Add($"white frame/padding connected to the border: {padCount} working px " +
                     $"({F(100.0 * padCount / padding.Length, "F1")}%), grown by {grow} px, treated as background");
        }

        var frame = invalid;
        var ruler = BuildRulerExclusion(rulerStart, rulerEnd, width, height, kx, ky, diag);
        if (ruler is null) return (invalid, frame);
        var withRuler = new bool[invalid.Length];
        for (int i = 0; i < invalid.Length; i++) withRuler[i] = invalid[i] || ruler(i % w, i / w);
        return (withRuler, frame);
    }

    /// <summary>
    /// Band of ±3% image width around the ruler segment, extended 5% of image height past both ends.
    /// Returns a working-pixel predicate, or null when no ruler is given.
    /// </summary>
    private static Func<int, int, bool>? BuildRulerExclusion((double X, double Y)? start, (double X, double Y)? end,
                                                            int width, int height, double kx, double ky, List<string> diag)
    {
        if (start is null && end is null) { diag.Add("ruler: not provided, nothing excluded"); return null; }
        if (start is null || end is null)
        {
            diag.Add("WARNING: only one ruler endpoint provided; ruler exclusion skipped");
            return null;
        }
        var (a, b) = (start.Value, end.Value);
        if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(b.X) || !double.IsFinite(b.Y))
        {
            diag.Add("WARNING: ruler endpoints are not finite; ruler exclusion skipped");
            return null;
        }

        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double ux = len > 1e-9 ? dx / len : 0, uy = len > 1e-9 ? dy / len : 1;
        double band = RulerBandFractionOfWidth * width, ext = RulerExtensionFractionOfHeight * height;
        var corners = new[] { (-ext, -band), (len + ext, -band), (len + ext, band), (-ext, band) }
            .Select(c => (X: a.X + ux * c.Item1 - uy * c.Item2, Y: a.Y + uy * c.Item1 + ux * c.Item2)).ToArray();
        diag.Add($"ruler excluded band (original px), corners: " +
                 string.Join(" ", corners.Select(c => $"({F(c.X, "F0")},{F(c.Y, "F0")})")) +
                 $"; bounding rect x {F(corners.Min(c => c.X), "F0")}..{F(corners.Max(c => c.X), "F0")}, " +
                 $"y {F(corners.Min(c => c.Y), "F0")}..{F(corners.Max(c => c.Y), "F0")}");

        return (x, y) =>
        {
            // Working pixel centre back to original pixels, then into the ruler frame.
            double ox = (x + 0.5) / kx - 0.5 - a.X, oy = (y + 0.5) / ky - 0.5 - a.Y;
            double along = ox * ux + oy * uy, across = -ox * uy + oy * ux;
            return along >= -ext && along <= len + ext && Math.Abs(across) <= band;
        };
    }
}
