using System.Numerics;

namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Iterative Closest Point (ICP) rigid registration for aligning dental scan meshes
/// to CT segmentation surfaces. Uses point-to-point ICP with SVD-based optimal rotation.
/// </summary>
public static class IcpAligner
{
    public class AlignResult
    {
        /// <summary>4x4 rigid transform matrix (row-major, [row,col]).</summary>
        public double[,] Transform { get; set; } = new double[4, 4];
        /// <summary>Root-mean-square distance error after alignment.</summary>
        public double RmsError { get; set; }
        /// <summary>Number of iterations actually performed.</summary>
        public int Iterations { get; set; }
    }

    /// <summary>
    /// Compute the rigid transform to align source points onto target points.
    /// Uses an initial transform from landmark-based registration, then refines with ICP.
    /// </summary>
    /// <param name="sourceVerts">Source mesh vertices (the STL scan to move).</param>
    /// <param name="targetVerts">Target mesh vertices (the CT dental surface, stays fixed).</param>
    /// <param name="initialTransform">4x4 initial guess from landmark registration (can be identity).</param>
    /// <param name="maxIterations">Maximum ICP iterations.</param>
    /// <param name="tolerance">Convergence threshold on RMS change.</param>
    /// <param name="progress">Optional progress callback (0.0–1.0).</param>
    public static AlignResult Align(
        List<float[]> sourceVerts,
        List<float[]> targetVerts,
        double[,]? initialTransform = null,
        int maxIterations = 80,
        double tolerance = 0.001,
        double trimRatio = 0.60,
        Action<double>? progress = null)
    {
        // Subsample source for performance (use every Nth point, max ~8000)
        int step = Math.Max(1, sourceVerts.Count / 8000);
        var srcSampled = new List<float[]>();
        for (int i = 0; i < sourceVerts.Count; i += step)
            srcSampled.Add(sourceVerts[i]);

        int nSrc = srcSampled.Count;

        // Apply initial transform to sampled source
        var currentSrc = new double[nSrc, 3];
        var initT = initialTransform ?? Identity4x4();

        for (int i = 0; i < nSrc; i++)
        {
            TransformPoint(initT, srcSampled[i][0], srcSampled[i][1], srcSampled[i][2],
                out double tx, out double ty, out double tz);
            currentSrc[i, 0] = tx;
            currentSrc[i, 1] = ty;
            currentSrc[i, 2] = tz;
        }

        // --- PRE-ICP CULLING OF THE TARGET MESH ---
        // The CT target mesh is massive (entire skull/cranium) but we only want to align to the teeth.
        // We temporarily build a KdTree of the *source* (STL cast) at its initial position.
        var sourceTree = new KdTree();
        var sourceListForTree = new List<float[]>();
        for (int i = 0; i < nSrc; i++) sourceListForTree.Add(new float[] { (float)currentSrc[i, 0], (float)currentSrc[i, 1], (float)currentSrc[i, 2] });
        sourceTree.Build(sourceListForTree);

        // Find distance from every target point to the closest source point
        var tgtDistances = new (float[] pt, double distSq)[targetVerts.Count];
        for (int i = 0; i < targetVerts.Count; i++)
        {
            var p = targetVerts[i];
            var (_, distSq) = sourceTree.FindNearest(p[0], p[1], p[2]);
            tgtDistances[i] = (p, distSq);
        }

        // Sort and completely discard the furthest 90% of the CT mesh (cranium, spine, etc.)
        Array.Sort(tgtDistances, (a, b) => a.distSq.CompareTo(b.distSq));
        int keepTgt = Math.Max(10, (int)(targetVerts.Count * 0.10));
        var croppedTarget = new List<float[]>(keepTgt);
        for (int i = 0; i < keepTgt; i++) croppedTarget.Add(tgtDistances[i].pt);

        // Build main k-d tree on the incredibly cropped target (teeth only!)
        var tree = new KdTree();
        tree.Build(croppedTarget);

        // Accumulate total transform
        var totalT = (double[,])initT.Clone();
        double prevRms = double.MaxValue;
        int iter;

        for (iter = 0; iter < maxIterations; iter++)
        {
            progress?.Invoke((double)iter / maxIterations);

            // Step 1: Find closest points in target for each source point
            var distances = new (int srcIdx, double distSq, double tgtX, double tgtY, double tgtZ)[nSrc];

            for (int i = 0; i < nSrc; i++)
            {
                var (idx, distSq) = tree.FindNearest(
                    (float)currentSrc[i, 0], (float)currentSrc[i, 1], (float)currentSrc[i, 2]);
                var (ptx, pty, ptz) = tree.GetPoint(idx);
                distances[i] = (i, distSq, ptx, pty, ptz);
            }

            // DYNAMIC TRIMMING: Start with 100% of points, linearly decrease to trimRatio over first 40 iterations
            double currentTrimRatio = trimRatio;
            if (iter < 40)
            {
                double t = iter / 40.0;
                currentTrimRatio = 1.0 * (1.0 - t) + trimRatio * t;
            }

            Array.Sort(distances, (a, b) => a.distSq.CompareTo(b.distSq));
            int nKeep = Math.Max(10, (int)(nSrc * currentTrimRatio));

            // Build trimmed correspondence arrays
            var trimSrc = new double[nKeep, 3];
            var trimTgt = new double[nKeep, 3];
            double sumDistSq = 0;

            for (int i = 0; i < nKeep; i++)
            {
                int si = distances[i].srcIdx;
                trimSrc[i, 0] = currentSrc[si, 0];
                trimSrc[i, 1] = currentSrc[si, 1];
                trimSrc[i, 2] = currentSrc[si, 2];
                trimTgt[i, 0] = distances[i].tgtX;
                trimTgt[i, 1] = distances[i].tgtY;
                trimTgt[i, 2] = distances[i].tgtZ;
                sumDistSq += distances[i].distSq;
            }

            double rms = Math.Sqrt(sumDistSq / nKeep);

            // Convergence check: require at least 20 iterations to avoid getting trapped in early local minima during dynamic trimming
            if (iter > 20 && Math.Abs(prevRms - rms) < tolerance)
            {
                prevRms = rms;
                iter++;
                break;
            }
            prevRms = rms;

            // Step 2: Compute optimal rigid transform from TRIMMED pairs only
            var stepT = ComputeRigidTransformSVD(trimSrc, trimTgt, nKeep);

            // Apply step transform to ALL current source positions
            for (int i = 0; i < nSrc; i++)
            {
                TransformPoint(stepT, currentSrc[i, 0], currentSrc[i, 1], currentSrc[i, 2],
                    out double nx, out double ny, out double nz);
                currentSrc[i, 0] = nx;
                currentSrc[i, 1] = ny;
                currentSrc[i, 2] = nz;
            }

            // Accumulate: totalT = stepT * totalT
            totalT = Multiply4x4(stepT, totalT);
        }

        progress?.Invoke(1.0);

        return new AlignResult
        {
            Transform = totalT,
            RmsError = prevRms,
            Iterations = iter
        };
    }

