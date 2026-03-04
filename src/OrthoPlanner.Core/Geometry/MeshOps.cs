using System;
using System.Collections.Generic;
using System.Linq;

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
                result.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
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

    /// <summary>
    /// Remove triangles whose centroid is within <paramref name="radiusMm"/> of any
    /// point on the arch spline. The spline is given as a dense list of 3D samples.
    /// </summary>
    public static List<float[]> SubtractByArchVolume(
        List<float[]> boneVerts, List<(double X, double Y, double Z)> splineSamples, float radiusMm)
    {
        // Build a KdTree from the spline samples for fast proximity queries
        var splineTree = new KdTree();
        var splinePoints = splineSamples.Select(
            p => new float[] { (float)p.X, (float)p.Y, (float)p.Z }).ToList();
        splineTree.Build(splinePoints);

        float radiusSq = radiusMm * radiusMm;
        var result = new List<float[]>(boneVerts.Count);

        for (int i = 0; i + 2 < boneVerts.Count; i += 3)
        {
            float cx = (boneVerts[i][0] + boneVerts[i + 1][0] + boneVerts[i + 2][0]) / 3f;
            float cy = (boneVerts[i][1] + boneVerts[i + 1][1] + boneVerts[i + 2][1]) / 3f;
            float cz = (boneVerts[i][2] + boneVerts[i + 1][2] + boneVerts[i + 2][2]) / 3f;

            var (_, distSq) = splineTree.FindNearest(cx, cy, cz);
            if (distSq > radiusSq)
            {
                result.Add(new float[] { boneVerts[i][0], boneVerts[i][1], boneVerts[i][2] });
                result.Add(new float[] { boneVerts[i + 1][0], boneVerts[i + 1][1], boneVerts[i + 1][2] });
                result.Add(new float[] { boneVerts[i + 2][0], boneVerts[i + 2][1], boneVerts[i + 2][2] });
            }
        }
        return result;
    }

    /// <summary>
    /// Find connected components in a triangle-soup mesh by flood-filling on shared vertex positions.
    /// Returns a list of vertex lists, sorted by size (largest first).
    /// </summary>
    public static List<List<float[]>> LabelConnectedComponents(List<float[]> verts)
    {
        int triCount = verts.Count / 3;
        if (triCount == 0) return new List<List<float[]>>();

        // Build adjacency: two triangles are adjacent if they share a vertex position.
        // Key: quantized position string → list of triangle indices that have a vertex at that position.
        var posToTris = new Dictionary<long, List<int>>();

        for (int t = 0; t < triCount; t++)
        {
            for (int v = 0; v < 3; v++)
            {
                var pt = verts[t * 3 + v];
                long key = QuantizePosition(pt[0], pt[1], pt[2]);
                if (!posToTris.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    posToTris[key] = list;
                }
                list.Add(t);
            }
        }

        // Build triangle adjacency graph
        var adj = new List<int>[triCount];
        for (int t = 0; t < triCount; t++) adj[t] = new List<int>();

        foreach (var group in posToTris.Values)
        {
            for (int a = 0; a < group.Count; a++)
                for (int b = a + 1; b < group.Count; b++)
                {
                    int ta = group[a], tb = group[b];
                    if (ta != tb)
                    {
                        adj[ta].Add(tb);
                        adj[tb].Add(ta);
                    }
                }
        }

        // BFS flood fill
        var visited = new bool[triCount];
        var components = new List<List<int>>();

        for (int t = 0; t < triCount; t++)
        {
            if (visited[t]) continue;
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(t);
            visited[t] = true;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                component.Add(cur);
                foreach (var nb in adj[cur])
                {
                    if (!visited[nb])
                    {
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
            components.Add(component);
        }

        // Sort by size (largest first) and convert to vertex lists
        components.Sort((a, b) => b.Count.CompareTo(a.Count));

        var result = new List<List<float[]>>();
        foreach (var comp in components)
        {
            var mesh = new List<float[]>(comp.Count * 3);
            foreach (int t in comp)
            {
                mesh.Add(verts[t * 3]);
                mesh.Add(verts[t * 3 + 1]);
                mesh.Add(verts[t * 3 + 2]);
            }
            result.Add(mesh);
        }
        return result;
    }

    /// <summary>
    /// Quantize a 3D position to a long key for hashing (0.01mm precision).
    /// </summary>
    private static long QuantizePosition(float x, float y, float z)
    {
        // Round to 0.01mm to handle floating-point noise
        long qx = (long)Math.Round(x * 100);
        long qy = (long)Math.Round(y * 100);
        long qz = (long)Math.Round(z * 100);
        // Pack into a single long with enough range
        return qx * 10000000000L + qy * 100000L + qz;
    }
}
