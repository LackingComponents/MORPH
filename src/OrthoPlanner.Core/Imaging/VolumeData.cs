using System;
using System.Collections.Generic;
using OrthoPlanner.Core.Segmentation;

namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Represents a 3D volume constructed from DICOM slices.
/// Stores Hounsfield Unit (HU) values in a flat array for performance.
/// </summary>
public class VolumeData
{
    /// <summary>Width of the volume in voxels (columns).</summary>
    public int Width { get; }

    /// <summary>Height of the volume in voxels (rows).</summary>
    public int Height { get; }

    /// <summary>Depth of the volume in voxels (number of slices).</summary>
    public int Depth { get; }

    /// <summary>Voxel spacing in mm: [X, Y, Z].</summary>
    public double[] Spacing { get; }

    /// <summary>
    /// Flat array of HU values stored in [x + y*Width + z*Width*Height] order.
    /// Using short (Int16) since HU range is typically -1024 to +3071.
    /// </summary>
    public short[] Voxels { get; }

    /// <summary>Minimum HU value in the volume.</summary>
    public short MinValue { get; private set; }

    /// <summary>Maximum HU value in the volume.</summary>
    public short MaxValue { get; private set; }

    /// <summary>Patient name from DICOM metadata.</summary>
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Patient date of birth from DICOM metadata.</summary>
    public string PatientDOB { get; set; } = string.Empty;

    /// <summary>Study date from DICOM metadata.</summary>
    public string StudyDate { get; set; } = string.Empty;

    /// <summary>Series description from DICOM metadata.</summary>
    public string SeriesDescription { get; set; } = string.Empty;

    public VolumeData(int width, int height, int depth, double[] spacing)
    {
        Width = width;
        Height = height;
        Depth = depth;
        Spacing = spacing;
        Voxels = new short[width * height * depth];
    }

