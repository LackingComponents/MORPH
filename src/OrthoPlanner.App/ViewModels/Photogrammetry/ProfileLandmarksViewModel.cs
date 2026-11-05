using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OrthoPlanner.Core.Photogrammetry.ProfileMatching;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

/// <summary>One landmark as shown: image-pixel position; <see cref="Edited"/> = moved by the user.</summary>
public sealed record ProfileLandmarkPoint(ProfileLandmarkId Id, Point Position, int ContourIndex,
                                          LandmarkConfidence Confidence, string Reason, bool Edited)
{
    public string Label => ProfileLandmarkDetector.Label(Id);

    /// <summary>True when a measurement using this point should carry a "check" marker.</summary>
    public bool NeedsCheck => !Edited && Confidence == LandmarkConfidence.Low;
}

/// <summary>One row of the facial-thirds table. <see cref="Mm"/> is null when a landmark is missing.</summary>
public sealed record ThirdsRow(string Label, double? Mm, double? Percent, string Warning, bool IsGroupStart)
{
    public string MmText => Mm is double v ? $"{v:F1} mm" : "—";
    public string PercentText => Percent is double p ? $"{p:F0}%" : "";
}

/// <summary>
/// Profile landmarks of one photo. IMMUTABLE: every edit returns a new instance, so the undo history can keep a
/// reference to the previous state. Positions are in image pixels (rotation independent).
/// </summary>
public sealed class ProfileLandmarksViewModel
{
    public ProfileLandmarksViewModel(IReadOnlyList<Point> contour, bool facingRight,
        IReadOnlyDictionary<ProfileLandmarkId, ProfileLandmarkPoint> automatic,
        IReadOnlyDictionary<ProfileLandmarkId, ProfileLandmarkPoint> points,
        IReadOnlyList<ProfileLandmarkId> missing, IReadOnlyList<string> diagnostics)
    {
        Contour = contour;
        FacingRight = facingRight;
        Automatic = automatic;
        Points = points;
        Missing = missing;
        Diagnostics = diagnostics;
    }

    /// <summary>Photo profile contour (image px, top to bottom) used for snapping and the profile line.</summary>
    public IReadOnlyList<Point> Contour { get; }
    public bool FacingRight { get; }
    public IReadOnlyDictionary<ProfileLandmarkId, ProfileLandmarkPoint> Automatic { get; }
    public IReadOnlyDictionary<ProfileLandmarkId, ProfileLandmarkPoint> Points { get; }
    public IReadOnlyList<ProfileLandmarkId> Missing { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    public static ProfileLandmarksViewModel FromDetection(ProfileLandmarkResult result, IReadOnlyList<Point> contour, bool facingRight)
    {
        var auto = result.Landmarks.ToDictionary(l => l.Id,
            l => new ProfileLandmarkPoint(l.Id, new Point(l.X, l.Y), l.ContourIndex, l.Confidence, l.Reason, false));
        return new ProfileLandmarksViewModel(contour, facingRight, auto, auto, result.Missing, result.Diagnostics);
    }

    public ProfileLandmarksViewModel WithMoved(ProfileLandmarkId id, Point position, int contourIndex)
    {
        if (!Points.TryGetValue(id, out var old)) return this;
        var points = Points.ToDictionary(kv => kv.Key, kv => kv.Value);
        points[id] = old with { Position = position, ContourIndex = contourIndex, Edited = true, Reason = "moved by the user" };
        return new ProfileLandmarksViewModel(Contour, FacingRight, Automatic, points, Missing, Diagnostics);
    }

    public ProfileLandmarksViewModel WithReset(ProfileLandmarkId id)
    {
        if (!Automatic.TryGetValue(id, out var auto)) return this;
        var points = Points.ToDictionary(kv => kv.Key, kv => kv.Value);
        points[id] = auto;
        return new ProfileLandmarksViewModel(Contour, FacingRight, Automatic, points, Missing, Diagnostics);
    }

    /// <summary>Index of the contour sample nearest to <paramref name="p"/> (image px).</summary>
    public int NearestContourIndex(Point p)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < Contour.Count; i++)
        {
            double dx = Contour[i].X - p.X, dy = Contour[i].Y - p.Y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Vertical coordinate in the NHP frame (image px): the photo rotation makes it vertical on screen.</summary>
    public static double NhpVertical(Point p, double rotationDeg)
    {
        double r = rotationDeg * Math.PI / 180;
        return p.X * Math.Sin(r) + p.Y * Math.Cos(r);
    }

    /// <summary>Facial thirds as vertical distances in the NHP frame (mm and % of the reference span).</summary>
    public IReadOnlyList<ThirdsRow> Thirds(double rotationDeg, double pxPerMm)
    {
        double? Dist(ProfileLandmarkId a, ProfileLandmarkId b) =>
            Points.TryGetValue(a, out var pa) && Points.TryGetValue(b, out var pb) && pxPerMm > 0
                ? Math.Abs(NhpVertical(pb.Position, rotationDeg) - NhpVertical(pa.Position, rotationDeg)) / pxPerMm
                : null;
        string Warn(params ProfileLandmarkId[] ids) => string.Join(" ", ids.Distinct()
            .Where(id => !Points.TryGetValue(id, out var p) || p.NeedsCheck)
            .Select(id => "⚠ check " + ProfileLandmarkDetector.Label(id)));
        ThirdsRow Row(string label, ProfileLandmarkId a, ProfileLandmarkId b, ProfileLandmarkId t0, ProfileLandmarkId t1, bool group = false)
        {
            double? mm = Dist(a, b), total = Dist(t0, t1);
            double? pct = mm is double m && total is double t && t > 0 ? 100 * m / t : null;
            return new ThirdsRow(label, mm, pct, Warn(a, b, t0, t1), group);
        }
        const ProfileLandmarkId Tr = ProfileLandmarkId.Tr, G = ProfileLandmarkId.G, N = ProfileLandmarkId.N,
            Sn = ProfileLandmarkId.Sn, Stm = ProfileLandmarkId.Stm, Me = ProfileLandmarkId.Me;
        return new[]
        {
            Row("Upper  Tr–G'", Tr, G, Tr, Me, true), Row("Middle G'–Sn", G, Sn, Tr, Me), Row("Lower  Sn–Me'", Sn, Me, Tr, Me),
            Row("Total  Tr–Me'", Tr, Me, Tr, Me),
            Row("N'–Sn", N, Sn, N, Me, true), Row("Sn–Me'", Sn, Me, N, Me),
            Row("Sn–Stm", Sn, Stm, Sn, Me, true), Row("Stm–Me'", Stm, Me, Sn, Me),
        };
    }
}
