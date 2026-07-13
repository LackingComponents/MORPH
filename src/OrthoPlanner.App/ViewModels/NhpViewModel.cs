using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.App.Helpers;

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

    // ━━━ NHP Profiles (NHP 1, NHP 2, …) ━━━
    public ObservableCollection<NhpProfileViewModel> NhpProfiles { get; } = new();
    private NhpProfileViewModel? _activeNhpProfile;
    private NhpProfileViewModel? _hookedActiveProfile;

    /// <summary>While true, viewport camera orientation must not overwrite NHP fields (e.g. right after adding a profile).</summary>
    internal bool SuppressCameraNhpSync { get; private set; }

    private static readonly Regex DefaultNhpNameRegex = new(@"^NHP (\d+)$", RegexOptions.Compiled);

    public string ActiveNhpProfileName => _activeNhpProfile?.Name ?? "NHP 1";

    public bool CanDeleteAnyNhpProfile => NhpProfiles.Count > 1;

    // ─── Lazy transform stack: NhpShared is the single shared NHP matrix every piece composes with. ───
    // Absolute-from-source: zeros = the original un-NHP volume frame. Built from the live six.
    private Matrix3D _nhpShared = Matrix3D.Identity;

    /// <summary>The shared NHP transform, bound to the CT volume render (Task 2). piece.Transform = Compose(NhpShared, piece.LocalTransform).</summary>
    public Transform3D NhpSharedTransform { get; private set; } = Transform3D.Identity;

    public Rect3D BoneOnlyBounds { get; private set; } = Rect3D.Empty; // Bone segment bounds only

    // Dirty = live sliders differ from the active (committed) profile (an uncommitted preview exists).
    // Active profile is guaranteed non-null (InitNhpProfiles/EnsureDefaultNhpProfile run in the ctor).
    public bool IsNhpDirty =>
        _activeNhpProfile != null &&
        (Math.Abs(NhpLateral - _activeNhpProfile.Lateral) > 0.01 ||
         Math.Abs(NhpAnteroposterior - _activeNhpProfile.Anteroposterior) > 0.01 ||
         Math.Abs(NhpVertical - _activeNhpProfile.Vertical) > 0.01 ||
         Math.Abs(NhpRoll - _activeNhpProfile.Roll) > 0.01 ||
         Math.Abs(NhpPitch - _activeNhpProfile.Pitch) > 0.01 ||
         Math.Abs(NhpYaw - _activeNhpProfile.Yaw) > 0.01);

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

    partial void OnNhpLateralChanged(double value)         { if (value != ClampNhp(value, false)) NhpLateral          = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpAnteroposteriorChanged(double value) { if (value != ClampNhp(value, false)) NhpAnteroposterior  = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpVerticalChanged(double value)        { if (value != ClampNhp(value, false)) NhpVertical         = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpRollChanged(double value)            { if (value != ClampNhp(value, true))  NhpRoll             = ClampNhp(value, true);  else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpPitchChanged(double value)           { if (value != ClampNhp(value, true))  NhpPitch            = ClampNhp(value, true);  else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }
    partial void OnNhpYawChanged(double value)             { if (value != ClampNhp(value, true))  NhpYaw              = ClampNhp(value, true);  else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); ScheduleDebouncedSliceUpdate(); } }

    [RelayCommand]
    private void AdjustNhp(string param)
    {
        double step = 0.1;
        if (param.Contains("Lat"))        NhpLateral          = ClampNhp(NhpLateral          + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Ant"))   NhpAnteroposterior  = ClampNhp(NhpAnteroposterior  + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Vert"))  NhpVertical         = ClampNhp(NhpVertical          + (param.EndsWith("+") ? step : -step), false);
        else if (param.Contains("Roll"))  NhpRoll             = ClampNhp(NhpRoll              + (param.EndsWith("+") ? step : -step), true);
        else if (param.Contains("Pitch")) NhpPitch            = ClampNhp(NhpPitch             + (param.EndsWith("+") ? step : -step), true);
        else if (param.Contains("Yaw"))   NhpYaw              = ClampNhp(NhpYaw               + (param.EndsWith("+") ? step : -step), true);
    }

    /// <summary>
    /// Lazy commit (Task 3): a flag flip, NOT a vertex bake. The current pose already lives on every
    /// piece as piece.Transform = Compose(NhpShared, piece.LocalTransform); vertices stay in source
    /// DICOM space forever. INV3: commit moves nothing — the 3D scene and MPR do not change. The
    /// committed pose is recorded into the active profile + marked committed (req e: sliders stay,
    /// showing the current matrix's values, not reset to zero).
    /// </summary>
    [RelayCommand]
    private void CommitNhp()
    {
        if (BoneOnlyBounds.IsEmpty) { StatusText = "⚠ Segment bone first to enable NHP commit"; return; }

        SaveActiveNhpProfileFromUi();
        if (_activeNhpProfile != null)
            _activeNhpProfile.IsCommitted = true;

        OnPropertyChanged(nameof(IsNhpDirty));
        StatusText = $"{_activeNhpProfile?.Name ?? "NHP"} committed.";
    }

    /// <summary>Apply camera pitch/roll/yaw to the live NHP rotation fields (preview only).</summary>
    public void ApplyCameraAnglesToNhp(double pitch, double roll, double yaw)
    {
        NhpPitch = ClampNhp(pitch, true);
        NhpRoll  = ClampNhp(roll, true);
        NhpYaw   = ClampNhp(yaw, true);
    }

    /// <summary>Capture current viewport camera orientation into NHP rotation fields.</summary>
    public void SetNhpRotationsFromCamera(Vector3D lookDir, Vector3D upDir)
    {
        if (SuppressCameraNhpSync) return;
        var (pitch, roll, yaw) = NhpCameraAngles.FromCamera(lookDir, upDir);
        ApplyCameraAnglesToNhp(pitch, roll, yaw);
        StatusText = "Camera orientation applied to NHP rotations. Press DONE to commit.";
    }

    /// <summary>Reset every NHP translation and rotation to zero (with confirmation).</summary>
    [RelayCommand]
    private void ZeroAllNhp()
    {
        if (MessageBox.Show(
                "Reset all Natural Head Position parameters (translations and rotations) to zero?",
                "Reset NHP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        NhpLateral = 0;
        NhpAnteroposterior = 0;
        NhpVertical = 0;
        NhpRoll = 0;
        NhpPitch = 0;
        NhpYaw = 0;
        SaveActiveNhpProfileFromUi();
        StatusText = "NHP parameters reset to zero.";
    }

    /// <summary>Reset all NHP parameters to the committed (active profile) pose.
    /// _activeNhpProfile is guaranteed non-null (InitNhpProfiles/EnsureDefaultNhpProfile run in the ctor),
    /// so the old _cLat fallback is dead and removed with the bake-model fields (Task 4).</summary>
    [RelayCommand]
    private void ResetNhp()
    {
        if (_activeNhpProfile == null) return;
        ForceSetNhpUi(_activeNhpProfile.Lateral, _activeNhpProfile.Anteroposterior, _activeNhpProfile.Vertical,
            _activeNhpProfile.Roll, _activeNhpProfile.Pitch, _activeNhpProfile.Yaw);
    }

    /// <summary>Zero a single NHP parameter by name (e.g. "Lat", "Pitch").</summary>
    [RelayCommand]
    private void ZeroNhpParam(string param)
    {
        // Direct field write to suppress the OnChanged handler (avoid double UpdateNhpTransform)
#pragma warning disable MVVMTK0034
        if      (param.Contains("Lat"))   _nhpLateral         = 0;
        else if (param.Contains("Ant"))   _nhpAnteroposterior = 0;
        else if (param.Contains("Vert"))  _nhpVertical        = 0;
        else if (param.Contains("Roll"))  _nhpRoll            = 0;
        else if (param.Contains("Pitch")) _nhpPitch           = 0;
        else if (param.Contains("Yaw"))   _nhpYaw             = 0;
#pragma warning restore MVVMTK0034

        OnPropertyChanged(nameof(NhpLateral)); OnPropertyChanged(nameof(NhpAnteroposterior));
        OnPropertyChanged(nameof(NhpVertical)); OnPropertyChanged(nameof(NhpRoll));
        OnPropertyChanged(nameof(NhpPitch)); OnPropertyChanged(nameof(NhpYaw));
        OnPropertyChanged(nameof(IsNhpDirty));

        _mprDebounceTimer?.Stop();
        UpdateNhpTransform();
        UpdateAllSlices();
    }

    /// <summary>
    /// Builds the NHP transform matrix from the given six, centered at <paramref name="center"/>.
    /// Zeros = the original un-NHP source frame. Pure geometry — center passed in, no instance state.
    /// The instance overload below sources <paramref name="center"/> from <see cref="VolumePivot"/>/bone
    /// bounds; this static form is what the DEBUG <see cref="NhpMathSelfCheck"/> verifies.
    /// </summary>
    // ponytail: internal for the DEBUG NhpMathSelfCheck only — pure function of center + six.
    internal static Matrix3D BuildNhpMatrix(Point3D center, double dLat, double dAnt, double dVert,
        double dRoll, double dPitch, double dYaw)
    {
        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));
        return nhp.Value;
    }

    /// <summary>Instance path: center from <see cref="VolumePivot"/> (or bone bounds) → the pure static builder.</summary>
    private Matrix3D BuildNhpMatrix(double dLat, double dAnt, double dVert,
        double dRoll, double dPitch, double dYaw)
    {
        var center = VolumePivot ?? new Point3D(
            BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
            BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
            BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
        return BuildNhpMatrix(center, dLat, dAnt, dVert, dRoll, dPitch, dYaw);
    }

    /// <summary>NhpShared = the absolute NHP matrix from the live six (zeros = source frame). No cumulative/delta split under the lazy model.</summary>
    private Matrix3D BuildAbsoluteNhpPreviewMatrix()
        => BuildNhpMatrix(NhpLateral, NhpAnteroposterior, NhpVertical, NhpRoll, NhpPitch, NhpYaw);

    private void UpdateNhpTransform()
    {
        if (BoneOnlyBounds.IsEmpty)
        {
            if (Math.Abs(NhpLateral) > 0.01 || Math.Abs(NhpAnteroposterior) > 0.01 ||
                Math.Abs(NhpVertical) > 0.01 || Math.Abs(NhpRoll) > 0.01 ||
                Math.Abs(NhpPitch) > 0.01 || Math.Abs(NhpYaw) > 0.01)
                StatusText = "⚠ Segment bone first to enable NHP adjustment";
            return;
        }

        RecomputeAllTransforms();
        ScheduleDebouncedSliceUpdate();
    }

    /// <summary>The one recompute site (INV1): every piece.Transform == Compose(NhpShared, piece.LocalTransform).</summary>
    private void RecomputeAllTransforms()
    {
        _nhpShared = BuildAbsoluteNhpPreviewMatrix();
        NhpSharedTransform = new MatrixTransform3D(_nhpShared);
        OnPropertyChanged(nameof(NhpSharedTransform));

        if (HardTissueModel != null) HardTissueModel.Transform = ComposeTransforms(NhpSharedTransform, HardTissueModel.LocalTransform);
        if (SoftTissueModel != null) SoftTissueModel.Transform = ComposeTransforms(NhpSharedTransform, SoftTissueModel.LocalTransform);
        if (DentalModel     != null) DentalModel.Transform     = ComposeTransforms(NhpSharedTransform, DentalModel.LocalTransform);

        foreach (var seg  in Segments)        seg.Transform  = ComposeTransforms(NhpSharedTransform, seg.LocalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = ComposeTransforms(NhpSharedTransform, mesh.LocalTransform);
        foreach (var occ  in LoadedOcclusions) occ.Transform = ComposeTransforms(NhpSharedTransform, occ.LocalTransform);

#if DEBUG
        AssertFormulaHolds();
#endif

        // INV7: the camera pivot is decoupled from NHP — feed the CONSTANT source-space VolumePivot,
        // not NhpShared·VolumePivot. Rotation already worked (a pivot doesn't move under rotation);
        // translation now shows because the pivot no longer follows (and visually cancels) it.
        if (VolumePivot.HasValue)
        {
            ModelCenter = VolumePivot.Value;
            OnPropertyChanged(nameof(ModelCenter));
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void AssertFormulaHolds()
    {
        // INV1 — every piece carries the formula. RecomputeAllTransforms just wrote each, so verify each.
        bool Eq(Matrix3D a, Matrix3D b)
            => Math.Abs(a.M11-b.M11)<1e-9 && Math.Abs(a.OffsetX-b.OffsetX)<1e-9
            && Math.Abs(a.M22-b.M22)<1e-9 && Math.Abs(a.OffsetY-b.OffsetY)<1e-9
            && Math.Abs(a.M33-b.M33)<1e-9 && Math.Abs(a.OffsetZ-b.OffsetZ)<1e-9;
        Matrix3D Expected(Transform3D local)
        { var g = new MatrixTransform3D(_nhpShared); var c = ComposeTransforms(g, local); return c.Value; }
        void Expect(Transform3D? t, Transform3D? local, string what)
        { if (t == null || local == null) return; System.Diagnostics.Debug.Assert(Eq(t.Value, Expected(local)), "INV1 " + what); }
        // INV1 — assert the formula on every piece RecomputeAllTransforms wrote (named models are also
        // Segments refs but asserted explicitly so a future non-Segments named model is still caught).
        foreach (var seg in Segments)           Expect(seg.Transform,            seg.LocalTransform,           "segment");
        Expect(HardTissueModel?.Transform,      HardTissueModel?.LocalTransform, "hard-tissue");
        Expect(SoftTissueModel?.Transform,      SoftTissueModel?.LocalTransform, "soft-tissue");
        Expect(DentalModel?.Transform,          DentalModel?.LocalTransform,     "dental");
        foreach (var mesh in ImportedMeshes)    Expect(mesh.Transform,           mesh.LocalTransform,          "mesh");
        foreach (var occ in LoadedOcclusions)   Expect(occ.Transform,            occ.LocalTransform,            "occlusion");
    }

    /// <summary>Returns the inverse of a Matrix3D, or Identity if not invertible.</summary>
    private static Matrix3D InvertMatrix(Matrix3D m)
    {
        if (!m.HasInverse) return Matrix3D.Identity;
        var inv = m;
        inv.Invert();
        return inv;
    }

    /// <summary>Composes two transforms: applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    private static Transform3D ComposeTransforms(Transform3D first, Transform3D second)
    {
        if (second == Transform3D.Identity) return first;
        var g = new Transform3DGroup();
        g.Children.Add(first);
        g.Children.Add(second);
        return g;
    }

    /// <summary>
    /// NHP Ledger: wire up collection-changed handlers so new pieces receive the composed
    /// NhpShared transform on addition. Lazy model (Task 3): new pieces stay in source space —
    /// we only compose, never bake. Called once from MainViewModel constructor.
    /// </summary>
    private void InitNhpLedger()
    {
        Segments.CollectionChanged        += OnSegmentsChangedForNhp;
        ImportedMeshes.CollectionChanged  += OnMeshesChangedForNhp;
        LoadedOcclusions.CollectionChanged += OnOcclusionsChangedForNhp;
    }

    private void OnSegmentsChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            foreach (SegmentViewModel seg in e.NewItems)
                seg.Transform = ComposeTransforms(NhpSharedTransform, seg.LocalTransform);
    }

    private void OnMeshesChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            foreach (MeshViewModel mesh in e.NewItems)
                mesh.Transform = ComposeTransforms(NhpSharedTransform, mesh.LocalTransform);
    }

    private void OnOcclusionsChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            foreach (MeshViewModel occ in e.NewItems)
                occ.Transform = ComposeTransforms(NhpSharedTransform, occ.LocalTransform);
    }

    // ─── NHP profile lifecycle (NHP 1, NHP 2, …) ──────────────────────────────────────────

    private void InitNhpProfiles()
    {
        NhpProfiles.CollectionChanged += (_, _) => RefreshNhpProfileFlags();
        if (NhpProfiles.Count == 0)
            EnsureDefaultNhpProfile();
        else
            RefreshNhpProfileFlags();
    }

    private void RefreshNhpProfileFlags()
    {
        for (int i = 0; i < NhpProfiles.Count; i++)
            NhpProfiles[i].IsLatest = i == NhpProfiles.Count - 1;
        OnPropertyChanged(nameof(CanDeleteAnyNhpProfile));
    }

    private static int ParseDefaultNhpNumber(string name)
    {
        var m = DefaultNhpNameRegex.Match(name);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    private int GetNextNhpProfileNumber()
    {
        var max = 0;
        foreach (var p in NhpProfiles)
            max = Math.Max(max, ParseDefaultNhpNumber(p.Name));
        return Math.Max(max, NhpProfiles.Count) + 1;
    }

    private void RenumberAllNhpProfileNames()
    {
        for (int i = 0; i < NhpProfiles.Count; i++)
            NhpProfiles[i].Name = $"NHP {i + 1}";
        OnPropertyChanged(nameof(ActiveNhpProfileName));
    }

    private void HookActiveNhpProfile(NhpProfileViewModel? profile)
    {
        if (_hookedActiveProfile != null)
            _hookedActiveProfile.PropertyChanged -= ActiveNhpProfile_PropertyChanged;
        _hookedActiveProfile = profile;
        if (profile != null)
            profile.PropertyChanged += ActiveNhpProfile_PropertyChanged;
    }

    private void ActiveNhpProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NhpProfileViewModel.Name))
            OnPropertyChanged(nameof(ActiveNhpProfileName));
    }

    private void EnsureDefaultNhpProfile()
    {
        if (NhpProfiles.Count > 0) return;
        var profile = NewNhpProfileModel("NHP 1");
        profile.IsSelected = true;
        _activeNhpProfile = profile;
        NhpProfiles.Add(profile);
        HookActiveNhpProfile(profile);
        OnPropertyChanged(nameof(ActiveNhpProfileName));
    }

    private static NhpProfileViewModel NewNhpProfileModel(string name) => new()
    {
        Name = name,
        Lateral = 0, Anteroposterior = 0, Vertical = 0,
        Roll = 0, Pitch = 0, Yaw = 0
    };

    /// <summary>Record the live sliders into the active profile (the profile IS the committed pose).</summary>
    private void SaveActiveNhpProfileFromUi()
    {
        if (_activeNhpProfile == null) return;
        _activeNhpProfile.Lateral = NhpLateral;
        _activeNhpProfile.Anteroposterior = NhpAnteroposterior;
        _activeNhpProfile.Vertical = NhpVertical;
        _activeNhpProfile.Roll = NhpRoll;
        _activeNhpProfile.Pitch = NhpPitch;
        _activeNhpProfile.Yaw = NhpYaw;
    }

    private void ForceSetNhpUi(double lateral, double anteroposterior, double vertical,
        double roll, double pitch, double yaw)
    {
#pragma warning disable MVVMTK0034
        _nhpLateral = ClampNhp(lateral, false);
        _nhpAnteroposterior = ClampNhp(anteroposterior, false);
        _nhpVertical = ClampNhp(vertical, false);
        _nhpRoll = ClampNhp(roll, true);
        _nhpPitch = ClampNhp(pitch, true);
        _nhpYaw = ClampNhp(yaw, true);
#pragma warning restore MVVMTK0034

        OnPropertyChanged(nameof(NhpLateral));
        OnPropertyChanged(nameof(NhpAnteroposterior));
        OnPropertyChanged(nameof(NhpVertical));
        OnPropertyChanged(nameof(NhpRoll));
        OnPropertyChanged(nameof(NhpPitch));
        OnPropertyChanged(nameof(NhpYaw));
        OnPropertyChanged(nameof(IsNhpDirty));

        _mprDebounceTimer?.Stop();
        UpdateNhpTransform();
        UpdateAllSlices();
        SaveActiveNhpProfileFromUi();
    }

    private void ApplyNhpProfile(NhpProfileViewModel profile)
    {
        ForceSetNhpUi(profile.Lateral, profile.Anteroposterior, profile.Vertical,
            profile.Roll, profile.Pitch, profile.Yaw);
    }

    private void SetActiveNhpProfile(NhpProfileViewModel profile)
    {
        foreach (var p in NhpProfiles) p.IsSelected = false;
        profile.IsSelected = true;
        _activeNhpProfile = profile;
        HookActiveNhpProfile(profile);
        OnPropertyChanged(nameof(ActiveNhpProfileName));
        OnPropertyChanged(nameof(IsNhpDirty));
    }

    [RelayCommand]
    private void AddNhpProfile()
    {
        SaveActiveNhpProfileFromUi();
        EnsureDefaultNhpProfile();

        var profile = NewNhpProfileModel($"NHP {GetNextNhpProfileNumber()}");
        NhpProfiles.Add(profile);
        SetActiveNhpProfile(profile);

        SuppressCameraNhpSync = true;
        ForceSetNhpUi(0, 0, 0, 0, 0, 0);
        Application.Current?.Dispatcher.BeginInvoke(
            () => SuppressCameraNhpSync = false,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        StatusText = $"Working on {profile.Name}. Adjust and press DONE to commit.";
        RefreshNhpProfileFlags();
    }

    [RelayCommand]
    private void DeleteNhpProfile(NhpProfileViewModel? profile)
    {
        profile ??= _activeNhpProfile;
        if (profile == null || NhpProfiles.Count <= 1) return;

        if (MessageBox.Show(
                $"Delete {profile.Name}? This saved Natural Head Position will be permanently removed.",
                "Delete NHP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SaveActiveNhpProfileFromUi();
        var removedName = profile.Name;
        var index = NhpProfiles.IndexOf(profile);
        var wasActive = profile == _activeNhpProfile;
        NhpProfiles.Remove(profile);
        RenumberAllNhpProfileNames();

        if (wasActive)
        {
            var next = NhpProfiles[Math.Max(0, Math.Min(index, NhpProfiles.Count - 1))];
            SetActiveNhpProfile(next);
            ApplyNhpProfile(next);
            StatusText = $"Deleted {removedName}. Now editing {next.Name}.";
        }
        else
            StatusText = $"Deleted {removedName}.";

        RefreshNhpProfileFlags();
    }

    [RelayCommand]
    private void SelectNhpProfile(NhpProfileViewModel profile)
    {
        if (profile == _activeNhpProfile) return;
        SaveActiveNhpProfileFromUi();
        SetActiveNhpProfile(profile);
        ApplyNhpProfile(profile);
        StatusText = $"Loaded {profile.Name}.";
    }

    /// <summary>
    /// Restore the NHP profile set from a saved new-format project, then seed the live sliders from the
    /// active (IsSelected, else first) profile WITHOUT recomputing. The load path calls
    /// RecomputeAllTransforms later (via RefreshCombinedModel once BoneOnlyBounds is restored), which
    /// rebuilds _nhpShared from these seeded sliders. Recomputing here would hit the empty-bounds guard
    /// mid-load and toast a misleading "segment bone first" warning. IsLatest is recomputed by
    /// RefreshNhpProfileFlags (it is derived from position), so it is not restored from JSON.
    /// </summary>
    internal void RestoreNhpProfilesFromProject(IEnumerable<NhpProfileViewModel> profiles)
    {
        NhpProfiles.Clear();
        foreach (var p in profiles)
            NhpProfiles.Add(p);

        if (NhpProfiles.Count == 0)
        {
            EnsureDefaultNhpProfile();
            return;
        }

        var active = NhpProfiles.FirstOrDefault(p => p.IsSelected) ?? NhpProfiles[0];
        SetActiveNhpProfile(active);

#pragma warning disable MVVMTK0034 // direct field set during bulk restore — recompute is deferred to the load tail
        _nhpLateral         = ClampNhp(active.Lateral, false);
        _nhpAnteroposterior = ClampNhp(active.Anteroposterior, false);
        _nhpVertical        = ClampNhp(active.Vertical, false);
        _nhpRoll            = ClampNhp(active.Roll, true);
        _nhpPitch           = ClampNhp(active.Pitch, true);
        _nhpYaw             = ClampNhp(active.Yaw, true);
#pragma warning restore MVVMTK0034

        OnPropertyChanged(nameof(NhpLateral));
        OnPropertyChanged(nameof(NhpAnteroposterior));
        OnPropertyChanged(nameof(NhpVertical));
        OnPropertyChanged(nameof(NhpRoll));
        OnPropertyChanged(nameof(NhpPitch));
        OnPropertyChanged(nameof(NhpYaw));
        OnPropertyChanged(nameof(IsNhpDirty));

        RefreshNhpProfileFlags();
    }

    /// <summary>
    /// Legacy bake-model file: no NhpProfiles, just a NhpBaseline six. Build a single "NHP 1" profile
    /// from them and seed the sliders. The vertex/landmark un-bake that makes legacy files render
    /// correctly under the lazy model is Task 6 (spec §6); until then legacy files double-pose (known
    /// transient, ponytail). Signature takes the six from the load path, not the deleted fields.
    /// </summary>
    internal void MigrateBaselineToNhpProfileIfNeeded(double lat, double ant, double vert, double roll, double pitch, double yaw)
    {
        if (NhpProfiles.Count > 0) return;

        var profile = NewNhpProfileModel("NHP 1");
        profile.Lateral = lat; profile.Anteroposterior = ant; profile.Vertical = vert;
        profile.Roll = roll; profile.Pitch = pitch; profile.Yaw = yaw;
        profile.IsCommitted = Math.Abs(lat) > 0.01 || Math.Abs(ant) > 0.01 || Math.Abs(vert) > 0.01
            || Math.Abs(roll) > 0.01 || Math.Abs(pitch) > 0.01 || Math.Abs(yaw) > 0.01;

        NhpProfiles.Add(profile);
        SetActiveNhpProfile(profile);

#pragma warning disable MVVMTK0034
        _nhpLateral         = ClampNhp(lat, false);
        _nhpAnteroposterior = ClampNhp(ant, false);
        _nhpVertical        = ClampNhp(vert, false);
        _nhpRoll            = ClampNhp(roll, true);
        _nhpPitch           = ClampNhp(pitch, true);
        _nhpYaw             = ClampNhp(yaw, true);
#pragma warning restore MVVMTK0034

        OnPropertyChanged(nameof(NhpLateral));
        OnPropertyChanged(nameof(NhpAnteroposterior));
        OnPropertyChanged(nameof(NhpVertical));
        OnPropertyChanged(nameof(NhpRoll));
        OnPropertyChanged(nameof(NhpPitch));
        OnPropertyChanged(nameof(NhpYaw));
        OnPropertyChanged(nameof(IsNhpDirty));

        RefreshNhpProfileFlags();
    }
}
