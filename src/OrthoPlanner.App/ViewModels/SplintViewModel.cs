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
        // ── UPPER (maxilla / upper dental cast) ─────────────────────────────
        // Priority:
        //   1. Dental cast STL classified as Upper
        //   2. "Maxilla (LeFort 1 Separated)" — after LeFort1 osteotomy
        //   3. Any segment whose name contains "Maxilla"
        //   4. "Cranium (Split)" — condyle-split result (NOT "Cranium (LeFort Upper)")
        float[]? upper =
            ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Upper && m.Vertices != null)?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla (LeFort 1 Separated)"))?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla"))?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name == "Cranium (Split)")?.Vertices;

        // ── LOWER (mandible / lower dental cast) ────────────────────────────
        // Priority:
        //   1. Dental cast STL classified as Lower
        //   2. Any segment whose name contains "Mandible"
        float[]? lower =
            ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Lower && m.Vertices != null)?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Mandible"))?.Vertices;

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
                var missing = new System.Text.StringBuilder();
                if (upper == null) missing.AppendLine("• Upper arch: no dental cast classified as 'Upper', and no 'Maxilla' segment found.");
                if (lower == null) missing.AppendLine("• Lower arch: no dental cast classified as 'Lower', and no 'Mandible' segment found.");
                MessageBox.Show(
                    "Splint generation requires classified dental models:\n\n" +
                    missing.ToString() +
                    "\nTo fix: Import your dental STL casts and classify them as Upper / Lower " +
                    "using the STL import dialog. The full CT bone (cranium) cannot be used as the upper arch.",
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
