namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>
/// Hybrid profile: the T1 photo contour is reliable from the nasal dorsum down (nose, lips, chin), but between the
/// forehead and the nasal dorsum it runs over eyebrows, lashes and the eye. When the CT skin mesh is registered on the
/// photo, that upper part is replaced by the mapped skin-mesh profile. The junction is on the dorsum, where the two
/// profiles agree; above the CT field of view the photo contour is kept (forehead and hairline, needed for Tr).
/// </summary>
public static partial class ProfileLandmarkDetector
{
    // To be tuned on more cases.
    private const double SpliceTolMm = 0.5;            // photo and mesh "agree" within this distance
    private const double SpliceRunMm = 1.0;            // ... over this much photo arc length
    private const double SpliceStartFraction = 0.5;    // junction search starts here, from N' (0) to Prn (1) on the mesh
    private const double SpliceTopGapMaxMm = 2.0;      // photo kept above the CT only if it meets the mesh top this close
    private const double SpliceMinMeshMm = 15.0;       // minimum length of the mesh part

    /// <summary>Hybrid profile; <see cref="MeshStart"/>..<see cref="MeshEnd"/> = hybrid samples taken from the mesh.</summary>
    private sealed record SpliceResult(bool Success, string? Reason, ProfileContour? Contour, int MeshStart, int MeshEnd,
                                       IReadOnlyList<string> Diagnostics);

    private static SpliceResult Splice(ProfileContour photo, ProfileSkinAssist assist, double rot, double pxPerMm)
    {
        var diag = new List<string>();
        SpliceResult Fail(string why) => new(false, why, null, -1, -1, diag);

        var skin = MapSkin(assist, photo.FacingRight, rot);
        if (skin is null) return Fail("invalid skin-mesh overlay matrix");
        var (mesh, sf) = skin.Value;
        int sPrn = Pronasale(sf), sN = sPrn < 0 ? -1 : Nasion(sf, sPrn);
        if (sN < 0) return Fail("skin mesh does not cover the nasion");

        var pts = photo.Points;
        var f = ToFrame(pts, photo.Confidence, photo.FacingRight, rot);
        int prn = Pronasale(f);
        if (prn < 0) return Fail("photo pronasale not found");

        // Distance of every photo sample down to Prn (+ a margin) to the mesh polyline, in px.
        double tol = SpliceTolMm * pxPerMm, run = SpliceRunMm * pxPerMm;
        var arc = ProfileCurveTools.ArcLength(pts);
        int last = prn;
        while (last < pts.Count - 1 && arc[last] - arc[prn] <= run) last++;
        var dist = new double[last + 1];
        var near = new int[last + 1];
        for (int i = 0; i <= last; i++) (dist[i], near[i]) = DistanceToPolyline(pts[i], mesh);

        // 1. Junction on the dorsum: from the mesh point halfway N' -> Prn, walk down the photo contour to the first
        //    sample where photo and mesh agree for 1 mm of arc.
        double vStart = sf.V[sN] + SpliceStartFraction * (sf.V[sPrn] - sf.V[sN]);
        int ms = sN;
        while (ms < sPrn && sf.V[ms] < vStart) ms++;
        int start = Nearest(pts, mesh[ms], 0, prn);
        int j = -1;
        for (int i = start; i <= prn && j < 0; i++)
        {
            bool ok = true;
            for (int k = i; k <= last && arc[k] - arc[i] <= run; k++)
                if (dist[k] > tol) { ok = false; break; }
            if (ok) j = i;
        }
        if (j < 0)
            return Fail($"photo and skin-mesh profiles do not agree within {SpliceTolMm} mm on the nasal dorsum (check the registration)");
        int mc = near[j];
        if (mc <= sN) return Fail("junction above the skin-mesh nasion");

        // 2. Top: keep the photo above the CT field of view only if it meets the mesh top closely.
        int top = Nearest(pts, mesh[0], 0, j);
        double topGapMm = Math.Sqrt(Sq(pts[top].X - mesh[0].X) + Sq(pts[top].Y - mesh[0].Y)) / pxPerMm;
        int prefix = topGapMm <= SpliceTopGapMaxMm ? top : 0;

        var meshArc = ProfileCurveTools.ArcLength(mesh);
        double meshMm = meshArc[mc] / pxPerMm;
        if (meshMm < SpliceMinMeshMm) return Fail($"skin mesh too short above the nose ({meshMm:F1} mm < {SpliceMinMeshMm} mm)");

        double maxReplaced = 0;
        for (int i = prefix; i < j; i++) maxReplaced = Math.Max(maxReplaced, dist[i]);

        // 3. Hybrid = photo[0..prefix) + mesh[0..mc] + photo[j..end], resampled at the PHOTO sample spacing (the
        //    landmark rules count samples, so the spacing must not change).
        var raw = new List<(double X, double Y)>();
        var conf = new List<double>();
        var fromMesh = new List<bool>();
        for (int i = 0; i < prefix; i++) { raw.Add(pts[i]); conf.Add(photo.Confidence[i]); fromMesh.Add(false); }
        for (int i = 0; i <= mc; i++) { raw.Add(mesh[i]); conf.Add(1.0); fromMesh.Add(true); }
        for (int i = j; i < pts.Count; i++) { raw.Add(pts[i]); conf.Add(photo.Confidence[i]); fromMesh.Add(false); }
        double step = arc[^1] / (pts.Count - 1);
        var (rp, rc, rm) = ResampleWithValues(raw, conf, fromMesh, step);
        int meshStart = rm.IndexOf(true), meshEnd = rm.LastIndexOf(true);
        if (meshStart < 0) return Fail("empty mesh part after resampling");

        double span = sf.V[sPrn] - sf.V[sN];
        diag.Add($"profile splice: CT skin-mesh profile used from the top of the CT down to the nasal dorsum " +
                 $"(junction at {F(100 * (f.V[j] - sf.V[sN]) / span, "F0")}% of N'->Prn, gap {F(dist[j] / pxPerMm, "F2")} mm); " +
                 $"mesh part {F(meshMm)} mm; photo contour replaced there deviated up to {F(maxReplaced / pxPerMm, "F1")} mm " +
                 "(eyebrows, lashes, eye)");
        diag.Add(prefix > 0
            ? $"profile splice: photo contour kept above the CT field of view (joins the CT at {F(topGapMm, "F2")} mm)"
            : $"WARNING: profile splice: photo forehead is {F(topGapMm)} mm from the top of the CT: profile starts at the CT top " +
              "(trichion searched on the CT part only)");

        var contour = new ProfileContour(rp, rc, photo.FacingRight, false,
                                         photo.Diagnostics.Concat(diag).ToList().AsReadOnly());
        return new SpliceResult(true, null, contour, meshStart, meshEnd, diag);
    }

