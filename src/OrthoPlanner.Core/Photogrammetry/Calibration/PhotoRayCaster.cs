using g3;

namespace OrthoPlanner.Core.Photogrammetry.Calibration;

/// <summary>
/// Back-projects photo pixels onto the CT soft-tissue surface (same mm frame as the calibration landmarks),
/// enabling millimetric measurements on a calibrated photo.
/// </summary>
public sealed class PhotoRayCaster
{
    private readonly DMeshAABBTree3 _tree;

    public PhotoRayCaster(DMesh3 skinMesh)
    {
        ArgumentNullException.ThrowIfNull(skinMesh);
        if (skinMesh.TriangleCount == 0)
            throw new ArgumentException("Skin mesh has no triangles.", nameof(skinMesh));
        _tree = new DMeshAABBTree3(skinMesh, autoBuild: true);
    }

    /// <summary>Nearest surface point hit by the ray from the camera centre through <paramref name="pixel"/>, or null if the ray misses.</summary>
    public Vector3d? PixelToSurface(Vector2d pixel, PinholeCamera cam, CameraPose pose)
    {
        ArgumentNullException.ThrowIfNull(cam);
        ArgumentNullException.ThrowIfNull(pose);

        Vector3d origin = pose.CameraCenterWorld;
        Vector3d direction = (pose.Rotation.Transpose() * cam.PixelToCameraRay(pixel)).Normalized;
        var ray = new Ray3d(origin, direction, true);

        int tid = _tree.FindNearestHitTriangle(ray);
        if (tid == DMesh3.InvalidID)
            return null;

        DMesh3 mesh = _tree.Mesh;
        var v0 = Vector3d.Zero; var v1 = Vector3d.Zero; var v2 = Vector3d.Zero;
        mesh.GetTriVertices(tid, ref v0, ref v1, ref v2);
        var intersection = new IntrRay3Triangle3(ray, new Triangle3d(v0, v1, v2));
        if (!intersection.Find())
            return null;
        return ray.PointAt(intersection.RayParameter);
    }

    /// <summary>Surface distance in mm between the hits of two pixels, or null if either ray misses the mesh.</summary>
    public double? MeasureMm(Vector2d a, Vector2d b, PinholeCamera cam, CameraPose pose)
    {
        Vector3d? pa = PixelToSurface(a, cam, pose);
        Vector3d? pb = PixelToSurface(b, cam, pose);
        if (pa is null || pb is null)
            return null;
        return pa.Value.Distance(pb.Value);
    }
}
