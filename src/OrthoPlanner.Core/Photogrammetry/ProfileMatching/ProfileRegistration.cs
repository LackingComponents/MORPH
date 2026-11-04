using System.Globalization;

namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>
/// Automatic registration of a profile photo onto a lateral cephalogram through their soft-tissue profiles
/// (port of the validated T2 prototype). The X-ray contour is converted to millimetres first, so the similarity
/// photo px &lt;-&gt; X-ray mm has a scale that IS the photo px per mm. Three solutions are computed (photo to
/// X-ray, X-ray to photo, symmetric); the symmetric one is the final value.
/// </summary>
public static partial class ProfileRegistration
{
    private const double XrayRoiStepMm = 0.1;
    private const double PhotoRoiStepPx = 1.0;
    private const int SourceStride = 2;
    private const int MaxIterations = 80;
    private static readonly double[] StartRotationsDeg = { -6, -3, 0, 3, 6 };
    private static readonly double[] StartScaleFactors = { 0.94, 1.0, 1.06 };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string F(double v, string fmt = "F3") => v.ToString(fmt, Inv);

    /// <summary>Internal working data: both curves facing +X, photo in px, X-ray in mm.</summary>
    private sealed record Prepared(
        List<(double X, double Y)> PhotoRoi, List<(double X, double Y)> XrayRoi,
        LandmarkIndices PhotoLm, LandmarkIndices XrayLm,
        IReadOnlyList<(double X, double Y)> PhotoAll, IReadOnlyList<(double X, double Y)> XrayAll,
        double PhotoSign, double XraySign);

    public static ProfileRegistrationResult Register(ProfileContour photo, ProfileContour xray,
                                                     double xrayMmPerPixelX, double xrayMmPerPixelY,
                                                     string xrayScaleSource)
    {
        var diag = new List<string>();
        var warnings = new List<string>();
        string? invalid = ValidateInputs(photo, xray, xrayMmPerPixelX, xrayMmPerPixelY);
        if (invalid is not null) return Fail(invalid, warnings, diag);

        try
        {
            return RegisterCore(photo, xray, xrayMmPerPixelX, xrayMmPerPixelY, xrayScaleSource ?? "", warnings, diag);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail($"registration crashed: {ex.GetType().Name}: {ex.Message}", warnings, diag);
        }
    }

    private static string? ValidateInputs(ProfileContour? photo, ProfileContour? xray, double mmx, double mmy)
    {
        if (photo is null) return "photo contour is null";
        if (xray is null) return "X-ray contour is null";
        if (photo.Points.Count != photo.Confidence.Count) return "photo contour: points and confidence differ in length";
        if (xray.Points.Count != xray.Confidence.Count) return "X-ray contour: points and confidence differ in length";
        if (photo.Points.Count < 50) return $"photo contour too short ({photo.Points.Count} points)";
        if (xray.Points.Count < 50) return $"X-ray contour too short ({xray.Points.Count} points)";
        if (!double.IsFinite(mmx) || mmx <= 0 || !double.IsFinite(mmy) || mmy <= 0)
            return $"invalid X-ray scale ({mmx} x {mmy} mm/px): a calibrated scale is required";
        return null;
    }

    private static ProfileRegistrationResult RegisterCore(ProfileContour photo, ProfileContour xray, double mmx,
        double mmy, string scaleSource, List<string> warnings, List<string> diag)
    {
        RepeatT1Warnings(photo, "photo", warnings);
        RepeatT1Warnings(xray, "X-ray", warnings);
        diag.Add($"X-ray scale {F(mmx, "F5")} x {F(mmy, "F5")} mm/px (source: {(scaleSource.Length > 0 ? scaleSource : "unknown")})");

        var prep = Prepare(photo, xray, mmx, mmy, diag, out string? failure);
        if (prep is null) return Fail(failure!, warnings, diag);
        bool mirrored = prep.PhotoSign != prep.XraySign;
        var photoLandmarks = ToOriginal(prep.PhotoAll, prep.PhotoLm, p => (prep.PhotoSign * p.X, p.Y));
        var xrayLandmarks = ToOriginal(prep.XrayAll, prep.XrayLm, p => (prep.XraySign * p.X / mmx, p.Y / mmy));
        var partial = new ProfileRegistrationResult
        {
            PhotoMirrored = mirrored, PhotoLandmarks = photoLandmarks, XrayLandmarks = xrayLandmarks,
        };

        var solutions = Solve(prep, diag, out failure);
        if (solutions is null) return Fail(failure!, warnings, diag, partial);

        var result = BuildResult(prep, solutions, mmx, mmy, scaleSource, warnings, diag) with
        {
            PhotoMirrored = mirrored, PhotoLandmarks = photoLandmarks, XrayLandmarks = xrayLandmarks,
        };
        return result;
    }

