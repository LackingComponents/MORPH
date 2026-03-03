using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.Core.Segmentation;
using OrthoPlanner.App.ViewModels.Photogrammetry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
    }

    // ─── Photogrammetry ───
    public PhotogrammetryViewModel PhotogrammetrySpace { get; } = new();
    [ObservableProperty] private bool _isPhotogrammetryOpen;

    [RelayCommand]
    private void OpenPhotogrammetry()
    {
        IsPhotogrammetryOpen = true;
    }

    [RelayCommand]
    private void ClosePhotogrammetry()
    {
        IsPhotogrammetryOpen = false;
    }

    // ─── Volume State ───
    [ObservableProperty] private VolumeData? _volume;
    [ObservableProperty] private bool _isVolumeLoaded;
    [ObservableProperty] private string _statusText = "Ready — Open a DICOM folder to begin";
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;
    private string? _lastDicomPath;

    // ─── Patient Info ───
    [ObservableProperty] private string _patientName = "";
    [ObservableProperty] private string _studyDate = "";
    [ObservableProperty] private string _seriesDescription = "";
    [ObservableProperty] private string _volumeDimensions = "";

    // ─── 2D Slice Indices ───
    [ObservableProperty] private int _totalSlices;
    [ObservableProperty] private int _currentSlice;
    [ObservableProperty] private int _axialIndex;
    [ObservableProperty] private int _coronalIndex;
    [ObservableProperty] private int _sagittalIndex;
    
    // ─── 3D Viewport Anchors ───
    [ObservableProperty] private System.Windows.Media.Media3D.Point3D _modelCenter = new System.Windows.Media.Media3D.Point3D(0, 0, 0);

    [ObservableProperty] private int _axialMax = 1;
    [ObservableProperty] private int _coronalMax = 1;
    [ObservableProperty] private int _sagittalMax = 1;
    
    // Proportional Heights for 1:1 Anatomical Scale in UI Viewports
    [ObservableProperty] private System.Windows.GridLength _axialDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _coronalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _sagittalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);

    // ─── Windowing ───
    [ObservableProperty] private double _windowCenter = 40;
    [ObservableProperty] private double _windowWidth = 2000;

    // ─── 3D Iso Threshold ───
    [ObservableProperty] private double _isoThreshold = 300;
    [ObservableProperty] private double _isoMin = -1024;
    [ObservableProperty] private double _isoMax = 3071;

    // ─── Slice Images ───
    [ObservableProperty] private WriteableBitmap? _axialImage;
    [ObservableProperty] private WriteableBitmap? _coronalImage;
    [ObservableProperty] private WriteableBitmap? _sagittalImage;

    // ─── 3D Model ───
    [ObservableProperty] private Model3DGroup? _boneModel; // The combined 3D rendering scene

    // ─── Named Anatomy ───
    public SegmentViewModel? HardTissueModel { get; private set; }
    public SegmentViewModel? SoftTissueModel { get; private set; }
    public SegmentViewModel? DentalModel { get; private set; }

    // ─── HU Histograms (Independent) ───
    [ObservableProperty] private WriteableBitmap? _boneHistogramImage;
    [ObservableProperty] private WriteableBitmap? _softHistogramImage;
    [ObservableProperty] private WriteableBitmap? _dentalHistogramImage;
    [ObservableProperty] private WriteableBitmap? _customHistogramImage;

    // ─── Segmentation (Independent Thresholds) ───
    private SegmentationVolume? _segVolume;
    [ObservableProperty] private double _boneMinHU = 400;
    [ObservableProperty] private double _boneMaxHU = 3071;
    [ObservableProperty] private bool _showBoneOverlay;

    [ObservableProperty] private double _softMinHU = -300;
    [ObservableProperty] private double _softMaxHU = 3071;
    [ObservableProperty] private bool _showSoftOverlay;

    [ObservableProperty] private double _dentalMinHU = 2000;
    [ObservableProperty] private double _dentalMaxHU = 3071;
    [ObservableProperty] private bool _showDentalOverlay;

    [ObservableProperty] private double _customMinHU = 200;
    [ObservableProperty] private double _customMaxHU = 3071;
    [ObservableProperty] private bool _showCustomOverlay;

    [ObservableProperty] private bool _showSegmentation;

    // ─── Undo/Redo Stacks ───
    private readonly Stack<StateSnapshot> _undoStack = new();
    private readonly Stack<StateSnapshot> _redoStack = new();

    private class StateSnapshot
    {
        public List<SegmentViewModel> Segments { get; init; } = new();
        public List<MeshViewModel> ImportedMeshes { get; init; } = new();
        public SegmentViewModel? HardTissueModel { get; init; }
        public SegmentViewModel? SoftTissueModel { get; init; }
        public SegmentViewModel? DentalModel { get; init; }
    }

    // ─── Region Growing ───
    [ObservableProperty] private bool _isRegionGrowMode;
    [ObservableProperty] private short _regionGrowTolerance = 500; // Generous guiding mask tolerance
    [ObservableProperty] private double _splitterMinHU = 200; // Step 2 Strict Bounds
    [ObservableProperty] private double _splitterMaxHU = 3000;
    
    // 0 = Mandible (Red), 1 = Cranium (Blue), 2 = Exclude (deleted)
    [ObservableProperty] private int _activeSeedClass = 0; 
    
    public bool IsMandibleSeed 
    { 
        get => ActiveSeedClass == 0; 
        set { if (value) ActiveSeedClass = 0; OnPropertyChanged(); } 
    }
    public bool IsCraniumSeed 
    { 
        get => ActiveSeedClass == 1; 
        set { if (value) ActiveSeedClass = 1; OnPropertyChanged(); } 
    }
    public bool IsExcludeSeed 
    { 
        get => ActiveSeedClass == 2; 
        set { if (value) ActiveSeedClass = 2; OnPropertyChanged(); } 
    }

    partial void OnActiveSeedClassChanged(int value)
    {
        OnPropertyChanged(nameof(IsMandibleSeed));
        OnPropertyChanged(nameof(IsCraniumSeed));
        OnPropertyChanged(nameof(IsExcludeSeed));
    }

    // ─── NHP Parameters (Live adjusted) ───
    [ObservableProperty] private double _nhpLateral = 0.0;
    [ObservableProperty] private double _nhpAnteroposterior = 0.0;
    [ObservableProperty] private double _nhpVertical = 0.0;
    [ObservableProperty] private double _nhpRoll = 0.0;
    [ObservableProperty] private double _nhpPitch = 0.0;
    [ObservableProperty] private double _nhpYaw = 0.0;

    // ─── NHP Committed State (Baseline) ───
    private double _cLat, _cAnt, _cVert, _cRoll, _cPitch, _cYaw;

    public bool IsNhpDirty => Math.Abs(NhpLateral - _cLat) > 0.01 || 
                              Math.Abs(NhpAnteroposterior - _cAnt) > 0.01 ||
                              Math.Abs(NhpVertical - _cVert) > 0.01 ||
                              Math.Abs(NhpRoll - _cRoll) > 0.01 ||
                              Math.Abs(NhpPitch - _cPitch) > 0.01 ||
                              Math.Abs(NhpYaw - _cYaw) > 0.01;

    partial void OnNhpLateralChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpAnteroposteriorChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpVerticalChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpRollChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpPitchChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpYawChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }

    [RelayCommand]
    private void AdjustNhp(string param)
    {
        double step = 0.1;
        if (param.Contains("Lat")) NhpLateral += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Ant")) NhpAnteroposterior += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Vert")) NhpVertical += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Roll")) NhpRoll += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Pitch")) NhpPitch += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Yaw")) NhpYaw += param.EndsWith("+") ? step : -step;
    }

    [RelayCommand]
    private async Task CommitNhpAsync()
    {
        // Baseline matches current
        _cLat = NhpLateral; _cAnt = NhpAnteroposterior; _cVert = NhpVertical;
        _cRoll = NhpRoll; _cPitch = NhpPitch; _cYaw = NhpYaw;
        OnPropertyChanged(nameof(IsNhpDirty));

        // Start Reslice Engine
        await PerformPhysicalResliceAsync();
    }

    private void UpdateNhpTransform()
    {
        if (BoneModel == null) return;

        // Calculate centroid once to pivot around
        var bounds = BoneModel.Bounds;
        if (bounds.IsEmpty) return;
        var center = new Point3D(bounds.X + bounds.SizeX/2, bounds.Y + bounds.SizeY/2, bounds.Z + bounds.SizeZ/2);

        // DELTA MATH: Only visually rotate/translate by the *difference* between the current UI values
        // and the physically baked (committed) values. This prevents compounding geometry.
        var dPitch = NhpPitch - _cPitch;
        var dRoll = NhpRoll - _cRoll;
        var dYaw = NhpYaw - _cYaw;
        var dLat = NhpLateral - _cLat;
        var dAnt = NhpAnteroposterior - _cAnt;
        var dVert = NhpVertical - _cVert;

        var group = new Transform3DGroup();
        // Translate to local pivot
        group.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        
        // Rotate
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        group.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));

        // Translate back + User Translation
        group.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));

        BoneModel.Transform = group;
        
        // Dynamically enforce the freehand rotation pivot point!
        ModelCenter = group.Transform(center);
    }

    public ObservableCollection<(int X, int Y, int Z, byte ClassLabel)> MultiSeeds { get; } = new();

    [RelayCommand]
    private void ClearSeeds()
    {
        MultiSeeds.Clear();
        StatusText = "Seeds cleared.";
    }

    // ─── Viewport toggles ───
    [ObservableProperty] private bool _isOrthographic;
    [ObservableProperty] private bool _showGrid;

    // ─── MPR toggles ───
    [ObservableProperty] private bool _showCrosshairs = true;
    [ObservableProperty] private int _enlargedView; // 0=none, 1=axial, 2=coronal, 3=sagittal
    public ObservableCollection<SegmentViewModel> Segments { get; } = new();

    // ─── Imported Meshes ───
    public ObservableCollection<MeshViewModel> ImportedMeshes { get; } = new();

    partial void OnAxialIndexChanged(int value) => UpdateAxialSlice();
    partial void OnCoronalIndexChanged(int value) => UpdateCoronalSlice();
    partial void OnSagittalIndexChanged(int value) => UpdateSagittalSlice();
    partial void OnWindowCenterChanged(double value) => UpdateAllSlices();
    partial void OnWindowWidthChanged(double value) => UpdateAllSlices();

    partial void OnIsoThresholdChanged(double value)
    {
        // Removed Base CT thresholding
    }

    partial void OnBoneMinHUChanged(double value) { UpdateHistograms(); if (ShowBoneOverlay) UpdateAllSlices(); }
    partial void OnBoneMaxHUChanged(double value) { UpdateHistograms(); if (ShowBoneOverlay) UpdateAllSlices(); }
    partial void OnSoftMinHUChanged(double value) { UpdateHistograms(); if (ShowSoftOverlay) UpdateAllSlices(); }
    partial void OnSoftMaxHUChanged(double value) { UpdateHistograms(); if (ShowSoftOverlay) UpdateAllSlices(); }
    partial void OnDentalMinHUChanged(double value) { UpdateHistograms(); if (ShowDentalOverlay) UpdateAllSlices(); }
    partial void OnDentalMaxHUChanged(double value) { UpdateHistograms(); if (ShowDentalOverlay) UpdateAllSlices(); }
    partial void OnCustomMinHUChanged(double value) { UpdateHistograms(); if (ShowCustomOverlay) UpdateAllSlices(); }
    partial void OnCustomMaxHUChanged(double value) { UpdateHistograms(); if (ShowCustomOverlay) UpdateAllSlices(); }

    partial void OnShowBoneOverlayChanged(bool value)
    {
        if (value) { ShowSoftOverlay = false; ShowDentalOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
    }

    [ObservableProperty] private bool _enhanceSegmentation = true;

    // ─── Environment Lighting ───
    [ObservableProperty] private byte _frontLightIntensity = 0;
    partial void OnFrontLightIntensityChanged(byte value) => RefreshCombinedModel();

    [ObservableProperty] private double _frontLightZ = 0.0; // Straight frontal
    partial void OnFrontLightZChanged(double value) => RefreshCombinedModel();

    [ObservableProperty] private byte _bottomLightIntensity = 0;
    partial void OnBottomLightIntensityChanged(byte value) => RefreshCombinedModel();

    [ObservableProperty] private byte _leftRightLightIntensity = 0;
    partial void OnLeftRightLightIntensityChanged(byte value) => RefreshCombinedModel();

    [ObservableProperty] private byte _backLightIntensity = 0;
    partial void OnBackLightIntensityChanged(byte value) => RefreshCombinedModel();
    partial void OnShowSoftOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowDentalOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
    }
    partial void OnShowDentalOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowSoftOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
    }
    partial void OnShowCustomOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowSoftOverlay = false; ShowDentalOverlay = false; }
        UpdateAllSlices();
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
            StatusText = "Saving project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            // 1. project.json — metadata
            var meta = new
            {
                Version = "2.0",
                PatientName,
                StudyDate,
                Segmentation = new
                {
                    BoneMinHU, BoneMaxHU,
                    SoftMinHU, SoftMaxHU,
                    DentalMinHU, DentalMaxHU,
                    CustomMinHU, CustomMaxHU,
                    Segments = Segments.Select(s => new { s.Name, s.IsVisible, s.ColorR, s.ColorG, s.ColorB }).ToArray()
                },
                ImportedMeshes = ImportedMeshes.Select(m => new { m.Name, m.IsVisible, m.ColorR, m.ColorG, m.ColorB }).ToArray(),
                Volume = Volume != null ? new { Volume.Width, Volume.Height, Volume.Depth, Volume.Spacing } : null,
                WindowCenter,
                WindowWidth
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
                bw.Write(mesh.Vertices.Count);
                foreach (var v in mesh.Vertices)
                {
                    bw.Write(v[0]); bw.Write(v[1]); bw.Write(v[2]);
                }
            }

            // 4. segments/*.bin — segmented 3D model vertex data
            for (int i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                if (seg.Vertices == null) continue;
                var segEntry = zip.CreateEntry($"segments/{i}_{seg.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var ss = segEntry.Open();
                using var bw2 = new BinaryWriter(ss);
                bw2.Write(seg.Vertices.Count);
                foreach (var v in seg.Vertices)
                {
                    bw2.Write(v[0]); bw2.Write(v[1]); bw2.Write(v[2]);
                }
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
            StudyDate = root.GetProperty("StudyDate").GetString() ?? "";
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
                    UpdateHistograms();
                    UpdateAllSlices();
                }
            }

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
                    var vertices = new List<float[]>(count);
                    for (int i = 0; i < count; i++)
                        vertices.Add(new[] { br.ReadSingle(), br.ReadSingle(), br.ReadSingle() });

                    var meshVm = new MeshViewModel
                    {
                        Name = name,
                        Vertices = vertices,
                        ColorR = meshMeta.TryGetProperty("ColorR", out var cr) ? cr.GetByte() : (byte)245,
                        ColorG = meshMeta.TryGetProperty("ColorG", out var cg) ? cg.GetByte() : (byte)245,
                        ColorB = meshMeta.TryGetProperty("ColorB", out var cb) ? cb.GetByte() : (byte)230,
                        IsVisible = meshMeta.GetProperty("IsVisible").GetBoolean()
                    };
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
                        var verts = new List<float[]>(cnt);
                        for (int i = 0; i < cnt; i++)
                            verts.Add(new[] { br2.ReadSingle(), br2.ReadSingle(), br2.ReadSingle() });

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
                }
            }

            RefreshCombinedModel();
            StatusText = $"Project loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task OpenDicomFolderAsync()
    {
        if (IsVolumeLoaded)
        {
            var res = System.Windows.MessageBox.Show(
                "A project is already open. Do you want to save it before starting a new session?",
                "Save Current Project?", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
            
            if (res == System.Windows.MessageBoxResult.Cancel) return;
            if (res == System.Windows.MessageBoxResult.Yes) SaveProject();
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select DICOM / CBCT Folder"
        };

        if (dialog.ShowDialog() != true) return;

        await LoadDicomAsync(dialog.FolderName);
    }

    private async Task LoadDicomAsync(string folderPath)
    {
        try
        {
            IsLoading = true;
            _lastDicomPath = folderPath;
            StatusText = "Scanning DICOM folder...";
            LoadProgress = 0;

            // Reset existing state
            Segments.Clear();
            ImportedMeshes.Clear();
            _segVolume = null;
            BoneModel = null;

            var seriesList = await Task.Run(() =>
                DicomLoader.ScanFolderAsync(folderPath, p =>
                    Application.Current.Dispatcher.Invoke(() => LoadProgress = p * 40)));

            if (seriesList.Count == 0)
            {
                StatusText = "No valid DICOM series found.";
                IsLoading = false;
                return;
            }

            // Always show the selector dialog to confirm Patient details and Preview
            var selectorVm = new DicomSelectorViewModel(seriesList);
            var dialog = new OrthoPlanner.App.Views.DicomSelectorWindow(selectorVm)
            {
                Owner = Application.Current.MainWindow
            };
            
            dialog.ShowDialog();

            if (!selectorVm.Accepted || selectorVm.SelectedSeries == null)
            {
                StatusText = "Load cancelled.";
                IsLoading = false;
                return;
            }

            StatusText = $"Loading series ({selectorVm.SelectedSeries.Info.ImageCount} slices)...";

            Volume = await Task.Run(() =>
                DicomLoader.LoadSeriesAsync(selectorVm.SelectedSeries.Info.FilePaths, p =>
                    Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 60)));

            // Update UI state
            PatientName = Volume.PatientName;
            StudyDate = Volume.StudyDate;
            SeriesDescription = Volume.SeriesDescription;
            VolumeDimensions = $"{Volume.Width} × {Volume.Height} × {Volume.Depth}";

            if (Volume == null) return;
        
            IsLoading = true;
            StatusText = "Drawing Projections...";
            
            AxialMax = Volume.Depth - 1;
            CoronalMax = Volume.Height - 1;
            SagittalMax = Volume.Width - 1;

            AxialIndex = Volume.Depth / 2;
            CoronalIndex = Volume.Height / 2;
            SagittalIndex = Volume.Width / 2;
            
            // Push the physical aspect ratios to the Grid Rows so the UI enforces 1:1 squares visually
            // Because the Grid widths are uniform "*", we scale height directly mapping to Voxel spread.
            AxialDisplayHeight = new System.Windows.GridLength(Volume.Height * Volume.Spacing[1], System.Windows.GridUnitType.Star);
            CoronalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);

            IsoMin = -1000; // Always start exactly at -1000 (air) for predictable UI
            IsoMax = Volume.MaxValue;

            IsVolumeLoaded = true;
            UpdateAllSlices();
            UpdateHistograms();

            StatusText = $"Loaded: {Volume.PatientName} — {Volume.Depth} slices";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }

    private void UpdateAllSlices()
    {
        UpdateAxialSlice();
        UpdateCoronalSlice();
        UpdateSagittalSlice();
    }

    private bool GetActiveThreshold(out double min, out double max)
    {
        if (ShowBoneOverlay) { min = BoneMinHU; max = BoneMaxHU; return true; }
        if (ShowSoftOverlay) { min = SoftMinHU; max = SoftMaxHU; return true; }
        if (ShowDentalOverlay) { min = DentalMinHU; max = DentalMaxHU; return true; }
        if (ShowCustomOverlay) { min = CustomMinHU; max = CustomMaxHU; return true; }
        min = 0; max = 0;
        return false;
    }

    private void UpdateAxialSlice()
    {
        if (Volume == null) return;
        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetAxialSliceWithMaskBgra(AxialIndex, WindowCenter, WindowWidth, _segVolume);
            AxialImage = CreateBgraBitmap(data, Volume.Width, Volume.Height,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetAxialSliceBgra(AxialIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            AxialImage = CreateBgraBitmap(data, Volume.Width, Volume.Height,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
        else
        {
            var data = Volume.GetAxialSlice(AxialIndex, WindowCenter, WindowWidth);
            AxialImage = CreateGrayscaleBitmap(data, Volume.Width, Volume.Height,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
    }

    private void UpdateCoronalSlice()
    {
        if (Volume == null) return;
        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetCoronalSliceWithMaskBgra(CoronalIndex, WindowCenter, WindowWidth, _segVolume);
            CoronalImage = CreateBgraBitmap(data, Volume.Width, Volume.Depth,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetCoronalSliceBgra(CoronalIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            CoronalImage = CreateBgraBitmap(data, Volume.Width, Volume.Depth,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetCoronalSlice(CoronalIndex, WindowCenter, WindowWidth);
            CoronalImage = CreateGrayscaleBitmap(data, Volume.Width, Volume.Depth,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
    }

    private void UpdateSagittalSlice()
    {
        if (Volume == null) return;
        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetSagittalSliceWithMaskBgra(SagittalIndex, WindowCenter, WindowWidth, _segVolume);
            SagittalImage = CreateBgraBitmap(data, Volume.Height, Volume.Depth,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetSagittalSliceBgra(SagittalIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            SagittalImage = CreateBgraBitmap(data, Volume.Height, Volume.Depth,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetSagittalSlice(SagittalIndex, WindowCenter, WindowWidth);
            SagittalImage = CreateGrayscaleBitmap(data, Volume.Height, Volume.Depth,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
    }

    private WriteableBitmap CreateGrayscaleBitmap(byte[] pixels, int w, int h,
        double spacingCol, double spacingRow)
    {
        double minSpacing = Math.Min(spacingCol, spacingRow);
        double dpiX = 96.0 * minSpacing / spacingCol;
        double dpiY = 96.0 * minSpacing / spacingRow;
        var bmp = new WriteableBitmap(w, h, dpiX, dpiY, PixelFormats.Gray8, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w, 0);
        return bmp;
    }

    private WriteableBitmap CreateBgraBitmap(byte[] pixels, int w, int h,
        double spacingCol, double spacingRow)
    {
        double minSpacing = Math.Min(spacingCol, spacingRow);
        double dpiX = 96.0 * minSpacing / spacingCol;
        double dpiY = 96.0 * minSpacing / spacingRow;
        var bmp = new WriteableBitmap(w, h, dpiX, dpiY, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        return bmp;
    }

    /// <summary>
    /// Generate 4 separate histogram images showing HU distribution with the respective
    /// segmentation range highlighted in its specific color.
    /// </summary>
    private void UpdateHistograms()
    {
        if (Volume == null || Volume.Histogram.Length == 0) return;

        double range = Volume.MaxValue - Volume.MinValue;
        if (range <= 0) return;

        int localMax = 1;
        int limitAirBin = (int)((-800 - Volume.MinValue) / range * 512);
        limitAirBin = Math.Clamp(limitAirBin, 0, 511);

        for (int i = limitAirBin; i < 512; i++)
            if (Volume.Histogram[i] > localMax) localMax = Volume.Histogram[i];

        BoneHistogramImage = GenerateColoredHistogram(BoneMinHU, BoneMaxHU, localMax, range, 90, 130, 170);
        SoftHistogramImage = GenerateColoredHistogram(SoftMinHU, SoftMaxHU, localMax, range, 90, 130, 170);
        DentalHistogramImage = GenerateColoredHistogram(DentalMinHU, DentalMaxHU, localMax, range, 90, 130, 170);
        CustomHistogramImage = GenerateColoredHistogram(CustomMinHU, CustomMaxHU, localMax, range, 90, 130, 170);
    }

    private WriteableBitmap? GenerateColoredHistogram(double minHU, double maxHU, int localMax, double range, byte r, byte g, byte b)
    {
        if (Volume == null) return null;
        
        int histW = 512; 
        int histH = 80;
        var pixels = new byte[histW * histH * 4];

        double uiRange = IsoMax - IsoMin; // IsoMin is always -1000 here
        if (uiRange <= 0) return null;

        for (int x = 0; x < histW; x++)
        {
            double hu = IsoMin + (x * uiRange / (histW - 1));
            bool inRange = hu >= minHU && hu <= maxHU;
            
            int originalBin = (int)((hu - Volume.MinValue) / range * 511);
            int binVal = 0;
            if (originalBin >= 0 && originalBin < 512)
                binVal = Volume.Histogram[originalBin];

            int barHeight = binVal > 0 
                ? (int)(Math.Log(1 + binVal) / Math.Log(1 + localMax) * (histH - 2))
                : 0;
            if (barHeight > histH - 2) barHeight = histH - 2;

            for (int y = 0; y < histH; y++)
            {
                int row = histH - 1 - y;
                int idx = (row * histW + x) * 4;
                if (y < barHeight)
                {
                    if (inRange)
                    { pixels[idx] = b; pixels[idx+1] = g; pixels[idx+2] = r; pixels[idx+3] = 0xFF; }
                    else
                    { pixels[idx] = (byte)(b/4); pixels[idx+1] = (byte)(g/4); pixels[idx+2] = (byte)(r/4); pixels[idx+3] = 0xFF; }
                }
                else
                { pixels[idx] = 0x14; pixels[idx+1] = 0x10; pixels[idx+2] = 0x0D; pixels[idx+3] = 0xFF; }
            }
        }

        var bmp = new WriteableBitmap(histW, histH, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, histW, histH), pixels, histW * 4, 0);
        return bmp;
    }

    // ═══════════════════════════════════════
    // PHASE 2: SEGMENTATION COMMANDS
    // ═══════════════════════════════════════

    [RelayCommand]
    private async Task RunBoneSegmentAsync() => 
        await RunSegmentInternalAsync("Bone", BoneMinHU, BoneMaxHU, 230, 210, 180, HardTissueModel, enhanceThinBone: EnhanceSegmentation);

    [RelayCommand]
    private async Task RunSoftTissueSegmentAsync() => 
        await RunSegmentInternalAsync("Soft Tissue", SoftMinHU, SoftMaxHU, 210, 150, 150, SoftTissueModel);

    [RelayCommand]
    private async Task RunDentalSegmentAsync() => 
        await RunSegmentInternalAsync("Dental Model", DentalMinHU, DentalMaxHU, 245, 245, 230, DentalModel, applyNoiseRemoval: false);

    [RelayCommand]
    private async Task RunCustomSegmentAsync() => 
        await RunSegmentInternalAsync("Custom Segment", CustomMinHU, CustomMaxHU, 200, 180, 140, null);

    private async Task RunSegmentInternalAsync(
        string name, double minHU, double maxHU, 
        byte r, byte g, byte b, 
        SegmentViewModel? modelToOverwrite, 
        bool applyNoiseRemoval = true,
        bool enhanceThinBone = false)
    {
        if (Volume == null || IsLoading) return;

        if (modelToOverwrite != null)
        {
            var result = System.Windows.MessageBox.Show(
                $"A {name} model already exists. Generating a new one will overwrite it. Continue?",
                "Confirm Overwrite", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            
            if (result != System.Windows.MessageBoxResult.Yes) return;
            
            DeleteSegmentItem(modelToOverwrite);
        }

        SaveStateForUndo();

        if (_segVolume == null)
            _segVolume = new SegmentationVolume(Volume);

        byte label = (byte)(Segments.Count + 1);
        _segVolume.AddSegment(new SegmentInfo
            { Id = label, Name = $"{name} ({minHU:F0} to {maxHU:F0})", ColorR = r, ColorG = g, ColorB = b });

        IsLoading = true;
        StatusText = $"Running {name} segmentation...";
        LoadProgress = 0;
        short min = (short)minHU, max = (short)maxHU;

        await Task.Run(() =>
            SegmentationEngine.ThresholdSegment(Volume, _segVolume, label, min, max, enhanceThinBone,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = p * 40)));

        long count = _segVolume.CountVoxels(label);

        if (count == 0)
        {
            StatusText = $"No voxels found in range {min}–{max} HU";
            IsLoading = false;
            return;
        }

        if (enhanceThinBone)
        {
            StatusText = "Removing thin-bone scatter noise...";
            LoadProgress = 20;
            await Task.Run(() =>
                SegmentationEngine.RemoveSmallComponents(_segVolume, label, 50, 
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 20 + p * 10)));
        }

        // --- NEW: Morphology Pass ---
        StatusText = "Closing structural holes (Morphology)...";
        LoadProgress = 30;
        int morphologyIterations = enhanceThinBone ? 2 : 1;
        
        await Task.Run(() =>
            SegmentationEngine.MorphologicalClosing(_segVolume, label, morphologyIterations, 
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 30 + p * 10)));

        if (applyNoiseRemoval)
        {
            StatusText = "Removing noise (keeping largest component)...";
            LoadProgress = 40;
            await Task.Run(() =>
                SegmentationEngine.KeepLargestComponent(_segVolume, label,
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 10)));
            
            count = _segVolume.CountVoxels(label); // update count after noise removal
            if (count == 0)
            {
                StatusText = $"Model disappeared after noise removal. Try a lower lower threshold.";
                IsLoading = false;
                return;
            }
        }

        StatusText = "Smoothing mask boundaries...";
        LoadProgress = 50;
        await Task.Run(() =>
            SegmentationEngine.SmoothLabelMask(_segVolume, label,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 50 + p * 10)));

        StatusText = $"Generating mesh from {count:N0} voxels...";
        LoadProgress = 50;

        await GenerateSegmentMeshAsync(label);

        StatusText = $"Segmented {count:N0} voxels ({min}–{max} HU)";
        LoadProgress = 100;
        IsLoading = false;
    }

    public async Task AddSeedPointAsync(int x, int y, int z)
    {
        if (Volume == null || !IsRegionGrowMode) return;

        // 0 = Mandible, 1 = Cranium, 2 = Exclude
        byte classLabel = (byte)(ActiveSeedClass + 1); 
        MultiSeeds.Add((x, y, z, classLabel));
        
        StatusText = $"Added seed for Class {classLabel} at ({x}, {y}, {z}). Previewing mask...";
        IsLoading = true;
        LoadProgress = 0;

        try
        {
            if (_segVolume == null)
            {
                _segVolume = new SegmentationVolume(Volume);
                _segVolume.AddSegment(new SegmentInfo { Id = 1, Name = "Mandible (Preview)", ColorR = 255, ColorG = 150, ColorB = 0 }); // Orange
                _segVolume.AddSegment(new SegmentInfo { Id = 2, Name = "Cranium (Preview)", ColorR = 0, ColorG = 100, ColorB = 255 }); // Dark Blue
                _segVolume.AddSegment(new SegmentInfo { Id = 3, Name = "Exclude (Preview)", ColorR = 255, ColorG = 0, ColorB = 0 }); // Red
            }
            else
            {
                _segVolume.ClearAll(); // Clear previous preview
            }

            var engineSeeds = MultiSeeds.Select(s => (s.X, s.Y, s.Z, s.ClassLabel)).ToList();

            short minSeedVal = short.MaxValue, maxSeedVal = short.MinValue;
            foreach (var s in engineSeeds)
            {
                short val = Volume.GetVoxel(s.X, s.Y, s.Z);
                if (val < minSeedVal) minSeedVal = val;
                if (val > maxSeedVal) maxSeedVal = val;
            }

            short genMin = (short)(minSeedVal - RegionGrowTolerance);
            short genMax = (short)(maxSeedVal + RegionGrowTolerance);

            await Task.Run(() =>
                SegmentationEngine.CompetitiveRegionGrow(Volume, _segVolume, engineSeeds, genMin, genMax, null));

            UpdateAllSlices(); // Force MPR to redraw with new alpha-blended segVolume
            StatusText = $"Preview updated for {MultiSeeds.Count} seeds.";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }

    [RelayCommand]
    private async Task ComputeMultiSeedSplitAsync()
    {
        if (Volume == null || MultiSeeds.Count == 0 || IsLoading) return;

        IsLoading = true;
        StatusText = "Step 1/2: Competitive Multi-Source Growth...";
        LoadProgress = 0;

        SaveStateForUndo();

        if (_segVolume == null)
            _segVolume = new SegmentationVolume(Volume);

        // 1. Convert ViewModel UI seeds into Engine seeds
        var engineSeeds = MultiSeeds.Select(s => (s.X, s.Y, s.Z, s.ClassLabel)).ToList();

        // 2. Find the global max/min of all seeds to set the Generous Tolerance Window
        short minSeedVal = short.MaxValue, maxSeedVal = short.MinValue;
        foreach (var s in engineSeeds)
        {
            short val = Volume.GetVoxel(s.X, s.Y, s.Z);
            if (val < minSeedVal) minSeedVal = val;
            if (val > maxSeedVal) maxSeedVal = val;
        }

        short genMin = (short)(minSeedVal - RegionGrowTolerance);
        short genMax = (short)(maxSeedVal + RegionGrowTolerance);

        // 3. Fire the BFS Race!
        await Task.Run(() =>
            SegmentationEngine.CompetitiveRegionGrow(Volume, _segVolume, engineSeeds, genMin, genMax,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = p * 40)));

        // 4. Strict Threshold Cut and Mesh Extraction
        StatusText = $"Step 2/2: Strict Mask Cut [{SplitterMinHU:F0}, {SplitterMaxHU:F0}] HU...";
        short strictMin = (short)SplitterMinHU;
        short strictMax = (short)SplitterMaxHU;

        await Task.Run(() => 
        {
            int total = Volume.Width * Volume.Height * Volume.Depth;
            for (int i = 0; i < total; i++)
            {
                byte label = _segVolume.Labels[i];
                if (label > 0 && label <= 3) // 1=Mand, 2=Cran, 3=Excl
                {
                    short val = Volume.Voxels[i];
                    if (val < strictMin || val > strictMax || label == 3)
                    {
                        // Strip fat, OR totally delete the "Exclude" class
                        _segVolume.Labels[i] = 0; 
                    }
                }
            }
        });

        Application.Current.Dispatcher.Invoke(() => LoadProgress = 50);

        // 5. Generate meshes for Mandible (1) and Cranium (2)
        if (_segVolume.CountVoxels(1) > 0)
        {
            StatusText = "Meshing Mandible...";
            await GenerateSegmentMeshAsync(1, "Isolated Mandible", 255, 150, 0); // Orange
        }

        if (_segVolume.CountVoxels(2) > 0)
        {
            StatusText = "Meshing Cranium...";
            await GenerateSegmentMeshAsync(2, "Isolated Cranium", 0, 100, 255); // Dark Blue
        }

        StatusText = "Multi-Seed Competitive Split Complete.";
        LoadProgress = 100;
        IsLoading = false;
        MultiSeeds.Clear();
    }

    [RelayCommand]
    private async Task SplitComponentsAsync()
    {
        if (Volume == null || _segVolume == null || IsLoading) return;

        IsLoading = true;
        StatusText = "Analyzing connected components...";
        LoadProgress = 0;

        // Only split the primary bone label (1)
        var components = await Task.Run(() =>
            SegmentationEngine.SplitConnectedComponents(_segVolume, 1, 1));

        if (components.Count < 2)
        {
            StatusText = "Only 1 connected region found — cannot split";
            IsLoading = false;
            return;
        }

        // Keep only the 2 largest components (mandible + skull/maxilla), discard small fragments
        var sorted = components.OrderByDescending(c => c.voxelCount).ToList();
        StatusText = $"Found {components.Count} components — keeping top 2...";

        // Remove all small fragment labels
        for (int i = 2; i < sorted.Count; i++)
            _segVolume.ClearLabel(sorted[i].newLabel);

        // Clear old segment ViewModels
        SaveStateForUndo();
        Segments.Clear();

        // Identify mandible vs skull using Z-centroid (mandible has LOWER Z in most CBCT/CT orientations)
        var comp1 = sorted[0];
        var comp2 = sorted[1];

        double z1 = await Task.Run(() => ComputeZCentroid(comp1.newLabel));
        double z2 = await Task.Run(() => ComputeZCentroid(comp2.newLabel));

        byte mandibleLabel, skullLabel;
        if (z1 < z2) { mandibleLabel = comp1.newLabel; skullLabel = comp2.newLabel; }
        else { mandibleLabel = comp2.newLabel; skullLabel = comp1.newLabel; }

        _segVolume.AddSegment(new SegmentInfo
            { Id = skullLabel, Name = "Maxilla / Skull", ColorR = 230, ColorG = 210, ColorB = 180 });
        _segVolume.AddSegment(new SegmentInfo
            { Id = mandibleLabel, Name = "Mandible", ColorR = 190, ColorG = 165, ColorB = 130 });

        StatusText = "Generating meshes...";
        LoadProgress = 50;
        await GenerateSegmentMeshAsync(skullLabel);
        LoadProgress = 75;
        await GenerateSegmentMeshAsync(mandibleLabel);

        StatusText = $"Split: Maxilla ({_segVolume.CountVoxels(skullLabel):N0}) + Mandible ({_segVolume.CountVoxels(mandibleLabel):N0})";
        LoadProgress = 100;
        IsLoading = false;
    }

    private double ComputeZCentroid(byte label)
    {
        if (_segVolume == null) return 0;
        long sumZ = 0, count = 0;
        int w = _segVolume.Width, h = _segVolume.Height;
        for (int z = 0; z < _segVolume.Depth; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (_segVolume.GetLabel(x, y, z) == label)
            { sumZ += z; count++; }
        }
        return count > 0 ? (double)sumZ / count : 0;
    }

    [RelayCommand]
    private async Task ImportStlAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import STL Mesh",
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusText = "Importing STL...";

        var vertices = await Task.Run(() => StlIO.LoadStl(dialog.FileName));

        var meshVm = new MeshViewModel
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            Vertices = vertices,
            ColorR = 245, ColorG = 245, ColorB = 230,
            IsVisible = true
        };
        meshVm.OnVisibilityChanged = RefreshCombinedModel;
        meshVm.BuildModel();
        SaveStateForUndo();
        ImportedMeshes.Add(meshVm);

        RefreshCombinedModel();
        StatusText = $"Imported: {meshVm.Name} ({vertices.Count / 3:N0} triangles)";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ExportStlAsync()
    {
        if (Segments == null || Segments.Count == 0) return;

        var exportWindow = new ExportWindow(Segments)
        {
            Owner = Application.Current.MainWindow
        };

        if (exportWindow.ShowDialog() != true) return;

        var selectedSegments = exportWindow.SelectedSegments;
        if (selectedSegments.Count == 0) return;

        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Export Folder"
        };
        
        if (folderDialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusText = "Exporting selected models...";
        string folderPath = folderDialog.FolderName;
        
        string safePatientName = string.IsNullOrWhiteSpace(PatientName) ? "UnknownPatient" : PatientName.Replace(" ", "").Replace("^", "_");
        string safeDate = string.IsNullOrWhiteSpace(StudyDate) ? "UnknownDate" : StudyDate;

        int exportedCount = 0;
        foreach (var seg in selectedSegments)
        {
            if (seg.Vertices == null) continue;
            
            string safeSegName = string.Join("_", seg.Name.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{safePatientName}_{safeDate}_{safeSegName}.stl";
            string fullPath = Path.Combine(folderPath, fileName);
            
            await Task.Run(() => StlIO.SaveBinaryStl(fullPath, seg.Vertices));
            exportedCount++;
        }

        StatusText = $"Exported {exportedCount} STL models to {folderPath}";
        IsLoading = false;
    }

    private async Task GenerateSegmentMeshAsync(byte label, string? nameOverride = null, byte? r = null, byte? g = null, byte? b = null)
    {
        if (Volume == null || _segVolume == null) return;

        var vol = Volume;
        var segVol = _segVolume;
        // Full resolution for best quality
        int step = 1;

        var vertices = await Task.Run(() =>
            SegmentationEngine.ExtractSegmentMesh(vol, segVol, label, step,
                p => Application.Current.Dispatcher.Invoke(() =>
                    LoadProgress = Math.Min(99, LoadProgress + p * 10))));

        if (vertices.Count < 3) return;

        var info = _segVolume.Segments.GetValueOrDefault(label)
            ?? new SegmentInfo { Id = label, Name = $"Segment {label}" };
            
        string finalName = nameOverride ?? info.Name;
        byte finalR = r ?? info.ColorR;
        byte finalG = g ?? info.ColorG;
        byte finalB = b ?? info.ColorB;

        var segVm = new SegmentViewModel
        {
            Label = label,
            Name = finalName,
            Vertices = vertices,
            ColorR = finalR,
            ColorG = finalG,
            ColorB = finalB,
            IsVisible = true
        };
        segVm.OnVisibilityChanged = RefreshCombinedModel;
        segVm.BuildModel();
        Segments.Add(segVm);

        // Auto-assign to named properties
        if (info.Name == "Bone" || segVm.Name.StartsWith("Bone")) HardTissueModel = segVm;
        else if (info.Name == "Soft Tissue" || segVm.Name.StartsWith("Soft Tissue")) SoftTissueModel = segVm;
        else if (info.Name == "Dental Scan" || segVm.Name.StartsWith("Dental")) DentalModel = segVm;

        RefreshCombinedModel();
    }

    private void RefreshCombinedModel()
    {
        var group = new Model3DGroup();

        // Very low ambient light to barely prevent pitch black shadows, letting Headlamp do the work
        group.Children.Add(new AmbientLight(Color.FromRgb(30, 30, 35)));
        
        // Strong key light directly from the front (patient faces Y = -1, so light shines towards +Y)
        group.Children.Add(new DirectionalLight(Color.FromRgb(FrontLightIntensity, FrontLightIntensity, FrontLightIntensity), new Vector3D(0, 1, FrontLightZ)));
        
        // Moderate fill light from the bottom to illuminate undercuts (looking up)
        group.Children.Add(new DirectionalLight(Color.FromRgb(BottomLightIntensity, BottomLightIntensity, BottomLightIntensity), new Vector3D(0, 0, 1)));
        
        // Weak fill light from the left side
        group.Children.Add(new DirectionalLight(Color.FromRgb(LeftRightLightIntensity, LeftRightLightIntensity, LeftRightLightIntensity), new Vector3D(1, 0, 0)));
        
        // Weak fill light from the right side
        group.Children.Add(new DirectionalLight(Color.FromRgb(LeftRightLightIntensity, LeftRightLightIntensity, LeftRightLightIntensity), new Vector3D(-1, 0, 0)));
        
        // Weak fill light from the back
        group.Children.Add(new DirectionalLight(Color.FromRgb(BackLightIntensity, BackLightIntensity, BackLightIntensity), new Vector3D(0, -1, 0)));

        // Add segment models
        foreach (var seg in Segments.Where(s => s.IsVisible && s.Model3D != null))
            group.Children.Add(seg.Model3D!);

        // Add imported meshes
        foreach (var mesh in ImportedMeshes.Where(m => m.IsVisible && m.Model3D != null))
            group.Children.Add(mesh.Model3D!);

        BoneModel = group;
        
        // Crucial: Always re-apply active NHP transforms and calculate the active Pivot offset!
        UpdateNhpTransform();
    }

    [RelayCommand]
    private void DeleteSegmentItem(SegmentViewModel seg)
    {
        if (seg == null) return;
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete '{seg.Name}'?", "Confirm Delete", 
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        SaveStateForUndo();
        Segments.Remove(seg);
        if (_segVolume != null) _segVolume.ClearLabel(seg.Label);
        if (HardTissueModel == seg) HardTissueModel = null;
        if (SoftTissueModel == seg) SoftTissueModel = null;
        if (DentalModel == seg) DentalModel = null;
        RefreshCombinedModel();
    }

    [RelayCommand]
    private void DeleteImportedMesh(MeshViewModel mesh)
    {
        if (mesh == null) return;
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete imported mesh '{mesh.Name}'?", "Confirm Delete", 
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        SaveStateForUndo();
        ImportedMeshes.Remove(mesh);
        RefreshCombinedModel();
    }

    // ═══════════════════════════════════════
    // UNDO / REDO STATE MANAGEMENT
    // ═══════════════════════════════════════
    private void SaveStateForUndo()
    {
        var snapshot = CreateStateSnapshot();
        _undoStack.Push(snapshot);
        _redoStack.Clear(); // Any new action invalidates redo stack
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_undoStack.Pop());
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_redoStack.Pop());
    }

    private StateSnapshot CreateStateSnapshot()
    {
        return new StateSnapshot
        {
            Segments = Segments.ToList(),
            ImportedMeshes = ImportedMeshes.ToList(),
            HardTissueModel = HardTissueModel,
            SoftTissueModel = SoftTissueModel,
            DentalModel = DentalModel
        };
    }

    private void RestoreStateSnapshot(StateSnapshot snapshot)
    {
        Segments.Clear();
        foreach (var s in snapshot.Segments) Segments.Add(s);

        ImportedMeshes.Clear();
        foreach (var m in snapshot.ImportedMeshes) ImportedMeshes.Add(m);

        HardTissueModel = snapshot.HardTissueModel;
        SoftTissueModel = snapshot.SoftTissueModel;
        DentalModel = snapshot.DentalModel;

        RefreshCombinedModel();
    }

    [RelayCommand]
    private void OpenLightingConfig()
    {
        var window = new LightingWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }

    private async Task PerformPhysicalResliceAsync()
    {
        if (Volume == null || BoneModel == null) return;

        StatusText = "Calculating exact physical volume bounds...";
        IsLoading = true;
        
        // --- 1. Calculate Spatial Centroid Pivot ---
        var bounds = BoneModel.Bounds;
        Point3D center;
        if (!bounds.IsEmpty)
            center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        else
        {
            var dims = Volume.GetPhysicalDimensions();
            center = new Point3D(dims.Width / 2, dims.Height / 2, dims.Depth / 2);
        }

        // --- 2. Build the STRICT INVERSE Transformation Matrix (Target -> Source) ---
        var invGroup = new Transform3DGroup();
        invGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        invGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), NhpPitch)));
        invGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), NhpRoll)));
        invGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), NhpYaw)));
        invGroup.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));
        var invMatrix = invGroup.Value;
        if (invMatrix.HasInverse) invMatrix.Invert();

        var inverseTransform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = invMatrix.M11, M12 = invMatrix.M12, M13 = invMatrix.M13, M14 = invMatrix.M14,
            M21 = invMatrix.M21, M22 = invMatrix.M22, M23 = invMatrix.M23, M24 = invMatrix.M24,
            M31 = invMatrix.M31, M32 = invMatrix.M32, M33 = invMatrix.M33, M34 = invMatrix.M34,
            M41 = invMatrix.OffsetX, M42 = invMatrix.OffsetY, M43 = invMatrix.OffsetZ, M44 = invMatrix.M44
        };
            
        // --- 3. Build the STRICT FORWARD Transformation Matrix (Source -> Target) ---
        var fwdGroup = new Transform3DGroup();
        fwdGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        fwdGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -NhpPitch)));
        fwdGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -NhpRoll)));
        fwdGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), -NhpYaw)));
        fwdGroup.Children.Add(new TranslateTransform3D(center.X - NhpLateral, center.Y - NhpAnteroposterior, center.Z - NhpVertical));
        var fwdMatrix = fwdGroup.Value;
        if (fwdMatrix.HasInverse) fwdMatrix.Invert();
        
        var transform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = fwdMatrix.M11, M12 = fwdMatrix.M12, M13 = fwdMatrix.M13, M14 = fwdMatrix.M14,
            M21 = fwdMatrix.M21, M22 = fwdMatrix.M22, M23 = fwdMatrix.M23, M24 = fwdMatrix.M24,
            M31 = fwdMatrix.M31, M32 = fwdMatrix.M32, M33 = fwdMatrix.M33, M34 = fwdMatrix.M34,
            M41 = fwdMatrix.OffsetX, M42 = fwdMatrix.OffsetY, M43 = fwdMatrix.OffsetZ, M44 = fwdMatrix.M44
        };

        StatusText = "Reslicing volume matrix...";
        
        // Pass both transforms so the Engine can determine exact physical boundaries and pad without waste!
        var resliced = await Task.Run(() => SegmentationEngine.ResliceVolume(Volume, transform, inverseTransform));
        
        IsLoading = false;
        
        // NOTE: We DO NOT zero out NhpLateral, NhpPitch, etc. They must persist in the UI!
        // We also DO NOT overwrite the baseline (_cLat, etc). Keeping the baseline at 0 
        // ensures the 3D model visibly maintains its exact transformation relative to the MPR!

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
            Volume = resliced;
            
            // Re-initialize segmentation volume for the new dimensions (Air Padded array)
            _segVolume = new SegmentationVolume(Volume);

            // INTENTIONALLY NOT REBUILDING THE 3D MODEL
            // The mesh remains strictly untouched so it doesn't snap, scale, or wipe colors.
            // The Delta Transform applied in UpdateNhpTransform() perfectly holds its visual
            // position relative to the new baked Array mathematically.
            
            // Refresh 2D Slices
            AxialMax = Volume.Depth - 1;
            CoronalMax = Volume.Height - 1;
            SagittalMax = Volume.Width - 1;
            
            // Push updated aspect ratios out
            AxialDisplayHeight = new System.Windows.GridLength(Volume.Height * Volume.Spacing[1], System.Windows.GridUnitType.Star);
            CoronalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            
            UpdateAllSlices();
            UpdateHistograms();
            
            StatusText = "NHP Alignment Complete. Model frozen.";
            OnPropertyChanged(nameof(IsNhpDirty));
        });
    }
}

