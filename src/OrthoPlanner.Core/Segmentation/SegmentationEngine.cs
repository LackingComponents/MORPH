using System.Collections;
using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.Core.Segmentation;

/// <summary>
/// Segmentation algorithms: threshold, region growing, connected components.
/// All operate on VolumeData + SegmentationVolume.
/// </summary>
public static class SegmentationEngine
{
    /// <summary>
    /// Threshold segmentation: label all voxels within [minHU, maxHU] range.
    /// If enhanceThinBone is true, voxels just below minHU are evaluated for high local contrast
    /// (e.g. touching air/fat). If contrast is high, they are included as partial-volume bone bounds.
    /// </summary>
    public static void ThresholdSegment(
        VolumeData volume, SegmentationVolume segVol,
        byte label, short minHU, short maxHU,
        bool enhanceThinBone = false,
        Action<double>? progress = null)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int total = w * h * d;
        
        // 6-connectivity for checking high-contrast air/fat neighbors
        int[][] n6 = [[1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]];
        short airThreshold = -400; // Anything below this is definitively air/fat, providing high contrast

        BitArray? externalAirMask = null;
        if (enhanceThinBone)
        {
            if (progress != null) progress(0.05);
            externalAirMask = ComputeExternalAirMask(volume, airThreshold);
        }

        for (int z = 0; z < d; z++)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = x + y * w + z * w * h;
                short val = volume.Voxels[i];
                
