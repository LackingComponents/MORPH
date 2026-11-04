namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Small image-processing toolbox for profile extraction (row-major float/bool arrays).</summary>
internal static class ProfileImageOps
{
    /// <summary>Working size for a target longest side; never upscales.</summary>
    public static (int W, int H) WorkingSize(int width, int height, int longestSide)
    {
        double s = Math.Min(1.0, (double)longestSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * s)), Math.Max(1, (int)Math.Round(height * s)));
    }

    /// <summary>Area-average downscale from (w,h) to (tw,th). Each target pixel averages its source box.</summary>
    public static float[] AreaDownscale(float[] src, int w, int h, int tw, int th)
    {
        if (tw == w && th == h) return (float[])src.Clone();
        var dst = new float[tw * th];
        double fx = (double)w / tw, fy = (double)h / th;
        for (int ty = 0; ty < th; ty++)
        {
            int y0 = (int)(ty * fy), y1 = Math.Max(y0 + 1, Math.Min(h, (int)((ty + 1) * fy)));
            for (int tx = 0; tx < tw; tx++)
            {
                int x0 = (int)(tx * fx), x1 = Math.Max(x0 + 1, Math.Min(w, (int)((tx + 1) * fx)));
                double sum = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++) sum += src[row + x];
                }
                dst[ty * tw + tx] = (float)(sum / ((x1 - x0) * (y1 - y0)));
            }
        }
        return dst;
    }

    /// <summary>Separable Gaussian blur with clamped borders.</summary>
    public static float[] GaussianBlur(float[] src, int w, int h, double sigma)
    {
        if (sigma <= 0) return (float[])src.Clone();
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new double[2 * r + 1];
        double ks = 0;
        for (int i = -r; i <= r; i++) { k[i + r] = Math.Exp(-i * i / (2 * sigma * sigma)); ks += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= ks;

        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int i = -r; i <= r; i++) s += k[i + r] * src[y * w + Math.Clamp(x + i, 0, w - 1)];
                tmp[y * w + x] = (float)s;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int i = -r; i <= r; i++) s += k[i + r] * tmp[Math.Clamp(y + i, 0, h - 1) * w + x];
                dst[y * w + x] = (float)s;
            }
        return dst;
    }

    /// <summary>Gradient magnitude by central differences.</summary>
    public static float[] GradientMagnitude(float[] img, int w, int h)
    {
        var g = new float[img.Length];
        for (int y = 0; y < h; y++)
        {
            int ym = Math.Max(0, y - 1), yp = Math.Min(h - 1, y + 1);
            for (int x = 0; x < w; x++)
            {
                int xm = Math.Max(0, x - 1), xp = Math.Min(w - 1, x + 1);
                float gx = (img[y * w + xp] - img[y * w + xm]) * 0.5f;
                float gy = (img[yp * w + x] - img[ym * w + x]) * 0.5f;
                g[y * w + x] = MathF.Sqrt(gx * gx + gy * gy);
            }
        }
        return g;
    }

    /// <summary>Bilinear sample with clamped coordinates.</summary>
    public static double Bilinear(float[] img, int w, int h, double x, double y)
    {
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        int x0 = Math.Min((int)x, w - 2 < 0 ? 0 : w - 2), y0 = Math.Min((int)y, h - 2 < 0 ? 0 : h - 2);
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double fx = x - x0, fy = y - y0;
        double top = img[y0 * w + x0] * (1 - fx) + img[y0 * w + x1] * fx;
        double bot = img[y1 * w + x0] * (1 - fx) + img[y1 * w + x1] * fx;
        return top * (1 - fy) + bot * fy;
    }

    public static double Median(List<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    /// <summary>Median and median absolute deviation.</summary>
    public static (double Median, double Mad) MedianMad(List<double> values)
    {
        double med = Median(values);
        double mad = Median(values.Select(v => Math.Abs(v - med)).ToList());
        return (med, mad);
    }

    public static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return double.NaN;
        int idx = Math.Clamp((int)Math.Round(p / 100.0 * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[idx];
    }

    /// <summary>Otsu threshold of values inside [lo, hi] with 256 bins. Returns NaN when the range is empty.</summary>
    public static double Otsu(IEnumerable<double> values, double lo, double hi)
    {
        const int bins = 256;
        if (!(hi > lo)) return double.NaN;
        var hist = new long[bins];
        long total = 0;
        foreach (double v in values)
        {
            int b = (int)((Math.Clamp(v, lo, hi) - lo) / (hi - lo) * (bins - 1));
            hist[b]++;
            total++;
        }
        if (total == 0) return double.NaN;

        double sumAll = 0;
        for (int i = 0; i < bins; i++) sumAll += i * (double)hist[i];
        double sumB = 0, best = -1;
        long wB = 0;
        int bestIdx = 0;
        for (int i = 0; i < bins; i++)
        {
            wB += hist[i];
            if (wB == 0) continue;
            long wF = total - wB;
            if (wF == 0) break;
            sumB += i * (double)hist[i];
            double mB = sumB / wB, mF = (sumAll - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; bestIdx = i; }
        }
        return lo + (bestIdx + 0.5) / (bins - 1) * (hi - lo);
    }

    /// <summary>Pixels connected to the image border whose value is at least <paramref name="minValue"/> (e.g. white frames).</summary>
    public static bool[] BorderConnectedAtLeast(float[] img, int w, int h, float minValue)
    {
        var mark = new bool[img.Length];
        var stack = new Stack<int>();
        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (!mark[i] && img[i] >= minValue) { mark[i] = true; stack.Push(i); }
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (stack.Count > 0)
        {
            int p = stack.Pop(), px = p % w, py = p / w;
            if (px > 0) Seed(px - 1, py);
            if (px < w - 1) Seed(px + 1, py);
            if (py > 0) Seed(px, py - 1);
            if (py < h - 1) Seed(px, py + 1);
        }
        return mark;
    }

    /// <summary>Dilation by a disk of radius r (outside the image counts as background).</summary>
    public static bool[] Dilate(bool[] m, int w, int h, int r) => DiskFilter(m, w, h, r, dilate: true);

    /// <summary>Erosion by a disk of radius r (outside the image counts as foreground, so border objects survive).</summary>
    public static bool[] Erode(bool[] m, int w, int h, int r) => DiskFilter(m, w, h, r, dilate: false);

    public static bool[] Open(bool[] m, int w, int h, int r) => Dilate(Erode(m, w, h, r), w, h, r);

    public static bool[] Close(bool[] m, int w, int h, int r) => Erode(Dilate(m, w, h, r), w, h, r);

    // Disk morphology using per-row prefix counts: O(r) per pixel.
    private static bool[] DiskFilter(bool[] m, int w, int h, int r, bool dilate)
    {
        if (r <= 0) return (bool[])m.Clone();
        var prefix = new int[h * (w + 1)];
        for (int y = 0; y < h; y++)
        {
            int b = y * (w + 1);
            for (int x = 0; x < w; x++) prefix[b + x + 1] = prefix[b + x] + (m[y * w + x] ? 1 : 0);
        }
        var half = new int[2 * r + 1];
        for (int dy = -r; dy <= r; dy++) half[dy + r] = (int)Math.Floor(Math.Sqrt(r * r - dy * dy));

        var dst = new bool[m.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                bool result = !dilate;
                for (int dy = -r; dy <= r && result != dilate; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    int x0 = Math.Max(0, x - half[dy + r]), x1 = Math.Min(w - 1, x + half[dy + r]);
                    int b = yy * (w + 1);
                    int count = prefix[b + x1 + 1] - prefix[b + x0];
                    if (dilate && count > 0) result = true;
                    else if (!dilate && count < x1 - x0 + 1) result = false;
                }
                dst[y * w + x] = result;
            }
        });
        return dst;
    }

    /// <summary>Largest 8-connected component. Returns its mask, its size and the number of components.</summary>
    public static (bool[] Mask, int Size, int Components) LargestComponent(bool[] m, int w, int h)
    {
        var label = new int[m.Length];
        var stack = new Stack<int>();
        int current = 0, bestLabel = 0, bestSize = 0;
        for (int start = 0; start < m.Length; start++)
        {
            if (!m[start] || label[start] != 0) continue;
            current++;
            int size = 0;
            label[start] = current;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                size++;
                int px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (m[q] && label[q] == 0) { label[q] = current; stack.Push(q); }
                    }
            }
            if (size > bestSize) { bestSize = size; bestLabel = current; }
        }
        var mask = new bool[m.Length];
        if (bestLabel != 0)
            for (int i = 0; i < m.Length; i++) mask[i] = label[i] == bestLabel;
        return (mask, bestSize, current);
    }
}
