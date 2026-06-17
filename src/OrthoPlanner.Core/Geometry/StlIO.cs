namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// STL file import/export. Supports both binary and ASCII STL formats.
/// Vertices are stored as flat float[] with stride 3 (x,y,z,x,y,z,...).
/// </summary>
public static class StlIO
{
    /// <summary>
    /// Load an STL file (auto-detects binary vs ASCII).
    /// Returns triangle vertices as a flat float[] (stride 3, every 9 floats = one triangle).
    /// </summary>
    public static float[] LoadStl(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        string header = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(80, bytes.Length));
        if (header.StartsWith("solid", StringComparison.OrdinalIgnoreCase) && !IsBinaryStl(bytes))
            return LoadAsciiStl(filePath);

        return LoadBinaryStl(bytes);
    }

    private static bool IsBinaryStl(byte[] bytes)
    {
        if (bytes.Length < 84) return false;
        uint triCount = BitConverter.ToUInt32(bytes, 80);
        long expectedLength = 84 + triCount * 50L;
        return Math.Abs(bytes.Length - expectedLength) < 10;
    }

    private static float[] LoadBinaryStl(byte[] bytes)
    {
        uint triCount = BitConverter.ToUInt32(bytes, 80);
        var vertices = new float[triCount * 9];
        int vIdx = 0;

        for (uint i = 0; i < triCount; i++)
        {
            int offset = 84 + (int)(i * 50);
            for (int v = 0; v < 3; v++)
            {
                int vOffset = offset + 12 + v * 12;
                vertices[vIdx++] = BitConverter.ToSingle(bytes, vOffset);
                vertices[vIdx++] = BitConverter.ToSingle(bytes, vOffset + 4);
                vertices[vIdx++] = BitConverter.ToSingle(bytes, vOffset + 8);
            }
        }
        return vertices;
    }

    private static float[] LoadAsciiStl(string filePath)
    {
        var vertices = new List<float>();
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                vertices.Add(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                vertices.Add(float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                vertices.Add(float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return vertices.ToArray();
    }

    /// <summary>
    /// Export vertices as a binary STL file.
    /// Vertices must be a flat float[] (stride 3, every 9 = one triangle).
    /// </summary>
    public static void SaveBinaryStl(string filePath, float[] vertices)
    {
        int triCount = vertices.Length / 9;
        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);

        bw.Write(new byte[80]);
        bw.Write((uint)triCount);

        for (int t = 0; t < triCount; t++)
        {
            int i = t * 9;
            float v0x = vertices[i],     v0y = vertices[i + 1], v0z = vertices[i + 2];
            float v1x = vertices[i + 3], v1y = vertices[i + 4], v1z = vertices[i + 5];
            float v2x = vertices[i + 6], v2y = vertices[i + 7], v2z = vertices[i + 8];

            float ux = v1x - v0x, uy = v1y - v0y, uz = v1z - v0z;
            float vx = v2x - v0x, vy = v2y - v0y, vz = v2z - v0z;
            float nx = uy * vz - uz * vy;
            float ny = uz * vx - ux * vz;
            float nz = ux * vy - uy * vx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len > 0) { nx /= len; ny /= len; nz /= len; }

            bw.Write(nx); bw.Write(ny); bw.Write(nz);
            bw.Write(v0x); bw.Write(v0y); bw.Write(v0z);
            bw.Write(v1x); bw.Write(v1y); bw.Write(v1z);
            bw.Write(v2x); bw.Write(v2y); bw.Write(v2z);
            bw.Write((ushort)0);
        }
    }

    /// <summary>
    /// Simple Laplacian mesh smoothing on a flat float[] (stride 3).
    /// </summary>
    public static void SmoothMesh(float[] vertices, int iterations = 3, float lambda = 0.5f)
    {
        int vertCount = vertices.Length / 3;
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

        var newPos = new float[vertices.Length];
        for (int iter = 0; iter < iterations; iter++)
        {
            System.Threading.Tasks.Parallel.For(0, vertCount, i =>
            {
                if (neighbors[i].Count == 0)
                {
                    newPos[i * 3]     = vertices[i * 3];
                    newPos[i * 3 + 1] = vertices[i * 3 + 1];
                    newPos[i * 3 + 2] = vertices[i * 3 + 2];
                    return;
                }
                float avgX = 0, avgY = 0, avgZ = 0;
                foreach (int n in neighbors[i])
                {
                    avgX += vertices[n * 3];
                    avgY += vertices[n * 3 + 1];
                    avgZ += vertices[n * 3 + 2];
                }
                int cnt = neighbors[i].Count;
                avgX /= cnt; avgY /= cnt; avgZ /= cnt;

                newPos[i * 3]     = vertices[i * 3]     + lambda * (avgX - vertices[i * 3]);
                newPos[i * 3 + 1] = vertices[i * 3 + 1] + lambda * (avgY - vertices[i * 3 + 1]);
                newPos[i * 3 + 2] = vertices[i * 3 + 2] + lambda * (avgZ - vertices[i * 3 + 2]);
            });
            Array.Copy(newPos, vertices, vertices.Length);
        }
    }

    /// <summary>
    /// Simple mesh decimation on a flat float[] (stride 3).
    /// </summary>
    public static float[] DecimateMesh(float[] vertices, float ratio = 0.5f)
    {
        int triCount = vertices.Length / 9;
        int keepEvery = Math.Max(1, (int)(1.0f / ratio));
        var result = new List<float>();

        for (int t = 0; t < triCount; t++)
        {
            if (t % keepEvery == 0)
            {
                int i = t * 9;
                for (int k = 0; k < 9; k++) result.Add(vertices[i + k]);
            }
        }
        return result.ToArray();
    }
}
