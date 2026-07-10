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

public enum MprOrientation { Axial, Coronal, Sagittal }

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Volume State ÔöÇÔöÇÔöÇ
    [ObservableProperty] private VolumeData? _volume;
    [ObservableProperty] private bool _isVolumeLoaded;
    private string? _lastDicomPath;

    // Raised when a new DICOM load or project open resets the session — lets the view
    // drop viewport-bound visuals (custom measurements) that the VM doesn't own.
    public event Action? ProjectReset;

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
    partial void OnAxialIndexChanged(int value) { GetInverseNhpTransform(out var invNhp); UpdateAxialSlice(invNhp); }
    partial void OnCoronalIndexChanged(int value) { GetInverseNhpTransform(out var invNhp); UpdateCoronalSlice(invNhp); }
    partial void OnSagittalIndexChanged(int value) { GetInverseNhpTransform(out var invNhp); UpdateSagittalSlice(invNhp); }
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
            LoadedOcclusions.Clear();
            OcclusionNodes.Clear();

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

            // Load is committed — tell the view to drop session-bound visuals (measurements).
            ProjectReset?.Invoke();

            StatusText = $"Loading series ({selectorVm.SelectedSeries.Info.ImageCount} slices)...";

            Volume = await Task.Run(() =>
                DicomLoader.LoadSeriesAsync(selectorVm.SelectedSeries.Info.FilePaths, p =>
                    Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 60)));

            // Phase 0: Bake the volume pivot from the original DICOM dimensions.
            // This is the permanent rotation pivot; it never drifts across reslices.
            VolumePivot = new Point3D(
                Volume.Width * Volume.Spacing[0] / 2.0,
                Volume.Height * Volume.Spacing[1] / 2.0,
                Volume.Depth * Volume.Spacing[2] / 2.0);

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

    // ─── Slice Update Methods ───

    // NHP padded AABB bounds (in NHP-space mm), computed once per UpdateAllSlices call.
    // Used by slice methods AND crosshair drawing. Null when NHP is identity.
    private double? _nhpBoundsMinX, _nhpBoundsMaxX, _nhpBoundsMinY, _nhpBoundsMaxY, _nhpBoundsMinZ, _nhpBoundsMaxZ;

    /// <summary>True when NHP transform is non-identity (padded bitmaps are in use).</summary>
    public bool IsNhpPadded => _nhpBoundsMinX.HasValue;

    /// <summary>
    /// Returns the physical extents (mm) for the specified MPR orientation,
    /// in display order: hMin/hMax = left/right, vMin/vMax = top/bottom.
    /// Maps directly to canvas coordinates: fraction = (phys - min) / (max - min).
    /// </summary>
    public void GetMprPhysicalBounds(MprOrientation orient,
        out double hMin, out double hMax, out double vMin, out double vMax)
    {
        if (Volume == null) { hMin = hMax = vMin = vMax = 0; return; }

        double minX = IsNhpPadded ? _nhpBoundsMinX!.Value : 0;
        double maxX = IsNhpPadded ? _nhpBoundsMaxX!.Value : Volume.Width * Volume.Spacing[0];
        double minY = IsNhpPadded ? _nhpBoundsMinY!.Value : 0;
        double maxY = IsNhpPadded ? _nhpBoundsMaxY!.Value : Volume.Height * Volume.Spacing[1];
        double minZ = IsNhpPadded ? _nhpBoundsMinZ!.Value : 0;
        double maxZ = IsNhpPadded ? _nhpBoundsMaxZ!.Value : (Volume.Depth - 1) * Volume.Spacing[2];

        switch (orient)
        {
            case MprOrientation.Axial:    // H = X (left→right), V = Y (top→bottom)
                hMin = minX; hMax = maxX; vMin = minY; vMax = maxY;
                break;
            case MprOrientation.Coronal:  // H = X (left→right), V = Z (top=maxZ, bottom=minZ, flipped)
                hMin = minX; hMax = maxX; vMin = maxZ; vMax = minZ;
                break;
            case MprOrientation.Sagittal: // H = Y (left→right), V = Z (top=maxZ, bottom=minZ, flipped)
                hMin = minY; hMax = maxY; vMin = maxZ; vMax = minZ;
                break;
            default:
                hMin = hMax = vMin = vMax = 0;
                break;
        }
    }

    private void UpdateAllSlices()
    {
        if (Volume == null) return;

        // Compute NHP total bounds once (cumulative × delta), share across slice methods
        GetInverseNhpTransform(out var invNhp);
        bool isNhpEffectivelyIdentity = _cumulativeNhpMatrix.IsIdentity && _nhpTransform.Value.IsIdentity;
        if (isNhpEffectivelyIdentity)
        {
            _nhpBoundsMinX = _nhpBoundsMaxX = _nhpBoundsMinY = _nhpBoundsMaxY = _nhpBoundsMinZ = _nhpBoundsMaxZ = null;
            // Restore original slider ranges and display heights
            AxialMax    = Volume.Depth  - 1;
            CoronalMax  = Volume.Height - 1;
            SagittalMax = Volume.Width  - 1;
            AxialDisplayHeight    = new System.Windows.GridLength(Volume.Height * Volume.Spacing[1], System.Windows.GridUnitType.Star);
            CoronalDisplayHeight  = new System.Windows.GridLength(Volume.Depth  * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(Volume.Depth  * Volume.Spacing[2], System.Windows.GridUnitType.Star);
        }
        else
        {
            GetNhpVolumeBounds(out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ);
            _nhpBoundsMinX = minX; _nhpBoundsMaxX = maxX;
            _nhpBoundsMinY = minY; _nhpBoundsMaxY = maxY;
            _nhpBoundsMinZ = minZ; _nhpBoundsMaxZ = maxZ;
            // Update slider ranges to cover the full NHP-padded AABB
            AxialMax    = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / Volume.Spacing[2]));
            CoronalMax  = Math.Max(1, (int)Math.Ceiling((maxY - minY) / Volume.Spacing[1]));
            SagittalMax = Math.Max(1, (int)Math.Ceiling((maxX - minX) / Volume.Spacing[0]));
            // Update display heights to match NHP-padded extents for uniform cranium scale
            AxialDisplayHeight    = new System.Windows.GridLength(maxY - minY, System.Windows.GridUnitType.Star);
            CoronalDisplayHeight  = new System.Windows.GridLength(maxZ - minZ, System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(maxZ - minZ, System.Windows.GridUnitType.Star);
        }
        // Clamp current indices to new ranges (prevent out-of-bounds after NHP range change)
        AxialIndex    = Math.Clamp(AxialIndex, 0, AxialMax);
        CoronalIndex  = Math.Clamp(CoronalIndex, 0, CoronalMax);
        SagittalIndex = Math.Clamp(SagittalIndex, 0, SagittalMax);

        // Pass identity-check flag so slice methods avoid GetNhpBounds re-computation
        UpdateAxialSlice(invNhp);
        UpdateCoronalSlice(invNhp);
        UpdateSagittalSlice(invNhp);
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

    private void UpdateAxialSlice(Matrix3D invNhp)
    {
        if (Volume == null) return;

        double zMm = AxialIndex * Volume.Spacing[2];
        int outW, outH;
        Point3D originNhp;
        Vector3D uAxisNhp, vAxisNhp;

        if (invNhp.IsIdentity)
        {
            outW = Volume.Width;
            outH = Volume.Height;
            originNhp = new Point3D(0, 0, zMm);
            uAxisNhp  = new Vector3D(Volume.Spacing[0], 0, 0);
            vAxisNhp  = new Vector3D(0, Volume.Spacing[1], 0);
        }
        else
        {
            double minX = _nhpBoundsMinX!.Value, maxX = _nhpBoundsMaxX!.Value;
            double minY = _nhpBoundsMinY!.Value, maxY = _nhpBoundsMaxY!.Value;
            outW = Math.Max(1, (int)Math.Ceiling((maxX - minX) / Volume.Spacing[0]));
            outH = Math.Max(1, (int)Math.Ceiling((maxY - minY) / Volume.Spacing[1]));
            // V-0.2: Cap MPR output size to prevent OOM from extreme NHP rotations
            outW = Math.Min(outW, Volume.Width * MaxMprExpansion);
            outH = Math.Min(outH, Volume.Height * MaxMprExpansion);
            // Offset slice position to NHP-space (slider now covers full NHP AABB)
            zMm = _nhpBoundsMinZ!.Value + AxialIndex * Volume.Spacing[2];
            originNhp = new Point3D(minX, minY, zMm);
            uAxisNhp  = new Vector3D(Volume.Spacing[0], 0, 0);
            vAxisNhp  = new Vector3D(0, Volume.Spacing[1], 0);
        }

        var origin = invNhp.Transform(originNhp);
        var uAxis  = invNhp.Transform(uAxisNhp);
        var vAxis  = invNhp.Transform(vAxisNhp);

        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetObliqueSliceWithMaskBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, _segVolume);
            AxialImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetObliqueSliceBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, (short)min, (short)max);
            AxialImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
        else
        {
            var data = Volume.GetObliqueSliceGrayscale(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth);
            AxialImage = CreateGrayscaleBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
    }

    private void UpdateCoronalSlice(Matrix3D invNhp)
    {
        if (Volume == null) return;

        double yMm = CoronalIndex * Volume.Spacing[1];
        int outW, outH;
        Point3D originNhp;
        Vector3D uAxisNhp, vAxisNhp;

        if (invNhp.IsIdentity)
        {
            outW = Volume.Width;
            outH = Volume.Depth;
            // Flipped V (negative Z) so row 0 = top of image matches original GetCoronalSlice display
            originNhp = new Point3D(0, yMm, (Volume.Depth - 1) * Volume.Spacing[2]);
            uAxisNhp  = new Vector3D(Volume.Spacing[0], 0, 0);
            vAxisNhp  = new Vector3D(0, 0, -Volume.Spacing[2]);
        }
        else
        {
            double minX = _nhpBoundsMinX!.Value, maxX = _nhpBoundsMaxX!.Value;
            double minZ = _nhpBoundsMinZ!.Value, maxZ = _nhpBoundsMaxZ!.Value;
            outW = Math.Max(1, (int)Math.Ceiling((maxX - minX) / Volume.Spacing[0]));
            outH = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / Volume.Spacing[2]));
            // V-0.2: Cap MPR output size to prevent OOM from extreme NHP rotations
            outW = Math.Min(outW, Volume.Width * MaxMprExpansion);
            outH = Math.Min(outH, Volume.Depth * MaxMprExpansion);
            // Offset slice position to NHP-space
            yMm = _nhpBoundsMinY!.Value + CoronalIndex * Volume.Spacing[1];
            originNhp = new Point3D(minX, yMm, maxZ);
            uAxisNhp  = new Vector3D(Volume.Spacing[0], 0, 0);
            vAxisNhp  = new Vector3D(0, 0, -Volume.Spacing[2]);
        }

        var origin = invNhp.Transform(originNhp);
        var uAxis  = invNhp.Transform(uAxisNhp);
        var vAxis  = invNhp.Transform(vAxisNhp);

        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetObliqueSliceWithMaskBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, _segVolume);
            CoronalImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetObliqueSliceBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, (short)min, (short)max);
            CoronalImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetObliqueSliceGrayscale(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth);
            CoronalImage = CreateGrayscaleBitmap(data, outW, outH,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
    }

    private void UpdateSagittalSlice(Matrix3D invNhp)
    {
        if (Volume == null) return;

        double xMm = SagittalIndex * Volume.Spacing[0];
        int outW, outH;
        Point3D originNhp;
        Vector3D uAxisNhp, vAxisNhp;

        if (invNhp.IsIdentity)
        {
            outW = Volume.Height;
            outH = Volume.Depth;
            // Flipped V (negative Z) so row 0 = top of image matches original GetSagittalSlice display
            originNhp = new Point3D(xMm, 0, (Volume.Depth - 1) * Volume.Spacing[2]);
            uAxisNhp  = new Vector3D(0, Volume.Spacing[1], 0);
            vAxisNhp  = new Vector3D(0, 0, -Volume.Spacing[2]);
        }
        else
        {
            double minY = _nhpBoundsMinY!.Value, maxY = _nhpBoundsMaxY!.Value;
            double minZ = _nhpBoundsMinZ!.Value, maxZ = _nhpBoundsMaxZ!.Value;
            outW = Math.Max(1, (int)Math.Ceiling((maxY - minY) / Volume.Spacing[1]));
            outH = Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / Volume.Spacing[2]));
            // V-0.2: Cap MPR output size to prevent OOM from extreme NHP rotations
            outW = Math.Min(outW, Volume.Height * MaxMprExpansion);
            outH = Math.Min(outH, Volume.Depth * MaxMprExpansion);
            // Offset slice position to NHP-space
            xMm = _nhpBoundsMinX!.Value + SagittalIndex * Volume.Spacing[0];
            originNhp = new Point3D(xMm, minY, maxZ);
            uAxisNhp  = new Vector3D(0, Volume.Spacing[1], 0);
            vAxisNhp  = new Vector3D(0, 0, -Volume.Spacing[2]);
        }

        var origin = invNhp.Transform(originNhp);
        var uAxis  = invNhp.Transform(uAxisNhp);
        var vAxis  = invNhp.Transform(vAxisNhp);

        if (IsRegionGrowMode && _segVolume != null)
        {
            var data = Volume.GetObliqueSliceWithMaskBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, _segVolume);
            SagittalImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
        else if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetObliqueSliceBgra(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth, (short)min, (short)max);
            SagittalImage = CreateBgraBitmap(data, outW, outH,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetObliqueSliceGrayscale(outW, outH,
                origin.X, origin.Y, origin.Z,
                uAxis.X, uAxis.Y, uAxis.Z,
                vAxis.X, vAxis.Y, vAxis.Z,
                WindowCenter, WindowWidth);
            SagittalImage = CreateGrayscaleBitmap(data, outW, outH,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
    }

    // ─── NHP Oblique-Slice Helpers ───

    /// <summary>Maximum MPR output expansion factor (prevents OOM from extreme NHP rotations).</summary>
    private const int MaxMprExpansion = 4;

    /// <summary>
    /// Inverts the TOTAL NHP transform (cumulative committed × current delta) so we can
    /// map NHP-space slice geometry back into DICOM space for oblique sampling.
    /// - Cumulative: baked history of all past commits (DICOM → baked space)
    /// - Delta: current uncommitted preview (baked space → preview space)
    /// - Total = cumulative × delta (CORRECT order for row-vector convention)
    /// </summary>
    private void GetInverseNhpTransform(out Matrix3D matrix)
    {
        // Total transform = cumulative baked history × current uncommitted delta
        var deltaMatrix = _nhpTransform.Value;
        bool isTotalIdentity = _cumulativeNhpMatrix.IsIdentity && deltaMatrix.IsIdentity;

        if (isTotalIdentity)
        {
            matrix = Matrix3D.Identity;
            return;
        }

        // Compose: cumulative first, then delta (row-vector convention: cumulative × delta)
        matrix = _cumulativeNhpMatrix;
        matrix.Append(deltaMatrix);

        if (matrix.HasInverse) matrix.Invert();
        else { matrix = Matrix3D.Identity; return; }

        // V-0.3: Post-inversion NaN/Infinity safety check
        if (double.IsNaN(matrix.M11) || double.IsInfinity(matrix.M11) ||
            double.IsNaN(matrix.OffsetX) || double.IsInfinity(matrix.OffsetX))
        {
            matrix = Matrix3D.Identity;
            StatusText = "⚠ NHP transform near-singular — MPR using identity fallback";
        }
    }


    /// <summary>
    /// Computes the AABB of the volume after the TOTAL NHP transform (cumulative × delta).
    /// Used to size the MPR output bitmaps so nothing clips during rotation.
    /// </summary>
    private void GetNhpVolumeBounds(out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ)
    {
        if (Volume == null)
        {
            minX = maxX = minY = maxY = minZ = maxZ = 0;
            return;
        }
        double w = Volume.Width  * Volume.Spacing[0];
        double h = Volume.Height * Volume.Spacing[1];
        double d = Volume.Depth  * Volume.Spacing[2];

        // Total transform = cumulative × delta (row-vector convention)
        var totalMatrix = _cumulativeNhpMatrix;
        totalMatrix.Append(_nhpTransform.Value);

        if (totalMatrix.IsIdentity)
        {
            minX = 0; maxX = w; minY = 0; maxY = h; minZ = 0; maxZ = d;
            return;
        }

        var corners = new Point3D[]
        {
            new(0, 0, 0), new(w, 0, 0), new(0, h, 0), new(w, h, 0),
            new(0, 0, d), new(w, 0, d), new(0, h, d), new(w, h, d),
        };
        minX = maxX = minY = maxY = minZ = maxZ = 0;
        bool first = true;
        foreach (var p in corners)
        {
            var tp = totalMatrix.Transform(p);
            if (first) { minX = maxX = tp.X; minY = maxY = tp.Y; minZ = maxZ = tp.Z; first = false; continue; }
            minX = Math.Min(minX, tp.X); maxX = Math.Max(maxX, tp.X);
            minY = Math.Min(minY, tp.Y); maxY = Math.Max(maxY, tp.Y);
            minZ = Math.Min(minZ, tp.Z); maxZ = Math.Max(maxZ, tp.Z);
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

    // NHP is now visual-only (see NhpViewModel.cs CommitNhp).
    // Physical reslicing has been removed — oblique MPR sampling handles the rotated views.

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
