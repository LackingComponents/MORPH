using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace OrthoPlanner.App.ViewModels.Cephalometry;

/// <summary>Image shown in the Cephalometry view.</summary>
public enum CephImageSource
{
    Drr,
    Xray,
}

/// <summary>
/// A real lateral cephalogram loaded from file. <see cref="OriginalBytes"/> are kept verbatim (never
/// re-encoded) for project persistence; <see cref="Luminance"/> is the decoded image normalised to [0,1]
/// in clinical polarity (bone bright), row-major, OriginalPixelWidth x OriginalPixelHeight.
/// </summary>
public sealed class CephXrayImage
{
    public string FileName { get; }
    public byte[] OriginalBytes { get; }
    public bool IsDicom { get; }

    /// <summary>Column (horizontal) spacing in mm/px from DICOM PixelSpacing/ImagerPixelSpacing; null if unknown.</summary>
    public double? PixelSpacingMm { get; }

    /// <summary>Row (vertical) spacing in mm/px; null if unknown.</summary>
    public double? PixelSpacingRowMm { get; }

    public int OriginalPixelWidth { get; }
    public int OriginalPixelHeight { get; }
    public float[] Luminance { get; }

    /// <summary>Ruler calibration and 2D annotations made on this X-ray (image pixels).</summary>
    public CephXrayMeasurements Measurements { get; } = new();

    /// <summary>Current pixel scale: the ruler calibration wins over DICOM spacing; null when neither exists.</summary>
    public XrayScale? Scale
    {
        get
        {
            if (Measurements.RulerMmPerPixel is double r)
                return new XrayScale(r, r, XrayScale.SourceRuler);
            if (PixelSpacingMm is double sx)
                return new XrayScale(sx, PixelSpacingRowMm ?? sx, XrayScale.SourceDicom);
            return null;
        }
    }

    private CephXrayImage(string fileName, byte[] bytes, bool isDicom, double? spacingCol, double? spacingRow,
                          int width, int height, float[] luminance)
    {
        FileName = fileName;
        OriginalBytes = bytes;
        IsDicom = isDicom;
        PixelSpacingMm = spacingCol;
        PixelSpacingRowMm = spacingRow;
        OriginalPixelWidth = width;
        OriginalPixelHeight = height;
        Luminance = luminance;
    }

    /// <summary>Zip entry extension: the original one, or "dcm" for extension-less DICOM files.</summary>
    public string Extension
    {
        get
        {
            string ext = Path.GetExtension(FileName).TrimStart('.').ToLowerInvariant();
            if (ext.Length > 0 && ext.All(char.IsLetterOrDigit)) return ext;
            return IsDicom ? "dcm" : "img";
        }
    }

    public static CephXrayImage LoadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return FromBytes(Path.GetFileName(path), bytes);
    }

    /// <summary>Decodes an image or DICOM file. Throws <see cref="InvalidDataException"/> with a readable message on failure.</summary>
    public static CephXrayImage FromBytes(string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new InvalidDataException($"{fileName}: file is empty.");

        bool hasPreamble = bytes.Length > 132 &&
                           bytes[128] == 'D' && bytes[129] == 'I' && bytes[130] == 'C' && bytes[131] == 'M';
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (hasPreamble || ext == ".dcm")
            return DecodeDicom(fileName, bytes);
        if (ext.Length > 0)
            return DecodeBitmap(fileName, bytes);

        // No extension: usually DICOM without preamble; fall back to a plain image file.
        try { return DecodeDicom(fileName, bytes); }
        catch (InvalidDataException dicomError)
        {
            try { return DecodeBitmap(fileName, bytes); }
            catch (InvalidDataException) { throw dicomError; }
        }
    }

    private static CephXrayImage DecodeBitmap(string fileName, byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var gray = new FormatConvertedBitmap(frame, PixelFormats.Gray16, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight;
            var raw = new ushort[w * h];
            gray.CopyPixels(raw, w * 2, 0);

            var lum = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) lum[i] = raw[i] / 65535f;
            return new CephXrayImage(fileName, bytes, false, null, null, w, h, lum);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{fileName}: cannot decode image ({ex.Message}).", ex);
        }
    }

    private static CephXrayImage DecodeDicom(string fileName, byte[] bytes)
    {
        DicomDataset ds;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            ds = DicomFile.Open(ms, FileReadOption.ReadAll).Dataset;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{fileName}: not a readable DICOM file ({ex.Message}).", ex);
        }

        try
        {
            if (!ds.Contains(DicomTag.PixelData))
                throw new InvalidDataException($"{fileName}: DICOM file has no image (PixelData missing).");

            int w = ds.GetSingleValue<int>(DicomTag.Columns);
            int h = ds.GetSingleValue<int>(DicomTag.Rows);
            int bits = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, 16);
            int samples = ds.GetSingleValueOrDefault(DicomTag.SamplesPerPixel, 1);
            int repr = ds.GetSingleValueOrDefault(DicomTag.PixelRepresentation, 0);
            string photometric = ds.GetSingleValueOrDefault(DicomTag.PhotometricInterpretation, "MONOCHROME2");
            if (samples != 1 || (bits != 8 && bits != 16))
                throw new InvalidDataException(
                    $"{fileName}: unsupported DICOM pixel format ({samples} samples, {bits} bits allocated).");

            var pixelData = DicomPixelData.Create(ds);
            byte[] data = ds.InternalTransferSyntax.IsEncapsulated
                ? new DicomTranscoder(ds.InternalTransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian)
                    .DecodeFrame(ds, 0).Data
                : pixelData.GetFrame(0).Data;
            if (data.Length < w * h * (bits / 8))
                throw new InvalidDataException($"{fileName}: DICOM pixel data is truncated.");

            var stored = new float[w * h];
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < stored.Length; i++)
            {
                float v = bits == 8 ? data[i]
                        : repr == 1 ? BitConverter.ToInt16(data, i * 2)
                        : BitConverter.ToUInt16(data, i * 2);
                stored[i] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float range = max > min ? max - min : 1f;
            bool monochrome1 = photometric.Trim().Equals("MONOCHROME1", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < stored.Length; i++)
            {
                float n = (stored[i] - min) / range;
                stored[i] = monochrome1 ? 1f - n : n; // MONOCHROME1: low values are white
            }

            var (col, row) = ReadSpacing(ds);
            return new CephXrayImage(fileName, bytes, true, col, row, w, h, stored);
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"{fileName}: cannot decode DICOM image ({ex.Message}).", ex);
        }
    }

    /// <summary>PixelSpacing, else ImagerPixelSpacing. DICOM order is [row spacing, column spacing].</summary>
    private static (double? Col, double? Row) ReadSpacing(DicomDataset ds)
    {
        foreach (var tag in new[] { DicomTag.PixelSpacing, DicomTag.ImagerPixelSpacing })
        {
            if (ds.TryGetValues(tag, out double[]? v) && v.Length >= 2 &&
                double.IsFinite(v[0]) && double.IsFinite(v[1]) && v[0] > 0 && v[1] > 0)
                return (v[1], v[0]);
        }
        return (null, null);
    }
}