                // 1. Standard Threshold Inclusion
                if (val >= minHU && val <= maxHU)
                {
                    segVol.Labels[i] = label;
                }
                // 2. Thin Bone Enhancement (Partial Volume Effect Recovery)
                else if (enhanceThinBone && val >= minHU - 200 && val < minHU)
                {
                    // This voxel is just slightly below the bone threshold. 
                    // Does it touch stark empty space within a 2-voxel radius (5x5x5)?
                    bool touchesInternalAir = false;
                    bool touchesExternalAir = false;

                    for (int dz = -2; dz <= 2; dz++)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        int nx = x + dx, ny = y + dy, nz = z + dz;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d)
                        {
                            int nIdx = nx + ny * w + nz * w * h;
                            if (volume.Voxels[nIdx] <= airThreshold)
                            {
                                if (externalAirMask != null && externalAirMask[nIdx])
                                    touchesExternalAir = true;
                                else
                                    touchesInternalAir = true;
                            }
                        }
                    }
                    
                    // Only enhance if it touches protected INTERNAL air (sinuses)
                    // and does NOT touch EXTERNAL Room air (which coats the skin).
                    if (touchesInternalAir && !touchesExternalAir)
                    {
                        segVol.Labels[i] = label;
                    }
                }
            }

            if (progress != null && z % 20 == 0)
                progress((double)z / d);
        }
        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Computes a boolean mask of "Room Air" by extracting the largest connected component of air voxels.
    /// Used to prevent the thin-bone edge-enhancer from wrapping onto the patient's external skin.
    ///
    /// ponytail: uses BitArray (12.5 MB) instead of bool[] (100 MB) for both the visited
    /// set and result mask, and avoids the intermediate List&lt;int&gt; (up to 200 MB for 50M+
    /// room-air voxels) by tracking only the seed of the largest component, then doing a
    /// second BFS pass to fill the result mask. Total savings: ~375 MB per bone seg.
    /// </summary>
    private static BitArray ComputeExternalAirMask(VolumeData volume, short maxAirHU)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int totalVoxels = w * h * d;
        var globalVisited = new BitArray(totalVoxels);

        // Pass 1: find every air component, but only keep the size + seed of the largest.
        int largestSize = 0;
        (int x, int y, int z) largestSeed = (0, 0, 0);

        int[][] n6 = [ [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1] ];
        var queue = new Queue<(int x, int y, int z)>();

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (!globalVisited[idx] && volume.Voxels[idx] <= maxAirHU)
            {
                int componentSize = 0;

                globalVisited[idx] = true;
                queue.Enqueue((x, y, z));

                while (queue.Count > 0)
                {
                    var (cx, cy, cz) = queue.Dequeue();
                    componentSize++;

                    foreach (var n in n6)
                    {
                        int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;

                        int nIdx = nx + ny * w + nz * w * h;
                        if (!globalVisited[nIdx] && volume.Voxels[nIdx] <= maxAirHU)
                        {
                            globalVisited[nIdx] = true;
                            queue.Enqueue((nx, ny, nz));
                        }
                    }
                }

                if (componentSize > largestSize)
                {
                    largestSize = componentSize;
                    largestSeed = (x, y, z);
                }
            }
        }

        // Pass 2: BFS from the largest component's seed to fill the result mask.
        // The resultMask itself doubles as the visited set for this pass.
        var resultMask = new BitArray(totalVoxels);
        if (largestSize > 0)
        {
            int seedIdx = largestSeed.x + largestSeed.y * w + largestSeed.z * w * h;
            resultMask[seedIdx] = true;
            queue.Enqueue(largestSeed);

            while (queue.Count > 0)
            {
                var (cx, cy, cz) = queue.Dequeue();

                foreach (var n in n6)
                {
                    int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;

                    int nIdx = nx + ny * w + nz * w * h;
                    if (!resultMask[nIdx] && volume.Voxels[nIdx] <= maxAirHU)
                    {
                        resultMask[nIdx] = true;
                        queue.Enqueue((nx, ny, nz));
                    }
                }
            }
        }

        return resultMask;
    }

    /// <summary>
    /// Seed-First Bounded Region Growing: Flood-fills starting from the seed point,
    /// constrained strictly to voxels that fall within the [minHU, maxHU] global threshold.
    /// This allows mechanical separation across soft joints.
    /// </summary>
    public static int RegionGrow(
        VolumeData volume, SegmentationVolume segVol,
        int seedX, int seedY, int seedZ,
        byte label, short minHU, short maxHU,
        Action<double>? progress = null)
    {
        short seedValue = volume.GetVoxel(seedX, seedY, seedZ);
        
        // Ensure the seed itself is actually within the growth bounds!
        if (seedValue < minHU || seedValue > maxHU) return 0;

        var visited = new bool[volume.Width * volume.Height * volume.Depth];
        var queue = new Queue<(int x, int y, int z)>();
        queue.Enqueue((seedX, seedY, seedZ));

        int idx = seedX + seedY * volume.Width + seedZ * volume.Width * volume.Height;
        visited[idx] = true;

        int count = 0;
        int totalVoxels = volume.Width * volume.Height * volume.Depth;

        // 6-connectivity offsets
        int[][] neighbors =
        [
            [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]
        ];

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            segVol.SetLabel(x, y, z, label);
            count++;

            if (progress != null && count % 50000 == 0)
                progress(Math.Min(1.0, (double)count / (totalVoxels * 0.1)));

            foreach (var n in neighbors)
            {
                int nx = x + n[0], ny = y + n[1], nz = z + n[2];
                if (nx < 0 || nx >= volume.Width ||
                    ny < 0 || ny >= volume.Height ||
                    nz < 0 || nz >= volume.Depth) continue;

                int nIdx = nx + ny * volume.Width + nz * volume.Width * volume.Height;
                if (visited[nIdx]) continue;
                visited[nIdx] = true;

                short val = volume.Voxels[nIdx];
                if (val >= minHU && val <= maxHU)
                    queue.Enqueue((nx, ny, nz));
            }
        }

        progress?.Invoke(1.0);
        return count;
    }

    /// <summary>
    /// Multi-Source Competitive BFS.
    /// Takes a list of seed markers and their assigned target labels.
    /// All seeds emit a flood-fill simultaneously at the same velocity using the global HU bounds.
    /// When expanding regions collide at bottlenecks (like the TMJ), they block each other,
    /// mechanically severing connected anatomy based on Voronoi-like distance metrics!
    /// </summary>
    public static void CompetitiveRegionGrow(
        VolumeData volume, SegmentationVolume segVol,
        List<(int x, int y, int z, byte label)> seeds,
        short minHU, short maxHU,
        Action<double>? progress = null)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        var visited = new bool[w * h * d];
        var queue = new Queue<(int x, int y, int z, byte label)>();

        // Enqueue all competing seeds simultaneously to start the parallel race
        foreach (var seed in seeds)
        {
            short seedValue = volume.GetVoxel(seed.x, seed.y, seed.z);
            if (seedValue >= minHU && seedValue <= maxHU)
            {
                int idx = seed.x + seed.y * w + seed.z * w * h;
                visited[idx] = true;
                queue.Enqueue(seed);
            }
        }

        int[][] neighbors = [ [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1] ];
        
        int totalProcessed = 0;
        int maxEstimate = w * h * d / 10;

        while (queue.Count > 0)
        {
            var (cx, cy, cz, label) = queue.Dequeue();
            segVol.SetLabel(cx, cy, cz, label);
            totalProcessed++;

            if (progress != null && totalProcessed % 50000 == 0)
                progress(Math.Min(1.0, (double)totalProcessed / maxEstimate));

            foreach (var n in neighbors)
            {
                int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;

                int nIdx = nx + ny * w + nz * w * h;
                
                // If this voxel has already been claimed by ANY seed's shockwave, we can't touch it.
                // This is where masks collide and sever!
                if (visited[nIdx]) continue;
                visited[nIdx] = true;

                short val = volume.Voxels[nIdx];
                if (val >= minHU && val <= maxHU)
                {
                    queue.Enqueue((nx, ny, nz, label));
                }
            }
        }

        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Mask-Based Region Growing: Flood-fills starting from the seed coordinate,
    /// but ONLY traverses voxels that already belong to 'sourceLabel'.
    /// Converts these connected voxels to 'newLabel'.
    /// </summary>
    public static int RegionGrowLabel(
        SegmentationVolume segVol,
        int seedX, int seedY, int seedZ,
        byte sourceLabel, byte newLabel,
        Action<double>? progress = null)
    {
        if (seedX < 0 || seedX >= segVol.Width ||
            seedY < 0 || seedY >= segVol.Height ||
            seedZ < 0 || seedZ >= segVol.Depth)
            return 0;

        // Ensure the seed actually sits on the source mask!
        if (segVol.GetLabel(seedX, seedY, seedZ) != sourceLabel) return 0;

        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var visited = new bool[w * h * d];
        var queue = new Queue<(int x, int y, int z)>();
        
        queue.Enqueue((seedX, seedY, seedZ));

        int idx = seedX + seedY * w + seedZ * w * h;
        visited[idx] = true;

        int count = 0;
        int maxPossible = w * h * d / 10; // rough guess for progress reporting

        // 6-connectivity offsets
        int[][] neighbors = [[1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]];

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            segVol.SetLabel(x, y, z, newLabel);
            count++;

            if (progress != null && count % 20000 == 0)
                progress(Math.Min(1.0, (double)count / maxPossible));

            foreach (var n in neighbors)
            {
                int nx = x + n[0], ny = y + n[1], nz = z + n[2];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;

                int nIdx = nx + ny * w + nz * w * h;
                if (visited[nIdx]) continue;
                
                // ONLY traverse if this neighbor is currently part of the Source Mask
                if (segVol.Labels[nIdx] == sourceLabel)
                {
                    visited[nIdx] = true;
                    queue.Enqueue((nx, ny, nz));
                }
            }
        }

        progress?.Invoke(1.0);
        return count;
    }

    /// <summary>
    /// Connected component labeling: finds all disconnected regions with the
    /// same label and splits them into separate labels. Returns the number
    /// of components found.
    /// </summary>
    public static List<(byte newLabel, int voxelCount)> SplitConnectedComponents(
        SegmentationVolume segVol, byte sourceLabel, byte startingLabel)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var visited = new bool[w * h * d];
        var components = new List<(byte, int)>();
        byte currentLabel = startingLabel;

        int[][] neighbors =
        [
            [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]
        ];

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (visited[idx] || segVol.Labels[idx] != sourceLabel) continue;

            // BFS flood fill for this component
            var queue = new Queue<(int, int, int)>();
            queue.Enqueue((x, y, z));
            visited[idx] = true;
            int count = 0;

            while (queue.Count > 0)
            {
                var (cx, cy, cz) = queue.Dequeue();
                segVol.SetLabel(cx, cy, cz, currentLabel);
                count++;

                foreach (var n in neighbors)
                {
                    int nIdx = cx + n[0], cy_new = cy + n[1], cz_new = cz + n[2];
                    if (nIdx < 0 || nIdx >= w || cy_new < 0 || cy_new >= h || cz_new < 0 || cz_new >= d) continue;
                    int flatIdx = nIdx + cy_new * w + cz_new * w * h;
                    if (!visited[flatIdx] && segVol.Labels[flatIdx] == sourceLabel)
                    {
                        visited[flatIdx] = true;
                        queue.Enqueue((nIdx, cy_new, cz_new));
                    }
                }
            }

            components.Add((currentLabel, count));
            currentLabel++;
        }

        return components;
    }

    /// <summary>
    /// Deletes any isolated islands of voxels that contain fewer than `minVoxelCount`.
    /// Useful for removing scatter noise caused by aggressive contrast enhancement.
    /// </summary>
    public static void RemoveSmallComponents(SegmentationVolume segVol, byte targetLabel, int minVoxelCount, Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var visited = new bool[w * h * d];

        int[][] neighbors = [ [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1] ];
        
        int totalProcessed = 0;
        int maxEstimate = w * h * d / 12;

        for (int z = 0; z < d; z++)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + y * w + z * w * h;
                if (visited[idx] || segVol.Labels[idx] != targetLabel) continue;

                // Found a new island! Let's BFS to count its size.
                var islandVoxels = new List<int>();
                var queue = new Queue<(int, int, int)>();
                
                queue.Enqueue((x, y, z));
                visited[idx] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy, cz) = queue.Dequeue();
                    int cIdx = cx + cy * w + cz * w * h;
                    islandVoxels.Add(cIdx);
                    totalProcessed++;

                    if (progress != null && totalProcessed % 10000 == 0)
                        progress(Math.Min(1.0, (double)totalProcessed / maxEstimate));

                    foreach (var n in neighbors)
                    {
                        int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;
                        
                        int nIdx = nx + ny * w + nz * w * h;
                        if (!visited[nIdx] && segVol.Labels[nIdx] == targetLabel)
                        {
                            visited[nIdx] = true;
                            queue.Enqueue((nx, ny, nz));
                        }
                    }
                }

                // If this island is too small (scatter noise), delete it!
                if (islandVoxels.Count < minVoxelCount)
                {
                    foreach (var islandIdx in islandVoxels)
                    {
                        segVol.Labels[islandIdx] = 0;
                    }
                }
            }
        }
        
        progress?.Invoke(1.0);
    }


    /// <summary>
    /// Finds the largest connected component of the given label and clears all other
    /// disconnected regions with the same label to remove scatter noise.
    /// </summary>
    public static void KeepLargestComponent(SegmentationVolume segVol, byte label, Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        int total = w * h * d;
        var visited = new bool[total];

        int maxSize = 0;
        var largestComponentSeeds = new List<(int, int, int)>();
        var components = new List<List<(int, int, int)>>();

        int[][] neighbors =
        [
            [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]
        ];

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (visited[idx] || segVol.Labels[idx] != label) continue;

            // Found a new component, flood fill to find its size
            var queue = new Queue<(int, int, int)>();
            var compVoxels = new List<(int, int, int)>();
            
            queue.Enqueue((x, y, z));
            visited[idx] = true;

            while (queue.Count > 0)
            {
                var (cx, cy, cz) = queue.Dequeue();
                compVoxels.Add((cx, cy, cz));

                foreach (var n in neighbors)
                {
                    int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;
                    int nIdx = nx + ny * w + nz * w * h;
                    if (!visited[nIdx] && segVol.Labels[nIdx] == label)
                    {
                        visited[nIdx] = true;
                        queue.Enqueue((nx, ny, nz));
                    }
                }
            }

            if (compVoxels.Count > maxSize)
            {
                maxSize = compVoxels.Count;
            }
            components.Add(compVoxels);
            
            if (progress != null && components.Count % 50 == 0)
                progress(Math.Min(0.5, (double)z / d));
        }

        // Clear all except the largest
        int clearCount = 0;
        foreach (var comp in components)
        {
            if (comp.Count == maxSize) continue; // Skip largest
            foreach (var (cx, cy, cz) in comp)
            {
                segVol.SetLabel(cx, cy, cz, 0); // clear
            }
            clearCount++;
            if (progress != null && clearCount % 50 == 0)
                progress(0.5 + Math.Min(0.5, (double)clearCount / components.Count * 0.5));
        }

        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Finds the largest N connected components of the given label and clears all other
    /// disconnected regions with the same label to remove scatter noise.
    /// </summary>
    public static void KeepLargestComponents(SegmentationVolume segVol, byte label, int countToKeep, Action<double>? progress = null)
    {
        if (countToKeep <= 0) return;

        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        int total = w * h * d;
        var visited = new bool[total];

        var components = new List<List<(int, int, int)>>();

        int[][] neighbors =
        [
            [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]
        ];

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (visited[idx] || segVol.Labels[idx] != label) continue;

            // Found a new component, flood fill to find its size
            var queue = new Queue<(int, int, int)>();
            var compVoxels = new List<(int, int, int)>();

            queue.Enqueue((x, y, z));
            visited[idx] = true;

            while (queue.Count > 0)
            {
                var (cx, cy, cz) = queue.Dequeue();
                compVoxels.Add((cx, cy, cz));

                foreach (var n in neighbors)
                {
                    int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;
                    int nIdx = nx + ny * w + nz * w * h;
                    if (!visited[nIdx] && segVol.Labels[nIdx] == label)
                    {
                        visited[nIdx] = true;
                        queue.Enqueue((nx, ny, nz));
                    }
                }
            }

            components.Add(compVoxels);

            if (progress != null && components.Count % 50 == 0)
                progress(Math.Min(0.5, (double)z / d));
        }

        // Sort descending by size
        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        // Clear all components starting from countToKeep index
        int clearCount = 0;
        for (int i = countToKeep; i < components.Count; i++)
        {
            foreach (var (cx, cy, cz) in components[i])
            {
                segVol.SetLabel(cx, cy, cz, 0); // clear
            }
            clearCount++;
            if (progress != null && clearCount % 50 == 0)
                progress(0.5 + Math.Min(0.5, (double)clearCount / components.Count * 0.5));
        }

        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Performs a Morphological Closing (Dilation followed by Erosion) on a given label.
    /// Dilation expands the mask by 1 voxel to bridge small gaps and holes.
    /// Erosion shrinks the mask by 1 voxel to restore original thickness without reopening the bridged holes.
    /// </summary>
    public static void MorphologicalClosing(SegmentationVolume segVol, byte label, int iterations = 1, Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var temp = new byte[segVol.Labels.Length];

        // 6-connectivity cross for Dilation/Erosion
        int[][] n6 = [[1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1]];

        for (int iter = 0; iter < iterations; iter++)
        {
            // === 1. DILATION ===
            Array.Copy(segVol.Labels, temp, temp.Length);
            for (int z = 1; z < d - 1; z++)
            {
                for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int cIdx = x + y * w + z * w * h;
                    if (temp[cIdx] == label) continue; // Already ON

                    // If any 6-neighbor is ON, turn this voxel ON
                    foreach (var offset in n6)
                    {
                        int nIdx = (x + offset[0]) + (y + offset[1]) * w + (z + offset[2]) * w * h;
                        if (temp[nIdx] == label)
                        {
                            segVol.SetLabel(x, y, z, label);
                            break;
                        }
                    }
                }
                if (progress != null && z % 20 == 0) progress(0.0 + ((double)z / d) * 0.25);
            }

            // === 2. EROSION ===
            Array.Copy(segVol.Labels, temp, temp.Length);
            for (int z = 1; z < d - 1; z++)
            {
                for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int cIdx = x + y * w + z * w * h;
                    if (temp[cIdx] != label) continue; // Already OFF

                    // If any 6-neighbor is OFF, turn this voxel OFF
                    foreach (var offset in n6)
                    {
                        int nIdx = (x + offset[0]) + (y + offset[1]) * w + (z + offset[2]) * w * h;
                        if (temp[nIdx] != label)
                        {
                            segVol.SetLabel(x, y, z, 0);
                            break;
                        }
                    }
                }
                if (progress != null && z % 20 == 0) progress(0.25 + ((double)z / d) * 0.25);
            }
        }
        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Identifies all disconnected components and removes the smallest X% of them by count.
    /// E.g. keeping the largest 30% of components (removing the 70% of smaller objects).
    /// </summary>
    public static void KeepTopPercentageComponents(SegmentationVolume segVol, byte label, double keepRatio, Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        int totalVoxels = w * h * d;
        var visited = new bool[totalVoxels];
        
        var components = new List<List<int>>();

        int[] n6 = { 1, -1, w, -w, w * h, -w * h }; // 1D offsets for speed
        var queue = new Queue<int>();

        for (int i = 0; i < totalVoxels; i++)
        {
            if (segVol.Labels[i] == label && !visited[i])
            {
                var comp = new List<int>();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    comp.Add(curr);

                    foreach (var offset in n6)
                    {
                        int nIdx = curr + offset;
                        if (nIdx >= 0 && nIdx < totalVoxels && !visited[nIdx] && segVol.Labels[nIdx] == label)
                        {
                            visited[nIdx] = true;
                            queue.Enqueue(nIdx);
                        }
                    }
                }
                components.Add(comp);
            }
        }

        if (components.Count == 0) return;

        // Sort by size descending
        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        // Keep top ratio
        int keepCount = Math.Max(1, (int)(components.Count * keepRatio));
        
        // Wipe the removed components from the volume
        for (int i = keepCount; i < components.Count; i++)
        {
            foreach (var idx in components[i])
            {
                segVol.Labels[idx] = 0;
            }
        }
    }

    /// <summary>
    /// Smooths the binary mask for a specific label using a 3x3x3 majority-vote 
    /// morphological filter. This fills small holes and smooths jagged boundaries.
    /// </summary>
    public static void SmoothLabelMask(SegmentationVolume segVol, byte label, Action<double>? progress = null)
    {
        int w = segVol.Width, h = segVol.Height, d = segVol.Depth;
        var temp = new byte[segVol.Labels.Length];
        Array.Copy(segVol.Labels, temp, temp.Length);

        // Required threshold of neighbors to turn ON a background voxel or keep an ON voxel
        // Out of 26 neighbors + 1 center = 27 total. Majority is >= 14
        const int threshold = 14; 

        // Offsets for 3x3x3 neighborhood
        int[][] n27 = new int[27][];
        int idx = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            n27[idx++] = [dx, dy, dz];

        for (int z = 1; z < d - 1; z++)
        {
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int cIdx = x + y * w + z * w * h;
                int count = 0;

                foreach (var offset in n27)
                {
                    int nx = x + offset[0], ny = y + offset[1], nz = z + offset[2];
                    int nIdx = nx + ny * w + nz * w * h;
                    if (temp[nIdx] == label) count++;
                }

                if (count >= threshold)
                    segVol.SetLabel(x, y, z, label);
                else if (temp[cIdx] == label)
                    segVol.SetLabel(x, y, z, 0); // clear if it lost the vote
            }
            if (progress != null && z % 10 == 0)
                progress((double)z / d);
        }
        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Generate a 3D mesh from a labeled segment using marching cubes.
    /// Uses actual HU values (not binary) for smooth interpolation.
    /// The iso value is the midpoint of the threshold range.
    /// </summary>
    public static float[] ExtractSegmentMesh(
        VolumeData volume, SegmentationVolume segVol,
        byte label, int stepSize = 1, Action<double>? progress = null,
        double smoothingAmount = 0.6666667, int smoothingPasses = 1)
    {
        var vertices = new List<float>();
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        double sx = volume.Spacing[0], sy = volume.Spacing[1], sz = volume.Spacing[2];

        float[]? smooth = null;
        double isoLevel;

        if (stepSize == 1)
        {
            // 1. Convert mask to probability field
            float[] field = new float[w * h * d];
            for (int i = 0; i < field.Length; i++)
                field[i] = segVol.Labels[i] == label ? 100f : 0f;

            smooth = new float[w * h * d];

            if (smoothingPasses > 0)
            {
                float s = (float)smoothingAmount;
                float wCenter = 1.0f - s;
                float wNeighbors = s / 2.0f;

                for (int pass = 0; pass < smoothingPasses; pass++)
                {
                    // X-blur
                    for (int z = 0; z < d; z++)
                    for (int y = 0; y < h; y++)
                    for (int x = 1; x < w - 1; x++)
                        smooth[x+y*w+z*w*h] = field[x+y*w+z*w*h] * wCenter + (field[x-1+y*w+z*w*h] + field[x+1+y*w+z*w*h]) * wNeighbors;

                    // Y-blur
                    for (int z = 0; z < d; z++)
                    for (int y = 1; y < h - 1; y++)
                    for (int x = 0; x < w; x++)
                        field[x+y*w+z*w*h] = smooth[x+y*w+z*w*h] * wCenter + (smooth[x+(y-1)*w+z*w*h] + smooth[x+(y+1)*w+z*w*h]) * wNeighbors;

                    // Z-blur
                    for (int z = 1; z < d - 1; z++)
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        smooth[x+y*w+z*w*h] = field[x+y*w+z*w*h] * wCenter + (field[x+y*w+(z-1)*w*h] + field[x+y*w+(z+1)*w*h]) * wNeighbors;

                    // Copy smoothed output back to input for the next iter
                    if (pass < smoothingPasses - 1) Array.Copy(smooth, field, smooth.Length);
                }
            }
            else
            {
                Array.Copy(field, smooth, field.Length);
            }

            // Lowered isoLevel captures thinner bones that get diluted down in probability, preventing holes
            isoLevel = 35.0;
        }
        else
        {
            // Fast preview mode: bypass expensive 800MB array allocations and blurs
            isoLevel = 50.0;
        }

        // Iterate from -1 to Width to allow the zero-padded bounds to act as solid sealing mesh walls
        int maxX = w, maxY = h, maxZ = d;
        for (int z = -1; z < maxZ; z += stepSize)
        {
            for (int y = -1; y < maxY; y += stepSize)
            for (int x = -1; x < maxX; x += stepSize)
            {
                int[] ox = [0, stepSize, stepSize, 0, 0, stepSize, stepSize, 0];
                int[] oy = [0, 0, stepSize, stepSize, 0, 0, stepSize, stepSize];
                int[] oz = [0, 0, 0, 0, stepSize, stepSize, stepSize, stepSize];

                double[] val = new double[8];
                for (int i = 0; i < 8; i++)
                {
                    int px = x + ox[i];
                    int py = y + oy[i];
                    int pz = z + oz[i];
                    
                    // If out of bounds of the actual volume, explicitly return 0.0 probability.
                    // This forces Marching Cubes to abruptly jump across the 45.0 IsoLevel threshold
                    // right at the boundary box edges, drawing completely flat sealing caps across the gap!
                    if (px < 0 || px >= w || py < 0 || py >= h || pz < 0 || pz >= d)
                    {
                         val[i] = 0.0;
                    }
                    else if (stepSize == 1)
                    {
                        val[i] = smooth![px + py * w + pz * w * h];
                    }
                    else
                    {
                        val[i] = segVol.Labels[px + py * w + pz * w * h] == label ? 100.0 : 0.0;
                    }
                }

                int cubeIndex = 0;
                for (int i = 0; i < 8; i++)
                    if (val[i] >= isoLevel) cubeIndex |= (1 << i);
                if (cubeIndex == 0 || cubeIndex == 255) continue;

                // Corner positions in world coordinates
                double[][] pos =
                [
                    [x*sx, y*sy, z*sz],
                    [(x+stepSize)*sx, y*sy, z*sz],
                    [(x+stepSize)*sx, (y+stepSize)*sy, z*sz],
                    [x*sx, (y+stepSize)*sy, z*sz],
                    [x*sx, y*sy, (z+stepSize)*sz],
                    [(x+stepSize)*sx, y*sy, (z+stepSize)*sz],
                    [(x+stepSize)*sx, (y+stepSize)*sy, (z+stepSize)*sz],
                    [x*sx, (y+stepSize)*sy, (z+stepSize)*sz]
                ];

                float[][] edgeVerts = new float[12][];
                int[] edgePairs = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
                int edgeFlags = MarchingCubes.GetEdgeFlags(cubeIndex);

                for (int i = 0; i < 12; i++)
                {
                    if ((edgeFlags & (1 << i)) == 0) continue;
                    int a = edgePairs[i * 2], b = edgePairs[i * 2 + 1];

                    // Linear interpolation based on actual values
                    double diff = val[b] - val[a];
                    double t = Math.Abs(diff) > 0.001 ? (isoLevel - val[a]) / diff : 0.5;
                    t = Math.Clamp(t, 0, 1);

                    edgeVerts[i] =
                    [
                        (float)(pos[a][0] + t * (pos[b][0] - pos[a][0])),
                        (float)(pos[a][1] + t * (pos[b][1] - pos[a][1])),
                        (float)(pos[a][2] + t * (pos[b][2] - pos[a][2]))
                    ];
                }

                var triIndices = MarchingCubes.GetTriangles(cubeIndex);
                for (int i = 0; i < triIndices.Length && triIndices[i] != -1; i += 3)
                {
                    var ev0 = edgeVerts[triIndices[i]];
                    var ev1 = edgeVerts[triIndices[i + 1]];
                    var ev2 = edgeVerts[triIndices[i + 2]];
                    vertices.Add(ev0[0]); vertices.Add(ev0[1]); vertices.Add(ev0[2]);
                    vertices.Add(ev1[0]); vertices.Add(ev1[1]); vertices.Add(ev1[2]);
                    vertices.Add(ev2[0]); vertices.Add(ev2[1]); vertices.Add(ev2[2]);
                }
            }
            progress?.Invoke((double)(z + 1) / d);
        }
        return vertices.ToArray();
    }

    /// <summary>
    /// Generates a highly subsampled, raw Marching Cubes mesh directly from the VolumeData
    /// based on min/max HU values for real-time slider proxy rendering.
    /// </summary>
    public static float[] ExtractLivePreviewMesh(
        VolumeData volume, short minHU, short maxHU, int stepSize = 4)
    {
        var vertices = new List<float>();
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        double sx = volume.Spacing[0], sy = volume.Spacing[1], sz = volume.Spacing[2];

        // Use zero as the isosurface threshold.
        // Positive distance values are inside the bounds, negative are outside.
        double isoLevel = 0.0;

        int maxX = w - stepSize, maxY = h - stepSize, maxZ = d - stepSize;
        
        for (int z = 0; z < maxZ; z += stepSize)
        for (int y = 0; y < maxY; y += stepSize)
        for (int x = 0; x < maxX; x += stepSize)
        {
            int[] ox = [0, stepSize, stepSize, 0, 0, stepSize, stepSize, 0];
            int[] oy = [0, 0, stepSize, stepSize, 0, 0, stepSize, stepSize];
            int[] oz = [0, 0, 0, 0, stepSize, stepSize, stepSize, stepSize];

            double[] val = new double[8];
            for (int i = 0; i < 8; i++)
            {
                int px = x + ox[i], py = y + oy[i], pz = z + oz[i];
                short hu = volume.Voxels[px + py * w + pz * w * h];
                if (hu >= minHU && hu <= maxHU)
                {
                    val[i] = Math.Min(hu - minHU, maxHU - hu);
                    if (val[i] == 0) val[i] = 0.001; // ensure strict inclusion for boundaries
                }
                else if (hu < minHU)
                {
                    val[i] = hu - minHU;
                }
                else
                {
                    val[i] = maxHU - hu;
                }
            }

            int cubeIndex = 0;
            for (int i = 0; i < 8; i++)
                if (val[i] >= isoLevel) cubeIndex |= (1 << i);
            
            if (cubeIndex == 0 || cubeIndex == 255) continue;

            double[][] pos =
            [
                [x*sx, y*sy, z*sz],
                [(x+stepSize)*sx, y*sy, z*sz],
                [(x+stepSize)*sx, (y+stepSize)*sy, z*sz],
                [x*sx, (y+stepSize)*sy, z*sz],
                [x*sx, y*sy, (z+stepSize)*sz],
                [(x+stepSize)*sx, y*sy, (z+stepSize)*sz],
                [(x+stepSize)*sx, (y+stepSize)*sy, (z+stepSize)*sz],
                [x*sx, (y+stepSize)*sy, (z+stepSize)*sz]
            ];

            float[][] edgeVerts = new float[12][];
            int[] edgePairs = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
            int edgeFlags = MarchingCubes.GetEdgeFlags(cubeIndex);

            for (int i = 0; i < 12; i++)
            {
                if ((edgeFlags & (1 << i)) == 0) continue;
                int a = edgePairs[i * 2], b = edgePairs[i * 2 + 1];

                double diff = val[b] - val[a];
                double t = Math.Abs(diff) > 0.001 ? (isoLevel - val[a]) / diff : 0.5;
                t = Math.Clamp(t, 0, 1);

                edgeVerts[i] =
                [
                    (float)(pos[a][0] + t * (pos[b][0] - pos[a][0])),
                    (float)(pos[a][1] + t * (pos[b][1] - pos[a][1])),
                    (float)(pos[a][2] + t * (pos[b][2] - pos[a][2]))
                ];
            }

            var triIndices = MarchingCubes.GetTriangles(cubeIndex);
            for (int i = 0; i < triIndices.Length && triIndices[i] != -1; i += 3)
            {
                var ev0 = edgeVerts[triIndices[i]];
                var ev1 = edgeVerts[triIndices[i + 1]];
                var ev2 = edgeVerts[triIndices[i + 2]];
                vertices.Add(ev0[0]); vertices.Add(ev0[1]); vertices.Add(ev0[2]);
                vertices.Add(ev1[0]); vertices.Add(ev1[1]); vertices.Add(ev1[2]);
                vertices.Add(ev2[0]); vertices.Add(ev2[1]); vertices.Add(ev2[2]);
            }
        }
        
        return vertices.ToArray();
    }

    /// <summary>
    /// Performs a physical reslice of the volume using trilinear interpolation.
    /// </summary>
    public static VolumeData ResliceVolume(VolumeData source, NhpTransform transform, NhpTransform inverseTransform)
    {
        int wSrc = source.Width, hSrc = source.Height, dSrc = source.Depth;
        double sx = source.Spacing[0], sy = source.Spacing[1], sz = source.Spacing[2];

        // The world-center of the Original Volume
        double cx = wSrc * sx / 2.0;
        double cy = hSrc * sy / 2.0;
        double cz = dSrc * sz / 2.0;

        // Extract the 8 local physical corners of the source box
        double[] xCorners = [0, wSrc * sx];
        double[] yCorners = [0, hSrc * sy];
        double[] zCorners = [0, dSrc * sz];

        double minWorldX = double.MaxValue, maxWorldX = double.MinValue;
        double minWorldY = double.MaxValue, maxWorldY = double.MinValue;
        double minWorldZ = double.MaxValue, maxWorldZ = double.MinValue;

        // Map every corner through the inverse transform (Source -> Forward -> Target)
        // to find exactly where the bounding box reaches in the New Coordinate Space
        foreach (double x in xCorners)
        {
            foreach (double y in yCorners)
            {
                foreach (double z in zCorners)
                {
                    // Center the corner
                    double lox = x - cx; double loy = y - cy; double loz = z - cz;
                    // Project it forward to see how far the rotation throws it
                    var (wx, wy, wz) = inverseTransform.TransformPoint(lox, loy, loz);
                    
                    if (wx < minWorldX) minWorldX = wx; if (wx > maxWorldX) maxWorldX = wx;
                    if (wy < minWorldY) minWorldY = wy; if (wy > maxWorldY) maxWorldY = wy;
                    if (wz < minWorldZ) minWorldZ = wz; if (wz > maxWorldZ) maxWorldZ = wz;
                }
            }
        }

        // Calculate absolute minimum grid bounds necessary to enclose the rotated data
        int maxW = (int)Math.Ceiling((maxWorldX - minWorldX) / sx);
        int maxH = (int)Math.Ceiling((maxWorldY - minWorldY) / sy);
        int maxD = (int)Math.Ceiling((maxWorldZ - minWorldZ) / sz);

        int w = maxW, h = maxH, d = maxD;
        var newVoxels = new short[w * h * d];

        System.Threading.Tasks.Parallel.For(0, d, z =>
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Iterate through the NEW grid bounds and map backwards to see what's there
                    // Shift the (0,0,0) iteration point back to the global Box Minimums
                    double worldX = (x * sx) + minWorldX;
                    double worldY = (y * sy) + minWorldY;
                    double worldZ = (z * sz) + minWorldZ;

                    var (ox, oy, oz) = transform.TransformPoint(worldX, worldY, worldZ);
                    
                    // Shift back into local Source coordinates before Trilinear Interpolation picks it up
                    newVoxels[x + y * w + z * w * h] = SampleTrilinear(source, ox + cx, oy + cy, oz + cz);
                }
            }
        });

        var newVolume = new VolumeData(w, h, d, (double[])source.Spacing.Clone());
        Array.Copy(newVoxels, newVolume.Voxels, newVoxels.Length);
        
        newVolume.PatientName = source.PatientName;
        newVolume.StudyDate = source.StudyDate;
        newVolume.SeriesDescription = source.SeriesDescription + " (Resliced)";
        
        newVolume.ComputeMinMax();
        return newVolume;
    }

    private static short SampleTrilinear(VolumeData source, double vx, double vy, double vz)
    {
        double px = vx / source.Spacing[0];
        double py = vy / source.Spacing[1];
        double pz = vz / source.Spacing[2];

        int w = source.Width, h = source.Height, d = source.Depth;

        if (px < 0 || px >= w - 1 || py < 0 || py >= h - 1 || pz < 0 || pz >= d - 1)
            return -1024;

        int x0 = (int)px; int x1 = x0 + 1;
        int y0 = (int)py; int y1 = y0 + 1;
        int z0 = (int)pz; int z1 = z0 + 1;

        double dx = px - x0;
        double dy = py - y0;
        double dz = pz - z0;

        double v000 = source.Voxels[x0 + y0 * w + z0 * w * h];
        double v100 = source.Voxels[x1 + y0 * w + z0 * w * h];
        double v010 = source.Voxels[x0 + y1 * w + z0 * w * h];
        double v110 = source.Voxels[x1 + y1 * w + z0 * w * h];
        double v001 = source.Voxels[x0 + y0 * w + z1 * w * h];
        double v101 = source.Voxels[x1 + y0 * w + z1 * w * h];
        double v011 = source.Voxels[x0 + y1 * w + z1 * w * h];
        double v111 = source.Voxels[x1 + y1 * w + z1 * w * h];

        double v00 = v000 * (1 - dx) + v100 * dx;
        double v01 = v001 * (1 - dx) + v101 * dx;
        double v10 = v010 * (1 - dx) + v110 * dx;
        double v11 = v011 * (1 - dx) + v111 * dx;

        double v0 = v00 * (1 - dy) + v10 * dy;
        double v1 = v01 * (1 - dy) + v11 * dy;

        return (short)(v0 * (1 - dz) + v1 * dz);
    }

    /// <summary>
    /// Competitive multi-label region grow CONSTRAINED to a binary mask.
    /// <paramref name="outVol"/> must already contain the accepted seed labels (any non-zero
    /// label) before this call. Seeds that do NOT sit on a <paramref name="maskLabel"/> voxel of
    /// <paramref name="maskVol"/> are discarded (treated as preview noise and cleared).
    ///
    /// All seed fronts advance simultaneously at equal velocity (FIFO BFS) through 6-connected
    /// voxels where <paramref name="maskVol"/> == <paramref name="maskLabel"/>, claiming each
    /// unvisited mask voxel for the first front to reach it. Where two fronts collide they block
    /// each other, so the resulting cut follows bone connectivity (Voronoi-like geodesic split).
    ///
    /// Voxels outside the mask are never claimed; mask voxels in islands unreachable from any seed
    /// are left unlabeled — feed those to <see cref="FillNearestLabelWithinMask"/> as a fallback.
    /// Existing Core methods are untouched; this is an additive helper.
    /// </summary>
    public static void CompetitiveGrowLabelsWithinMask(
        SegmentationVolume maskVol, byte maskLabel,
        SegmentationVolume outVol,
        Action<double>? progress = null)
    {
        int w = maskVol.Width, h = maskVol.Height, d = maskVol.Depth;
        int total = w * h * d;
        var visited = new bool[total];
        var queue = new Queue<int>();

        // Initial frontier: every accepted seed voxel that lands on the mask.
        for (int i = 0; i < total; i++)
        {
            byte lbl = outVol.Labels[i];
            if (lbl == 0) continue;

            if (maskVol.Labels[i] == maskLabel)
            {
                visited[i] = true;
                queue.Enqueue(i);
            }
            else
            {
                outVol.Labels[i] = 0; // off-mask preview noise — discard
            }
        }

        int plane = w * h;
        int processed = 0;
        int maxEstimate = Math.Max(1, total / 8);

        while (queue.Count > 0)
        {
            int curr = queue.Dequeue();
            byte label = outVol.Labels[curr];
            processed++;
            if (progress != null && (processed & 0xFFFF) == 0)
                progress(Math.Min(1.0, (double)processed / maxEstimate));

            int cz = curr / plane;
            int rem = curr - cz * plane;
            int cy = rem / w;
            int cx = rem - cy * w;

            // 6-connectivity with explicit per-axis bounds (prevents row/plane wraparound).
            if (cx + 1 < w) TryClaim(curr + 1);
            if (cx - 1 >= 0) TryClaim(curr - 1);
            if (cy + 1 < h) TryClaim(curr + w);
            if (cy - 1 >= 0) TryClaim(curr - w);
            if (cz + 1 < d) TryClaim(curr + plane);
            if (cz - 1 >= 0) TryClaim(curr - plane);

            void TryClaim(int nIdx)
            {
                if (visited[nIdx]) return;
                if (maskVol.Labels[nIdx] != maskLabel) return;
                visited[nIdx] = true;
                outVol.Labels[nIdx] = label;
                queue.Enqueue(nIdx);
            }
        }

        progress?.Invoke(1.0);
    }

    /// <summary>
    /// Fallback for <see cref="CompetitiveGrowLabelsWithinMask"/>: assigns every still-unlabeled
    /// <paramref name="maskLabel"/> voxel of <paramref name="maskVol"/> the label of its nearest
    /// already-labeled voxel in <paramref name="outVol"/>. Distance is the 6-connected grid
    /// (geodesic) distance computed by a single multi-source BFS that is allowed to bridge across
    /// non-mask space, so disconnected bone islands — unreachable by the constrained grow — are
    /// still covered. Guarantees every mask voxel ends labeled (no holes, no unclassified bone).
    /// Additive helper; existing Core methods are untouched.
    /// </summary>
    public static void FillNearestLabelWithinMask(
        SegmentationVolume maskVol, byte maskLabel,
        SegmentationVolume outVol,
        Action<double>? progress = null)
    {
        int w = maskVol.Width, h = maskVol.Height, d = maskVol.Depth;
        int total = w * h * d;

        var prop = new byte[total]; // propagated nearest label (0 = not yet reached)
        var queue = new Queue<int>();

        int remaining = 0; // count of unlabeled mask voxels still needing a label
        for (int i = 0; i < total; i++)
        {
            if (outVol.Labels[i] != 0)
            {
                prop[i] = outVol.Labels[i];
                queue.Enqueue(i);
            }
            else if (maskVol.Labels[i] == maskLabel)
            {
                remaining++;
            }
        }

        if (remaining == 0 || queue.Count == 0) { progress?.Invoke(1.0); return; }

        int plane = w * h;
        int processed = 0;
        int maxEstimate = Math.Max(1, remaining);

        while (queue.Count > 0 && remaining > 0)
        {
            int curr = queue.Dequeue();
            byte lbl = prop[curr];

            int cz = curr / plane;
            int rem = curr - cz * plane;
            int cy = rem / w;
            int cx = rem - cy * w;

            if (cx + 1 < w) Visit(curr + 1);
            if (cx - 1 >= 0) Visit(curr - 1);
            if (cy + 1 < h) Visit(curr + w);
            if (cy - 1 >= 0) Visit(curr - w);
            if (cz + 1 < d) Visit(curr + plane);
            if (cz - 1 >= 0) Visit(curr - plane);

            void Visit(int nIdx)
            {
                if (prop[nIdx] != 0) return; // already reached by a nearer source
                prop[nIdx] = lbl;
                queue.Enqueue(nIdx);

                // Commit the label only into unlabeled mask voxels.
                if (outVol.Labels[nIdx] == 0 && maskVol.Labels[nIdx] == maskLabel)
                {
                    outVol.Labels[nIdx] = lbl;
                    remaining--;
                    processed++;
                    if (progress != null && (processed & 0xFFFF) == 0)
                        progress(Math.Min(1.0, (double)processed / maxEstimate));
                }
            }
        }

        progress?.Invoke(1.0);
    }
}
