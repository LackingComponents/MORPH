using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using OrthoPlanner.App.ViewModels;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Segmentation;

namespace OrthoPlanner.App;

public partial class SeedSplitWindow : Window, INotifyPropertyChanged
{
    private const byte MandibleLabel = 1;
    private const byte CraniumLabel = 2;
    private const byte ExcludeLabel = 3;
    private const byte BoneHqLabel = 10; // isolated label for the high-quality whole-skull bone mask

    private readonly VolumeData _volume;
    private readonly SegmentationVolume _previewSeg;
    private readonly DispatcherTimer _previewDebounce;
    private readonly double _boneSmoothingAmount;
    private readonly int _boneSmoothingPasses;
    private readonly bool _boneKeepAllParts;
    private readonly int _bonePartsKept;
    private readonly bool _enhanceSegmentation;
    private int _previewVersion;
    private byte _activeClassLabel = CraniumLabel;
    private BitmapSource? _coronalImage;
    private SeedRow? _selectedSeed;
    private double _seedTolerance;
    private double _maskMinHu;
    private double _maskMaxHu;
    private int _coronalIndex;
    private double _progress;
    private string _statusText = "Place cranium, mandible, and optional exclude seeds.";
    private string _seedThresholdText = "Seed threshold: add seeds to derive grow window.";
    private bool _isBusy;

    public bool Accepted { get; private set; }
    public List<float[]>? MandibleResult { get; private set; }
    public List<float[]>? CraniumResult { get; private set; }

    /// <summary>
    /// The full high-quality bone mask partitioned into Mandible(1)/Cranium(2), on the volume grid.
    /// Exposed for callers that want the two-label classification (not just the meshes).
    /// </summary>
    public SegmentationVolume? ClassifiedSplit => _classifiedSplit;
    private SegmentationVolume? _classifiedSplit;

    public ObservableCollection<SeedRow> Seeds { get; } = new();

    public BitmapSource? CoronalImage
    {
        get => _coronalImage;
        private set => SetField(ref _coronalImage, value);
    }

    public SeedRow? SelectedSeed
    {
        get => _selectedSeed;
        set => SetField(ref _selectedSeed, value);
    }

    public double SeedTolerance
    {
        get => _seedTolerance;
        set
        {
            if (!SetField(ref _seedTolerance, Math.Clamp(value, 0, 5000))) return;
            SchedulePreview();
        }
    }

    public double MaskMinHu
    {
        get => _maskMinHu;
        set
        {
            double clamped = Math.Clamp(value, -1024, 3071);
            if (!SetField(ref _maskMinHu, Math.Min(clamped, MaskMaxHu))) return;
            SchedulePreview();
        }
    }

    public double MaskMaxHu
    {
        get => _maskMaxHu;
        set
        {
            double clamped = Math.Clamp(value, -1024, 3071);
            if (!SetField(ref _maskMaxHu, Math.Max(clamped, MaskMinHu))) return;
            SchedulePreview();
        }
    }

    public int CoronalIndex
    {
        get => _coronalIndex;
        private set
        {
            if (!SetField(ref _coronalIndex, Math.Clamp(value, 0, CoronalMax))) return;
            RenderCoronalSlice();
        }
    }

