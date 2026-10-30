using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

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
    };

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
            return (CephXrayImage.FromBytes(fileName, bytes), source, null);
        }
        catch (Exception ex)
        {
            return (null, CephImageSource.Drr, $"Ceph X-ray {fileName}: {ex.Message}");
        }
    }
}
