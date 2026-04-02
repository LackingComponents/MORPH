using System.Collections.Concurrent;
using OrthoPlanner.Core.Imaging;
using ILGPU;
using ILGPU.Runtime;

namespace OrthoPlanner.Core.Segmentation;

/// <summary>
/// Segmentation algorithms: threshold, region growing, connected components.
///
/// OPTIMIZATIONS vs original:
///   • ThresholdSegment      → GPU kernel (ILGPU), fallback Parallel.For
///   • MorphologicalClosing  → GPU dilation+erosion kernels, fallback Parallel.For
///   • SmoothLabelMask       → GPU majority-vote kernel, fallback Parallel.For
///   • ExtractSegmentMesh    → Parallel.For mask build + optimized MarchingCubes
///   • ResliceVolume         → Parallel.For (GPU removed: too many params for ILGPU inference)
///   • All BFS operations    → unchanged (inherently sequential)
/// </summary>
public static class SegmentationEngine
{
    // ─────────────────────────────────────────────────────────────────────────
    // THRESHOLD SEGMENTATION
    // ─────────────────────────────────────────────────────────────────────────

    public static void ThresholdSegment(
        VolumeData volume, SegmentationVolume segVol,
        byte label, short minHU, short maxHU,
        bool enhanceThinBone = false,
        Action<double>? progress = null)
    {
        if (!enhanceThinBone)
        {
            try { ThresholdGpu(volume, segVol, label, minHU, maxHU, progress); return; }
            catch { }
        }
        ThresholdCpu(volume, segVol, label, minHU, maxHU, enhanceThinBone, progress);
    }

    private static void ThresholdGpu(
        VolumeData volume, SegmentationVolume segVol,
        byte label, short minHU, short maxHU,
        Action<double>? progress)
    {
        var gpu = GpuContext.Instance;
        int n = volume.Voxels.Length;
        using var gpuVoxels = gpu.Accelerator.Allocate1D<short>(volume.Voxels);
        using var gpuLabels = gpu.Accelerator.Allocate1D<byte>(segVol.Labels);

        // ILGPU 1.5.x: cast to Action delegate for type inference
        var kernel = gpu.Accelerator.LoadAutoGroupedStreamKernel(
            (Action<Index1D, ArrayView<short>, ArrayView<byte>, short, short, byte>)
            GpuKernels.ThresholdKernel);

        kernel(n, gpuVoxels.View, gpuLabels.View, minHU, maxHU, label);
        gpu.Accelerator.Synchronize();
        gpuLabels.CopyToCPU(segVol.Labels);
        progress?.Invoke(1.0);
    }

    private static void ThresholdCpu(
        VolumeData volume, SegmentationVolume segVol,
        byte label, short minHU, short maxHU,
        bool enhanceThinBone, Action<double>? progress)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        short airThreshold = -400;
        bool[]? externalAirMask = null;
        if (enhanceThinBone)
        {
            progress?.Invoke(0.05);
            externalAirMask = ComputeExternalAirMask(volume, airThreshold);
        }

