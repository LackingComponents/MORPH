using g3;

namespace OrthoPlanner.Core.Photogrammetry.Calibration;

/// <summary>
/// Rigid world-to-camera transform: pCam = Rotation * pWorld + Translation.
/// World frame is the CT volume frame (mm).
/// </summary>
public sealed record CameraPose
{
    private const double SmallAngle = 1e-12;

    public Matrix3d Rotation { get; }
    public Vector3d Translation { get; }

    public CameraPose(Matrix3d rotation, Vector3d translation)
    {
        if (!IsFinite(rotation) || !IsFinite(translation))
            throw new ArgumentException("Camera pose contains NaN or infinite values.");
        Rotation = rotation;
        Translation = translation;
    }

    /// <summary>Builds a pose from a Rodrigues (axis * angle, radians) vector and a translation.</summary>
    public static CameraPose FromRodrigues(Vector3d rodrigues, Vector3d translation)
        => new(RodriguesToMatrix(rodrigues), translation);

    /// <summary>Rotation as a Rodrigues vector (axis * angle, radians).</summary>
    public Vector3d RotationVector => MatrixToRodrigues(Rotation);

    /// <summary>Camera optical centre expressed in the world (CT) frame: -R^T * t.</summary>
    public Vector3d CameraCenterWorld => -(Rotation.Transpose() * Translation);

    public Vector3d WorldToCamera(Vector3d pWorld) => Rotation * pWorld + Translation;

    public static Matrix3d RodriguesToMatrix(Vector3d r)
    {
        double theta = r.Length;
        if (theta < SmallAngle)
            return Matrix3d.Identity;

        Vector3d k = r / theta;
        double c = Math.Cos(theta), s = Math.Sin(theta), v = 1.0 - c;
        return new Matrix3d(
            c + k.x * k.x * v,       k.x * k.y * v - k.z * s, k.x * k.z * v + k.y * s,
            k.y * k.x * v + k.z * s, c + k.y * k.y * v,       k.y * k.z * v - k.x * s,
            k.z * k.x * v - k.y * s, k.z * k.y * v + k.x * s, c + k.z * k.z * v);
    }

    public static Vector3d MatrixToRodrigues(Matrix3d m)
    {
        double cosTheta = Math.Clamp((m[0, 0] + m[1, 1] + m[2, 2] - 1.0) * 0.5, -1.0, 1.0);
        double theta = Math.Acos(cosTheta);
        var skew = new Vector3d(m[2, 1] - m[1, 2], m[0, 2] - m[2, 0], m[1, 0] - m[0, 1]);

        if (theta < 1e-8)
            return skew * 0.5; // first-order: R ~ I + [r]x

        double sinTheta = Math.Sin(theta);
        if (sinTheta > 1e-6)
            return skew * (theta / (2.0 * sinTheta));

        // theta ~ pi: axis from the symmetric part, R = 2kk^T - I
        double xx = Math.Sqrt(Math.Max(0, (m[0, 0] + 1) * 0.5));
        double yy = Math.Sqrt(Math.Max(0, (m[1, 1] + 1) * 0.5));
        double zz = Math.Sqrt(Math.Max(0, (m[2, 2] + 1) * 0.5));
        Vector3d axis;
        if (xx >= yy && xx >= zz)
            axis = new Vector3d(xx, (m[0, 1] + m[1, 0]) / (4 * xx), (m[0, 2] + m[2, 0]) / (4 * xx));
        else if (yy >= zz)
            axis = new Vector3d((m[0, 1] + m[1, 0]) / (4 * yy), yy, (m[1, 2] + m[2, 1]) / (4 * yy));
        else
            axis = new Vector3d((m[0, 2] + m[2, 0]) / (4 * zz), (m[1, 2] + m[2, 1]) / (4 * zz), zz);
        return axis.Normalized * theta;
    }

    private static bool IsFinite(Vector3d v) => double.IsFinite(v.x) && double.IsFinite(v.y) && double.IsFinite(v.z);

    private static bool IsFinite(Matrix3d m) => IsFinite(m.Row0) && IsFinite(m.Row1) && IsFinite(m.Row2);
}
