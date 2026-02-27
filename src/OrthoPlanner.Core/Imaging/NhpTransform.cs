namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// A simple 4x4 matrix for 3D transformations, decoupled from WPF.
/// Used for volume reslicing in the Core library.
/// </summary>
public struct NhpTransform
{
    public double M11, M12, M13, M14;
    public double M21, M22, M23, M24;
    public double M31, M32, M33, M34;
    public double M41, M42, M43, M44;

    public static NhpTransform Identity => new NhpTransform { M11=1, M22=1, M33=1, M44=1 };

    public (double x, double y, double z) TransformPoint(double x, double y, double z)
    {
        // Simple affine transformation (assuming M14, M24, M34 are 0 and M44 is 1 for typical NHP transforms)
        return (
            x * M11 + y * M21 + z * M31 + M41,
            x * M12 + y * M22 + z * M32 + M42,
            x * M13 + y * M23 + z * M33 + M43
        );
    }
}