// ─── Helper ViewModels ───

public partial class SegmentViewModel : ObservableObject
{
    [ObservableProperty] private byte _label;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isSelectedForExport = true;
    [ObservableProperty] private byte _colorR = 200, _colorG = 180, _colorB = 140;
    public List<float[]>? Vertices { get; set; }
    public GeometryModel3D? Model3D { get; set; }

    /// <summary>Callback so the parent ViewModel can refresh 3D when visibility toggles.</summary>
    public Action? OnVisibilityChanged { get; set; }

    partial void OnIsVisibleChanged(bool value) => OnVisibilityChanged?.Invoke();

    public void BuildModel()
    {
        if (Vertices == null || Vertices.Count < 3) return;
        Model3D = MeshHelper.BuildModel3D(Vertices, ColorR, ColorG, ColorB);
    }
}

public partial class MeshViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private byte _colorR = 245, _colorG = 245, _colorB = 230;
    public List<float[]>? Vertices { get; set; }
    public GeometryModel3D? Model3D { get; set; }

    public Action? OnVisibilityChanged { get; set; }
    partial void OnIsVisibleChanged(bool value) => OnVisibilityChanged?.Invoke();

    public void BuildModel()
    {
        if (Vertices == null || Vertices.Count < 3) return;
        Model3D = MeshHelper.BuildModel3D(Vertices, ColorR, ColorG, ColorB);
    }
}

