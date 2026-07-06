namespace OrthoPlanner.Core.Imaging.Cephalometry;

/// <summary>
/// Computes named cephalometric analyses (Steiner, Tweed, Ricketts) from placed landmarks.
/// All geometry is derived from the 2D DRR pixel coordinates
/// (<see cref="CephalometricLandmark.Position"/>) via <see cref="CephToolEngine"/>, calibrated
/// with per-axis DICOM spacing. Missing landmarks are an expected state during digitization and
/// never throw: the affected measurement is returned with
/// <see cref="CephMeasurementStatus.MissingLandmarks"/> and a null value.
/// </summary>
public static class CephAnalysisEngine
{
    /// <summary>
    /// Runs the requested analysis over the given landmark set.
    /// </summary>
    /// <param name="type">Which analysis protocol to run.</param>
    /// <param name="landmarks">The landmark set (placed and unplaced); typically the full 42.</param>
    /// <param name="spacingX">DRR pixel spacing in mm along X.</param>
    /// <param name="spacingY">DRR pixel spacing in mm along Y.</param>
    public static CephAnalysisResult Compute(
        CephAnalysisType type,
        IReadOnlyList<CephalometricLandmark> landmarks,
        double spacingX, double spacingY)
    {
        var measurements = type switch
        {
            CephAnalysisType.Steiner => ComputeSteiner(landmarks, spacingX, spacingY),
            CephAnalysisType.Tweed => ComputeTweed(landmarks, spacingX, spacingY),
            CephAnalysisType.Ricketts => ComputeRicketts(landmarks, spacingX, spacingY),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported analysis type.")
        };

        return new CephAnalysisResult(type, measurements);
    }

    // ── Steiner ─────────────────────────────────────────────────────────────

    private static List<CephMeasurementResult> ComputeSteiner(
        IReadOnlyList<CephalometricLandmark> lm, double sx, double sy)
    {
        var results = new List<CephMeasurementResult>();

        // SNA: angle at N between N→S and N→A.
        var sna = Measure("SNA", "SNA", CephUnit.Degrees, 82, 2, lm, q =>
            CephToolEngine.Angle3Pts(q.Get("Sella"), q.Get("Nasion"), q.Get("Point A"), sx, sy));
        results.Add(sna);

        // SNB: angle at N between N→S and N→B.
        var snb = Measure("SNB", "SNB", CephUnit.Degrees, 80, 2, lm, q =>
            CephToolEngine.Angle3Pts(q.Get("Sella"), q.Get("Nasion"), q.Get("Point B"), sx, sy));
        results.Add(snb);

        // ANB: derived from SNA and SNB (not re-measured geometrically).
        results.Add(DeriveAnb(sna, snb));

        results.Add(Measure("SN-GoGn", "SN-GoGn", CephUnit.Degrees, 32, 2, lm, q =>
            CephToolEngine.AngleLines(q.Get("Sella"), q.Get("Nasion"),
                                      q.Get("Gonion"), q.Get("Gnathion"), sx, sy)));

        results.Add(Measure("U1-NA angle", "U1-NA°", CephUnit.Degrees, 22, 2, lm, q =>
            CephToolEngine.AngleLines(q.Get("U1"), q.Get("U1 Root"),
                                      q.Get("Nasion"), q.Get("Point A"), sx, sy)));

        results.Add(Measure("U1-NA distance", "U1-NA mm", CephUnit.Millimeters, 4, 2, lm, q =>
            CephToolEngine.PerpendicularToLine(q.Get("U1"),
                                               q.Get("Nasion"), q.Get("Point A"), sx, sy).DistMm));

        results.Add(Measure("L1-NB angle", "L1-NB°", CephUnit.Degrees, 25, 2, lm, q =>
            CephToolEngine.AngleLines(q.Get("L1"), q.Get("L1 Root"),
                                      q.Get("Nasion"), q.Get("Point B"), sx, sy)));

        results.Add(Measure("L1-NB distance", "L1-NB mm", CephUnit.Millimeters, 4, 2, lm, q =>
            CephToolEngine.PerpendicularToLine(q.Get("L1"),
                                               q.Get("Nasion"), q.Get("Point B"), sx, sy).DistMm));

        results.Add(Measure("Interincisal angle", "U1-L1", CephUnit.Degrees, 131, 5, lm, q =>
            InterincisalAngle(q, sx, sy)));

        return results;
    }

    /// <summary>
    /// Interincisal angle between the upper axis (U1→U1 Root) and lower axis (L1→L1 Root).
    /// This angle is anatomically obtuse (~131°), so it is measured at the intersection of the
    /// two axes using the directed <see cref="CephToolEngine.Angle3Pts"/> (which spans 0–180°)
    /// rather than <see cref="CephToolEngine.AngleLines"/> (which folds to the acute 0–90° angle).
    /// If the axes are parallel, falls back to the supplement of the acute line angle.
    /// </summary>
    private static double InterincisalAngle(LandmarkQuery q, double sx, double sy)
    {
        var u1 = q.Get("U1");
        var u1Root = q.Get("U1 Root");
        var l1 = q.Get("L1");
        var l1Root = q.Get("L1 Root");

        var intersection = CephToolEngine.LineIntersection(u1, u1Root, l1, l1Root);
        if (intersection is { } i)
            return CephToolEngine.Angle3Pts(u1Root, i, l1Root, sx, sy);

        return 180.0 - CephToolEngine.AngleLines(u1, u1Root, l1, l1Root, sx, sy);
    }

