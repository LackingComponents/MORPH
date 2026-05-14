using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ NHP Parameters (Live adjusted) ÔöÇÔöÇÔöÇ
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
        // Capture the uncommitted delta BEFORE locking it in as the new baseline
        double dPitch = NhpPitch - _cPitch;
        double dRoll  = NhpRoll  - _cRoll;
        double dYaw   = NhpYaw   - _cYaw;
        double dLat   = NhpLateral         - _cLat;
        double dAnt   = NhpAnteroposterior - _cAnt;
        double dVert  = NhpVertical        - _cVert;

        // Now lock in as the new committed baseline
        _cLat = NhpLateral; _cAnt = NhpAnteroposterior; _cVert = NhpVertical;
        _cRoll = NhpRoll; _cPitch = NhpPitch; _cYaw = NhpYaw;
        OnPropertyChanged(nameof(IsNhpDirty));

        // Start Reslice Engine, passing the true delta that needs to be baked
        await PerformPhysicalResliceAsync(dPitch, dRoll, dYaw, dLat, dAnt, dVert);
    }

    private void UpdateNhpTransform()
    {
        if (BoneOnlyBounds.IsEmpty) return;

        // Use bone-only bounds for camera centering (ignore imported STL meshes)
        var bounds = BoneOnlyBounds;
        var center = new Point3D(bounds.X + bounds.SizeX/2, bounds.Y + bounds.SizeY/2, bounds.Z + bounds.SizeZ/2);

        // DELTA MATH: Only visually rotate/translate by the *difference* between the current UI values
        // and the physically baked (committed) values. This prevents compounding geometry.
        var dPitch = NhpPitch - _cPitch;
        var dRoll  = NhpRoll  - _cRoll;
        var dYaw   = NhpYaw   - _cYaw;
        var dLat   = NhpLateral         - _cLat;
        var dAnt   = NhpAnteroposterior - _cAnt;
        var dVert  = NhpVertical        - _cVert;

        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));

        // Store so surgery transforms can compose on top
        _nhpTransform = nhp;

        // Apply: NHP first, then per-segment surgical offset on top
        if (HardTissueModel != null) HardTissueModel.Transform = _nhpTransform;
        if (SoftTissueModel != null) SoftTissueModel.Transform = _nhpTransform;
        if (DentalModel != null)     DentalModel.Transform     = _nhpTransform;
        foreach (var seg  in Segments)      seg.Transform  = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = _nhpTransform;

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
