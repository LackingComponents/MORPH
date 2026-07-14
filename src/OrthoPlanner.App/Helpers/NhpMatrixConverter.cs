using System.Windows.Media.Media3D;
using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.App.Helpers;

internal static class NhpMatrixConverter
{
    public static NhpTransform ToNhpTransform(Matrix3D matrix) => new()
    {
        M11 = matrix.M11, M12 = matrix.M12, M13 = matrix.M13, M14 = 0,
        M21 = matrix.M21, M22 = matrix.M22, M23 = matrix.M23, M24 = 0,
        M31 = matrix.M31, M32 = matrix.M32, M33 = matrix.M33, M34 = 0,
        M41 = matrix.OffsetX, M42 = matrix.OffsetY, M43 = matrix.OffsetZ, M44 = 1,
    };
}
