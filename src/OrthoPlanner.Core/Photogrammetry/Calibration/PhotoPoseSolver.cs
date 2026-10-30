using g3;

namespace OrthoPlanner.Core.Photogrammetry.Calibration;

/// <summary>Approximate direction from which a clinical photo was taken.</summary>
public enum ViewHint
{
    Frontal,
    ProfileRight,
    ProfileLeft,
}

/// <summary>Result of a pose estimation. Always check <see cref="Converged"/> before using <see cref="Pose"/>.</summary>
public sealed record PoseSolution(
    CameraPose Pose,
    double[] ResidualsPx,
    double MeanErrorPx,
    double RmsErrorPx,
    bool Converged,
    int Iterations);

/// <summary>
/// Photo-to-CT camera calibration: estimates the camera pose from 3D landmarks (CT frame, mm)
/// and their 2D clicks (pixels) with Levenberg-Marquardt on a Rodrigues + translation parametrisation.
/// Port of the validated Python prototype (calib_core.py, OpenCV solvePnP + solvePnPRefineLM).
/// </summary>
/// <remarks>
/// CT volume frame (DICOM-like): +X = patient LEFT, +Y = POSTERIOR, +Z = CRANIAL.
/// </remarks>
public static class PhotoPoseSolver
{
    public const int MinPointPairs = 6;

    /// <summary>Leave-one-out folds drop one point, so the core solver accepts this many.</summary>
    private const int MinPointPairsCore = 4;

    private const int ParamCount = 6;
    private const int MaxIterations = 200;
    private const double RelativeCostTolerance = 1e-12;
    private const double InitialLambda = 1e-3;
    private const double LambdaUp = 10.0;
    private const double LambdaDown = 0.1;
    private const double MaxLambda = 1e16;
    private const double RotationStep = 1e-6;
    private const double TranslationStepMm = 1e-3;
    private const double YawRetryDeg = 15.0;

    /// <summary>
    /// Refines <paramref name="initial"/> by Levenberg-Marquardt. Requires at least <see cref="MinPointPairs"/> pairs.
    /// The returned solution reports <see cref="PoseSolution.Converged"/> = false if the solver did not converge.
    /// </summary>
    public static PoseSolution Solve(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                     PinholeCamera cam, CameraPose initial)
    {
        ValidateInputs(world, pixels, cam, MinPointPairs);
        ArgumentNullException.ThrowIfNull(initial);
        return SolveCore(world, pixels, cam, initial);
    }

    /// <summary>
    /// Coarse pose from the view direction: rotation from the hint, translation that puts the 3D centroid
    /// on the optical axis at a depth matching the ratio between 3D (mm) and 2D (px) spreads.
    /// </summary>
    public static CameraPose InitialPose(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                         PinholeCamera cam, ViewHint hint)
    {
        ValidateInputs(world, pixels, cam, MinPointPairs);
        return BuildInitialPose(world, pixels, cam, HintRotation(hint));
    }

    /// <summary>
    /// Full calibration: initial pose from the hint, LM refinement, and yaw +/-15 deg retries if needed.
    /// Throws <see cref="InvalidOperationException"/> if no attempt converges.
    /// </summary>
    public static PoseSolution SolveFromHint(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                             PinholeCamera cam, ViewHint hint)
    {
        ValidateInputs(world, pixels, cam, MinPointPairs);

        Matrix3d baseRotation = HintRotation(hint);
        PoseSolution first = SolveCore(world, pixels, cam, BuildInitialPose(world, pixels, cam, baseRotation));
        if (first.Converged)
            return first;

        PoseSolution? best = null;
        foreach (double yawDeg in new[] { YawRetryDeg, -YawRetryDeg })
        {
            Matrix3d yawed = CameraYaw(yawDeg * Math.PI / 180.0) * baseRotation;
            PoseSolution attempt = SolveCore(world, pixels, cam, BuildInitialPose(world, pixels, cam, yawed));
            if (attempt.Converged && (best is null || attempt.RmsErrorPx < best.RmsErrorPx))
                best = attempt;
        }

        return best ?? throw new InvalidOperationException(
            $"Photo pose solver did not converge for view {hint} (initial and yaw +/-{YawRetryDeg} deg attempts). " +
            $"Last RMS {first.RmsErrorPx:F2} px after {first.Iterations} iterations.");
    }

