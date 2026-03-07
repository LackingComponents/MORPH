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
    public class PlaneInfo
    {
        public float Nx { get; set; }
        public float Ny { get; set; }
        public float Nz { get; set; }
        public float D { get; set; }
        public List<float[]> Triangles { get; set; } = new List<float[]>();
        public double Area { get; set; }
    }

    public static PlaneInfo? FindLargestFlatSection(List<float[]> verts)
    {
        var planes = new Dictionary<string, PlaneInfo>();
        PlaneInfo? bestPlane = null;
        double maxArea = -1;

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            float[] v0 = verts[i];
            float[] v1 = verts[i + 1];
            float[] v2 = verts[i + 2];

            float ux = v1[0] - v0[0]; float uy = v1[1] - v0[1]; float uz = v1[2] - v0[2];
            float wx = v2[0] - v0[0]; float wy = v2[1] - v0[1]; float wz = v2[2] - v0[2];

            float nx = uy * wz - uz * wy;
            float ny = uz * wx - ux * wz;
            float nz = ux * wy - uy * wx;

            float length = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 1e-6f) continue;
            nx /= length; ny /= length; nz /= length;

            float d = -(nx * v0[0] + ny * v0[1] + nz * v0[2]);
            double area = 0.5 * length;

            // Enforce consistent winding/normal direction (faces mostly pointing outwards)
            // But we just bin them by normal
            int qnx = (int)Math.Round(nx * 20); // 0.05 precision
            int qny = (int)Math.Round(ny * 20);
            int qnz = (int)Math.Round(nz * 20);
            int qd = (int)Math.Round(d * 5); // 0.2 mm precision

            string key = $"{qnx}_{qny}_{qnz}_{qd}";
            if (!planes.TryGetValue(key, out PlaneInfo? info))
            {
                info = new PlaneInfo { Nx = nx, Ny = ny, Nz = nz, D = d };
                planes[key] = info;
            }

            info.Triangles.Add(v0);
            info.Triangles.Add(v1);
            info.Triangles.Add(v2);
            info.Area += area;

            if (info.Area > maxArea)
            {
                maxArea = info.Area;
                bestPlane = info;
            }
        }

        return bestPlane;
    }

    public static List<float[]> CleanAndMergeDentalCast(List<float[]> boneVerts, List<float[]> castVerts, bool closeHoles = false)
    {
        var flatPlane = FindLargestFlatSection(castVerts);
        if (flatPlane == null || flatPlane.Triangles.Count == 0) return MergeVertices(boneVerts, castVerts);

        // Normalize the averaged normal of the best plane bin
        float nx = flatPlane.Nx, ny = flatPlane.Ny, nz = flatPlane.Nz;
        float d = flatPlane.D; // flatPlane.D is the distance from origin
        
        bool isSuperior = nz > 0; // base points UP -> Maxilla


        // Generalizing precise cut for both Jaws:
        // Extrusion direction = -N (Down for Maxilla, Up for Mandible)
        // This vector points directly from the bone interface into the teeth.
        float ex = -nx, ey = -ny, ez = -nz;
            
            // Find bounding limits to determine "posterior" direction.
            // Assuming the arch is U-shaped, the variance in X is wider at the posterior (molars)
            // than at the anterior (incisors).
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < flatPlane.Triangles.Count; i++)
            {
                float y = flatPlane.Triangles[i][1];
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            float midY = (minY + maxY) / 2f;
            float minX_low = float.MaxValue, maxX_low = float.MinValue;
            float minX_high = float.MaxValue, maxX_high = float.MinValue;

            for (int i = 0; i < flatPlane.Triangles.Count; i++)
            {
                float x = flatPlane.Triangles[i][0];
                float y = flatPlane.Triangles[i][1];
                if (y < midY)
                {
                    if (x < minX_low) minX_low = x;
                    if (x > maxX_low) maxX_low = x;
                }
                else
                {
                    if (x < minX_high) minX_high = x;
                    if (x > maxX_high) maxX_high = x;
                }
            }

            float spreadLow = maxX_low - minX_low;
            float spreadHigh = maxX_high - minX_high;

            // In our viewer, -Y or +Y could be posterior. The side with the wider X spread is the molar (posterior) side.
            bool posteriorIsLowY = spreadLow > spreadHigh;
            float posteriorLimitY = posteriorIsLowY ? minY : maxY;

            // Build dense KdTree of flat plane for 2D distance checks
            var planeSamples = new List<float[]>();
            Random rand = new Random(0);
            foreach (var t in Enumerable.Range(0, flatPlane.Triangles.Count / 3))
            {
                float[] v0 = flatPlane.Triangles[t * 3];
                float[] v1 = flatPlane.Triangles[t * 3 + 1];
                float[] v2 = flatPlane.Triangles[t * 3 + 2];

                float ux = v1[0] - v0[0]; float uy = v1[1] - v0[1]; float uz = v1[2] - v0[2];
                float wx = v2[0] - v0[0]; float wy = v2[1] - v0[1]; float wz = v2[2] - v0[2];
                float ax = uy * wz - uz * wy; float ay = uz * wx - ux * wz; float az = ux * wy - uy * wx;
                double area = 0.5 * Math.Sqrt(ax * ax + ay * ay + az * az);
                
                int numSamples = Math.Max(1, (int)(area / 2.0));
                for (int s = 0; s < numSamples; s++)
                {
                    float r1 = (float)rand.NextDouble();
                    float r2 = (float)rand.NextDouble();
                    if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                    float r0 = 1 - r1 - r2;
                    planeSamples.Add(new float[] {
                        r0 * v0[0] + r1 * v1[0] + r2 * v2[0],
                        r0 * v0[1] + r1 * v1[1] + r2 * v2[1],
                        r0 * v0[2] + r1 * v1[2] + r2 * v2[2]
                    });
                }
            }

            var tree = new KdTree();
            tree.Build(planeSamples);

            var remainingBone = new List<float[]>();
            var originalComponents = LabelConnectedComponents(boneVerts);

            foreach (var comp in originalComponents)
            {
                var compRemaining = new List<float[]>();

                for (int i = 0; i + 2 < comp.Count; i += 3)
                {
                    float cx = (comp[i][0] + comp[i + 1][0] + comp[i + 2][0]) / 3f;
                    float cy = (comp[i][1] + comp[i + 1][1] + comp[i + 2][1]) / 3f;
                    float cz = (comp[i][2] + comp[i + 1][2] + comp[i + 2][2]) / 3f;

                    // Project centroid to plane along normal 
                    float planeDist = nx * cx + ny * cy + nz * cz + d;
                    
                    // Height into the teeth space h:
                    float h = -planeDist; 

                    bool insideExtrusion = false;

                    // "Nothing outside the plane must be touched" -> Only cut if h > 0
                    if (h > 0.0f && h <= 50.0f)
                    {
                        float px = cx + h * nx;
                        float py = cy + h * ny;
                        float pz = cz + h * nz;

                        // "Limit the expansion behind the horseshoe shape at the level of the most posterior point"
                        bool isBehind = posteriorIsLowY ? (py < posteriorLimitY - 1.0f) : (py > posteriorLimitY + 1.0f);
                        
                        if (!isBehind)
                        {
                            var (_, distSq) = tree.FindNearest(px, py, pz);
                            // Tight footprint match without morphological dilation
                            if (distSq <= 2.25f)
                            {
                                insideExtrusion = true;
                            }
                        }
                    }

                    if (!insideExtrusion)
                    {
                        compRemaining.Add(comp[i]);
                        compRemaining.Add(comp[i + 1]);
                        compRemaining.Add(comp[i + 2]);
                    }
                }

                if (compRemaining.Count > 0)
                {
                    // "if possible, 'label' the unconnected components. the ones that appear after the boolean subtraction have to be removed."
                    // By running connected components strictly on the remaining pieces of THIS specific original component,
                    // we keep the largest piece (the main body of this component) and discard any smaller severed artifacts.
                    // Pre-existing independent cranium pieces are preserved safely!
                    var newComps = LabelConnectedComponents(compRemaining);
                    if (newComps.Count > 0)
                    {
                        remainingBone.AddRange(newComps[0]);
                    }
                }
            }

            // "Keep this plane, move it 0.1mm above and generate a bridging between the mandibular/maxillary bone model and teeth scan"
            // We use the flatPlane triangles, shift them 0.1mm into the teeth (-N), and flip winding.
            var bridgingPolys = new List<float[]>();
            float shift = 0.1f;
            float bx = ex * shift, by = ey * shift, bz = ez * shift;

            for (int i = 0; i + 2 < flatPlane.Triangles.Count; i += 3)
            {
                var v0 = flatPlane.Triangles[i];
                var v1 = flatPlane.Triangles[i + 1];
                var v2 = flatPlane.Triangles[i + 2];

                float[] p0 = new float[] { v0[0] + bx, v0[1] + by, v0[2] + bz };
                float[] p1 = new float[] { v1[0] + bx, v1[1] + by, v1[2] + bz };
                float[] p2 = new float[] { v2[0] + bx, v2[1] + by, v2[2] + bz };

                // Flip winding to cap the bone (face towards teeth)
                bridgingPolys.Add(p0);
                bridgingPolys.Add(p2);
                bridgingPolys.Add(p1);
            }
            
            // Cut the teeth mesh (castVerts) so that nothing exists below the 0.1mm shifted plane.
            // Distance above the original plane is h = -planeDist.
            // For the new plane shifted 0.1mm into the teeth space, its height is h = 0.1f.
            // We want to KEEP triangles in the teeth mesh that are ABOVE this new plane (h > 0.1f).
            var cutCastVerts = new List<float[]>();
            for (int i = 0; i + 2 < castVerts.Count; i += 3)
            {
                float cx = (castVerts[i][0] + castVerts[i + 1][0] + castVerts[i + 2][0]) / 3f;
                float cy = (castVerts[i][1] + castVerts[i + 1][1] + castVerts[i + 2][1]) / 3f;
                float cz = (castVerts[i][2] + castVerts[i + 1][2] + castVerts[i + 2][2]) / 3f;

                float planeDist = nx * cx + ny * cy + nz * cz + d;
                float h = -planeDist; // Height into the teeth space

                // Keep cast triangles that are at least 0.1mm into the teeth space 
                // (or slightly less, to intersect with the bridging cap. The cap is at h=0.1.
                // We keep h >= 0.05f to ensure they fuse nicely and don't leave another gap).
                if (h >= 0.05f)
                {
                    cutCastVerts.Add(castVerts[i]);
                    cutCastVerts.Add(castVerts[i + 1]);
                    cutCastVerts.Add(castVerts[i + 2]);
                }
            }

            // 1. Strongly prevent teeth specks: The dental scan is normally a single watertight shell.
            // After cutting at h=0.05, we might have severed tiny tips of the roots.
            // We strictly keep ONLY the largest connected component of the cut cast to destroy all floating teeth fragments.
            var castComps = LabelConnectedComponents(cutCastVerts);
            if (castComps.Count > 0)
            {
                cutCastVerts = castComps[0];
            }

            var resultBone = MergeVertices(remainingBone, bridgingPolys);
            var finalSurgicalModel = MergeVertices(resultBone, cutCastVerts);

            // 2. Optional: Close all topological holes in the final merged surgical model to make it perfectly watertight
            if (closeHoles)
            {
                finalSurgicalModel = CloseHoles(finalSurgicalModel);
            }

            return finalSurgicalModel;
    }

    /// <summary>
    /// Finds all boundary edges in a triangle soup mesh and seals them using centroid-fan triangulation.
    /// This makes the mesh watertight.
    /// </summary>
    public static List<float[]> CloseHoles(List<float[]> verts)
    {
        var edgeCounts = new Dictionary<(long, long), int>();
        var edgeToVert = new Dictionary<long, float[]>();

        // Extract half-edges
        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            long v0 = QuantizePosition(verts[i][0], verts[i][1], verts[i][2]);
            long v1 = QuantizePosition(verts[i + 1][0], verts[i + 1][1], verts[i + 1][2]);
            long v2 = QuantizePosition(verts[i + 2][0], verts[i + 2][1], verts[i + 2][2]);

            edgeToVert[v0] = verts[i];
            edgeToVert[v1] = verts[i + 1];
            edgeToVert[v2] = verts[i + 2];

            AddHalfEdge(edgeCounts, v0, v1);
            AddHalfEdge(edgeCounts, v1, v2);
            AddHalfEdge(edgeCounts, v2, v0);
        }

        // Identify boundary edges: an edge is a boundary if it only exists in one direction
        var bounds = new Dictionary<long, long>();
        foreach (var kv in edgeCounts)
        {
            var forward = kv.Key;
            var backward = (forward.Item2, forward.Item1);
            if (!edgeCounts.ContainsKey(backward))
            {
                bounds[forward.Item1] = forward.Item2;
            }
        }

        var result = new List<float[]>();
        var visited = new HashSet<long>();

        // Trace and triangulate loops
        foreach (var startNode in bounds.Keys)
        {
            if (visited.Contains(startNode)) continue;

            var loop = new List<long>();
            long curr = startNode;
            while (true)
            {
                visited.Add(curr);
                loop.Add(curr);
                if (bounds.TryGetValue(curr, out long next))
                {
                    if (next == startNode) break; // closed loop
                    if (visited.Contains(next)) break; // fractured loop, still try to close what we have
                    curr = next;
                }
                else break;
            }

            if (loop.Count >= 3)
            {
                // Compute centroid
                float cx = 0, cy = 0, cz = 0;
                foreach (var v in loop)
                {
                    var pt = edgeToVert[v];
                    cx += pt[0]; cy += pt[1]; cz += pt[2];
                }
                cx /= loop.Count;
                cy /= loop.Count;
                cz /= loop.Count;
                float[] centroid = new float[] { cx, cy, cz };

                for (int i = 0; i < loop.Count; i++)
                {
                    long v0 = loop[i];
                    long v1 = loop[(i + 1) % loop.Count];
                    
                    // CCW winding to cap the hole facing outward: (v1, v0, centroid)
                    result.Add(edgeToVert[v1]);
                    result.Add(edgeToVert[v0]);
                    result.Add(centroid);
                }
            }
        }

        // Combine new caps with original mesh
        result.AddRange(verts);
        return result;
    }

    private static void AddHalfEdge(Dictionary<(long, long), int> edgeCounts, long a, long b)
    {
        var key = (a, b);
        if (edgeCounts.ContainsKey(key)) edgeCounts[key]++;
        else edgeCounts[key] = 1;
    }

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
