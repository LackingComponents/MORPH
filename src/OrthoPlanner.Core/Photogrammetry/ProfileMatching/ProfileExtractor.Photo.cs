namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

public static partial class ProfileExtractor
{
    private const int PhotoCloseRadius = 3;
    private const int PhotoOpenRadius = 2;
    private const double PhotoEdgeSigma = 1.0;
    private const double MaxUniformMadSum = 15.0;

    /// <summary>
    /// Extracts the anterior profile from a profile photo.
    /// </summary>
    /// <param name="rgb24">Pixels as R,G,B bytes, row-major, <paramref name="stride"/> bytes per row.</param>
    public static ProfileExtractionResult ExtractFromPhoto(byte[] rgb24, int width, int height, int stride,
                                                           FacingOverride facing = FacingOverride.Auto)
    {
        var diag = new List<string>();
        string? invalid = ValidatePhotoInput(rgb24, width, height, stride);
        if (invalid is not null) return ProfileExtractionResult.Fail(invalid, diag);

        try
        {
            return ExtractFromPhotoCore(rgb24, width, height, stride, facing, diag);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ProfileExtractionResult.Fail($"photo profile extraction crashed: {ex.GetType().Name}: {ex.Message}", diag);
        }
    }

    private static string? ValidatePhotoInput(byte[]? rgb24, int width, int height, int stride)
    {
        if (rgb24 is null) return "photo pixel buffer is null";
        if (width < 16 || height < 16) return $"photo too small ({width}x{height})";
        if (stride < width * 3) return $"stride {stride} is smaller than width*3 = {width * 3}";
        long needed = (long)stride * (height - 1) + width * 3L;
        if (rgb24.Length < needed) return $"photo buffer has {rgb24.Length} bytes, expected at least {needed}";
        return null;
    }

    private static ProfileExtractionResult ExtractFromPhotoCore(byte[] rgb, int width, int height, int stride,
                                                               FacingOverride facing, List<string> diag)
    {
        var (w, h) = ProfileImageOps.WorkingSize(width, height, WorkingLongestSide);
        diag.Add($"photo {width}x{height} -> working {w}x{h} (scale {F((double)w / width, "F4")})");

        var (lab0, lab1, lab2) = ToWorkingLab(rgb, width, height, stride, w, h);
        var lum = new float[lab0.Length];
        for (int i = 0; i < lum.Length; i++) lum[i] = lab0[i] / 100f;
        var grad = ProfileImageOps.GradientMagnitude(ProfileImageOps.GaussianBlur(lum, w, h, PhotoEdgeSigma), w, h);

        var comp = PhotoForeground(lab0, lab1, lab2, w, h, diag, out string? maskFailure);
        if (comp is null) return ProfileExtractionResult.Fail(maskFailure!, diag);

        var sides = facing == FacingOverride.Auto ? new[] { false, true } : new[] { facing == FacingOverride.Right };
        var candidates = sides.Select(r => BuildCandidate(comp, w, h, r, diag)).OfType<SideCandidate>().ToList();
        var chosen = ChooseSide(candidates, facing, ComponentHeight(comp, w), diag, out string? reason);
        if (chosen is null) return ProfileExtractionResult.Fail(reason!, diag);
        return Finish(chosen, new WorkingFrame(width, height, w, h, grad, null, null), diag);
    }

    private sealed record BackgroundModel(string Side, double L, double A, double B, double MadSum);

