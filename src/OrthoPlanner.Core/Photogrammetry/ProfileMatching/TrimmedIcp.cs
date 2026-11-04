namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Trimmed point-to-line ICP with a similarity transform (port of the T2 prototype `icp` / `icp_sym`).</summary>
internal static class TrimmedIcp
{
    public const double Keep = 0.8;
    private const double ConvergenceTolerance = 1e-6;

    /// <summary>
    /// Maps <paramref name="src"/> onto the target of <paramref name="dst"/>. Each iteration keeps the best 80% of
    /// the valid residuals and refits. Returns the transform and the final correspondences (null if degenerate).
    /// </summary>
    public static (Similarity2D T, Correspondence[] Final)? Run(
        IReadOnlyList<(double X, double Y)> src, PolylineMatcher dst, Similarity2D start, int maxIterations)
    {
        var t = start;
        double extent = Extent(dst.Points);
        for (int it = 0; it < maxIterations; it++)
        {
            var corr = dst.Match(t.Apply(src));
            var sel = Select(corr, null);
            if (sel.Count < 3) return null;
            var next = Similarity2D.Fit(sel.Select(i => src[i]).ToList(), sel.Select(i => corr[i].Foot).ToList());
            if (next is null) return null;
            bool converged = next.Value.ChangeFrom(t, extent) < ConvergenceTolerance;
            t = next.Value;
            if (converged) break;
        }
        return (t, dst.Match(t.Apply(src)));
    }

    /// <summary>
    /// Symmetric ICP: correspondences src->dst and dst->src (through the current inverse), each trimmed to 80%,
    /// one similarity fitted on the union. Optional per-sample weights on <paramref name="src"/> (block bootstrap);
    /// dst->src matches take the weight of the src vertex they land on.
    /// </summary>
    public static Similarity2D? RunSymmetric(
        IReadOnlyList<(double X, double Y)> src, PolylineMatcher srcCurve,
        IReadOnlyList<(double X, double Y)> dst, PolylineMatcher dstCurve,
        Similarity2D start, int maxIterations, IReadOnlyList<double>? srcWeights = null)
    {
        var t = start;
        double extent = Extent(dstCurve.Points);
        for (int it = 0; it < maxIterations; it++)
        {
            var c1 = dstCurve.Match(t.Apply(src));
            var s1 = Select(c1, srcWeights is null ? null : i => srcWeights[i]);
            var c2 = srcCurve.Match(t.Inverse().Apply(dst));
            var s2 = Select(c2, srcWeights is null ? null : i => srcWeights[c2[i].Vertex]);

            var from = new List<(double X, double Y)>(s1.Count + s2.Count);
            var to = new List<(double X, double Y)>(s1.Count + s2.Count);
            var w = new List<double>(s1.Count + s2.Count);
            foreach (int i in s1) { from.Add(src[i]); to.Add(c1[i].Foot); w.Add(srcWeights?[i] ?? 1); }
            foreach (int i in s2) { from.Add(c2[i].Foot); to.Add(dst[i]); w.Add(srcWeights?[c2[i].Vertex] ?? 1); }
            if (from.Count < 3) return null;

            var next = Similarity2D.Fit(from, to, w);
            if (next is null) return null;
            bool converged = next.Value.ChangeFrom(t, extent) < ConvergenceTolerance;
            t = next.Value;
            if (converged) break;
        }
        return t;
    }

    /// <summary>Indices of valid matches (positive weight) whose residual is within the 80% quantile.</summary>
    private static List<int> Select(Correspondence[] corr, Func<int, double>? weight)
    {
        var candidates = new List<int>(corr.Length);
        for (int i = 0; i < corr.Length; i++)
            if (corr[i].Valid && (weight is null || weight(i) > 0)) candidates.Add(i);
        if (candidates.Count == 0) return candidates;
        double limit = Quantile(candidates.Select(i => corr[i].Residual).ToList(), Keep);
        return candidates.Where(i => corr[i].Residual <= limit).ToList();
    }

    /// <summary>Linear-interpolated quantile (numpy default).</summary>
    public static double Quantile(List<double> values, double q)
    {
        if (values.Count == 0) return double.NaN;
        var v = values.OrderBy(x => x).ToArray();
        double pos = q * (v.Length - 1);
        int lo = (int)Math.Floor(pos), hi = Math.Min(lo + 1, v.Length - 1);
        return v[lo] + (pos - lo) * (v[hi] - v[lo]);
    }

    private static double Extent(IReadOnlyList<(double X, double Y)> pts)
    {
        double w = pts.Max(p => p.X) - pts.Min(p => p.X), h = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        return Math.Sqrt(w * w + h * h);
    }
}
