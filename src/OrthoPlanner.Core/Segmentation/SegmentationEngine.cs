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

        bool[]? externalAirMask = null;
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
    /// </summary>
    private static bool[] ComputeExternalAirMask(VolumeData volume, short maxAirHU)
    {
        int w = volume.Width, h = volume.Height, d = volume.Depth;
        int totalVoxels = w * h * d;
        var globalVisited = new bool[totalVoxels];
        
        List<int> largestComponent = new List<int>();
        int maxSize = 0;

        int[][] n6 = [ [1,0,0], [-1,0,0], [0,1,0], [0,-1,0], [0,0,1], [0,0,-1] ];
        var queue = new Queue<(int x, int y, int z)>();

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (!globalVisited[idx] && volume.Voxels[idx] <= maxAirHU)
            {
                var currentComponent = new List<int>();
                
                globalVisited[idx] = true;
                queue.Enqueue((x, y, z));

                while (queue.Count > 0)
                {
                    var (cx, cy, cz) = queue.Dequeue();
                    int cIdx = cx + cy * w + cz * w * h;
                    currentComponent.Add(cIdx);

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

                if (currentComponent.Count > maxSize)
                {
                    maxSize = currentComponent.Count;
                    largestComponent = currentComponent;
                }
            }
        }

        var resultMask = new bool[totalVoxels];
        foreach (int idx in largestComponent)
        {
            resultMask[idx] = true;
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
    public static List<float[]> ExtractSegmentMesh(
        VolumeData volume, SegmentationVolume segVol,
        byte label, int stepSize = 1, Action<double>? progress = null)
    {
        var vertices = new List<float[]>();
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

            // 2. Separable 3D Box Blur applied 1 time (A nice balance between smoothness and detail retention)
            smooth = new float[w * h * d];
            
            for (int pass = 0; pass < 1; pass++)
            {
                // X-blur
                for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                for (int x = 1; x < w - 1; x++)
                    smooth[x+y*w+z*w*h] = (field[x-1+y*w+z*w*h] + field[x+y*w+z*w*h] + field[x+1+y*w+z*w*h]) * 0.3333333f;
                    
                // Y-blur
                for (int z = 0; z < d; z++)
                for (int y = 1; y < h - 1; y++)
                for (int x = 0; x < w; x++)
                    field[x+y*w+z*w*h] = (smooth[x+(y-1)*w+z*w*h] + smooth[x+y*w+z*w*h] + smooth[x+(y+1)*w+z*w*h]) * 0.3333333f;
                    
                // Z-blur
                for (int z = 1; z < d - 1; z++)
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    smooth[x+y*w+z*w*h] = (field[x+y*w+(z-1)*w*h] + field[x+y*w+z*w*h] + field[x+y*w+(z+1)*w*h]) * 0.3333333f;
                
                // Copy smoothed output back to input for the next iter
                if (pass < 0) Array.Copy(smooth, field, smooth.Length);
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
                    vertices.Add(edgeVerts[triIndices[i]]);
                    vertices.Add(edgeVerts[triIndices[i + 1]]);
                    vertices.Add(edgeVerts[triIndices[i + 2]]);
                }
            }
            progress?.Invoke((double)(z + 1) / d);
        }
        return vertices;
    }

    /// <summary>
    /// Performs a physical reslice of the volume using trilinear interpolation.
    /// </summary>
    public static VolumeData ResliceVolume(VolumeData source, NhpTransform transform)
    {
        // We use the transform to map from the Target grid to the Source.
        // The calling side should pass the inverse if they want to pull from source.
        int w = source.Width, h = source.Height, d = source.Depth;
        var newVoxels = new short[w * h * d];
        double sx = source.Spacing[0], sy = source.Spacing[1], sz = source.Spacing[2];

        System.Threading.Tasks.Parallel.For(0, d, z =>
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Map target voxel physical point back to source space
                    var (ox, oy, oz) = transform.TransformPoint(x * sx, y * sy, z * sz);
                    
                    // Sample HU using trilinear interpolation
                    newVoxels[x + y * w + z * w * h] = SampleTrilinear(source, ox, oy, oz);
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
}
