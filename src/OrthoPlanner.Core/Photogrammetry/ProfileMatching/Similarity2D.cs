namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>2D similarity p' = S * R(theta) * p + T (no reflection). Immutable.</summary>
internal readonly record struct Similarity2D(double Scale, double Theta, double Tx, double Ty)
{
    public static Similarity2D Identity => new(1, 0, 0, 0);

    public (double X, double Y) Apply((double X, double Y) p)
    {
        double c = Math.Cos(Theta) * Scale, s = Math.Sin(Theta) * Scale;
        return (c * p.X - s * p.Y + Tx, s * p.X + c * p.Y + Ty);
    }

    public (double X, double Y)[] Apply(IReadOnlyList<(double X, double Y)> pts)
    {
        var result = new (double X, double Y)[pts.Count];
        for (int i = 0; i < result.Length; i++) result[i] = Apply(pts[i]);
        return result;
    }

    public Similarity2D Inverse()
    {
        double si = 1 / Scale, c = Math.Cos(-Theta) * si, s = Math.Sin(-Theta) * si;
        return new Similarity2D(si, -Theta, -(c * Tx - s * Ty), -(s * Tx + c * Ty));
    }

    /// <summary>This transform applied after <paramref name="first"/>.</summary>
    public Similarity2D After(Similarity2D first)
    {
        var t = Apply((first.Tx, first.Ty));
        return new Similarity2D(Scale * first.Scale, Theta + first.Theta, t.X, t.Y);
    }

    public double RotationDeg => Math.Atan2(Math.Sin(Theta), Math.Cos(Theta)) * 180 / Math.PI;

    /// <summary>Largest relative change between two transforms (scale ratio, angle, translation / extent).</summary>
    public double ChangeFrom(Similarity2D other, double extent) =>
        Math.Max(Math.Max(Math.Abs(Scale / other.Scale - 1), Math.Abs(Theta - other.Theta)),
                 Math.Sqrt((Tx - other.Tx) * (Tx - other.Tx) + (Ty - other.Ty) * (Ty - other.Ty)) / Math.Max(extent, 1e-12));

    /// <summary>
    /// Weighted Umeyama fit dst ~ s R src + t, closed form in 2D (rotation only, no reflection).
    /// Returns null when the source points are degenerate.
    /// </summary>
    public static Similarity2D? Fit(IReadOnlyList<(double X, double Y)> src, IReadOnlyList<(double X, double Y)> dst,
                                    IReadOnlyList<double>? weights = null)
    {
        int n = src.Count;
        double sw = 0, msx = 0, msy = 0, mdx = 0, mdy = 0;
        for (int i = 0; i < n; i++)
        {
            double w = weights?[i] ?? 1;
            sw += w;
            msx += w * src[i].X; msy += w * src[i].Y;
            mdx += w * dst[i].X; mdy += w * dst[i].Y;
        }
        if (sw <= 0) return null;
        msx /= sw; msy /= sw; mdx /= sw; mdy /= sw;

        double a = 0, b = 0, var = 0;
        for (int i = 0; i < n; i++)
        {
            double w = weights?[i] ?? 1;
            double sx = src[i].X - msx, sy = src[i].Y - msy, dx = dst[i].X - mdx, dy = dst[i].Y - mdy;
            a += w * (sx * dx + sy * dy);
            b += w * (sx * dy - sy * dx);
            var += w * (sx * sx + sy * sy);
        }
        if (var <= 1e-18) return null;
        double theta = Math.Atan2(b, a);
        double scale = Math.Sqrt(a * a + b * b) / var;
        double c = Math.Cos(theta) * scale, s = Math.Sin(theta) * scale;
        return new Similarity2D(scale, theta, mdx - (c * msx - s * msy), mdy - (s * msx + c * msy));
    }
}
