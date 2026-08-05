using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Loads a DICOM series from a folder and constructs a VolumeData.
/// </summary>
public static class DicomLoader
{
    // Frame 0 of an i-CAT multi-frame export is the most superior slice (vertex);
    // subsequent frames advance inferiorly, i.e. toward decreasing Z in LPS.
    // If a loaded volume appears vertically inverted, flip this to +1.
    private const int FRAME_ORDER_SIGN = -1;

    /// <summary>
    /// Fast scan of a folder to group DICOM files by SeriesInstanceUID,
    /// read metadata, and extract a middle slice thumbnail.
    /// </summary>
    public static async Task<List<DicomSeriesInfo>> ScanFolderAsync(string folderPath, Action<double>? progress = null)
    {
        var dicomFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ima", StringComparison.OrdinalIgnoreCase)
                     || !Path.HasExtension(f))
            .ToList();

        if (dicomFiles.Count == 0)
            throw new FileNotFoundException("No DICOM files found in the specified folder.");

        var seriesDict = new Dictionary<string, DicomSeriesInfo>();
        // Now one entry PER FRAME (not per file): a file with NumberOfFrames > 1
        // contributes N entries sharing the same File/Dataset but distinct FrameIndex.
        var seriesSlices = new Dictionary<string, List<(string File, DicomDataset Dataset, int FrameIndex, double Pos)>>();
        var seriesFilePaths = new Dictionary<string, List<string>>();

