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
    /// <summary>
    /// Original trimmed ICP — used by DentalAlignmentWindow (STL→CT registration).
    /// Dynamically trims the worst correspondences from 100% down to trimRatio over 40 iterations,
    /// then uses SVD-based optimal rigid transform on the kept pairs.
    /// </summary>
    public static AlignResult Align(
        float[] sourceVerts,
        float[] targetVerts,
        double[,]? initialTransform = null,
        int maxIterations = 80,
        double tolerance = 0.001,
        double trimRatio = 0.60,
        Action<double>? progress = null)
    {
        int totalSrcPts = sourceVerts.Length / 3;
        int step = Math.Max(1, totalSrcPts / 8000);
        int nSrc = 0;
        for (int i = 0; i < totalSrcPts; i += step) nSrc++;

        var initT = initialTransform ?? Identity4x4();
        var currentSrc = new double[nSrc, 3];
        int si = 0;
        for (int i = 0; i < totalSrcPts; i += step)
        {
            int b = i * 3;
            TransformPoint(initT, sourceVerts[b], sourceVerts[b + 1], sourceVerts[b + 2],
                out double tx, out double ty, out double tz);
            currentSrc[si, 0] = tx; currentSrc[si, 1] = ty; currentSrc[si, 2] = tz;
            si++;
        }

        // Build KdTree on full target
        var tree = new KdTree();
        tree.Build(targetVerts, targetVerts.Length / 3);

        var totalT = (double[,])initT.Clone();
        double prevRms = double.MaxValue;
        int iter;

        for (iter = 0; iter < maxIterations; iter++)
        {
            progress?.Invoke((double)iter / maxIterations);

            var distances = new (int srcIdx, double distSq, double tgtX, double tgtY, double tgtZ)[nSrc];
            for (int i = 0; i < nSrc; i++)
            {
                var (idx, distSq) = tree.FindNearest(
                    (float)currentSrc[i, 0], (float)currentSrc[i, 1], (float)currentSrc[i, 2]);
                var (ptx, pty, ptz) = tree.GetPoint(idx);
                distances[i] = (i, distSq, ptx, pty, ptz);
            }

            // Dynamic trimming: start at 100%, linearly reduce to trimRatio over first 40 iterations
            double currentTrimRatio = trimRatio;
            if (iter < 40)
            {
                double t = iter / 40.0;
                currentTrimRatio = 1.0 * (1.0 - t) + trimRatio * t;
            }

            Array.Sort(distances, (a, b) => a.distSq.CompareTo(b.distSq));
            int nKeep = Math.Max(10, (int)(nSrc * currentTrimRatio));

            var trimSrc = new double[nKeep, 3];
            var trimTgt = new double[nKeep, 3];
            double sumDistSq = 0;
            for (int i = 0; i < nKeep; i++)
            {
                int srcIdx = distances[i].srcIdx;
                trimSrc[i, 0] = currentSrc[srcIdx, 0];
                trimSrc[i, 1] = currentSrc[srcIdx, 1];
                trimSrc[i, 2] = currentSrc[srcIdx, 2];
                trimTgt[i, 0] = distances[i].tgtX;
                trimTgt[i, 1] = distances[i].tgtY;
                trimTgt[i, 2] = distances[i].tgtZ;
                sumDistSq += distances[i].distSq;
            }

            double rms = Math.Sqrt(sumDistSq / nKeep);

            if (iter > 20 && Math.Abs(prevRms - rms) < tolerance)
            {
                prevRms = rms;
                iter++;
                break;
            }
            prevRms = rms;

            var stepT = ComputeRigidTransformSVD(trimSrc, trimTgt, nKeep);

            for (int i = 0; i < nSrc; i++)
            {
                TransformPoint(stepT, currentSrc[i, 0], currentSrc[i, 1], currentSrc[i, 2],
                    out double nx, out double ny, out double nz);
                currentSrc[i, 0] = nx;
                currentSrc[i, 1] = ny;
                currentSrc[i, 2] = nz;
            }

            totalT = Multiply4x4(stepT, totalT);
        }

        progress?.Invoke(1.0);
        return new AlignResult { Transform = totalT, RmsError = prevRms, Iterations = iter };
    }

    /// <summary>
    /// Robust occlusion ICP — for aligning a dental scan (occlusion STL) against a bone surface.
    /// Uses bidirectional culling to isolate the dental-overlap zone on both meshes,
    /// then refines with Gaussian-weighted SVD so local accurate contacts dominate over
    /// the large non-dental bone regions (chin, ramus, palate, etc.).
    /// </summary>
    public static AlignResult AlignRobust(
        float[] sourceVerts,
        float[] targetVerts,
        double[,]? initialTransform = null,
        int maxIterations = 200,
        double tolerance = 0.0005,
        double targetCullRatio = 0.30,
        double sourceCullRatio = 0.50,
        double sigmaEnd = 2.0,
        Action<double>? progress = null)
    {
        int totalSrcPts = sourceVerts.Length / 3;
        int step = Math.Max(1, totalSrcPts / 8000);
        int nSrc = 0;
        for (int i = 0; i < totalSrcPts; i += step) nSrc++;

        var initT = initialTransform ?? Identity4x4();
        var currentSrc = new double[nSrc, 3];
        int si = 0;
        for (int i = 0; i < totalSrcPts; i += step)
        {
            int b = i * 3;
            TransformPoint(initT, sourceVerts[b], sourceVerts[b + 1], sourceVerts[b + 2],
                out double tx, out double ty, out double tz);
            currentSrc[si, 0] = tx; currentSrc[si, 1] = ty; currentSrc[si, 2] = tz;
            si++;
        }

        // ── Pass 1: Source KD-tree → cull TARGET ──────────────────────────────────────
        // Removes non-dental bone regions (ramus, chin, palate) from the target.
        // Keeps the closest targetCullRatio fraction of target points (those nearest to source).
        var srcFlat = new float[nSrc * 3];
        for (int i = 0; i < nSrc; i++)
        { srcFlat[i*3] = (float)currentSrc[i,0]; srcFlat[i*3+1] = (float)currentSrc[i,1]; srcFlat[i*3+2] = (float)currentSrc[i,2]; }
        var sourceTree = new KdTree();
        sourceTree.Build(srcFlat, nSrc);

        int totalTgtPts = targetVerts.Length / 3;
        var tgtDistances = new (int idx, double distSq)[totalTgtPts];
        for (int i = 0; i < totalTgtPts; i++)
        {
            int b = i * 3;
            var (_, distSq) = sourceTree.FindNearest(targetVerts[b], targetVerts[b+1], targetVerts[b+2]);
            tgtDistances[i] = (i, distSq);
        }
        Array.Sort(tgtDistances, (a, b) => a.distSq.CompareTo(b.distSq));
        int keepTgt = Math.Max(10, (int)(totalTgtPts * targetCullRatio));

        var croppedFlat = new float[keepTgt * 3];
        for (int i = 0; i < keepTgt; i++)
        {
            int b = tgtDistances[i].idx * 3;
            croppedFlat[i*3] = targetVerts[b]; croppedFlat[i*3+1] = targetVerts[b+1]; croppedFlat[i*3+2] = targetVerts[b+2];
        }
        var tree = new KdTree();
        tree.Build(croppedFlat, keepTgt);

        // ── Pass 2: Cropped-target KD-tree → cull SOURCE ──────────────────────────────
        // Removes gum tissue and arch-base from the source so only crown surfaces participate.
        // Keeps the closest sourceCullRatio fraction of source points (those nearest to target).
        var srcToTgtDist = new (int srcIdx, double distSq)[nSrc];
        for (int i = 0; i < nSrc; i++)
        {
            var (_, dSq) = tree.FindNearest(
                (float)currentSrc[i,0], (float)currentSrc[i,1], (float)currentSrc[i,2]);
            srcToTgtDist[i] = (i, dSq);
        }
        Array.Sort(srcToTgtDist, (a, b) => a.distSq.CompareTo(b.distSq));
        int nSrcActive = Math.Max(6, (int)(nSrc * sourceCullRatio));

        var activeSrc = new double[nSrcActive, 3];
        for (int i = 0; i < nSrcActive; i++)
        {
            int old = srcToTgtDist[i].srcIdx;
            activeSrc[i, 0] = currentSrc[old, 0];
            activeSrc[i, 1] = currentSrc[old, 1];
            activeSrc[i, 2] = currentSrc[old, 2];
        }
        currentSrc = activeSrc;
        nSrc = nSrcActive;

        // Gaussian ICP with sigma annealing from 20mm → sigmaEnd over first 1/3 of iterations.

        var totalT = (double[,])initT.Clone();
        double prevRms = double.MaxValue;
        int iter;

        for (iter = 0; iter < maxIterations; iter++)
        {
            progress?.Invoke((double)iter / maxIterations);

            double t      = Math.Min(1.0, iter / Math.Max(1.0, maxIterations / 3.0));
            double sigma  = 20.0 + (sigmaEnd - 20.0) * t;
            double sigSq  = sigma * sigma;

            double sumWDistSq = 0, sumW = 0;
            int nInliers = 0;
            double wSumX = 0, wSumY = 0, wSumZ = 0;
            double wTSumX = 0, wTSumY = 0, wTSumZ = 0;
            var pairs = new (double sx, double sy, double sz,
                             double tx, double ty, double tz,
                             double w)[nSrc];

            for (int i = 0; i < nSrc; i++)
            {
                var (idx, distSq) = tree.FindNearest(
                    (float)currentSrc[i, 0], (float)currentSrc[i, 1], (float)currentSrc[i, 2]);

                // All surviving post-cull points participate; Gaussian weight handles distance
                double w = Math.Exp(-distSq / sigSq);
                var (ptx, pty, ptz) = tree.GetPoint(idx);

                pairs[i] = (currentSrc[i,0], currentSrc[i,1], currentSrc[i,2], ptx, pty, ptz, w);
                wSumX  += w * currentSrc[i,0]; wSumY  += w * currentSrc[i,1]; wSumZ  += w * currentSrc[i,2];
                wTSumX += w * ptx;             wTSumY += w * pty;             wTSumZ += w * ptz;
                sumW += w;
                sumWDistSq += w * distSq;
                nInliers++;
            }

            if (sumW < 1e-12 || nInliers < 6) break;

            double csx = wSumX/sumW, csy = wSumY/sumW, csz = wSumZ/sumW;
            double ctx = wTSumX/sumW, cty = wTSumY/sumW, ctz = wTSumZ/sumW;

            var H = new double[3, 3];
            for (int i = 0; i < nSrc; i++)
            {
                double w = pairs[i].w;
                if (w < 1e-12) continue;
                double ax = pairs[i].sx - csx, ay = pairs[i].sy - csy, az = pairs[i].sz - csz;
                double bx = pairs[i].tx - ctx, by = pairs[i].ty - cty, bz = pairs[i].tz - ctz;
                H[0,0] += w*ax*bx; H[0,1] += w*ax*by; H[0,2] += w*ax*bz;
                H[1,0] += w*ay*bx; H[1,1] += w*ay*by; H[1,2] += w*ay*bz;
                H[2,0] += w*az*bx; H[2,1] += w*az*by; H[2,2] += w*az*bz;
            }

            double rms = Math.Sqrt(sumWDistSq / sumW);
            if (iter > 20 && Math.Abs(prevRms - rms) < tolerance)
            {
                prevRms = rms; iter++; break;
            }
            prevRms = rms;

            var stepT = ComputeRigidTransformFromH(H, csx, csy, csz, ctx, cty, ctz);
            for (int i = 0; i < nSrc; i++)
            {
                TransformPoint(stepT, currentSrc[i,0], currentSrc[i,1], currentSrc[i,2],
                    out double nx, out double ny, out double nz);
                currentSrc[i,0] = nx; currentSrc[i,1] = ny; currentSrc[i,2] = nz;
            }
            totalT = Multiply4x4(stepT, totalT);
        }

        progress?.Invoke(1.0);
        return new AlignResult { Transform = totalT, RmsError = prevRms, Iterations = iter };
    }

    // ── List<float[]> overloads ──────────────────────────────────────────────

    /// <summary>
    /// Compute a rigid transform from matched landmark pairs using SVD.
    /// </summary>
    public static double[,] ComputeLandmarkTransform(
        List<(double X, double Y, double Z)> sourceLandmarks,
        List<(double X, double Y, double Z)> targetLandmarks)
    {
        int n = Math.Min(sourceLandmarks.Count, targetLandmarks.Count);
        if (n < 3) return Identity4x4();

        // Centroids
        double sx = 0, sy = 0, sz = 0, tx = 0, ty = 0, tz = 0;
        for (int i = 0; i < n; i++)
        {
            sx += sourceLandmarks[i].X; sy += sourceLandmarks[i].Y; sz += sourceLandmarks[i].Z;
            tx += targetLandmarks[i].X; ty += targetLandmarks[i].Y; tz += targetLandmarks[i].Z;
        }
        sx /= n; sy /= n; sz /= n;
        tx /= n; ty /= n; tz /= n;

        // Cross-covariance H
        double h00=0,h01=0,h02=0,h10=0,h11=0,h12=0,h20=0,h21=0,h22=0;
        for (int i = 0; i < n; i++)
        {
            double ax = sourceLandmarks[i].X - sx, ay = sourceLandmarks[i].Y - sy, az = sourceLandmarks[i].Z - sz;
            double bx = targetLandmarks[i].X - tx, by = targetLandmarks[i].Y - ty, bz = targetLandmarks[i].Z - tz;
            h00+=ax*bx; h01+=ax*by; h02+=ax*bz;
            h10+=ay*bx; h11+=ay*by; h12+=ay*bz;
            h20+=az*bx; h21+=az*by; h22+=az*bz;
        }

        // Horn's method: build 4×4 symmetric K from H's antisymmetric part
        // The largest eigenvector of K is the optimal rotation quaternion q=[w,x,y,z]
        double[,] K = new double[4, 4]
        {
            { h00+h11+h22,  h12-h21,      h20-h02,      h01-h10      },
            { h12-h21,      h00-h11-h22,  h01+h10,      h20+h02      },
            { h20-h02,      h01+h10,     -h00+h11-h22,  h12+h21      },
            { h01-h10,      h20+h02,      h12+h21,      -h00-h11+h22 }
        };

        // Gershgorin spectral shift: ensure K is positive-definite before power iteration.
        // Power iteration converges to the largest-MAGNITUDE eigenvector, not the
        // largest-positive one. For near-coplanar landmarks, K[3,3] can dominate in
        // absolute value (it is negative), sending power iteration to the wrong eigenvector.
        // Shifting K += |λ_min|·I (bounded via Gershgorin) makes all eigenvalues ≥ 0,
        // so the dominant eigenvector is guaranteed to be the correct rotation quaternion.
        double gershShift = 0;
        for (int i = 0; i < 4; i++)
        {
            double offDiagSum = 0;
            for (int j = 0; j < 4; j++) if (j != i) offDiagSum += Math.Abs(K[i, j]);
            double lowerBound = K[i, i] - offDiagSum;
            if (lowerBound < gershShift) gershShift = lowerBound;
        }
        if (gershShift < 0)
            for (int i = 0; i < 4; i++) K[i, i] -= gershShift; // adds |gershShift|

        // Power iteration to find dominant eigenvector of K
        double[] q = { 1, 0, 0, 0 };
        for (int iter = 0; iter < 200; iter++)
        {
            double[] Kq = new double[4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Kq[r] += K[r, c] * q[c];
            double norm = Math.Sqrt(Kq[0]*Kq[0]+Kq[1]*Kq[1]+Kq[2]*Kq[2]+Kq[3]*Kq[3]);
            if (norm < 1e-14) break;
            q[0]=Kq[0]/norm; q[1]=Kq[1]/norm; q[2]=Kq[2]/norm; q[3]=Kq[3]/norm;
        }
        // q = [w, x, y, z]
        double qw=q[0], qx=q[1], qy=q[2], qz=q[3];

        // Quaternion → rotation matrix
        double R00 = 1-2*(qy*qy+qz*qz), R01 = 2*(qx*qy-qz*qw), R02 = 2*(qx*qz+qy*qw);
        double R10 = 2*(qx*qy+qz*qw),   R11 = 1-2*(qx*qx+qz*qz), R12 = 2*(qy*qz-qx*qw);
        double R20 = 2*(qx*qz-qy*qw),   R21 = 2*(qy*qz+qx*qw),   R22 = 1-2*(qx*qx+qy*qy);

        // Translation: t = centroid_tgt - R * centroid_src
        double ttx = tx - (R00*sx + R01*sy + R02*sz);
        double tty = ty - (R10*sx + R11*sy + R12*sz);
        double ttz = tz - (R20*sx + R21*sy + R22*sz);

        var T = new double[4, 4];
        T[0,0]=R00; T[0,1]=R01; T[0,2]=R02; T[0,3]=ttx;
        T[1,0]=R10; T[1,1]=R11; T[1,2]=R12; T[1,3]=tty;
        T[2,0]=R20; T[2,1]=R21; T[2,2]=R22; T[2,3]=ttz;
        T[3,3]=1.0;
        return T;
    }


    /// <summary>
    /// Apply a 4x4 transform to all vertices in place (flat float[] stride-3).
    /// </summary>
    public static void TransformVertices(float[] vertices, double[,] transform)
    {
        for (int i = 0; i < vertices.Length; i += 3)
        {
            TransformPoint(transform, vertices[i], vertices[i + 1], vertices[i + 2],
                out double tx, out double ty, out double tz);
            vertices[i]     = (float)tx;
            vertices[i + 1] = (float)ty;
            vertices[i + 2] = (float)tz;
        }
    }

    // ═══ Internal SVD-based rigid transform ═══

    // ─── Compatibility overloads for windows that still use List<float[]> ───

    public static AlignResult Align(
        List<float[]> sourceVerts, List<float[]> targetVerts,
        double[,]? initialTransform = null, int maxIterations = 80,
        double tolerance = 0.001, double trimRatio = 0.60,
        Action<double>? progress = null)
    {
        var srcFlat = new float[sourceVerts.Count * 3];
        for (int i = 0; i < sourceVerts.Count; i++)
        { srcFlat[i*3] = sourceVerts[i][0]; srcFlat[i*3+1] = sourceVerts[i][1]; srcFlat[i*3+2] = sourceVerts[i][2]; }

        var tgtFlat = new float[targetVerts.Count * 3];
        for (int i = 0; i < targetVerts.Count; i++)
        { tgtFlat[i*3] = targetVerts[i][0]; tgtFlat[i*3+1] = targetVerts[i][1]; tgtFlat[i*3+2] = targetVerts[i][2]; }

        return Align(srcFlat, tgtFlat, initialTransform, maxIterations, tolerance, trimRatio, progress);
    }

    public static AlignResult AlignRobust(
        List<float[]> sourceVerts, List<float[]> targetVerts,
        double[,]? initialTransform = null, int maxIterations = 200,
        double tolerance = 0.0005,
        double targetCullRatio = 0.30,
        double sourceCullRatio = 0.50,
        double sigmaEnd = 2.0,
        Action<double>? progress = null)
    {
        var srcFlat = new float[sourceVerts.Count * 3];
        for (int i = 0; i < sourceVerts.Count; i++)
        { srcFlat[i*3] = sourceVerts[i][0]; srcFlat[i*3+1] = sourceVerts[i][1]; srcFlat[i*3+2] = sourceVerts[i][2]; }

        var tgtFlat = new float[targetVerts.Count * 3];
        for (int i = 0; i < targetVerts.Count; i++)
        { tgtFlat[i*3] = targetVerts[i][0]; tgtFlat[i*3+1] = targetVerts[i][1]; tgtFlat[i*3+2] = targetVerts[i][2]; }

        return AlignRobust(srcFlat, tgtFlat, initialTransform, maxIterations, tolerance,
            targetCullRatio, sourceCullRatio, sigmaEnd, progress);
    }

    public static void TransformVertices(List<float[]> vertices, double[,] transform)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            TransformPoint(transform, vertices[i][0], vertices[i][1], vertices[i][2],
                out double tx, out double ty, out double tz);
            vertices[i][0] = (float)tx; vertices[i][1] = (float)ty; vertices[i][2] = (float)tz;
        }
    }

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
        SVD3x3(H, out double[,] U, out double[,] V);

        // R = V * U^T
        var R = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int k = 0; k < 3; k++)
                    sum += V[i, k] * U[j, k]; // V * U^T
                R[i, j] = sum;
            }

        // Ensure proper rotation (det(R) = +1)
        double det = R[0, 0] * (R[1, 1] * R[2, 2] - R[1, 2] * R[2, 1])
                   - R[0, 1] * (R[1, 0] * R[2, 2] - R[1, 2] * R[2, 0])
                   + R[0, 2] * (R[1, 0] * R[2, 1] - R[1, 1] * R[2, 0]);
        if (det < 0)
        {
            // Flip the column of V corresponding to the smallest singular value
            for (int i = 0; i < 3; i++) V[i, 2] = -V[i, 2];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                        sum += V[i, k] * U[j, k];
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

    // ─── Builds a rigid transform from a pre-computed weighted cross-covariance H ───
    // Used by AlignRobust to avoid re-allocating pair arrays per step.
    private static double[,] ComputeRigidTransformFromH(
        double[,] H,
        double csx, double csy, double csz,
        double ctx, double cty, double ctz)
    {
        SVD3x3(H, out double[,] U, out double[,] V);

        var R = new double[3,3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                double s = 0;
                for (int k = 0; k < 3; k++) s += V[i,k] * U[j,k];
                R[i,j] = s;
            }

        double det = R[0,0]*(R[1,1]*R[2,2]-R[1,2]*R[2,1])
                   - R[0,1]*(R[1,0]*R[2,2]-R[1,2]*R[2,0])
                   + R[0,2]*(R[1,0]*R[2,1]-R[1,1]*R[2,0]);
        if (det < 0)
        {
            for (int i = 0; i < 3; i++) V[i,2] = -V[i,2];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double s = 0;
                    for (int k = 0; k < 3; k++) s += V[i,k] * U[j,k];
                    R[i,j] = s;
                }
        }

        double ttx = ctx - (R[0,0]*csx + R[0,1]*csy + R[0,2]*csz);
        double tty = cty - (R[1,0]*csx + R[1,1]*csy + R[1,2]*csz);
        double ttz = ctz - (R[2,0]*csx + R[2,1]*csy + R[2,2]*csz);

        var T = new double[4,4];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) T[i,j] = R[i,j];
        T[0,3] = ttx; T[1,3] = tty; T[2,3] = ttz; T[3,3] = 1.0;
        return T;
    }

    /// <summary>
    /// Minimalist 3x3 SVD via Jacobi eigenvalue decomposition of H^T*H.
    /// </summary>
    private static void SVD3x3(double[,] H, out double[,] U, out double[,] VOut)
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
        VOut = V;

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
