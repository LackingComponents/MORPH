using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

public enum PhotogrammetryToolMode
{
    Pan,       // default: left-drag to pan, scroll to zoom
    Normalize, // draw a line, enter its mm length → sets PixelsPerMm
    Horizon,   // draw a line between two points → levels image
    Measure,   // draw a line, read distance in mm
    DrawLine,  // draw a permanent annotation line
    Angle      // draw a line, read its angle from horizontal
}

public partial class PhotogrammetryViewModel : ObservableObject
{
    public ObservableCollection<PhotoViewModel> Photos { get; } = new();

    [ObservableProperty] private PhotoViewModel? _activePhoto;
    [ObservableProperty] private PhotogrammetryToolMode _activeTool = PhotogrammetryToolMode.Pan;
    [ObservableProperty] private bool _showGridOverlay = false;

    [RelayCommand]
    private void LoadPhotos()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Photos",
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.webp"
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            // Avoid duplicates
            if (Photos.Any(p => p.FilePath == file)) continue;

            var photoVm = new PhotoViewModel(file);
            Photos.Add(photoVm);
        }

        // Select first photo if none selected
        if (ActivePhoto == null && Photos.Count > 0)
            ActivePhoto = Photos[0];
    }

    [RelayCommand]
    private void SetActivePhoto(PhotoViewModel photo)
    {
        if (ActivePhoto != null) ActivePhoto.IsSelected = false;
        ActivePhoto = photo;
        if (ActivePhoto != null) ActivePhoto.IsSelected = true;
    }

    [RelayCommand]
    private void RemovePhoto(PhotoViewModel photo)
    {
        if (photo == null) return;
        int idx = Photos.IndexOf(photo);
        Photos.Remove(photo);
        if (ActivePhoto == photo)
            ActivePhoto = Photos.Count > 0 ? Photos[Math.Max(0, idx - 1)] : null;
    }

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        ActiveTool = toolName switch
        {
            "Normalize" => PhotogrammetryToolMode.Normalize,
            "Horizon"   => PhotogrammetryToolMode.Horizon,
            "Measure"   => PhotogrammetryToolMode.Measure,
            "DrawLine"  => PhotogrammetryToolMode.DrawLine,
            "Angle"     => PhotogrammetryToolMode.Angle,
            _           => PhotogrammetryToolMode.Pan,
        };
    }
}