    private static CephMeasurementResult DeriveAnb(CephMeasurementResult sna, CephMeasurementResult snb)
    {
        const string name = "ANB";
        const string abbrev = "ANB";

        if (sna.Status == CephMeasurementStatus.MissingLandmarks ||
            snb.Status == CephMeasurementStatus.MissingLandmarks)
        {
            var missing = new List<string>();
            AddDistinct(missing, sna.MissingLandmarkNames);
            AddDistinct(missing, snb.MissingLandmarkNames);
            return CephMeasurementResult.Missing(name, abbrev, CephUnit.Degrees, 2, 2, missing);
        }

        return CephMeasurementResult.Computed(
            name, abbrev, CephUnit.Degrees, sna.Value!.Value - snb.Value!.Value, 2, 2);
    }

    // ── Tweed ───────────────────────────────────────────────────────────────

    private static List<CephMeasurementResult> ComputeTweed(
        IReadOnlyList<CephalometricLandmark> lm, double sx, double sy)
    {
        var results = new List<CephMeasurementResult>();

        // FMA: Frankfort horizontal (Po→Or) vs mandibular plane (Go→Gn).
        results.Add(Measure("FMA", "FMA", CephUnit.Degrees, 25, 4, lm, q =>
            CephToolEngine.AngleLines(q.Get("Porion"), q.Get("Orbitale"),
                                      q.Get("Gonion"), q.Get("Gnathion"), sx, sy)));

        // FMIA: Frankfort horizontal vs lower incisor axis (L1→L1 Root).
        results.Add(Measure("FMIA", "FMIA", CephUnit.Degrees, 65, 5, lm, q =>
            CephToolEngine.AngleLines(q.Get("Porion"), q.Get("Orbitale"),
                                      q.Get("L1"), q.Get("L1 Root"), sx, sy)));

        // IMPA: mandibular plane (Go→Gn) vs lower incisor axis (L1→L1 Root).
        results.Add(Measure("IMPA", "IMPA", CephUnit.Degrees, 90, 5, lm, q =>
            CephToolEngine.AngleLines(q.Get("Gonion"), q.Get("Gnathion"),
                                      q.Get("L1"), q.Get("L1 Root"), sx, sy)));

        return results;
    }

    // ── Ricketts (reduced skeletal subset) ──────────────────────────────────

    private static List<CephMeasurementResult> ComputeRicketts(
        IReadOnlyList<CephalometricLandmark> lm, double sx, double sy)
    {
        var results = new List<CephMeasurementResult>();

        // Facial Depth: Frankfort horizontal (Po→Or) vs facial plane (N→Pog).
        results.Add(Measure("Facial Depth", "FD", CephUnit.Degrees, 87, 3, lm, q =>
            CephToolEngine.AngleLines(q.Get("Porion"), q.Get("Orbitale"),
                                      q.Get("Nasion"), q.Get("Pogonion"), sx, sy)));

        // Mandibular Plane: Frankfort horizontal (Po→Or) vs mandibular plane (Go→Gn).
        results.Add(Measure("Mandibular Plane", "MP", CephUnit.Degrees, 26, 4, lm, q =>
            CephToolEngine.AngleLines(q.Get("Porion"), q.Get("Orbitale"),
                                      q.Get("Gonion"), q.Get("Gnathion"), sx, sy)));

        // Convexity: perpendicular distance from Point A to facial plane (N→Pog).
        results.Add(Measure("Convexity", "Conv", CephUnit.Millimeters, 2, 2, lm, q =>
            CephToolEngine.PerpendicularToLine(q.Get("Point A"),
                                               q.Get("Nasion"), q.Get("Pogonion"), sx, sy).DistMm));

        return results;
    }

    // ── Shared helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves all landmarks referenced by <paramref name="compute"/> and returns a computed
    /// result, or — if any were missing — a <see cref="CephMeasurementStatus.MissingLandmarks"/>
    /// result listing them. The compute delegate never throws for missing landmarks: unresolved
    /// references yield a default point and the resulting value is discarded.
    /// </summary>
    private static CephMeasurementResult Measure(
        string name, string abbrev, CephUnit unit, double normMean, double normTolerance,
        IReadOnlyList<CephalometricLandmark> landmarks, Func<LandmarkQuery, double> compute)
    {
        var query = new LandmarkQuery(landmarks);
        double value = compute(query);

        return query.HasMissing
            ? CephMeasurementResult.Missing(name, abbrev, unit, normMean, normTolerance,
                                            new List<string>(query.Missing))
            : CephMeasurementResult.Computed(name, abbrev, unit, value, normMean, normTolerance);
    }

    private static void AddDistinct(List<string> target, IReadOnlyList<string> source)
    {
        foreach (var s in source)
            if (!target.Contains(s))
                target.Add(s);
    }

    /// <summary>
    /// Resolves landmark base names to 2D points for a single measurement, accumulating the names
    /// of any that cannot be resolved so the measurement can be flagged
    /// <see cref="CephMeasurementStatus.MissingLandmarks"/>.
    /// </summary>
    private sealed class LandmarkQuery
    {
        private readonly IReadOnlyList<CephalometricLandmark> _landmarks;
        private readonly List<string> _missing = new();

        public LandmarkQuery(IReadOnlyList<CephalometricLandmark> landmarks) => _landmarks = landmarks;

        /// <summary>
        /// Resolves <paramref name="baseName"/>. Records the name as missing and returns a default
        /// point (discarded by the caller) when the landmark is not fully placed.
        /// </summary>
        public CephPoint Get(string baseName)
        {
            var point = CephLandmarkResolver.Resolve(_landmarks, baseName, out var missingName);
            if (point is { } p)
                return p;

            if (!_missing.Contains(missingName))
                _missing.Add(missingName);
            return default;
        }

        public bool HasMissing => _missing.Count > 0;

        public IReadOnlyList<string> Missing => _missing;
    }
}
