using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Resolves the best available upper and lower dental meshes.
    /// Priority: aligned ImportedMeshes (ScanType Upper/Lower) → Split bone segments.
    /// </summary>
    private (float[]? upper, float[]? lower) ResolveDentalMeshes()
    {
        float[]? upper = ImportedMeshes
            .FirstOrDefault(m => m.ScanType == DentalScanType.Upper && m.Vertices != null)?.Vertices;
        float[]? lower = ImportedMeshes
            .FirstOrDefault(m => m.ScanType == DentalScanType.Lower && m.Vertices != null)?.Vertices;

        // Fall back to split cranium (maxilla) and mandible bone segments
        if (upper == null)
            upper = Segments.FirstOrDefault(s =>
                s.IsVisible && (s.Name.Contains("Maxilla") || s.Name.Contains("Cranium (LeFort Upper)")))?.Vertices
                ?? HardTissueModel?.Vertices;

        if (lower == null)
            lower = Segments.FirstOrDefault(s =>
                s.IsVisible && s.Name.Contains("Mandible"))?.Vertices
                ?? HardTissueModel?.Vertices;

        return (upper, lower);
    }

    [RelayCommand]
    private void OpenSplintPlanner()
    {
        try
        {
            var (upper, lower) = ResolveDentalMeshes();

            if (upper == null || lower == null)
            {
                MessageBox.Show(
                    "Splint generation requires at least an upper and a lower dental model.\n\n" +
                    "Please import and classify dental STL casts (Upper / Lower) first, " +
                    "or run segmentation and split the cranium from the mandible.",
                    "Missing Dental Models",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var win = new SplintPlannerWindow(upper, lower, this);
            win.Owner = Application.Current.MainWindow;

            // Use Closed event so result is read after the window fully shuts down
            win.Closed += (_, _) =>
            {
                if (win.Accepted && win.SplintVertices != null && win.SplintVertices.Length >= 9)
                {
                    var splintMesh = new MeshViewModel
                    {
                        Name      = "Splint (Final Occlusion)",
                        Vertices  = win.SplintVertices,
                        ColorR    = 200,
                        ColorG    = 230,
                        ColorB    = 255,
                        IsVisible = true
                    };
                    splintMesh.OnVisibilityChanged = RefreshCombinedModel;
                    splintMesh.BuildModel();
                    ImportedMeshes.Add(splintMesh);
                    RefreshCombinedModel();
                    StatusText = $"Splint generated — {win.SplintVertices.Length / 9:N0} triangles.";
                }
            };

            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Splint Planner:\n{ex.Message}\n\n{ex.StackTrace}",
                "Splint Planner Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
