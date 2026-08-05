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
}
