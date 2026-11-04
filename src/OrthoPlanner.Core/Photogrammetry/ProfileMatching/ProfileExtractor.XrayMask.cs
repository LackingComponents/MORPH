namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

public static partial class ProfileExtractor
{
    private const double XrayOpenFractionOfWidth = 0.006;
    private const int XrayCloseRadius = 3;
    private const double OuterBandFraction = 0.25;
    private const double AirStripFraction = 0.03;
    private const double EdgeThresholdInSigmas = 5.0;
    private const double RayleighP25 = 0.758;
    private const double MinAirGradientNoise = 2.5e-4;
    private const double MinStrongEdgeFraction = 0.5;
    private const string LowContrastMessage = "soft tissue profile not visible enough on this radiograph";

    /// <summary>
    /// Soft-tissue mask assuming the face looks toward one side.
    /// Processed cephalograms often show skin at about the same grey level as the air, visible only as a sharp edge
    /// (with a dark edge-enhancement halo), while the air itself drifts slowly (soft-tissue filter, heel effect).
    /// So the air is grown from the border of that side through smooth pixels (gradient below 5 sigma of the air's own
    /// gradient noise); everything the air cannot reach is the subject. Then open (removes thin
    /// rods), close, largest component.
    /// </summary>
    private static bool[]? XrayTissueMaskForSide(float[] lum, float[] grad, bool[] invalid, int w, int h, bool rightSide,
                                                 List<string> diag, out double edgeT, out string? failure)
    {
        failure = null;
        edgeT = double.NaN;
        string side = rightSide ? "right" : "left";
        var strip = AirStripPixels(invalid, w, h, rightSide);
        if (strip.Count == 0)
        {
            failure = "border strip has no usable pixels";
            diag.Add($"{side}: {failure}");
            return null;
        }

        // Gradient noise of the air: for Gaussian noise the gradient magnitude is Rayleigh distributed, whose 25th
        // percentile is 0.758 sigma. Using a low percentile tolerates printed scales/text covering most of the strip.
        var stripGrad = strip.Select(i => (double)grad[i]).ToList();
        double gP25 = ProfileImageOps.Percentile(stripGrad, 25);
        double sigma = Math.Max(gP25 / RayleighP25, MinAirGradientNoise);
        var (lMed, lMad) = ProfileImageOps.MedianMad(strip.Select(i => (double)lum[i]).ToList());
        edgeT = EdgeThresholdInSigmas * sigma;
        var edges = new bool[grad.Length];
        for (int i = 0; i < edges.Length; i++) edges[i] = grad[i] >= edgeT;
        edges = ProfileImageOps.Dilate(edges, w, h, 1); // plug one-pixel gaps in the skin edge

        var air = FloodAir(strip, edges, invalid, w, h);
        int airCount = air.Count(a => a);
        diag.Add($"{side}: air strip luminance median {F(lMed, "F3")} MAD {F(lMad, "F4")}; gradient p25 {F(gP25, "F5")} " +
                 $"-> noise sigma {F(sigma, "F5")} -> edge threshold {F(edgeT, "F4")}; air region {F(100.0 * airCount / air.Length, "F1")}% of image");

        var mask = new bool[lum.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = !air[i] && !invalid[i];
        int openR = Math.Max(1, (int)Math.Round(XrayOpenFractionOfWidth * w));
        mask = ProfileImageOps.Close(ProfileImageOps.Open(mask, w, h, openR), w, h, XrayCloseRadius);
        var (comp, size, count) = ProfileImageOps.LargestComponent(mask, w, h);
        double frac = (double)size / mask.Length;
        diag.Add($"{side}: open r={openR}, close r={XrayCloseRadius}; {count} components, largest {size} px ({F(100 * frac, "F1")}%)");
        if (frac < MinComponentFraction || frac > 1 - MinComponentFraction)
        {
            failure = $"no usable silhouette (largest region {F(100 * frac, "F1")}% of image)";
            diag.Add($"{side}: {failure}");
            return null;
        }
        return comp;
    }

    /// <summary>
    /// Contrast check on the chosen front chain: share of points lying on a clear edge (gradient at or above the
    /// side's edge threshold within 2 px). Below 50% the profile is not trusted.
    /// </summary>
    private static string? CheckProfileContrast(List<(double X, double Y)> chain, float[] grad, int w, int h,
                                                double edgeThreshold, List<string> diag)
    {
        int ok = chain.Count(p => MaxGradientNear(grad, w, h, p) >= edgeThreshold);
        double share = chain.Count > 0 ? (double)ok / chain.Count : 0;
        diag.Add($"profile contrast: {F(100 * share, "F0")}% of front-chain points on a clear edge " +
                 $"(need {F(100 * MinStrongEdgeFraction, "F0")}%)");
        return share < MinStrongEdgeFraction ? LowContrastMessage : null;
    }

    private static double MaxGradientNear(float[] grad, int w, int h, (double X, double Y) p)
    {
        int cx = (int)Math.Round(p.X), cy = (int)Math.Round(p.Y);
        double best = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < w && y < h) best = Math.Max(best, grad[y * w + x]);
            }
        return best;
    }

    /// <summary>Per row, the first 3%-of-width valid pixels inward from the border (skips frames and the ruler band).</summary>
    private static List<int> AirStripPixels(bool[] invalid, int w, int h, bool rightSide)
    {
        int strip = Math.Max(2, (int)Math.Ceiling(AirStripFraction * w));
        int maxDepth = Math.Max(strip, (int)(OuterBandFraction * w));
        var pixels = new List<int>(strip * h);
        for (int y = 0; y < h; y++)
        {
            int taken = 0;
            for (int d = 0; d < maxDepth && taken < strip; d++)
            {
                int x = rightSide ? w - 1 - d : d;
                if (invalid[y * w + x]) continue;
                pixels.Add(y * w + x);
                taken++;
            }
        }
        return pixels;
    }

    /// <summary>4-connected flood from the air strip through valid, non-edge pixels.</summary>
    private static bool[] FloodAir(List<int> seeds, bool[] edges, bool[] invalid, int w, int h)
    {
        var air = new bool[edges.Length];
        var stack = new Stack<int>();
        foreach (int s in seeds)
            if (!edges[s] && !air[s]) { air[s] = true; stack.Push(s); }
        while (stack.Count > 0)
        {
            int p = stack.Pop(), px = p % w, py = p / w;
            Visit(px - 1, py); Visit(px + 1, py); Visit(px, py - 1); Visit(px, py + 1);
        }
        return air;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int q = y * w + x;
            if (air[q] || edges[q] || invalid[q]) return;
            air[q] = true;
            stack.Push(q);
        }
    }
}
