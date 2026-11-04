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

    // Radiograph overlay for this photo (memory only, not saved in the project yet)
    [ObservableProperty] private CephOverlayState? _cephOverlay;

    // Annotation collections (rendered by code-behind on demand)
    public ObservableCollection<MeasurementViewModel>    Measurements     { get; } = new();
    public ObservableCollection<LineAnnotationViewModel> LineAnnotations  { get; } = new();
    public ObservableCollection<AngleAnnotationViewModel> AngleAnnotations { get; } = new();

    /// <summary>Maximum decoded width; larger (DSLR) files are downscaled to stay memory-safe.</summary>
    public const int MaxDecodeWidth = 4096;

    /// <summary>
    /// Exact bytes of the source file, read once at load. Saved verbatim into the project
    /// (never re-encoded) so EXIF metadata and full resolution are preserved.
    /// </summary>
    public byte[] OriginalBytes { get; } = Array.Empty<byte>();

    // Native resolution from the decoder (before the 4096 px cap). 0 if decoding failed.
    public int OriginalPixelWidth  { get; private set; }
    public int OriginalPixelHeight { get; private set; }

    /// <summary>Displayed pixel width / original pixel width. Annotations stay in displayed pixels.</summary>
    public double DecodeScale { get; private set; } = 1.0;

    /// <summary>Last load/decode error, or null. Lets callers surface failures instead of dropping them.</summary>
    public string? LoadError { get; private set; }

    public PhotoViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        try
        {
            OriginalBytes = System.IO.File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            LoadError = $"cannot read file: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"PhotoViewModel: {FileName}: {LoadError}");
            return;
        }
        LoadImage();
    }

    /// <summary>Rebuilds a photo from bytes stored in a project file.</summary>
    public PhotoViewModel(string fileName, byte[] originalBytes)
    {
        FileName = fileName;
        OriginalBytes = originalBytes ?? throw new ArgumentNullException(nameof(originalBytes));
        LoadImage();
    }

    private void LoadImage()
    {
        try
        {
            using (var probe = new System.IO.MemoryStream(OriginalBytes, writable: false))
            {
                var frame = BitmapDecoder.Create(probe, BitmapCreateOptions.IgnoreColorProfile,
                                                 BitmapCacheOption.None).Frames[0];
                OriginalPixelWidth  = frame.PixelWidth;
                OriginalPixelHeight = frame.PixelHeight;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource  = new System.IO.MemoryStream(OriginalBytes, writable: false);
            bitmap.CacheOption   = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            // Cap to 4096px wide to keep DSLR files memory-safe (never upscale smaller photos)
            if (OriginalPixelWidth > MaxDecodeWidth)
                bitmap.DecodePixelWidth = MaxDecodeWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            ImageSource = bitmap;
            DecodeScale = OriginalPixelWidth > 0 ? (double)bitmap.PixelWidth / OriginalPixelWidth : 1.0;
        }
        catch (Exception ex)
        {
            LoadError = $"cannot decode image: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"PhotoViewModel.LoadImage: {FileName}: {ex.Message}");
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
