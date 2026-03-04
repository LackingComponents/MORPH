using System;
using System.Collections.Generic;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Mesh operations for cranium-mandible splitting:
/// proximity-based subtraction, merging, Z-plane split, and bounding-box clipping.
/// All meshes are stored as List of float[3] vertices in triangle-soup format (every 3 consecutive vertices = 1 triangle).
/// </summary>
public static class MeshOps
{
    /// <summary>
    /// Remove triangles from <paramref name="boneVerts"/> whose centroid is within
    /// <paramref name="radiusMm"/> of any point in the <paramref name="castTree"/>.
    /// Returns a new vertex list with overlapping triangles removed.
    /// </summary>
    public static List<float[]> SubtractByProximity(List<float[]> boneVerts, KdTree castTree, float radiusMm)
    {
        float radiusSq = radiusMm * radiusMm;
        var result = new List<float[]>(boneVerts.Count);

        for (int i = 0; i + 2 < boneVerts.Count; i += 3)
        {
            // Triangle centroid
            float cx = (boneVerts[i][0] + boneVerts[i + 1][0] + boneVerts[i + 2][0]) / 3f;
            float cy = (boneVerts[i][1] + boneVerts[i + 1][1] + boneVerts[i + 2][1]) / 3f;
            float cz = (boneVerts[i][2] + boneVerts[i + 1][2] + boneVerts[i + 2][2]) / 3f;

            var (_, distSq) = castTree.FindNearest(cx, cy, cz);

            if (distSq > radiusSq)
            {
                // Keep this triangle
                result.Add(new float[] { boneVerts[i][0], boneVerts[i][1], boneVerts[i][2] });
                result.Add(new float[] { boneVerts[i + 1][0], boneVerts[i + 1][1], boneVerts[i + 1][2] });
                result.Add(new float[] { boneVerts[i + 2][0], boneVerts[i + 2][1], boneVerts[i + 2][2] });
            }
        }
        return result;
    }

    /// <summary>
    /// Concatenate two triangle-soup vertex lists into one.
    /// </summary>
    public static List<float[]> MergeVertices(List<float[]> meshA, List<float[]> meshB)
    {
        var merged = new List<float[]>(meshA.Count + meshB.Count);
        merged.AddRange(meshA);
        merged.AddRange(meshB);
        return merged;
    }

    /// <summary>
    /// Split a triangle-soup mesh into two parts at a Z threshold.
    /// Triangles with centroid above zCut go to 'above', everything else goes to 'below'.
    /// </summary>
    public static (List<float[]> Above, List<float[]> Below) SplitByZPlane(List<float[]> verts, float zCut)
    {
        var above = new List<float[]>();
        var below = new List<float[]>();

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            float cz = (verts[i][2] + verts[i + 1][2] + verts[i + 2][2]) / 3f;

            var target = cz >= zCut ? above : below;
            target.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
            target.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
            target.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
        }

        return (above, below);
    }

    /// <summary>
    /// Return only the triangles whose centroid lies inside the given axis-aligned bounding box.
    /// </summary>
    public static List<float[]> ClipToBoundingBox(List<float[]> verts, float[] center, float[] halfExtents)
    {
        var result = new List<float[]>();
        float minX = center[0] - halfExtents[0], maxX = center[0] + halfExtents[0];
        float minY = center[1] - halfExtents[1], maxY = center[1] + halfExtents[1];
        float minZ = center[2] - halfExtents[2], maxZ = center[2] + halfExtents[2];

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            float cx = (verts[i][0] + verts[i + 1][0] + verts[i + 2][0]) / 3f;
            float cy = (verts[i][1] + verts[i + 1][1] + verts[i + 2][1]) / 3f;
            float cz = (verts[i][2] + verts[i + 1][2] + verts[i + 2][2]) / 3f;

            if (cx >= minX && cx <= maxX && cy >= minY && cy <= maxY && cz >= minZ && cz <= maxZ)
            {
                result.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
                result.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
                result.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
            }
        }
        return result;
    }

    /// <summary>
    /// Remove triangles whose centroid lies inside the given bounding box.
    /// Returns a new vertex list with those triangles removed.
    /// </summary>
    public static List<float[]> ExcludeBoundingBox(List<float[]> verts, float[] center, float[] halfExtents)
    {
        var result = new List<float[]>(verts.Count);
        float minX = center[0] - halfExtents[0], maxX = center[0] + halfExtents[0];
        float minY = center[1] - halfExtents[1], maxY = center[1] + halfExtents[1];
        float minZ = center[2] - halfExtents[2], maxZ = center[2] + halfExtents[2];

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            float cx = (verts[i][0] + verts[i + 1][0] + verts[i + 2][0]) / 3f;
            float cy = (verts[i][1] + verts[i + 1][1] + verts[i + 2][1]) / 3f;
            float cz = (verts[i][2] + verts[i + 1][2] + verts[i + 2][2]) / 3f;

            bool inside = cx >= minX && cx <= maxX && cy >= minY && cy <= maxY && cz >= minZ && cz <= maxZ;
            if (!inside)
            {
                result.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
                result.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 2][2] });
                result.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
            }
        }
        return result;
    }

    /// <summary>
    /// Compute the average Z of a set of vertices.
    /// </summary>
    public static float AverageZ(List<float[]> verts)
    {
        if (verts.Count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < verts.Count; i++) sum += verts[i][2];
        return (float)(sum / verts.Count);
    }
}