    /// <summary>
    /// Compute a rigid transform from matched landmark pairs using SVD.
    /// </summary>
    public static double[,] ComputeLandmarkTransform(
        List<(double X, double Y, double Z)> sourceLandmarks,
        List<(double X, double Y, double Z)> targetLandmarks)
    {
        int n = Math.Min(sourceLandmarks.Count, targetLandmarks.Count);
        if (n < 3) return Identity4x4();

        var src = new double[n, 3];
        var tgt = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            src[i, 0] = sourceLandmarks[i].X; src[i, 1] = sourceLandmarks[i].Y; src[i, 2] = sourceLandmarks[i].Z;
            tgt[i, 0] = targetLandmarks[i].X; tgt[i, 1] = targetLandmarks[i].Y; tgt[i, 2] = targetLandmarks[i].Z;
        }

        return ComputeRigidTransformSVD(src, tgt, n);
    }

    /// <summary>
    /// Apply a 4x4 transform to all vertices in place.
    /// </summary>
    public static void TransformVertices(List<float[]> vertices, double[,] transform)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            TransformPoint(transform, vertices[i][0], vertices[i][1], vertices[i][2],
                out double tx, out double ty, out double tz);
            vertices[i][0] = (float)tx;
            vertices[i][1] = (float)ty;
            vertices[i][2] = (float)tz;
        }
    }

    // ═══ Internal SVD-based rigid transform ═══

    private static double[,] ComputeRigidTransformSVD(double[,] src, double[,] tgt, int n)
    {
        // Compute centroids
        double sx = 0, sy = 0, sz = 0, tx = 0, ty = 0, tz = 0;
        for (int i = 0; i < n; i++)
        {
            sx += src[i, 0]; sy += src[i, 1]; sz += src[i, 2];
            tx += tgt[i, 0]; ty += tgt[i, 1]; tz += tgt[i, 2];
        }
        sx /= n; sy /= n; sz /= n;
        tx /= n; ty /= n; tz /= n;

        // Cross-covariance matrix H = Sum((src_i - centroidSrc) * (tgt_i - centroidTgt)^T)
        var H = new double[3, 3];
        for (int i = 0; i < n; i++)
        {
            double ax = src[i, 0] - sx, ay = src[i, 1] - sy, az = src[i, 2] - sz;
            double bx = tgt[i, 0] - tx, by = tgt[i, 1] - ty, bz = tgt[i, 2] - tz;
            H[0, 0] += ax * bx; H[0, 1] += ax * by; H[0, 2] += ax * bz;
            H[1, 0] += ay * bx; H[1, 1] += ay * by; H[1, 2] += ay * bz;
            H[2, 0] += az * bx; H[2, 1] += az * by; H[2, 2] += az * bz;
        }

        // SVD of H = U * S * V^T using Jacobi rotations on H^T*H
        SVD3x3(H, out double[,] U, out double[,] Vt);

        // R = V * U^T
        var R = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++)
                    sum += Vt[k, i] * U[k, j]; // V * U^T
                R[i, j] = sum;
            }

        // Ensure proper rotation (det(R) = +1)
        double det = R[0, 0] * (R[1, 1] * R[2, 2] - R[1, 2] * R[2, 1])
                   - R[0, 1] * (R[1, 0] * R[2, 2] - R[1, 2] * R[2, 0])
                   + R[0, 2] * (R[1, 0] * R[2, 1] - R[1, 1] * R[2, 0]);
        if (det < 0)
        {
            // Flip the column of Vt corresponding to the smallest singular value
            for (int i = 0; i < 3; i++) Vt[i, 2] = -Vt[i, 2];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                        sum += Vt[k, i] * U[k, j];
                    R[i, j] = sum;
                }
        }

        // Translation: t = centroidTgt - R * centroidSrc
        double ttx = tx - (R[0, 0] * sx + R[0, 1] * sy + R[0, 2] * sz);
        double tty = ty - (R[1, 0] * sx + R[1, 1] * sy + R[1, 2] * sz);
        double ttz = tz - (R[2, 0] * sx + R[2, 1] * sy + R[2, 2] * sz);

        // Build 4x4
        var T = new double[4, 4];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                T[i, j] = R[i, j];
        T[0, 3] = ttx; T[1, 3] = tty; T[2, 3] = ttz;
        T[3, 3] = 1.0;

        return T;
    }

    /// <summary>
    /// Minimalist 3x3 SVD via Jacobi eigenvalue decomposition of H^T*H.
    /// </summary>
    private static void SVD3x3(double[,] H, out double[,] U, out double[,] Vt)
    {
        // Compute H^T * H
        var HtH = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double s = 0;
                for (int k = 0; k < 3; k++) s += H[k, i] * H[k, j];
                HtH[i, j] = s;
            }

        // Jacobi eigendecomposition of symmetric HtH
        var V = new double[3, 3]; // eigenvectors
        V[0, 0] = 1; V[1, 1] = 1; V[2, 2] = 1;
        var A = (double[,])HtH.Clone();

        for (int sweep = 0; sweep < 50; sweep++)
        {
            for (int p = 0; p < 3; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(A[p, q]) < 1e-15) continue;
                    double tau = (A[q, q] - A[p, p]) / (2.0 * A[p, q]);
                    double t = Math.Sign(tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1.0 / Math.Sqrt(1 + t * t);
                    double s = t * c;

                    // Rotate A
                    double app = A[p, p], aqq = A[q, q], apq = A[p, q];
                    A[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                    A[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                    A[p, q] = A[q, p] = 0;
                    for (int r = 0; r < 3; r++)
                    {
                        if (r == p || r == q) continue;
                        double arp = A[r, p], arq = A[r, q];
                        A[r, p] = A[p, r] = c * arp - s * arq;
                        A[r, q] = A[q, r] = s * arp + c * arq;
                    }
                    // Rotate V
                    for (int r = 0; r < 3; r++)
                    {
                        double vrp = V[r, p], vrq = V[r, q];
                        V[r, p] = c * vrp - s * vrq;
                        V[r, q] = s * vrp + c * vrq;
                    }
                }
        }

        // eigenvalues are diagonal of A, singular values are sqrt
        // V columns are eigenvectors of H^T*H = right singular vectors
        Vt = V;

        // U = H * V * S^{-1}
        U = new double[3, 3];
        for (int j = 0; j < 3; j++)
        {
            // Compute H * v_j
            double[] hv = new double[3];
            for (int i = 0; i < 3; i++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++) sum += H[i, k] * V[k, j];
                hv[i] = sum;
            }
            // Normalize to get u_j
            double norm = Math.Sqrt(hv[0] * hv[0] + hv[1] * hv[1] + hv[2] * hv[2]);
            if (norm > 1e-12)
            {
                U[0, j] = hv[0] / norm;
                U[1, j] = hv[1] / norm;
                U[2, j] = hv[2] / norm;
            }
            else
            {
                U[0, j] = j == 0 ? 1 : 0;
                U[1, j] = j == 1 ? 1 : 0;
                U[2, j] = j == 2 ? 1 : 0;
            }
        }
    }

    // ═══ Matrix helpers ═══

    public static double[,] Identity4x4()
    {
        var m = new double[4, 4];
        m[0, 0] = m[1, 1] = m[2, 2] = m[3, 3] = 1.0;
        return m;
    }

    public static double[,] Multiply4x4(double[,] a, double[,] b)
    {
        var c = new double[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j];
                c[i, j] = s;
            }
        return c;
    }

    public static void TransformPoint(double[,] T, double x, double y, double z,
        out double ox, out double oy, out double oz)
    {
        ox = T[0, 0] * x + T[0, 1] * y + T[0, 2] * z + T[0, 3];
        oy = T[1, 0] * x + T[1, 1] * y + T[1, 2] * z + T[1, 3];
        oz = T[2, 0] * x + T[2, 1] * y + T[2, 2] * z + T[2, 3];
    }
}