    /// <summary>
    /// Honest error estimate: for each point, re-solves without it (starting from <paramref name="solution"/>)
    /// and returns that point's reprojection error in pixels. Throws if any fold fails to converge.
    /// </summary>
    public static double[] LeaveOneOut(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                       PinholeCamera cam, PoseSolution solution)
    {
        ValidateInputs(world, pixels, cam, MinPointPairs);
        ArgumentNullException.ThrowIfNull(solution);

        int n = world.Count;
        var errors = new double[n];
        for (int i = 0; i < n; i++)
        {
            var subWorld = new List<Vector3d>(n - 1);
            var subPixels = new List<Vector2d>(n - 1);
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                subWorld.Add(world[j]);
                subPixels.Add(pixels[j]);
            }

            PoseSolution fold = SolveCore(subWorld, subPixels, cam, solution.Pose);
            if (!fold.Converged)
                throw new InvalidOperationException($"Leave-one-out fold {i} did not converge after {fold.Iterations} iterations.");

            Vector2d predicted = cam.Project(fold.Pose.WorldToCamera(world[i]));
            errors[i] = (predicted - pixels[i]).Length;
        }
        return errors;
    }

    /// <summary>Millimetres per pixel at the mean camera-space depth of the world points.</summary>
    public static double MmPerPixel(PinholeCamera cam, CameraPose pose, IReadOnlyList<Vector3d> world)
    {
        ArgumentNullException.ThrowIfNull(cam);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(world);
        if (world.Count == 0)
            throw new ArgumentException("At least one world point is required.", nameof(world));

        double sumZ = 0;
        foreach (Vector3d p in world)
            sumZ += pose.WorldToCamera(p).z;
        double meanZ = sumZ / world.Count;
        if (!(meanZ > 0))
            throw new InvalidOperationException($"World points are not in front of the camera (mean depth {meanZ} mm).");
        return meanZ / cam.Fx;
    }

    // ------------------------------------------------------------------ initial pose

    /// <summary>World-to-camera rotation for each view. Rows are the camera axes (image right, image down, look) in world coordinates.</summary>
    private static Matrix3d HintRotation(ViewHint hint) => hint switch
    {
        // Camera in front of the face looking posteriorly: image right = patient left (+X), image down = caudal.
        ViewHint.Frontal => new Matrix3d(
            new Vector3d(1, 0, 0), new Vector3d(0, 0, -1), new Vector3d(0, 1, 0), true),
        // Camera on the patient's right looking toward +X: face points to image right (anterior = -Y).
        ViewHint.ProfileRight => new Matrix3d(
            new Vector3d(0, -1, 0), new Vector3d(0, 0, -1), new Vector3d(1, 0, 0), true),
        // Camera on the patient's left looking toward -X: face points to image left, image right = +Y.
        ViewHint.ProfileLeft => new Matrix3d(
            new Vector3d(0, 1, 0), new Vector3d(0, 0, -1), new Vector3d(-1, 0, 0), true),
        _ => throw new ArgumentOutOfRangeException(nameof(hint), hint, "Unknown view hint."),
    };

    /// <summary>Rotation about the camera's vertical (image-down) axis.</summary>
    private static Matrix3d CameraYaw(double angleRad)
        => CameraPose.RodriguesToMatrix(new Vector3d(0, angleRad, 0));

    private static CameraPose BuildInitialPose(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                               PinholeCamera cam, Matrix3d rotation)
    {
        int n = world.Count;
        Vector3d c3 = Vector3d.Zero;
        Vector2d c2 = Vector2d.Zero;
        for (int i = 0; i < n; i++)
        {
            c3 += world[i];
            c2 += pixels[i];
        }
        c3 /= n;
        c2 /= n;

        // Spreads measured in the image plane: 3D points projected on the camera x/y axes, so that
        // depth extent along the view direction (large in profile views) does not inflate the estimate.
        double s3 = 0, s2 = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3d d = world[i] - c3;
            double dx = rotation.Row0.Dot(d), dy = rotation.Row1.Dot(d);
            s3 += dx * dx + dy * dy;
            s2 += (pixels[i] - c2).LengthSquared;
        }
        s3 = Math.Sqrt(s3 / n);
        s2 = Math.Sqrt(s2 / n);
        if (!(s3 > 0) || !(s2 > 0))
            throw new InvalidOperationException($"Degenerate landmark configuration (3D spread {s3} mm, 2D spread {s2} px).");

        double depth = cam.Fx * s3 / s2;
        Vector3d translation = new Vector3d(0, 0, depth) - rotation * c3;
        return new CameraPose(rotation, translation);
    }

    // ------------------------------------------------------------------ Levenberg-Marquardt

    private static PoseSolution SolveCore(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                          PinholeCamera cam, CameraPose initial)
    {
        ValidateInputs(world, pixels, cam, MinPointPairsCore);

        int m = 2 * world.Count;
        double[] p = ToParams(initial);
        var residuals = new double[m];
        if (!TryResiduals(p, world, pixels, cam, residuals))
            throw new InvalidOperationException("Initial pose places landmarks behind the camera.");
        double cost = SumSquares(residuals);

        double lambda = InitialLambda;
        bool converged = cost == 0;
        int iteration = 0;
        var jacobian = new double[m, ParamCount];
        var trial = new double[m];

        while (!converged && iteration < MaxIterations)
        {
            iteration++;
            NumericJacobian(p, world, pixels, cam, jacobian);
            BuildNormalEquations(jacobian, residuals, out double[,] jtj, out double[] jtr);

            bool accepted = false;
            while (!accepted)
            {
                double[,] a = (double[,])jtj.Clone();
                for (int k = 0; k < ParamCount; k++)
                    a[k, k] += lambda * Math.Max(jtj[k, k], 1e-12);

                double[] delta = SolveLinear(a, jtr.Select(v => -v).ToArray());
                double[] candidate = new double[ParamCount];
                for (int k = 0; k < ParamCount; k++)
                    candidate[k] = p[k] + delta[k];

                if (delta.All(double.IsFinite) && TryResiduals(candidate, world, pixels, cam, trial))
                {
                    double newCost = SumSquares(trial);
                    if (newCost <= cost)
                    {
                        double relChange = cost > 0 ? (cost - newCost) / cost : 0;
                        p = candidate;
                        (residuals, trial) = (trial, residuals);
                        cost = newCost;
                        lambda = Math.Max(lambda * LambdaDown, 1e-12);
                        accepted = true;
                        converged = relChange < RelativeCostTolerance;
                        continue;
                    }
                }

                lambda *= LambdaUp;
                if (lambda > MaxLambda)
                {
                    // No damped step reduces the cost: current parameters are a local minimum to machine precision.
                    converged = true;
                    break;
                }
            }
        }

        return BuildSolution(FromParams(p), residuals, converged, iteration);
    }

    private static PoseSolution BuildSolution(CameraPose pose, double[] residuals, bool converged, int iterations)
    {
        int n = residuals.Length / 2;
        var perPoint = new double[n];
        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            double du = residuals[2 * i], dv = residuals[2 * i + 1];
            double sq = du * du + dv * dv;
            perPoint[i] = Math.Sqrt(sq);
            sum += perPoint[i];
            sumSq += sq;
        }
        return new PoseSolution(pose, perPoint, sum / n, Math.Sqrt(sumSq / n), converged, iterations);
    }

    private static double[] ToParams(CameraPose pose)
    {
        Vector3d r = pose.RotationVector;
        Vector3d t = pose.Translation;
        return new[] { r.x, r.y, r.z, t.x, t.y, t.z };
    }

    private static CameraPose FromParams(double[] p)
        => CameraPose.FromRodrigues(new Vector3d(p[0], p[1], p[2]), new Vector3d(p[3], p[4], p[5]));

    /// <summary>Residuals = projected - observed (u, v interleaved). Returns false if any point is behind the camera.</summary>
    private static bool TryResiduals(double[] p, IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                     PinholeCamera cam, double[] residuals)
    {
        Matrix3d r = CameraPose.RodriguesToMatrix(new Vector3d(p[0], p[1], p[2]));
        var t = new Vector3d(p[3], p[4], p[5]);
        for (int i = 0; i < world.Count; i++)
        {
            Vector3d pc = r * world[i] + t;
            if (!(pc.z > 0))
                return false;
            residuals[2 * i] = cam.Fx * pc.x / pc.z + cam.Cx - pixels[i].x;
            residuals[2 * i + 1] = cam.Fy * pc.y / pc.z + cam.Cy - pixels[i].y;
        }
        return true;
    }

    private static void NumericJacobian(double[] p, IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                        PinholeCamera cam, double[,] jacobian)
    {
        int m = 2 * world.Count;
        var plus = new double[m];
        var minus = new double[m];
        var q = (double[])p.Clone();
        for (int k = 0; k < ParamCount; k++)
        {
            double h = k < 3 ? RotationStep : TranslationStepMm;
            q[k] = p[k] + h;
            bool okPlus = TryResiduals(q, world, pixels, cam, plus);
            q[k] = p[k] - h;
            bool okMinus = TryResiduals(q, world, pixels, cam, minus);
            q[k] = p[k];
            if (!okPlus || !okMinus)
                throw new InvalidOperationException("Jacobian evaluation moved landmarks behind the camera.");
            for (int i = 0; i < m; i++)
                jacobian[i, k] = (plus[i] - minus[i]) / (2 * h);
        }
    }

    private static void BuildNormalEquations(double[,] j, double[] r, out double[,] jtj, out double[] jtr)
    {
        int m = r.Length;
        jtj = new double[ParamCount, ParamCount];
        jtr = new double[ParamCount];
        for (int a = 0; a < ParamCount; a++)
        {
            for (int i = 0; i < m; i++)
                jtr[a] += j[i, a] * r[i];
            for (int b = a; b < ParamCount; b++)
            {
                double s = 0;
                for (int i = 0; i < m; i++)
                    s += j[i, a] * j[i, b];
                jtj[a, b] = s;
                jtj[b, a] = s;
            }
        }
    }

    /// <summary>Gaussian elimination with partial pivoting. Returns NaNs if the system is singular.</summary>
    private static double[] SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    pivot = row;
            if (Math.Abs(a[pivot, col]) < 1e-300)
                return Enumerable.Repeat(double.NaN, n).ToArray();

            if (pivot != col)
            {
                for (int k = 0; k < n; k++)
                    (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double f = a[row, col] / a[col, col];
                for (int k = col; k < n; k++)
                    a[row, k] -= f * a[col, k];
                x[row] -= f * x[col];
            }
        }
        for (int row = n - 1; row >= 0; row--)
        {
            double s = x[row];
            for (int k = row + 1; k < n; k++)
                s -= a[row, k] * x[k];
            x[row] = s / a[row, row];
        }
        return x;
    }

    private static double SumSquares(double[] v)
    {
        double s = 0;
        foreach (double x in v) s += x * x;
        return s;
    }

    // ------------------------------------------------------------------ validation

    private static void ValidateInputs(IReadOnlyList<Vector3d> world, IReadOnlyList<Vector2d> pixels,
                                       PinholeCamera cam, int minPairs)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(cam);
        if (world.Count != pixels.Count)
            throw new ArgumentException($"Point count mismatch: {world.Count} world vs {pixels.Count} pixel points.");
        if (world.Count < minPairs)
            throw new ArgumentException($"At least {minPairs} point pairs are required (got {world.Count}).");
        for (int i = 0; i < world.Count; i++)
        {
            Vector3d w = world[i];
            Vector2d px = pixels[i];
            if (!double.IsFinite(w.x) || !double.IsFinite(w.y) || !double.IsFinite(w.z))
                throw new ArgumentException($"World point {i} is not finite: {w}.");
            if (!double.IsFinite(px.x) || !double.IsFinite(px.y))
                throw new ArgumentException($"Pixel point {i} is not finite: {px}.");
        }
    }
}
