using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>Segments plus panel-listed imported meshes (e.g. splint) for the bottom 3D MODELS list.</summary>
    public ObservableCollection<object> ThreeDModelsPanelItems { get; } = new();

    private void InitializeThreeDModelsPanel()
    {
        Segments.CollectionChanged += (_, _) => RebuildThreeDModelsPanel();
        ImportedMeshes.CollectionChanged += OnImportedMeshesCollectionChanged;
        RebuildThreeDModelsPanel();
    }

    private void OnImportedMeshesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (MeshViewModel mesh in e.NewItems)
                mesh.PropertyChanged += OnPanelMeshPropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (MeshViewModel mesh in e.OldItems)
                mesh.PropertyChanged -= OnPanelMeshPropertyChanged;
        }
        RebuildThreeDModelsPanel();
    }

    private void OnPanelMeshPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeshViewModel.ShowInModelsPanel))
            RebuildThreeDModelsPanel();
    }

    private void RebuildThreeDModelsPanel()
    {
        ThreeDModelsPanelItems.Clear();
        foreach (var seg in Segments)
            ThreeDModelsPanelItems.Add(seg);
        foreach (var mesh in ImportedMeshes.Where(m => m.ShowInModelsPanel))
            ThreeDModelsPanelItems.Add(mesh);
    }

    /// <summary>Apply a palette colour to a segment or imported mesh.</summary>
    public void ApplyModelColor(object model, Color color)
    {
        SaveStateForUndo();
        switch (model)
        {
            case SegmentViewModel seg:
                seg.ColorR = color.R;
                seg.ColorG = color.G;
                seg.ColorB = color.B;
                break;
            case MeshViewModel mesh:
                mesh.ColorR = color.R;
                mesh.ColorG = color.G;
                mesh.ColorB = color.B;
                break;
        }
    }

    [RelayCommand]
    private async Task ExportSingleModelAsync(object? model)
    {
        float[]? vertices = null;
        string modelName = "Model";

        switch (model)
        {
            case SegmentViewModel seg:
                vertices = seg.Vertices;
                modelName = seg.Name;
                break;
            case MeshViewModel mesh:
                vertices = mesh.Vertices;
                modelName = mesh.Name;
                break;
        }

        if (vertices == null || vertices.Length < 9)
        {
            System.Windows.MessageBox.Show(
                "This model has no geometry to export.",
                "Export", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        string safeName = string.Join("_", modelName.Split(Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save 3D Model",
            Filter = "STL mesh (*.stl)|*.stl|Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*",
            FileName = $"{safeName}.stl",
            DefaultExt = ".stl",
            AddExtension = true,
            OverwritePrompt = true
        };

        var owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog(owner) != true) return;

        IsLoading = true;
        StatusText = $"Exporting {modelName}…";
        try
        {
            string path = dialog.FileName;
            await Task.Run(() =>
            {
                if (path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    ObjIO.SaveObj(path, vertices);
                else
                    StlIO.SaveBinaryStl(path, vertices);
            });
            StatusText = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Export failed:\n{ex.Message}",
                "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            StatusText = "Export failed.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
