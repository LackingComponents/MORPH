using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Segmentation;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Volume State ÔöÇÔöÇÔöÇ
    [ObservableProperty] private VolumeData? _volume;
    [ObservableProperty] private bool _isVolumeLoaded;
    [ObservableProperty] private VolumeData? _originalVolume;
    private string? _lastDicomPath;

    // ÔöÇÔöÇÔöÇ Patient Info ÔöÇÔöÇÔöÇ
    [ObservableProperty] private string _patientName = "";
    [ObservableProperty] private string _patientDOB = "";
    [ObservableProperty] private string _studyDate = "";
    [ObservableProperty] private string _seriesDescription = "";
    [ObservableProperty] private string _volumeDimensions = "";

    // ÔöÇÔöÇÔöÇ 2D Slice Indices ÔöÇÔöÇÔöÇ
    [ObservableProperty] private int _totalSlices;
    [ObservableProperty] private int _currentSlice;
    [ObservableProperty] private int _axialIndex;
    [ObservableProperty] private int _coronalIndex;
    [ObservableProperty] private int _sagittalIndex;

    [ObservableProperty] private int _axialMax = 1;
    [ObservableProperty] private int _coronalMax = 1;
    [ObservableProperty] private int _sagittalMax = 1;

    // Proportional Heights for 1:1 Anatomical Scale in UI Viewports
    [ObservableProperty] private System.Windows.GridLength _axialDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _coronalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _sagittalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);

    // ÔöÇÔöÇÔöÇ Windowing ÔöÇÔöÇÔöÇ
    [ObservableProperty] private double _windowCenter = 40;
    [ObservableProperty] private double _windowWidth = 2000;

    // ÔöÇÔöÇÔöÇ 3D Iso Threshold ÔöÇÔöÇÔöÇ
    [ObservableProperty] private double _isoThreshold = 300;
    [ObservableProperty] private double _isoMin = -1024;
    [ObservableProperty] private double _isoMax = 3071;

    // ÔöÇÔöÇÔöÇ Slice Images ÔöÇÔöÇÔöÇ
    [ObservableProperty] private WriteableBitmap? _axialImage;
    [ObservableProperty] private WriteableBitmap? _coronalImage;
    [ObservableProperty] private WriteableBitmap? _sagittalImage;

    // ÔöÇÔöÇÔöÇ HU Histograms (Independent) ÔöÇÔöÇÔöÇ
    [ObservableProperty] private WriteableBitmap? _boneHistogramImage;
    [ObservableProperty] private WriteableBitmap? _softHistogramImage;
    [ObservableProperty] private WriteableBitmap? _dentalHistogramImage;
    [ObservableProperty] private WriteableBitmap? _customHistogramImage;

    // ÔöÇÔöÇÔöÇ Segmentation HU Ranges (used to render MPR overlays) ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ Partial handlers: slice index & windowing ÔöÇÔöÇÔöÇ
    partial void OnAxialIndexChanged(int value) => UpdateAxialSlice();
    partial void OnCoronalIndexChanged(int value) => UpdateCoronalSlice();
    partial void OnSagittalIndexChanged(int value) => UpdateSagittalSlice();
    partial void OnWindowCenterChanged(double value) => UpdateAllSlices();
    partial void OnWindowWidthChanged(double value) => UpdateAllSlices();

    partial void OnIsoThresholdChanged(double value)
    {
        // Removed Base CT thresholding
    }

    partial void OnBoneMinHUChanged(double value) { UpdateHistograms(); TriggerLivePreviewUpdate(); if (ShowBoneOverlay) UpdateAllSlices(); }
    partial void OnBoneMaxHUChanged(double value) { UpdateHistograms(); TriggerLivePreviewUpdate(); if (ShowBoneOverlay) UpdateAllSlices(); }
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
        TriggerLivePreviewUpdate();
    }

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

    // ÔöÇÔöÇÔöÇ DICOM Load Command ÔöÇÔöÇÔöÇ
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

            OriginalVolume = null; // Reset starting position for new DICOM

            // Update UI state
            PatientName = Volume.PatientName?.Replace("^", " ") ?? "";
            PatientDOB = Volume.PatientDOB;
            StudyDate = FormatStudyDate(Volume.StudyDate);
            SeriesDescription = Volume.SeriesDescription;
            VolumeDimensions = $"{Volume.Width} \u00d7 {Volume.Height} \u00d7 {Volume.Depth}";

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

            // Reset to bone window (W:2000, C:400) so CT slices show correctly on every load
            WindowCenter = 400;
            WindowWidth = 2000;

            IsVolumeLoaded = true;
            UpdateAllSlices();
            UpdateHistograms();
            RefreshCombinedModel(); // Force UI Camera to center on the raw Volume bounds

            StatusText = $"Loaded: {Volume.PatientName} \u2014 {Volume.Depth} slices";
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

    // ÔöÇÔöÇÔöÇ Slice Update Methods ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ Bitmap Helpers ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ Histogram Methods ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ NHP Physical Reslice (bakes DICOM volume to NHP orientation) ÔöÇÔöÇÔöÇ
    private async Task PerformPhysicalResliceAsync(
        double dPitch = 0, double dRoll = 0, double dYaw = 0,
        double dLat   = 0, double dAnt  = 0, double dVert = 0)
    {
        if (OriginalVolume == null) OriginalVolume = Volume; // Initial capture
        if (OriginalVolume == null || BoneOnlyBounds.IsEmpty) return;

        StatusText = "Calculating exact physical volume bounds...";
        IsLoading = true;

        // --- 1. Calculate Spatial Centroid Pivot ---
        // Crucial: Use the bounds of the original bone to pivot from the original space
        var bounds = BoneOnlyBounds;
        Point3D center;
        if (!bounds.IsEmpty)
            center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        else
        {
            var dims = OriginalVolume.GetPhysicalDimensions();
            center = new Point3D(dims.Width / 2, dims.Height / 2, dims.Depth / 2);
        }

        // --- 2. Build the STRICT SOURCE -> TARGET Matrix (exactly matching visual UpdateNhpTransform) ---
        var visualGroup = new Transform3DGroup();
        visualGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), NhpPitch)));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), NhpRoll)));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), NhpYaw)));
        visualGroup.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));

        var sourceToTargetMatrix = visualGroup.Value;

        // Target -> Source is exactly the inverse of the above
        var targetToSourceMatrix = sourceToTargetMatrix;
        if (targetToSourceMatrix.HasInverse) targetToSourceMatrix.Invert();

        // SegmentationEngine ResliceVolume parameter 2 is 'transform', mathematically mapping Target to Source!
        var transform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = targetToSourceMatrix.M11, M12 = targetToSourceMatrix.M12, M13 = targetToSourceMatrix.M13, M14 = targetToSourceMatrix.M14,
            M21 = targetToSourceMatrix.M21, M22 = targetToSourceMatrix.M22, M23 = targetToSourceMatrix.M23, M24 = targetToSourceMatrix.M24,
            M31 = targetToSourceMatrix.M31, M32 = targetToSourceMatrix.M32, M33 = targetToSourceMatrix.M33, M34 = targetToSourceMatrix.M34,
            M41 = targetToSourceMatrix.OffsetX, M42 = targetToSourceMatrix.OffsetY, M43 = targetToSourceMatrix.OffsetZ, M44 = targetToSourceMatrix.M44
        };

        // SegmentationEngine ResliceVolume parameter 3 is 'inverseTransform', mapping Source to Target to find bounds!
        var inverseTransform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = sourceToTargetMatrix.M11, M12 = sourceToTargetMatrix.M12, M13 = sourceToTargetMatrix.M13, M14 = sourceToTargetMatrix.M14,
            M21 = sourceToTargetMatrix.M21, M22 = sourceToTargetMatrix.M22, M23 = sourceToTargetMatrix.M23, M24 = sourceToTargetMatrix.M24,
            M31 = sourceToTargetMatrix.M31, M32 = sourceToTargetMatrix.M32, M33 = sourceToTargetMatrix.M33, M34 = sourceToTargetMatrix.M34,
            M41 = sourceToTargetMatrix.OffsetX, M42 = sourceToTargetMatrix.OffsetY, M43 = sourceToTargetMatrix.OffsetZ, M44 = sourceToTargetMatrix.M44
        };

        StatusText = "Reslicing volume matrix...";

        // Pass both transforms so the Engine can determine exact physical boundaries and pad without waste!
        // Reslice from the ORIGINAL volume so angles are absolute!
        var resliced = await Task.Run(() => SegmentationEngine.ResliceVolume(OriginalVolume, transform, inverseTransform));

        IsLoading = false;

        // dPitch/Roll/Yaw/Lat/Ant/Vert are the delta values passed in from CommitNhpAsync
        // (captured before _cXxx was updated, so they are the true increment to bake)
        var deltaGroup = new Transform3DGroup();
        deltaGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        deltaGroup.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));

        var m = deltaGroup.Value;

        var allSegmentModels = Segments.ToList();
        if (HardTissueModel != null && !allSegmentModels.Contains(HardTissueModel)) allSegmentModels.Add(HardTissueModel);
        if (SoftTissueModel != null && !allSegmentModels.Contains(SoftTissueModel)) allSegmentModels.Add(SoftTissueModel);
        if (DentalModel != null && !allSegmentModels.Contains(DentalModel)) allSegmentModels.Add(DentalModel);

        foreach (var seg in allSegmentModels)
        {
            if (seg.Vertices != null)
            {
                var p = new Point3D();
                for (int i = 0; i < seg.Vertices.Length; i += 3)
                {
                    p.X = seg.Vertices[i]; p.Y = seg.Vertices[i + 1]; p.Z = seg.Vertices[i + 2];
                    var t = m.Transform(p);
                    seg.Vertices[i] = (float)t.X; seg.Vertices[i + 1] = (float)t.Y; seg.Vertices[i + 2] = (float)t.Z;
                }
            }
        }
        foreach (var mesh in ImportedMeshes)
        {
            if (mesh.Vertices != null)
            {
                var p = new Point3D();
                for (int i = 0; i < mesh.Vertices.Length; i += 3)
                {
                    p.X = mesh.Vertices[i]; p.Y = mesh.Vertices[i + 1]; p.Z = mesh.Vertices[i + 2];
                    var t = m.Transform(p);
                    mesh.Vertices[i] = (float)t.X; mesh.Vertices[i + 1] = (float)t.Y; mesh.Vertices[i + 2] = (float)t.Z;
                }
            }
        }
        // NHP-FIX: Transform Occlusion STL meshes by the same delta so they stay aligned with bone
        foreach (var occ in LoadedOcclusions)
        {
            if (occ.Vertices != null)
            {
                var p = new Point3D();
                for (int i = 0; i < occ.Vertices.Length; i += 3)
                {
                    p.X = occ.Vertices[i]; p.Y = occ.Vertices[i + 1]; p.Z = occ.Vertices[i + 2];
                    var t = m.Transform(p);
                    occ.Vertices[i] = (float)t.X; occ.Vertices[i + 1] = (float)t.Y; occ.Vertices[i + 2] = (float)t.Z;
                }
            }
        }
        // NHP-FIX: Transform condylar axis pivot points and dental midline so surgical pivots remain correct
        if (LeftCondyleCenter.HasValue)
        {
            var lc = m.Transform(new Point3D(LeftCondyleCenter.Value.X, LeftCondyleCenter.Value.Y, LeftCondyleCenter.Value.Z));
            LeftCondyleCenter = (lc.X, lc.Y, lc.Z);
        }
        if (RightCondyleCenter.HasValue)
        {
            var rc = m.Transform(new Point3D(RightCondyleCenter.Value.X, RightCondyleCenter.Value.Y, RightCondyleCenter.Value.Z));
            RightCondyleCenter = (rc.X, rc.Y, rc.Z);
        }
        if (DentalMidlinePoint.HasValue)
        {
            var dm = m.Transform(new Point3D(DentalMidlinePoint.Value.X, DentalMidlinePoint.Value.Y, DentalMidlinePoint.Value.Z));
            DentalMidlinePoint = (dm.X, dm.Y, dm.Z);
        }
        // NHP-FIX: Transform cephalometric 3D landmark positions (2D coords are invalidated)
        if (SavedCephLandmarks.Count > 0)
        {
            var updatedLandmarks = new List<CephLandmarkSave>();
            foreach (var lm in SavedCephLandmarks)
            {
                if (lm.X3D.HasValue && lm.Y3D.HasValue && lm.Z3D.HasValue)
                {
                    var tp = m.Transform(new Point3D(lm.X3D.Value, lm.Y3D.Value, lm.Z3D.Value));
                    // Invalidate 2D DRR coords (null) — must be re-projected from the new volume
                    updatedLandmarks.Add(new CephLandmarkSave(lm.Name, null, null, tp.X, tp.Y, tp.Z));
                }
                else
                {
                    updatedLandmarks.Add(lm);
                }
            }
            SavedCephLandmarks = updatedLandmarks;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
            Volume = resliced;

            // Re-initialize segmentation volume for the new dimensions (Air Padded array)
            _segVolume = new SegmentationVolume(Volume);

            // NHP-FIX: Explicitly invalidate _boneOnlySegVolume — its dimensions no longer match
            // the resliced volume. SplitCraniumMandibleAsync will auto-regenerate it when needed.
            _boneOnlySegVolume = null;

            // Rebuild models since we mutated their vertices physically
            foreach (var seg in allSegmentModels) seg.BuildModel();
            foreach (var mesh in ImportedMeshes) mesh.BuildModel();
            foreach (var occ in LoadedOcclusions) occ.BuildModel();

            // Reset all transforms to identity — vertices are now physically at the correct NHP position
            _nhpTransform = System.Windows.Media.Media3D.Transform3D.Identity;
            foreach (var seg in allSegmentModels)
            {
                seg.SurgicalTransform = System.Windows.Media.Media3D.Transform3D.Identity;
                seg.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            }
            foreach (var mesh in ImportedMeshes)
                mesh.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            foreach (var occ in LoadedOcclusions)
                occ.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (HardTissueModel != null) HardTissueModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (SoftTissueModel != null) SoftTissueModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (DentalModel != null)     DentalModel.Transform     = System.Windows.Media.Media3D.Transform3D.Identity;

            // NHP-FIX: Reset all surgical movement sliders to zero.
            // The SurgicalTransform on each segment is already reset to Identity above,
            // but the UI slider values must also be zeroed to prevent them from being
            // re-applied on the next UpdateSurgeryTransform() call.
            SurgMaxillaLat = SurgMaxillaAnt = SurgMaxillaVert = 0;
            SurgMaxillaRoll = SurgMaxillaPitch = SurgMaxillaYaw = 0;
            SurgMandibleLat = SurgMandibleAnt = SurgMandibleVert = 0;
            SurgMandibleRoll = SurgMandiblePitch = SurgMandibleYaw = 0;
            SurgRightRamusLat = SurgRightRamusAnt = SurgRightRamusVert = 0;
            SurgRightRamusRoll = SurgRightRamusPitch = SurgRightRamusYaw = 0;
            SurgLeftRamusLat = SurgLeftRamusAnt = SurgLeftRamusVert = 0;
            SurgLeftRamusRoll = SurgLeftRamusPitch = SurgLeftRamusYaw = 0;
            SurgChinLat = SurgChinAnt = SurgChinVert = 0;
            SurgChinRoll = SurgChinPitch = SurgChinYaw = 0;
            _savedMaxilla = (0, 0, 0, 0, 0, 0);
            _savedMandible = (0, 0, 0, 0, 0, 0);

            // NHP-FIX: Clear undo/redo stacks. Snapshots hold references to the same
            // SegmentViewModel objects whose Vertices arrays were just mutated in-place,
            // so the snapshot data is silently corrupted. Undo after NHP commit is not supported.
            _undoStack.Clear();
            _redoStack.Clear();

            // CRITICAL: sync BoneOnlyBounds to the new resliced volume NOW.
            // Without this, the first visibility toggle after commit would find
            // newBounds != BoneOnlyBounds (old dims) and snap ModelCenter to the
            // new padded-volume center, causing a visible caudal translation.
            BoneOnlyBounds = new Rect3D(0, 0, 0,
                Volume.Width  * Volume.Spacing[0],
                Volume.Height * Volume.Spacing[1],
                Volume.Depth  * Volume.Spacing[2]);
            ModelCenter = new Point3D(
                BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
            OnPropertyChanged(nameof(BoneOnlyBounds));
            OnPropertyChanged(nameof(ModelCenter));

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

    private static string FormatStudyDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return "";

        string clean = dateStr.Trim();

        // 1. Raw DICOM format YYYYMMDD
        if (clean.Length == 8 && long.TryParse(clean, out _))
        {
            return $"{clean.Substring(6, 2)}-{clean.Substring(4, 2)}-{clean.Substring(0, 4)}";
        }

        // 2. Format with slashes DD/MM/YYYY
        if (clean.Length == 10 && clean[2] == '/' && clean[5] == '/')
        {
            return $"{clean.Substring(0, 2)}-{clean.Substring(3, 2)}-{clean.Substring(6, 4)}";
        }

        // 3. Try parsing with standard DateTime parser
        if (System.DateTime.TryParse(clean, out var parsed))
        {
            return parsed.ToString("dd-MM-yyyy");
        }

        return clean;
    }
}