    /// <summary>Mirroring to +X, mm conversion, landmarks and the nasion..menton regions of interest.</summary>
    private static Prepared? Prepare(ProfileContour photo, ProfileContour xray, double mmx, double mmy,
                                     List<string> diag, out string? failure)
    {
        double ps = photo.FacingRight ? 1 : -1, xs = xray.FacingRight ? 1 : -1;
        var photoPts = photo.Points.Select(p => (ps * p.X, p.Y)).ToList();
        var xrayPts = xray.Points.Select(p => (xs * p.X * mmx, p.Y * mmy)).ToList();
        diag.Add($"facing: photo {(photo.FacingRight ? "right" : "left")}, X-ray {(xray.FacingRight ? "right" : "left")}" +
                 $"{(ps != xs ? " -> photo mirrored" : "")}");

        var pl = ProfileCurveTools.FindLandmarks(photoPts, photo.Confidence, out string? pf);
        if (pl is null) { failure = $"photo: {pf}"; return null; }
        var xl = ProfileCurveTools.FindLandmarks(xrayPts, xray.Confidence, out string? xf);
        if (xl is null) { failure = $"X-ray: {xf}"; return null; }
        diag.Add($"landmark indices photo nas/prn/men {pl.Value.Nasion}/{pl.Value.Pronasale}/{pl.Value.Menton}, " +
                 $"X-ray {xl.Value.Nasion}/{xl.Value.Pronasale}/{xl.Value.Menton}");

        var xrayRoiRaw = Enumerable.Range(xl.Value.Nasion, xl.Value.Menton - xl.Value.Nasion + 1)
            .Where(i => xray.Confidence[i] > ProfileCurveTools.MinConfidence).Select(i => xrayPts[i]).ToList();
        var photoRoiRaw = photoPts.GetRange(pl.Value.Nasion, pl.Value.Menton - pl.Value.Nasion + 1);
        if (xrayRoiRaw.Count < 2) { failure = "X-ray profile region too short (no confident points between nasion and menton)"; return null; }

        double xrayLen = ProfileCurveTools.ArcLength(xrayRoiRaw)[^1];
        diag.Add($"ROI: X-ray {F(xrayLen, "F1")} mm ({xrayRoiRaw.Count} confident points), " +
                 $"photo {F(ProfileCurveTools.ArcLength(photoRoiRaw)[^1], "F0")} px");
        if (xrayLen < MinXrayRoiMm)
        {
            failure = $"profile region too short on the X-ray ({F(xrayLen, "F1")} mm nasion-menton, need {F(MinXrayRoiMm, "F0")} mm)";
            return null;
        }

        var xrayRoi = ProfileCurveTools.Resample(xrayRoiRaw, XrayRoiStepMm);
        var photoRoi = ProfileCurveTools.Resample(photoRoiRaw, PhotoRoiStepPx);
        if (photoRoi.Count < 20) { failure = $"photo profile region too short ({photoRoi.Count} px)"; return null; }
        failure = null;
        return new Prepared(photoRoi, xrayRoi, pl.Value, xl.Value, photoPts, xrayPts, ps, xs);
    }

    private static ProfileLandmarks ToOriginal(IReadOnlyList<(double X, double Y)> pts, LandmarkIndices lm,
                                               Func<(double X, double Y), (double X, double Y)> back) =>
        new(back(pts[lm.Nasion]), back(pts[lm.Pronasale]), back(pts[lm.Menton]));

    /// <summary>The three transforms (internal frames) and the matchers needed for the reports.</summary>
    private sealed record Solutions(
        Similarity2D B, Similarity2D A, Similarity2D S,
        PolylineMatcher XrayCurve, PolylineMatcher PhotoCurve, PolylineMatcher XrayHalf, PolylineMatcher PhotoHalf,
        List<(double X, double Y)> PhotoSrc, List<(double X, double Y)> XraySrc);

