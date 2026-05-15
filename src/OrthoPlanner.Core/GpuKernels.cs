using ILGPU;
using ILGPU.Runtime;

namespace OrthoPlanner.Core;

/// <summary>
/// ILGPU kernels for GPU-accelerated volume processing.
/// All kernels are static methods with Index1D as first parameter (ILGPU convention).
/// </summary>
public static class GpuKernels
{
    // ─────────────────────────────────────────────────────────────────────────
    // SEGMENTATION: Threshold
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: label voxels in [minHU, maxHU] range.
    /// Each GPU thread processes one voxel.
    /// </summary>
    public static void ThresholdKernel(
        Index1D index,
        ArrayView<short> voxels,
        ArrayView<byte> labels,
        short minHU,
        short maxHU,
        byte label)
    {
        if (index >= voxels.Length) return;
        short val = voxels[index];
        if (val >= minHU && val <= maxHU)
            labels[index] = label;
    }

    /// <summary>
    /// GPU kernel: clear (zero) a byte array in parallel.
    /// </summary>
    public static void ClearKernel(Index1D index, ArrayView<byte> data)
    {
        if (index < data.Length)
            data[index] = 0;
    }

    /// <summary>
    /// GPU kernel: copy short array into float array (for further processing).
    /// </summary>
    public static void ShortToFloatKernel(
        Index1D index,
        ArrayView<short> src,
        ArrayView<float> dst)
    {
        if (index < src.Length)
            dst[index] = src[index];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SEGMENTATION: Morphological Closing (Dilation + Erosion)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: morphological dilation — a voxel is set if any 6-neighbor has the label.
    /// </summary>
    public static void DilationKernel(
        Index1D index,
        ArrayView<byte> input,
        ArrayView<byte> output,
        int w, int h, int d,
        byte label)
    {
        if (index >= w * h * d) return;

        int z = (int)(index / (w * h));
        int rem = (int)(index % (w * h));
        int y = rem / w;
        int x = rem % w;

        // Already labeled → keep
        if (input[index] == label)
        {
            output[index] = label;
            return;
        }

        // Check 6-connectivity neighbors
        bool found = false;
        if (x > 0     && input[index - 1] == label) found = true;
        if (x < w - 1 && input[index + 1] == label) found = true;
        if (y > 0     && input[index - w] == label) found = true;
        if (y < h - 1 && input[index + w] == label) found = true;
        if (z > 0     && input[index - w * h] == label) found = true;
        if (z < d - 1 && input[index + w * h] == label) found = true;

        if (found)
            output[index] = label;
        else if (output[index] == label)
            output[index] = 0; // clear stale from previous iteration
    }

    /// <summary>
    /// GPU kernel: morphological erosion — a voxel is cleared if any 6-neighbor is NOT labeled.
    /// </summary>
    public static void ErosionKernel(
        Index1D index,
        ArrayView<byte> input,
        ArrayView<byte> output,
        int w, int h, int d,
        byte label)
    {
        if (index >= w * h * d) return;

        if (input[index] != label)
        {
            // Not labeled → keep as is
            if (output[index] == label) output[index] = 0;
            return;
        }

        int z = (int)(index / (w * h));
        int rem = (int)(index % (w * h));
        int y = rem / w;
        int x = rem % w;

        // If all 6 neighbors are labeled, keep. Otherwise erode.
        bool erode =
            (x == 0     || input[index - 1] != label) ||
            (x == w - 1 || input[index + 1] != label) ||
            (y == 0     || input[index - w] != label) ||
            (y == h - 1 || input[index + w] != label) ||
            (z == 0     || input[index - w * h] != label) ||
            (z == d - 1 || input[index + w * h] != label);

        output[index] = erode ? (byte)0 : label;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SEGMENTATION: Smoothing (3D majority vote)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: 3x3x3 majority-vote smoothing on a label mask.
    /// Voxel keeps its label if ≥14 of 27 neighbors (including itself) agree.
    /// </summary>
    public static void SmoothLabelKernel(
        Index1D index,
        ArrayView<byte> input,
        ArrayView<byte> output,
        int w, int h, int d,
        byte label)
    {
        if (index >= w * h * d) return;

        int z = (int)(index / (w * h));
        int rem = (int)(index % (w * h));
        int y = rem / w;
        int x = rem % w;

        // Skip border voxels
        if (x == 0 || x >= w - 1 || y == 0 || y >= h - 1 || z == 0 || z >= d - 1)
        {
            output[index] = input[index];
            return;
        }

        int count = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int ni = (x + dx) + (y + dy) * w + (z + dz) * w * h;
            if (input[ni] == label) count++;
        }

        output[index] = count >= 14 ? label : (byte)0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MARCHING CUBES: Cube index computation pass
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel (pass 1 of 2): for each cube (x,y,z), compute the cubeIndex (0-255)
    /// and store it. Cubes with index 0 or 255 get -1 (empty/full).
    /// </summary>
    public static void MarchingCubesCubeIndexKernel(
        Index1D index,
        ArrayView<short> voxels,
        ArrayView<int> cubeIndices,
        int w, int h, int d,
        float isoValue,
        int stepSize)
    {
        // Total grid cells after stepping
        int gw = (w - 1) / stepSize;
        int gh = (h - 1) / stepSize;
        // int gd = (d - 1) / stepSize; // unused but left for clarity

        if (index >= cubeIndices.Length) return;

        int gz = (int)(index / (gw * gh));
        int rem = (int)(index % (gw * gh));
        int gy = rem / gw;
        int gx = rem % gw;

        int x = gx * stepSize;
        int y = gy * stepSize;
        int z = gz * stepSize;

        int xs = stepSize, ys = stepSize * w, zs = stepSize * w * h;

        // 8 corner indices in flat array
        int i000 = x + y * w + z * w * h;

        float v0 = voxels[i000];
        float v1 = (x + stepSize < w) ? voxels[i000 + xs] : v0;
        float v2 = (x + stepSize < w && y + stepSize < h) ? voxels[i000 + xs + ys] : v0;
        float v3 = (y + stepSize < h) ? voxels[i000 + ys] : v0;
        float v4 = (z + stepSize < d) ? voxels[i000 + zs] : v0;
        float v5 = (x + stepSize < w && z + stepSize < d) ? voxels[i000 + xs + zs] : v0;
        float v6 = (x + stepSize < w && y + stepSize < h && z + stepSize < d) ? voxels[i000 + xs + ys + zs] : v0;
        float v7 = (y + stepSize < h && z + stepSize < d) ? voxels[i000 + ys + zs] : v0;

        int ci = 0;
        if (v0 >= isoValue) ci |= 1;
        if (v1 >= isoValue) ci |= 2;
        if (v2 >= isoValue) ci |= 4;
        if (v3 >= isoValue) ci |= 8;
        if (v4 >= isoValue) ci |= 16;
        if (v5 >= isoValue) ci |= 32;
        if (v6 >= isoValue) ci |= 64;
        if (v7 >= isoValue) ci |= 128;

        cubeIndices[index] = (ci == 0 || ci == 255) ? -1 : ci;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICP / MESH: Parallel transform application
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: apply a 4x4 rigid transform to a flat float vertex array (x,y,z triplets).
    /// transform is passed as a flat 16-element array (row-major).
    /// </summary>
    public static void TransformVerticesKernel(
        Index1D index,
        ArrayView<float> vertices,  // flat: x0,y0,z0, x1,y1,z1 ...
        ArrayView<float> transform) // 16 elements, row-major 4x4
    {
        int base3 = index * 3;
        if (base3 + 2 >= vertices.Length) return;

        float x = vertices[base3];
        float y = vertices[base3 + 1];
        float z = vertices[base3 + 2];

        vertices[base3]     = transform[0] * x + transform[1] * y + transform[2] * z + transform[3];
        vertices[base3 + 1] = transform[4] * x + transform[5] * y + transform[6] * z + transform[7];
        vertices[base3 + 2] = transform[8] * x + transform[9] * y + transform[10] * z + transform[11];
    }

    /// <summary>
    /// GPU kernel: compute squared distance from each source point to its pre-matched target.
    /// src and tgt are flat float arrays (x,y,z triplets), distances output one float per pair.
    /// </summary>
    public static void ComputeDistancesKernel(
        Index1D index,
        ArrayView<float> src,       // flat x,y,z
        ArrayView<float> tgt,       // flat x,y,z (same length)
        ArrayView<float> distances) // one per point
    {
        int base3 = index * 3;
        if (base3 + 2 >= src.Length) return;

        float dx = src[base3] - tgt[base3];
        float dy = src[base3 + 1] - tgt[base3 + 1];
        float dz = src[base3 + 2] - tgt[base3 + 2];
        distances[index] = dx * dx + dy * dy + dz * dz;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MESH OPS: Parallel centroid computation + filter
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: compute triangle centroid Z values from flat vertex array.
    /// Used for SplitByZPlane.
    /// </summary>
    public static void ComputeTriCentroidZKernel(
        Index1D triIndex,
        ArrayView<float> vertices,  // flat x,y,z triplets; 3 consecutive verts = 1 triangle
        ArrayView<float> centroidZ) // one float per triangle
    {
        int base9 = triIndex * 9; // 3 verts × 3 floats
        if (base9 + 8 >= vertices.Length) return;
        centroidZ[triIndex] = (vertices[base9 + 2] + vertices[base9 + 5] + vertices[base9 + 8]) / 3f;
    }

    /// <summary>
    /// GPU kernel: build a boolean mask — true if triangle centroid Z >= zCut.
    /// </summary>
    public static void ZPlaneMaskKernel(
        Index1D triIndex,
        ArrayView<float> centroidZ,
        ArrayView<int> mask,        // 1 = above, 0 = below
        float zCut)
    {
        if (triIndex >= centroidZ.Length) return;
        mask[triIndex] = centroidZ[triIndex] >= zCut ? 1 : 0;
    }

    /// <summary>
    /// GPU kernel: trilinear interpolation for volume reslicing.
    /// newVoxels[index] = sampled value from source at the back-projected world coordinate.
    /// </summary>
    public static void TrilinearResliceKernel(
        Index1D index,
        ArrayView<short> srcVoxels,
        ArrayView<short> dstVoxels,
        int srcW, int srcH, int srcD,
        int dstW, int dstH,
        // Inverse transform row-major 3x3 rotation + translation packed as 12 floats:
        // [r00,r01,r02,tx, r10,r11,r12,ty, r20,r21,r22,tz]
        ArrayView<float> invTransform,
        float dstSx, float dstSy, float dstSz,  // destination spacing
        float minWorldX, float minWorldY, float minWorldZ,
        float srcSx, float srcSy, float srcSz,  // source spacing
        float cx, float cy, float cz)            // source center offsets
    {
        if (index >= dstVoxels.Length) return;

        int dstWH = dstW * dstH;
        int iz = (int)(index / dstWH);
        int rem = (int)(index % dstWH);
        int iy = rem / dstW;
        int ix = rem % dstW;

        float worldX = ix * dstSx + minWorldX;
        float worldY = iy * dstSy + minWorldY;
        float worldZ = iz * dstSz + minWorldZ;

        // Apply inverse transform
        float ox = invTransform[0] * worldX + invTransform[1] * worldY + invTransform[2] * worldZ + invTransform[3];
        float oy = invTransform[4] * worldX + invTransform[5] * worldY + invTransform[6] * worldZ + invTransform[7];
        float oz = invTransform[8] * worldX + invTransform[9] * worldY + invTransform[10] * worldZ + invTransform[11];

        // Shift to source local coordinates
        float vx = (ox + cx) / srcSx;
        float vy = (oy + cy) / srcSy;
        float vz = (oz + cz) / srcSz;

        // Trilinear interpolation
        int x0 = (int)vx, y0 = (int)vy, z0 = (int)vz;
        int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;

        if (x0 < 0 || y0 < 0 || z0 < 0 || x1 >= srcW || y1 >= srcH || z1 >= srcD)
        {
            dstVoxels[index] = short.MinValue;
            return;
        }

        float tx = vx - x0, ty = vy - y0, tz = vz - z0;

        int i000 = x0 + y0 * srcW + z0 * srcW * srcH;
        float c000 = srcVoxels[i000];
        float c100 = srcVoxels[i000 + 1];
        float c010 = srcVoxels[i000 + srcW];
        float c110 = srcVoxels[i000 + srcW + 1];
        float c001 = srcVoxels[i000 + srcW * srcH];
        float c101 = srcVoxels[i000 + srcW * srcH + 1];
        float c011 = srcVoxels[i000 + srcW * srcH + srcW];
        float c111 = srcVoxels[i000 + srcW * srcH + srcW + 1];

        float val =
            c000 * (1 - tx) * (1 - ty) * (1 - tz) +
            c100 * tx       * (1 - ty) * (1 - tz) +
            c010 * (1 - tx) * ty       * (1 - tz) +
            c110 * tx       * ty       * (1 - tz) +
            c001 * (1 - tx) * (1 - ty) * tz +
            c101 * tx       * (1 - ty) * tz +
            c011 * (1 - tx) * ty       * tz +
            c111 * tx       * ty       * tz;

        dstVoxels[index] = (short)Math.Clamp((int)val, short.MinValue, short.MaxValue);
    }
}
