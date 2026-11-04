namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>One point-to-line correspondence on a target polyline.</summary>
internal readonly record struct Correspondence((double X, double Y) Foot, double Residual, int Vertex, bool Valid);

/// <summary>
/// Point-to-line matching on a fixed, densely sampled target polyline: nearest vertex through a uniform grid, then
/// projection on the chord between its neighbours. Matches whose nearest vertex lies within 3 samples of either end
/// are flagged invalid (prevents scale collapse when the two curves do not cover the same extent).
/// </summary>
internal sealed class PolylineMatcher
{
    private const int EndExclusion = 3;

    private readonly (double X, double Y)[] _pts;
    private readonly double _cell, _minX, _minY;
    private readonly int _nx, _ny;
    private readonly int[] _cellStart, _cellItems;

    public PolylineMatcher(IReadOnlyList<(double X, double Y)> target)
    {
        if (target.Count < 2 * EndExclusion + 2)
            throw new ArgumentException($"target polyline too short ({target.Count} samples)", nameof(target));
        _pts = target.ToArray();
        _minX = _pts.Min(p => p.X); _minY = _pts.Min(p => p.Y);
        double w = _pts.Max(p => p.X) - _minX, h = _pts.Max(p => p.Y) - _minY;
        double length = 0;
        for (int i = 1; i < _pts.Length; i++) length += Dist(_pts[i], _pts[i - 1]);
        _cell = Math.Max(4 * length / _pts.Length, 1e-9); // a few samples per cell
        _nx = Math.Max(1, (int)(w / _cell) + 1);
        _ny = Math.Max(1, (int)(h / _cell) + 1);

        var counts = new int[_nx * _ny + 1];
        foreach (var p in _pts) counts[CellOf(p) + 1]++;
        for (int i = 1; i < counts.Length; i++) counts[i] += counts[i - 1];
        _cellStart = (int[])counts.Clone();
        _cellItems = new int[_pts.Length];
        var fill = (int[])counts.Clone();
        for (int i = 0; i < _pts.Length; i++) _cellItems[fill[CellOf(_pts[i])]++] = i;
    }

    public int Count => _pts.Length;

    public IReadOnlyList<(double X, double Y)> Points => _pts;

    public Correspondence Match((double X, double Y) q)
    {
        int j = Nearest(q);
        bool valid = j > EndExclusion - 1 && j < _pts.Length - EndExclusion;
        int jc = Math.Clamp(j, 1, _pts.Length - 2);
        var a = _pts[jc - 1];
        var b = _pts[jc + 1];
        double ux = b.X - a.X, uy = b.Y - a.Y, len = Math.Sqrt(ux * ux + uy * uy);
        (double X, double Y) foot;
        if (len < 1e-12) foot = _pts[jc];
        else
        {
            ux /= len; uy /= len;
            double t = (q.X - a.X) * ux + (q.Y - a.Y) * uy;
            foot = (a.X + t * ux, a.Y + t * uy);
        }
        return new Correspondence(foot, Dist(q, foot), j, valid);
    }

    public Correspondence[] Match(IReadOnlyList<(double X, double Y)> qs)
    {
        var result = new Correspondence[qs.Count];
        for (int i = 0; i < result.Length; i++) result[i] = Match(qs[i]);
        return result;
    }

    // Exact nearest vertex: search growing rings of cells until the ring distance exceeds the best found.
    private int Nearest((double X, double Y) q)
    {
        int cx = (int)Math.Floor((q.X - _minX) / _cell), cy = (int)Math.Floor((q.Y - _minY) / _cell);
        int best = -1;
        double bestD2 = double.MaxValue;
        // Chebyshev distance (in cells) from the query cell to the grid: rings below it are empty.
        int startRing = Math.Max(Math.Max(-cx, cx - (_nx - 1)), Math.Max(-cy, cy - (_ny - 1)));
        startRing = Math.Max(0, startRing);
        int maxRing = startRing + _nx + _ny + 2;
        for (int ring = startRing; ring <= maxRing; ring++)
        {
            double ringDist = (ring - 1) * _cell;
            if (best >= 0 && ringDist > 0 && ringDist * ringDist > bestD2) break;
            if (ring == 0) { ScanCell(cx, cy, q, ref best, ref bestD2); continue; }
            for (int gx = cx - ring; gx <= cx + ring; gx++)
            {
                ScanCell(gx, cy - ring, q, ref best, ref bestD2);
                ScanCell(gx, cy + ring, q, ref best, ref bestD2);
            }
            for (int gy = cy - ring + 1; gy <= cy + ring - 1; gy++)
            {
                ScanCell(cx - ring, gy, q, ref best, ref bestD2);
                ScanCell(cx + ring, gy, q, ref best, ref bestD2);
            }
        }
        return best;
    }

    private void ScanCell(int gx, int gy, (double X, double Y) q, ref int best, ref double bestD2)
    {
        if (gx < 0 || gy < 0 || gx >= _nx || gy >= _ny) return;
        int c = gy * _nx + gx;
        for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
        {
            int i = _cellItems[k];
            double dx = _pts[i].X - q.X, dy = _pts[i].Y - q.Y, d2 = dx * dx + dy * dy;
            if (d2 < bestD2 || (d2 == bestD2 && i < best)) { bestD2 = d2; best = i; }
        }
    }

    private int CellOf((double X, double Y) p) =>
        Math.Min(_ny - 1, (int)((p.Y - _minY) / _cell)) * _nx + Math.Min(_nx - 1, (int)((p.X - _minX) / _cell));

    private static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
