using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OrthoPlanner.App.ViewModels.Cephalometry;
using OrthoPlanner.Core.Photogrammetry.ProfileMatching;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

/// <summary>
/// Outcome of a ceph overlay run. <see cref="Registration"/> is null when the run was refused or a profile could not
/// be extracted; <see cref="Message"/> then says why. ROI curves are in DISPLAYED photo pixels.
/// </summary>
public sealed record CephOverlayResult(
    bool Success,
    string Message,
    ProfileRegistrationResult? Registration,
    CephXrayImage? Xray,
    IReadOnlyList<Point> PhotoRoi,
    IReadOnlyList<Point> XrayRoiInPhoto,
    string? RulerWarning,
    string? RulerDiagnostic,
    long ElapsedMs)
{
    public static CephOverlayResult Refused(string message) =>
        new(false, message, null, null, Array.Empty<Point>(), Array.Empty<Point>(), null, null, 0);

    /// <summary>All warnings shown to the user: registration warnings, then the ruler cross-check.</summary>
    public IReadOnlyList<string> AllWarnings =>
        (Registration?.Warnings ?? Array.Empty<string>())
            .Concat(RulerWarning is null ? Array.Empty<string>() : new[] { RulerWarning }).ToList();

    /// <summary>True when the overlay matrix exists (also when only a quality gate failed).</summary>
    public bool HasMatrix => Registration?.XrayToPhotoMatrix.Count == 6 && Xray is not null;

    public bool CanApplyScale => Registration is { Success: true } r && double.IsFinite(r.PhotoPixelsPerMm) && r.PhotoPixelsPerMm > 0;
}

/// <summary>Per-photo overlay state, kept in memory only (saving it in the project is T4).</summary>
public partial class CephOverlayState : ObservableObject
{
    public CephOverlayState(CephOverlayResult result) => Result = result;

    public CephOverlayResult Result { get; }

    [ObservableProperty] private double _opacity = 0.5;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _showProfiles = true;
}

/// <summary>
/// Automatic overlay of the lateral radiograph on a profile photo (T1 extraction + T2 registration), plus a ruler
/// cross-check. No UI code: <see cref="Validate"/> runs on the UI thread, <see cref="Run"/> on a background thread.
/// </summary>
public static class CephOverlayService
{
    private const double RulerTickToleranceMm = 0.02;
    private const double RoiMinConfidence = 0.05;

    /// <summary>Everything the background run needs, captured on the UI thread.</summary>
    public sealed record Input(BitmapSource Photo, CephXrayImage Xray, XrayScale Scale, FacingOverride Facing);

    /// <summary>Returns the refusal, or null and the captured input when the overlay can run.</summary>
    public static CephOverlayResult? Validate(PhotoViewModel? photo, CephXrayImage? xray, FacingOverride facing, out Input? input)
    {
        input = null;
        if (xray is null) return CephOverlayResult.Refused("Load a lateral radiograph in the Cephalometry section first.");
        if (xray.Scale is not XrayScale scale)
            return CephOverlayResult.Refused("Calibrate the X-ray ruler in the Cephalometry section first.");
        if (photo?.ImageSource is not BitmapSource image) return CephOverlayResult.Refused("No photo.");
        if (photo.RotationAngle != 0)
            return CephOverlayResult.Refused("Reset the photo rotation to 0° before the overlay (rotation is visual " +
                                             "only and would misplace the overlay).");
        if (!image.IsFrozen) image = (BitmapSource)image.GetCurrentValueAsFrozen();
        input = new Input(image, xray, scale, facing);
        return null;
    }

