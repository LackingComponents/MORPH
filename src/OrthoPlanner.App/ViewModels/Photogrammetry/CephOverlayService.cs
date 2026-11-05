using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using OrthoPlanner.App.ViewModels.Cephalometry;
using OrthoPlanner.Core.Photogrammetry.ProfileMatching;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

/// <summary>What the photo is registered on.</summary>
public enum OverlayReference
{
    /// <summary>Lateral silhouette of the CT soft-tissue mesh (true mm, no magnification).</summary>
    SkinMesh,
    /// <summary>Lateral radiograph with its ruler calibration (fallback).</summary>
    Xray,
}

/// <summary>
/// Outcome of a reference overlay run. <see cref="Registration"/> is null when the run was refused or a profile could not
/// be extracted; <see cref="Message"/> then says why. ROI curves are in DISPLAYED photo pixels. The registration
/// matrix maps reference pixels (X-ray pixels, or skin-silhouette pixels) to photo pixels.
/// </summary>
public sealed record CephOverlayResult(
    bool Success,
    string Message,
    OverlayReference Reference,
    ProfileRegistrationResult? Registration,
    CephXrayImage? Xray,
    SkinSilhouetteResult? Skin,
    IReadOnlyList<Point> PhotoRoi,
    IReadOnlyList<Point> XrayRoiInPhoto,
    string? RulerWarning,
    string? ReferenceDiagnostic,
    long ElapsedMs)
{
    public static CephOverlayResult Refused(OverlayReference reference, string message) =>
        new(false, message, reference, null, null, null, Array.Empty<Point>(), Array.Empty<Point>(), null, null, 0);

    /// <summary>All warnings shown to the user: registration warnings, then the ruler cross-check.</summary>
    public IReadOnlyList<string> AllWarnings =>
        (Registration?.Warnings ?? Array.Empty<string>())
            .Concat(RulerWarning is null ? Array.Empty<string>() : new[] { RulerWarning }).ToList();

    /// <summary>True when the overlay matrix exists (also when only a quality gate failed).</summary>
    public bool HasMatrix => Registration?.XrayToPhotoMatrix.Count == 6 &&
                             (Reference == OverlayReference.SkinMesh ? Skin?.Silhouette is not null : Xray is not null);

    public bool CanApplyScale => Registration is { Success: true } r && double.IsFinite(r.PhotoPixelsPerMm) && r.PhotoPixelsPerMm > 0;

    /// <summary>Skin mesh only: transform the silhouette was built with (row-major 4x4, column vectors).</summary>
    public double[]? SkinAffine { get; init; }
}

/// <summary>Per-photo overlay state, kept in memory only (saving it in the project is T4).</summary>
public partial class CephOverlayState : ObservableObject
{
    public CephOverlayState(CephOverlayResult result) => Result = result;

    public CephOverlayResult Result { get; }

    /// <summary>Reference the photo was registered on.</summary>
    public OverlayReference Reference => Result.Reference;

    [ObservableProperty] private double _opacity = 0.5;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _showProfiles = true;
}

/// <summary>
/// Automatic overlay of a reference on a profile photo (T1 extraction + T2 registration). Reference = lateral
/// silhouette of the CT skin mesh (true mm) or the lateral radiograph (ruler mm, with a ruler cross-check).
/// No UI code: <see cref="Validate"/> runs on the UI thread, <see cref="Run"/> on a background thread.
/// </summary>
public static class CephOverlayService
{
    private const double RulerTickToleranceMm = 0.02;
    private const double RoiMinConfidence = 0.05;
    private const double SkinMmPerPixel = 0.1;
    public const string SkinMeshScaleSource = "SkinMesh";

    /// <summary>Skin mesh captured on the UI thread: vertices (triangle soup, mm) and the displayed transform.</summary>
    public sealed record SkinInput(object Owner, float[] Vertices, double[] Affine);

    /// <summary>Everything the background run needs, captured on the UI thread.</summary>
    public sealed record Input(BitmapSource Photo, OverlayReference Reference, FacingOverride Facing,
                               CephXrayImage? Xray, XrayScale? Scale, SkinInput? Skin);

    // Last built silhouette (one entry): rebuilt only when the model, its vertex array or its transform change.
    private static readonly object SkinCacheLock = new();
    private static (object Owner, float[] Vertices, double[] Affine, SkinSilhouetteResult Result)? _skinCache;

    /// <summary>True when the soft-tissue model has a mesh usable as skin reference.</summary>
    public static bool HasSkinMesh(SegmentViewModel? softTissue) => softTissue?.Vertices is { Length: >= 9 };