    public int CoronalMax => Math.Max(0, _volume.Height - 1);

    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string SeedThresholdText
    {
        get => _seedThresholdText;
        private set => SetField(ref _seedThresholdText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SeedSplitWindow(
        VolumeData volume,
        int initialCoronalIndex,
        double windowCenter,
        double windowWidth,
        double seedTolerance,
        double maskMinHu,
        double maskMaxHu,
        double boneSmoothingAmount,
        int boneSmoothingPasses,
        bool boneKeepAllParts,
        int bonePartsKept,
        bool enhanceSegmentation)
    {
        InitializeComponent();
        DataContext = this;

        _volume = volume;
        _previewSeg = CreateSegmentationVolume(_volume);
        _windowCenter = windowCenter;
        _windowWidth = windowWidth;
        _seedTolerance = seedTolerance;
        _maskMinHu = maskMinHu;
        _maskMaxHu = maskMaxHu;
        _boneSmoothingAmount = boneSmoothingAmount;
        _boneSmoothingPasses = boneSmoothingPasses;
        _boneKeepAllParts = boneKeepAllParts;
        _bonePartsKept = bonePartsKept;
        _enhanceSegmentation = enhanceSegmentation;
        _coronalIndex = Math.Clamp(initialCoronalIndex, 0, CoronalMax);

        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _previewDebounce.Tick += async (_, _) =>
        {
            _previewDebounce.Stop();
            await RecomputePreviewAsync();
        };

        RenderCoronalSlice();
    }

    private readonly double _windowCenter;
    private readonly double _windowWidth;

    private static SegmentationVolume CreateSegmentationVolume(VolumeData volume)
    {
        var seg = new SegmentationVolume(volume);
        seg.AddSegment(new SegmentInfo { Id = MandibleLabel, Name = "Mandible", ColorR = 255, ColorG = 150, ColorB = 0 });
        seg.AddSegment(new SegmentInfo { Id = CraniumLabel, Name = "Cranium", ColorR = 70, ColorG = 120, ColorB = 255 });
        seg.AddSegment(new SegmentInfo { Id = ExcludeLabel, Name = "Exclude", ColorR = 255, ColorG = 40, ColorB = 40 });
        return seg;
    }

    private SegmentationVolume CreateSegmentationVolume() => CreateSegmentationVolume(_volume);

    private void CoronalHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        CoronalIndex += e.Delta > 0 ? 1 : -1;
        e.Handled = true;
    }

    private void CoronalHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isBusy || !TryMapClickToVoxel(e.GetPosition(CoronalHost), out int x, out int y, out int z)) return;

