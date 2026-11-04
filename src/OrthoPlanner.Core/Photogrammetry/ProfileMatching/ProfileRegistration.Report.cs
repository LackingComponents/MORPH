using System.Diagnostics;

namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

public static partial class ProfileRegistration
{
    // Quality gates: initial thresholds, to be tuned on more cases.
    private const double MinXrayRoiMm = 40.0;           // to be tuned on more cases
    private const double FailRmsBest80Mm = 1.5;         // to be tuned on more cases
    private const double FailMinCoverage = 0.5;         // to be tuned on more cases
    private const double WarnP95Mm = 2.0;               // to be tuned on more cases
    private const double WarnRmsBest80Mm = 0.5;         // to be tuned on more cases
    private const double WarnScaleDifferencePercent = 1.0; // to be tuned on more cases
    private const double WarnRotationDeg = 20.0;        // to be tuned on more cases

    private const int BootstrapResamples = 200;
    private const int BootstrapIterations = 20;
    private const int BootstrapSeed = 12345;
    private const double BootstrapBlockMm = 5.0;

    private static ProfileRegistrationResult BuildResult(Prepared prep, Solutions sol, double mmx, double mmy,
        string scaleSource, List<string> warnings, List<string> diag)
    {
        int xrayN = sol.XrayCurve.Count;
        var repB = Report(sol.XrayCurve.Match(sol.B.Apply(sol.PhotoSrc)), 1.0, c => c.Vertex, xrayN,
                          1 / sol.B.Scale, sol.B.Inverse().RotationDeg);
        var repA = Report(sol.PhotoCurve.Match(sol.A.Apply(sol.XraySrc)), sol.A.Scale, (_, i) => i * SourceStride, xrayN,
                          sol.A.Scale, sol.A.RotationDeg);
        var repS = Report(sol.XrayCurve.Match(sol.S.Apply(prep.PhotoRoi)), 1.0, c => c.Vertex, xrayN,
                          1 / sol.S.Scale, sol.S.Inverse().RotationDeg);
        double diffAB = (repA.PhotoPixelsPerMm / repB.PhotoPixelsPerMm - 1) * 100;

        var trip = prep.XrayRoi.Select(c => Dist(sol.B.Apply(sol.A.Apply(c)), c)).ToList();
        var back = sol.PhotoCurve.Match(sol.S.Inverse().Apply(prep.XrayRoi));
        double coverage = (double)back.Count(c => c.Valid) / back.Length;
        diag.Add($"overlap: {F(100 * coverage, "F0")}% of the X-ray ROI samples match inside the photo ROI (symmetric solution)");

        var (ciLow, ciHigh, n, ms) = BootstrapScale(prep, sol);
        diag.Add($"bootstrap: {n} block resamples (blocks of {F(BootstrapBlockMm, "F0")} mm, seed {BootstrapSeed}, " +
                 $"{BootstrapIterations} iterations each) in {ms} ms");

        var matrix = XrayToPhotoMatrix(sol.S, prep.PhotoSign, prep.XraySign, mmx, mmy);
        ApplyWarnings(repS, diffAB, scaleSource, warnings);
        string? gate = repS.RmsBest80Mm > FailRmsBest80Mm
            ? $"profiles do not match: wrong contour on one image or different patient (RMS of best 80% " +
              $"{F(repS.RmsBest80Mm, "F2")} mm > {F(FailRmsBest80Mm, "F1")} mm)"
            : coverage < FailMinCoverage
                ? $"too little overlap between the two profiles ({F(100 * coverage, "F0")}% of the X-ray ROI matched, " +
                  $"need {F(100 * FailMinCoverage, "F0")}%)"
                : null;

        return new ProfileRegistrationResult
        {
            Success = gate is null,
            FailureReason = gate,
            PhotoPixelsPerMm = gate is null ? repS.PhotoPixelsPerMm : double.NaN,
            RotationDeg = repS.RotationDeg,
            XrayToPhotoMatrix = matrix,
            PhotoToXray = repB, XrayToPhoto = repA, Symmetric = repS,
            ScaleDifferenceAvsBPercent = diffAB,
            RoundTripMeanMm = trip.Average(), RoundTripMaxMm = trip.Max(),
            ScaleCi95LowPxPerMm = ciLow, ScaleCi95HighPxPerMm = ciHigh,
            Warnings = warnings.ToList().AsReadOnly(), Diagnostics = diag.ToList().AsReadOnly(),
        };
    }

    private static SolutionReport Report(Correspondence[] corr, double residualDivisor, Func<Correspondence, int> xrayIndex,
                                         int xrayCount, double pxPerMm, double rotationDeg) =>
        Report(corr, residualDivisor, (c, _) => xrayIndex(c), xrayCount, pxPerMm, rotationDeg);

