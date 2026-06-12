using FellowOakDicom;
using FellowOakDicom.Imaging;

namespace OrthoPlanner.Core.Imaging;

/// <summary>
/// Loads a DICOM series from a folder and constructs a VolumeData.
/// </summary>
public static class DicomLoader
{
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
        var seriesSlices = new Dictionary<string, List<(string File, double Pos)>>();

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
                    seriesSlices[seriesUid] = new List<(string, double)>();
                }

                seriesSlices[seriesUid].Add((filePath, GetSlicePosition(dcm.Dataset)));
            }
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
            info.FilePaths = slices.Select(s => s.File).ToList();
            info.ImageCount = slices.Count;

            // Extract middle slice for thumbnail
            try
            {
                int midIdx = slices.Count / 2;
                var midDcm = await DicomFile.OpenAsync(slices[midIdx].File, FileReadOption.ReadAll);
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
                var rawBytes = pixelData.GetFrame(0).Data;

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

        // ÔöÇÔöÇ Phase 1: scan for slice positions (metadata only, no pixel data held in memory) ÔöÇÔöÇ
        var slices = new List<(string Path, double SlicePosition)>();
        foreach (var filePath in filePaths)
        {
            try
            {
                // Default open is fine here ÔÇö we only read small metadata tags
                var dcm = await DicomFile.OpenAsync(filePath);
                if (!dcm.Dataset.Contains(DicomTag.PixelData)) continue;

                slices.Add((filePath, GetSlicePosition(dcm.Dataset)));
            }
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
        // Each file is re-opened with FileReadOption.ReadAll to guarantee that pixel
        // bytes are fully loaded into memory ÔÇö avoids fo-dicom's lazy IByteBuffer
        // returning garbage for Implicit VR / large-tag deferred reads.
        int failedSlices = 0;
        for (int z = 0; z < depth; z++)
        {
            try
            {
                var dcm = await DicomFile.OpenAsync(slices[z].Path, FileReadOption.ReadAll);
                var ds = dcm.Dataset;

                double slope = ds.GetSingleValueOrDefault(DicomTag.RescaleSlope, 1.0);
                double intercept = ds.GetSingleValueOrDefault(DicomTag.RescaleIntercept, -1024.0);
                int bits = ds.GetSingleValueOrDefault(DicomTag.BitsAllocated, 16);
                int repr = ds.GetSingleValueOrDefault(DicomTag.PixelRepresentation, 0);

                var pixelData = DicomPixelData.Create(ds);
                byte[] rawBytes = pixelData.GetFrame(0).Data;

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