        Parallel.For(0, d, z =>
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w + z * w * h;
                short val = volume.Voxels[i];
                if (val >= minHU && val <= maxHU)
                {
                    segVol.Labels[i] = label;
                }
                else if (enhanceThinBone && val >= minHU - 200 && val < minHU)
                {
                    bool touchesInternalAir = false;
                    bool touchesExternalAir = false;
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        int nx2 = x+dx, ny2 = y+dy, nz2 = z+dz;
                        if (nx2>=0 && nx2<w && ny2>=0 && ny2<h && nz2>=0 && nz2<d)
                        {
                            int nIdx = nx2 + ny2*w + nz2*w*h;
                            if (volume.Voxels[nIdx] <= airThreshold)
                            {
                                if (externalAirMask != null && externalAirMask[nIdx])
                                    touchesExternalAir = true;
                                else
                                    touchesInternalAir = true;
                            }
                        }
                    }
                    if (touchesInternalAir && !touchesExternalAir)
                        segVol.Labels[i] = label;
                }
            }
            if (z % 20 == 0) progress?.Invoke((double)z / d);
        });
        progress?.Invoke(1.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EXTERNAL AIR MASK
    // ─────────────────────────────────────────────────────────────────────────

    private static bool[] ComputeExternalAirMask(VolumeData volume, short maxAirHU)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int totalVoxels = w * h * d;
        var globalVisited = new bool[totalVoxels];
        List<int> largestComponent = new();
        int maxSize = 0;
        int[][] n6 = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var queue = new Queue<(int x, int y, int z)>();

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y*w + z*w*h;
            if (!globalVisited[idx] && volume.Voxels[idx] <= maxAirHU)
            {
                var comp = new List<int>();
                globalVisited[idx] = true;
                queue.Enqueue((x, y, z));
                while (queue.Count > 0)
                {
                    var (cx, cy, cz) = queue.Dequeue();
                    comp.Add(cx + cy*w + cz*w*h);
                    foreach (var n in n6)
                    {
                        int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                        if (nx>=0&&nx<w&&ny>=0&&ny<h&&nz>=0&&nz<d)
                        {
                            int ni = nx+ny*w+nz*w*h;
                            if (!globalVisited[ni] && volume.Voxels[ni] <= maxAirHU)
                            { globalVisited[ni] = true; queue.Enqueue((nx,ny,nz)); }
                        }
                    }
                }
                if (comp.Count > maxSize) { maxSize = comp.Count; largestComponent = comp; }
            }
        }
        var mask = new bool[totalVoxels];
        foreach (int i in largestComponent) mask[i] = true;
        return mask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGION GROWING
    // ─────────────────────────────────────────────────────────────────────────

    public static int RegionGrow(
        VolumeData volume, SegmentationVolume segVol,
        int seedX, int seedY, int seedZ,
        byte label, short minHU, short maxHU,
        Action<double>? progress = null)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        int seedIdx = seedX + seedY*w + seedZ*w*h;
        if (segVol.Labels[seedIdx] == label) return 0;
        short seedVal = volume.Voxels[seedIdx];
        if (seedVal < minHU || seedVal > maxHU) return 0;

        var queue = new Queue<(int x, int y, int z)>();
        queue.Enqueue((seedX, seedY, seedZ));
        segVol.Labels[seedIdx] = label;
        int count = 1;
        while (queue.Count > 0)
        {
            var (cx, cy, cz) = queue.Dequeue();
            foreach (var n in neighbors)
            {
                int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                int ni = nx+ny*w+nz*w*h;
                if (segVol.Labels[ni] == label) continue;
                short val = volume.Voxels[ni];
                if (val < minHU || val > maxHU) continue;
                segVol.Labels[ni] = label;
                count++;
                queue.Enqueue((nx, ny, nz));
            }
            if (count % 50000 == 0) progress?.Invoke((double)count / (w*h*d));
        }
        progress?.Invoke(1.0);
        return count;
    }

    public static void CompetitiveRegionGrow(
        VolumeData volume, SegmentationVolume segVol,
        List<(int x, int y, int z, byte label)> seeds,
        short minHU, short maxHU,
        Action<double>? progress = null)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var queue = new Queue<(int x, int y, int z, byte label)>();
        foreach (var seed in seeds)
        {
            int si = seed.x + seed.y*w + seed.z*w*h;
            if (volume.Voxels[si] >= minHU && volume.Voxels[si] <= maxHU)
            { segVol.Labels[si] = seed.label; queue.Enqueue(seed); }
        }
        int processed = 0;
        while (queue.Count > 0)
        {
            var (cx, cy, cz, lbl) = queue.Dequeue();
            processed++;
            foreach (var n in neighbors)
            {
                int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                int ni = nx+ny*w+nz*w*h;
                if (segVol.Labels[ni] != 0) continue;
                short val = volume.Voxels[ni];
                if (val < minHU || val > maxHU) continue;
                segVol.Labels[ni] = lbl;
                queue.Enqueue((nx, ny, nz, lbl));
            }
            if (processed % 50000 == 0) progress?.Invoke((double)processed / (w*h*d));
        }
        progress?.Invoke(1.0);
    }

    public static int RegionGrowLabel(
        VolumeData volume, SegmentationVolume segVol,
        int seedX, int seedY, int seedZ,
        byte sourceLabel, byte newLabel,
        Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        int seedIdx = seedX + seedY*w + seedZ*w*h;
        if (segVol.Labels[seedIdx] != sourceLabel) return 0;

        var queue = new Queue<(int x, int y, int z)>();
        queue.Enqueue((seedX, seedY, seedZ));
        segVol.Labels[seedIdx] = newLabel;
        int count = 1;
        while (queue.Count > 0)
        {
            var (cx, cy, cz) = queue.Dequeue();
            foreach (var n in neighbors)
            {
                int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                int ni = nx+ny*w+nz*w*h;
                if (segVol.Labels[ni] != sourceLabel) continue;
                segVol.Labels[ni] = newLabel;
                count++;
                queue.Enqueue((nx, ny, nz));
            }
            if (count % 50000 == 0) progress?.Invoke((double)count / (w*h*d));
        }
        progress?.Invoke(1.0);
        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTED COMPONENTS
    // ─────────────────────────────────────────────────────────────────────────

    public static List<(byte newLabel, int voxelCount)> SplitConnectedComponents(
        SegmentationVolume segVol, byte sourceLabel, List<byte> newLabels,
        int w, int h, int d,
        Action<double>? progress = null)
    {
        int total = w * h * d;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var visited = new bool[total];
        var components = new List<(int seedIdx, List<int> voxels)>();

        for (int i = 0; i < total; i++)
        {
            if (visited[i] || segVol.Labels[i] != sourceLabel) continue;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i); visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                comp.Add(cur);
                int cz=cur/(w*h), rem=cur%(w*h), cy=rem/w, cx=rem%w;
                foreach (var n in neighbors)
                {
                    int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                    if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                    int ni = nx+ny*w+nz*w*h;
                    if (!visited[ni] && segVol.Labels[ni] == sourceLabel)
                    { visited[ni] = true; queue.Enqueue(ni); }
                }
            }
            components.Add((i, comp));
        }
        components.Sort((a, b) => b.voxels.Count.CompareTo(a.voxels.Count));
        var result = new List<(byte, int)>();
        for (int ci = 0; ci < Math.Min(components.Count, newLabels.Count); ci++)
        {
            byte nl = newLabels[ci];
            foreach (int idx in components[ci].voxels)
                segVol.Labels[idx] = nl;
            result.Add((nl, components[ci].voxels.Count));
        }
        progress?.Invoke(1.0);
        return result;
    }

    public static void RemoveSmallComponents(
        SegmentationVolume segVol, byte targetLabel, int minVoxelCount,
        Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, total = w*h*d;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var visited = new bool[total];
        for (int i = 0; i < total; i++)
        {
            if (visited[i] || segVol.Labels[i] != targetLabel) continue;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i); visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                comp.Add(cur);
                int cz=cur/(w*h), rem=cur%(w*h), cy=rem/w, cx=rem%w;
                foreach (var n in neighbors)
                {
                    int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                    if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                    int ni = nx+ny*w+nz*w*h;
                    if (!visited[ni] && segVol.Labels[ni] == targetLabel)
                    { visited[ni] = true; queue.Enqueue(ni); }
                }
            }
            if (comp.Count < minVoxelCount)
                foreach (int idx in comp) segVol.Labels[idx] = 0;
        }
        progress?.Invoke(1.0);
    }

    public static void KeepLargestComponent(
        SegmentationVolume segVol, byte label,
        Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, total = w*h*d;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var visited = new bool[total];
        List<int>? largest = null;
        int maxSize = 0;
        for (int i = 0; i < total; i++)
        {
            if (visited[i] || segVol.Labels[i] != label) continue;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i); visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                comp.Add(cur);
                int cz=cur/(w*h), rem=cur%(w*h), cy=rem/w, cx=rem%w;
                foreach (var n in neighbors)
                {
                    int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                    if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                    int ni = nx+ny*w+nz*w*h;
                    if (!visited[ni] && segVol.Labels[ni] == label)
                    { visited[ni] = true; queue.Enqueue(ni); }
                }
            }
            if (comp.Count > maxSize) { maxSize = comp.Count; largest = comp; }
        }
        for (int i = 0; i < total; i++)
            if (segVol.Labels[i] == label) segVol.Labels[i] = 0;
        if (largest != null)
            foreach (int idx in largest) segVol.Labels[idx] = label;
        progress?.Invoke(1.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MORPHOLOGICAL CLOSING — GPU first, Parallel.For fallback
    // ─────────────────────────────────────────────────────────────────────────

    public static void MorphologicalClosing(
        SegmentationVolume segVol, byte label, int iterations = 1,
        Action<double>? progress = null)
    {
        try { MorphClosingGpu(segVol, label, iterations, progress); }
        catch { MorphClosingCpu(segVol, label, iterations, progress); }
    }

    private static void MorphClosingGpu(
        SegmentationVolume segVol, byte label, int iterations,
        Action<double>? progress)
    {
        var gpu = GpuContext.Instance;
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, n = w*h*d;
        using var gpuA = gpu.Accelerator.Allocate1D<byte>(segVol.Labels);
        using var gpuB = gpu.Accelerator.Allocate1D<byte>(n);

        var dilK = gpu.Accelerator.LoadAutoGroupedStreamKernel(
            (Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int, byte>)
            GpuKernels.DilationKernel);
        var eroK = gpu.Accelerator.LoadAutoGroupedStreamKernel(
            (Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int, byte>)
            GpuKernels.ErosionKernel);

        for (int iter = 0; iter < iterations; iter++)
        {
            dilK(n, gpuA.View, gpuB.View, w, h, d, label);
            gpu.Accelerator.Synchronize();
            eroK(n, gpuB.View, gpuA.View, w, h, d, label);
            gpu.Accelerator.Synchronize();
            progress?.Invoke((double)(iter+1)/iterations);
        }
        gpuA.CopyToCPU(segVol.Labels);
    }

    private static void MorphClosingCpu(
        SegmentationVolume segVol, byte label, int iterations,
        Action<double>? progress)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, total = w*h*d;
        var temp = new byte[total];
        for (int iter = 0; iter < iterations; iter++)
        {
            Parallel.For(0, d, z =>
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x+y*w+z*w*h;
                    if (segVol.Labels[i] == label) { temp[i] = label; return; }
                    bool found =
                        (x>0   && segVol.Labels[i-1]   == label) ||
                        (x<w-1 && segVol.Labels[i+1]   == label) ||
                        (y>0   && segVol.Labels[i-w]   == label) ||
                        (y<h-1 && segVol.Labels[i+w]   == label) ||
                        (z>0   && segVol.Labels[i-w*h] == label) ||
                        (z<d-1 && segVol.Labels[i+w*h] == label);
                    temp[i] = found ? label : (byte)0;
                }
            });
            Array.Copy(temp, segVol.Labels, total);
            Parallel.For(0, d, z =>
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = x+y*w+z*w*h;
                    if (segVol.Labels[i] != label) { temp[i] = segVol.Labels[i]; return; }
                    bool erode =
                        (x==0   || segVol.Labels[i-1]   != label) ||
                        (x==w-1 || segVol.Labels[i+1]   != label) ||
                        (y==0   || segVol.Labels[i-w]   != label) ||
                        (y==h-1 || segVol.Labels[i+w]   != label) ||
                        (z==0   || segVol.Labels[i-w*h] != label) ||
                        (z==d-1 || segVol.Labels[i+w*h] != label);
                    temp[i] = erode ? (byte)0 : label;
                }
            });
            Array.Copy(temp, segVol.Labels, total);
            progress?.Invoke((double)(iter+1)/iterations);
        }
    }

    public static void KeepTopPercentageComponents(
        SegmentationVolume segVol, byte label, double keepRatio,
        Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, total = w*h*d;
        int[][] neighbors = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
        var visited = new bool[total];
        var components = new List<List<int>>();
        for (int i = 0; i < total; i++)
        {
            if (visited[i] || segVol.Labels[i] != label) continue;
            var comp = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(i); visited[i] = true;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                comp.Add(cur);
                int cz=cur/(w*h), rem=cur%(w*h), cy=rem/w, cx=rem%w;
                foreach (var n in neighbors)
                {
                    int nx=cx+n[0], ny=cy+n[1], nz=cz+n[2];
                    if (nx<0||nx>=w||ny<0||ny>=h||nz<0||nz>=d) continue;
                    int ni = nx+ny*w+nz*w*h;
                    if (!visited[ni] && segVol.Labels[ni] == label)
                    { visited[ni] = true; queue.Enqueue(ni); }
                }
            }
            components.Add(comp);
        }
        components.Sort((a, b) => b.Count.CompareTo(a.Count));
        int keep = Math.Max(1, (int)Math.Ceiling(components.Count * keepRatio));
        for (int i = 0; i < total; i++)
            if (segVol.Labels[i] == label) segVol.Labels[i] = 0;
        for (int ci = 0; ci < keep && ci < components.Count; ci++)
            foreach (int idx in components[ci]) segVol.Labels[idx] = label;
        progress?.Invoke(1.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SMOOTH LABEL MASK — GPU first, Parallel.For fallback
    // ─────────────────────────────────────────────────────────────────────────

    public static void SmoothLabelMask(
        SegmentationVolume segVol, byte label,
        Action<double>? progress = null)
    {
        try { SmoothGpu(segVol, label, progress); }
        catch { SmoothCpu(segVol, label, progress); }
    }

    private static void SmoothGpu(
        SegmentationVolume segVol, byte label, Action<double>? progress)
    {
        var gpu = GpuContext.Instance;
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth, n = w*h*d;
        using var gpuIn  = gpu.Accelerator.Allocate1D<byte>(segVol.Labels);
        using var gpuOut = gpu.Accelerator.Allocate1D<byte>(n);

        var kernel = gpu.Accelerator.LoadAutoGroupedStreamKernel(
            (Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int, byte>)
            GpuKernels.SmoothLabelKernel);

        kernel(n, gpuIn.View, gpuOut.View, w, h, d, label);
        gpu.Accelerator.Synchronize();
        gpuOut.CopyToCPU(segVol.Labels);
        progress?.Invoke(1.0);
    }

    private static void SmoothCpu(
        SegmentationVolume segVol, byte label, Action<double>? progress)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var output = new byte[w * h * d];
        Parallel.For(1, d-1, z =>
        {
            for (int y = 1; y < h-1; y++)
            for (int x = 1; x < w-1; x++)
            {
                int count = 0;
                for (int dz=-1; dz<=1; dz++)
                for (int dy=-1; dy<=1; dy++)
                for (int dx=-1; dx<=1; dx++)
                {
                    int ni = (x+dx) + (y+dy)*w + (z+dz)*w*h;
                    if (segVol.Labels[ni] == label) count++;
                }
                output[x + y*w + z*w*h] = count >= 14 ? label : (byte)0;
            }
        });
        Array.Copy(output, segVol.Labels, output.Length);
        progress?.Invoke(1.0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EXTRACT MESH
    // ─────────────────────────────────────────────────────────────────────────

    public static List<float[]> ExtractSegmentMesh(
        VolumeData volume, SegmentationVolume segVol,
        byte label, int stepSize = 1,
        Action<double>? progress = null)
    {
        int n = volume.Voxels.Length;
        var masked = new short[n];
        Parallel.For(0, n, i =>
            masked[i] = segVol.Labels[i] == label ? (short)0 : (short)-1024);
        var maskedVol = new VolumeData(volume.Width, volume.Height, volume.Depth,
            (double[])volume.Spacing.Clone());
        Array.Copy(masked, maskedVol.Voxels, n);
        progress?.Invoke(0.1);
        return MarchingCubes.Extract(maskedVol, -512.0, stepSize,
            p => progress?.Invoke(0.1 + p * 0.9));
    }

    public static List<float[]> ExtractLivePreviewMesh(
        VolumeData volume, SegmentationVolume segVol,
        byte label, int stepSize = 4,
        Action<double>? progress = null)
        => ExtractSegmentMesh(volume, segVol, label, stepSize, progress);

    // ─────────────────────────────────────────────────────────────────────────
    // RESLICE VOLUME — Parallel.For only (GPU path removed: too many params)
    // ─────────────────────────────────────────────────────────────────────────

    public static VolumeData ResliceVolume(
        VolumeData source, NhpTransform transform, NhpTransform inverseTransform)
    {
        double sx = source.Spacing[0], sy = source.Spacing[1], sz = source.Spacing[2];
        double cx = source.Width*sx/2, cy = source.Height*sy/2, cz = source.Depth*sz/2;

        var corners = new (double x, double y, double z)[8];
        int ci = 0;
        for (int bz=0; bz<=1; bz++)
        for (int by=0; by<=1; by++)
        for (int bx=0; bx<=1; bx++)
            corners[ci++] = transform.TransformPoint(
                bx*source.Width*sx - cx,
                by*source.Height*sy - cy,
                bz*source.Depth*sz  - cz);

        double minX=corners.Min(c=>c.x), maxX=corners.Max(c=>c.x);
        double minY=corners.Min(c=>c.y), maxY=corners.Max(c=>c.y);
        double minZ=corners.Min(c=>c.z), maxZ=corners.Max(c=>c.z);

        int w=(int)Math.Ceiling((maxX-minX)/sx);
        int h=(int)Math.Ceiling((maxY-minY)/sy);
        int d=(int)Math.Ceiling((maxZ-minZ)/sz);
        var newVoxels = new short[w*h*d];

        System.Threading.Tasks.Parallel.For(0, d, z =>
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double worldX = x*sx+minX, worldY = y*sy+minY, worldZ = z*sz+minZ;
                var (ox, oy, oz) = inverseTransform.TransformPoint(worldX, worldY, worldZ);
                newVoxels[x + y*w + z*w*h] = SampleTrilinear(source, ox+cx, oy+cy, oz+cz);
            }
        });

        var newVolume = new VolumeData(w, h, d, (double[])source.Spacing.Clone());
        Array.Copy(newVoxels, newVolume.Voxels, newVoxels.Length);
        newVolume.PatientName      = source.PatientName;
        newVolume.StudyDate        = source.StudyDate;
        newVolume.SeriesDescription = source.SeriesDescription + " (Resliced)";
        newVolume.ComputeMinMax();
        return newVolume;
    }

    private static short SampleTrilinear(VolumeData source, double vx, double vy, double vz)
    {
        int sw=source.Width, sh=source.Height, sd=source.Depth;
        double nvx=vx/source.Spacing[0], nvy=vy/source.Spacing[1], nvz=vz/source.Spacing[2];
        int x0=(int)nvx, y0=(int)nvy, z0=(int)nvz;
        int x1=x0+1,     y1=y0+1,     z1=z0+1;
        if (x0<0||y0<0||z0<0||x1>=sw||y1>=sh||z1>=sd) return short.MinValue;

        double tx=nvx-x0, ty=nvy-y0, tz=nvz-z0;
        double c000=source.Voxels[x0+y0*sw+z0*sw*sh], c100=source.Voxels[x1+y0*sw+z0*sw*sh];
        double c010=source.Voxels[x0+y1*sw+z0*sw*sh], c110=source.Voxels[x1+y1*sw+z0*sw*sh];
        double c001=source.Voxels[x0+y0*sw+z1*sw*sh], c101=source.Voxels[x1+y0*sw+z1*sw*sh];
        double c011=source.Voxels[x0+y1*sw+z1*sw*sh], c111=source.Voxels[x1+y1*sw+z1*sw*sh];

        double val =
            c000*(1-tx)*(1-ty)*(1-tz) + c100*tx*(1-ty)*(1-tz) +
            c010*(1-tx)*ty*(1-tz)     + c110*tx*ty*(1-tz)     +
            c001*(1-tx)*(1-ty)*tz     + c101*tx*(1-ty)*tz     +
            c011*(1-tx)*ty*tz         + c111*tx*ty*tz;

        return (short)Math.Clamp((int)val, short.MinValue, short.MaxValue);
    }
    // ─────────────────────────────────────────────────────────────────────────
    // OVERLOADS — original signatures kept for MainViewModel compatibility
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Original signature overload: splits connected components with auto w/h/d from segVol.
    /// </summary>
    public static List<(byte newLabel, int voxelCount)> SplitConnectedComponents(
        SegmentationVolume segVol, byte sourceLabel, byte startingLabel,
        Action<double>? progress = null)
    {
        return SplitConnectedComponents(
            segVol, sourceLabel,
            Enumerable.Range(0, 254).Select(i => (byte)(startingLabel + i)).ToList(),
            segVol.Width, segVol.Height, segVol.Depth,
            progress);
    }

    /// <summary>
    /// Original signature overload: live preview mesh from HU range directly (no SegmentationVolume needed).
    /// Uses Parallel.For for performance.
    /// </summary>
    public static List<float[]> ExtractLivePreviewMesh(
        VolumeData volume, short minHU, short maxHU, int stepSize = 4,
        Action<double>? progress = null)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        double sx = volume.Spacing[0], sy = volume.Spacing[1], sz = volume.Spacing[2];
        double isoLevel = 0.0;
        int maxX = w - stepSize, maxY = h - stepSize, maxZ = d - stepSize;
        int gw = maxX / stepSize, gh = maxY / stepSize, gd = maxZ / stepSize;

        var bag = new System.Collections.Concurrent.ConcurrentBag<List<float[]>>();

        Parallel.For(0, gd, () => new List<float[]>(256), (gz, _, localList) =>
        {
            int z = gz * stepSize;
            int[] ox = [0, stepSize, stepSize, 0, 0, stepSize, stepSize, 0];
            int[] oy = [0, 0, stepSize, stepSize, 0, 0, stepSize, stepSize];
            int[] oz = [0, 0, 0, 0, stepSize, stepSize, stepSize, stepSize];

            for (int gy = 0; gy < gh; gy++)
            {
                int y = gy * stepSize;
                for (int gx = 0; gx < gw; gx++)
                {
                    int x = gx * stepSize;
                    double[] val = new double[8];
                    for (int i = 0; i < 8; i++)
                    {
                        int px = x+ox[i], py = y+oy[i], pz = z+oz[i];
                        if (px>=w||py>=h||pz>=d) continue;
                        short hu = volume.Voxels[px + py*w + pz*w*h];
                        val[i] = hu >= minHU && hu <= maxHU
                            ? Math.Max(0.001, Math.Min(hu - minHU, maxHU - hu))
                            : hu < minHU ? hu - minHU : maxHU - hu;
                    }

                    int cubeIndex = 0;
                    for (int i = 0; i < 8; i++)
                        if (val[i] >= isoLevel) cubeIndex |= (1 << i);
                    if (cubeIndex == 0 || cubeIndex == 255) continue;

                    double[][] pos =
                    [
                        [x*sx, y*sy, z*sz], [(x+stepSize)*sx, y*sy, z*sz],
                        [(x+stepSize)*sx, (y+stepSize)*sy, z*sz], [x*sx, (y+stepSize)*sy, z*sz],
                        [x*sx, y*sy, (z+stepSize)*sz], [(x+stepSize)*sx, y*sy, (z+stepSize)*sz],
                        [(x+stepSize)*sx, (y+stepSize)*sy, (z+stepSize)*sz], [x*sx, (y+stepSize)*sy, (z+stepSize)*sz]
                    ];

                    int[] edgePairs = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
                    int edgeFlags = MarchingCubes.GetEdgeFlags(cubeIndex);
                    float[][] edgeVerts = new float[12][];

                    for (int i = 0; i < 12; i++)
                    {
                        if ((edgeFlags & (1 << i)) == 0) continue;
                        int a = edgePairs[i*2], b = edgePairs[i*2+1];
                        double diff = val[b] - val[a];
                        double t = Math.Abs(diff) > 0.001 ? (isoLevel - val[a]) / diff : 0.5;
                        t = Math.Clamp(t, 0, 1);
                        edgeVerts[i] = [
                            (float)(pos[a][0] + t*(pos[b][0]-pos[a][0])),
                            (float)(pos[a][1] + t*(pos[b][1]-pos[a][1])),
                            (float)(pos[a][2] + t*(pos[b][2]-pos[a][2]))
                        ];
                    }

                    var triIndices = MarchingCubes.GetTriangles(cubeIndex);
                    for (int i = 0; i < triIndices.Length && triIndices[i] != -1; i += 3)
                    {
                        localList.Add(edgeVerts[triIndices[i]]);
                        localList.Add(edgeVerts[triIndices[i+1]]);
                        localList.Add(edgeVerts[triIndices[i+2]]);
                    }
                }
            }
            return localList;
        }, localList => bag.Add(localList));

        var result = new List<float[]>();
        foreach (var local in bag) result.AddRange(local);
        progress?.Invoke(1.0);
        return result;
    }

}