    private static Solutions? Solve(Prepared prep, List<string> diag, out string? failure)
    {
        failure = null;
        var xrayCurve = new PolylineMatcher(prep.XrayRoi);
        var photoCurve = new PolylineMatcher(prep.PhotoRoi);
        var photoSrc = ProfileCurveTools.Every(prep.PhotoRoi, SourceStride);
        var xraySrc = ProfileCurveTools.Every(prep.XrayRoi, SourceStride);
        var photoHalf = new PolylineMatcher(photoSrc);
        var xrayHalf = new PolylineMatcher(xraySrc);

        var lmPhoto = new[] { prep.PhotoLm.Nasion, prep.PhotoLm.Pronasale, prep.PhotoLm.Menton }.Select(i => prep.PhotoAll[i]).ToList();
        var lmXray = new[] { prep.XrayLm.Nasion, prep.XrayLm.Pronasale, prep.XrayLm.Menton }.Select(i => prep.XrayAll[i]).ToList();
        var init = Similarity2D.Fit(lmPhoto, lmXray);
        if (init is null) { failure = "landmarks are degenerate (coincident points), cannot initialise"; return null; }
        diag.Add($"landmark init: {F(1 / init.Value.Scale, "F3")} photo px/mm, rotation {F(init.Value.Inverse().RotationDeg, "F2")} deg");

        var b = MultiStart(init.Value, lmPhoto[1], lmXray[1], photoSrc, xrayCurve, diag);
        if (b is null) { failure = "photo to X-ray ICP did not converge from any start"; return null; }
        var bFinal = TrimmedIcp.Run(photoSrc, xrayCurve, b.Value, MaxIterations);
        var aFinal = bFinal is null ? null : TrimmedIcp.Run(xraySrc, photoCurve, bFinal.Value.T.Inverse(), MaxIterations);
        if (bFinal is null || aFinal is null) { failure = "ICP became degenerate (no valid correspondences)"; return null; }
        var s = TrimmedIcp.RunSymmetric(photoSrc, photoHalf, xraySrc, xrayHalf, bFinal.Value.T, MaxIterations);
        if (s is null) { failure = "symmetric ICP became degenerate (no valid correspondences)"; return null; }

        return new Solutions(bFinal.Value.T, aFinal.Value.T, s.Value, xrayCurve, photoCurve, xrayHalf, photoHalf, photoSrc, xraySrc);
    }

    /// <summary>Starts around the landmark fit (rotation x scale about the pronasale); keeps the lowest RMS80.</summary>
    private static Similarity2D? MultiStart(Similarity2D init, (double X, double Y) photoPrn, (double X, double Y) xrayPrn,
                                            List<(double X, double Y)> photoSrc, PolylineMatcher xrayCurve, List<string> diag)
    {
        Similarity2D? best = null;
        double bestScore = double.MaxValue, bestRot = 0, bestScale = 0;
        foreach (double rotDeg in StartRotationsDeg)
            foreach (double factor in StartScaleFactors)
            {
                double theta = init.Theta + rotDeg * Math.PI / 180, scale = init.Scale * factor;
                var rotated = new Similarity2D(scale, theta, 0, 0).Apply(photoPrn);
                var start = new Similarity2D(scale, theta, xrayPrn.X - rotated.X, xrayPrn.Y - rotated.Y);
                var run = TrimmedIcp.Run(photoSrc, xrayCurve, start, MaxIterations);
                if (run is null) continue;
                double score = RmsBest80(run.Value.Final.Where(c => c.Valid).Select(c => c.Residual).ToList());
                if (double.IsFinite(score) && score < bestScore)
                {
                    (bestScore, best, bestRot, bestScale) = (score, run.Value.T, rotDeg, factor);
                }
            }
        if (best is not null)
            diag.Add($"multi-start: best start rotation {F(bestRot, "+0;-0;0")} deg, scale x{F(bestScale, "F2")}, RMS80 {F(bestScore)} mm");
        return best;
    }

    private static void RepeatT1Warnings(ProfileContour c, string label, List<string> warnings)
    {
        foreach (string line in c.Diagnostics.Where(d => d.StartsWith("WARNING", StringComparison.Ordinal)))
            warnings.Add($"{label} contour (T1): {line}");
    }

    private static ProfileRegistrationResult Fail(string reason, List<string> warnings, List<string> diag,
                                                  ProfileRegistrationResult? partial = null) =>
        (partial ?? new ProfileRegistrationResult()) with
        {
            Success = false, FailureReason = reason, PhotoPixelsPerMm = double.NaN,
            XrayToPhotoMatrix = Array.Empty<double>(),
            Warnings = warnings.ToList().AsReadOnly(), Diagnostics = diag.ToList().AsReadOnly(),
        };
}
