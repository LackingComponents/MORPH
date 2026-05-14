using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.Infrastructure;

/// <summary>
/// Generates Digitally Reconstructed Radiographs (DRR) from a <see cref="VolumeData"/>.
/// Uses ray-sum projection with percentile windowing, gamma correction, and auto-crop
/// to produce clinically realistic cephalometric images.
/// </summary>
public static class DrrGenerator
{
    // ÔöÇÔöÇ Tuning Constants ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private const double Gamma = 0.55;
    private const double LowPercentile = 1.0;
    private const double HighPercentile = 99.0;
    private const int CropMargin = 10;
    private const double CropThreshold = 15.0 / 255.0; // ~0.06 normalized

    /// <summary>
    /// Generates a lateral cephalogram by projecting along the X axis (mediolateral).
    /// Output: width = Height (anterior -> posterior), height = Depth (inferior -> superior, flipped).
    /// </summary>
    public static DrrResult GenerateLateral(VolumeData volume, CancellationToken ct = default)
    {
        int volW = volume.Width;   // X ÔÇö ray direction
        int volH = volume.Height;  // Y ÔÇö image width
        int volD = volume.Depth;   // Z ÔÇö image height

        int imgW = volH;
        int imgH = volD;
        var raw = new double[imgH * imgW];

        for (int z = 0; z < volD; z++)
        {
            ct.ThrowIfCancellationRequested();
            int dstRow = volD - 1 - z; // superior at top
            int sliceBase = z * volW * volH;

            for (int y = 0; y < volH; y++)
            {
                int rowBase = sliceBase + y * volW;
                double sum = 0;
                for (int x = 0; x < volW; x++)
                {
                    short hu = volume.Voxels[rowBase + x];
                    sum += HUToBrightness(hu);
                }
                raw[dstRow * imgW + (imgW - 1 - y)] = sum;
            }
        }

        return PostProcess(raw, imgW, imgH, volume.Spacing[1], volume.Spacing[2]);
    }

    /// <summary>
    /// Generates a PA cephalogram by projecting along the Y axis.
    /// Output: width = Width (left -> right), height = Depth (inferior -> superior, flipped).
    /// </summary>
    public static DrrResult GeneratePA(VolumeData volume, CancellationToken ct = default)
    {
        int volW = volume.Width;
        int volH = volume.Height;  // Y ÔÇö ray direction
        int volD = volume.Depth;

        int imgW = volW;
        int imgH = volD;
        var raw = new double[imgH * imgW];

        for (int z = 0; z < volD; z++)
        {
            ct.ThrowIfCancellationRequested();
            int dstRow = volD - 1 - z;
            int sliceBase = z * volW * volH;

            for (int x = 0; x < volW; x++)
            {
                double sum = 0;
                for (int y = 0; y < volH; y++)
                {
                    short hu = volume.Voxels[sliceBase + y * volW + x];
                    sum += HUToBrightness(hu);
                }
                raw[dstRow * imgW + x] = sum;
            }
        }

        return PostProcess(raw, imgW, imgH, volume.Spacing[0], volume.Spacing[2]);
    }

    // ÔöÇÔöÇ Post-Processing Pipeline ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private static DrrResult PostProcess(double[] raw, int w, int h, double spacingX, double spacingY)
    {
        // 1. Percentile-based normalization to [0, 1]
        var pixels = PercentileNormalize(raw, w * h);

        // 2. Gamma correction (brighten midtones for soft tissue visibility)
        ApplyGamma(pixels);

        // 3. Background mask ÔÇö flood-fill exterior air from corners ÔåÆ black
        MaskExteriorBackground(pixels, w, h);

        // 4. Auto-crop to remove excess black border
        var cropped = AutoCrop(pixels, w, h, out int cropX, out int cropY,
                               out int cropW, out int cropH);

        return new DrrResult(cropped, cropW, cropH, spacingX, spacingY, cropX, cropY);
    }

    // ÔöÇÔöÇ Percentile Normalization ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private static float[] PercentileNormalize(double[] raw, int len)
    {
        // Histogram-based percentile finding (O(n), no sorting needed)
        const int BinCount = 2000;
        double dataMin = double.MaxValue, dataMax = double.MinValue;

        for (int i = 0; i < len; i++)
        {
            double v = raw[i];
            if (v < dataMin) dataMin = v;
            if (v > dataMax) dataMax = v;
        }

        if (dataMax <= dataMin)
            return new float[len];

        double binScale = (BinCount - 1) / (dataMax - dataMin);
        var histogram = new int[BinCount];
        for (int i = 0; i < len; i++)
        {
            int bin = Math.Clamp((int)((raw[i] - dataMin) * binScale), 0, BinCount - 1);
            histogram[bin]++;
        }

        int lowTarget = (int)(len * LowPercentile / 100.0);
        int highTarget = (int)(len * HighPercentile / 100.0);
        double pLow = dataMin, pHigh = dataMax;

        int cumulative = 0;
        for (int b = 0; b < BinCount; b++)
        {
            cumulative += histogram[b];
            if (pLow == dataMin && cumulative >= lowTarget)
                pLow = dataMin + b / binScale;
            if (cumulative >= highTarget)
            {
                pHigh = dataMin + b / binScale;
                break;
            }
        }

        double range = pHigh - pLow;
        double invRange = range > 0 ? 1.0 / range : 0;
        var pixels = new float[len];
        for (int i = 0; i < len; i++)
            pixels[i] = (float)Math.Clamp((raw[i] - pLow) * invRange, 0.0, 1.0);

        return pixels;
    }

