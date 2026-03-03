using System;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

public enum PhotoCategory
{
    Uncategorized,
    Frontal,
    Profile,
    ThreeQuarter,
    Intraoral,
    Occlusal
}

public enum PhotoExpression
{
    Uncategorized,
    Neutral,
    Smile,
    Dynamic
}

public class MeasurementViewModel : ObservableObject
{
    private string _name;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    
    // Pixel coordinates
    private Point _startPoint;
    public Point StartPoint
    {
        get => _startPoint;
        set => SetProperty(ref _startPoint, value);
    }

    private Point _endPoint;
    public Point EndPoint
    {
        get => _endPoint;
        set => SetProperty(ref _endPoint, value);
    }

    // Reference measurement computed from PhotoViewModel's scale
    private double _distanceMm;
    public double DistanceMm
    {
        get => _distanceMm;
        set => SetProperty(ref _distanceMm, value);
    }
}

public partial class PhotoViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private BitmapImage? _imageSource;
    
    [ObservableProperty] private PhotoCategory _category = PhotoCategory.Uncategorized;
    [ObservableProperty] private PhotoExpression _expression = PhotoExpression.Uncategorized;

    // Viewport transform states for interactivity
    [ObservableProperty] private double _scale = 0; // 0 means not auto-fitted yet
    [ObservableProperty] private double _panX = 0;
    [ObservableProperty] private double _panY = 0;
    [ObservableProperty] private double _rotationAngle = 0;

    // Normalization specific logic
    [ObservableProperty] private double _pixelsPerMm = 0;
    [ObservableProperty] private bool _isNormalized = false;

    // Stored measurements
    public ObservableCollection<MeasurementViewModel> Measurements { get; } = new();

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
            bitmap.UriSource = new Uri(FilePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load entirely into memory so file is not locked
            
            // Limit to 4K resolution roughly to prevent OutOfMemoryException crashes on huge images
            bitmap.DecodePixelWidth = 3840; 
            
            bitmap.EndInit();
            bitmap.Freeze(); // Needed for thread safety
            
            ImageSource = bitmap;
        }
        catch (Exception)
        {
            // Fallback or ignore if unsupported file format
            ImageSource = null;
        }
    }

    public void NormalizeScale(double pixelDistance, double mmDistance)
    {
        if (pixelDistance > 0 && mmDistance > 0)
        {
            PixelsPerMm = pixelDistance / mmDistance;
            IsNormalized = true;
            UpdateMeasurements();
        }
    }

    public void ReHorizon(Point p1, Point p2)
    {
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;

        if (dx == 0 && dy == 0) return;

        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * (180.0 / Math.PI);

        // Adjust rotation to make it horizontal
        RotationAngle -= angleDeg;
    }

    private void UpdateMeasurements()
    {
        if (!IsNormalized || PixelsPerMm == 0) return;

        foreach (var m in Measurements)
        {
            double dx = m.EndPoint.X - m.StartPoint.X;
            double dy = m.EndPoint.Y - m.StartPoint.Y;
            double distPix = Math.Sqrt(dx * dx + dy * dy);
            m.DistanceMm = distPix / PixelsPerMm;
        }
    }
}
