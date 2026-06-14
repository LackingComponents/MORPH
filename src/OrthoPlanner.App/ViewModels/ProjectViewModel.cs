using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    private void ReportLoadProgress(double value)
    {
        LoadProgress = value;
        Application.Current.Dispatcher.Invoke(
            System.Windows.Threading.DispatcherPriority.Render,
            static () => { });
    }

    [RelayCommand]
    private void SaveProject()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save OrthoPlanner Project",
            Filter = "OrthoPlanner Project (*.orthoplan)|*.orthoplan",
            DefaultExt = ".orthoplan"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            LoadProgress = 0;
            StatusText = "Saving project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            // Helper: Matrix3D → 16-element double array (row-major)
            static double[] MatrixToArray(System.Windows.Media.Media3D.Matrix3D m) =>
                new[] { m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.OffsetX, m.OffsetY, m.OffsetZ, m.M44 };
            static double[]? PointToArray((double X, double Y, double Z)? p) =>
                p.HasValue ? new[] { p.Value.X, p.Value.Y, p.Value.Z } : null;

            // 1. project.json — metadata
            var meta = new
            {
                Version = "2.2",
                PatientName,
                StudyDate,
                CondyleFulcrums = new
                {
                    LeftCondyleCenter = PointToArray(LeftCondyleCenter),
                    RightCondyleCenter = PointToArray(RightCondyleCenter),
                    LeftCondyleHalfExtents = PointToArray(LeftCondyleHalfExtents),
                    RightCondyleHalfExtents = PointToArray(RightCondyleHalfExtents),
                    DentalMidlinePoint = PointToArray(DentalMidlinePoint)
                },
                Segmentation = new
                {
                    BoneMinHU, BoneMaxHU,
                    SoftMinHU, SoftMaxHU,
                    DentalMinHU, DentalMaxHU,
                    CustomMinHU, CustomMaxHU,
                    Segments = Segments.Select(s => new { s.Name, s.IsVisible, s.ColorR, s.ColorG, s.ColorB }).ToArray()
                },
                ImportedMeshes = ImportedMeshes.Select(m => new { m.Name, m.IsVisible, m.ColorR, m.ColorG, m.ColorB, ScanType = m.ScanType.ToString(), m.ShowInModelsPanel }).ToArray(),
                // Issue 11: occlusion meshes with their alignment transforms
                OcclusionMeshes = LoadedOcclusions.Select(o => new
                {
                    o.Name, o.IsVisible, o.ColorR, o.ColorG, o.ColorB,
                    MaxillaOcclusionTransform = MatrixToArray(o.MaxillaOcclusionTransform),
                    MandibleOcclusionTransform = MatrixToArray(o.MandibleOcclusionTransform)
                }).ToArray(),
                CurrentSurgeryPlan = SnapshotCurrentPlan("Current"),
                ActiveOcclusionIndex = _activeOcclusionNode != null ? LoadedOcclusions.IndexOf(_activeOcclusionNode.Occlusion) : -1,
                OcclusionPlanNodes = OcclusionNodes.Select(n => new
                {
                    n.Name,
                    n.IsExpanded,
                    n.IsActive,
                    OcclusionIndex = LoadedOcclusions.IndexOf(n.Occlusion),
                    Plans = n.Plans.ToArray()
                }).ToArray(),
                Volume = Volume != null ? new { Volume.Width, Volume.Height, Volume.Depth, Volume.Spacing } : null,
                WindowCenter,
                WindowWidth,
                // Issue 10: cephalometry landmarks
                CephLandmarks = SavedCephLandmarks.Select(c => new
                {
                    c.Name, c.X2D, c.Y2D, c.X3D, c.Y3D, c.Z3D
                }).ToArray()
            };
            var jsonEntry = zip.CreateEntry("project.json");
            using (var sw = new StreamWriter(jsonEntry.Open()))
            {
                sw.Write(System.Text.Json.JsonSerializer.Serialize(meta,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            LoadProgress = 10;

            // 2. volume.bin — raw voxel data
            if (Volume != null)
            {
                var volEntry = zip.CreateEntry("volume.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var volStream = volEntry.Open();
                var bytes = new byte[Volume.Voxels.Length * 2];
                Buffer.BlockCopy(Volume.Voxels, 0, bytes, 0, bytes.Length);
                const int chunkSize = 1 << 20;
                for (int offset = 0; offset < bytes.Length; offset += chunkSize)
                {
                    int count = Math.Min(chunkSize, bytes.Length - offset);
                    volStream.Write(bytes, offset, count);
                    ReportLoadProgress(10 + (double)offset / bytes.Length * 40);
                }
            }
            else
                ReportLoadProgress(50);

            // 3. meshes/*.bin — imported STL vertex data
            for (int i = 0; i < ImportedMeshes.Count; i++)
            {
                var mesh = ImportedMeshes[i];
                if (mesh.Vertices == null) continue;
                var meshEntry = zip.CreateEntry($"meshes/{i}_{mesh.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var ms = meshEntry.Open();
                using var bw = new BinaryWriter(ms);
                bw.Write(mesh.Vertices.Length / 3);
                for (int vi = 0; vi < mesh.Vertices.Length; vi += 3)
                    { bw.Write(mesh.Vertices[vi]); bw.Write(mesh.Vertices[vi + 1]); bw.Write(mesh.Vertices[vi + 2]); }
                ReportLoadProgress(50 + (double)(i + 1) / Math.Max(1, ImportedMeshes.Count) * 15);
            }

            // 4. segments/*.bin — segmented 3D model vertex data
            for (int i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                if (seg.Vertices == null) continue;
                var segEntry = zip.CreateEntry($"segments/{i}_{seg.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var ss = segEntry.Open();
                using var bw2 = new BinaryWriter(ss);
                bw2.Write(seg.Vertices.Length / 3);
                for (int vi = 0; vi < seg.Vertices.Length; vi += 3)
                    { bw2.Write(seg.Vertices[vi]); bw2.Write(seg.Vertices[vi + 1]); bw2.Write(seg.Vertices[vi + 2]); }
                ReportLoadProgress(65 + (double)(i + 1) / Math.Max(1, Segments.Count) * 15);
            }

            // 5. (Issue 11) occlusions/*.bin — occlusion STL vertex data
            for (int i = 0; i < LoadedOcclusions.Count; i++)
            {
                var occ = LoadedOcclusions[i];
                if (occ.Vertices == null) continue;
                var occEntry = zip.CreateEntry($"occlusions/{i}_{occ.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var os = occEntry.Open();
                using var bw3 = new BinaryWriter(os);
                bw3.Write(occ.Vertices.Length / 3);
                for (int vi = 0; vi < occ.Vertices.Length; vi += 3)
                    { bw3.Write(occ.Vertices[vi]); bw3.Write(occ.Vertices[vi + 1]); bw3.Write(occ.Vertices[vi + 2]); }
                ReportLoadProgress(80 + (double)(i + 1) / Math.Max(1, LoadedOcclusions.Count) * 15);
            }

            LoadProgress = 100;
            StatusText = $"Project saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (IsVolumeLoaded)
        {
            var res = System.Windows.MessageBox.Show(
                "A project is already open. Do you want to save it before opening another?",
                "Save Current Project?", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);

            if (res == System.Windows.MessageBoxResult.Cancel) return;
            if (res == System.Windows.MessageBoxResult.Yes) SaveProject();
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open OrthoPlanner Project",
            Filter = "OrthoPlanner Project (*.orthoplan)|*.orthoplan|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            LoadProgress = 0;
            StatusText = "Loading project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

            // 1. Read project.json
            var jsonEntry = zip.GetEntry("project.json");
            if (jsonEntry == null) { StatusText = "Invalid project file"; return; }

            string json;
            using (var sr = new StreamReader(jsonEntry.Open()))
                json = await sr.ReadToEndAsync();
            LoadProgress = 10;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            PatientName = root.GetProperty("PatientName").GetString() ?? "";
            StudyDate = FormatStudyDate(root.GetProperty("StudyDate").GetString() ?? "");
            WindowCenter = root.GetProperty("WindowCenter").GetDouble();
            WindowWidth = root.GetProperty("WindowWidth").GetDouble();
            static (double X, double Y, double Z)? ReadPoint(System.Text.Json.JsonElement parent, string name)
            {
                if (!parent.TryGetProperty(name, out var point) || point.ValueKind == System.Text.Json.JsonValueKind.Null)
                    return null;

                if (point.ValueKind == System.Text.Json.JsonValueKind.Array && point.GetArrayLength() >= 3)
                    return (point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble());

                if (point.TryGetProperty("X", out var x)
                    && point.TryGetProperty("Y", out var y)
                    && point.TryGetProperty("Z", out var z))
                    return (x.GetDouble(), y.GetDouble(), z.GetDouble());

                return null;
            }

            if (root.TryGetProperty("CondyleFulcrums", out var condyleNode))
            {
                LeftCondyleCenter = ReadPoint(condyleNode, nameof(LeftCondyleCenter));
                RightCondyleCenter = ReadPoint(condyleNode, nameof(RightCondyleCenter));
                LeftCondyleHalfExtents = ReadPoint(condyleNode, nameof(LeftCondyleHalfExtents));
                RightCondyleHalfExtents = ReadPoint(condyleNode, nameof(RightCondyleHalfExtents));
                DentalMidlinePoint = ReadPoint(condyleNode, nameof(DentalMidlinePoint));
            }
            else
            {
                LeftCondyleCenter = null;
                RightCondyleCenter = null;
                LeftCondyleHalfExtents = null;
                RightCondyleHalfExtents = null;
                DentalMidlinePoint = null;
            }
            var segNode = root.GetProperty("Segmentation");

            // Backwards compatibility for older project files
            if (segNode.TryGetProperty("MinHU", out var minHuProp))
            {
                CustomMinHU = minHuProp.GetDouble();
                CustomMaxHU = segNode.GetProperty("MaxHU").GetDouble();
            }
            else
            {
                BoneMinHU = segNode.GetProperty("BoneMinHU").GetDouble();
                BoneMaxHU = segNode.GetProperty("BoneMaxHU").GetDouble();
                SoftMinHU = segNode.GetProperty("SoftMinHU").GetDouble();
                SoftMaxHU = segNode.GetProperty("SoftMaxHU").GetDouble();
                DentalMinHU = segNode.GetProperty("DentalMinHU").GetDouble();
                DentalMaxHU = segNode.GetProperty("DentalMaxHU").GetDouble();
                CustomMinHU = segNode.GetProperty("CustomMinHU").GetDouble();
                CustomMaxHU = segNode.GetProperty("CustomMaxHU").GetDouble();
            }

            // 2. Read volume.bin
            var volMeta = root.GetProperty("Volume");
            if (volMeta.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                int w = volMeta.GetProperty("Width").GetInt32();
                int h = volMeta.GetProperty("Height").GetInt32();
                int d = volMeta.GetProperty("Depth").GetInt32();
                var spacingArr = volMeta.GetProperty("Spacing");
                double[] spacing = new double[3];
                for (int i = 0; i < 3; i++)
                    spacing[i] = spacingArr[i].GetDouble();

                var volEntry = zip.GetEntry("volume.bin");
                if (volEntry != null)
                {
                    var vol = new VolumeData(w, h, d, spacing);
                    using var volStream = volEntry.Open();
                    var bytes = new byte[vol.Voxels.Length * 2];
                    int totalRead = 0;
                    while (totalRead < bytes.Length)
                    {
                        int read = await volStream.ReadAsync(bytes, totalRead, bytes.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                        ReportLoadProgress(10 + (double)totalRead / bytes.Length * 35);
                    }
                    Buffer.BlockCopy(bytes, 0, vol.Voxels, 0, bytes.Length);
                    vol.PatientName = PatientName;
                    vol.StudyDate = StudyDate;
                    vol.ComputeMinMax();

                    Volume = vol;
                    OriginalVolume = null; // Reset starting position for new project
                    IsVolumeLoaded = true;
                    IsoMin = Math.Max(-1000, (double)vol.MinValue);
                    IsoMax = vol.MaxValue;
                    AxialMax = vol.Depth - 1;
                    CoronalMax = vol.Height - 1;
                    SagittalMax = vol.Width - 1;
                    AxialIndex = vol.Depth / 2;
                    CoronalIndex = vol.Height / 2;
                    SagittalIndex = vol.Width / 2;

                    // Restore physical aspect ratios so MPR views are not stretched
                    AxialDisplayHeight    = new System.Windows.GridLength(vol.Height * vol.Spacing[1], System.Windows.GridUnitType.Star);
                    CoronalDisplayHeight  = new System.Windows.GridLength(vol.Depth  * vol.Spacing[2], System.Windows.GridUnitType.Star);
                    SagittalDisplayHeight = new System.Windows.GridLength(vol.Depth  * vol.Spacing[2], System.Windows.GridUnitType.Star);

                    UpdateHistograms();
                    UpdateAllSlices();
                }
            }

            static DentalScanType ReadScanType(System.Text.Json.JsonElement meshMeta, string name)
            {
                if (meshMeta.TryGetProperty("ScanType", out var scanTypeProp)
                    && Enum.TryParse<DentalScanType>(scanTypeProp.GetString(), ignoreCase: true, out var scanType))
                {
                    return scanType;
                }

                // Older project files did not persist ScanType; infer from the default import names.
                if (name.Contains("Maxillary", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Upper", StringComparison.OrdinalIgnoreCase))
                    return DentalScanType.Upper;

                if (name.Contains("Mandibular", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Lower", StringComparison.OrdinalIgnoreCase))
                    return DentalScanType.Lower;

                return DentalScanType.Other;
            }

            static bool ReadBool(System.Text.Json.JsonElement parent, string name, bool defaultValue = false) =>
                parent.TryGetProperty(name, out var value) && (value.ValueKind == System.Text.Json.JsonValueKind.True || value.ValueKind == System.Text.Json.JsonValueKind.False)
                    ? value.GetBoolean()
                    : defaultValue;

            static double ReadDouble(System.Text.Json.JsonElement parent, string name) =>
                parent.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? value.GetDouble()
                    : 0;

            static string ReadString(System.Text.Json.JsonElement parent, string name, string defaultValue) =>
                parent.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString() ?? defaultValue
                    : defaultValue;

            static OcclusionPlanViewModel ReadPlan(System.Text.Json.JsonElement planMeta, string defaultName) => new()
            {
                Name = ReadString(planMeta, nameof(OcclusionPlanViewModel.Name), defaultName),
                IsSelected = ReadBool(planMeta, nameof(OcclusionPlanViewModel.IsSelected)),
                IsMaxillaBasedSurgery = ReadBool(planMeta, nameof(OcclusionPlanViewModel.IsMaxillaBasedSurgery), true),
                IsMandibleBasedSurgery = ReadBool(planMeta, nameof(OcclusionPlanViewModel.IsMandibleBasedSurgery)),
                IsManualOcclusionSurgery = ReadBool(planMeta, nameof(OcclusionPlanViewModel.IsManualOcclusionSurgery)),
                IsKeepOcclusionSurgery = ReadBool(planMeta, nameof(OcclusionPlanViewModel.IsKeepOcclusionSurgery)),
                MaxillaLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaLat)),
                MaxillaAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaAnt)),
                MaxillaVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaVert)),
                MaxillaRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaRoll)),
                MaxillaPitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaPitch)),
                MaxillaYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaYaw)),
                MandibleLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleLat)),
                MandibleAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleAnt)),
                MandibleVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleVert)),
                MandibleRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleRoll)),
                MandiblePitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandiblePitch)),
                MandibleYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleYaw)),
                RightRamusLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusLat)),
                RightRamusAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusAnt)),
                RightRamusVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusVert)),
                RightRamusRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusRoll)),
                RightRamusPitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusPitch)),
                RightRamusYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusYaw)),
                LeftRamusLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusLat)),
                LeftRamusAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusAnt)),
                LeftRamusVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusVert)),
                LeftRamusRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusRoll)),
                LeftRamusPitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusPitch)),
                LeftRamusYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusYaw)),
                ChinLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinLat)),
                ChinAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinAnt)),
                ChinVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinVert)),
                ChinRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinRoll)),
                ChinPitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinPitch)),
                ChinYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.ChinYaw)),
                SavedMaxillaLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaLat)),
                SavedMaxillaAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaAnt)),
                SavedMaxillaVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaVert)),
                SavedMaxillaRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaRoll)),
                SavedMaxillaPitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaPitch)),
                SavedMaxillaYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaYaw)),
                SavedMandibleLat = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleLat)),
                SavedMandibleAnt = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleAnt)),
                SavedMandibleVert = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleVert)),
                SavedMandibleRoll = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleRoll)),
                SavedMandiblePitch = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandiblePitch)),
                SavedMandibleYaw = ReadDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleYaw)),
            };

            // 3. Read imported meshes
            ImportedMeshes.Clear();
            var meshesArr = root.GetProperty("ImportedMeshes");
            int meshIdx = 0;
            foreach (var meshMeta in meshesArr.EnumerateArray())
            {
                string name = meshMeta.GetProperty("Name").GetString() ?? $"Mesh_{meshIdx}";
                var meshEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith($"meshes/{meshIdx}_"));
                if (meshEntry != null)
                {
                    using var ms = meshEntry.Open();
                    using var br = new BinaryReader(ms);
                    int count = br.ReadInt32();
                    var vertices = new float[count * 3];
                    for (int i = 0; i < count; i++)
                    { vertices[i * 3] = br.ReadSingle(); vertices[i * 3 + 1] = br.ReadSingle(); vertices[i * 3 + 2] = br.ReadSingle(); }

                    var meshVm = new MeshViewModel
                    {
                        Name = name,
                        Vertices = vertices,
                        ColorR = meshMeta.TryGetProperty("ColorR", out var cr) ? cr.GetByte() : (byte)245,
                        ColorG = meshMeta.TryGetProperty("ColorG", out var cg) ? cg.GetByte() : (byte)245,
                        ColorB = meshMeta.TryGetProperty("ColorB", out var cb) ? cb.GetByte() : (byte)230,
                        ScanType = ReadScanType(meshMeta, name),
                        IsVisible = meshMeta.GetProperty("IsVisible").GetBoolean(),
                        ShowInModelsPanel = meshMeta.TryGetProperty("ShowInModelsPanel", out var sip)
                            ? sip.GetBoolean()
                            : name.Contains("Splint", StringComparison.OrdinalIgnoreCase)
                    };
                    meshVm.OnVisibilityChanged = RefreshCombinedModel;
                    meshVm.BuildModel();
                    ImportedMeshes.Add(meshVm);
                }
                meshIdx++;
                ReportLoadProgress(45 + (double)meshIdx / Math.Max(1, meshesArr.GetArrayLength()) * 15);
            }

            // 4. Read segments
            Segments.Clear();
            if (root.TryGetProperty("Segmentation", out var segProp) && segProp.TryGetProperty("Segments", out var segsArr))
            {
                int segIdx = 0;
                foreach (var segMeta in segsArr.EnumerateArray())
                {
                    string sName = segMeta.GetProperty("Name").GetString() ?? $"Segment_{segIdx}";
                    var segEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith($"segments/{segIdx}_"));
                    if (segEntry != null)
                    {
                        using var ss = segEntry.Open();
                        using var br2 = new BinaryReader(ss);
                        int cnt = br2.ReadInt32();
                        var verts = new float[cnt * 3];
                        for (int i = 0; i < cnt; i++)
                        { verts[i * 3] = br2.ReadSingle(); verts[i * 3 + 1] = br2.ReadSingle(); verts[i * 3 + 2] = br2.ReadSingle(); }

                        var segVm = new SegmentViewModel
                        {
                            Name = sName,
                            Vertices = verts,
                            ColorR = segMeta.TryGetProperty("ColorR", out var scr) ? scr.GetByte() : (byte)200,
                            ColorG = segMeta.TryGetProperty("ColorG", out var scg) ? scg.GetByte() : (byte)180,
                            ColorB = segMeta.TryGetProperty("ColorB", out var scb) ? scb.GetByte() : (byte)140,
                            IsVisible = segMeta.GetProperty("IsVisible").GetBoolean()
                        };
                        segVm.OnVisibilityChanged = RefreshCombinedModel;
                        segVm.BuildModel();
                        Segments.Add(segVm);

                        // Restore named properties
                        if (sName == "Bone" || sName.StartsWith("Bone")) HardTissueModel = segVm;
                        else if (sName == "Soft Tissue" || sName.StartsWith("Soft Tissue")) SoftTissueModel = segVm;
                        else if (sName == "Dental Scan" || sName.StartsWith("Dental")) DentalModel = segVm;
                    }
                    segIdx++;
                    ReportLoadProgress(60 + (double)segIdx / Math.Max(1, segsArr.GetArrayLength()) * 15);
                }
            }

            // 5. (Issue 11) Read occlusion meshes + their alignment transforms
            LoadedOcclusions.Clear();
            OcclusionNodes.Clear();
            if (root.TryGetProperty("OcclusionMeshes", out var occArr))
            {
                // Helper: 16-element double array -> Matrix3D (row-major)
                static System.Windows.Media.Media3D.Matrix3D ArrayToMatrix(System.Text.Json.JsonElement el)
                {
                    var d = new double[16];
                    int idx = 0;
                    foreach (var v in el.EnumerateArray()) d[idx++] = v.GetDouble();
                    return new System.Windows.Media.Media3D.Matrix3D(
                        d[0],  d[1],  d[2],  d[3],
                        d[4],  d[5],  d[6],  d[7],
                        d[8],  d[9],  d[10], d[11],
                        d[12], d[13], d[14], d[15]);
                }

                int occIdx = 0;
                foreach (var occMeta in occArr.EnumerateArray())
                {
                    string oName = occMeta.TryGetProperty("Name", out var np) ? np.GetString() ?? $"Occlusion_{occIdx}" : $"Occlusion_{occIdx}";
                    var occEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith($"occlusions/{occIdx}_"));
                    if (occEntry != null)
                    {
                        using var os = occEntry.Open();
                        using var br3 = new BinaryReader(os);
                        int cnt = br3.ReadInt32();
                        var verts = new float[cnt * 3];
                        for (int i = 0; i < cnt; i++)
                        { verts[i * 3] = br3.ReadSingle(); verts[i * 3 + 1] = br3.ReadSingle(); verts[i * 3 + 2] = br3.ReadSingle(); }

                        var occVm = new MeshViewModel
                        {
                            Name = oName,
                            Vertices = verts,
                            ColorR = occMeta.TryGetProperty("ColorR", out var ocr) ? ocr.GetByte() : (byte)220,
                            ColorG = occMeta.TryGetProperty("ColorG", out var ocg) ? ocg.GetByte() : (byte)220,
                            ColorB = occMeta.TryGetProperty("ColorB", out var ocb) ? ocb.GetByte() : (byte)200,
                            IsVisible = occMeta.TryGetProperty("IsVisible", out var iv) && iv.GetBoolean(),
                            MaxillaOcclusionTransform  = occMeta.TryGetProperty("MaxillaOcclusionTransform",  out var mt) ? ArrayToMatrix(mt) : System.Windows.Media.Media3D.Matrix3D.Identity,
                            MandibleOcclusionTransform = occMeta.TryGetProperty("MandibleOcclusionTransform", out var nd) ? ArrayToMatrix(nd) : System.Windows.Media.Media3D.Matrix3D.Identity,
                        };
                        occVm.OnVisibilityChanged = RefreshCombinedModel;
                        occVm.BuildModel();
                        LoadedOcclusions.Add(occVm);
                    }
                    occIdx++;
                    ReportLoadProgress(75 + (double)occIdx / Math.Max(1, occArr.GetArrayLength()) * 10);
                }
            }

            OcclusionNodeViewModel? activeNode = null;
            OcclusionNodeViewModel? selectedNode = null;
            OcclusionPlanViewModel? selectedPlan = null;
            int activeOcclusionIndex = root.TryGetProperty("ActiveOcclusionIndex", out var activeIndexProp)
                && activeIndexProp.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? activeIndexProp.GetInt32()
                    : -1;

            if (root.TryGetProperty("OcclusionPlanNodes", out var nodeArr))
            {
                int nodeIdx = 0;
                foreach (var nodeMeta in nodeArr.EnumerateArray())
                {
                    int occlusionIndex = nodeMeta.TryGetProperty("OcclusionIndex", out var oi)
                        && oi.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? oi.GetInt32()
                            : nodeIdx;
                    if (occlusionIndex < 0 || occlusionIndex >= LoadedOcclusions.Count)
                    {
                        nodeIdx++;
                        continue;
                    }

                    var node = new OcclusionNodeViewModel
                    {
                        Name = ReadString(nodeMeta, nameof(OcclusionNodeViewModel.Name), $"Occlusion {nodeIdx + 1}"),
                        IsExpanded = ReadBool(nodeMeta, nameof(OcclusionNodeViewModel.IsExpanded), true),
                        Occlusion = LoadedOcclusions[occlusionIndex]
                    };

                    if (nodeMeta.TryGetProperty("Plans", out var plansArr))
                    {
                        int planIdx = 0;
                        foreach (var planMeta in plansArr.EnumerateArray())
                        {
                            var plan = ReadPlan(planMeta, $"Plan {planIdx + 1}");
                            node.Plans.Add(plan);
                            if (plan.IsSelected)
                            {
                                selectedPlan = plan;
                                selectedNode = node;
                            }
                            planIdx++;
                        }
                    }

                    OcclusionNodes.Add(node);
                    if (ReadBool(nodeMeta, nameof(OcclusionNodeViewModel.IsActive)) || occlusionIndex == activeOcclusionIndex)
                        activeNode = node;
                    nodeIdx++;
                }
            }

            if (OcclusionNodes.Count == 0)
            {
                for (int i = 0; i < LoadedOcclusions.Count; i++)
                {
                    var node = new OcclusionNodeViewModel
                    {
                        Name = $"Occlusion {i + 1}",
                        Occlusion = LoadedOcclusions[i],
                        IsExpanded = true
                    };
                    OcclusionNodes.Add(node);
                    if (i == activeOcclusionIndex)
                        activeNode = node;
                }
            }

            RefreshCombinedModel();
            SetActiveOcclusionNode(selectedNode ?? activeNode ?? OcclusionNodes.FirstOrDefault());
            if (selectedPlan != null)
            {
                ApplyPlan(selectedPlan);
            }
            else if (root.TryGetProperty("CurrentSurgeryPlan", out var currentPlanMeta))
            {
                ApplyPlan(ReadPlan(currentPlanMeta, "Current"));
            }

            // 6. (Issue 10) Read cephalometry landmarks into SavedCephLandmarks
            SavedCephLandmarks = new List<CephLandmarkSave>();
            if (root.TryGetProperty("CephLandmarks", out var cephArr))
            {
                foreach (var cl in cephArr.EnumerateArray())
                {
                    string cName = cl.TryGetProperty("Name", out var cn) ? cn.GetString() ?? "" : "";
                    double? x2d = cl.TryGetProperty("X2D", out var x2e) && x2e.ValueKind != System.Text.Json.JsonValueKind.Null ? x2e.GetDouble() : (double?)null;
                    double? y2d = cl.TryGetProperty("Y2D", out var y2e) && y2e.ValueKind != System.Text.Json.JsonValueKind.Null ? y2e.GetDouble() : (double?)null;
                    double? x3d = cl.TryGetProperty("X3D", out var x3e) && x3e.ValueKind != System.Text.Json.JsonValueKind.Null ? x3e.GetDouble() : (double?)null;
                    double? y3d = cl.TryGetProperty("Y3D", out var y3e) && y3e.ValueKind != System.Text.Json.JsonValueKind.Null ? y3e.GetDouble() : (double?)null;
                    double? z3d = cl.TryGetProperty("Z3D", out var z3e) && z3e.ValueKind != System.Text.Json.JsonValueKind.Null ? z3e.GetDouble() : (double?)null;
                    if (cName.Length > 0)
                        SavedCephLandmarks.Add(new CephLandmarkSave(cName, x2d, y2d, x3d, y3d, z3d));
                }
            }

            RefreshCombinedModel();
            OnPropertyChanged(nameof(HasUpperAndLowerScans));
            LoadProgress = 100;
            StatusText = $"Project loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }
}
