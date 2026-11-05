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

/// <summary>
/// NHP state of the 3D view as seen by the photo section. <see cref="Committed"/> is the NHP matrix of the active
/// (committed) profile; <see cref="IsSet"/> is false when no NHP has been committed or it is all zeros.
/// </summary>
public sealed record NhpSnapshot(bool IsSet, System.Windows.Media.Media3D.Matrix3D Committed, bool IsDirty);

public partial class PhotogrammetryViewModel : ObservableObject
{
    public ObservableCollection<PhotoViewModel> Photos { get; } = new();

    [ObservableProperty] private PhotoViewModel? _activePhoto;
    [ObservableProperty] private PhotogrammetryToolMode _activeTool = PhotogrammetryToolMode.Pan;
    [ObservableProperty] private bool _showGridOverlay = false;

    /// <summary>Gives the Photo section access to the radiograph loaded in Cephalometry (set by MainViewModel).</summary>
    public Func<Cephalometry.CephXrayImage?>? CephXrayProvider { get; set; }

    /// <summary>Gives the Photo section access to the CT soft-tissue model (skin reference; set by MainViewModel).</summary>
    public Func<SegmentViewModel?>? SoftTissueProvider { get; set; }

    /// <summary>Gives the Photo section the committed NHP of the 3D view (set by MainViewModel).</summary>
    public Func<NhpSnapshot>? NhpProvider { get; set; }

    // ── Undo / redo (Photogrammetry only; the 3D history is separate) ──
    private readonly PhotoUndoHistory _history = new();

    /// <summary>Raised after an undo/redo restored a snapshot, so the view redraws.</summary>
    public event EventHandler? HistoryRestored;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public PhotogrammetryViewModel()
    {
        // Photo commands (Rotate 90°, Clear annotations) record undo through this hook.
        Photos.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (PhotoViewModel p in e.NewItems) p.BeforeUserChange = SaveUndo;
        };
    }

    /// <summary>Call BEFORE a user change in the photo section; <paramref name="action"/> is shown on undo.</summary>
    public void SaveUndo(string action) => _history.SaveUndo(this, action);

    /// <summary>Undoes the last photo change. Returns the status-bar text.</summary>
    public string Undo()
    {
        bool restores = _history.CanUndo;
        string text = _history.Undo(this);
        if (restores) HistoryRestored?.Invoke(this, EventArgs.Empty);
        return text;
    }

    /// <summary>Redoes the last undone photo change. Returns the status-bar text.</summary>
    public string Redo()
    {
        bool restores = _history.CanRedo;
        string text = _history.Redo(this);
        if (restores) HistoryRestored?.Invoke(this, EventArgs.Empty);
        return text;
    }

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

        // Avoid duplicates
        var newFiles = dialog.FileNames.Where(f => !Photos.Any(p => p.FilePath == f)).ToList();
        if (newFiles.Count == 0) return;
        SaveUndo(newFiles.Count == 1 ? "Load photo" : $"Load {newFiles.Count} photos");

        foreach (var file in newFiles)
            Photos.Add(new PhotoViewModel(file));

        // Select first photo if none selected
        if (ActivePhoto == null && Photos.Count > 0)
            ActivePhoto = Photos[0];
    }

    /// <summary>Removes every photo (used when another project is opened).</summary>
    public void ClearPhotos()
    {
        ActivePhoto = null;
        Photos.Clear();
        _history.Clear();   // another project: its photos must not come back with Undo
    }

    /// <summary>Replaces the photo list with restored photos and selects the saved active one.</summary>
    public void RestorePhotos(IEnumerable<PhotoViewModel> photos, int activeIndex)
    {
        ClearPhotos();
        foreach (var p in photos)
        {
            p.IsSelected = false;
            Photos.Add(p);
        }
        if (Photos.Count == 0) return;
        SetActivePhoto(Photos[activeIndex >= 0 && activeIndex < Photos.Count ? activeIndex : 0]);
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
        SaveUndo($"Remove photo {photo.FileName}");
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
