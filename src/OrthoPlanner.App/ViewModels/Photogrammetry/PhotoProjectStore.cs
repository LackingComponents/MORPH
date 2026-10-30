using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

/// <summary>
/// Persists Photogrammetry photos inside the .orthoplan zip: original file bytes under
/// photos/{i}_{safeFileName} plus per-photo state in the "Photos" array of project.json.
/// </summary>
public static class PhotoProjectStore
{
    public const string PhotosKey = "Photos";
    public const string ActivePhotoIndexKey = "ActivePhotoIndex";
    private const string EntryFolder = "photos/";

    /// <summary>What SaveProject must write: JSON metadata, zip entries, and photos that could not be saved.</summary>
    public sealed record SavePlan(object[] PhotosMeta, int ActivePhotoIndex,
                                  IReadOnlyList<(string EntryName, byte[] Bytes)> Entries,
                                  IReadOnlyList<string> Problems);

    public static SavePlan BuildSavePlan(PhotogrammetryViewModel space)
    {
        var meta = new List<object>();
        var entries = new List<(string, byte[])>();
        var problems = new List<string>();
        int activeIndex = -1;

        for (int i = 0; i < space.Photos.Count; i++)
        {
            var p = space.Photos[i];
            if (p.OriginalBytes.Length == 0)
            {
                problems.Add($"{p.FileName} ({p.LoadError ?? "no image data"})");
                continue;
            }
            if (ReferenceEquals(p, space.ActivePhoto)) activeIndex = meta.Count;

            string entryName = $"{EntryFolder}{i}_{SafeFileName(p.FileName)}";
            entries.Add((entryName, p.OriginalBytes));
            meta.Add(new
            {
                p.FileName,
                ZipEntry = entryName,
                Category = p.Category.ToString(),
                Expression = p.Expression.ToString(),
                p.RotationAngle,
                p.PixelsPerMm,
                p.IsNormalized,
                p.OriginalPixelWidth,
                p.OriginalPixelHeight,
                p.DecodeScale,
                Measurements = p.Measurements.Select(m => new
                {
                    m.Name, m.DistanceMm, StartPoint = Pt(m.StartPoint), EndPoint = Pt(m.EndPoint)
                }).ToArray(),
                LineAnnotations = p.LineAnnotations.Select(l => new
                {
                    StartPoint = Pt(l.StartPoint), EndPoint = Pt(l.EndPoint), l.Color
                }).ToArray(),
                AngleAnnotations = p.AngleAnnotations.Select(a => new
                {
                    a.Name, a.AngleDeg,
                    L1Start = Pt(a.L1Start), L1End = Pt(a.L1End),
                    L2Start = Pt(a.L2Start), L2End = Pt(a.L2End)
                }).ToArray(),
            });
        }
        return new SavePlan(meta.ToArray(), activeIndex, entries, problems);
    }

    public static void WriteEntries(ZipArchive zip, SavePlan plan)
    {
        foreach (var (entryName, bytes) in plan.Entries)
        {
            // JPEG/PNG are already compressed: store verbatim
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Rebuilds the photos from project.json + zip entries. Older projects without "Photos" leave the list empty.
    /// Returns a description of every photo that could not be restored (or restored with problems).
    /// </summary>
    public static List<string> Restore(ZipArchive zip, JsonElement root, PhotogrammetryViewModel space)
    {
        var problems = new List<string>();
        space.ClearPhotos();
        if (!root.TryGetProperty(PhotosKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return problems;

        var restored = new List<PhotoViewModel>();
        int savedActive = ReadInt(root, ActivePhotoIndexKey, -1);
        int restoredActive = -1;
        int idx = 0;
        foreach (var node in arr.EnumerateArray())
        {
            int savedIdx = idx++;
            string fileName = ReadString(node, "FileName", $"Photo_{savedIdx}");
            string entryName = ReadString(node, "ZipEntry", "");
            var entry = entryName.Length > 0 ? zip.GetEntry(entryName) : null;
            if (entry == null)
            {
                problems.Add($"{fileName} (missing zip entry '{entryName}')");
                continue;
            }

            byte[] bytes;
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }

            var photo = new PhotoViewModel(fileName, bytes);
            if (photo.LoadError != null)
                problems.Add($"{fileName} ({photo.LoadError}; kept in project, not displayed)");

            int savedW = ReadInt(node, "OriginalPixelWidth", 0);
            if (photo.LoadError == null && savedW > 0 && savedW != photo.OriginalPixelWidth)
                problems.Add($"{fileName} (saved width {savedW}px, decoded {photo.OriginalPixelWidth}px: annotations may be misplaced)");

            photo.Category = ReadEnum(node, "Category", PhotoCategory.Uncategorized);
            photo.Expression = ReadEnum(node, "Expression", PhotoExpression.Uncategorized);
            photo.RotationAngle = ReadDouble(node, "RotationAngle", 0);
            photo.PixelsPerMm = ReadDouble(node, "PixelsPerMm", 0);
            photo.IsNormalized = ReadBool(node, "IsNormalized") && photo.PixelsPerMm > 0;

            foreach (var m in ReadArray(node, "Measurements"))
                photo.Measurements.Add(new MeasurementViewModel
                {
                    Name = ReadString(m, "Name", ""),
                    DistanceMm = ReadDouble(m, "DistanceMm", 0),
                    StartPoint = ReadPoint(m, "StartPoint"),
                    EndPoint = ReadPoint(m, "EndPoint"),
                });
            foreach (var l in ReadArray(node, "LineAnnotations"))
                photo.LineAnnotations.Add(new LineAnnotationViewModel
                {
                    StartPoint = ReadPoint(l, "StartPoint"),
                    EndPoint = ReadPoint(l, "EndPoint"),
                    Color = ReadString(l, "Color", "#FFFFCC00"),
                });
            foreach (var a in ReadArray(node, "AngleAnnotations"))
                photo.AngleAnnotations.Add(new AngleAnnotationViewModel
                {
                    Name = ReadString(a, "Name", ""),
                    AngleDeg = ReadDouble(a, "AngleDeg", 0),
                    L1Start = ReadPoint(a, "L1Start"), L1End = ReadPoint(a, "L1End"),
                    L2Start = ReadPoint(a, "L2Start"), L2End = ReadPoint(a, "L2End"),
                });

            if (savedIdx == savedActive) restoredActive = restored.Count;
            restored.Add(photo);
        }

        space.RestorePhotos(restored, restoredActive);
        return problems;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c).ToArray());
        return safe.Length > 0 ? safe : "photo";
    }

    private static object Pt(Point p) => new { p.X, p.Y };

    private static IEnumerable<JsonElement> ReadArray(JsonElement node, string key)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static string ReadString(JsonElement node, string key, string fallback)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? fallback : fallback;

    private static double ReadDouble(JsonElement node, string key, double fallback)
    {
        if (!node.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Number) return fallback;
        double v = el.GetDouble();
        return double.IsFinite(v) ? v : fallback;
    }

    private static int ReadInt(JsonElement node, string key, int fallback)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v)
            ? v : fallback;

    private static bool ReadBool(JsonElement node, string key)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True;

    private static TEnum ReadEnum<TEnum>(JsonElement node, string key, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(ReadString(node, key, ""), out var v) && Enum.IsDefined(v) ? v : fallback;

    private static Point ReadPoint(JsonElement node, string key)
        => node.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Object
            ? new Point(ReadDouble(el, "X", 0), ReadDouble(el, "Y", 0)) : new Point();
}
