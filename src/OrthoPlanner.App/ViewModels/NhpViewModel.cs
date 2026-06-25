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

    // ━━━ NHP Transform State ━━━
    // _nhpTransform: the DELTA from committed baseline (what is visually applied as a preview).
    // When committed, this is Identity. Applied on top of already-baked vertices.
    private Transform3D _nhpTransform = Transform3D.Identity;

    // _cumulativeNhpMatrix: product of ALL committed NHP deltas since DICOM load.
    // Used by MPR (cumulative × delta = total transform from DICOM space).
    // CORRECT multiplication order: _cumulativeNhpMatrix = _cumulativeNhpMatrix * delta
    private Matrix3D _cumulativeNhpMatrix = Matrix3D.Identity;

    public Rect3D BoneOnlyBounds { get; private set; } = Rect3D.Empty; // Bone segment bounds only

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
    /// Commit NHP: bake the current delta into all mesh vertices and landmarks,
    /// update the cumulative matrix, and reset the delta to Identity.
    /// After commit, _nhpTransform = Identity; everything lives in baked NHP space.
    /// </summary>
    [RelayCommand]
    private void CommitNhp()
    {
        if (BoneOnlyBounds.IsEmpty) { StatusText = "⚠ Segment bone first to enable NHP commit"; return; }

        // 1. Build the delta matrix (current slider values - baseline)
        var deltaMatrix = BuildNhpMatrix(
            NhpLateral - _cLat, NhpAnteroposterior - _cAnt, NhpVertical - _cVert,
            NhpRoll - _cRoll, NhpPitch - _cPitch, NhpYaw - _cYaw);

        // 2. Snapshot for undo BEFORE mutating vertices
        SaveStateForUndo();

        // 3. Bake delta into all mesh vertices (with dedup for named models)
        var baked = new HashSet<SegmentViewModel>();
        foreach (var seg in Segments)
        {
            if (seg.Vertices != null) BakeTransformIntoVertices(seg.Vertices, deltaMatrix);
            seg.BuildModel();
            baked.Add(seg);
        }
        void BakeNamedSeg(SegmentViewModel? s) { if (s != null && !baked.Contains(s) && s.Vertices != null) { BakeTransformIntoVertices(s.Vertices, deltaMatrix); s.BuildModel(); baked.Add(s); } }
        BakeNamedSeg(HardTissueModel);
        BakeNamedSeg(SoftTissueModel);
        BakeNamedSeg(DentalModel);

        foreach (var mesh in ImportedMeshes)
            if (mesh.Vertices != null) { BakeTransformIntoVertices(mesh.Vertices, deltaMatrix); mesh.BuildModel(); }

        foreach (var occ in LoadedOcclusions)
            if (occ.Vertices != null) { BakeTransformIntoVertices(occ.Vertices, deltaMatrix); occ.BuildModel(); }

        // 4. Bake anatomical landmarks
        DentalMidlinePoint = TransformTuple(DentalMidlinePoint, deltaMatrix);
        LeftCondyleCenter  = TransformTuple(LeftCondyleCenter, deltaMatrix);
        RightCondyleCenter = TransformTuple(RightCondyleCenter, deltaMatrix);

        // 5. Bake VolumePivot (rotation center must move with the baked space)
        if (VolumePivot.HasValue)
            VolumePivot = deltaMatrix.Transform(VolumePivot.Value);

        // 6. Bake cephalometric 3D coordinates
        if (SavedCephLandmarks.Count > 0)
        {
            var updatedLandmarks = new List<CephLandmarkSave>(SavedCephLandmarks.Count);
            foreach (var lm in SavedCephLandmarks)
            {
                if (lm.X3D == null || lm.Y3D == null || lm.Z3D == null)
                    updatedLandmarks.Add(lm);
                else
                {
                    var p = deltaMatrix.Transform(new Point3D(lm.X3D.Value, lm.Y3D.Value, lm.Z3D.Value));
                    updatedLandmarks.Add(new CephLandmarkSave(lm.Name, lm.X2D, lm.Y2D, p.X, p.Y, p.Z));
                }
            }
            SavedCephLandmarks = updatedLandmarks;
        }

        // 7. Update cumulative matrix: CORRECT ORDER — cumulative first, then delta
        _cumulativeNhpMatrix = _cumulativeNhpMatrix * deltaMatrix;

        // 8. New baseline = current slider values
        _cLat = NhpLateral; _cAnt = NhpAnteroposterior; _cVert = NhpVertical;
        _cRoll = NhpRoll;   _cPitch = NhpPitch;         _cYaw  = NhpYaw;

        // 9. Delta = 0 → _nhpTransform becomes Identity
        _nhpTransform = Transform3D.Identity;

        // 10. Re-apply transforms and refresh
        OnPropertyChanged(nameof(IsNhpDirty));
        ApplyNhpToAllTrackedObjects();
        RefreshCombinedModel();
        UpdateAllSlices();
        StatusText = "NHP committed and baked into geometry.";
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
    /// Builds the NHP delta transform matrix from delta values centered on VolumePivot.
    /// </summary>
    private Matrix3D BuildNhpMatrix(double dLat, double dAnt, double dVert,
        double dRoll, double dPitch, double dYaw)
    {
        var center = VolumePivot ?? new Point3D(
            BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
            BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
            BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);

        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));
        return nhp.Value;
    }

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

        // Build the DELTA transform from committed baseline
        var deltaMatrix = BuildNhpMatrix(
            NhpLateral - _cLat, NhpAnteroposterior - _cAnt, NhpVertical - _cVert,
            NhpRoll - _cRoll, NhpPitch - _cPitch, NhpYaw - _cYaw);

        _nhpTransform = new MatrixTransform3D(deltaMatrix);

        ApplyNhpToAllTrackedObjects();

        // ModelCenter follows the delta-transformed pivot
        var center = VolumePivot ?? new Point3D(
            BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
            BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
            BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
        ModelCenter = deltaMatrix.Transform(center);
    }

    /// <summary>
    /// Applies the current NHP delta transform to ALL tracked objects.
    /// Called when sliders change (preview mode) or after commit (Identity = no-op).
    /// </summary>
    private void ApplyNhpToAllTrackedObjects()
    {
        if (HardTissueModel != null) HardTissueModel.Transform = _nhpTransform;
        if (SoftTissueModel != null) SoftTissueModel.Transform = _nhpTransform;
        if (DentalModel     != null) DentalModel.Transform     = _nhpTransform;

        foreach (var seg  in Segments)       seg.Transform  = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = _nhpTransform;
        foreach (var occ  in LoadedOcclusions) occ.Transform = _nhpTransform;
    }

    /// <summary>
    /// NHP Ledger: Wire up collection-changed handlers so new objects
    /// automatically receive the current NHP transform on addition.
    /// Called once from MainViewModel constructor.
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
        {
            foreach (SegmentViewModel seg in e.NewItems)
            {
                // Determine if this segment's vertices are already in NHP-baked space:
                //  1. DerivedFrom lineage: parent is baked → child inherits (osteotomy children)
                //  2. Direct NhpBaked flag (set by undo/restore or prior ledger pass)
                bool alreadyBaked = seg.NhpBaked || (seg.DerivedFrom?.NhpBaked == true);

                // Bake cumulative NHP into fresh DICOM-space vertices only.
                // Skip if: globally suppressed (undo/restore), or already in baked space.
                if (!SuppressLedgerBake && !alreadyBaked && seg.Vertices != null && !_cumulativeNhpMatrix.IsIdentity)
                {
                    BakeTransformIntoVertices(seg.Vertices, _cumulativeNhpMatrix);
                    seg.BuildModel();
                }
                seg.NhpBaked = true;
                seg.Transform = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
            }
        }
    }

    private void OnMeshesChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (MeshViewModel mesh in e.NewItems)
            {
                if (!SuppressLedgerBake && !mesh.NhpBaked && mesh.Vertices != null && !_cumulativeNhpMatrix.IsIdentity)
                {
                    BakeTransformIntoVertices(mesh.Vertices, _cumulativeNhpMatrix);
                    mesh.BuildModel();
                    mesh.NhpBaked = true;
                }
                mesh.Transform = _nhpTransform;
            }
        }
    }

    private void OnOcclusionsChangedForNhp(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (MeshViewModel occ in e.NewItems)
            {
                if (!SuppressLedgerBake && !occ.NhpBaked && occ.Vertices != null && !_cumulativeNhpMatrix.IsIdentity)
                {
                    BakeTransformIntoVertices(occ.Vertices, _cumulativeNhpMatrix);
                    occ.BuildModel();
                    occ.NhpBaked = true;
                }
                occ.Transform = _nhpTransform;
            }
        }
    }

    /// <summary>
    /// Bakes a Matrix3D transform directly into a flat float[] vertex array (stride 3).
    /// Mutates vertices in-place. Always call seg.BuildModel() after.
    /// </summary>
    private static void BakeTransformIntoVertices(float[] vertices, Matrix3D matrix)
    {
        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            var p  = new Point3D(vertices[i], vertices[i + 1], vertices[i + 2]);
            var tp = matrix.Transform(p);
            vertices[i]     = (float)tp.X;
            vertices[i + 1] = (float)tp.Y;
            vertices[i + 2] = (float)tp.Z;
        }
    }

    /// <summary>Transforms a nullable (X,Y,Z) tuple by a Matrix3D.</summary>
    private static (double X, double Y, double Z)? TransformTuple(
        (double X, double Y, double Z)? pt, Matrix3D m)
    {
        if (pt == null) return null;
        var p = m.Transform(new Point3D(pt.Value.X, pt.Value.Y, pt.Value.Z));
        return (p.X, p.Y, p.Z);
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
}
