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

    /// <summary>
    /// Extract a 2D axial slice at the given Z index.
    /// Returns pixel data as normalized 0-255 grayscale based on window/level.
    /// </summary>
    public byte[] GetAxialSlice(int z, double windowCenter, double windowWidth)
    {
        var slice = new byte[Width * Height];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            short hu = GetVoxel(x, y, z);
            double normalized = Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0);
            slice[x + y * Width] = (byte)(normalized * 255);
        }
        return slice;
    }

    /// <summary>
    /// Axial slice as BGRA32 with threshold overlay tint.
    /// Voxels in [threshMin, threshMax] get a red overlay.
    /// </summary>
    public byte[] GetAxialSliceBgra(int z, double windowCenter, double windowWidth,
        short threshMin, short threshMax)
    {
        int pixelCount = Width * Height;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            short hu = GetVoxel(x, y, z);
            byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
            int idx = (x + y * Width) * 4;
            
            if (hu >= threshMin && hu <= threshMax)
            {
                // Blend: 60% Light Blue (B=255, G=200, R=50)
                bgra[idx + 0] = (byte)(gray * 0.4 + 255 * 0.6); // B
                bgra[idx + 1] = (byte)(gray * 0.4 + 200 * 0.6); // G
                bgra[idx + 2] = (byte)(gray * 0.4 + 50 * 0.6);  // R
            }
            else
            {
                bgra[idx + 0] = gray; // B
                bgra[idx + 1] = gray; // G
                bgra[idx + 2] = gray; // R
            }
            bgra[idx + 3] = 255; // A
        }
        return bgra;
    }

    /// <summary>
    /// Extract a 2D coronal slice at the given Y index.
    /// Z is reversed so superior (high Z) is at the top of the image.
    /// </summary>
    public byte[] GetCoronalSlice(int y, double windowCenter, double windowWidth)
    {
        var slice = new byte[Width * Depth];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int x = 0; x < Width; x++)
            {
                short hu = GetVoxel(x, y, z);
                double normalized = Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0);
                slice[x + destRow * Width] = (byte)(normalized * 255);
            }
        }
        return slice;
    }

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
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + 255 * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + 200 * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + 50 * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
            }
        }
        return bgra;
    }

    /// <summary>
    /// Extract a 2D sagittal slice at the given X index.
    /// Z is reversed so superior (high Z) is at the top of the image.
    /// </summary>
    public byte[] GetSagittalSlice(int x, double windowCenter, double windowWidth)
    {
        var slice = new byte[Height * Depth];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int y = 0; y < Height; y++)
            {
                short hu = GetVoxel(x, y, z);
                double normalized = Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0);
                slice[y + destRow * Height] = (byte)(normalized * 255);
            }
        }
        return slice;
    }

    /// <summary>
    /// Sagittal slice as BGRA32 with threshold overlay tint.
    /// </summary>
    public byte[] GetSagittalSliceBgra(int x, double windowCenter, double windowWidth,
        short threshMin, short threshMax)
    {
        int pixelCount = Height * Depth;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int y = 0; y < Height; y++)
            {
                short hu = GetVoxel(x, y, z);
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (y + destRow * Height) * 4;

                if (hu >= threshMin && hu <= threshMax)
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + 255 * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + 200 * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + 50 * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
            }
        }
        return bgra;
    }

    /// <summary>
    /// Axial slice as BGRA32, blending live SegmentationVolume label colors.
    /// </summary>
    public byte[] GetAxialSliceWithMaskBgra(int z, double windowCenter, double windowWidth, SegmentationVolume segVol)
    {
        int pixelCount = Width * Height;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int flatIdx = x + y * Width + z * Width * Height;
            short hu = Voxels[flatIdx];
            byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
            int idx = (x + y * Width) * 4;

            byte label = segVol.Labels[flatIdx];
            if (label > 0 && segVol.Segments.TryGetValue(label, out var info))
            {
                bgra[idx + 0] = (byte)(gray * 0.4 + info.ColorB * 0.6);
                bgra[idx + 1] = (byte)(gray * 0.4 + info.ColorG * 0.6);
                bgra[idx + 2] = (byte)(gray * 0.4 + info.ColorR * 0.6);
            }
            else
            {
                bgra[idx + 0] = gray;
                bgra[idx + 1] = gray;
                bgra[idx + 2] = gray;
            }
            bgra[idx + 3] = 255;
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
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + info.ColorB * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + info.ColorG * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + info.ColorR * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
            }
        }
        return bgra;
    }

    /// <summary>
    /// Sagittal slice as BGRA32, blending live SegmentationVolume label colors.
    /// </summary>
    public byte[] GetSagittalSliceWithMaskBgra(int x, double windowCenter, double windowWidth, SegmentationVolume segVol)
    {
        int pixelCount = Height * Depth;
        var bgra = new byte[pixelCount * 4];
        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        for (int z = 0; z < Depth; z++)
        {
            int destRow = Depth - 1 - z;
            for (int y = 0; y < Height; y++)
            {
                int flatIdx = x + y * Width + z * Width * Height;
                short hu = Voxels[flatIdx];
                byte gray = (byte)(Math.Clamp((hu - lower) / (upper - lower), 0.0, 1.0) * 255);
                int idx = (y + destRow * Height) * 4;

                byte label = segVol.Labels[flatIdx];
                if (label > 0 && segVol.Segments.TryGetValue(label, out var info))
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + info.ColorB * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + info.ColorG * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + info.ColorR * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
            }
        }
        return bgra;
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
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + 255 * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + 200 * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + 50 * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
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
                {
                    bgra[idx + 0] = (byte)(gray * 0.4 + info.ColorB * 0.6);
                    bgra[idx + 1] = (byte)(gray * 0.4 + info.ColorG * 0.6);
                    bgra[idx + 2] = (byte)(gray * 0.4 + info.ColorR * 0.6);
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                }
                bgra[idx + 3] = 255;
            }
        });

        return bgra;
    }

    /// <summary>Trilinearly sample the volume at fractional voxel coordinates.</summary>
    public short SamplePhysicalMm(double xMm, double yMm, double zMm) =>
        SampleTrilinear(xMm / Spacing[0], yMm / Spacing[1], zMm / Spacing[2]);

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

    /// <summary>
    /// Generates a 2D Curved Panoramic MPR using Maximum Intensity Projection (MIP) along a spline.
    /// The spline defines the dental arch in the XY plane.
    /// At each point, we look +/- 10mm perpendicular to the curve and capture the maximum HU.
    /// We do this for a vertical column (Z) of 50mm centered around zCenterMm.
    /// If an STL mesh point falls near this exact voxel, we tint the pixel GOLD.
    /// </summary>
    public byte[] GetPanoramicMIPBgra(
        List<(double X, double Y)> archCurveMm,
        double zCenterMm,
        double windowCenter, double windowWidth,
        OrthoPlanner.Core.Geometry.KdTree? alignedStlTree = null)
    {
        // Settings 
        double thicknessMm = 20.0; // 10mm inside, 10mm outside
        double heightMm = 50.0;    // 25mm up, 25mm down
        double sampleRateMm = 0.5; // Compute a pixel every 0.5mm
        double meshThresholdMm = 0.5; // Threshold for highlighting STL mesh

        int imgWidth = (int)(archCurveMm.Count * 0.5); // assuming points are dense
        int imgHeight = (int)(heightMm / sampleRateMm);
        var bgra = new byte[imgWidth * imgHeight * 4];

        // Ensure we actually have width to write
        if (imgWidth == 0 || imgHeight == 0) return bgra;

        double zStartMm = zCenterMm - (heightMm / 2.0);

        double lower = windowCenter - windowWidth / 2.0;
        double upper = windowCenter + windowWidth / 2.0;

        // Loop horizontally (along the curve)
        for (int i = 0; i < imgWidth; i++)
        {
            // Find our index in the smooth curve
            int cIdx = (int)(i * (archCurveMm.Count / (double)imgWidth));
            cIdx = Math.Min(cIdx, archCurveMm.Count - 1);
            
            var p = archCurveMm[cIdx];

            // Estimate normal by looking slightly ahead and behind
            int prevIdx = Math.Max(0, cIdx - 5);
            int nextIdx = Math.Min(archCurveMm.Count - 1, cIdx + 5);
            double dx = archCurveMm[nextIdx].X - archCurveMm[prevIdx].X;
            double dy = archCurveMm[nextIdx].Y - archCurveMm[prevIdx].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) { dx = 1; dy = 0; len = 1; }
            
            // Perpendicular
            double nx = -dy / len;
            double ny = dx / len;

            // Loop vertically (Z axis)
            for (int h = 0; h < imgHeight; h++)
            {
                double zMm = zStartMm + (h * sampleRateMm);
                int zVoxel = (int)(zMm / Spacing[2]);
                
                short maxHu = short.MinValue;
                bool isMeshProfile = false;

                // Loop through the thickness of the slice (MIP Raycast)
                for (double t = -thicknessMm / 2.0; t <= thicknessMm / 2.0; t += 0.5)
                {
                    double xMm = p.X + nx * t;
                    double yMm = p.Y + ny * t;

                    int xVoxel = (int)(xMm / Spacing[0]);
                    int yVoxel = (int)(yMm / Spacing[1]);

                    // Get HU
                    short hu = GetVoxel(xVoxel, yVoxel, zVoxel);
                    if (hu > maxHu) maxHu = hu;

                    // If STL tree is provided, check if we hit the mesh surface right here!
                    if (!isMeshProfile && alignedStlTree != null)
                    {
                        var (_, distSq) = alignedStlTree.FindNearest((float)xMm, (float)yMm, (float)zMm);
                        if (distSq < meshThresholdMm * meshThresholdMm)
                        {
                            isMeshProfile = true; // BAM! The mesh passes right through this exact MPR slice.
                        }
                    }
                }

                // Map to grayscale based on Window/Level
                if (maxHu < -1024) maxHu = -1024; // safety cap out of bounds
                double normalized = Math.Clamp((maxHu - lower) / (upper - lower), 0.0, 1.0);
                byte gray = (byte)(normalized * 255);

                // Write pixel (Z is reversed so superior is top)
                int destY = imgHeight - 1 - h;
                int idx = (i + destY * imgWidth) * 4;

                if (isMeshProfile)
                {
                    // Draw gold profile outline for STL mesh
                    bgra[idx + 0] = 50;  // B
                    bgra[idx + 1] = 200; // G
                    bgra[idx + 2] = 255; // R
                    bgra[idx + 3] = 255; // A
                }
                else
                {
                    bgra[idx + 0] = gray;
                    bgra[idx + 1] = gray;
                    bgra[idx + 2] = gray;
                    bgra[idx + 3] = 255;
                }
            }
        }

        return bgra;
    }
}
