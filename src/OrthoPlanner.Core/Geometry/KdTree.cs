namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// A simple 3D k-d tree for fast nearest-neighbor lookups during ICP alignment.
/// </summary>
public class KdTree
{
    private KdNode? _root;
    private float[]? _points; // flat array: x0,y0,z0, x1,y1,z1, ...
    private int _count;

    /// <summary>
    /// Build the tree from a list of 3D points (each float[3]).
    /// </summary>
    public void Build(List<float[]> points)
    {
        _count = points.Count;
        _points = new float[_count * 3];
        var indices = new int[_count];
        for (int i = 0; i < _count; i++)
        {
            _points[i * 3] = points[i][0];
            _points[i * 3 + 1] = points[i][1];
            _points[i * 3 + 2] = points[i][2];
            indices[i] = i;
        }
        _root = BuildRecursive(indices, 0, _count - 1, 0);
    }

    /// <summary>
    /// Build the tree from a flat float array (x0,y0,z0, x1,y1,z1, ...).
    /// </summary>
    public void Build(float[] flatPoints, int pointCount)
    {
        _count = pointCount;
        _points = flatPoints;
        var indices = new int[_count];
        for (int i = 0; i < _count; i++) indices[i] = i;
        _root = BuildRecursive(indices, 0, _count - 1, 0);
    }

    /// <summary>
    /// Find the nearest point to the query point. Returns the index and squared distance.
    /// </summary>
    public (int Index, float DistanceSq) FindNearest(float qx, float qy, float qz)
    {
        int bestIdx = -1;
        float bestDistSq = float.MaxValue;
        SearchRecursive(_root, qx, qy, qz, ref bestIdx, ref bestDistSq);
        return (bestIdx, bestDistSq);
    }

    /// <summary>
    /// Get the coordinates of a point by index.
    /// </summary>
    public (float X, float Y, float Z) GetPoint(int index)
    {
        return (_points![index * 3], _points[index * 3 + 1], _points[index * 3 + 2]);
    }

    private KdNode BuildRecursive(int[] indices, int lo, int hi, int depth)
    {
        if (lo > hi) return null!;
        if (lo == hi) return new KdNode { Index = indices[lo] };

        int axis = depth % 3;
        // Partial sort (median selection)
        Array.Sort(indices, lo, hi - lo + 1, new AxisComparer(_points!, axis));
        int mid = (lo + hi) / 2;

        return new KdNode
        {
            Index = indices[mid],
            Left = lo <= mid - 1 ? BuildRecursive(indices, lo, mid - 1, depth + 1) : null,
            Right = mid + 1 <= hi ? BuildRecursive(indices, mid + 1, hi, depth + 1) : null,
            SplitAxis = axis
        };
    }

    private void SearchRecursive(KdNode? node, float qx, float qy, float qz, ref int bestIdx, ref float bestDistSq)
    {
        if (node == null) return;

        int idx = node.Index;
        float dx = _points![idx * 3] - qx;
        float dy = _points[idx * 3 + 1] - qy;
        float dz = _points[idx * 3 + 2] - qz;
        float distSq = dx * dx + dy * dy + dz * dz;

        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            bestIdx = idx;
        }

        float splitVal = _points[idx * 3 + node.SplitAxis];
        float queryVal = node.SplitAxis switch { 0 => qx, 1 => qy, _ => qz };
        float diff = queryVal - splitVal;

        // Search the nearer side first
        KdNode? nearSide = diff < 0 ? node.Left : node.Right;
        KdNode? farSide = diff < 0 ? node.Right : node.Left;

        SearchRecursive(nearSide, qx, qy, qz, ref bestIdx, ref bestDistSq);

        // Only search the far side if the splitting plane is closer than the current best
        if (diff * diff < bestDistSq)
        {
            SearchRecursive(farSide, qx, qy, qz, ref bestIdx, ref bestDistSq);
        }
    }

    private class KdNode
    {
        public int Index;
        public KdNode? Left;
        public KdNode? Right;
        public int SplitAxis;
    }

    private class AxisComparer : IComparer<int>
    {
        private readonly float[] _pts;
        private readonly int _axis;
        public AxisComparer(float[] pts, int axis) { _pts = pts; _axis = axis; }
        public int Compare(int a, int b) => _pts[a * 3 + _axis].CompareTo(_pts[b * 3 + _axis]);
    }
}