    /// <summary>Returns the refusal, or null and the captured input when the overlay can run.</summary>
    public static CephOverlayResult? Validate(PhotoViewModel? photo, OverlayReference reference, CephXrayImage? xray,
                                              SegmentViewModel? softTissue, FacingOverride facing, out Input? input)
    {
        input = null;
        CephXrayImage? usedXray = null;
        XrayScale? scale = null;
        SkinInput? skin = null;
        if (reference == OverlayReference.SkinMesh)
        {
            if (!HasSkinMesh(softTissue))
                return CephOverlayResult.Refused(reference,
                    "No skin mesh: segment the soft tissue (Segmentation section) or choose X-ray.");
            skin = new SkinInput(softTissue!, softTissue!.Vertices!, SkinBuildAffine(softTissue));
        }
        else
        {
            if (xray is null)
                return CephOverlayResult.Refused(reference, "Load a lateral radiograph in the Cephalometry section first.");
            if (xray.Scale is not XrayScale s)
                return CephOverlayResult.Refused(reference, "Calibrate the X-ray ruler in the Cephalometry section first.");
            usedXray = xray;
            scale = s;
        }
        // The view rotation is irrelevant: registration works on unrotated image pixels.
        if (photo?.ImageSource is not BitmapSource image) return CephOverlayResult.Refused(reference, "No photo.");
        if (!image.IsFrozen) image = (BitmapSource)image.GetCurrentValueAsFrozen();
        input = new Input(image, reference, facing, usedXray, scale, skin);
        return null;
    }

    /// <summary>
    /// Transform the skin silhouette is built with: the model's own LocalTransform, WITHOUT the NHP rotation. In the
    /// CT frame the field-of-view cuts are horizontal, so SkinSilhouette trims them reliably (a tilted NHP frame
    /// leaves the cut edges in the front chain and breaks Auto-align for pitches around 10 deg). The NHP is applied
    /// afterwards by <see cref="NhpPitchDegrees"/>.
    /// </summary>
    public static double[] SkinBuildAffine(SegmentViewModel softTissue) => ToAffine(softTissue.LocalTransform.Value);

    /// <summary>WPF row-vector Matrix3D -> row-major 4x4 for column vectors (SkinSilhouette convention).</summary>
    public static double[] ToAffine(Matrix3D m) => new[]
    {
        m.M11, m.M21, m.M31, m.OffsetX,
        m.M12, m.M22, m.M32, m.OffsetY,
        m.M13, m.M23, m.M33, m.OffsetZ,
        m.M14, m.M24, m.M34, m.M44,
    };

