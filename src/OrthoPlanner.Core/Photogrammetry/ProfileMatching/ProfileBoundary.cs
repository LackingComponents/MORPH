namespace OrthoPlanner.Core.Photogrammetry.ProfileMatching;

/// <summary>Boundary tracing, front-chain selection and nose-protrusion measurement.</summary>
internal static class ProfileBoundary
{
    // Moore neighbourhood, clockwise on screen (y down), starting West.
    private static readonly (int Dx, int Dy)[] Dirs =
        { (-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1) };

    /// <summary>Outer boundary of a single component by Moore-neighbour tracing (closed loop, start not repeated).</summary>
    public static List<(int X, int Y)> TraceOuter(bool[] comp, int w, int h)
    {
        var loop = new List<(int X, int Y)>();
        int start = Array.IndexOf(comp, true);
        if (start < 0) return loop;

        var s = (X: start % w, Y: start / w);
        loop.Add(s);
        // The pixel West of the first raster hit is always background.
        if (!Step(comp, w, h, s, 0, out var second, out int secondBack)) return loop;

        var p = second;
        int back = secondBack;
        long guard = 4L * comp.Length + 16;
        for (long iter = 0; iter < guard; iter++)
        {
            loop.Add(p);
            if (!Step(comp, w, h, p, back, out var q, out int qBack)) break;
            // Jacob's criterion: stop when we leave the start pixel exactly as the first time.
            if (p == s && q == second && qBack == secondBack) { loop.RemoveAt(loop.Count - 1); break; }
            p = q;
            back = qBack;
        }
        return loop;
    }

    // One Moore step: scan clockwise from the backtrack direction; returns the next pixel and its backtrack.
    private static bool Step(bool[] comp, int w, int h, (int X, int Y) p, int back,
                             out (int X, int Y) next, out int nextBack)
    {
        for (int k = 1; k <= 8; k++)
        {
            int d = (back + k) % 8;
            int nx = p.X + Dirs[d].Dx, ny = p.Y + Dirs[d].Dy;
            if (nx < 0 || ny < 0 || nx >= w || ny >= h || !comp[ny * w + nx]) continue;
            int prev = (d + 7) % 8;
            int bx = p.X + Dirs[prev].Dx, by = p.Y + Dirs[prev].Dy;
            next = (nx, ny);
            nextBack = DirIndex(bx - nx, by - ny);
            return true;
        }
        next = p;
        nextBack = back;
        return false;
    }

    private static int DirIndex(int dx, int dy)
    {
        for (int i = 0; i < 8; i++)
            if (Dirs[i].Dx == dx && Dirs[i].Dy == dy) return i;
        return 0;
    }

    /// <summary>
    /// Boundary arc facing the given side, from its topmost to its bottommost point.
    /// Points lying on the image border at either end are trimmed.
    /// </summary>
    public static List<(double X, double Y)> FrontChain(List<(int X, int Y)> loop, bool facingRight, int w, int h)
    {
        var chain = new List<(double X, double Y)>();
        int n = loop.Count;
        if (n < 3) return chain;

        int sign = facingRight ? 1 : -1;
        int iTop = 0, iBot = 0;
        for (int i = 1; i < n; i++)
        {
            var p = loop[i];
            var t = loop[iTop];
            if (p.Y < t.Y || (p.Y == t.Y && sign * p.X > sign * t.X)) iTop = i;
            var b = loop[iBot];
            if (p.Y > b.Y || (p.Y == b.Y && sign * p.X > sign * b.X)) iBot = i;
        }

        var forward = Arc(loop, iTop, iBot, +1);
        var backward = Arc(loop, iTop, iBot, -1);
        double mf = forward.Average(p => sign * p.X), mb = backward.Average(p => sign * p.X);
        var arc = mf >= mb ? forward : backward;

        int s = 0, e = arc.Count - 1;
        while (s < e && OnBorder(arc[s], w, h)) s++;
        while (e > s && OnBorder(arc[e], w, h)) e--;
        for (int i = s; i <= e; i++) chain.Add((arc[i].X, arc[i].Y));
        return chain;
    }

    private static List<(int X, int Y)> Arc(List<(int X, int Y)> loop, int from, int to, int step)
    {
        int n = loop.Count;
        var arc = new List<(int X, int Y)>();
        for (int i = from; ; i = ((i + step) % n + n) % n)
        {
            arc.Add(loop[i]);
            if (i == to || arc.Count > n) break;
        }
        return arc;
    }

    public static bool OnBorder((double X, double Y) p, int w, int h) =>
        p.X <= 0.5 || p.Y <= 0.5 || p.X >= w - 1.5 || p.Y >= h - 1.5;

    public static bool OnBorder((int X, int Y) p, int w, int h) =>
        p.X <= 0 || p.Y <= 0 || p.X >= w - 1 || p.Y >= h - 1;

    /// <summary>
    /// Protrusions toward the facing side in the middle 60% of [yMin, yMax], strongest first.
    /// Prominence is measured inside a window of ±windowFraction × height, so a sharp nose beats a broad occiput.
    /// </summary>
    public static IReadOnlyList<(double Prominence, int Row)> Protrusions(
        List<(double X, double Y)> chain, bool facingRight, int yMin, int yMax, double windowFraction = 0.08)
    {
        int height = yMax - yMin + 1;
        int r0 = yMin + (int)(0.2 * height), r1 = yMax - (int)(0.2 * height);
        var peaks = new List<(double Prominence, int Row)>();
        if (r1 - r0 < 5 || chain.Count == 0) return peaks;

        int sign = facingRight ? 1 : -1;
        var g = new double[r1 - r0 + 1];
        Array.Fill(g, double.NaN);
        foreach (var p in chain)
        {
            int row = (int)Math.Round(p.Y);
            if (row < r0 || row > r1) continue;
            double v = sign * p.X;
            int i = row - r0;
            if (double.IsNaN(g[i]) || v > g[i]) g[i] = v;
        }
        FillGaps(g);
        if (g.All(double.IsNaN)) return peaks;

        int win = Math.Max(3, (int)(windowFraction * height));
        for (int i = 1; i < g.Length - 1; i++)
        {
            if (!(g[i] > g[i - 1] && g[i] >= g[i + 1])) continue;
            double minL = g[i], minR = g[i];
            for (int j = i - 1; j >= Math.Max(0, i - win) && g[j] <= g[i]; j--) minL = Math.Min(minL, g[j]);
            for (int j = i + 1; j <= Math.Min(g.Length - 1, i + win) && g[j] <= g[i]; j++) minR = Math.Min(minR, g[j]);
            double prom = g[i] - Math.Max(minL, minR);
            if (prom > 0) peaks.Add((prom, i + r0));
        }
        return peaks.OrderByDescending(p => p.Prominence).ToList();
    }

    // Linear interpolation across NaN gaps; edges take the nearest value.
    private static void FillGaps(double[] g)
    {
        int last = -1;
        for (int i = 0; i < g.Length; i++)
        {
            if (double.IsNaN(g[i])) continue;
            if (last < 0) for (int j = 0; j < i; j++) g[j] = g[i];
            else for (int j = last + 1; j < i; j++) g[j] = g[last] + (g[i] - g[last]) * (j - last) / (i - last);
            last = i;
        }
        if (last >= 0) for (int j = last + 1; j < g.Length; j++) g[j] = g[last];
    }
}
