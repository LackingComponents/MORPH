namespace OrthoPlanner.Core.Geometry;

/// <summary>
/// Wavefront OBJ export for triangle meshes stored as flat float[] (stride 3).
/// </summary>
public static class ObjIO
{
    public static void SaveObj(string filePath, float[] vertices)
    {
        int triCount = vertices.Length / 9;
        using var sw = new StreamWriter(filePath);

        for (int t = 0; t < triCount; t++)
        {
            int i = t * 9;
            for (int v = 0; v < 3; v++)
            {
                int vi = i + v * 3;
                sw.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "v {0} {1} {2}",
                    vertices[vi], vertices[vi + 1], vertices[vi + 2]));
            }
        }

        for (int t = 0; t < triCount; t++)
        {
            int baseIdx = t * 3 + 1;
            sw.WriteLine($"f {baseIdx} {baseIdx + 1} {baseIdx + 2}");
        }
    }
}