        short hu = _volume.GetVoxel(x, y, z);
        var row = new SeedRow(Seeds.Count + 1, _activeClassLabel, x, y, z, hu);
        Seeds.Add(row);
        SelectedSeed = row;
        StatusText = $"Added {row.ClassName} seed at ({x}, {y}, {z}), HU {hu}.";
        SchedulePreview(immediate: true);
        e.Handled = true;
    }

    private bool TryMapClickToVoxel(Point pos, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (CoronalImageView.ActualWidth <= 0 || CoronalImageView.ActualHeight <= 0) return false;

        double imageW = CoronalImageView.ActualWidth;
        double imageH = CoronalImageView.ActualHeight;
        double hostW = CoronalHost.ActualWidth;
        double hostH = CoronalHost.ActualHeight;
        double left = (hostW - imageW) / 2.0;
        double top = (hostH - imageH) / 2.0;

        double ix = pos.X - left;
        double iy = pos.Y - top;
        if (ix < 0 || iy < 0 || ix > imageW || iy > imageH) return false;

        double rx = Math.Clamp(ix / imageW, 0, 1);
        double ry = Math.Clamp(iy / imageH, 0, 1);

        x = Math.Clamp((int)Math.Round(rx * (_volume.Width - 1)), 0, _volume.Width - 1);
        y = CoronalIndex;
        z = Math.Clamp(_volume.Depth - 1 - (int)Math.Round(ry * (_volume.Depth - 1)), 0, _volume.Depth - 1);
        return true;
    }

    private void SeedClass_Checked(object sender, RoutedEventArgs e)
    {
        _activeClassLabel = sender switch
        {
            RadioButton rb when rb == MandibleRadio => MandibleLabel,
            RadioButton rb when rb == ExcludeRadio => ExcludeLabel,
            _ => CraniumLabel
        };
    }

    private void DeleteSeed_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSeed == null) return;
        Seeds.Remove(SelectedSeed);
        RenumberSeeds();
        SelectedSeed = Seeds.Count > 0 ? Seeds[^1] : null;
        StatusText = "Selected seed deleted.";
        SchedulePreview(immediate: true);
    }

    private void ClearSeeds_Click(object sender, RoutedEventArgs e)
    {
        Seeds.Clear();
        SelectedSeed = null;
        _previewSeg.ClearAll();
        SeedThresholdText = "Seed threshold: add seeds to derive grow window.";
        StatusText = "Seeds cleared.";
        RenderCoronalSlice();
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        await RecomputePreviewAsync();
    }

    private async void Compute_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSeeds()) return;
        SetBusy(true, "Computing final split...");
        try
        {
            // 1. Seed partition (tolerance grow + strict cut). This is the low-resolution
            //    preview classification — used ONLY as a source of confidently-labeled seeds.
            StatusText = "Step 1/4: Competitive seed partition...";
            SegmentationVolume? partition = await BuildMaskAsync(p => Dispatcher.Invoke(() => Progress = p * 20));
            Progress = 20;

            // 2. High-quality whole-skull bone mask — identical pipeline to Compute Bone Model.
            StatusText = "Step 2/4: Building high-quality whole-skull bone mask...";
            SegmentationVolume? boneHq = await BuildBoneHqMaskAsync(p => Dispatcher.Invoke(() => Progress = 20 + p * 35));
            Progress = 55;

            if (boneHq.CountVoxels(BoneHqLabel) == 0)
            {
                StatusText = "Bone mask is empty for the current HU bounds. Adjust thresholds.";
                AcceptBtn.IsEnabled = false;
                return;
            }

            // 3. Propagate the partition through the FULL bone mask: project the partition labels
            //    onto boneHq, then competitively grow them through bone connectivity, and finally
            //    sweep up any disconnected bone islands by nearest label. Every bone voxel of the
            //    whole "Bone" model ends up labeled Mandible(1) or Cranium(2) — no holes, no loss.
            StatusText = "Step 3/4: Propagating split through high-quality bone mask...";
            var partitionRef = partition;
            var boneHqRef = boneHq;
            SegmentationVolume split = await Task.Run(() =>
            {
                var s = CreateSegmentationVolume();

                // Accepted seeds = partition voxels (1/2) that land on the HQ bone mask.
                // Partition labels falling outside the bone mask are dropped as preview noise.
                for (int i = 0; i < s.Labels.Length; i++)
                {
                    byte pl = partitionRef.Labels[i];
                    if ((pl == MandibleLabel || pl == CraniumLabel) && boneHqRef.Labels[i] == BoneHqLabel)
                        s.Labels[i] = pl;
                }

                SegmentationEngine.CompetitiveGrowLabelsWithinMask(boneHqRef, BoneHqLabel, s,
                    p => Dispatcher.Invoke(() => Progress = 55 + p * 12));
                SegmentationEngine.FillNearestLabelWithinMask(boneHqRef, BoneHqLabel, s,
                    p => Dispatcher.Invoke(() => Progress = 67 + p * 3));
                return s;
            });
            Progress = 70;

            _classifiedSplit = split;

            // Release the large intermediate volumes before the meshing allocations.
            partition = null;
            boneHq = null;

            // 4. Mesh both labels with the SAME extraction path/smoothing as the whole Bone model.
            StatusText = "Step 4/4: Meshing mandible...";
            var mandible = await Task.Run(() => SegmentationEngine.ExtractSegmentMesh(_volume, split, MandibleLabel, 1,
                p => Dispatcher.Invoke(() => Progress = 70 + p * 14),
                _boneSmoothingAmount, _boneSmoothingPasses));

            StatusText = "Step 4/4: Meshing cranium...";
            var cranium = await Task.Run(() => SegmentationEngine.ExtractSegmentMesh(_volume, split, CraniumLabel, 1,
                p => Dispatcher.Invoke(() => Progress = 84 + p * 14),
                _boneSmoothingAmount, _boneSmoothingPasses));

            MandibleResult = mandible.Length >= 9 ? MeshHelper.ToVertexList(mandible) : null;
            CraniumResult = cranium.Length >= 9 ? MeshHelper.ToVertexList(cranium) : null;

            if (MandibleResult == null && CraniumResult == null)
            {
                StatusText = "No meshes were produced. Adjust seeds or thresholds.";
                AcceptBtn.IsEnabled = false;
                return;
            }

            Progress = 100;
            AcceptBtn.IsEnabled = true;
            StatusText = $"Split ready. Mandible tris: {(MandibleResult?.Count ?? 0) / 3:N0}; Cranium tris: {(CraniumResult?.Count ?? 0) / 3:N0}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Split failed: {ex.Message}";
            MessageBox.Show($"Seed split failed:\n{ex.Message}", "Seed Split", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Builds the high-quality whole-skull bone mask using the SAME pipeline as
    /// MainViewModel.RunBoneSegmentAsync / RunSegmentInternalAsync("Bone", ...):
    /// threshold (+ thin-bone enhancement) → small-component removal → morphological closing →
    /// largest-component(s) keeping → majority-vote smoothing. The result lives on the same grid
    /// as the volume and is labeled <see cref="BoneHqLabel"/>.
    /// </summary>
    private async Task<SegmentationVolume> BuildBoneHqMaskAsync(Action<double>? progress = null)
    {
        var seg = new SegmentationVolume(_volume);
        seg.AddSegment(new SegmentInfo { Id = BoneHqLabel, Name = "Bone (HQ)", ColorR = 230, ColorG = 210, ColorB = 180 });

        short min = (short)Math.Clamp(Math.Min(MaskMinHu, MaskMaxHu), short.MinValue, short.MaxValue);
        short max = (short)Math.Clamp(Math.Max(MaskMinHu, MaskMaxHu), short.MinValue, short.MaxValue);

        await Task.Run(() =>
        {
            SegmentationEngine.ThresholdSegment(_volume, seg, BoneHqLabel, min, max, _enhanceSegmentation,
                p => progress?.Invoke(p * 0.45));

            int morphologyIterations = 1;
            if (_enhanceSegmentation)
            {
                SegmentationEngine.RemoveSmallComponents(seg, BoneHqLabel, 50,
                    p => progress?.Invoke(0.45 + p * 0.10));
                morphologyIterations = 2;
            }

            SegmentationEngine.MorphologicalClosing(seg, BoneHqLabel, morphologyIterations,
                p => progress?.Invoke(0.55 + p * 0.15));

            if (!_boneKeepAllParts)
            {
                if (_bonePartsKept > 0)
                    SegmentationEngine.KeepLargestComponents(seg, BoneHqLabel, _bonePartsKept,
                        p => progress?.Invoke(0.70 + p * 0.10));
                else
                    SegmentationEngine.KeepLargestComponent(seg, BoneHqLabel,
                        p => progress?.Invoke(0.70 + p * 0.10));
            }

            SegmentationEngine.SmoothLabelMask(seg, BoneHqLabel,
                p => progress?.Invoke(0.80 + p * 0.20));
        });

        return seg;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (MandibleResult == null && CraniumResult == null) return;
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }

    private bool ValidateSeeds()
    {
        bool hasMandible = Seeds.Any(s => s.ClassLabel == MandibleLabel);
        bool hasCranium = Seeds.Any(s => s.ClassLabel == CraniumLabel);
        if (hasMandible && hasCranium) return true;

        MessageBox.Show("Place at least one Cranium seed and one Mandible seed before computing.",
            "Seed Split", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void SchedulePreview(bool immediate = false)
    {
        MandibleResult = null;
        CraniumResult = null;
        AcceptBtn.IsEnabled = false;
        _previewDebounce.Stop();

        if (immediate)
        {
            _ = RecomputePreviewAsync();
        }
        else
        {
            _previewDebounce.Start();
        }
    }

    private async Task RecomputePreviewAsync()
    {
        int version = ++_previewVersion;
        if (Seeds.Count == 0)
        {
            _previewSeg.ClearAll();
            RenderCoronalSlice();
            return;
        }

        SetBusy(true, "Updating live mask preview...");
        try
        {
            var preview = await BuildMaskAsync(p => Dispatcher.Invoke(() => Progress = p * 100));
            if (version != _previewVersion) return;

            Array.Copy(preview.Labels, _previewSeg.Labels, _previewSeg.Labels.Length);
            Progress = 100;
            StatusText = $"Preview updated for {Seeds.Count} seed(s).";
            RenderCoronalSlice();
        }
        catch (Exception ex)
        {
            StatusText = $"Preview failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<SegmentationVolume> BuildMaskAsync(Action<double>? progress = null)
    {
        var seg = CreateSegmentationVolume();
        if (Seeds.Count == 0) return seg;

        var engineSeeds = Seeds.Select(s => (s.X, s.Y, s.Z, s.ClassLabel)).ToList();
        short minSeedVal = Seeds.Min(s => s.Hu);
        short maxSeedVal = Seeds.Max(s => s.Hu);
        short genMin = (short)Math.Clamp(minSeedVal - SeedTolerance, short.MinValue, short.MaxValue);
        short genMax = (short)Math.Clamp(maxSeedVal + SeedTolerance, short.MinValue, short.MaxValue);
        SeedThresholdText = $"Seed threshold: {genMin} to {genMax} HU (seed range {minSeedVal} to {maxSeedVal})";

        await Task.Run(() => SegmentationEngine.CompetitiveRegionGrow(_volume, seg, engineSeeds, genMin, genMax, progress));
        ApplyStrictMask(seg);
        return seg;
    }

    private void ApplyStrictMask(SegmentationVolume seg)
    {
        short strictMin = (short)Math.Clamp(Math.Min(MaskMinHu, MaskMaxHu), short.MinValue, short.MaxValue);
        short strictMax = (short)Math.Clamp(Math.Max(MaskMinHu, MaskMaxHu), short.MinValue, short.MaxValue);

        for (int i = 0; i < seg.Labels.Length; i++)
        {
            byte label = seg.Labels[i];
            if (label == 0) continue;
            short hu = _volume.Voxels[i];
            if (label == ExcludeLabel || hu < strictMin || hu > strictMax)
                seg.Labels[i] = 0;
        }
    }

    private void RenderCoronalSlice()
    {
        byte[] data = Seeds.Count > 0
            ? _volume.GetCoronalSliceWithMaskBgra(CoronalIndex, _windowCenter, _windowWidth, _previewSeg)
            : _volume.GetCoronalSliceBgra(CoronalIndex, _windowCenter, _windowWidth, (short)MaskMinHu, (short)MaskMaxHu);

        CoronalImage = BitmapSource.Create(
            _volume.Width, _volume.Depth,
            96.0 * _volume.Spacing[0], 96.0 * _volume.Spacing[2],
            PixelFormats.Bgra32, null, data, _volume.Width * 4);

        RenderSeedMarkers();
    }

    private void CoronalHost_SizeChanged(object sender, SizeChangedEventArgs e) => RenderSeedMarkers();

    private void RenderSeedMarkers()
    {
        if (SeedOverlay == null || CoronalImageView.ActualWidth <= 0 || CoronalImageView.ActualHeight <= 0) return;
        SeedOverlay.Children.Clear();

        double imageW = CoronalImageView.ActualWidth;
        double imageH = CoronalImageView.ActualHeight;
        double left = (CoronalHost.ActualWidth - imageW) / 2.0;
        double top = (CoronalHost.ActualHeight - imageH) / 2.0;

        foreach (var seed in Seeds.Where(s => s.Y == CoronalIndex))
        {
            double px = left + (seed.X / Math.Max(1.0, _volume.Width - 1.0)) * imageW;
            double py = top + ((_volume.Depth - 1.0 - seed.Z) / Math.Max(1.0, _volume.Depth - 1.0)) * imageH;
            var marker = new Ellipse
            {
                Width = 12,
                Height = 12,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(seed.DisplayColor),
                Opacity = 0.95
            };
            Canvas.SetLeft(marker, px - 6);
            Canvas.SetTop(marker, py - 6);
            SeedOverlay.Children.Add(marker);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        ComputeBtn.IsEnabled = !busy;
        if (message != null) StatusText = message;
        if (busy) Progress = 0;
    }

    private void RenumberSeeds()
    {
        for (int i = 0; i < Seeds.Count; i++)
            Seeds[i].Index = i + 1;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public sealed class SeedRow : INotifyPropertyChanged
    {
        private int _index;

        public SeedRow(int index, byte classLabel, int x, int y, int z, short hu)
        {
            _index = index;
            ClassLabel = classLabel;
            X = x;
            Y = y;
            Z = z;
            Hu = hu;
        }

        public int Index
        {
            get => _index;
            set
            {
                if (_index == value) return;
                _index = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Index)));
            }
        }

        public byte ClassLabel { get; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public short Hu { get; }
        public string Coordinates => $"{X}, {Y}, {Z}";
        public string ClassName => ClassLabel switch
        {
            MandibleLabel => "Mandible",
            CraniumLabel => "Cranium",
            ExcludeLabel => "Exclude",
            _ => $"Class {ClassLabel}"
        };

        public Color DisplayColor => ClassLabel switch
        {
            MandibleLabel => Color.FromRgb(255, 150, 0),
            CraniumLabel => Color.FromRgb(70, 120, 255),
            ExcludeLabel => Color.FromRgb(255, 40, 40),
            _ => Colors.White
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
