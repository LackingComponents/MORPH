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

    // ÔöÇÔöÇÔöÇ NHP Committed State (Baseline) ÔöÇÔöÇÔöÇ
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

    partial void OnNhpLateralChanged(double value) { if (value != ClampNhp(value, false)) NhpLateral = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }
    partial void OnNhpAnteroposteriorChanged(double value) { if (value != ClampNhp(value, false)) NhpAnteroposterior = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }
    partial void OnNhpVerticalChanged(double value) { if (value != ClampNhp(value, false)) NhpVertical = ClampNhp(value, false); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }
    partial void OnNhpRollChanged(double value) { if (value != ClampNhp(value, true)) NhpRoll = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }
    partial void OnNhpPitchChanged(double value) { if (value != ClampNhp(value, true)) NhpPitch = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }
    partial void OnNhpYawChanged(double value) { if (value != ClampNhp(value, true)) NhpYaw = ClampNhp(value, true); else { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); UpdateAllSlices(); } }

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

    private void UpdateNhpTransform()
    {
        if (BoneOnlyBounds.IsEmpty) return;

        // Phase 0: Use the baked VolumePivot for rotation center (stable across reslices).
        // Fallback to bounds-derived center when VolumePivot has not been set yet.
        var center = VolumePivot == new Point3D(0, 0, 0)
            ? new Point3D(BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                          BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                          BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2)
            : VolumePivot;

        // TOTAL NHP transform: apply the full current NHP values (not just the delta).
        // This makes _nhpTransform the definitive orientation of all meshes in world space.
        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), NhpPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), NhpRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), NhpYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));

        _nhpTransform = nhp;

        // Apply total NHP transform to all models, then compose with per-segment surgical offset
        if (HardTissueModel != null) HardTissueModel.Transform = _nhpTransform;
        if (SoftTissueModel != null) SoftTissueModel.Transform = _nhpTransform;
        if (DentalModel != null)     DentalModel.Transform     = _nhpTransform;
        foreach (var seg  in Segments)      seg.Transform  = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = _nhpTransform;
        foreach (var occ  in LoadedOcclusions) occ.Transform = _nhpTransform;

        // Dynamically enforce the freehand rotation pivot point!
        ModelCenter = nhp.Transform(center);
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
