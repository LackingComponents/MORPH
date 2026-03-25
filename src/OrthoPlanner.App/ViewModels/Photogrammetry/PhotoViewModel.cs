using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

public enum PhotoCategory
{
    Uncategorized, Frontal, Profile, ThreeQuarter, Intraoral, Occlusal
}

public enum PhotoExpression
{
    Uncategorized, Neutral, Smile, Dynamic
}

/// <summary>A calibrated distance measurement.</summary>
public class MeasurementViewModel : ObservableObject
{
    private string _name = string.Empty;
    public string Name        { get => _name;        set => SetProperty(ref _name, value); }
    private double _distanceMm;
    public double DistanceMm  { get => _distanceMm;  set => SetProperty(ref _distanceMm, value); }

    // Stored in image-logical pixels
    public Point StartPoint { get; set; }
    public Point EndPoint   { get; set; }
}

/// <summary>A plain annotation line (no distance label).</summary>
public class LineAnnotationViewModel
{
    public Point StartPoint { get; set; }   // image pixels
    public Point EndPoint   { get; set; }
    public string Color     { get; set; } = "#FFFFCC00"; // amber by default
}

/// <summary>
/// An angle annotation — two user-drawn lines; AngleDeg is the angle between them.
/// </summary>
public class AngleAnnotationViewModel : ObservableObject
{
    private string _name = string.Empty;
    public string Name     { get => _name;    set => SetProperty(ref _name, value); }
    private double _angleDeg;
    public double AngleDeg { get => _angleDeg; set => SetProperty(ref _angleDeg, value); }

    // Line 1 endpoints (image pixels)
    public Point L1Start { get; set; }
    public Point L1End   { get; set; }
    // Line 2 endpoints (image pixels)
    public Point L2Start { get; set; }
    public Point L2End   { get; set; }
}

public partial class PhotoViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath  = string.Empty;
    [ObservableProperty] private string _fileName  = string.Empty;
    [ObservableProperty] private BitmapSource? _imageSource;

    [ObservableProperty] private PhotoCategory  _category   = PhotoCategory.Uncategorized;
    [ObservableProperty] private PhotoExpression _expression = PhotoExpression.Uncategorized;

    // Index-based bindings for ComboBox SelectedIndex
    public int CategoryIndex
    {
        get => (int)Category;
        set { Category = (PhotoCategory)value; OnPropertyChanged(); }
    }
    public int ExpressionIndex
    {
        get => (int)Expression;
        set { Expression = (PhotoExpression)value; OnPropertyChanged(); }
    }

    // --- Viewport state (owned by View, stored here for photo-switching) ---
    [ObservableProperty] private double _offsetX   = 0;
    [ObservableProperty] private double _offsetY   = 0;
    [ObservableProperty] private double _zoomScale = 1.0;

    // Thumbnail / selection
    [ObservableProperty] private bool  _isSelected = false;

    // Visual rotation (degrees, applied as RenderTransform - also rotates thumbnail)
    [ObservableProperty] private double _rotationAngle = 0;

    // Scale calibration
    [ObservableProperty] private double _pixelsPerMm = 0;
    [ObservableProperty] private bool   _isNormalized = false;

    // Annotation collections (rendered by code-behind on demand)
    public ObservableCollection<MeasurementViewModel>    Measurements     { get; } = new();
    public ObservableCollection<LineAnnotationViewModel> LineAnnotations  { get; } = new();
    public ObservableCollection<AngleAnnotationViewModel> AngleAnnotations { get; } = new();

    public PhotoViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        LoadImage();
    }

    private void LoadImage()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource     = new Uri(FilePath);
            bitmap.CacheOption   = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            // Cap to 4096px wide to keep DSLR files memory-safe
            bitmap.DecodePixelWidth = 4096;
            bitmap.EndInit();
            bitmap.Freeze();
            ImageSource = bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PhotoViewModel.LoadImage: {ex.Message}");
            ImageSource = null;
        }
    }

    public void NormalizeScale(double pixelDistance, double mmDistance)
    {
        if (pixelDistance <= 0 || mmDistance <= 0) return;
        PixelsPerMm = pixelDistance / mmDistance;
        IsNormalized = true;
        // Recompute existing measurements
        foreach (var m in Measurements)
        {
            double dx = m.EndPoint.X - m.StartPoint.X;
            double dy = m.EndPoint.Y - m.StartPoint.Y;
            m.DistanceMm = Math.Sqrt(dx * dx + dy * dy) / PixelsPerMm;
        }
    }

    [RelayCommand]
    public void Rotate90() => RotationAngle = (RotationAngle + 90.0) % 360.0;

    [RelayCommand]
    public void ResetView()
    {
        ZoomScale = 0;   // sentinel: view will re-fit
        OffsetX   = 0;
        OffsetY   = 0;
    }

    [RelayCommand]
    public void ClearAnnotations()
    {
        Measurements.Clear();
        LineAnnotations.Clear();
        AngleAnnotations.Clear();
    }
}