internal static class MeshHelper
{
    public static GeometryModel3D BuildModel3D(List<float[]> vertices, byte r, byte g, byte b)
    {
        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection(vertices.Count);
        var indices = new Int32Collection(vertices.Count);

        for (int i = 0; i < vertices.Count; i++)
        {
            positions.Add(new Point3D(vertices[i][0], vertices[i][1], vertices[i][2]));
            indices.Add(i);
        }
        mesh.Positions = positions;
        mesh.TriangleIndices = indices;

        // Compute normals
        var normals = new Vector3DCollection(positions.Count);
        for (int i = 0; i < positions.Count; i++) normals.Add(new Vector3D(0, 0, 0));
        for (int i = 0; i < indices.Count; i += 3)
        {
            var p0 = positions[indices[i]]; var p1 = positions[indices[i + 1]]; var p2 = positions[indices[i + 2]];
            var u = new Vector3D(p1.X - p0.X, p1.Y - p0.Y, p1.Z - p0.Z);
            var v = new Vector3D(p2.X - p0.X, p2.Y - p0.Y, p2.Z - p0.Z);
            var n = Vector3D.CrossProduct(u, v);
            normals[indices[i]] += n; normals[indices[i + 1]] += n; normals[indices[i + 2]] += n;
        }
        for (int i = 0; i < normals.Count; i++) { var n = normals[i]; n.Normalize(); normals[i] = n; }
        mesh.Normals = normals;

        mesh.Freeze();

        // Solid opaque material — no specular to avoid translucent appearance
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();

        var backBrush = new SolidColorBrush(Color.FromRgb((byte)(r * 0.8), (byte)(g * 0.8), (byte)(b * 0.8)));
        backBrush.Freeze();
        var backMaterial = new DiffuseMaterial(backBrush);
        backMaterial.Freeze();

        var geomModel = new GeometryModel3D(mesh, material)
        {
            BackMaterial = backMaterial
        };
        geomModel.Freeze();
        return geomModel;
    }
}
