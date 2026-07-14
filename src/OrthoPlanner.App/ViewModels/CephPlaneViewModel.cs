using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OrthoPlanner.Core.Imaging;

namespace OrthoPlanner.App.ViewModels;

/// <summary>
/// Adapter that exposes cephalometric planes to the bottom 3D models panel.
/// It mirrors the same color/opacity/visibility surface used by 3D models.
/// </summary>
public partial class CephPlaneViewModel : ObservableObject
{
    private readonly Action<CephPlaneViewModel>? _changed;
    private readonly Action<CephPlaneViewModel>? _deleteRequested;

    public CephPlaneViewModel(
        CephMeasurement source,
        Action<CephPlaneViewModel>? changed = null,
        Action<CephPlaneViewModel>? deleteRequested = null)
    {
        Source = source;
        _name = source.Label;
        _isVisible = source.IsVisible;
        _colorR = source.ColorR;
        _colorG = source.ColorG;
        _colorB = source.ColorB;
        _opacity = Math.Clamp(source.Opacity, 0.0, 1.0);
        _changed = changed;
        _deleteRequested = deleteRequested;
    }

    public CephMeasurement Source { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private byte _colorR;
    [ObservableProperty] private byte _colorG;
    [ObservableProperty] private byte _colorB;

    public Brush DisplayColorBrush =>
        new SolidColorBrush(Color.FromRgb(ColorR, ColorG, ColorB));

    private double _opacity = 1.0;
    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref _opacity, clamped))
            {
                Source.Opacity = clamped;
                OnPropertyChanged(nameof(OpacityPercent));
                _changed?.Invoke(this);
            }
        }
    }

    public double OpacityPercent
    {
        get => Opacity * 100.0;
        set => Opacity = Math.Clamp(value, 0.0, 100.0) / 100.0;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        Source.IsVisible = value;
        _changed?.Invoke(this);
    }

    partial void OnColorRChanged(byte value) => ApplyColorChange();
    partial void OnColorGChanged(byte value) => ApplyColorChange();
    partial void OnColorBChanged(byte value) => ApplyColorChange();

    private void ApplyColorChange()
    {
        Source.ColorR = ColorR;
        Source.ColorG = ColorG;
        Source.ColorB = ColorB;
        OnPropertyChanged(nameof(DisplayColorBrush));
        _changed?.Invoke(this);
    }

    public void RequestDelete() => _deleteRequested?.Invoke(this);
}
