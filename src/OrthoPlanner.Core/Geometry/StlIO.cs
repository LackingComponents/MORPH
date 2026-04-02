namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// STL file import/export. Supports both binary and ASCII STL formats.
/// </summary>
public static class StlIO
{
    /// <summary>
    /// Load an STL file (auto-detects binary vs ASCII).
    /// Returns triangle vertices as float[3] arrays (every 3 = one triangle).
    /// </summary>
    public static List<float[]> LoadStl(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        // ASCII STL starts with "solid" (but some binary files do too)
        // Check if it's really ASCII by looking for "facet" keyword
        string header = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(80, bytes.Length));
        if (header.StartsWith("solid", StringComparison.OrdinalIgnoreCase) && !IsBinaryStl(bytes))
            return LoadAsciiStl(filePath);

        return LoadBinaryStl(bytes);
    }

    private static bool IsBinaryStl(byte[] bytes)
    {
        if (bytes.Length < 84) return false;
        // Binary STL: 80-byte header + 4-byte triangle count
        uint triCount = BitConverter.ToUInt32(bytes, 80);
        // Each triangle = 50 bytes (12 normal + 36 vertex + 2 attribute)
        long expectedLength = 84 + triCount * 50L;
        return Math.Abs(bytes.Length - expectedLength) < 10;
    }

    private static List<float[]> LoadBinaryStl(byte[] bytes)
    {
        uint triCount = BitConverter.ToUInt32(bytes, 80);
        var vertices = new List<float[]>((int)(triCount * 3));

        for (uint i = 0; i < triCount; i++)
        {
            int offset = 84 + (int)(i * 50);
            // Skip normal (12 bytes), read 3 vertices (36 bytes)
            for (int v = 0; v < 3; v++)
            {
                int vOffset = offset + 12 + v * 12;
                vertices.Add(
                [
                    BitConverter.ToSingle(bytes, vOffset),
                    BitConverter.ToSingle(bytes, vOffset + 4),
                    BitConverter.ToSingle(bytes, vOffset + 8)
                ]);
            }
        }
        return vertices;
    }

    private static List<float[]> LoadAsciiStl(string filePath)
    {
        var vertices = new List<float[]>();
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                vertices.Add(
                [
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)
                ]);
            }
        }
        return vertices;
    }

    /// <summary>
    /// Export vertices as a binary STL file.
    /// Vertices must be in groups of 3 (each group = one triangle).
    /// </summary>
    public static void SaveBinaryStl(string filePath, List<float[]> vertices)
    {
        int triCount = vertices.Count / 3;
        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);

        // 80-byte header
        bw.Write(new byte[80]);
        bw.Write((uint)triCount);

        for (int t = 0; t < triCount; t++)
        {
            var v0 = vertices[t * 3];
            var v1 = vertices[t * 3 + 1];
            var v2 = vertices[t * 3 + 2];

            // Compute face normal
            float ux = v1[0] - v0[0], uy = v1[1] - v0[1], uz = v1[2] - v0[2];
            float vx = v2[0] - v0[0], vy = v2[1] - v0[1], vz = v2[2] - v0[2];
            float nx = uy * vz - uz * vy;
            float ny = uz * vx - ux * vz;
            float nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }

            bw.Write(nx); bw.Write(ny); bw.Write(nz);
            bw.Write(v0[0]); bw.Write(v0[1]); bw.Write(v0[2]);
            bw.Write(v1[0]); bw.Write(v1[1]); bw.Write(v1[2]);
            bw.Write(v2[0]); bw.Write(v2[1]); bw.Write(v2[2]);
            bw.Write((ushort)0); // attribute byte count
        }
    }

    /// <summary>
    /// Simple Laplacian mesh smoothing. Moves each vertex toward the
    /// average of its neighbors. Iterations controls smoothness.
    /// </summary>
    public static void SmoothMesh(List<float[]> vertices, int iterations = 3, float lambda = 0.5f)
    {
        // Build adjacency: for each vertex index, find which other vertices share a triangle
        int vertCount = vertices.Count;
        var neighbors = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < vertCount; i++)
            neighbors[i] = new HashSet<int>();

        for (int t = 0; t < vertCount; t += 3)
        {
            int a = t, b = t + 1, c = t + 2;
            neighbors[a].Add(b); neighbors[a].Add(c);
            neighbors[b].Add(a); neighbors[b].Add(c);
            neighbors[c].Add(a); neighbors[c].Add(b);
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            var newPositions = new float[vertCount][];
            for (int i = 0; i < vertCount; i++)
            {
                if (neighbors[i].Count == 0)
                {
                    newPositions[i] = [vertices[i][0], vertices[i][1], vertices[i][2]];
                    continue;
                }
                float avgX = 0, avgY = 0, avgZ = 0;
                foreach (int n in neighbors[i])
                {
                    avgX += vertices[n][0];
                    avgY += vertices[n][1];
                    avgZ += vertices[n][2];
                }
                int cnt = neighbors[i].Count;
                avgX /= cnt; avgY /= cnt; avgZ /= cnt;

                newPositions[i] =
                [
                    vertices[i][0] + lambda * (avgX - vertices[i][0]),
                    vertices[i][1] + lambda * (avgY - vertices[i][1]),
                    vertices[i][2] + lambda * (avgZ - vertices[i][2])
                ];
            }
            for (int i = 0; i < vertCount; i++)
                vertices[i] = newPositions[i];
        }
    }

    /// <summary>
    /// Simple mesh decimation: removes every nth triangle to reduce count.
    /// </summary>
    public static List<float[]> DecimateMesh(List<float[]> vertices, float ratio = 0.5f)
    {
        int triCount = vertices.Count / 3;
        int keepEvery = Math.Max(1, (int)(1.0f / ratio));
        var result = new List<float[]>();

        for (int t = 0; t < triCount; t++)
        {
            if (t % keepEvery == 0)
            {
                result.Add(vertices[t * 3]);
                result.Add(vertices[t * 3 + 1]);
                result.Add(vertices[t * 3 + 2]);
            }
        }
        return result;
    }
}