    /// <summary>
    /// Get the HU value at a specific voxel coordinate.
    /// </summary>
    public short GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
            return short.MinValue;
        return Voxels[x + y * Width + z * Width * Height];
    }

    /// <summary>
    /// Set the HU value at a specific voxel coordinate.
    /// </summary>
    public void SetVoxel(int x, int y, int z, short value)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
            return;
        Voxels[x + y * Width + z * Width * Height] = value;
    }

    /// <summary>
    /// Compute min/max values across the entire volume. Call after loading all voxels.
    /// </summary>
    public void ComputeMinMax()
    {
        if (Voxels.Length == 0) return;
        short min = short.MaxValue;
        short max = short.MinValue;
        for (int i = 0; i < Voxels.Length; i++)
        {
            if (Voxels[i] < min) min = Voxels[i];
            if (Voxels[i] > max) max = Voxels[i];
        }
        MinValue = min;
        MaxValue = max;
        ComputeHistogram();
    }

    // ponytail: GetAxialSlice, GetAxialSliceBgra, GetAxialSliceWithMaskBgra,
    //           GetSagittalSlice, GetSagittalSliceBgra, GetSagittalSliceWithMaskBgra,
    //           GetCoronalSlice (grayscale) — all deleted, zero callers.
    //           DicomViewModel uses oblique slices; SeedSplitWindow uses GetCoronalSliceBgra/WithMaskBgra.

    /// <summary>
    /// Coronal slice as BGRA32 with threshold overlay tint.
    /// </summary>
    public byte[] GetCoronalSliceBgra(int y, double windowCenter, double windowWidth,
        short threshMin, short threshMax)
    {
        int pixelCount = Width * Depth;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int x = 0; x < Width; x++)
            {
                short hu = GetVoxel(x, y, z);
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (x + destRow * Width) * 4;

                if (hu >= threshMin && hu <= threshMax)
                    WriteOverlayBgra(bgra, idx, gray, 255, 200, 50);
                else
                    WriteGrayBgra(bgra, idx, gray);
            }
        }
        return bgra;
    }

    /// <summary>
    /// Coronal slice as BGRA32, blending live SegmentationVolume label colors.
    /// </summary>
    public byte[] GetCoronalSliceWithMaskBgra(int y, double windowCenter, double windowWidth, SegmentationVolume segVol)
    {
        int pixelCount = Width * Depth;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int x = 0; x < Width; x++)
            {
                int flatIdx = x + y * Width + z * Width * Height;
                short hu = Voxels[flatIdx];
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (x + destRow * Width) * 4;

                byte label = segVol.Labels[flatIdx];
                if (label > 0 && segVol.Segments.TryGetValue(label, out var info))
                    WriteOverlayBgra(bgra, idx, gray, info.ColorB, info.ColorG, info.ColorR);
                else
                    WriteGrayBgra(bgra, idx, gray);
            }
        }
        return bgra;
    }

    // ── BGRA pixel helpers (ponytail: factored from 9× copy-pasted blocks) ──

    /// <summary>Write a gray BGRA pixel (R=G=B=gray, A=255).</summary>
    private static void WriteGrayBgra(byte[] bgra, int idx, byte gray)
    {
        bgra[idx] = gray; bgra[idx + 1] = gray; bgra[idx + 2] = gray; bgra[idx + 3] = 255;
    }

    /// <summary>Write a 60/40 overlay-tinted BGRA pixel (40% gray + 60% color, A=255).</summary>
    private static void WriteOverlayBgra(byte[] bgra, int idx, byte gray, byte cb, byte cg, byte cr)
    {
        bgra[idx]     = (byte)(gray * 0.4 + cb * 0.6);
        bgra[idx + 1] = (byte)(gray * 0.4 + cg * 0.6);
        bgra[idx + 2] = (byte)(gray * 0.4 + cr * 0.6);
        bgra[idx + 3] = 255;
    }

    // ━━━ Oblique Slice Sampling (Visual-Only NHP) ━━━

    /// <summary>
    /// Sample an oblique plane through the volume using trilinear interpolation.
    /// Returns grayscale pixel data (0-255) based on window/level.
    /// </summary>
    public byte[] GetObliqueSliceGrayscale(
        int outWidth, int outHeight,
        double originX, double originY, double originZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double windowCenter, double windowWidth)
    {
        var slice = new byte[outWidth * outHeight];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        System.Threading.Tasks.Parallel.For(0, outHeight, row =>
        {
            for (int col = 0; col < outWidth; col++)
            {
                double x = originX + col * uX + row * vX;
                double y = originY + col * uY + row * vY;
                double z = originZ + col * uZ + row * vZ;

                double ix = x / Spacing[0];
                double iy = y / Spacing[1];
                double iz = z / Spacing[2];

                short hu = SampleTrilinear(ix, iy, iz);
                double normalized = Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0);
                slice[col + row * outWidth] = (byte)(normalized * 255);
            }
        });

        return slice;
    }

    /// <summary>
    /// Sample an oblique plane as BGRA32 with threshold overlay tint.
    /// </summary>
    public byte[] GetObliqueSliceBgra(
        int outWidth, int outHeight,
        double originX, double originY, double originZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double windowCenter, double windowWidth,
        short threshMin, short threshMax)
    {
        var bgra = new byte[outWidth * outHeight * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        System.Threading.Tasks.Parallel.For(0, outHeight, row =>
        {
            for (int col = 0; col < outWidth; col++)
            {
                double x = originX + col * uX + row * vX;
                double y = originY + col * uY + row * vY;
                double z = originZ + col * uZ + row * vZ;

                double ix = x / Spacing[0];
                double iy = y / Spacing[1];
                double iz = z / Spacing[2];

                short hu = SampleTrilinear(ix, iy, iz);
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (col + row * outWidth) * 4;

                if (hu >= threshMin && hu <= threshMax)
                    WriteOverlayBgra(bgra, idx, gray, 255, 200, 50);
                else
                    WriteGrayBgra(bgra, idx, gray);
            }
        });

        return bgra;
    }

    /// <summary>
    /// Sample an oblique plane as BGRA32, blending live SegmentationVolume label colors.
    /// </summary>
    public byte[] GetObliqueSliceWithMaskBgra(
        int outWidth, int outHeight,
        double originX, double originY, double originZ,
        double uX, double uY, double uZ,
        double vX, double vY, double vZ,
        double windowCenter, double windowWidth,
        SegmentationVolume segVol)
    {
        var bgra = new byte[outWidth * outHeight * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        System.Threading.Tasks.Parallel.For(0, outHeight, row =>
        {
            for (int col = 0; col < outWidth; col++)
            {
                double x = originX + col * uX + row * vX;
                double y = originY + col * uY + row * vY;
                double z = originZ + col * uZ + row * vZ;

                double ix = x / Spacing[0];
                double iy = y / Spacing[1];
                double iz = z / Spacing[2];

                short hu = SampleTrilinear(ix, iy, iz);
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (col + row * outWidth) * 4;

                // Sample segmentation label via nearest-neighbor (integer coords)
                int lx = (int)Math.Round(ix);
                int ly = (int)Math.Round(iy);
                int lz = (int)Math.Round(iz);
                byte label = 0;
                if (lx >= 0 && lx < Width && ly >= 0 && ly < Height && lz >= 0 && lz < Depth)
                    label = segVol.Labels[lx + ly * Width + lz * Width * Height];

                if (label > 0 && segVol.Segments.TryGetValue(label, out var info))
                    WriteOverlayBgra(bgra, idx, gray, info.ColorB, info.ColorG, info.ColorR);
                else
                    WriteGrayBgra(bgra, idx, gray);
            }
        });

        return bgra;
    }

    /// <summary>Trilinearly sample the volume at fractional voxel coordinates.</summary>
    private short SampleTrilinear(double ix, double iy, double iz)
    {
        int x0 = (int)Math.Floor(ix);
        int y0 = (int)Math.Floor(iy);
        int z0 = (int)Math.Floor(iz);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        double fx = ix - x0;
        double fy = iy - y0;
        double fz = iz - z0;

        // Trilinear interpolation
        double v000 = GetVoxelSafe(x0, y0, z0);
        double v100 = GetVoxelSafe(x1, y0, z0);
        double v010 = GetVoxelSafe(x0, y1, z0);
        double v110 = GetVoxelSafe(x1, y1, z0);
        double v001 = GetVoxelSafe(x0, y0, z1);
        double v101 = GetVoxelSafe(x1, y0, z1);
        double v011 = GetVoxelSafe(x0, y1, z1);
        double v111 = GetVoxelSafe(x1, y1, z1);

        double v00 = v000 * (1 - fx) + v100 * fx;
        double v10 = v010 * (1 - fx) + v110 * fx;
        double v01 = v001 * (1 - fx) + v101 * fx;
        double v11 = v011 * (1 - fx) + v111 * fx;

        double v0 = v00 * (1 - fy) + v10 * fy;
        double v1 = v01 * (1 - fy) + v11 * fy;

        return (short)(v0 * (1 - fz) + v1 * fz);
    }

    private short GetVoxelSafe(int x, int y, int z)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
            return -1000; // Air value for out-of-bounds
        return Voxels[x + y * Width + z * Width * Height];
    }

    /// <summary>
    /// HU histogram with 512 bins from MinValue to MaxValue.
    /// </summary>
    public int[] Histogram { get; private set; } = [];
    public int HistogramMax { get; private set; }

    private void ComputeHistogram()
    {
        const int bins = 512;
        Histogram = new int[bins];
        double range = MaxValue - MinValue;
        if (range <= 0) return;

        double scale = (bins - 1) / range;
        for (int i = 0; i < Voxels.Length; i++)
        {
            int bin = (int)((Voxels[i] - MinValue) * scale);
            bin = Math.Clamp(bin, 0, bins - 1);
            Histogram[bin]++;
        }

        // Find max (skip the first few bins which are often air/background spikes)
        HistogramMax = 0;
        for (int i = 10; i < bins; i++)
            if (Histogram[i] > HistogramMax) HistogramMax = Histogram[i];
    }

    /// <summary>Get the HU value for a histogram bin index.</summary>
    public double HistogramBinToHU(int bin)
    {
        double range = MaxValue - MinValue;
        return MinValue + (bin * range / (Histogram.Length - 1));
    }
    /// <summary>
    /// Returns the physical dimensions of the volume in mm.
    /// </summary>
    public (double Width, double Height, double Depth) GetPhysicalDimensions()
    {
        return (Width * Spacing[0], Height * Spacing[1], Depth * Spacing[2]);
    }

    // ponytail: GetPanoramicMIPBgra — deleted, zero callers
}