    /// <summary>Extraction, registration and ruler check. Never throws: errors come back as a message.</summary>
    public static CephOverlayResult Run(Input input)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return RunCore(input, sw);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CephOverlayService.Run: {ex}");
            return CephOverlayResult.Refused($"Overlay failed unexpectedly: {ex.Message}");
        }
    }

    private static CephOverlayResult RunCore(Input input, Stopwatch sw)
    {
        // DECODED photo on purpose: contour, matrix and px/mm are then in displayed pixels, like annotations.
        var (rgb, w, h, stride) = ToRgb24(input.Photo);
        var photoRes = ProfileExtractor.ExtractFromPhoto(rgb, w, h, stride, input.Facing);
        if (!photoRes.Success) return CephOverlayResult.Refused($"Photo: {photoRes.FailureReason}");

        var xray = input.Xray;
        var rs = xray.Measurements.RulerStart;
        var re = xray.Measurements.RulerEnd;
        var xrayRes = ProfileExtractor.ExtractFromXray(xray.Luminance, xray.OriginalPixelWidth, xray.OriginalPixelHeight,
            rs is Point a ? (a.X, a.Y) : null, re is Point b ? (b.X, b.Y) : null, FacingOverride.Auto);
        if (!xrayRes.Success) return CephOverlayResult.Refused($"Radiograph: {xrayRes.FailureReason}");

        var reg = ProfileRegistration.Register(photoRes.Contour!, xrayRes.Contour!,
                                               input.Scale.MmPerPixelX, input.Scale.MmPerPixelY, input.Scale.Source);
        var (rulerWarning, rulerDiag) = RulerCheck(xray, input.Scale);

        var photoRoi = Roi(photoRes.Contour!, reg.PhotoLandmarks, confidentOnly: false);
        var xrayRoi = reg.XrayToPhotoMatrix.Count == 6
            ? Roi(xrayRes.Contour!, reg.XrayLandmarks, confidentOnly: true).Select(p => Map(reg.XrayToPhotoMatrix, p)).ToList()
            : new List<Point>();
        string message = reg.Success
            ? "Aligned."
            : reg.FailureReason ?? "Registration failed without a reason.";
        return new CephOverlayResult(reg.Success, message, reg, xray, photoRoi, xrayRoi, rulerWarning, rulerDiag,
                                     sw.ElapsedMilliseconds);
    }

    private static (byte[] Rgb, int W, int H, int Stride) ToRgb24(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Rgb24, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 3;
        var buf = new byte[stride * h];
        conv.CopyPixels(buf, stride, 0);
        return (buf, w, h, stride);
    }

    /// <summary>
    /// Re-measures the tick period along the saved ruler segment. A ruler calibration that does not mean
    /// 1 tick = 1 mm is reported as a warning (never blocking); a failed detection is only a diagnostic.
    /// </summary>
    private static (string? Warning, string? Diagnostic) RulerCheck(CephXrayImage xray, XrayScale scale)
    {
        if (scale.Source != XrayScale.SourceRuler) return (null, "Ruler check skipped: the X-ray scale is not from the ruler.");
        if (xray.Measurements.RulerStart is not Point s || xray.Measurements.RulerEnd is not Point e)
            return (null, "Ruler check skipped: the ruler segment was not saved.");
        try
        {
            var det = RulerTickDetector.Detect(xray.Luminance, xray.OriginalPixelWidth, xray.OriginalPixelHeight, s.X, s.Y, e.X, e.Y);
            double implied = det.PeriodPx * scale.MmPerPixelX;
            string diag = $"Ruler check: tick period {det.PeriodPx.ToString("F2", CultureInfo.InvariantCulture)} px -> " +
                          $"one tick = {implied.ToString("F3", CultureInfo.InvariantCulture)} mm.";
            if (Math.Abs(implied - 1.0) <= RulerTickToleranceMm) return (null, diag);
            string pct = ((implied - 1) * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
            return ($"Ruler check: the saved calibration means one tick = {implied.ToString("F2", CultureInfo.InvariantCulture)} mm. " +
                    $"If the ruler ticks are 1 mm, every mm is off by {pct}%. Recalibrate with 'Auto from ticks'.", diag);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return (null, $"Ruler check not possible: {ex.Message}");
        }
    }

    // Contour points between the points nearest to nasion and menton (X-ray: confident points only).
    private static List<Point> Roi(ProfileContour c, ProfileLandmarks? lm, bool confidentOnly)
    {
        if (lm is null || c.Points.Count == 0) return new List<Point>();
        int a = Nearest(c, lm.Nasion), b = Nearest(c, lm.Menton);
        if (a > b) (a, b) = (b, a);
        return Enumerable.Range(a, b - a + 1)
            .Where(i => !confidentOnly || c.Confidence[i] > RoiMinConfidence)
            .Select(i => new Point(c.Points[i].X, c.Points[i].Y)).ToList();
    }

    private static int Nearest(ProfileContour c, (double X, double Y) q)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < c.Points.Count; i++)
        {
            double dx = c.Points[i].X - q.X, dy = c.Points[i].Y - q.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private static Point Map(IReadOnlyList<double> m, Point p) =>
        new(m[0] * p.X + m[1] * p.Y + m[2], m[3] * p.X + m[4] * p.Y + m[5]);
}
