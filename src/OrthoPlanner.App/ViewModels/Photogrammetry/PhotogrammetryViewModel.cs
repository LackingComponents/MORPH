using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace OrthoPlanner.App.ViewModels.Photogrammetry;

public enum PhotogrammetryToolMode
{
    None,
    Normalize,
    Horizon,
    Measure
}

public partial class PhotogrammetryViewModel : ObservableObject
{
    public ObservableCollection<PhotoViewModel> Photos { get; } = new();

    [ObservableProperty] private PhotoViewModel? _activePhoto;
    
    // Tools
    [ObservableProperty] private PhotogrammetryToolMode _activeTool = PhotogrammetryToolMode.None;
    
    // Viewport State
    [ObservableProperty] private bool _showGridOverlay = false;

    // Command to load photos
    [RelayCommand]
    private void LoadPhotos()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Photos",
            Multiselect = true,
            Filter = "Images|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.heic"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var photoVm = new PhotoViewModel(file);
                if (photoVm.ImageSource != null)
                {
                    Photos.Add(photoVm);
                }
            }

            if (ActivePhoto == null && Photos.Any())
            {
                ActivePhoto = Photos.First();
            }
        }
    }

    [RelayCommand]
    private void RemovePhoto(PhotoViewModel photo)
    {
        if (photo == null) return;
        
        Photos.Remove(photo);
        if (ActivePhoto == photo)
        {
            ActivePhoto = Photos.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        switch (toolName)
        {
            case "Normalize": ActiveTool = PhotogrammetryToolMode.Normalize; break;
            case "Horizon": ActiveTool = PhotogrammetryToolMode.Horizon; break;
            case "Measure": ActiveTool = PhotogrammetryToolMode.Measure; break;
            default: ActiveTool = PhotogrammetryToolMode.None; break;
        }
    }
}