    private static double Sq(double v) => v * v;

    private static int Nearest(IReadOnlyList<(double X, double Y)> p, (double X, double Y) q, int from, int to)
    {
        int best = from;
        double bd = double.MaxValue;
        for (int i = from; i <= to && i < p.Count; i++)
        {
            double d = Sq(p[i].X - q.X) + Sq(p[i].Y - q.Y);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Point-to-polyline distance (px) and the index of the polyline vertex nearest to the foot point.</summary>
    private static (double Dist, int Index) DistanceToPolyline((double X, double Y) q, IReadOnlyList<(double X, double Y)> l)
    {
        double bd = double.MaxValue;
        int bi = 0;
        for (int i = 0; i < l.Count - 1; i++)
        {
            double ax = l[i].X, ay = l[i].Y, dx = l[i + 1].X - ax, dy = l[i + 1].Y - ay, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? Math.Clamp(((q.X - ax) * dx + (q.Y - ay) * dy) / len2, 0, 1) : 0;
            double d = Sq(ax + t * dx - q.X) + Sq(ay + t * dy - q.Y);
            if (d < bd) { bd = d; bi = t < 0.5 ? i : i + 1; }
        }
        return (Math.Sqrt(bd), bi);
    }

    /// <summary>Uniform arc-length resampling that also interpolates the confidence and carries the mesh flag.</summary>
    private static (List<(double X, double Y)>, List<double>, List<bool>) ResampleWithValues(
        IReadOnlyList<(double X, double Y)> p, IReadOnlyList<double> c, IReadOnlyList<bool> mesh, double step)
    {
        var d = ProfileCurveTools.ArcLength(p);
        var rp = new List<(double X, double Y)>();
        var rc = new List<double>();
        var rm = new List<bool>();
        int seg = 0;
        for (int k = 0; k * step <= d[^1]; k++)
        {
            double s = k * step;
            while (seg < p.Count - 2 && d[seg + 1] < s) seg++;
            double len = d[seg + 1] - d[seg], t = len > 0 ? Math.Clamp((s - d[seg]) / len, 0, 1) : 0;
            rp.Add((p[seg].X + t * (p[seg + 1].X - p[seg].X), p[seg].Y + t * (p[seg + 1].Y - p[seg].Y)));
            rc.Add(c[seg] + t * (c[seg + 1] - c[seg]));
            rm.Add(t < 0.5 ? mesh[seg] : mesh[seg + 1]);
        }
        return (rp, rc, rm);
    }
}
