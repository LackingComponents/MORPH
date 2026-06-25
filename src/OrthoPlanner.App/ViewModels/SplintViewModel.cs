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
    /// Also detects whether fallback segments carry dental anatomy (fused CT+dental
    /// via clean-and-merge, or osteotomy segments with "LeFort"/"Separated") to
    /// suppress false CT-bone warnings.
    /// </summary>
    private (float[]? upper, float[]? lower, bool upperHasDental, bool lowerHasDental) ResolveDentalMeshes()
    {
        // ── UPPER (maxilla / upper dental cast) ─────────────────────────────
        var upperScan = ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Upper && m.Vertices != null)?.Vertices;
        // Also check hidden scans — if a scan exists (even hidden), the segment it was merged into has dental anatomy
        bool anyUpperScanExists = ImportedMeshes.Any(m => m.ScanType == DentalScanType.Upper);
        SegmentViewModel? upperSeg = null;
        float[]? upper =
            upperScan
            ?? (upperSeg = Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla (LeFort 1 Separated)")))?.Vertices
            ?? (upperSeg = Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Maxilla")))?.Vertices
            ?? (upperSeg = Segments.FirstOrDefault(s => s.IsVisible && s.Name == "Cranium (Split)"))?.Vertices;

        // ── LOWER (mandible / lower dental cast) ────────────────────────────
        var lowerScan = ImportedMeshes.FirstOrDefault(m => m.ScanType == DentalScanType.Lower && m.Vertices != null)?.Vertices;
        bool anyLowerScanExists = ImportedMeshes.Any(m => m.ScanType == DentalScanType.Lower);
        SegmentViewModel? lowerSeg = null;
        float[]? lower =
            lowerScan
            ?? (lowerSeg = Segments.FirstOrDefault(s => s.IsVisible && s.Name.Contains("Mandible")))?.Vertices;

        // Determine dental-anatomy quality:
        //  - Intraoral scans always have dental anatomy
        //  - Segments that went through clean-and-merge have dental anatomy (HasMergedDental flag)
        //  - If a hidden Upper/Lower scan exists, it was merged into the segment at some point
        //  - Osteotomized segments with "LeFort" or "Separated" carry dental surfaces
        bool upperHasDental = upperScan != null
            || anyUpperScanExists
            || (upperSeg?.HasMergedDental == true)
            || (upperSeg?.Name.Contains("LeFort") == true)
            || (upperSeg?.Name.Contains("Separated") == true);
        bool lowerHasDental = lowerScan != null
            || anyLowerScanExists
            || (lowerSeg?.HasMergedDental == true)
            || (lowerSeg?.Name.Contains("Separated") == true);

        return (upper, lower, upperHasDental, lowerHasDental);
    }

    [RelayCommand]
    private void OpenSplintPlanner()
    {
        try
        {
            var (upper, lower, upperHasDental, lowerHasDental) = ResolveDentalMeshes();

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

            // Step 3: warn only when a mesh truly lacks dental anatomy
            // (i.e., it's raw CT bone, not a fused CT+dental osteotomy segment).
            bool fromScans = upperHasDental && lowerHasDental;
            if (!fromScans)
            {
                var src = new System.Text.StringBuilder();
                if (!upperHasDental) src.AppendLine("• Upper arch is using CT-segmented bone, not a registered intraoral scan.");
                if (!lowerHasDental) src.AppendLine("• Lower arch is using CT-segmented bone, not a registered intraoral scan.");
                var choice = MessageBox.Show(
                    "For a clinically accurate splint, both arches should come from registered "
                    + "intraoral scans (classified Upper / Lower):\n\n" + src.ToString()
                    + "\nCT bone lacks true crown anatomy, so the splint may not seat precisely.\n\n"
                    + "Continue with the CT-bone fallback anyway?",
                    "Intraoral Scans Recommended",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
            }

            if ((LeftCondyleCenter == null || RightCondyleCenter == null) && !EnsureCondyleFulcrum())
                return;

            // Step 2: clinical pose/labelling is carried as config, not hard-coded.
            // (Geometry sliders are merged into this inside the window.)
            OrthoPlanner.Core.Geometry.CondyleBox? leftCondyleBox = null;
            OrthoPlanner.Core.Geometry.CondyleBox? rightCondyleBox = null;
            if (LeftCondyleCenter is { } lc && LeftCondyleHalfExtents is { } lhe)
            {
                leftCondyleBox = new OrthoPlanner.Core.Geometry.CondyleBox(
                    (float)lc.X, (float)lc.Y, (float)lc.Z,
                    (float)lhe.X, (float)lhe.Y, (float)lhe.Z);
            }
            if (RightCondyleCenter is { } rc && RightCondyleHalfExtents is { } rhe)
            {
                rightCondyleBox = new OrthoPlanner.Core.Geometry.CondyleBox(
                    (float)rc.X, (float)rc.Y, (float)rc.Z,
                    (float)rhe.X, (float)rhe.Y, (float)rhe.Z);
            }

            // ── Step 0: mandibular autorotation (open the bite about the condylar axis) ──
            // The mandible is opened about the condyle-center hinge in BOTH maxilla-first
            // and mandible-first plans (clearance is what the wafer needs); the surgical
            // labelling is chosen separately in the splint planner.
            float[] lowerForSplint = lower;
            double manualOpenDegrees = 0;
            if (LeftCondyleCenter is { } lcC && RightCondyleCenter is { } rcC)
            {
                var autoWin = new MandibleAutorotationWindow(upper, lower, lcC, rcC)
                {
                    Owner = Application.Current.MainWindow
                };
                autoWin.ShowDialog();
                if (!autoWin.Accepted)
                    return; // surgeon backed out of the whole splint flow
                if (autoWin.RotatedMandible != null && autoWin.RotatedMandible.Length >= 9)
                    lowerForSplint = autoWin.RotatedMandible;
                manualOpenDegrees = autoWin.OpenDegrees;
            }
            bool manualOpenApplied = Math.Abs(manualOpenDegrees) > 0.01;

            var config = new OrthoPlanner.Core.Geometry.SplintConfig
            {
                Type               = OrthoPlanner.Core.Geometry.SplintType.Final,
                FirstOperated      = OrthoPlanner.Core.Geometry.MobileJaw.Maxilla,
                Scope              = OrthoPlanner.Core.Geometry.JawScope.Bimaxillary,
                FromIntraoralScans = fromScans,
                LeftCondyleBox     = leftCondyleBox,
                RightCondyleBox    = rightCondyleBox,
                // If the surgeon already opened the bite manually, respect that pose and
                // skip the engine's automatic opening; otherwise keep it as a safety net.
                EnableAutorotation = !manualOpenApplied,
            };

            var win = new SplintPlannerWindow(upper, lowerForSplint, this, config);
            win.Owner = Application.Current.MainWindow;

            // Use Closed event so result is read after the window fully shuts down
            win.Closed += (_, _) =>
            {
                try
                {
                    if (!win.Accepted || win.SplintVertices == null || win.SplintVertices.Length < 9)
                        return;

                    var verts = win.SplintVertices;
                    var labelledConfig = config with { FirstOperated = win.ChosenFirstOperated };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var splintMesh = new MeshViewModel
                        {
                            Name              = labelledConfig.DisplayName,
                            Vertices          = verts,
                            NhpBaked          = true,  // vertices already in NHP space (generated from NHP-baked dental meshes)
                            ColorR            = 200,
                            ColorG            = 230,
                            ColorB            = 255,
                            IsVisible         = true,
                            ShowInModelsPanel = true
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