    /// <summary>
    /// Person mask: a background model (median Lab + MAD) from each 3% border strip; uniform strips are kept
    /// (a strip that is not uniform, e.g. a wall edge, is dropped unless none is uniform). A pixel's distance is
    /// its Lab distance to the nearest kept model; Otsu threshold, close/open, largest component.
    /// </summary>
    private static bool[]? PhotoForeground(float[] l, float[] a, float[] b, int w, int h, List<string> diag,
                                           out string? failure)
    {
        failure = null;
        var models = new[] { false, true }.Select(right =>
        {
            var (mL, madL) = ProfileImageOps.MedianMad(StripValues(l, w, h, right, null));
            var (mA, madA) = ProfileImageOps.MedianMad(StripValues(a, w, h, right, null));
            var (mB, madB) = ProfileImageOps.MedianMad(StripValues(b, w, h, right, null));
            var m = new BackgroundModel(right ? "right" : "left", mL, mA, mB, madL + madA + madB);
            diag.Add($"{m.Side} background Lab median ({F(mL, "F1")}, {F(mA, "F1")}, {F(mB, "F1")}), " +
                     $"MAD ({F(madL, "F1")}, {F(madA, "F1")}, {F(madB, "F1")})");
            return m;
        }).ToList();

        var kept = models.Where(m => m.MadSum <= MaxUniformMadSum).ToList();
        if (kept.Count == 0)
        {
            kept = new() { models.MinBy(m => m.MadSum)! };
            diag.Add($"WARNING: no border strip is uniform (sum of MAD > {F(MaxUniformMadSum, "F0")}); " +
                     $"using the {kept[0].Side} one, background separation may be poor");
        }
        else if (kept.Count < models.Count)
            diag.Add($"{models.Except(kept).Single().Side} strip not uniform (sum of MAD > {F(MaxUniformMadSum, "F0")}); ignored");
        diag.Add($"background model(s) used: {string.Join(", ", kept.Select(m => m.Side))}");

        var dist = new double[l.Length];
        for (int i = 0; i < dist.Length; i++)
        {
            double best = double.MaxValue;
            foreach (var m in kept)
            {
                double dl = l[i] - m.L, da = a[i] - m.A, db = b[i] - m.B;
                best = Math.Min(best, Math.Sqrt(dl * dl + da * da + db * db));
            }
            dist[i] = best;
        }
        double hi = ProfileImageOps.Percentile(dist.Where((_, i) => i % 3 == 0), 99.5);
        double t = ProfileImageOps.Otsu(dist, 0, hi);
        if (double.IsNaN(t))
        {
            failure = "the photo is uniform: nothing differs from the background colour";
            return null;
        }
        diag.Add($"Otsu threshold dE = {F(t, "F1")} (histogram range 0..{F(hi, "F1")})");

        var mask = new bool[dist.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = dist[i] > t;
        mask = ProfileImageOps.Open(ProfileImageOps.Close(mask, w, h, PhotoCloseRadius), w, h, PhotoOpenRadius);
        var (comp, size, count) = ProfileImageOps.LargestComponent(mask, w, h);
        double frac = (double)size / mask.Length;
        diag.Add($"morphology: close r={PhotoCloseRadius}, open r={PhotoOpenRadius}; {count} components, " +
                 $"largest {size} px ({F(100 * frac, "F1")}% of image)");
        if (frac < MinComponentFraction)
        {
            failure = $"could not separate the person from the background (largest region {F(100 * frac, "F1")}% of image)";
            return null;
        }
        return comp;
    }

    /// <summary>Area-averages the RGB bytes straight to the working size and converts to CIELab (D65).</summary>
    private static (float[] L, float[] A, float[] B) ToWorkingLab(byte[] rgb, int width, int height, int stride,
                                                                   int w, int h)
    {
        var r = new float[w * h];
        var g = new float[w * h];
        var b = new float[w * h];
        double fx0 = (double)width / w, fy0 = (double)height / h;
        Parallel.For(0, h, ty =>
        {
            int y0 = (int)(ty * fy0), y1 = Math.Max(y0 + 1, Math.Min(height, (int)((ty + 1) * fy0)));
            for (int tx = 0; tx < w; tx++)
            {
                int x0 = (int)(tx * fx0), x1 = Math.Max(x0 + 1, Math.Min(width, (int)((tx + 1) * fx0)));
                long sr = 0, sg = 0, sb = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0, p = y * stride + x0 * 3; x < x1; x++, p += 3)
                    {
                        sr += rgb[p];
                        sg += rgb[p + 1];
                        sb += rgb[p + 2];
                    }
                float n = 255f * (x1 - x0) * (y1 - y0);
                int i = ty * w + tx;
                r[i] = sr / n;
                g[i] = sg / n;
                b[i] = sb / n;
            }
        });

        var lOut = new float[w * h];
        var aOut = new float[w * h];
        var bOut = new float[w * h];
        for (int i = 0; i < lOut.Length; i++)
        {
            double lr = Linear(r[i]), lg = Linear(g[i]), lb = Linear(b[i]);
            double fx = LabF((0.4124 * lr + 0.3576 * lg + 0.1805 * lb) / 0.95047);
            double fy = LabF(0.2126 * lr + 0.7152 * lg + 0.0722 * lb);
            double fz = LabF((0.0193 * lr + 0.1192 * lg + 0.9505 * lb) / 1.08883);
            lOut[i] = (float)(116 * fy - 16);
            aOut[i] = (float)(500 * (fx - fy));
            bOut[i] = (float)(200 * (fy - fz));
        }
        return (lOut, aOut, bOut);
    }

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LabF(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
}
