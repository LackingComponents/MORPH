namespace OrthoPlanner.Core.Imaging.Cephalometry;

/// <summary>
/// Resolves a landmark, referenced by its base anatomical name, to a single 2D DRR point.
/// Midline landmarks resolve to their own placed position; bilateral landmarks resolve to
/// the midpoint of their placed left and right sides.
/// </summary>
internal static class CephLandmarkResolver
{
    /// <summary>
    /// Resolves <paramref name="baseName"/> to a single 2D point.
    /// <list type="bullet">
    /// <item>Midline (e.g. "Nasion"): returns its placed position, or null if not placed.</item>
    /// <item>Bilateral (e.g. "Porion" → "Porion (L)"/"Porion (R)"): returns the midpoint of
    /// both sides. If only one side (or neither) is placed the landmark is treated as missing —
    /// there is deliberately no single-side fallback.</item>
    /// </list>
    /// When resolution fails, <paramref name="missingName"/> carries the human-readable name to
    /// report (the base name), and the method returns null.
    /// </summary>
    public static CephPoint? Resolve(
        IReadOnlyList<CephalometricLandmark> landmarks,
        string baseName,
        out string missingName)
    {
        missingName = baseName;

        // Midline: an exact, non-bilateral name match.
        var midline = FindByName(landmarks, baseName);
        if (midline is not null)
        {
            return midline.Position is { } p ? new CephPoint(p.X, p.Y) : null;
        }

        // Bilateral: require both sides to be placed, then take the midpoint.
        var left = FindByName(landmarks, baseName + " (L)");
        var right = FindByName(landmarks, baseName + " (R)");
        if (left is not null && right is not null)
        {
            if (left.Position is { } lp && right.Position is { } rp)
                return new CephPoint((lp.X + rp.X) / 2.0, (lp.Y + rp.Y) / 2.0);

            return null; // one or both sides not yet placed → missing (no single-side fallback)
        }

        // Name not present in the set at all: defensively treat as missing.
        return null;
    }

    private static CephalometricLandmark? FindByName(
        IReadOnlyList<CephalometricLandmark> landmarks, string name)
    {
        foreach (var l in landmarks)
        {
            if (string.Equals(l.Name, name, StringComparison.Ordinal))
                return l;
        }
        return null;
    }
}
