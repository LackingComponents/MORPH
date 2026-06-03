using System;
using System.Collections.Generic;
using System.Linq;
using g3;

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
                // Prevent massive ray-burst artifacts on large structural boundaries (like the entire mandible base)
                // Centroid-fan is only geometrically sound for relatively small, convex topological holes.
                if (loop.Count > 300) continue;

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
    /// Split a triangle-soup mesh into two parts using a custom Polyplane surface.
    /// Triangles with centroid "above" the polyplane go to 'above', everything else to 'below'.
    /// </summary>
    public static (List<float[]> Above, List<float[]> Below) SplitByPolyplane(List<float[]> verts, Polyplane polyplane)
    {
        var above = new List<float[]>();
        var below = new List<float[]>();

        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            float cx = (verts[i][0] + verts[i + 1][0] + verts[i + 2][0]) / 3f;
            float cy = (verts[i][1] + verts[i + 1][1] + verts[i + 2][1]) / 3f;
            float cz = (verts[i][2] + verts[i + 1][2] + verts[i + 2][2]) / 3f;

            double[] centroid = new double[] { cx, cy, cz };
            int? aboveResult = polyplane.IsAbove(centroid);

            // null = centroid is outside the finite plane's influence zone.
            // In a finite-plane cut, such triangles are NOT split — they belong to BOTH halves
            // (the cut doesn't reach them). We add them to both so neither segment is missing geometry.
            if (aboveResult == null)
            {
                above.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
                above.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
                above.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
                below.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
                below.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
                below.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
            }
            else
            {
                var target = (aboveResult.Value >= 0) ? above : below;
                target.Add(new float[] { verts[i][0], verts[i][1], verts[i][2] });
                target.Add(new float[] { verts[i + 1][0], verts[i + 1][1], verts[i + 1][2] });
                target.Add(new float[] { verts[i + 2][0], verts[i + 2][1], verts[i + 2][2] });
            }
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

    // ─────────────────────────────────────────────────────────────────────────
    //  TRUE GEOMETRY SLICING  (geometry3Sharp – MeshPlaneCut)
    //
    //  Triangles that straddle the cutting plane are split at the intersection
    //  line; new vertices are inserted so both halves are proper closed meshes.
    //  When capEnds = true the resulting open boundary loops are filled with
    //  flat triangulated caps (the osteotomy cut face).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Carries one connected component produced by <see cref="TrueSliceByMultiplePlanes"/>.
    /// </summary>
    public class PlaneSliceComponent
    {
        /// <summary>Triangle soup for this component.</summary>
        public List<float[]> Mesh { get; set; } = new();
        /// <summary>
        /// For each input plane: <c>true</c> when the component centroid lies on
        /// the positive side (nx·x + ny·y + nz·z + d ≥ 0).
        /// </summary>
        public bool[] AbovePlanes { get; set; } = Array.Empty<bool>();
    }

    // ── Conversion helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Converts a triangle-soup (every 3 consecutive float[3] = one triangle)
    /// to an indexed <see cref="DMesh3"/>. Vertices within 0.01 mm are merged.
    /// </summary>
    public static DMesh3 ToIndexedMesh(List<float[]> soup)
    {
        var dm   = new DMesh3();
        var vmap = new Dictionary<(int, int, int), int>();

        int GetV(float x, float y, float z)
        {
            var key = ((int)Math.Round(x * 100), (int)Math.Round(y * 100), (int)Math.Round(z * 100));
            if (!vmap.TryGetValue(key, out int vi))
            {
                vi = dm.AppendVertex(new Vector3d(x, y, z));
                vmap[key] = vi;
            }
            return vi;
        }

        for (int i = 0; i + 2 < soup.Count; i += 3)
        {
            int a = GetV(soup[i][0],   soup[i][1],   soup[i][2]);
            int b = GetV(soup[i+1][0], soup[i+1][1], soup[i+1][2]);
            int c = GetV(soup[i+2][0], soup[i+2][1], soup[i+2][2]);
            if (a != b && b != c && a != c)
                dm.AppendTriangle(a, b, c);
        }
        return dm;
    }

    /// <summary>Converts a <see cref="DMesh3"/> back to flat triangle-soup.</summary>
    public static List<float[]> ToTriangleSoup(DMesh3 dm)
    {
        var result = new List<float[]>(dm.TriangleCount * 3);
        foreach (int tid in dm.TriangleIndices())
        {
            var tri = dm.GetTriangle(tid);
            var va  = dm.GetVertex(tri.a);
            var vb  = dm.GetVertex(tri.b);
            var vc  = dm.GetVertex(tri.c);
            result.Add(new float[] { (float)va.x, (float)va.y, (float)va.z });
            result.Add(new float[] { (float)vb.x, (float)vb.y, (float)vb.z });
            result.Add(new float[] { (float)vc.x, (float)vc.y, (float)vc.z });
        }
        return result;
    }

    // ── Polyplane true-slice (BFS-classification + edge splitting) ────────────

    /// <summary>
    /// Splits <paramref name="soup"/> into two meshes ("above" and "below") by the
    /// given <paramref name="polyplane"/>.  Unlike the centroid-only BFS approach,
    /// any mesh triangle whose edges cross the polyplane is split at the exact
    /// Möller-Trumbore intersection points, producing a clean straight edge.
    /// Both halves are always returned; the open boundary loops are capped with
    /// flat fan-triangulations that approximate the polyplane surface.
    /// </summary>
    /// <param name="soup">Input triangle soup.</param>
    /// <param name="polyplane">The Polyplane whose mesh is used as the cutting surface.</param>
    /// <param name="aboveReference">A world-space point known to be on the "above" side
    ///   (e.g. highest-Z centroid of the mesh). Used to orient the parity test.</param>
    /// <param name="capEnds">When true, open boundary loops are fan-filled.</param>
    /// <param name="secondaryPlane">Optional. When set, a vertex is "above" only if it's
    ///   above both the primary polyplane AND this secondary plane. Used for Y-cut
    ///   (arm + stem) compound classification.</param>
    public static (List<float[]> Above, List<float[]> Below) TrueSliceByPolyplane(
        List<float[]> soup,
        Polyplane polyplane,
        double[] aboveReference,
        bool capEnds = true,
        Polyplane? secondaryPlane = null)
    {
        var above = new List<float[]>();
        var below = new List<float[]>();

        // ── 1. Classify every vertex as above (true) or below (false) ─────────
        // For single-plane polyplanes (sagittal, Y-cut arms): use plane equation
        // directly — Ax+By+Cz+D, compare sign with reference. O(1), exact, infinite.
        // For multi-panel polyplanes (LeFort horizontal): use parity ray-casting.
        int nTri = soup.Count / 3;
        bool usePlaneEq = polyplane.IsSinglePlane;

        // Cache vertex-side classification; use quantised key to avoid re-testing
        // shared vertices multiple times.
        var vertSide = new Dictionary<string, bool>(nTri * 2);
        bool VertexAbove(float[] v)
        {
            string key = $"{Math.Round(v[0],2)},{Math.Round(v[1],2)},{Math.Round(v[2],2)}";
            if (!vertSide.TryGetValue(key, out bool side))
            {
                if (usePlaneEq)
                    side = polyplane.SameSideByPlaneEq(v, aboveReference);
                else
                {
                    double[] vd = { v[0], v[1], v[2] };
                    side = polyplane.SameSideAs(vd, aboveReference);
                }
                // Compound: must also be above secondary plane
                if (side && secondaryPlane != null)
                    side = secondaryPlane.SameSideByPlaneEq(v, aboveReference);
                vertSide[key] = side;
            }
            return side;
        }

        // Directed cut edges collected for loop-based capping.
        // Convention: each entry (A, B) is the boundary edge of the "above" half
        // at that cut — the directed segment as it appears in the above mesh boundary.
        var cutEdges = new List<(float[] A, float[] B)>();

        // ── 2. Process each triangle ──────────────────────────────────────────
        for (int i = 0; i < nTri; i++)
        {
            float[] v0 = soup[i * 3];
            float[] v1 = soup[i * 3 + 1];
            float[] v2 = soup[i * 3 + 2];

            bool s0 = VertexAbove(v0);
            bool s1 = VertexAbove(v1);
            bool s2 = VertexAbove(v2);

            int aboveCount = (s0 ? 1 : 0) + (s1 ? 1 : 0) + (s2 ? 1 : 0);

            if (aboveCount == 3) { above.Add(v0); above.Add(v1); above.Add(v2); continue; }
            if (aboveCount == 0) { below.Add(v0); below.Add(v1); below.Add(v2); continue; }

            // ── Straddling triangle: split at polyplane crossing ───────────────
            float[]? p01 = EdgeCross(polyplane, v0, v1, s0 != s1);
            float[]? p12 = EdgeCross(polyplane, v1, v2, s1 != s2);
            float[]? p20 = EdgeCross(polyplane, v2, v0, s2 != s0);

            // Distribute sub-triangles and record the directed cut edge
            SplitStraddlingTriangle(v0, s0, v1, s1, v2, s2, p01, p12, p20, above, below, cutEdges);
        }

        // -- 3. Cap: subdivide polyplane, PIP-test against open boundary loops --
        CapLog($"TrueSliceByPolyplane: soup={soup.Count/3} tris, above={above.Count/3}, below={below.Count/3}, cutEdges={cutEdges.Count}, capEnds={capEnds}");
        if (capEnds && cutEdges.Count >= 2)
            CapFromCutSurface(soup, cutEdges, polyplane, above, below);

        return (above, below);
    }

    // ── Triangle splitting helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns the intersection point of segment (a→b) with the polyplane,
    /// or null if the edge does not cross (crossEdge=false) or no hit is found.
    /// For single-plane polyplanes, uses the plane equation directly (infinite plane).
    /// </summary>
    private static float[]? EdgeCross(Polyplane pp, float[] a, float[] b, bool crossEdge)
    {
        if (!crossEdge) return null;

        double t;
        if (pp.IsSinglePlane)
        {
            // Plane equation intersection: t = -(N·A + D) / (N·(B-A))
            t = pp.PlaneIntersectT(a, b);
        }
        else
        {
            double[] ad = { a[0], a[1], a[2] };
            double[] bd = { b[0], b[1], b[2] };
            t = pp.SegmentIntersectT(ad, bd);
        }

        if (double.IsNaN(t) || t < 0.0 || t > 1.0)
        {
            // Last resort fallback: midpoint
            return new float[] { (a[0]+b[0])*0.5f, (a[1]+b[1])*0.5f, (a[2]+b[2])*0.5f };
        }
        return new float[]
        {
            (float)(a[0] + t*(b[0]-a[0])),
            (float)(a[1] + t*(b[1]-a[1])),
            (float)(a[2] + t*(b[2]-a[2]))
        };
    }

    /// <summary>
    /// Splits a straddling triangle into 2 or 3 sub-triangles and routes them
    /// to the correct (above/below) output lists.
    /// p01, p12, p20 are the edge-crossing points on edges (v0-v1), (v1-v2), (v2-v0).
    /// </summary>
    /// <summary>
    /// Splits a straddling triangle into sub-triangles routed to the correct halves,
    /// and records the directed cut edge (boundary of the "above" half) in cutEdges.
    /// </summary>
    private static void SplitStraddlingTriangle(
        float[] v0, bool s0, float[] v1, bool s1, float[] v2, bool s2,
        float[]? p01, float[]? p12, float[]? p20,
        List<float[]> above, List<float[]> below,
        List<(float[], float[])> cutEdges)
    {
        if (s0 == s1 && s0 != s2)
        {
            // v0, v1 same side; v2 alone  → crossing points p12, p20
            if (p12 == null || p20 == null) { Fallback(v0,s0,v1,s1,v2,s2,above,below); return; }
            var same  = s0 ? above : below;
            var other = s0 ? below : above;
            same.Add(v0);  same.Add(v1);  same.Add(p12);
            same.Add(v0);  same.Add(p12); same.Add(p20);
            other.Add(v2); other.Add(p20); other.Add(p12);
            // Cut edge as boundary of "above": in same=(above) triangles, p12→p20 is the boundary edge;
            // for other=(above) triangles, p20→p12 is the boundary edge.
            // Record oriented as boundary of above:
            if (s0) cutEdges.Add((p12, p20));
            else     cutEdges.Add((p20, p12));
        }
        else if (s1 == s2 && s1 != s0)
        {
            // v1, v2 same side; v0 alone  → crossing points p01, p20
            if (p01 == null || p20 == null) { Fallback(v0,s0,v1,s1,v2,s2,above,below); return; }
            var same  = s1 ? above : below;
            var other = s1 ? below : above;
            same.Add(v1);  same.Add(v2);  same.Add(p20);
            same.Add(v1);  same.Add(p20); same.Add(p01);
            other.Add(v0); other.Add(p01); other.Add(p20);
            if (s1) cutEdges.Add((p20, p01));
            else     cutEdges.Add((p01, p20));
        }
        else if (s0 == s2 && s0 != s1)
        {
            // v0, v2 same side; v1 alone  → crossing points p01, p12
            if (p01 == null || p12 == null) { Fallback(v0,s0,v1,s1,v2,s2,above,below); return; }
            var same  = s0 ? above : below;
            var other = s0 ? below : above;
            same.Add(v0);  same.Add(p01); same.Add(p12);
            same.Add(v0);  same.Add(p12); same.Add(v2);
            other.Add(v1); other.Add(p12); other.Add(p01);
            if (s0) cutEdges.Add((p01, p12));
            else     cutEdges.Add((p12, p01));
        }
        else
        {
            Fallback(v0, s0, v1, s1, v2, s2, above, below);
        }
    }

    private static void Fallback(
        float[] v0, bool s0, float[] v1, bool s1, float[] v2, bool s2,
        List<float[]> above, List<float[]> below)
    {
        int n = (s0?1:0)+(s1?1:0)+(s2?1:0);
        var t = n >= 2 ? above : below;
        t.Add(v0); t.Add(v1); t.Add(v2);
    }

    // -- Cap fill: polyplane surface clipped to bone interior -----------------

    private static readonly string _capLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "cap_debug.log");
    private static void CapLog(string msg)
    {
        try { System.IO.File.AppendAllText(_capLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
        catch { }
    }

    /// <summary>
    /// Caps the open cut boundary by using the cutting polyplane surface itself:
    ///   1. Builds a spatial index (AABB tree) from the original unsplit bone mesh.
    ///   2. Subdivides polyplane triangles to ~1mm.
    ///   3. Keeps only sub-triangles whose centroid is inside the bone volume.
    ///   4. For boundary cap vertices (outside the bone), snaps them to the
    ///      nearest point on the cut-edge boundary so the cap perimeter exactly
    ///      coincides with the bone cut edge — closing the mesh locally.
    ///   5. Adds cap triangles to both halves with opposing windings.
    /// </summary>
    private static void CapFromCutSurface(
        List<float[]> originalSoup,
        List<(float[] A, float[] B)> cutEdges,
        Polyplane polyplane,
        List<float[]> above,
        List<float[]> below)
    {
        var pverts = polyplane.MeshVertices;
        CapLog($"CapFromCutSurface ENTER: originalSoup={originalSoup.Count/3} tris, cutEdges={cutEdges.Count}, polyplane tris={pverts.Count/3}");
        if (pverts.Count < 3) { CapLog("EARLY EXIT: pverts < 3"); return; }

        // -- 1. Build AABB tree from original mesh for inside/outside queries --
        DMesh3 boneMesh = ToIndexedMesh(originalSoup);
        CapLog($"  ToIndexedMesh: {boneMesh.VertexCount} verts, {boneMesh.TriangleCount} tris");
        if (boneMesh.TriangleCount < 4) { CapLog("EARLY EXIT: boneMesh < 4 tris"); return; }
        var tree = new DMeshAABBTree3(boneMesh, true);

        // -- 1b. Pre-filter: skip polyplane triangles outside bone AABB --------
        // The polyplane often extends far beyond the bone (100mm+ extensions for
        // robust SameSideAs). No point subdividing triangles that will all fail
        // IsInside anyway.
        float bxMin=float.MaxValue,bxMax=float.MinValue;
        float byMin=float.MaxValue,byMax=float.MinValue;
        float bzMin=float.MaxValue,bzMax=float.MinValue;
        foreach (var v in originalSoup)
        {
            if(v[0]<bxMin)bxMin=v[0]; if(v[0]>bxMax)bxMax=v[0];
            if(v[1]<byMin)byMin=v[1]; if(v[1]>byMax)byMax=v[1];
            if(v[2]<bzMin)bzMin=v[2]; if(v[2]>bzMax)bzMax=v[2];
        }
        // Pad by 1mm to avoid clipping boundary tiles
        bxMin-=1f; byMin-=1f; bzMin-=1f;
        bxMax+=1f; byMax+=1f; bzMax+=1f;

        var filteredPverts = new List<float[]>();
        for (int i = 0; i + 2 < pverts.Count; i += 3)
        {
            var ta=pverts[i]; var tb=pverts[i+1]; var tc=pverts[i+2];
            float txMin=Math.Min(ta[0],Math.Min(tb[0],tc[0]));
            float txMax=Math.Max(ta[0],Math.Max(tb[0],tc[0]));
            float tyMin=Math.Min(ta[1],Math.Min(tb[1],tc[1]));
            float tyMax=Math.Max(ta[1],Math.Max(tb[1],tc[1]));
            float tzMin=Math.Min(ta[2],Math.Min(tb[2],tc[2]));
            float tzMax=Math.Max(ta[2],Math.Max(tb[2],tc[2]));
            if (txMax<bxMin||txMin>bxMax||tyMax<byMin||tyMin>byMax||tzMax<bzMin||tzMin>bzMax)
                continue; // triangle entirely outside bone bbox — skip
            filteredPverts.Add(ta); filteredPverts.Add(tb); filteredPverts.Add(tc);
        }
        CapLog($"  AABB filter: {pverts.Count/3} input tris -> {filteredPverts.Count/3} kept, boneBBox=[{bxMin:F1}..{bxMax:F1}]x[{byMin:F1}..{byMax:F1}]x[{bzMin:F1}..{bzMax:F1}]");
        if (filteredPverts.Count < 3) { CapLog("EARLY EXIT: filteredPverts < 3"); return; }

        // -- 1c. Subdivide filtered polyplane triangles to ~0.5mm ---------------
        static float EdgeLen(float[] a, float[] b)
        { float dx=a[0]-b[0],dy=a[1]-b[1],dz=a[2]-b[2]; return MathF.Sqrt(dx*dx+dy*dy+dz*dz); }
        static float[] Mid(float[] a, float[] b)
            => new[]{ (a[0]+b[0])*.5f,(a[1]+b[1])*.5f,(a[2]+b[2])*.5f };

        var subTris = new List<float[]>(filteredPverts);
        for (int level = 0; level < 12; level++)
        {
            bool any = false;
            var next = new List<float[]>(subTris.Count * 4);
            for (int i = 0; i + 2 < subTris.Count; i += 3)
            {
                var a=subTris[i]; var b=subTris[i+1]; var c=subTris[i+2];
                float me=Math.Max(EdgeLen(a,b),Math.Max(EdgeLen(b,c),EdgeLen(c,a)));
                if (me > 0.5f)
                {
                    any=true;
                    var mab=Mid(a,b); var mbc=Mid(b,c); var mca=Mid(c,a);
                    next.AddRange(new[]{ a,mab,mca }); next.AddRange(new[]{ mab,b,mbc });
                    next.AddRange(new[]{ mca,mbc,c }); next.AddRange(new[]{ mab,mbc,mca });
                }
                else { next.Add(a); next.Add(b); next.Add(c); }
            }
            subTris = next;
            if (!any) break;
        }
        CapLog($"  Subdivision: {filteredPverts.Count/3} -> {subTris.Count/3} tiles");

        // -- 2. Collect accepted tiles ----------------------------------------
        // Try AABB IsInside first (works for watertight solid meshes).
        // Fall back to 2D PIP against cutEdges if IsInside returns 0 tiles
        // (happens for thin-shell meshes like the capped LeFort maxilla).
        var accepted = new List<(float[] a, float[] b, float[] c)>();
        for (int i = 0; i + 2 < subTris.Count; i += 3)
        {
            var pa = subTris[i]; var pb = subTris[i+1]; var pc = subTris[i+2];
            float cx = (pa[0]+pb[0]+pc[0]) / 3f;
            float cy = (pa[1]+pb[1]+pc[1]) / 3f;
            float cz = (pa[2]+pb[2]+pc[2]) / 3f;
            if (tree.IsInside(new Vector3d(cx, cy, cz)))
                accepted.Add((pa, pb, pc));
        }
        CapLog($"  IsInside accepted: {accepted.Count} tiles out of {subTris.Count/3}");

        // -- 2b. Fallback: 2D PIP against cutEdges projected onto cutting plane --
        if (accepted.Count == 0 && cutEdges.Count >= 3)
        {
            // Compute cutting plane normal from first polyplane triangle
            float[] pn0 = pverts[0], pn1 = pverts[1], pn2 = pverts[2];
            float nx = (pn1[1]-pn0[1])*(pn2[2]-pn0[2]) - (pn1[2]-pn0[2])*(pn2[1]-pn0[1]);
            float ny = (pn1[2]-pn0[2])*(pn2[0]-pn0[0]) - (pn1[0]-pn0[0])*(pn2[2]-pn0[2]);
            float nz = (pn1[0]-pn0[0])*(pn2[1]-pn0[1]) - (pn1[1]-pn0[1])*(pn2[0]-pn0[0]);
            float nl = MathF.Sqrt(nx*nx+ny*ny+nz*nz);
            if (nl > 1e-6f)
            {
                nx/=nl; ny/=nl; nz/=nl;
                // Two orthonormal axes on the plane
                float ux, uy, uz;
                if (MathF.Abs(nx) < 0.9f) { ux=0; uy=nz; uz=-ny; }
                else                       { ux=-nz; uy=0; uz=nx; }
                float ul=MathF.Sqrt(ux*ux+uy*uy+uz*uz); ux/=ul; uy/=ul; uz/=ul;
                float vx=ny*uz-nz*uy, vy=nz*ux-nx*uz, vz=nx*uy-ny*ux;

                // Project cut-edge endpoints
                var edgePts = new (float au, float av, float bu, float bv)[cutEdges.Count];
                for (int i = 0; i < cutEdges.Count; i++)
                {
                    var (ea, eb) = cutEdges[i];
                    edgePts[i] = (ea[0]*ux+ea[1]*uy+ea[2]*uz, ea[0]*vx+ea[1]*vy+ea[2]*vz,
                                  eb[0]*ux+eb[1]*uy+eb[2]*uz, eb[0]*vx+eb[1]*vy+eb[2]*vz);
                }

                // Winding-number PIP
                bool PIP(float pu, float pv)
                {
                    int w = 0;
                    foreach (var (au,av,bu,bv) in edgePts)
                    {
                        if (av <= pv)
                        { if (bv > pv && (bu-au)*(pv-av)-(bv-av)*(pu-au) > 0) w++; }
                        else
                        { if (bv <= pv && (bu-au)*(pv-av)-(bv-av)*(pu-au) < 0) w--; }
                    }
                    return w != 0;
                }

                for (int i = 0; i + 2 < subTris.Count; i += 3)
                {
                    var pa = subTris[i]; var pb = subTris[i+1]; var pc = subTris[i+2];
                    float cx = (pa[0]+pb[0]+pc[0]) / 3f;
                    float cy = (pa[1]+pb[1]+pc[1]) / 3f;
                    float cz = (pa[2]+pb[2]+pc[2]) / 3f;
                    float cu = cx*ux+cy*uy+cz*uz, cv = cx*vx+cy*vy+cz*vz;
                    if (PIP(cu, cv))
                        accepted.Add((pa, pb, pc));
                }
            }
        CapLog($"  After PIP fallback: {accepted.Count} tiles");
        }
        if (accepted.Count == 0) { CapLog("EARLY EXIT: 0 accepted tiles after both stages"); return; }

        // -- 3. Identify boundary cap vertices via edge adjacency -------------
        static string VK(float[] p) => $"{p[0]:F4},{p[1]:F4},{p[2]:F4}";
        static string EKE(float[] a, float[] b)
        { string ka=VK(a),kb=VK(b); return string.CompareOrdinal(ka,kb)<0 ? ka+"|"+kb : kb+"|"+ka; }

        var edgeCnt = new Dictionary<string, int>();
        foreach (var (a, b, c) in accepted)
        {
            void Inc(float[] x, float[] y) {
                string k=EKE(x,y); edgeCnt[k]=edgeCnt.GetValueOrDefault(k)+1; }
            Inc(a,b); Inc(b,c); Inc(c,a);
        }
        var bndVerts = new HashSet<string>();
        foreach (var (a, b, c) in accepted)
        {
            void Chk(float[] x, float[] y) {
                if (edgeCnt.GetValueOrDefault(EKE(x,y))==1)
                { bndVerts.Add(VK(x)); bndVerts.Add(VK(y)); } }
            Chk(a,b); Chk(b,c); Chk(c,a);
        }

        // -- 4. Weld boundary vertices to nearest cut-edge endpoint -----------
        // Collect unique bone cut-edge endpoint positions
        var ceVerts = new List<float[]>();
        var ceSet   = new HashSet<string>();
        foreach (var (ea, eb) in cutEdges)
        {
            if (ceSet.Add(VK(ea))) ceVerts.Add(ea);
            if (ceSet.Add(VK(eb))) ceVerts.Add(eb);
        }

        // Build weld map: boundary vertex key → target endpoint coordinates
        var weldMap = new Dictionary<string, float[]>();
        float weldR2 = 0.6f * 0.6f;
        foreach (var bk in bndVerts)
        {
            var parts = bk.Split(',');
            float vx=float.Parse(parts[0]), vy=float.Parse(parts[1]), vz=float.Parse(parts[2]);
            float bestD2 = weldR2;
            float[]? bestP = null;
            foreach (var ep in ceVerts)
            {
                float dx=vx-ep[0], dy=vy-ep[1], dz=vz-ep[2];
                float d2=dx*dx+dy*dy+dz*dz;
                if (d2 < bestD2) { bestD2=d2; bestP=ep; }
            }
            if (bestP != null)
                weldMap[bk] = new float[]{ bestP[0], bestP[1], bestP[2] };
        }

        // -- 5. Emit cap tiles — every vertex is a fresh clone ----------------
        // This guarantees zero shared references with bone mesh in above/below.
        float[] EmitV(float[] v)
        {
            string k = VK(v);
            if (weldMap.TryGetValue(k, out var w))
                return new float[]{ w[0], w[1], w[2] };
            return new float[]{ v[0], v[1], v[2] };
        }

        foreach (var (a, b, c) in accepted)
        {
            var ea = EmitV(a); var eb = EmitV(b); var ec = EmitV(c);
            // above cap: reversed winding
            above.Add(ea); above.Add(ec); above.Add(eb);
            // below cap: normal winding (separate clones)
            below.Add(new[]{ea[0],ea[1],ea[2]});
            below.Add(new[]{eb[0],eb[1],eb[2]});
            below.Add(new[]{ec[0],ec[1],ec[2]});
        }
    }


    // ── BFS-vertex-map-guided true slice ─────────────────────────────────────────

    /// <summary>
    /// Performs a true mesh split guided by a pre-computed per-vertex side map
    /// (typically derived from BFS triangle classification, e.g. BSSO ramus/body).
    /// Unlike plain triangle sorting, straddling triangles are split exactly at the
    /// polyplane intersection via <see cref="EdgeCross"/>, producing clean kerf edges.
    /// The open cut boundary is then capped with <see cref="CapFromPolyplaneSubdivided"/>.
    /// </summary>
    /// <param name="soup">Input mesh as flat triangle soup.</param>
    /// <param name="vertexSideMap">
    ///   Map from quantised vertex key ("{x},{y},{z}" at 1 dp) to side:
    ///   <c>true</c> = proximal/above, <c>false</c> = distal/below.
    /// </param>
    /// <param name="polyplane">Composite kerf polyplane for intersection + cap.</param>
    /// <param name="capEnds">When true, caps the open cut boundaries.</param>
    public static (List<float[]> Prox, List<float[]> Dist) SliceByVertexMap(
        List<float[]> soup,
        Dictionary<string, bool> vertexSideMap,
        Polyplane polyplane,
        bool capEnds = true)
    {
        var prox     = new List<float[]>();
        var dist     = new List<float[]>();
        var cutEdges = new List<(float[] A, float[] B)>();

        static string VK2(float[] p)
            => $"{Math.Round(p[0],1)},{Math.Round(p[1],1)},{Math.Round(p[2],1)}";
        bool VertAbove(float[] v)
            => vertexSideMap.TryGetValue(VK2(v), out bool s) ? s : false;

        int nTri = soup.Count / 3;
        for (int i = 0; i < nTri; i++)
        {
            var v0=soup[i*3]; var v1=soup[i*3+1]; var v2=soup[i*3+2];
            bool s0=VertAbove(v0), s1=VertAbove(v1), s2=VertAbove(v2);
            int ac=(s0?1:0)+(s1?1:0)+(s2?1:0);
            if (ac==3) { prox.Add(v0); prox.Add(v1); prox.Add(v2); continue; }
            if (ac==0) { dist.Add(v0); dist.Add(v1); dist.Add(v2); continue; }
            float[]? p01=EdgeCross(polyplane,v0,v1,s0!=s1);
            float[]? p12=EdgeCross(polyplane,v1,v2,s1!=s2);
            float[]? p20=EdgeCross(polyplane,v2,v0,s2!=s0);
            SplitStraddlingTriangle(v0,s0,v1,s1,v2,s2,p01,p12,p20,prox,dist,cutEdges);
        }

        if (capEnds && cutEdges.Count >= 2)
            CapFromCutSurface(soup, cutEdges, polyplane, prox, dist);

        return (prox, dist);
    }

    // ── Public slicing API ────────────────────────────────────────────────────

    /// <summary>
    /// Performs a single true plane cut on the input mesh using geometry3Sharp's
    /// <c>MeshPlaneCut</c>. Triangles straddling the plane are split at their
    /// intersection edges; open boundary loops are optionally capped with flat
    /// triangulated fills.<br/>
    /// Falls back to centroid classification if the cut fails.
    /// </summary>
    /// <param name="soup">Input mesh as triangle soup.</param>
    /// <param name="nx">Plane normal X.</param>
    /// <param name="ny">Plane normal Y.</param>
    /// <param name="nz">Plane normal Z.</param>
    /// <param name="d">Plane offset: nx·x + ny·y + nz·z + d = 0.</param>
    /// <param name="capEnds">Cap open cut boundaries with flat polygon fills.</param>
    public static (List<float[]> Above, List<float[]> Below) TrueSliceByPlane(
        List<float[]> soup,
        double nx, double ny, double nz, double d,
        bool capEnds = true)
    {
        try
        {
            var dm = ToIndexedMesh(soup);
            ApplyMeshPlaneCut(dm, nx, ny, nz, d, capEnds);
            var comps = FindConnectedComponents(dm);

            var above = new List<float[]>();
            var below = new List<float[]>();

            foreach (var comp in comps)
            {
                if (comp.Count == 0) continue;
                var tri0 = dm.GetTriangle(comp[0]);
                var c    = (dm.GetVertex(tri0.a) + dm.GetVertex(tri0.b) + dm.GetVertex(tri0.c)) / 3.0;
                bool isAbove = nx * c.x + ny * c.y + nz * c.z + d >= 0;

                var target = isAbove ? above : below;
                AppendComponentToSoup(dm, comp, target);
            }
            return (above, below);
        }
        catch
        {
            // Graceful fallback: centroid-only split (no triangle splitting)
            return SplitByPlaneCentroid(soup, nx, ny, nz, d);
        }
    }

    /// <summary>
    /// Applies <paramref name="planes"/> sequentially to the mesh using true
    /// triangle slicing, then returns each resulting connected component together
    /// with per-plane side metadata for downstream classification.
    /// Open boundary loops are optionally capped after each cut.
    /// </summary>
    public static List<PlaneSliceComponent> TrueSliceByMultiplePlanes(
        List<float[]> soup,
        (double Nx, double Ny, double Nz, double D)[] planes,
        bool capEnds = true)
    {
        try
        {
            var dm = ToIndexedMesh(soup);

            // Apply each plane cut in sequence; caps from earlier cuts are re-sliced
            // if a later plane intersects them — this is correct and desired behaviour.
            foreach (var (nx, ny, nz, d) in planes)
                ApplyMeshPlaneCut(dm, nx, ny, nz, d, capEnds);

            var comps   = FindConnectedComponents(dm);
            var results = new List<PlaneSliceComponent>(comps.Count);

            foreach (var comp in comps)
            {
                if (comp.Count == 0) continue;

                // Classify this component against every plane using its first-triangle centroid.
                var tri0     = dm.GetTriangle(comp[0]);
                var centroid = (dm.GetVertex(tri0.a) + dm.GetVertex(tri0.b) + dm.GetVertex(tri0.c)) / 3.0;

                var abovePlanes = new bool[planes.Length];
                for (int pi = 0; pi < planes.Length; pi++)
                    abovePlanes[pi] = planes[pi].Nx * centroid.x
                                    + planes[pi].Ny * centroid.y
                                    + planes[pi].Nz * centroid.z
                                    + planes[pi].D  >= 0;

                var mesh = new List<float[]>(comp.Count * 3);
                AppendComponentToSoup(dm, comp, mesh);

                results.Add(new PlaneSliceComponent { Mesh = mesh, AbovePlanes = abovePlanes });
            }
            return results;
        }
        catch
        {
            // Fallback: return the whole mesh as one unsplit component
            return new List<PlaneSliceComponent>
            {
                new PlaneSliceComponent
                {
                    Mesh        = new List<float[]>(soup),
                    AbovePlanes = new bool[planes.Length]
                }
            };
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Applies one infinite MeshPlaneCut to <paramref name="dm"/> in-place.</summary>
    private static void ApplyMeshPlaneCut(
        DMesh3 dm, double nx, double ny, double nz, double d, bool capEnds)
    {
        double lenSq = nx*nx + ny*ny + nz*nz;
        if (lenSq < 1e-12) return;

        // Point on the plane closest to the origin: P = -N * d / |N|²
        var origin = new Vector3d(-nx * d / lenSq, -ny * d / lenSq, -nz * d / lenSq);
        var normal = new Vector3d(nx, ny, nz);

        var cut = new MeshPlaneCut(dm, origin, normal);
        cut.Cut();
        if (capEnds) cut.FillHoles();

    }

    /// <summary>
    /// BFS over edge-adjacency of <paramref name="dm"/> to find connected
    /// components. Each component is returned as a list of triangle IDs.
    /// </summary>
    private static List<List<int>> FindConnectedComponents(DMesh3 dm)
    {
        var visited    = new HashSet<int>();
        var components = new List<List<int>>();

        foreach (int tid in dm.TriangleIndices())
        {
            if (visited.Contains(tid)) continue;

            var comp = new List<int>();
            var q    = new Queue<int>();
            q.Enqueue(tid); visited.Add(tid);

            while (q.Count > 0)
            {
                int t = q.Dequeue(); comp.Add(t);
                Index3i eids = dm.GetTriEdges(t);
                foreach (int eid in new[] { eids.a, eids.b, eids.c })
                {
                    Index2i nbrs = dm.GetEdgeT(eid);
                    if (nbrs.a >= 0 && !visited.Contains(nbrs.a)) { visited.Add(nbrs.a); q.Enqueue(nbrs.a); }
                    if (nbrs.b >= 0 && !visited.Contains(nbrs.b)) { visited.Add(nbrs.b); q.Enqueue(nbrs.b); }
                }
            }
            components.Add(comp);
        }
        return components;
    }

    /// <summary>Appends the triangles of one component to a triangle-soup list.</summary>
    private static void AppendComponentToSoup(DMesh3 dm, List<int> comp, List<float[]> target)
    {
        foreach (int t in comp)
        {
            var tri = dm.GetTriangle(t);
            var va  = dm.GetVertex(tri.a);
            var vb  = dm.GetVertex(tri.b);
            var vc  = dm.GetVertex(tri.c);
            target.Add(new float[] { (float)va.x, (float)va.y, (float)va.z });
            target.Add(new float[] { (float)vb.x, (float)vb.y, (float)vb.z });
            target.Add(new float[] { (float)vc.x, (float)vc.y, (float)vc.z });
        }
    }

    /// <summary>Centroid-only split — used as fallback when MeshPlaneCut fails.</summary>
    private static (List<float[]> Above, List<float[]> Below) SplitByPlaneCentroid(
        List<float[]> soup, double nx, double ny, double nz, double d)
    {
        var above = new List<float[]>();
        var below = new List<float[]>();
        for (int i = 0; i + 2 < soup.Count; i += 3)
        {
            double cx = (soup[i][0] + soup[i+1][0] + soup[i+2][0]) / 3.0;
            double cy = (soup[i][1] + soup[i+1][1] + soup[i+2][1]) / 3.0;
            double cz = (soup[i][2] + soup[i+1][2] + soup[i+2][2]) / 3.0;
            var target = nx*cx + ny*cy + nz*cz + d >= 0 ? above : below;
            target.Add(soup[i]); target.Add(soup[i+1]); target.Add(soup[i+2]);
        }
        return (above, below);
    }
}
