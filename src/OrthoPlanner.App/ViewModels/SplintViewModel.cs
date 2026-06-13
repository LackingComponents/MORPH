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
    private (float[]? upper, float[]? lower, bool upperFromScan, bool lowerFromScan) ResolveDentalMeshes()
    {
        // ── UPPER (maxilla / upper dental cast) ─────────────────────────────
        // A registered intraoral scan (classified Upper) is the clinical-grade
        // surface. Everything below it is a CT-bone fallback (flagged, not blocked).
        var upperScan = ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Upper && m.Vertices != null)?.Vertices;
        float[]? upper =
            upperScan
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla (LeFort 1 Separated)"))?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla"))?.Vertices
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name == "Cranium (Split)")?.Vertices;

        // ── LOWER (mandible / lower dental cast) ────────────────────────────
        var lowerScan = ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Lower && m.Vertices != null)?.Vertices;
        float[]? lower =
            lowerScan
            ?? Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Mandible"))?.Vertices;

        return (upper, lower, upperScan != null, lowerScan != null);
    }

    [RelayCommand]
    private void OpenSplintPlanner()
    {
        try
        {
            var (upper, lower, upperFromScan, lowerFromScan) = ResolveDentalMeshes();

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

            // Step 3: registered intraoral scans are the clinical surface. Warn — but
            // don't block — when either arch falls back to CT-segmented bone.
            bool fromScans = upperFromScan && lowerFromScan;
            if (!fromScans)
            {
                var src = new System.Text.StringBuilder();
                if (!upperFromScan) src.AppendLine("• Upper arch is using CT-segmented bone, not a registered intraoral scan.");
                if (!lowerFromScan) src.AppendLine("• Lower arch is using CT-segmented bone, not a registered intraoral scan.");
                var choice = MessageBox.Show(
                    "For a clinically accurate splint, both arches should come from registered "
                    + "intraoral scans (classified Upper / Lower):\n\n" + src.ToString()
                    + "\nCT bone lacks true crown anatomy, so the splint may not seat precisely.\n\n"
                    + "Continue with the CT-bone fallback anyway?",
                    "Intraoral Scans Recommended",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
            }

            // Step 2: clinical pose/labelling is carried as config, not hard-coded.
            // (Geometry sliders are merged into this inside the window.)
            var config = new OrthoPlanner.Core.Geometry.SplintConfig
            {
                Type               = OrthoPlanner.Core.Geometry.SplintType.Final,
                FirstOperated      = OrthoPlanner.Core.Geometry.MobileJaw.Maxilla,
                Scope              = OrthoPlanner.Core.Geometry.JawScope.Bimaxillary,
                FromIntraoralScans = fromScans,
            };

            var win = new SplintPlannerWindow(upper, lower, this, config);
            win.Owner = Application.Current.MainWindow;

            // Use Closed event so result is read after the window fully shuts down
            win.Closed += (_, _) =>
            {
                try
                {
                    if (!win.Accepted || win.SplintVertices == null || win.SplintVertices.Length < 9)
                        return;

                    var verts = win.SplintVertices;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var splintMesh = new MeshViewModel
                        {
                            Name      = config.DisplayName,
                            Vertices  = verts,
                            ColorR    = 200,
                            ColorG    = 230,
                            ColorB    = 255,
                            IsVisible = true
                        };
                        splintMesh.OnVisibilityChanged = RefreshCombinedModel;
                        splintMesh.BuildModel();
                        ImportedMeshes.Add(splintMesh);
                        RefreshCombinedModel();
                        StatusText = $"Splint generated — {verts.Length / 9:N0} triangles.";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to add splint mesh:\n{ex.Message}",
                            "Splint Error", MessageBoxButton.OK, MessageBoxImage.Error));
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
