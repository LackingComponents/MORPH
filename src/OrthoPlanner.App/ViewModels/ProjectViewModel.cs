using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
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
            StatusText = "Saving project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            // Helper: Matrix3D → 16-element double array (row-major)
            static double[] MatrixToArray(System.Windows.Media.Media3D.Matrix3D m) =>
                new[] { m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.OffsetX, m.OffsetY, m.OffsetZ, m.M44 };

            // 1. project.json — metadata
            var meta = new
            {
                Version = "2.2",
                PatientName,
                StudyDate,
                Segmentation = new
                {
                    BoneMinHU, BoneMaxHU,
                    SoftMinHU, SoftMaxHU,
                    DentalMinHU, DentalMaxHU,
                    CustomMinHU, CustomMaxHU,
                    Segments = Segments.Select(s => new { s.Name, s.IsVisible, s.ColorR, s.ColorG, s.ColorB,
                        LocalTransformMatrix = MatrixToArray(s.LocalTransform.Value) }).ToArray()
                },
                ImportedMeshes = ImportedMeshes.Select(m => new { m.Name, m.IsVisible, m.ColorR, m.ColorG, m.ColorB,
                    LocalTransformMatrix = MatrixToArray(m.LocalTransform.Value) }).ToArray(),
                // Issue 11: occlusion meshes with their alignment transforms
                OcclusionMeshes = LoadedOcclusions.Select(o => new
                {
                    o.Name, o.IsVisible, o.ColorR, o.ColorG, o.ColorB,
                    MaxillaOcclusionTransform = MatrixToArray(o.MaxillaOcclusionTransform),
                    MandibleOcclusionTransform = MatrixToArray(o.MandibleOcclusionTransform),
                    LocalTransformMatrix = MatrixToArray(o.LocalTransform.Value)
                }).ToArray(),
                Volume = Volume != null ? new { Volume.Width, Volume.Height, Volume.Depth, Volume.Spacing } : null,
                WindowCenter,
                WindowWidth,
                // Issue 10: cephalometry landmarks
                CephLandmarks = SavedCephLandmarks.Select(c => new
                {
                    c.Name, c.X2D, c.Y2D, c.X3D, c.Y3D, c.Z3D
                }).ToArray(),
                // NEW primary: NHP profiles (named checkpoints of the 6 absolute values)
                NhpProfiles = NhpProfiles.Select(p => new
                {
                    p.Name, p.Lateral, p.Anteroposterior, p.Vertical, p.Roll, p.Pitch, p.Yaw,
                    p.IsSelected, p.IsCommitted, p.IsLatest
                }).ToArray(),
                VolumePivot = VolumePivot.HasValue ? new { X = VolumePivot.Value.X, Y = VolumePivot.Value.Y, Z = VolumePivot.Value.Z } : (object?)null,
                // Hybrid NHP: Persist anatomical landmarks (in baked NHP space after first commit)
                CondyleCenters = new
                {
                    Left    = LeftCondyleCenter  == null ? null : (object)new { LeftCondyleCenter.Value.X,  LeftCondyleCenter.Value.Y,  LeftCondyleCenter.Value.Z },
                    Right   = RightCondyleCenter == null ? null : (object)new { RightCondyleCenter.Value.X, RightCondyleCenter.Value.Y, RightCondyleCenter.Value.Z },
                    LeftHalfExtents  = LeftCondyleHalfExtents  == null ? null : (object)new { LeftCondyleHalfExtents.Value.X,  LeftCondyleHalfExtents.Value.Y,  LeftCondyleHalfExtents.Value.Z },
                    RightHalfExtents = RightCondyleHalfExtents == null ? null : (object)new { RightCondyleHalfExtents.Value.X, RightCondyleHalfExtents.Value.Y, RightCondyleHalfExtents.Value.Z },
                    Midline = DentalMidlinePoint == null ? null : (object)new { DentalMidlinePoint.Value.X, DentalMidlinePoint.Value.Y, DentalMidlinePoint.Value.Z }
                },
                // Surgery plan persistence (Lore): save active movements so they survive project round-trip
                CurrentSurgeryPlan = SnapshotCurrentPlan("Current")
            };
            var jsonEntry = zip.CreateEntry("project.json");
            using (var sw = new StreamWriter(jsonEntry.Open()))
            {
                sw.Write(System.Text.Json.JsonSerializer.Serialize(meta,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }

            // 2. volume.bin — raw voxel data
            if (Volume != null)
            {
                var volEntry = zip.CreateEntry("volume.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var volStream = volEntry.Open();
                var bytes = new byte[Volume.Voxels.Length * 2];
                Buffer.BlockCopy(Volume.Voxels, 0, bytes, 0, bytes.Length);
                volStream.Write(bytes, 0, bytes.Length);
            }

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
            }

            StatusText = $"Project saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally { IsLoading = false; }
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
            // Open is committed — tell the view to drop session-bound visuals (measurements).
            ProjectReset?.Invoke();
            IsLoading = true;
            StatusText = "Loading project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

            // 1. Read project.json
            var jsonEntry = zip.GetEntry("project.json");
            if (jsonEntry == null) { StatusText = "Invalid project file"; return; }

            string json;
            using (var sr = new StreamReader(jsonEntry.Open()))
                json = await sr.ReadToEndAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            PatientName = root.GetProperty("PatientName").GetString() ?? "";
            StudyDate = FormatStudyDate(root.GetProperty("StudyDate").GetString() ?? "");
            WindowCenter = root.GetProperty("WindowCenter").GetDouble();
            WindowWidth = root.GetProperty("WindowWidth").GetDouble();
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
                    }
                    Buffer.BlockCopy(bytes, 0, vol.Voxels, 0, bytes.Length);
                    vol.PatientName = PatientName;
                    vol.StudyDate = StudyDate;
                    vol.ComputeMinMax();

                    Volume = vol;
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

                    // Phase 1: Restore baked VolumePivot (stable across reslices)
                    if (root.TryGetProperty("VolumePivot", out var vpNode))
                    {
                        double vpx = vpNode.GetProperty("X").GetDouble();
                        double vpy = vpNode.GetProperty("Y").GetDouble();
                        double vpz = vpNode.GetProperty("Z").GetDouble();
                        VolumePivot = new System.Windows.Media.Media3D.Point3D(vpx, vpy, vpz);
                    }
                    else
                    {
                        // Fallback: compute from loaded volume dimensions
                        VolumePivot = new System.Windows.Media.Media3D.Point3D(
                            vol.Width * vol.Spacing[0] / 2.0,
                            vol.Height * vol.Spacing[1] / 2.0,
                            vol.Depth * vol.Spacing[2] / 2.0);
                    }

                    // NHP profiles gate (spec §6): new-format files persist NhpProfiles; legacy bake-model
                    // files have only NhpBaseline and route to the Task-6 migration shim. NaN/Infinity are
                    // rejected (corrupt .orthoplan guard); values are clamped inside the restore/migrate calls.
                    static double ReadNhpD(System.Text.Json.JsonElement node, string key, double fb = 0)
                    {
                        if (!node.TryGetProperty(key, out var el) || el.ValueKind != System.Text.Json.JsonValueKind.Number) return fb;
                        double v = el.GetDouble();
                        return double.IsNaN(v) || double.IsInfinity(v) ? fb : v;
                    }
                    static bool ReadNhpB(System.Text.Json.JsonElement node, string key)
                        => node.TryGetProperty(key, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.True;
                    static string ReadNhpStr(System.Text.Json.JsonElement node, string key, string def)
                        => node.TryGetProperty(key, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String
                            ? (el.GetString() ?? def) : def;

                    bool hasNewProfiles = root.TryGetProperty("NhpProfiles", out var profilesNode);

                    if (hasNewProfiles)
                    {
                        // NEW format: restore profiles → active six into sliders → recompute LATER (no bake).
                        // RestoreNhpProfilesFromProject seeds the live sliders from the active profile and
                        // recomputes nothing here; the load tail (RefreshCombinedModel once BoneOnlyBounds is
                        // restored) rebuilds _nhpShared from these seeded sliders (INV2, INV4, INV8).
                        var restored = new System.Collections.Generic.List<NhpProfileViewModel>();
                        foreach (var p in profilesNode.EnumerateArray())
                        {
                            var prof = new NhpProfileViewModel { Name = ReadNhpStr(p, "Name", "NHP 1") };
                            prof.Lateral         = ReadNhpD(p, "Lateral");
                            prof.Anteroposterior = ReadNhpD(p, "Anteroposterior");
                            prof.Vertical        = ReadNhpD(p, "Vertical");
                            prof.Roll            = ReadNhpD(p, "Roll");
                            prof.Pitch           = ReadNhpD(p, "Pitch");
                            prof.Yaw             = ReadNhpD(p, "Yaw");
                            prof.IsSelected      = ReadNhpB(p, "IsSelected");
                            prof.IsCommitted     = ReadNhpB(p, "IsCommitted");
                            if (p.TryGetProperty("IsLatest", out var l) && l.ValueKind == System.Text.Json.JsonValueKind.True) prof.IsLatest = true;
                            restored.Add(prof);
                        }
                        RestoreNhpProfilesFromProject(restored);
                    }
                    else
                    {
                        // LEGACY bake-model file → migration shim (Task 6 fills the vertex/landmark un-bake).
                        // ponytail: removable shim once new-format test cases exist (spec §6).
                        var nb = root.TryGetProperty("NhpBaseline", out var bn) ? bn : default;
                        double bl(string k, bool rot) => nb.ValueKind == System.Text.Json.JsonValueKind.Undefined
                            ? 0 : Math.Clamp(ReadNhpD(nb, k), rot ? -45 : -200, rot ? 45 : 200);
                        MigrateBaselineToNhpProfileIfNeeded(
                            bl("Lat", false), bl("Ant", false), bl("Vert", false),
                            bl("Roll", true), bl("Pitch", true), bl("Yaw", true));
                        // Task 6 adds the vertex/landmark un-bake here (legacy vertices/landmarks were
                        // saved already-baked; the lazy model double-poses until the shim un-bakes them).
                    }

                    // Hybrid NHP: Restore anatomical landmarks (in baked NHP space)
                    if (root.TryGetProperty("CondyleCenters", out var ccNode))
                    {
                        static (double X, double Y, double Z)? ReadPoint(System.Text.Json.JsonElement parent, string key)
                        {
                            if (!parent.TryGetProperty(key, out var node)) return null;
                            if (node.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
                            return (node.GetProperty("X").GetDouble(),
                                    node.GetProperty("Y").GetDouble(),
                                    node.GetProperty("Z").GetDouble());
                        }
                        LeftCondyleCenter  = ReadPoint(ccNode, "Left");
                        RightCondyleCenter = ReadPoint(ccNode, "Right");
                        LeftCondyleHalfExtents  = ReadPoint(ccNode, "LeftHalfExtents");
                        RightCondyleHalfExtents = ReadPoint(ccNode, "RightHalfExtents");
                        DentalMidlinePoint = ReadPoint(ccNode, "Midline");
                    }

                    // Re-apply the NHP transform and regenerate MPR slices
                    UpdateNhpTransform();
                    UpdateAllSlices();
                } // end if (volEntry != null)
            } // end if (volMeta != Null)

            // Top helper: 16-element double array -> Matrix3D (row-major). Reused by the mesh/segment/
            // occlusion per-piece LocalTransform restores and the occlusion alignment transforms.
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

            // 3. Read imported meshes — SuppressLedgerBake prevents double-baking restored vertices
            SuppressLedgerBake = true;
            try
            {
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
                        IsVisible = meshMeta.GetProperty("IsVisible").GetBoolean()
                    };
                    meshVm.LocalTransform = meshMeta.TryGetProperty("LocalTransformMatrix", out var mlt)
                        ? new System.Windows.Media.Media3D.MatrixTransform3D(ArrayToMatrix(mlt))
                        : System.Windows.Media.Media3D.Transform3D.Identity;
                    meshVm.OnVisibilityChanged = RefreshCombinedModel;
                    meshVm.BuildModel();
                    ImportedMeshes.Add(meshVm);
                }
                meshIdx++;
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
                        segVm.LocalTransform = segMeta.TryGetProperty("LocalTransformMatrix", out var slt)
                            ? new System.Windows.Media.Media3D.MatrixTransform3D(ArrayToMatrix(slt))
                            : System.Windows.Media.Media3D.Transform3D.Identity;
                        segVm.OnVisibilityChanged = RefreshCombinedModel;
                        segVm.BuildModel();
                        Segments.Add(segVm);

                        // Restore named properties
                        if (sName == "Bone" || sName.StartsWith("Bone")) HardTissueModel = segVm;
                        else if (sName == "Soft Tissue" || sName.StartsWith("Soft Tissue")) SoftTissueModel = segVm;
                        else if (sName == "Dental Scan" || sName.StartsWith("Dental")) DentalModel = segVm;
                    }
                    segIdx++;
                }
            }

            } // end SuppressLedgerBake try
            finally { SuppressLedgerBake = false; }

            // Apply visual delta (Identity at this point since sliders == baseline) and refresh
            UpdateNhpTransform();

            // 5. (Issue 11) Read occlusion meshes + their alignment transforms
            LoadedOcclusions.Clear();
            OcclusionNodes.Clear();
            if (root.TryGetProperty("OcclusionMeshes", out var occArr))
            {
                // ArrayToMatrix hoisted to the top of the load (above the SuppressLedgerBake try).

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
                        occVm.LocalTransform = occMeta.TryGetProperty("LocalTransformMatrix", out var olt)
                            ? new System.Windows.Media.Media3D.MatrixTransform3D(ArrayToMatrix(olt))
                            : System.Windows.Media.Media3D.Transform3D.Identity;
                        occVm.OnVisibilityChanged = RefreshCombinedModel;
                        occVm.BuildModel();
                        LoadedOcclusions.Add(occVm);
                    }
                    occIdx++;
                }
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

            // 7. Restore saved surgery plan (Lore): re-apply active surgical movements
            if (root.TryGetProperty("CurrentSurgeryPlan", out var planMeta))
            {
                static double ReadPlanDouble(System.Text.Json.JsonElement node, string key, double fallback = 0.0)
                {
                    if (!node.TryGetProperty(key, out var prop)) return fallback;
                    return prop.ValueKind == System.Text.Json.JsonValueKind.Number ? prop.GetDouble() : fallback;
                }
                static bool ReadPlanBool(System.Text.Json.JsonElement node, string key, bool fallback = false)
                {
                    if (!node.TryGetProperty(key, out var prop)) return fallback;
                    return prop.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.True => true,
                        System.Text.Json.JsonValueKind.False => false,
                        _ => fallback
                    };
                }
                var plan = new OcclusionPlanViewModel
                {
                    Name = "Current",
                    IsMaxillaBasedSurgery  = ReadPlanBool(planMeta, nameof(OcclusionPlanViewModel.IsMaxillaBasedSurgery), true),
                    IsMandibleBasedSurgery = ReadPlanBool(planMeta, nameof(OcclusionPlanViewModel.IsMandibleBasedSurgery)),
                    IsManualOcclusionSurgery = ReadPlanBool(planMeta, nameof(OcclusionPlanViewModel.IsManualOcclusionSurgery)),
                    IsKeepOcclusionSurgery = ReadPlanBool(planMeta, nameof(OcclusionPlanViewModel.IsKeepOcclusionSurgery)),
                    MaxillaLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaLat)),
                    MaxillaAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaAnt)),
                    MaxillaVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaVert)),
                    MaxillaRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaRoll)),
                    MaxillaPitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaPitch)),
                    MaxillaYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MaxillaYaw)),
                    MandibleLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleLat)),
                    MandibleAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleAnt)),
                    MandibleVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleVert)),
                    MandibleRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleRoll)),
                    MandiblePitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandiblePitch)),
                    MandibleYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.MandibleYaw)),
                    RightRamusLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusLat)),
                    RightRamusAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusAnt)),
                    RightRamusVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusVert)),
                    RightRamusRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusRoll)),
                    RightRamusPitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusPitch)),
                    RightRamusYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.RightRamusYaw)),
                    LeftRamusLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusLat)),
                    LeftRamusAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusAnt)),
                    LeftRamusVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusVert)),
                    LeftRamusRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusRoll)),
                    LeftRamusPitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusPitch)),
                    LeftRamusYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.LeftRamusYaw)),
                    ChinLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinLat)),
                    ChinAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinAnt)),
                    ChinVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinVert)),
                    ChinRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinRoll)),
                    ChinPitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinPitch)),
                    ChinYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.ChinYaw)),
                    SavedMaxillaLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaLat)),
                    SavedMaxillaAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaAnt)),
                    SavedMaxillaVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaVert)),
                    SavedMaxillaRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaRoll)),
                    SavedMaxillaPitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaPitch)),
                    SavedMaxillaYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMaxillaYaw)),
                    SavedMandibleLat   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleLat)),
                    SavedMandibleAnt   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleAnt)),
                    SavedMandibleVert  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleVert)),
                    SavedMandibleRoll  = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleRoll)),
                    SavedMandiblePitch = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandiblePitch)),
                    SavedMandibleYaw   = ReadPlanDouble(planMeta, nameof(OcclusionPlanViewModel.SavedMandibleYaw)),
                };
                ApplyPlan(plan);
            }

            StatusText = $"Project loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)

        {
            StatusText = $"Open failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
