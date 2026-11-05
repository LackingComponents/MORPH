using System.Globalization;

namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Soft-tissue profile landmarks, top to bottom.</summary>
public enum ProfileLandmarkId { Tr, G, N, Prn, Sn, A, Ls, Stm, Li, B, Pog, Me }

public enum LandmarkConfidence { High, Low }

/// <summary>A detected landmark in photo image pixels; <see cref="ContourIndex"/> is the nearest photo contour sample.</summary>
public sealed record DetectedProfileLandmark(ProfileLandmarkId Id, double X, double Y, int ContourIndex,
                                             LandmarkConfidence Confidence, string Reason);

/// <summary>Registered skin-mesh profile (silhouette px) and its silhouette-px -> photo-px matrix [a b tx; c d ty].</summary>
public sealed record ProfileSkinAssist(ProfileContour SkinContour, IReadOnlyList<double> SkinToPhoto);

/// <summary>Outcome of <see cref="ProfileLandmarkDetector.Detect"/>. On failure <see cref="FailureReason"/> is set.</summary>
public sealed record ProfileLandmarkResult(bool Success, string? FailureReason,
                                          IReadOnlyList<DetectedProfileLandmark> Landmarks,
                                          IReadOnlyList<ProfileLandmarkId> Missing,
                                          IReadOnlyList<string> Diagnostics);

