using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Segmentation;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Segmentation Internal Volumes ÔöÇÔöÇÔöÇ
    private SegmentationVolume? _segVolume;
    private SegmentationVolume? _boneOnlySegVolume; // A pristine backup purely for the Cranium/Mandible split

    // ÔöÇÔöÇÔöÇ Live 3D Preview ÔöÇÔöÇÔöÇ
    [ObservableProperty] private HelixToolkit.SharpDX.Geometry3D? _livePreviewGeometry;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material? _livePreviewMaterial;
    private System.Threading.CancellationTokenSource? _previewDebounceCts;

    private async void TriggerLivePreviewUpdate()
    {
        if (Volume == null || !ShowBoneOverlay) return;

        _previewDebounceCts?.Cancel();
        _previewDebounceCts = new System.Threading.CancellationTokenSource();
        var token = _previewDebounceCts.Token;

        try
        {
            await Task.Delay(150, token); // Debounce trailing edge by 150ms

            if (token.IsCancellationRequested) return;

            short min = (short)BoneMinHU;
            short max = (short)BoneMaxHU;

            var verts = await Task.Run(() =>
                SegmentationEngine.ExtractLivePreviewMesh(Volume, min, max, 1), token);

            if (token.IsCancellationRequested) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var positions = new List<System.Numerics.Vector3>();
                var indices = new List<int>();
                var dict = new Dictionary<(float, float, float), int>();

                // verts is a flat float[] with stride 3 (x,y,z per vertex)
                for (int vi = 0; vi < verts.Length; vi += 3)
                {
                    float vx = verts[vi], vy = verts[vi + 1], vz = verts[vi + 2];
                    var key = (vx, vy, vz);
                    if (!dict.TryGetValue(key, out int idx))
                    {
                        idx = positions.Count;
                        positions.Add(new System.Numerics.Vector3(vx, vy, vz));
                        dict[key] = idx;
                    }
                    indices.Add(idx);
                }

                var builder = new HelixToolkit.Geometry.MeshBuilder(false, false);
                foreach (var p in positions) builder.Positions.Add(p);
                foreach (var i in indices) builder.TriangleIndices.Add(i);

                var mesh = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
                mesh.UpdateNormals();

                LivePreviewGeometry = mesh;

                if (LivePreviewMaterial == null)
                {
                    LivePreviewMaterial = new HelixToolkit.Wpf.SharpDX.PhongMaterial
                    {
                        DiffuseColor = new HelixToolkit.Maths.Color4(230/255f, 210/255f, 180/255f, 1.0f),
                        RenderEnvironmentMap = true
                    };
                }
            });
        }
        catch (TaskCanceledException) { }
    }

    // ÔöÇÔöÇÔöÇ Region Growing ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ Segmentation Flags ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _enhanceSegmentation = true;
    [ObservableProperty] private bool _closeHolesAfterMerge = false;
    [ObservableProperty] private bool _cleanDentalSegmentation = true;

    public ObservableCollection<(int X, int Y, int Z, byte ClassLabel)> MultiSeeds { get; } = new();

    [RelayCommand]
    private void ClearSeeds()
    {
        MultiSeeds.Clear();
        StatusText = "Seeds cleared.";
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // PHASE 2: SEGMENTATION COMMANDS
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    [RelayCommand]
    private async Task RunBoneSegmentAsync() =>
        await RunSegmentInternalAsync("Bone", BoneMinHU, BoneMaxHU, 230, 210, 180, HardTissueModel, enhanceThinBone: EnhanceSegmentation);

    [RelayCommand]
    private async Task RunSoftTissueSegmentAsync() =>
        await RunSegmentInternalAsync("Soft Tissue", SoftMinHU, SoftMaxHU, 210, 150, 150, SoftTissueModel);

    [RelayCommand]
    private async Task RunDentalSegmentAsync() =>
        await RunSegmentInternalAsync("Dental Model", DentalMinHU, DentalMaxHU, 245, 245, 230, DentalModel, applyNoiseRemoval: false, cleanDental: CleanDentalSegmentation);

    [RelayCommand]
    private async Task RunCustomSegmentAsync() =>
        await RunSegmentInternalAsync("Custom Segment", CustomMinHU, CustomMaxHU, 200, 180, 140, null);

    private async Task RunSegmentInternalAsync(
        string name, double minHU, double maxHU,
        byte r, byte g, byte b,
        SegmentViewModel? modelToOverwrite,
        bool applyNoiseRemoval = true,
        bool enhanceThinBone = false,
        int morphologyIterations = 1,
        bool cleanDental = false)
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
            StatusText = $"No voxels found in range {min}ÔÇô{max} HU";
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
            morphologyIterations = 2;
        }

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
        }
        else if (cleanDental)
        {
            StatusText = "Removing 70% of smaller objects (keeping top 30%)...";
            LoadProgress = 40;
            await Task.Run(() =>
                SegmentationEngine.KeepTopPercentageComponents(_segVolume, label, 0.30,
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 10)));

            count = _segVolume.CountVoxels(label); // update count after noise removal
        }

        if (count == 0)
        {
            StatusText = $"Model disappeared after noise removal. Try a lower lower threshold.";
            IsLoading = false;
            return;
        }

        StatusText = "Smoothing mask boundaries...";
        LoadProgress = 50;
        await Task.Run(() =>
            SegmentationEngine.SmoothLabelMask(_segVolume, label,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 50 + p * 10)));

        StatusText = $"Generating mesh from {count:N0} voxels...";
        LoadProgress = 50;

        await GenerateSegmentMeshAsync(label);

        StatusText = $"Segmented {count:N0} voxels ({min}ÔÇô{max} HU)";
        LoadProgress = 100;

        // Isolate the pure bone mask so that subsequent segmentations (e.g., Dental) do not overwrite and destroy it
        if (name.Contains("Bone"))
        {
            _boneOnlySegVolume = new SegmentationVolume(Volume);
            Array.Copy(_segVolume.Labels, _boneOnlySegVolume.Labels, _segVolume.Labels.Length);
        }

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
        if (!HasModelLoaded) return;

        IsLoading = true;
        IsSplitting = true;
        StatusText = "Analyzing connected components...";
        LoadProgress = 0;

        if (_segVolume == null) return;

        // Only split the primary bone label (1)
        var components = await Task.Run(() =>
            SegmentationEngine.SplitConnectedComponents(_segVolume, 1, 1));

        if (components.Count < 2)
        {
            StatusText = "Only 1 connected region found ÔÇö cannot split";
            IsLoading = false;
            return;
        }

        // Keep only the 2 largest components (mandible + skull/maxilla), discard small fragments
        var sorted = components.OrderByDescending(c => c.voxelCount).ToList();
        StatusText = $"Found {components.Count} components ÔÇö keeping top 2...";

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
        IsSplitting = false;
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

        if (vertices.Length < 9) return;

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
}
