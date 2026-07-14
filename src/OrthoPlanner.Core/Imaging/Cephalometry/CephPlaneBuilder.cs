using System.Numerics;

namespace OrthoPlanner.Core.Imaging.Cephalometry;

public static class CephPlaneBuilder
{
    public static bool TryBuildFrankfortHorizontal(
        IReadOnlyList<CephalometricLandmark> landmarks,
        out CephMeasurement plane,
        out string error)
    {
        plane = new CephMeasurement();
        error = "";

        if (!TryGet3D(landmarks, "Porion (L)", out var poL) ||
            !TryGet3D(landmarks, "Porion (R)", out var poR) ||
            !TryGet3D(landmarks, "Orbitale (L)", out var orL) ||
            !TryGet3D(landmarks, "Orbitale (R)", out var orR))
        {
            error = "Frankfort plane requires Porion (L/R) and Orbitale (L/R) placed in 3D.";
            return false;
        }

        var poMid = (poL + poR) * 0.5f;
        var orMid = (orL + orR) * 0.5f;
        var axisU = poR - poL;       // left-right direction
        var axisV = orMid - poMid;   // AP direction on Frankfort line
        var normal = Vector3.Cross(axisU, axisV);

        if (!TryNormalize(axisU, out axisU) ||
            !TryNormalize(axisV, out axisV) ||
            !TryNormalize(normal, out normal))
        {
            error = "Frankfort plane landmarks are nearly collinear; cannot build a stable plane.";
            return false;
        }

        var origin = (poMid + orMid) * 0.5f;
        plane = new CephMeasurement
        {
            Label = "Frankfort Plane",
            ToolType = CephTool.InfinitePlane,
            PlaneKind = CephPlaneKind.FrankfortHorizontal,
            PlaneOrigin3D = ToPoint(origin),
            PlaneNormal3D = ToPoint(normal),
            PlaneAxisU3D = ToPoint(axisU),
            PlaneAxisV3D = ToPoint(axisV),
            ColorR = 255,
            ColorG = 0,
            ColorB = 0,
            Opacity = 0.35,
            ConstructionNote = "Built from Porion (L/R) and midpoint of Orbitale (L/R)."
        };

        if (TryGet2D(landmarks, "Porion (L)", out var poL2) &&
            TryGet2D(landmarks, "Porion (R)", out var poR2) &&
            TryGet2D(landmarks, "Orbitale (L)", out var orL2) &&
            TryGet2D(landmarks, "Orbitale (R)", out var orR2))
        {
            var poMid2 = Mid(poL2, poR2);
            var orMid2 = Mid(orL2, orR2);
            plane.Points.Add(poMid2);
            plane.Points.Add(orMid2);
        }

        return true;
    }

    private static bool TryGet3D(
        IReadOnlyList<CephalometricLandmark> landmarks,
        string name,
        out Vector3 point)
    {
        foreach (var lm in landmarks)
        {
            if (!string.Equals(lm.Name, name, StringComparison.Ordinal)) continue;
            if (lm.Position3D is { } p)
            {
                point = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                return true;
            }
            break;
        }

        point = default;
        return false;
    }

    private static bool TryGet2D(
        IReadOnlyList<CephalometricLandmark> landmarks,
        string name,
        out CephPoint point)
    {
        foreach (var lm in landmarks)
        {
            if (!string.Equals(lm.Name, name, StringComparison.Ordinal)) continue;
            if (lm.Position is { } p)
            {
                point = new CephPoint(p.X, p.Y);
                return true;
            }
            break;
        }

        point = default;
        return false;
    }

    private static bool TryNormalize(Vector3 input, out Vector3 normalized)
    {
        var len = input.Length();
        if (len < 1e-4f)
        {
            normalized = default;
            return false;
        }

        normalized = input / len;
        return true;
    }

    private static CephPoint Mid(CephPoint a, CephPoint b) =>
        new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static CephPoint3D ToPoint(Vector3 p) => new(p.X, p.Y, p.Z);
}