/// <summary>
/// Automatic soft-tissue profile landmarks on the T1 photo contour, in the NHP frame (y = true vertical, nose +X).
/// Rules validated on three QIU photos; every landmark carries a confidence and a short reason.
/// </summary>
public static class ProfileLandmarkDetector
{
    private const double MinConfidence = 0.05;
    private const double ShapeSigma = 8;          // samples, for "most anterior / deepest" searches
    private const double DirectionSigma = 6;      // samples, for directions and turning angles
    private const double NasionRiseFraction = 0.3;
    private const int NasionMaxInvalidRun = 20;
    private const double GlabellaSpanFraction = 0.7;
    private const double LipBelowFraction = 0.25;
    private const double LipMinProminencePx = 3;
    private const int SubnasaleSkip = 10;
    private const int TurningHalfWindow = 5;
    private const int SnCoincideSamples = 3;
    private const double MentonMinAngleDeg = 55;
    private const double MentonMinRunFraction = 0.3;
    private const double MeshAssistMaxMm = 2.0;
    private const double LowConfidenceShare = 0.10;
    private const double LipsApartDepthMm = 2.5;
    private const double LipsApartBacktrackMm = 1.0;
    private const double HairOffsetMm = 1.0;
    private const double HairRunMm = 1.0;           // arc length of the colour window
    private const double HairRunShare = 0.7;        // share of the window that must look like hair
    private const double HairMinDeltaE = 15;
    private const double HairSkinFactor = 2.5;      // x median Delta E of the skin samples (brows and lashes inflate MAD)
    private const double HairMaxBehindGMm = 40;     // the frontal hairline is on the forehead, not over the skull
    private const int GlabellaLimitSamples = 3;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double v, string fmt = "F1") => v.ToString(fmt, Inv);

    public static string Label(ProfileLandmarkId id) => id switch
    {
        ProfileLandmarkId.Tr => "Tr", ProfileLandmarkId.G => "G'", ProfileLandmarkId.N => "N'",
        ProfileLandmarkId.Prn => "Prn", ProfileLandmarkId.Sn => "Sn", ProfileLandmarkId.A => "A'",
        ProfileLandmarkId.Ls => "Ls", ProfileLandmarkId.Stm => "Stm", ProfileLandmarkId.Li => "Li",
        ProfileLandmarkId.B => "B'", ProfileLandmarkId.Pog => "Pog'", _ => "Me'",
    };

    /// <summary>Contour samples in the NHP working frame: U anterior (+), V down (+).</summary>
    private sealed class Frame
    {
        public required double[] U, V, Us, Du, Dv;
        public required bool[] Ok;
        public int N => U.Length;
    }

    /// <param name="photo">T1 photo contour (image px, top to bottom).</param>
    /// <param name="nhpRotationDeg">Photo rotation applied by NHP (view convention, clockwise on screen).</param>
    /// <param name="rgb">Photo pixels, 3 bytes per pixel (R, G, B), same pixel grid as the contour.</param>
    /// <param name="pxPerMm">Photo scale (calibrated).</param>
    /// <param name="assist">Registered skin-mesh profile, used for N' and G' when the photo contour is unreliable there.</param>
    public static ProfileLandmarkResult Detect(ProfileContour photo, double nhpRotationDeg, byte[] rgb, int width,
                                               int height, int stride, double pxPerMm, ProfileSkinAssist? assist = null)
    {
        var diag = new List<string>();
        ProfileLandmarkResult Fail(string why) =>
            new(false, why, Array.Empty<DetectedProfileLandmark>(), Array.Empty<ProfileLandmarkId>(), diag.AsReadOnly());

        if (photo is null || photo.Points.Count < 50) return Fail("photo contour missing or too short");
        if (photo.Confidence.Count != photo.Points.Count) return Fail("photo contour: points and confidence differ in length");
        if (!double.IsFinite(pxPerMm) || pxPerMm <= 0) return Fail("the photo is not calibrated (px/mm missing)");
        if (rgb is null || rgb.Length < (long)stride * height) return Fail("photo pixels missing");
        try
        {
            return DetectCore(photo, nhpRotationDeg, rgb, width, height, stride, pxPerMm, assist, diag);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail($"landmark detection crashed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ProfileLandmarkResult DetectCore(ProfileContour photo, double rot, byte[] rgb, int width, int height,
        int stride, double pxPerMm, ProfileSkinAssist? assist, List<string> diag)
    {
        var pts = photo.Points;
        var f = ToFrame(pts, photo.Confidence, photo.FacingRight, rot);
        var found = new Dictionary<ProfileLandmarkId, DetectedProfileLandmark>();
        DetectedProfileLandmark At(ProfileLandmarkId id, int i, bool high, string reason) =>
            found[id] = new(id, pts[i].X, pts[i].Y, i, high && f.Ok[i] ? LandmarkConfidence.High : LandmarkConfidence.Low, reason);
        ProfileLandmarkResult Fail(string why) =>
            new(false, why, found.Values.ToList(), Array.Empty<ProfileLandmarkId>(), diag.AsReadOnly());
        diag.Add($"NHP rotation {F(rot, "F2")} deg, facing {(photo.FacingRight ? "right" : "left")}, {f.N} samples, {F(pxPerMm, "F3")} px/mm");

        int prn = Pronasale(f);
        if (prn < 0) return Fail("pronasale not found (no point with confidence > 0.05)");
        At(ProfileLandmarkId.Prn, prn, true, "most anterior point of the whole contour");

        int n = Nasion(f, prn);
        var (g, gChord) = n < 0 ? (-1, false) : Glabella(f, n, prn);
        double noseH = n < 0 ? double.NaN : f.V[prn] - f.V[n];
        if (n >= 0) At(ProfileLandmarkId.N, n, true, "deepest point above Prn (photo contour)");
        if (g >= 0) At(ProfileLandmarkId.G, g, !gChord, gChord ? GlabellaChordReason + " (photo contour)"
                                                               : "most anterior point above N' (photo contour)");
        if (assist != null) ApplyMeshAssist(pts, f, n, g, assist, photo.FacingRight, rot, pxPerMm, found, diag);
        if (found.TryGetValue(ProfileLandmarkId.N, out var nl)) { n = nl.ContourIndex; noseH = f.V[prn] - f.V[n]; }
        if (n < 0 || !(noseH > 0)) return Fail("soft-tissue nasion not found (no concavity above the nose tip)");

        var lips = LipPeaks(f, prn, f.V[prn] + LipBelowFraction * noseH);
        int ls = lips.Count > 0 ? lips[0] : -1, li = lips.Count > 1 ? lips[1] : -1;
        diag.Add($"lip peaks below Prn+{F(LipBelowFraction * noseH, "F0")} px: {lips.Count} (prominence >= {LipMinProminencePx} px)");
        if (ls < 0) return Fail("subnasale not found (no upper lip peak below the nose)");
        At(ProfileLandmarkId.Ls, ls, true, "first anterior lip peak below the nose");
        if (li >= 0) At(ProfileLandmarkId.Li, li, true, "second anterior lip peak");

        int d = ArgMin(f.Us, prn + 1, ls - 1);
        if (d < 0) return Fail("subnasale not found (no concavity between Prn and Ls)");
        int sn = prn + SubnasaleSkip <= d ? ArgMaxTurning(f, prn + SubnasaleSkip, d) : d;
        At(ProfileLandmarkId.Sn, sn, prn + SubnasaleSkip <= d,
           prn + SubnasaleSkip <= d ? "sharpest turn columella -> lip" : "short columella: deepest point used");

        int a = ArgMin(f.Us, sn, ls);
        if (a >= 0)
        {
            bool coincide = Math.Abs(a - sn) <= SnCoincideSamples;
            At(ProfileLandmarkId.A, a, !coincide, coincide ? "coincides with Sn (flat upper lip)" : "deepest point Sn -> Ls");
        }

        if (li >= 0) DetectStomion(f, ls, li, pxPerMm, At, diag);

        int me = Menton(f, li >= 0 ? li : ls, prn);
        if (me < 0) return Fail("soft-tissue menton not found (no backward turn under the chin)");
        At(ProfileLandmarkId.Me, me, true, "first backward turn > 55 deg under the chin");

        if (li >= 0 && me > li + 2)
        {
            int b = ChordExtreme(f, li, me, posterior: true);
            if (b >= 0)
            {
                At(ProfileLandmarkId.B, b, true, "most posterior point to the chord Li -> Me'");
                int pog = me > b + 2 ? ChordExtreme(f, b, me, posterior: false) : -1;
                if (pog >= 0) At(ProfileLandmarkId.Pog, pog, true, "most anterior point to the chord B' -> Me'");
            }
        }

        if (found.TryGetValue(ProfileLandmarkId.G, out var gl) && found.TryGetValue(ProfileLandmarkId.N, out var nl2))
        {
            int tr = Trichion(pts, f, photo.FacingRight, gl.ContourIndex, nl2.ContourIndex, rgb, width, height, stride, pxPerMm, diag);
            if (tr >= 0) At(ProfileLandmarkId.Tr, tr, false, "skin -> hair colour change above G' (always check)");
        }

        var order = Enum.GetValues<ProfileLandmarkId>();
        var list = order.Where(found.ContainsKey).Select(id => found[id]).ToList();
        var missing = order.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0) diag.Add($"missing: {string.Join(", ", missing.Select(Label))}");
        return new ProfileLandmarkResult(true, null, list, missing, diag.AsReadOnly());
    }

    // ── frame ─────────────────────────────────────────────────────────────────────
    private static Frame ToFrame(IReadOnlyList<(double X, double Y)> p, IReadOnlyList<double> conf, bool facingRight, double rotDeg)
    {
        double r = rotDeg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        double sign = (facingRight ? 1 : -1) * (c < 0 ? -1 : 1);
        int n = p.Count;
        var u = new double[n];
        var v = new double[n];
        for (int i = 0; i < n; i++)
        {
            u[i] = sign * (p[i].X * c - p[i].Y * s);
            v[i] = p[i].X * s + p[i].Y * c;
        }
        return new Frame
        {
            U = u, V = v, Us = ProfileCurveTools.GaussianReflect(u, ShapeSigma),
            Du = ProfileCurveTools.Gradient(ProfileCurveTools.GaussianReflect(u, DirectionSigma)),
            Dv = ProfileCurveTools.Gradient(ProfileCurveTools.GaussianReflect(v, DirectionSigma)),
            Ok = conf.Select(x => x > MinConfidence).ToArray(),
        };
    }

    private static int Pronasale(Frame f)
    {
        int prn = -1;
        for (int i = 0; i < f.N; i++)
            if (f.Ok[i] && (prn < 0 || f.Us[i] > f.Us[prn])) prn = i;
        return prn;
    }

    // Walking up from Prn: running minimum of U; stop when U rises above min + 0.3 (U_Prn - min). Same as T2.
    private static int Nasion(Frame f, int prn)
    {
        int k = prn, mn = prn, bad = 0;
        while (k > 0)
        {
            k--;
            if (!f.Ok[k]) { if (++bad > NasionMaxInvalidRun) break; continue; }
            bad = 0;
            if (f.Us[k] < f.Us[mn]) mn = k;
            if (f.Us[prn] - f.Us[mn] > 0 && f.Us[k] > f.Us[mn] + NasionRiseFraction * (f.Us[prn] - f.Us[mn])) break;
        }
        return mn == prn ? -1 : mn;
    }

    private const string GlabellaChordReason = "forehead keeps rising: most prominent point vs the N'-forehead chord";

    /// <summary>
    /// Most anterior point above N' within 0.7 (V_Prn - V_N'). When it lies at the top of that window (the forehead
    /// keeps coming forward), the most prominent point relative to the chord N' -> window top is used instead
    /// (<c>Chord</c> = true, reported as Low confidence).
    /// </summary>
    private static (int Index, bool Chord) Glabella(Frame f, int n, int prn)
    {
        double vMin = f.V[n] - GlabellaSpanFraction * (f.V[prn] - f.V[n]);
        int g = -1, top = n;
        for (int k = n - 1; k >= 0 && f.V[k] >= vMin; k--)
        {
            top = k;
            if (f.Ok[k] && (g < 0 || f.Us[k] > f.Us[g])) g = k;
        }
        if (g < 0 || g - top > GlabellaLimitSamples) return (g, false);
        int c = ChordExtreme(f, top, n, posterior: false);   // chord runs downward from the window top to N'; G' bulges forward
        return c >= 0 ? (c, true) : (g, true);
    }

    /// <summary>
    /// N' and G' on the registered skin-mesh profile (no hair, lashes or eyebrows). Used instead of the photo ones when
    /// the photo contour between G' and N' is unreliable or its N' is more than 2 mm from the mesh N'.
    /// </summary>
    private static void ApplyMeshAssist(IReadOnlyList<(double X, double Y)> pts, Frame f, int n, int g,
        ProfileSkinAssist assist, bool facingRight, double rot, double pxPerMm,
        Dictionary<ProfileLandmarkId, DetectedProfileLandmark> found, List<string> diag)
    {
        var m = assist.SkinToPhoto;
        if (m.Count != 6) { diag.Add("mesh assist skipped: invalid overlay matrix"); return; }
        var mapped = assist.SkinContour.Points.Select(p => (X: m[0] * p.X + m[1] * p.Y + m[2], Y: m[3] * p.X + m[4] * p.Y + m[5])).ToList();
        var conf = assist.SkinContour.Points.Select(_ => 1.0).ToList();
        var sf = ToFrame(mapped, conf, facingRight, rot);
        if (sf.N > 1 && sf.V[0] > sf.V[^1]) { mapped.Reverse(); sf = ToFrame(mapped, conf, facingRight, rot); }
        int sPrn = Pronasale(sf), sN = sPrn < 0 ? -1 : Nasion(sf, sPrn);
        var (sG, sGChord) = sN < 0 ? (-1, false) : Glabella(sf, sN, sPrn);
        if (sN < 0) { diag.Add("mesh assist: nasion not found on the skin-mesh profile"); return; }

        double gapMm = n < 0 ? double.PositiveInfinity
            : Math.Sqrt(Math.Pow(pts[n].X - mapped[sN].X, 2) + Math.Pow(pts[n].Y - mapped[sN].Y, 2)) / pxPerMm;
        double lowShare = n < 0 || g < 0 ? 1 : Enumerable.Range(g, n - g + 1).Count(i => !f.Ok[i]) / (double)(n - g + 1);
        diag.Add($"mesh assist: photo N' vs mesh N' {F(gapMm, "F2")} mm, low-confidence share G'..N' {F(lowShare * 100, "F0")}%");
        if (gapMm <= MeshAssistMaxMm && lowShare <= LowConfidenceShare)
        {
            diag.Add("mesh assist: photo N'/G' kept");
            return;
        }
        string why = gapMm > MeshAssistMaxMm ? $"photo N' {F(gapMm, "F1")} mm from mesh N'" : "photo contour unreliable G'..N'";
        found[ProfileLandmarkId.N] = FromMesh(ProfileLandmarkId.N, mapped[sN], pts, $"skin mesh ({why})");
        if (sG >= 0)
            found[ProfileLandmarkId.G] = FromMesh(ProfileLandmarkId.G, mapped[sG], pts,
                sGChord ? $"skin mesh, {GlabellaChordReason} ({why})" : $"skin mesh ({why})") with
                { Confidence = sGChord ? LandmarkConfidence.Low : LandmarkConfidence.High };
        diag.Add($"mesh assist: N'{(sG >= 0 ? " and G'" : "")} taken from the skin mesh ({why})");
    }

    private static DetectedProfileLandmark FromMesh(ProfileLandmarkId id, (double X, double Y) p,
        IReadOnlyList<(double X, double Y)> pts, string reason)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < pts.Count; i++)
        {
            double d = (pts[i].X - p.X) * (pts[i].X - p.X) + (pts[i].Y - p.Y) * (pts[i].Y - p.Y);
            if (d < bd) { bd = d; best = i; }
        }
        return new DetectedProfileLandmark(id, p.X, p.Y, best, LandmarkConfidence.High, reason);
    }

    // Local maxima of Us below vMin with prominence >= 3 px, in contour order.
    private static List<int> LipPeaks(Frame f, int prn, double vMin)
    {
        var peaks = new List<int>();
        for (int i = prn + 1; i < f.N - 1 && peaks.Count < 2; i++)
        {
            if (!f.Ok[i] || f.V[i] <= vMin || !(f.Us[i] > f.Us[i - 1] && f.Us[i] >= f.Us[i + 1])) continue;
            double minL = f.Us[i], minR = f.Us[i];
            for (int j = i - 1; j > prn && f.Us[j] <= f.Us[i]; j--) minL = Math.Min(minL, f.Us[j]);
            for (int j = i + 1; j < f.N && f.Us[j] <= f.Us[i]; j++) minR = Math.Min(minR, f.Us[j]);
            if (f.Us[i] - Math.Max(minL, minR) >= LipMinProminencePx) peaks.Add(i);
        }
        return peaks;
    }

    private static int ArgMin(double[] v, int from, int to)
    {
        if (from > to) return -1;
        int best = from;
        for (int i = from + 1; i <= to; i++) if (v[i] < v[best]) best = i;
        return best;
    }

    // Maximum |turning angle| of the direction over +-5 samples.
    private static int ArgMaxTurning(Frame f, int from, int to)
    {
        int best = from;
        double bestTurn = -1;
        for (int i = from; i <= to; i++)
        {
            int a = Math.Max(0, i - TurningHalfWindow), b = Math.Min(f.N - 1, i + TurningHalfWindow);
            double t = Math.Abs(Wrap(Math.Atan2(f.Dv[b], f.Du[b]) - Math.Atan2(f.Dv[a], f.Du[a])));
            if (t > bestTurn) { bestTurn = t; best = i; }
        }
        return best;
    }

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static void DetectStomion(Frame f, int ls, int li, double pxPerMm,
        Func<ProfileLandmarkId, int, bool, string, DetectedProfileLandmark> at, List<string> diag)
    {
        int stm = ArgMin(f.Us, ls, li);
        double depthMm = (Math.Min(f.Us[ls], f.Us[li]) - f.Us[stm]) / pxPerMm;
        double backtrackMm = 0, vMax = double.MinValue;
        for (int i = ls; i <= li; i++) { vMax = Math.Max(vMax, f.V[i]); backtrackMm = Math.Max(backtrackMm, (vMax - f.V[i]) / pxPerMm); }
        bool apart = depthMm > LipsApartDepthMm || backtrackMm > LipsApartBacktrackMm;
        diag.Add($"stomion depth {F(depthMm, "F2")} mm, contour backtrack {F(backtrackMm, "F2")} mm " +
                 $"(lips apart if > {F(LipsApartDepthMm)} / {F(LipsApartBacktrackMm)} mm)");
        at(ProfileLandmarkId.Stm, stm, !apart, apart ? "lips apart: point inside the mouth" : "most posterior point Ls -> Li");
    }

    // First point below the lips turning backward by > 55 deg and staying backward for >= 0.3 (V - V_Prn) of arc.
    private static int Menton(Frame f, int start, int prn)
    {
        var arc = new double[f.N];
        for (int i = 1; i < f.N; i++) arc[i] = arc[i - 1] + Math.Sqrt(Math.Pow(f.U[i] - f.U[i - 1], 2) + Math.Pow(f.V[i] - f.V[i - 1], 2));
        double Ang(int i) => Math.Atan2(-f.Du[i], f.Dv[i]) * 180 / Math.PI;   // 0 down, 90 backward
        for (int i = start + 1; i < f.N; i++)
        {
            if (Ang(i) <= MentonMinAngleDeg) continue;
            int j = i;
            while (j < f.N - 1 && Ang(j) > MentonMinAngleDeg) j++;
            if (arc[j] - arc[i] >= MentonMinRunFraction * (f.V[i] - f.V[prn])) return i;
            i = j;
        }
        return -1;
    }

    // Point farthest from the chord a -> b on the posterior (or anterior) side.
    private static int ChordExtreme(Frame f, int a, int b, bool posterior)
    {
        double cu = f.U[b] - f.U[a], cv = f.V[b] - f.V[a], len = Math.Sqrt(cu * cu + cv * cv);
        if (len < 1e-9) return -1;
        int best = -1;
        double bestD = double.NegativeInfinity;
        for (int i = a + 1; i < b; i++)
        {
            double cross = (cu * (f.V[i] - f.V[a]) - cv * (f.U[i] - f.U[a])) / len;   // > 0 posterior for a downward chord
            double d = posterior ? cross : -cross;
            if (d > bestD) { bestD = d; best = i; }
        }
        return bestD > 0 ? best : -1;
    }

    /// <summary>
    /// Walking up from G': first contour point where the Lab colour just inside the contour departs from the skin colour
    /// (median of the samples between G' and N') on at least 70% of the next 1 mm of contour. Threshold = max(15, 2.5 x
    /// the median Delta E of the skin samples to their own median): brows and lashes in that sample inflate its MAD, so
    /// the median is used. Only points above G' and less than 40 mm behind it are searched. Returns -1 when not found.
    /// </summary>
    private static int Trichion(IReadOnlyList<(double X, double Y)> p, Frame f, bool facingRight, int g, int n,
                                byte[] rgb, int width, int height, int stride, double pxPerMm, List<string> diag)
    {
        double offset = Math.Max(4, HairOffsetMm * pxPerMm);
        (double L, double A, double B)? Patch(int i)
        {
            int a = Math.Max(0, i - 3), b = Math.Min(p.Count - 1, i + 3);
            double tx = p[b].X - p[a].X, ty = p[b].Y - p[a].Y, tl = Math.Sqrt(tx * tx + ty * ty);
            if (tl < 1e-9) return null;
            double nx = -ty / tl, ny = tx / tl;
            if (nx * (facingRight ? 1 : -1) > 0) { nx = -nx; ny = -ny; }   // point inside the face
            return MedianLab(rgb, width, height, stride, p[i].X + nx * offset, p[i].Y + ny * offset);
        }

        int from = Math.Min(g, n), to = Math.Max(g, n);
        var skin = Enumerable.Range(from, to - from + 1).Select(Patch).Where(c => c.HasValue).Select(c => c!.Value).ToList();
        if (skin.Count < 5) { diag.Add("trichion: not enough skin samples between G' and N'"); return -1; }
        var med = (L: Median(skin.Select(c => c.L)), A: Median(skin.Select(c => c.A)), B: Median(skin.Select(c => c.B)));
        double De((double L, double A, double B) c) => Math.Sqrt(Math.Pow(c.L - med.L, 2) + Math.Pow(c.A - med.A, 2) + Math.Pow(c.B - med.B, 2));
        double m = Median(skin.Select(De));
        double threshold = Math.Max(HairMinDeltaE, HairSkinFactor * m);
        diag.Add($"trichion: skin Lab ({F(med.L)}, {F(med.A)}, {F(med.B)}) from G'..N', skin dE median {F(m)}, " +
                 $"threshold {F(threshold)} = max({HairMinDeltaE}, {HairSkinFactor} x median); hair = >= {F(HairRunShare * 100, "F0")}% " +
                 $"of {F(HairRunMm)} mm of contour above it; search above G' and < {F(HairMaxBehindGMm, "F0")} mm behind G'");

        // Candidates: from G' upward, above G', not far behind it (stop where the contour runs over the skull).
        var cand = new List<int>();
        for (int i = g - 1; i >= 0; i--)
        {
            if ((f.Us[g] - f.Us[i]) / pxPerMm > HairMaxBehindGMm) break;
            if (f.V[i] < f.V[g]) cand.Add(i);
        }
        var hair = cand.Select(i => Patch(i) is { } c && De(c) > threshold).ToArray();
        double runPx = HairRunMm * pxPerMm;
        for (int k = 0; k < cand.Count; k++)
        {
            int total = 0, hits = 0;
            for (int j = k; j < cand.Count; j++)
            {
                double dx = p[cand[j]].X - p[cand[k]].X, dy = p[cand[j]].Y - p[cand[k]].Y;
                if (Math.Sqrt(dx * dx + dy * dy) > runPx && total > 0) break;
                total++;
                if (hair[j]) hits++;
            }
            if (hair[k] && total >= 2 && hits >= HairRunShare * total) return cand[k];
        }
        diag.Add("trichion: no skin -> hair change found above G' (missing, not guessed)");
        return -1;
    }

    private static (double L, double A, double B)? MedianLab(byte[] rgb, int width, int height, int stride, double x, double y)
    {
        int cx = (int)Math.Round(x), cy = (int)Math.Round(y);
        if (cx < 1 || cy < 1 || cx >= width - 1 || cy >= height - 1) return null;
        var ls = new List<double>(9); var as_ = new List<double>(9); var bs = new List<double>(9);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int o = (cy + dy) * stride + 3 * (cx + dx);
                var (l, a, b) = Lab(rgb[o], rgb[o + 1], rgb[o + 2]);
                ls.Add(l); as_.Add(a); bs.Add(b);
            }
        return (Median(ls), Median(as_), Median(bs));
    }

    private static (double L, double A, double B) Lab(byte r, byte g, byte b)
    {
        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        static double Fn(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
        double lr = Lin(r / 255.0), lg = Lin(g / 255.0), lb = Lin(b / 255.0);
        double fx = Fn((0.4124 * lr + 0.3576 * lg + 0.1805 * lb) / 0.95047);
        double fy = Fn(0.2126 * lr + 0.7152 * lg + 0.0722 * lb);
        double fz = Fn((0.0193 * lr + 0.1192 * lg + 0.9505 * lb) / 1.08883);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double Median(IEnumerable<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        if (s.Count == 0) return double.NaN;
        return s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }
}
