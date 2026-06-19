using System.Collections.Specialized;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ─── NHP Safety Limits ───
    private const double MaxNhpTranslation = 200.0;  // mm
    private const double MaxNhpRotation = 45.0;       // degrees

    private static double ClampNhp(double value, bool isRotation)
        => Math.Clamp(value, isRotation ? -MaxNhpRotation : -MaxNhpTranslation, isRotation ? MaxNhpRotation : MaxNhpTranslation);

    // ─── NHP Parameters (Live adjusted) ───
    [ObservableProperty] private double _nhpLateral = 0.0;
    [ObservableProperty] private double _nhpAnteroposterior = 0.0;
    [ObservableProperty] private double _nhpVertical = 0.0;
    [ObservableProperty] private double _nhpRoll = 0.0;
    [ObservableProperty] private double _nhpPitch = 0.0;
    [ObservableProperty] private double _nhpYaw = 0.0;

    // ━━━ NHP Committed State (Baseline) ━━━
    private double _cLat, _cAnt, _cVert, _cRoll, _cPitch, _cYaw;
    // The current NHP delta transform (applied to ALL segments on top of their surgical offsets)
    private System.Windows.Media.Media3D.Transform3D _nhpTransform = System.Windows.Media.Media3D.Transform3D.Identity;
    public Rect3D BoneOnlyBounds { get; private set; } = Rect3D.Empty; // Bone segment bounds only, excludes imported STL meshes

    public bool IsNhpDirty => Math.Abs(NhpLateral - _cLat) > 0.01 ||
                              Math.Abs(NhpAnteroposterior - _cAnt) > 0.01 ||
                              Math.Abs(NhpVertical - _cVert) > 0.01 ||
                              Math.Abs(NhpRoll - _cRoll) > 0.01 ||
                              Math.Abs(NhpPitch - _cPitch) > 0.01 ||
                              Math.Abs(NhpYaw - _cYaw) > 0.01;

    public bool HasModelLoaded => !BoneOnlyBounds.IsEmpty || Volume != null || Segments.Count > 0;

    public bool IsCompletelyLoaded =>
        HasModelLoaded && (Volume != null || !BoneOnlyBounds.IsEmpty) && !IsLoading;

    // Prevent camera jumping during automated splits
    public bool IsSplitting { get; set; }

    // ─── Debounced slice update timer ───
    private System.Windows.Threading.DispatcherTimer? _mprDebounceTimer;

    private void ScheduleDebouncedSliceUpdate()
    {
        if (_mprDebounceTimer == null)
        {
            _mprDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };
            _mprDebounceTimer.Tick += (_, _) =>
            {
                _mprDebounceTimer.Stop();
                UpdateAllSlices();
            };
        }
        _mprDebounceTimer.Stop();
        _mprDebounceTimer.Start();
    }

    partial void OnNhpLateralChanged(double value) { if (value != ClampNhp(value, false)) NhpLateral = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpAnteroposteriorChanged(double value) { if (value != ClampNhp(value, false)) NhpAnteroposterior = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpVerticalChanged(double value) { if (value != ClampNhp(value, false)) NhpVertical = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpRollChanged(double value) { if (value != ClampNhp(value, true)) NhpRoll = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpPitchChanged(double value) { if (value != ClampNhp(value, true)) NhpPitch = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpYawChanged(double value) { if (value != ClampNhp(value, true)) NhpYaw = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }

    [RelayCommand]
    private void AdjustNhp(string param)
    {
        double step = 0.1;
        if (param.Contains("Lat")) NhpLateral = ClampNhp(NhpLateral + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Ant")) NhpAnteroposterior = ClampNhp(NhpAnteroposterior + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Vert")) NhpVertical = ClampNhp(NhpVertical + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Roll")) NhpRoll = ClampNhp(NhpRoll + (param.EndsWith("+") ? step : -step), true);
        else if (param.Contains("Pitch")) NhpPitch = ClampNhp(NhpPitch + (param.EndsWith("+") ? step : -step), true);
        else if (param.Contains("Yaw")) NhpYaw = ClampNhp(NhpYaw + (param.EndsWith("+") ? step : -step), true);
    }

    [RelayCommand]
    private void CommitNhp()
    {
        // Visual-only NHP: simply lock the current slider values as the new baseline.
        // No physical reslicing, no surgical reset, no undo clear.
        _cLat = NhpLateral; _cAnt = NhpAnteroposterior; _cVert = NhpVertical;
        _cRoll = NhpRoll; _cPitch = NhpPitch; _cYaw = NhpYaw;
        OnPropertyChanged(nameof(IsNhpDirty));
        StatusText = "NHP committed.";
    }

    /// <summary>Reset all NHP parameters to the committed baseline.</summary>
    [RelayCommand]
    private void ResetNhp()
    {
        // Direct field writes to avoid 6× redundant UpdateNhpTransform calls
#pragma warning disable MVVMTK0034
        _nhpLateral         = _cLat;
        _nhpAnteroposterior = _cAnt;
        _nhpVertical        = _cVert;
        _nhpRoll            = _cRoll;
        _nhpPitch           = _cPitch;
        _nhpYaw             = _cYaw;
#pragma warning restore MVVMTK0034

        // Notify UI of all property changes
        OnPropertyChanged(nameof(NhpLateral));
        OnPropertyChanged(nameof(NhpAnteroposterior));
        OnPropertyChanged(nameof(NhpVertical));
        OnPropertyChanged(nameof(NhpRoll));
        OnPropertyChanged(nameof(NhpPitch));
        OnPropertyChanged(nameof(NhpYaw));
        OnPropertyChanged(nameof(IsNhpDirty));

        // Force immediate full update (bypass debounce)
        _mprDebounceTimer?.Stop();
        UpdateNhpTransform();
        UpdateAllSlices();
    }

    /// <summary>Zero a single NHP parameter by name (e.g. "Lat", "Pitch").</summary>
    [RelayCommand]
    private void ZeroNhpParam(string param)
    {
        if (param.Contains("Lat")) NhpLateral = 0;
        else if (param.Contains("Ant")) NhpAnteroposterior = 0;
        else if (param.Contains("Vert")) NhpVertical = 0;
        else if (param.Contains("Roll")) NhpRoll = 0;
        else if (param.Contains("Pitch")) NhpPitch = 0;
        else if (param.Contains("Yaw")) NhpYaw = 0;

        // Force immediate full update (bypass debounce)
        _mprDebounceTimer?.Stop();
        UpdateNhpTransform();
        UpdateAllSlices();
    }

    private void UpdateNhpTransform()
    {
        if (BoneOnlyBounds.IsEmpty)
        {
            // Provide feedback instead of silently doing nothing
            if (Math.Abs(NhpLateral) > 0.01 || Math.Abs(NhpAnteroposterior) > 0.01 ||
                Math.Abs(NhpVertical) > 0.01 || Math.Abs(NhpRoll) > 0.01 ||
                Math.Abs(NhpPitch) > 0.01 || Math.Abs(NhpYaw) > 0.01)
            {
                StatusText = "⚠ Segment bone first to enable NHP adjustment";
            }
            return;
        }

        // Phase 0: Use the baked VolumePivot for rotation center (stable across reslices).
        // Fallback to bounds-derived center when VolumePivot has not been set yet.
        var center = VolumePivot == null
            ? new Point3D(BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                          BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                          BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2)
            : VolumePivot.Value;

        // TOTAL NHP transform: apply the full current NHP values (not just the delta).
        // This makes _nhpTransform the definitive orientation of all meshes in world space.
        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), NhpPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), NhpRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), NhpYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));

        _nhpTransform = nhp;

        // Apply total NHP transform to all tracked objects (the "NHP ledger")
        ApplyNhpToAllTrackedObjects();

        // Dynamically enforce the freehand rotation pivot point!
        ModelCenter = nhp.Transform(center);
    }

    /// <summary>
    /// NHP Ledger: Applies the current NHP transform to ALL viewport objects.
    /// Called whenever NHP changes. Individual objects are also auto-tagged
    /// via CollectionChanged handlers when they first enter the collections.
    /// </summary>
    private void ApplyNhpToAllTrackedObjects()
    {
        // Convenience references (may be null before segmentation)
        if (HardTissueModel != null) HardTissueModel.Transform = _nhpTransform;
        if (SoftTissueModel != null) SoftTissueModel.Transform = _nhpTransform;
        if (DentalModel != null)     DentalModel.Transform     = _nhpTransform;

        // All segments: NHP composes with their per-segment surgical offset
        foreach (var seg in Segments)
            seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);

        // All imported meshes: NHP only
        foreach (var mesh in ImportedMeshes)
            mesh.Transform = _nhpTransform;

        // All occlusion STLs: NHP only
        foreach (var occ in LoadedOcclusions)
            occ.Transform = _nhpTransform;
    }

    /// <summary>
    /// NHP Ledger: Wire up collection-changed handlers so new objects
    /// automatically receive the current NHP transform on addition.
    /// Called once from MainViewModel constructor.
    /// </summary>
    private void InitNhpLedger()
    {
        Segments.CollectionChanged += OnSegmentsChangedForNhp;
        ImportedMeshes.CollectionChanged += OnMeshesChangedForNhp;
        LoadedOcclusions.CollectionChanged += OnOcclusionsChangedForNhp;
    }

    private void OnSegmentsChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (SegmentViewModel seg in e.NewItems)
                seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
        }
    }

    private void OnMeshesChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (MeshViewModel mesh in e.NewItems)
                mesh.Transform = _nhpTransform;
        }
    }

    private void OnOcclusionsChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (MeshViewModel occ in e.NewItems)
                occ.Transform = _nhpTransform;
        }
    }

    /// <summary>Composes two transforms: applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    private static System.Windows.Media.Media3D.Transform3D ComposeTransforms(
        System.Windows.Media.Media3D.Transform3D first,
        System.Windows.Media.Media3D.Transform3D second)
    {
        if (second == System.Windows.Media.Media3D.Transform3D.Identity) return first;
        var g = new System.Windows.Media.Media3D.Transform3DGroup();
        g.Children.Add(first);
        g.Children.Add(second);
        return g;
    }
}
