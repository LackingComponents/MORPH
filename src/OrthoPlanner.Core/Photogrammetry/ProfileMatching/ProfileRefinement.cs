namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Sub-pixel edge refinement, chain smoothing and uniform arc-length resampling.</summary>
internal static class ProfileRefinement
{
    private const double SearchStep = 0.5;

    /// <summary>
    /// Moves each point along its local normal (±radius working px) to the gradient-magnitude maximum,
    /// with a parabolic fit around the best sample. Returns refined points, the gradient at each, and how many moved.
    /// </summary>
    public static (List<(double X, double Y)> Points, List<double> Gradient, int Moved) RefineToEdge(
        List<(double X, double Y)> chain, float[] gradMag, int w, int h, double radius)
    {
        var pts = new List<(double X, double Y)>(chain.Count);
        var grad = new List<double>(chain.Count);
        int moved = 0;
        int samples = (int)Math.Round(2 * radius / SearchStep) + 1;
        var profile = new double[samples];

        for (int i = 0; i < chain.Count; i++)
        {
            var (nx, ny) = Normal(chain, i);
            var p = chain[i];
            int best = 0;
            for (int k = 0; k < samples; k++)
            {
                double t = -radius + k * SearchStep;
                profile[k] = ProfileImageOps.Bilinear(gradMag, w, h, p.X + nx * t, p.Y + ny * t);
                if (profile[k] > profile[best]) best = k;
            }
            if (profile[best] <= 1e-9 || (nx == 0 && ny == 0))
            {
                pts.Add(p);
                grad.Add(0);
                continue;
            }
            double offset = 0;
            if (best > 0 && best < samples - 1)
            {
                double a = profile[best - 1], b = profile[best], c = profile[best + 1];
                double den = a - 2 * b + c;
                if (Math.Abs(den) > 1e-12) offset = Math.Clamp(0.5 * (a - c) / den, -0.5, 0.5) * SearchStep;
            }
            double tBest = -radius + best * SearchStep + offset;
            pts.Add((p.X + nx * tBest, p.Y + ny * tBest));
            grad.Add(profile[best]);
            moved++;
        }
        return (pts, grad, moved);
    }

    // Unit normal from the chord between neighbours two steps apart.
    private static (double X, double Y) Normal(List<(double X, double Y)> chain, int i)
    {
        var a = chain[Math.Max(0, i - 2)];
        var b = chain[Math.Min(chain.Count - 1, i + 2)];
        double tx = b.X - a.X, ty = b.Y - a.Y;
        double len = Math.Sqrt(tx * tx + ty * ty);
        return len < 1e-12 ? (0, 0) : (-ty / len, tx / len);
    }

    /// <summary>Gaussian smoothing along an open chain (sigma in points, kernel renormalised at the ends).</summary>
    public static List<(double X, double Y)> Smooth(List<(double X, double Y)> chain, double sigma)
    {
        int n = chain.Count;
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new double[2 * r + 1];
        for (int j = -r; j <= r; j++) k[j + r] = Math.Exp(-j * j / (2 * sigma * sigma));

        var result = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++)
        {
            double sx = 0, sy = 0, sw = 0;
            for (int j = -r; j <= r; j++)
            {
                int q = i + j;
                if (q < 0 || q >= n) continue;
                sx += k[j + r] * chain[q].X;
                sy += k[j + r] * chain[q].Y;
                sw += k[j + r];
            }
            result.Add((sx / sw, sy / sw));
        }
        return result;
    }

    /// <summary>Resamples points (and a per-point value) at uniform arc length.</summary>
    public static (List<(double X, double Y)> Points, List<double> Values) Resample(
        List<(double X, double Y)> chain, List<double> values, double step)
    {
        var outPts = new List<(double X, double Y)>();
        var outVals = new List<double>();
        if (chain.Count == 0) return (outPts, outVals);

        var cum = new double[chain.Count];
        for (int i = 1; i < chain.Count; i++)
        {
            double dx = chain[i].X - chain[i - 1].X, dy = chain[i].Y - chain[i - 1].Y;
            cum[i] = cum[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
        double total = cum[^1];
        int seg = 0;
        for (double s = 0; s <= total + 1e-9; s += step)
        {
            while (seg < chain.Count - 2 && cum[seg + 1] < s) seg++;
            double len = cum[Math.Min(seg + 1, chain.Count - 1)] - cum[seg];
            double t = len > 1e-12 ? Math.Clamp((s - cum[seg]) / len, 0, 1) : 0;
            int nxt = Math.Min(seg + 1, chain.Count - 1);
            outPts.Add((chain[seg].X + t * (chain[nxt].X - chain[seg].X),
                        chain[seg].Y + t * (chain[nxt].Y - chain[seg].Y)));
            outVals.Add(values[seg] + t * (values[nxt] - values[seg]));
        }
        return (outPts, outVals);
    }
}
