using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Headlamp direction (updated by MainWindow as camera moves) ÔöÇÔöÇÔöÇ
    [ObservableProperty] private System.Windows.Media.Media3D.Vector3D _headlampDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);

    // ÔöÇÔöÇÔöÇ 3D Viewport Anchors ÔöÇÔöÇÔöÇ
    [ObservableProperty] private System.Windows.Media.Media3D.Point3D _modelCenter = new System.Windows.Media.Media3D.Point3D(0, 0, 0);

    [ObservableProperty] private HelixToolkit.SharpDX.Geometry3D? _geometry;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material? _material;
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    // ÔöÇÔöÇÔöÇ Viewport toggles ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _isOrthographic;
    [ObservableProperty] private bool _showGrid;

    // ÔöÇÔöÇÔöÇ MPR toggles ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _showCrosshairs = true;
    [ObservableProperty] private int _enlargedView; // 0=none, 1=axial, 2=coronal, 3=sagittal
    [ObservableProperty] private int _rightPanelTabIndex = 0; // 0=CT, 1=Measurements, 2=Surgery

    // ÔöÇÔöÇÔöÇ Environment Lighting ÔöÇÔöÇÔöÇ
    [ObservableProperty] private byte _frontLightIntensity = 180;
    partial void OnFrontLightIntensityChanged(byte value) { OnPropertyChanged(nameof(FrontLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private double _frontLightZ = 0.0; // Straight frontal
    partial void OnFrontLightZChanged(double value) { OnPropertyChanged(nameof(FrontLightDirection)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _bottomLightIntensity = 100;
    partial void OnBottomLightIntensityChanged(byte value) { OnPropertyChanged(nameof(BottomLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _leftRightLightIntensity = 80;
    partial void OnLeftRightLightIntensityChanged(byte value) { OnPropertyChanged(nameof(LeftRightLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _backLightIntensity = 80;
    partial void OnBackLightIntensityChanged(byte value) { OnPropertyChanged(nameof(BackLightColor)); RefreshCombinedModel(); }

    // Color properties for XAML Binding
    public Color AmbientLightColor => Color.FromRgb(30, 30, 35);
    public Color FrontLightColor => Color.FromRgb(FrontLightIntensity, FrontLightIntensity, FrontLightIntensity);
    public Color BottomLightColor => Color.FromRgb(BottomLightIntensity, BottomLightIntensity, BottomLightIntensity);
    public Color LeftRightLightColor => Color.FromRgb(LeftRightLightIntensity, LeftRightLightIntensity, LeftRightLightIntensity);
    public Color BackLightColor => Color.FromRgb(BackLightIntensity, BackLightIntensity, BackLightIntensity);
    public Vector3D FrontLightDirection => new Vector3D(0, 1, FrontLightZ);

    [RelayCommand]
    private void OpenLightingConfig()
    {
        var window = new LightingWindow(this)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.Show();
    }

    // ÔöÇÔöÇÔöÇ Cephalometry ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _isCephalometryOpen;
    [ObservableProperty] private bool _showCephLandmarksIn3D;
}