        int count = 0;
        foreach (var filePath in dicomFiles)
        {
            try
            {
                var dcm = await DicomFile.OpenAsync(filePath);
                if (!dcm.Dataset.Contains(DicomTag.PixelData)) continue;

                string seriesUid = dcm.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "Unknown");

                if (!seriesDict.TryGetValue(seriesUid, out var info))
                {
                    info = new DicomSeriesInfo
                    {
                        SeriesInstanceUID = seriesUid,
                        PatientName = dcm.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, "Unknown"),
                        PatientDOB = FormatDicomDate(dcm.Dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, "Unknown")),
                        StudyDate = FormatDicomDate(dcm.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, "Unknown")),
                        SeriesDescription = dcm.Dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, "Unknown Series")
                    };
                    seriesDict[seriesUid] = info;
                    seriesSlices[seriesUid] = new List<(string, DicomDataset, int, double)>();
                    seriesFilePaths[seriesUid] = new List<string>();
                }

                seriesFilePaths[seriesUid].Add(filePath);

                int frameCount = dcm.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
                if (frameCount < 1) frameCount = 1;

                double effectiveSpacing = frameCount > 1
                    ? GetEffectiveMultiFrameSpacing(dcm.Dataset, filePath)
                    : 0.0;

                double basePos = GetSlicePosition(dcm.Dataset);
                for (int f = 0; f < frameCount; f++)
                {
                    double pos = f == 0 ? basePos : basePos + FRAME_ORDER_SIGN * f * effectiveSpacing;
                    seriesSlices[seriesUid].Add((filePath, dcm.Dataset, f, pos));
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* Skip unreadable files */ }

            count++;
            if (progress != null && count % 50 == 0)
                progress((double)count / dicomFiles.Count * 0.5); // First 50% is scanning
        }

        var results = new List<DicomSeriesInfo>();
        
        // Generate thumbnails
        int seriesCount = 0;
        foreach (var kvp in seriesSlices)
        {
            var slices = kvp.Value;
            slices.Sort((a, b) => a.Pos.CompareTo(b.Pos));

            var info = seriesDict[kvp.Key];
            info.FilePaths = seriesFilePaths[kvp.Key];
            info.ImageCount = slices.Count; // total frame count across all files in the series

            // Extract middle slice for thumbnail
            try
            {
                int midIdx = slices.Count / 2;
                var midEntry = slices[midIdx];
                var midDcm = await DicomFile.OpenAsync(midEntry.File, FileReadOption.ReadAll);
                var ds = midDcm.Dataset;

                int w = ds.GetSingleValue<int>(DicomTag.Columns);
                int h = ds.GetSingleValue<int>(DicomTag.Rows);
                info.PreviewWidth = w;
                info.PreviewHeight = h;

                double slope = ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
                double intercept = ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, -1024.0);
                int bits = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, 16);
                int repr = ds.GetSingleValueOrDefault(DicomTag.PixelRepresentation, 0);

                var pixelData = DicomPixelData.Create(ds);
                var rawBytes = DecodeFrame(ds, pixelData, midEntry.FrameIndex);

                var pixels = new byte[w * h];
                for (int i = 0; i < w * h; i++)
                {
                    double stored = 0;
                    if (bits == 16)
                    {
                        int bi = i * 2;
                        if (bi + 2 > rawBytes.Length) { pixels[i] = 0; continue; }
                        stored = repr == 1
                            ? BitConverter.ToInt16(rawBytes, bi)
                            : (double)BitConverter.ToUInt16(rawBytes, bi);
                    }
                    else if (bits == 8)
                    {
                        if (i >= rawBytes.Length) { pixels[i] = 0; continue; }
                        stored = rawBytes[i];
                    }

                    double hu = stored * slope + intercept;
                    // Window level for bone/tissue roughly (W:1500, L:300)
                    double norm = Math.Clamp((hu - (-450)) / 1500.0, 0, 1);
                    pixels[i] = (byte)(norm * 255);
                }
                info.PreviewPixels = pixels;
            }
            catch { /* Fallback to empty preview */ }

            results.Add(info);
            seriesCount++;

            if (progress != null)
                progress(0.5 + 0.5 * ((double)seriesCount / seriesSlices.Count));
        }

        progress?.Invoke(1.0);
        return results;
    }

    /// <summary>
    /// Loads the actual volume data given a pre-sorted list of DICOM file paths.
    /// </summary>
    public static async Task<VolumeData> LoadSeriesAsync(List<string> filePaths, Action<double>? progress = null)
    {
        if (filePaths == null || filePaths.Count == 0)
            throw new ArgumentException("File paths list is empty.");

        // ÔöÇÔöÇ Phase 1: scan for slice positions (metadata only, no pixel data held in memory).
        // filePaths identifies which files belong to the series; a file may be multi-frame
        // (NumberOfFrames greater than 1), contributing one entry per frame. Dedupe the
        // input first since a multi-frame file only needs to be opened once. ÔöÇÔöÇ
        var slices = new List<(string Path, int FrameIndex, double SlicePosition)>();
        foreach (var filePath in filePaths.Distinct())
        {
            try
            {
                // Default open is fine here ÔÇö we only read small metadata tags
                var dcm = await DicomFile.OpenAsync(filePath);
                if (!dcm.Dataset.Contains(DicomTag.PixelData)) continue;

                int frameCount = dcm.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1);
                if (frameCount < 1) frameCount = 1;

                double effectiveSpacing = frameCount > 1
                    ? GetEffectiveMultiFrameSpacing(dcm.Dataset, filePath)
                    : 0.0;

                double basePos = GetSlicePosition(dcm.Dataset);
                for (int f = 0; f < frameCount; f++)
                {
                    double pos = f == 0 ? basePos : basePos + FRAME_ORDER_SIGN * f * effectiveSpacing;
                    slices.Add((filePath, f, pos));
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { continue; }
        }

        if (slices.Count == 0)
            throw new InvalidOperationException("No valid DICOM image slices found.");

        slices.Sort((a, b) => a.SlicePosition.CompareTo(b.SlicePosition));

        // Read volume geometry from first slice
        var firstDcm = await DicomFile.OpenAsync(slices[0].Path);
        var first = firstDcm.Dataset;

        int width = first.GetSingleValue<int>(DicomTag.Columns);
        int height = first.GetSingleValue<int>(DicomTag.Rows);
        int depth = slices.Count;

        double psX = 1.0, psY = 1.0;
        if (first.Contains(DicomTag.PixelSpacing))
        {
            var ps = first.GetValues<double>(DicomTag.PixelSpacing);
            if (ps.Length >= 2) { psY = ps[0]; psX = ps[1]; }
        }

        // Median inter-slice delta across the whole series: robust against a single
        // duplicated/missing slice or a bad first gap, which would otherwise scale
        // the entire volume wrong along Z.
        double sliceSpacing = 1.0;
        if (slices.Count > 1)
        {
            var deltas = new List<double>(slices.Count - 1);
            for (int i = 1; i < slices.Count; i++)
                deltas.Add(Math.Abs(slices[i].SlicePosition - slices[i - 1].SlicePosition));
            deltas.Sort();
            double median = deltas[deltas.Count / 2];
            if (median > 0.001) sliceSpacing = median;
        }

        var volume = new VolumeData(width, height, depth, [psX, psY, sliceSpacing]);
        volume.PatientName = first.GetSingleValueOrDefault(DicomTag.PatientName, "Unknown");
        volume.PatientDOB = FormatDicomDate(first.GetSingleValueOrDefault(DicomTag.PatientBirthDate, "Unknown"));
        volume.StudyDate = first.GetSingleValueOrDefault(DicomTag.StudyDate, "");
        volume.SeriesDescription = first.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");

        // ÔöÇÔöÇ Phase 2: read pixel data slice by slice ÔöÇÔöÇ
        // Each distinct file is re-opened with FileReadOption.ReadAll (once per file, not
        // once per frame) to guarantee that pixel bytes are fully loaded into memory ÔÇö
        // avoids fo-dicom's lazy IByteBuffer returning garbage for Implicit VR / large-tag
        // deferred reads. DicomPixelData.Create is likewise cached per file so a 432-frame
        // multi-frame file is not re-parsed 432 times.
        var decodeCache = new Dictionary<string,
            (DicomDataset Dataset, DicomPixelData PixelData, DicomTranscoder? Transcoder,
             double Slope, double Intercept, int Bits, int Repr)>();
        int failedSlices = 0;
        for (int z = 0; z < depth; z++)
        {
            try
            {
                var entry = slices[z];
                if (!decodeCache.TryGetValue(entry.Path, out var decode))
                {
                    var dcm = await DicomFile.OpenAsync(entry.Path, FileReadOption.ReadAll);
                    var ds = dcm.Dataset;

                    double fileSlope = ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
                    double fileIntercept = ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, -1024.0);
                    int fileBits = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, 16);
                    int fileRepr = ds.GetSingleValueOrDefault(DicomTag.PixelRepresentation, 0);

                    var pixelData = DicomPixelData.Create(ds);
                    DicomTranscoder? transcoder = ds.InternalTransferSyntax.IsEncapsulated
                        ? new DicomTranscoder(ds.InternalTransferSyntax, DicomTransferSyntax.ExplicitVRLittleEndian)
                        : null;

                    decode = (ds, pixelData, transcoder, fileSlope, fileIntercept, fileBits, fileRepr);
                    decodeCache[entry.Path] = decode;
                }

                double slope = decode.Slope;
                double intercept = decode.Intercept;
                int bits = decode.Bits;
                int repr = decode.Repr;

                byte[] rawBytes = decode.Transcoder != null
                    ? decode.Transcoder.DecodeFrame(decode.Dataset, entry.FrameIndex).Data
                    : decode.PixelData.GetFrame(entry.FrameIndex).Data;

                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int idx = x + y * width;

                    if (bits == 16)
                    {
                        int bi = idx * 2;
                        if (bi + 2 > rawBytes.Length)
                        {
                            volume.SetVoxel(x, y, z, -1000); // out-of-bounds ÔåÆ air
                            continue;
                        }
                        double raw = repr == 1
                            ? BitConverter.ToInt16(rawBytes, bi)   // SIGNED Int16
                            : (double)BitConverter.ToUInt16(rawBytes, bi);
                        double hu = raw * slope + intercept;
                        volume.SetVoxel(x, y, z, (short)Math.Clamp(hu, -1024, 3071));
                    }
                    else if (bits == 8)
                    {
                        if (idx >= rawBytes.Length)
                        {
                            volume.SetVoxel(x, y, z, -1000);
                            continue;
                        }
                        double hu = rawBytes[idx] * slope + intercept;
                        volume.SetVoxel(x, y, z, (short)Math.Clamp(hu, -1024, 3071));
                    }
                }
            }
            catch
            {
                failedSlices++;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    volume.SetVoxel(x, y, z, -1000);
            }

            progress?.Invoke((double)(z + 1) / depth);
        }

        // A surgical plan must never be built on a volume with silently missing
        // anatomy — surface the failure instead of shipping air-filled slices.
        if (failedSlices > 0)
            throw new InvalidOperationException(
                $"{failedSlices}/{depth} slices failed to decode (likely a compressed transfer syntax without the codec). Volume is incomplete.");

        volume.ComputeMinMax();
        return volume;
    }

    /// <summary>
    /// Resolves the per-frame Z spacing for a multi-frame single-file series (e.g. i-CAT
    /// CBCT exports) that carries no Shared/PerFrameFunctionalGroupsSequence. Prefers
    /// SpacingBetweenSlices, falls back to SliceThickness. Throws if neither yields a
    /// positive, finite value, since silently defaulting would scale the whole volume wrong.
    /// </summary>
    private static double GetEffectiveMultiFrameSpacing(DicomDataset ds, string filePath)
    {
        if (ds.Contains(DicomTag.SpacingBetweenSlices))
        {
            double spacing = ds.GetSingleValueOrDefault(DicomTag.SpacingBetweenSlices, 0.0);
            if (spacing > 0 && double.IsFinite(spacing)) return spacing;
        }

        if (ds.Contains(DicomTag.SliceThickness))
        {
            double spacing = ds.GetSingleValueOrDefault(DicomTag.SliceThickness, 0.0);
            if (spacing > 0 && double.IsFinite(spacing)) return spacing;
        }

        throw new InvalidOperationException(
            $"'{filePath}' has NumberOfFrames > 1 but neither SpacingBetweenSlices (0018,0088) " +
            "nor SliceThickness (0018,0050) is present with a positive, finite value — " +
            "cannot synthesize per-frame Z spacing.");
    }

    /// <summary>
    /// Returns native pixel bytes for one frame. DicomPixelData.GetFrame returns the
    /// encapsulated JPEG bitstream unchanged, so compressed frames must be decoded first.
    /// </summary>
    private static byte[] DecodeFrame(DicomDataset ds, DicomPixelData pixelData, int frameIndex)
    {
        if (!ds.InternalTransferSyntax.IsEncapsulated)
            return pixelData.GetFrame(frameIndex).Data;

        var transcoder = new DicomTranscoder(
            ds.InternalTransferSyntax,
            DicomTransferSyntax.ExplicitVRLittleEndian);
        return transcoder.DecodeFrame(ds, frameIndex).Data;
    }

    /// <summary>
    /// Sort position of a slice along the stacking axis.
    /// Prefers ImagePositionPatient projected onto the slice normal (cross product of
    /// the ImageOrientationPatient row/column vectors), which is correct for tilted
    /// and oblique acquisitions. Falls back to raw IPP Z, then SliceLocation (using
    /// Contains rather than a 0.0 sentinel, since 0 is a legitimate location), then
    /// InstanceNumber as a last resort.
    /// </summary>
    private static double GetSlicePosition(DicomDataset ds)
    {
        if (ds.TryGetValues(DicomTag.ImagePositionPatient, out double[]? ipp) && ipp.Length >= 3)
        {
            if (ds.TryGetValues(DicomTag.ImageOrientationPatient, out double[]? iop) && iop.Length >= 6)
            {
                double nx = iop[1] * iop[5] - iop[2] * iop[4];
                double ny = iop[2] * iop[3] - iop[0] * iop[5];
                double nz = iop[0] * iop[4] - iop[1] * iop[3];
                return ipp[0] * nx + ipp[1] * ny + ipp[2] * nz;
            }
            return ipp[2];
        }

        if (ds.Contains(DicomTag.SliceLocation))
            return ds.GetSingleValueOrDefault(DicomTag.SliceLocation, 0.0);

        return ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
    }

    private static string FormatDicomDate(string dicomDate)
    {
        if (string.IsNullOrWhiteSpace(dicomDate) || dicomDate.Length != 8) 
            return dicomDate;

        return $"{dicomDate.Substring(6, 2)}-{dicomDate.Substring(4, 2)}-{dicomDate.Substring(0, 4)}";
    }
}
