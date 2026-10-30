using g3;

namespace OrthoPlanner.Core.Photogrammetry.Calibration;

/// <summary>
/// Ideal pinhole camera (no lens distortion) in pixel units.
/// Image axes: u to the right, v downward; camera looks along +Z.
/// </summary>
public sealed record PinholeCamera
{
    /// <summary>Diagonal of a 36x24 mm full-frame sensor, used for 35 mm-equivalent focal conversion.</summary>
    public const double FullFrameDiagonalMm = 43.27;

    public int Width { get; }
    public int Height { get; }
    public double Fx { get; }
    public double Fy { get; }
    public double Cx { get; }
    public double Cy { get; }

    public PinholeCamera(int width, int height, double fx, double fy, double cx, double cy)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Image size must be positive (got {width}x{height}).");
        if (!double.IsFinite(fx) || !double.IsFinite(fy) || fx <= 0 || fy <= 0)
            throw new ArgumentOutOfRangeException(nameof(fx), $"Focal lengths must be finite and positive (got fx={fx}, fy={fy}).");
        if (!double.IsFinite(cx) || !double.IsFinite(cy))
            throw new ArgumentOutOfRangeException(nameof(cx), $"Principal point must be finite (got cx={cx}, cy={cy}).");

        Width = width;
        Height = height;
        Fx = fx;
        Fy = fy;
        Cx = cx;
        Cy = cy;
    }

    /// <summary>
    /// Builds the camera from the EXIF "focal length in 35 mm film" value, scaling on the image diagonal.
    /// Principal point is assumed at the image centre.
    /// </summary>
    public static PinholeCamera FromFocal35mm(int width, int height, double focal35mm)
    {
        if (!double.IsFinite(focal35mm) || focal35mm <= 0)
            throw new ArgumentOutOfRangeException(nameof(focal35mm), $"35 mm-equivalent focal must be finite and positive (got {focal35mm}).");

        double diagPx = Math.Sqrt((double)width * width + (double)height * height);
        double f = focal35mm / FullFrameDiagonalMm * diagPx;
        return new PinholeCamera(width, height, f, f, width / 2.0, height / 2.0);
    }

    /// <summary>Projects a camera-space point (mm) to pixel coordinates. Throws if the point is not in front of the camera.</summary>
    public Vector2d Project(Vector3d pCam)
    {
        if (!(pCam.z > 0))
            throw new ArgumentOutOfRangeException(nameof(pCam), $"Point is not in front of the camera (z={pCam.z}).");
        return new Vector2d(Fx * pCam.x / pCam.z + Cx, Fy * pCam.y / pCam.z + Cy);
    }

    /// <summary>Camera-space direction (not normalized, z = 1) of the ray through a pixel.</summary>
    public Vector3d PixelToCameraRay(Vector2d pixel)
    {
        if (!double.IsFinite(pixel.x) || !double.IsFinite(pixel.y))
            throw new ArgumentOutOfRangeException(nameof(pixel), $"Pixel must be finite (got {pixel}).");
        return new Vector3d((pixel.x - Cx) / Fx, (pixel.y - Cy) / Fy, 1.0);
    }
}
