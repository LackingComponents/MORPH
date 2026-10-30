using System;
using System.Collections.Generic;
using System.Windows;

namespace OrthoPlanner.App.ViewModels.Cephalometry;

/// <summary>Pixel scale of the X-ray: mm per image pixel along X and Y, and where it comes from.</summary>
public readonly record struct XrayScale(double MmPerPixelX, double MmPerPixelY, string Source)
{
    public const string SourceRuler = "Ruler";
    public const string SourceDicom = "DICOM";

    public double DistanceMm(Point a, Point b)
    {
        double dx = (b.X - a.X) * MmPerPixelX, dy = (b.Y - a.Y) * MmPerPixelY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Angle A-Vertex-B in degrees, computed in mm space (correct for anisotropic DICOM pixels).</summary>
    public double AngleDeg(Point a, Point vertex, Point b)
    {
        double ax = (a.X - vertex.X) * MmPerPixelX, ay = (a.Y - vertex.Y) * MmPerPixelY;
        double bx = (b.X - vertex.X) * MmPerPixelX, by = (b.Y - vertex.Y) * MmPerPixelY;
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la == 0 || lb == 0) return double.NaN;
        return Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1.0, 1.0)) * 180.0 / Math.PI;
    }
}

/// <summary>X-ray-only distance annotation (image pixels). Never linked to CT landmarks.</summary>
public sealed record XrayDistanceAnnotation(Point Start, Point End);

/// <summary>X-ray-only 3-point angle annotation (image pixels), vertex in the middle.</summary>
public sealed record XrayAngleAnnotation(Point A, Point Vertex, Point B);

/// <summary>Ruler calibration and 2D annotations made on a cephalometric X-ray, all in image pixels.</summary>
public sealed class CephXrayMeasurements
{
    public double? RulerMmPerPixel { get; private set; }
    public Point? RulerStart { get; private set; }
    public Point? RulerEnd { get; private set; }

    public List<XrayDistanceAnnotation> Distances { get; } = new();
    public List<XrayAngleAnnotation> Angles { get; } = new();

    public bool HasAnnotations => Distances.Count > 0 || Angles.Count > 0;

    public void SetRuler(double mmPerPixel, Point start, Point end)
    {
        if (!double.IsFinite(mmPerPixel) || mmPerPixel <= 0)
            throw new ArgumentOutOfRangeException(nameof(mmPerPixel), $"Invalid ruler scale {mmPerPixel} mm/px.");
        RulerMmPerPixel = mmPerPixel;
        RulerStart = start;
        RulerEnd = end;
    }

    public void ClearAnnotations()
    {
        Distances.Clear();
        Angles.Clear();
    }
}
