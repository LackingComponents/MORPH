namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Indices of the automatic landmarks on a curve (initialisation only).</summary>
internal readonly record struct LandmarkIndices(int Nasion, int Pronasale, int Menton);

/// <summary>Curve utilities and automatic landmarks for profile registration (port of the T2 prototype).</summary>
internal static class ProfileCurveTools
{
    public const double MinConfidence = 0.05;
    private const double LandmarkSmoothSigma = 8;
    private const double DirectionSmoothSigma = 6;
    private const double NasionRiseFraction = 0.3;
    private const int NasionMaxInvalidRun = 20;
    private const double MentonMinDropFraction = 0.8;
    private const double MentonMinAngleDeg = 55;
    private const double MentonMinRunFraction = 0.3;

    /// <summary>
    /// Nasion, pronasale and menton on a curve facing +X, ordered top to bottom. Returns null with a reason when a
    /// landmark cannot be found.
    /// </summary>
    public static LandmarkIndices? FindLandmarks(IReadOnlyList<(double X, double Y)> p, IReadOnlyList<double> conf,
                                                 out string? failure)
    {
        failure = null;
        int n = p.Count;
        var x = GaussianReflect(p.Select(q => q.X).ToArray(), LandmarkSmoothSigma);
        var ok = conf.Select(c => c > MinConfidence).ToArray();

        int prn = -1;
        for (int i = 0; i < n; i++)
            if (ok[i] && (prn < 0 || x[i] > x[prn])) prn = i;
        if (prn < 0) { failure = "pronasale not found (no point with confidence > 0.05)"; return null; }

        // Nasion: deepest concavity walking up from the nose tip.
        int k = prn, mn = prn, bad = 0;
        while (k > 0)
        {
            k--;
            if (!ok[k]) { if (++bad > NasionMaxInvalidRun) break; continue; }
            bad = 0;
            if (x[k] < x[mn]) mn = k;
            if (x[k] > x[mn] + NasionRiseFraction * (x[prn] - x[mn]) && x[prn] - x[mn] > 0) break;
        }
        if (mn == prn) { failure = "nasion not found (no concavity above the nose tip)"; return null; }

        double drop = p[prn].Y - p[mn].Y;
        var dx = Gradient(GaussianReflect(p.Select(q => q.X).ToArray(), DirectionSmoothSigma));
        var dy = Gradient(GaussianReflect(p.Select(q => q.Y).ToArray(), DirectionSmoothSigma));
        var ang = new double[n];
        for (int i = 0; i < n; i++) ang[i] = Math.Atan2(-dx[i], dy[i]) * 180 / Math.PI; // 0 down, 90 backward
        var arc = ArcLength(p);

        for (int i = prn; i < n; i++)
        {
            if (p[i].Y - p[prn].Y < MentonMinDropFraction * drop || ang[i] <= MentonMinAngleDeg) continue;
            int j = i;
            while (j < n - 1 && ang[j] > MentonMinAngleDeg) j++;
            if (arc[j] - arc[i] >= MentonMinRunFraction * (p[i].Y - p[prn].Y))
                return new LandmarkIndices(mn, prn, i);
        }
        failure = "menton not found (no backward turn under the chin)";
        return null;
    }

    /// <summary>Gaussian filter, scipy.ndimage.gaussian_filter1d semantics (mode 'reflect', truncate 4).</summary>
    public static double[] GaussianReflect(double[] v, double sigma)
    {
        int n = v.Length, r = (int)(4 * sigma + 0.5);
        var k = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) { k[i + r] = Math.Exp(-0.5 * i * i / (sigma * sigma)); sum += k[i + r]; }
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = -r; j <= r; j++) s += k[j + r] * v[Reflect(i + j, n)];
            result[i] = s / sum;
        }
        return result;
    }

    // 'reflect' mode: d c b a | a b c d | d c b a
    private static int Reflect(int i, int n)
    {
        if (n == 1) return 0;
        int period = 2 * n;
        i = ((i % period) + period) % period;
        return i < n ? i : period - 1 - i;
    }

    /// <summary>numpy.gradient: central differences inside, one-sided at the ends.</summary>
    public static double[] Gradient(double[] v)
    {
        int n = v.Length;
        var g = new double[n];
        if (n < 2) return g;
        g[0] = v[1] - v[0];
        g[n - 1] = v[n - 1] - v[n - 2];
        for (int i = 1; i < n - 1; i++) g[i] = 0.5 * (v[i + 1] - v[i - 1]);
        return g;
    }

    public static double[] ArcLength(IReadOnlyList<(double X, double Y)> p)
    {
        var s = new double[p.Count];
        for (int i = 1; i < p.Count; i++)
            s[i] = s[i - 1] + Math.Sqrt((p[i].X - p[i - 1].X) * (p[i].X - p[i - 1].X) + (p[i].Y - p[i - 1].Y) * (p[i].Y - p[i - 1].Y));
        return s;
    }

    /// <summary>Uniform arc-length resampling (numpy arange semantics: last sample strictly before the end).</summary>
    public static List<(double X, double Y)> Resample(IReadOnlyList<(double X, double Y)> p, double step)
    {
        var d = ArcLength(p);
        var result = new List<(double X, double Y)>();
        int seg = 0;
        for (int k = 0; k * step < d[^1]; k++)
        {
            double s = k * step;
            while (seg < p.Count - 2 && d[seg + 1] < s) seg++;
            double len = d[seg + 1] - d[seg];
            double t = len > 0 ? (s - d[seg]) / len : 0;
            result.Add((p[seg].X + t * (p[seg + 1].X - p[seg].X), p[seg].Y + t * (p[seg + 1].Y - p[seg].Y)));
        }
        return result;
    }

    /// <summary>Every <paramref name="stride"/>-th sample, starting at 0 (numpy [::stride]).</summary>
    public static List<(double X, double Y)> Every(IReadOnlyList<(double X, double Y)> p, int stride) =>
        Enumerable.Range(0, (p.Count + stride - 1) / stride).Select(i => p[i * stride]).ToList();
}