    // ÔöÇÔöÇ Gamma Correction ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private static void ApplyGamma(float[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (float)Math.Pow(pixels[i], Gamma);
    }

    // ÔöÇÔöÇ Auto-Crop ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private static float[] AutoCrop(float[] pixels, int w, int h,
        out int offsetX, out int offsetY, out int cropW, out int cropH)
    {
        int top = h, bottom = 0, left = w, right = 0;

        for (int row = 0; row < h; row++)
        {
            int rowOff = row * w;
            for (int col = 0; col < w; col++)
            {
                if (pixels[rowOff + col] > CropThreshold)
                {
                    if (row < top) top = row;
                    if (row > bottom) bottom = row;
                    if (col < left) left = col;
                    if (col > right) right = col;
                }
            }
        }

        if (top > bottom || left > right)
        {
            offsetX = 0;
            offsetY = 0;
            cropW = w;
            cropH = h;
            return pixels;
        }

        top = Math.Max(0, top - CropMargin);
        bottom = Math.Min(h - 1, bottom + CropMargin);
        left = Math.Max(0, left - CropMargin);
        right = Math.Min(w - 1, right + CropMargin);

        offsetX = left;
        offsetY = top;
        cropW = right - left + 1;
        cropH = bottom - top + 1;

        var cropped = new float[cropW * cropH];
        for (int row = 0; row < cropH; row++)
            Array.Copy(pixels, (top + row) * w + left, cropped, row * cropW, cropW);

        return cropped;
    }

    // ÔöÇÔöÇ Piecewise HU ÔåÆ Brightness Transfer Function ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private static float HUToBrightness(float hu)
    {
        float brightness;
        if (hu < -300f)  brightness = 0f;                                                    // air ÔåÆ pure black
        else if (hu < 0f)     brightness = Lerp(0f,    0.15f, (hu + 300f) / 300f);          // soft tissue
        else if (hu < 300f)   brightness = Lerp(0.15f, 0.48f, hu / 300f);                  // muscle/skin
        else if (hu < 700f)   brightness = Lerp(0.48f, 0.82f, (hu - 300f) / 400f);         // trabecular bone
        else                  brightness = 0.92f;                                            // cortical bone: bright but NOT 1.0 ÔÇö keeps edge detail

        // Mild gamma to slightly darken midtones (keeps soft tissue separated from bone)
        return MathF.Pow(brightness, 1.15f);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    // ÔöÇÔöÇ Exterior Background Mask (flood-fill from corners) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    /// <summary>
    /// Flood-fills from the 4 image corners to find exterior air pixels (brightness == 0)
    /// and ensures they stay black. Interior dark areas (sinuses, nasal cavity) are preserved.
    /// </summary>
    private static void MaskExteriorBackground(float[] pixels, int w, int h)
    {
        var exterior = new bool[w * h];
        var queue = new Queue<int>();

        // Seed from all 4 corners
        void TrySeed(int x, int y)
        {
            int idx = y * w + x;
            if (pixels[idx] == 0f && !exterior[idx])
            {
                exterior[idx] = true;
                queue.Enqueue(idx);
            }
        }

        TrySeed(0, 0);
        TrySeed(w - 1, 0);
        TrySeed(0, h - 1);
        TrySeed(w - 1, h - 1);

        // BFS flood-fill through connected zero-brightness pixels
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w;
            int y = idx / w;

            void TryNeighbor(int nx, int ny)
            {
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
                int nIdx = ny * w + nx;
                if (!exterior[nIdx] && pixels[nIdx] == 0f)
                {
                    exterior[nIdx] = true;
                    queue.Enqueue(nIdx);
                }
            }

            TryNeighbor(x - 1, y);
            TryNeighbor(x + 1, y);
            TryNeighbor(x, y - 1);
            TryNeighbor(x, y + 1);
        }

        // Zero out only exterior pixels (interior dark areas are untouched)
        for (int i = 0; i < pixels.Length; i++)
        {
            if (exterior[i])
                pixels[i] = 0f;
        }
    }
}

/// <summary>
/// Result of DRR generation: normalised [0,1] float pixel buffer + dimensions + spacing.
/// </summary>
public sealed class DrrResult
{
    public float[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public double SpacingX { get; }
    public double SpacingY { get; }
    public int CropOffsetX { get; }
    public int CropOffsetY { get; }

    public DrrResult(float[] pixels, int width, int height, double spacingX, double spacingY,
                     int cropOffsetX = 0, int cropOffsetY = 0)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        SpacingX = spacingX;
        SpacingY = spacingY;
        CropOffsetX = cropOffsetX;
        CropOffsetY = cropOffsetY;
    }
}