    /// <summary>Extraction, registration and (X-ray only) ruler check. Never throws: errors come back as a message.</summary>
    public static CephOverlayResult Run(Input input)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return input.Reference == OverlayReference.SkinMesh ? RunSkin(input, sw) : RunXray(input, sw);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CephOverlayService.Run: {ex}");
            return CephOverlayResult.Refused(input.Reference, $"Overlay failed unexpectedly: {ex.Message}");
        }
    }

    private static ProfileExtractionResult ExtractPhoto(Input input)
    {
        // DECODED photo on purpose: contour, matrix and px/mm are then in displayed pixels, like annotations.
        var (rgb, w, h, stride) = ToRgb24(input.Photo);
        return ProfileExtractor.ExtractFromPhoto(rgb, w, h, stride, input.Facing);
    }

    private static CephOverlayResult RunSkin(Input input, Stopwatch sw)
    {
        var skin = SkinFor(input.Skin!);
        if (!skin.Success) return CephOverlayResult.Refused(input.Reference, $"Skin mesh: {skin.FailureReason}");

        var photoRes = ExtractPhoto(input);
        if (!photoRes.Success) return CephOverlayResult.Refused(input.Reference, $"Photo: {photoRes.FailureReason}");

        var reg = ProfileRegistration.Register(photoRes.Contour!, skin.Contour!, skin.MmPerPixel, skin.MmPerPixel,
                                               SkinMeshScaleSource);
        string diag = $"Skin silhouette: {skin.TriangleCount} triangles, {skin.Width}x{skin.Height} px at " +
                      $"{skin.MmPerPixel.ToString("F2", CultureInfo.InvariantCulture)} mm/px, built in {skin.ElapsedMs} ms.";
        return Finish(input.Reference, reg, photoRes.Contour!, skin.Contour!, null, skin, null, diag, sw)
            with { SkinAffine = input.Skin!.Affine };
    }

    private static CephOverlayResult RunXray(Input input, Stopwatch sw)
    {
        var photoRes = ExtractPhoto(input);
        if (!photoRes.Success) return CephOverlayResult.Refused(input.Reference, $"Photo: {photoRes.FailureReason}");

        var xray = input.Xray!;
        var scale = input.Scale!.Value;
        var rs = xray.Measurements.RulerStart;
        var re = xray.Measurements.RulerEnd;
        var xrayRes = ProfileExtractor.ExtractFromXray(xray.Luminance, xray.OriginalPixelWidth, xray.OriginalPixelHeight,
            rs is Point a ? (a.X, a.Y) : null, re is Point b ? (b.X, b.Y) : null, FacingOverride.Auto);
        if (!xrayRes.Success) return CephOverlayResult.Refused(input.Reference, $"Radiograph: {xrayRes.FailureReason}");

        var reg = ProfileRegistration.Register(photoRes.Contour!, xrayRes.Contour!,
                                               scale.MmPerPixelX, scale.MmPerPixelY, scale.Source);
        var (rulerWarning, rulerDiag) = RulerCheck(xray, scale);
        return Finish(input.Reference, reg, photoRes.Contour!, xrayRes.Contour!, xray, null, rulerWarning, rulerDiag, sw);
    }

    private static CephOverlayResult Finish(OverlayReference reference, ProfileRegistrationResult reg, ProfileContour photo,
        ProfileContour refContour, CephXrayImage? xray, SkinSilhouetteResult? skin, string? rulerWarning, string? diag,
        Stopwatch sw)
    {
        var photoRoi = Roi(photo, reg.PhotoLandmarks, confidentOnly: false);
        var refRoi = reg.XrayToPhotoMatrix.Count == 6
            ? Roi(refContour, reg.XrayLandmarks, confidentOnly: true).Select(p => Map(reg.XrayToPhotoMatrix, p)).ToList()
            : new List<Point>();
        string message = reg.Success ? "Aligned." : reg.FailureReason ?? "Registration failed without a reason.";
        return new CephOverlayResult(reg.Success, message, reference, reg, xray, skin, photoRoi, refRoi, rulerWarning, diag,
                                     sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Photo rotation (degrees, view convention: positive = clockwise on screen) that makes the NHP true vertical
    /// point straight down. The true inferior direction (0,0,-1) of the committed NHP frame is taken back to mesh
    /// space (inverse of <paramref name="committedAffine"/>), forward into the frame the silhouette was built in
    /// (<paramref name="buildAffine"/>), projected on the silhouette (column = Y, row = -Z) and mapped into photo
    /// pixels by the linear part of <paramref name="refToPhoto"/> (mirror included). Pure function, no WPF types.
    /// </summary>
    public static double NhpPitchDegrees(IReadOnlyList<double> refToPhoto, double[] buildAffine, double[] committedAffine)
    {
        if (refToPhoto.Count != 6) throw new ArgumentException("registration matrix must have 6 values");
        var u = Solve3(committedAffine, 0, 0, -1);
        double wy = buildAffine[4] * u.X + buildAffine[5] * u.Y + buildAffine[6] * u.Z;
        double wz = buildAffine[8] * u.X + buildAffine[9] * u.Y + buildAffine[10] * u.Z;
        double sx = wy, sy = -wz;   // silhouette pixel direction (scale irrelevant)
        double px = refToPhoto[0] * sx + refToPhoto[1] * sy, py = refToPhoto[3] * sx + refToPhoto[4] * sy;
        if (Math.Abs(px) + Math.Abs(py) < 1e-12)
            throw new InvalidOperationException("the NHP vertical is parallel to the projection direction");
        double theta = 90.0 - Math.Atan2(py, px) * 180.0 / Math.PI;
        theta %= 360.0;
        if (theta > 180) theta -= 360;
        if (theta <= -180) theta += 360;
        return theta;
    }

    // Solves L·u = b for the linear 3x3 part of a row-major 4x4 (Cramer's rule).
    private static (double X, double Y, double Z) Solve3(double[] a, double bx, double by, double bz)
    {
        double a00 = a[0], a01 = a[1], a02 = a[2], a10 = a[4], a11 = a[5], a12 = a[6], a20 = a[8], a21 = a[9], a22 = a[10];
        double det = a00 * (a11 * a22 - a12 * a21) - a01 * (a10 * a22 - a12 * a20) + a02 * (a10 * a21 - a11 * a20);
        if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("the NHP transform is not invertible");
        double x = (bx * (a11 * a22 - a12 * a21) - a01 * (by * a22 - a12 * bz) + a02 * (by * a21 - a11 * bz)) / det;
        double y = (a00 * (by * a22 - a12 * bz) - bx * (a10 * a22 - a12 * a20) + a02 * (a10 * bz - by * a20)) / det;
        double z = (a00 * (a11 * bz - by * a21) - a01 * (a10 * bz - by * a20) + bx * (a10 * a21 - a11 * a20)) / det;
        return (x, y, z);
    }

    /// <summary>Cached skin silhouette for this model + vertex array + transform; built on the calling thread.</summary>
    private static SkinSilhouetteResult SkinFor(SkinInput s)
    {
        lock (SkinCacheLock)
        {
            if (_skinCache is { } c && ReferenceEquals(c.Owner, s.Owner) && ReferenceEquals(c.Vertices, s.Vertices) &&
                c.Affine.AsSpan().SequenceEqual(s.Affine))
                return c.Result;
        }
        var result = SkinSilhouette.Build(s.Vertices, 3, s.Affine, SkinMmPerPixel);
        if (!result.Success) return result;
        lock (SkinCacheLock) _skinCache = (s.Owner, s.Vertices, s.Affine, result);
        return result;
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
