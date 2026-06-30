using System.Windows.Media.Media3D;

namespace OrthoPlanner.App.Helpers;

/// <summary>
/// Converts the main viewport camera orientation (relative to anterior +Z-up) into
/// NHP Euler angles matching <c>BuildNhpMatrix</c> order: R = Rz(yaw) · Ry(roll) · Rx(pitch).
/// </summary>
public static class NhpCameraAngles
{
    private static readonly Vector3D AnteriorLook = new(0, 1, 0);
    private static readonly Vector3D WorldUp = new(0, 0, 1);

    public static (double Pitch, double Roll, double Yaw) FromCamera(Vector3D lookDir, Vector3D upDir)
    {
        var mCur = BuildBasisMatrix(lookDir, upDir);
        var mRef = BuildBasisMatrix(AnteriorLook, WorldUp);
        var mDelta = InvertOrthogonal(mRef) * mCur;
        var mNhp = InvertOrthogonal(mDelta);
        return DecomposeZyxEuler(mNhp);
    }

    public static bool OrientationChanged(Vector3D lookA, Vector3D upA, Vector3D lookB, Vector3D upB)
    {
        lookA.Normalize(); upA.Normalize();
        lookB.Normalize(); upB.Normalize();
        const double eps = 1e-6;
        return (lookA - lookB).LengthSquared > eps || (upA - upB).LengthSquared > eps;
    }

    private static Matrix3D BuildBasisMatrix(Vector3D look, Vector3D up)
    {
        look.Normalize();
        var right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 1e-12)
            right = new Vector3D(1, 0, 0);
        right.Normalize();
        up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        return new Matrix3D(
            right.X, up.X, -look.X, 0,
            right.Y, up.Y, -look.Y, 0,
            right.Z, up.Z, -look.Z, 0,
            0, 0, 0, 1);
    }

    private static Matrix3D InvertOrthogonal(Matrix3D m) =>
        new Matrix3D(
            m.M11, m.M21, m.M31, 0,
            m.M12, m.M22, m.M32, 0,
            m.M13, m.M23, m.M33, 0,
            0, 0, 0, 1);

  private static (double Pitch, double Roll, double Yaw) DecomposeZyxEuler(Matrix3D r)
    {
        double sy = Math.Sqrt(r.M11 * r.M11 + r.M21 * r.M21);
        double pitch, roll, yaw;

        if (sy < 1e-6)
        {
            pitch = Math.Atan2(-r.M32, r.M33);
            roll  = Math.Atan2(-r.M21, r.M22);
            yaw   = 0;
        }
        else
        {
            roll  = Math.Atan2(r.M31, sy);
            yaw   = Math.Atan2(r.M21, r.M11);
            pitch = Math.Atan2(r.M32, r.M33);
        }

        const double rad2deg = 180.0 / Math.PI;
        return (pitch * rad2deg, roll * rad2deg, yaw * rad2deg);
    }
}
