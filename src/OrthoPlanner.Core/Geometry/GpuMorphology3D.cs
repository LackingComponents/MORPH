using System;
using ILGPU;
using ILGPU.Runtime;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// GPU-accelerated 3D binary morphological operations using ILGPU.
/// Supports dilation, erosion, closing (dilate→erode), and opening (erode→dilate).
/// Falls back to CPU accelerator if no GPU is available.
/// </summary>
public sealed class GpuMorphology3D : IDisposable
{
    private readonly Context _ctx;
    private readonly Accelerator _acc;
    private readonly Action<Index1D, ArrayView1D<byte, Stride1D.Dense>, ArrayView1D<byte, Stride1D.Dense>, int, int, int, int> _dilateKernel;
    private readonly Action<Index1D, ArrayView1D<byte, Stride1D.Dense>, ArrayView1D<byte, Stride1D.Dense>, int, int, int, int> _erodeKernel;

    public GpuMorphology3D()
    {
        _ctx = Context.CreateDefault();
        // Prefer GPU, fall back to CPU
        _acc = _ctx.GetPreferredDevice(preferCPU: false).CreateAccelerator(_ctx);
        System.Diagnostics.Debug.WriteLine($"[GpuMorphology3D] Using accelerator: {_acc.Name} ({_acc.AcceleratorType})");

        _dilateKernel = _acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<byte, Stride1D.Dense>, ArrayView1D<byte, Stride1D.Dense>, int, int, int, int>(DilateKernel);
        _erodeKernel  = _acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<byte, Stride1D.Dense>, ArrayView1D<byte, Stride1D.Dense>, int, int, int, int>(ErodeKernel);
    }

    /// <summary>3D binary dilation: expand '1' voxels by radius r (box structuring element).</summary>
    public byte[] Dilate(byte[] input, int nx, int ny, int nz, int radius)
    {
        var output = new byte[input.Length];
        RunKernel(_dilateKernel, input, output, nx, ny, nz, radius);
        return output;
    }

    /// <summary>3D binary erosion: shrink '1' regions by radius r (box structuring element).</summary>
    public byte[] Erode(byte[] input, int nx, int ny, int nz, int radius)
    {
        var output = new byte[input.Length];
        RunKernel(_erodeKernel, input, output, nx, ny, nz, radius);
        return output;
    }

    /// <summary>Morphological closing: dilate then erode. Bridges small gaps.</summary>
    public byte[] Close(byte[] input, int nx, int ny, int nz, int radius)
    {
        var dilated = Dilate(input, nx, ny, nz, radius);
        return Erode(dilated, nx, ny, nz, radius);
    }

    /// <summary>Morphological opening: erode then dilate. Removes thin protrusions.</summary>
    public byte[] Open(byte[] input, int nx, int ny, int nz, int radius)
    {
        var eroded = Erode(input, nx, ny, nz, radius);
        return Dilate(eroded, nx, ny, nz, radius);
    }

    private void RunKernel(
        Action<Index1D, ArrayView1D<byte, Stride1D.Dense>, ArrayView1D<byte, Stride1D.Dense>, int, int, int, int> kernel,
        byte[] input, byte[] output, int nx, int ny, int nz, int radius)
    {
        int total = nx * ny * nz;
        using var inBuf  = _acc.Allocate1D<byte>(total);
        using var outBuf = _acc.Allocate1D<byte>(total);
        inBuf.CopyFromCPU(input);
        kernel(total, inBuf.View, outBuf.View, nx, ny, nz, radius);
        _acc.Synchronize();
        outBuf.CopyToCPU(output);
    }

    // ── GPU Kernels ──────────────────────────────────────────────────────────

    /// <summary>Dilation kernel: output=1 if ANY voxel in [x±r, y±r, z±r] is 1.</summary>
    static void DilateKernel(
        Index1D index,
        ArrayView1D<byte, Stride1D.Dense> input,
        ArrayView1D<byte, Stride1D.Dense> output,
        int nx, int ny, int nz, int radius)
    {
        int idx = index.X;
        int z = idx / (nx * ny);
        int rem = idx % (nx * ny);
        int y = rem / nx;
        int x = rem % nx;

        byte result = 0;
        int zLo = z - radius; if (zLo < 0) zLo = 0;
        int zHi = z + radius; if (zHi >= nz) zHi = nz - 1;
        int yLo = y - radius; if (yLo < 0) yLo = 0;
        int yHi = y + radius; if (yHi >= ny) yHi = ny - 1;
        int xLo = x - radius; if (xLo < 0) xLo = 0;
        int xHi = x + radius; if (xHi >= nx) xHi = nx - 1;

        for (int kz = zLo; kz <= zHi && result == 0; kz++)
            for (int ky = yLo; ky <= yHi && result == 0; ky++)
                for (int kx = xLo; kx <= xHi && result == 0; kx++)
                    if (input[kz * nx * ny + ky * nx + kx] != 0) result = 1;

        output[idx] = result;
    }

    /// <summary>Erosion kernel: output=1 only if ALL voxels in [x±r, y±r, z±r] are 1.</summary>
    static void ErodeKernel(
        Index1D index,
        ArrayView1D<byte, Stride1D.Dense> input,
        ArrayView1D<byte, Stride1D.Dense> output,
        int nx, int ny, int nz, int radius)
    {
        int idx = index.X;
        int z = idx / (nx * ny);
        int rem = idx % (nx * ny);
        int y = rem / nx;
        int x = rem % nx;

        byte result = 1;
        int zLo = z - radius; if (zLo < 0) zLo = 0;
        int zHi = z + radius; if (zHi >= nz) zHi = nz - 1;
        int yLo = y - radius; if (yLo < 0) yLo = 0;
        int yHi = y + radius; if (yHi >= ny) yHi = ny - 1;
        int xLo = x - radius; if (xLo < 0) xLo = 0;
        int xHi = x + radius; if (xHi >= nx) xHi = nx - 1;

        for (int kz = zLo; kz <= zHi && result == 1; kz++)
            for (int ky = yLo; ky <= yHi && result == 1; ky++)
                for (int kx = xLo; kx <= xHi && result == 1; kx++)
                    if (input[kz * nx * ny + ky * nx + kx] == 0) result = 0;

        output[idx] = result;
    }

    public void Dispose()
    {
        _acc.Dispose();
        _ctx.Dispose();
    }
}