    /// <summary>Residual statistics in mm; zones = thirds of the X-ray ROI by arc length (uniform samples).</summary>
    private static SolutionReport Report(Correspondence[] corr, double residualDivisor, Func<Correspondence, int, int> xrayIndex,
                                         int xrayCount, double pxPerMm, double rotationDeg)
    {
        var res = new List<double>();
        var zones = new[] { new List<double>(), new List<double>(), new List<double>() };
        for (int i = 0; i < corr.Length; i++)
        {
            if (!corr[i].Valid) continue;
            double r = corr[i].Residual / residualDivisor;
            res.Add(r);
            zones[Math.Clamp(3 * xrayIndex(corr[i], i) / xrayCount, 0, 2)].Add(r);
        }
        return new SolutionReport(pxPerMm, rotationDeg, RmsBest80(res), TrimmedIcp.Quantile(res, 0.5),
                                  TrimmedIcp.Quantile(res, 0.95), res.Count,
                                  zones.Select(z => z.Count > 0 ? TrimmedIcp.Quantile(z, 0.5) : double.NaN).ToList().AsReadOnly());
    }

    /// <summary>RMS of the best 80% (sorted, first floor(0.8 n) values), as in the prototype.</summary>
    private static double RmsBest80(List<double> residuals)
    {
        int k = (int)(TrimmedIcp.Keep * residuals.Count);
        if (k == 0) return double.NaN;
        return Math.Sqrt(residuals.OrderBy(r => r).Take(k).Average(r => r * r));
    }

    /// <summary>
    /// Block bootstrap of the symmetric scale. Blocks of 5 mm of the photo ROI are drawn with replacement (fixed seed);
    /// instead of concatenating them (which would break the polyline the point-to-line matching needs), each photo
    /// sample is weighted by how many times its block was drawn. Symmetric ICP restarts from the final transform.
    /// </summary>
    private static (double Low, double High, int Count, long Ms) BootstrapScale(Prepared prep, Solutions sol)
    {
        var sw = Stopwatch.StartNew();
        int n = sol.PhotoSrc.Count;
        double pxPerMm = 1 / sol.S.Scale;
        int block = Math.Clamp((int)Math.Round(BootstrapBlockMm * pxPerMm / (PhotoRoiStepPx * SourceStride)), 1, n);
        int blocksPerSample = (n + block - 1) / block;
        var rng = new Random(BootstrapSeed);
        var weightSets = new double[BootstrapResamples][];
        for (int b = 0; b < BootstrapResamples; b++)
        {
            var w = new double[n];
            for (int k = 0; k < blocksPerSample; k++)
            {
                int start = rng.Next(0, n - block + 1);
                for (int i = start; i < start + block; i++) w[i]++;
            }
            weightSets[b] = w;
        }

        var scales = new double[BootstrapResamples];
        Parallel.For(0, BootstrapResamples, b =>
        {
            var t = TrimmedIcp.RunSymmetric(sol.PhotoSrc, sol.PhotoHalf, sol.XraySrc, sol.XrayHalf, sol.S,
                                            BootstrapIterations, weightSets[b]);
            scales[b] = t is null ? double.NaN : 1 / t.Value.Scale;
        });
        var valid = scales.Where(double.IsFinite).ToList();
        return (TrimmedIcp.Quantile(valid, 0.025), TrimmedIcp.Quantile(valid, 0.975), valid.Count, sw.ElapsedMilliseconds);
    }

    /// <summary>X-ray original px -> photo original px: mm conversion and facing flip, inverse of S, photo flip back.</summary>
    private static IReadOnlyList<double> XrayToPhotoMatrix(Similarity2D s, double photoSign, double xraySign,
                                                           double mmx, double mmy)
    {
        var inv = s.Inverse();
        double c = Math.Cos(inv.Theta) * inv.Scale, sn = Math.Sin(inv.Theta) * inv.Scale;
        // internal photo = L * (xraySign*mmx*u, mmy*v) + t ; photo = (photoSign*xi, yi)
        double a = photoSign * c * xraySign * mmx, b = photoSign * -sn * mmy, tx = photoSign * inv.Tx;
        double cc = sn * xraySign * mmx, d = c * mmy, ty = inv.Ty;
        return new[] { a, b, tx, cc, d, ty };
    }

    private static void ApplyWarnings(SolutionReport s, double diffAB, string scaleSource, List<string> warnings)
    {
        if (s.P95Mm > WarnP95Mm)
            warnings.Add($"large local differences (p95 {F(s.P95Mm, "F2")} mm; lips/mandible posture, expression or contour " +
                         "error): use a photo with lips closed in the same position as the radiograph");
        if (s.RmsBest80Mm > WarnRmsBest80Mm)
            warnings.Add($"mediocre fit (RMS of best 80% {F(s.RmsBest80Mm, "F2")} mm)");
        if (Math.Abs(diffAB) > WarnScaleDifferencePercent)
            warnings.Add($"the two directions disagree (scale difference {F(diffAB, "+0.00;-0.00")}%)");
        if (Math.Abs(s.RotationDeg) > WarnRotationDeg)
            warnings.Add($"unusual head rotation between photo and radiograph ({F(s.RotationDeg, "F1")} deg)");
        if (scaleSource is not ("Ruler" or "SkinMesh"))   // both are calibrated mm sources
            warnings.Add("X-ray scale is not from the ruler calibration: DICOM spacing is often measured at the detector " +
                         "and can overestimate mm by ~10% (qualitative, device dependent)");
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
