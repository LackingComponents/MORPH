using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace OrthoPlanner.App.ViewModels.Cephalometry;

/// <summary>
/// Persists the Cephalometry X-ray inside the .orthoplan zip: original bytes under ceph/xray_lateral.&lt;ext&gt;,
/// metadata in project.json ("CephXray") and the selected source ("CephSource": "DRR" | "Xray").
/// </summary>
public static class CephXrayProjectStore
{
    public const string XrayKey = "CephXray";
    public const string SourceKey = "CephSource";
    private const string SourceDrr = "DRR";
    private const string SourceXray = "Xray";

    public static string SourceToJson(CephImageSource source) => source == CephImageSource.Xray ? SourceXray : SourceDrr;

    public static string EntryName(CephXrayImage xray) => $"ceph/xray_lateral.{xray.Extension}";

    /// <summary>project.json metadata for the X-ray, or null when none is loaded.</summary>
    public static object? BuildMeta(CephXrayImage? xray) => xray == null ? null : new
    {
        xray.FileName,
        ZipEntry = EntryName(xray),
        xray.PixelSpacingMm,
        xray.PixelSpacingRowMm,
        xray.IsDicom,
        xray.OriginalPixelWidth,
        xray.OriginalPixelHeight,
        // Scale: effective mm/px (ruler wins over DICOM) and its source; ruler points in image pixels
        XrayMmPerPixel = xray.Scale?.MmPerPixelX,
        XrayScaleSource = xray.Scale?.Source,
        RulerStart = Pt(xray.Measurements.RulerStart),
        RulerEnd = Pt(xray.Measurements.RulerEnd),
        // X-ray-only annotations (image pixels). Values are informative; they are recomputed on load.
        XrayDistances = xray.Measurements.Distances.Select(d => new
        {
            Start = Pt(d.Start), End = Pt(d.End),
            DistanceMm = xray.Scale?.DistanceMm(d.Start, d.End)
        }).ToArray(),
        XrayAngles = xray.Measurements.Angles.Select(a => new
        {
            A = Pt(a.A), Vertex = Pt(a.Vertex), B = Pt(a.B),
            AngleDeg = Finite(xray.Scale?.AngleDeg(a.A, a.Vertex, a.B))
        }).ToArray(),
    };

    private static object? Pt(Point? p) => p is Point v ? new { v.X, v.Y } : null;

    // System.Text.Json refuses NaN: a degenerate value is written as null.
    private static double? Finite(double? v) => v is double d && double.IsFinite(d) ? d : null;

    public static void WriteEntry(ZipArchive zip, CephXrayImage? xray)
    {
        if (xray == null) return;
        var entry = zip.CreateEntry(EntryName(xray), CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(xray.OriginalBytes, 0, xray.OriginalBytes.Length);
    }

    /// <summary>
    /// Reads the X-ray and source from a project. Old projects (no fields) give (null, Drr).
    /// Any problem is returned as a message; the source then falls back to DRR.
    /// </summary>
    public static (CephXrayImage? Xray, CephImageSource Source, string? Problem) Restore(ZipArchive zip, JsonElement root)
    {
        var source = root.TryGetProperty(SourceKey, out var s) && s.ValueKind == JsonValueKind.String &&
                     s.GetString() == SourceXray
            ? CephImageSource.Xray : CephImageSource.Drr;

        if (!root.TryGetProperty(XrayKey, out var meta) || meta.ValueKind != JsonValueKind.Object)
            return (null, CephImageSource.Drr, source == CephImageSource.Xray ? "Ceph X-ray source saved without an X-ray" : null);

        string fileName = meta.TryGetProperty("FileName", out var fn) && fn.ValueKind == JsonValueKind.String
            ? fn.GetString() ?? "xray" : "xray";
        string entryName = meta.TryGetProperty("ZipEntry", out var ze) && ze.ValueKind == JsonValueKind.String
            ? ze.GetString() ?? "" : "";
        var entry = entryName.Length > 0 ? zip.GetEntry(entryName) : null;
        if (entry == null)
            return (null, CephImageSource.Drr, $"Ceph X-ray {fileName}: missing zip entry '{entryName}'");

        try
        {
            byte[] bytes;
            using (var es = entry.Open())
            using (var ms = new MemoryStream())
            {
                es.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var xray = CephXrayImage.FromBytes(fileName, bytes);
            string? problem = RestoreMeasurements(meta, xray);
            return (xray, source, problem);
        }
        catch (Exception ex)
        {
            return (null, CephImageSource.Drr, $"Ceph X-ray {fileName}: {ex.Message}");
        }
    }

    /// <summary>Restores ruler scale and annotations. Older projects have none of these fields.</summary>
    private static string? RestoreMeasurements(JsonElement meta, CephXrayImage xray)
    {
        string? problem = null;
        string? scaleSource = meta.TryGetProperty("XrayScaleSource", out var ss) && ss.ValueKind == JsonValueKind.String
            ? ss.GetString() : null;
        if (scaleSource == XrayScale.SourceRuler)
        {
            double mmPerPx = ReadDouble(meta, "XrayMmPerPixel");
            Point? start = ReadPoint(meta, "RulerStart"), end = ReadPoint(meta, "RulerEnd");
            if (double.IsFinite(mmPerPx) && mmPerPx > 0 && start is Point s0 && end is Point e0)
                xray.Measurements.SetRuler(mmPerPx, s0, e0);
            else
                problem = $"Ceph X-ray {xray.FileName}: invalid ruler calibration, scale not restored";
        }

        foreach (var d in ReadArray(meta, "XrayDistances"))
            if (ReadPoint(d, "Start") is Point a && ReadPoint(d, "End") is Point b)
                xray.Measurements.Distances.Add(new XrayDistanceAnnotation(a, b));
        foreach (var g in ReadArray(meta, "XrayAngles"))
            if (ReadPoint(g, "A") is Point a && ReadPoint(g, "Vertex") is Point v && ReadPoint(g, "B") is Point b)
                xray.Measurements.Angles.Add(new XrayAngleAnnotation(a, v, b));
        return problem;
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement node, string key)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static double ReadDouble(JsonElement node, string key)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : double.NaN;

    private static Point? ReadPoint(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object) return null;
        double x = ReadDouble(el, "X"), y = ReadDouble(el, "Y");
        return double.IsFinite(x) && double.IsFinite(y) ? new Point(x, y) : null;
    }
}
