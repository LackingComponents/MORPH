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
            // ── 1. Resolve dental meshes ─────────────────────────────────────────
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

            // ── 2. Confirm condyle fulcrum ──────────────────────────────────────
            if ((LeftCondyleCenter == null || RightCondyleCenter == null) && !EnsureCondyleFulcrum())
                return;

            // ── 3. Build condyle boxes ──────────────────────────────────────────
            OrthoPlanner.Core.Geometry.CondyleBox? leftCondyleBox  = null;
            OrthoPlanner.Core.Geometry.CondyleBox? rightCondyleBox = null;
            if (LeftCondyleCenter is { } lc && LeftCondyleHalfExtents is { } lhe)
                leftCondyleBox  = new OrthoPlanner.Core.Geometry.CondyleBox((float)lc.X,(float)lc.Y,(float)lc.Z,(float)lhe.X,(float)lhe.Y,(float)lhe.Z);
            if (RightCondyleCenter is { } rc && RightCondyleHalfExtents is { } rhe)
                rightCondyleBox = new OrthoPlanner.Core.Geometry.CondyleBox((float)rc.X,(float)rc.Y,(float)rc.Z,(float)rhe.X,(float)rhe.Y,(float)rhe.Z);

            // ── 4. Compute moved meshes via BakeToCopy (no scene mutation) ───────
            // Use the last visible Maxilla/Mandible segment and apply its SurgicalTransform.
            var maxillaSeg = Segments.LastOrDefault(s => s.IsVisible &&
                (s.Name?.Contains("Maxilla") == true || s.Name?.Contains("Cranium (Split)") == true));
            var mandibleSeg = Segments.LastOrDefault(s => s.IsVisible &&
                s.Name?.Contains("Mandible") == true && s.Name?.Contains("Cranium") != true);

            float[] upperMoved = maxillaSeg?.Vertices != null
                ? BakeToCopy(maxillaSeg.Vertices, maxillaSeg.SurgicalTransform)
                : upper;
            float[] lowerMoved = mandibleSeg?.Vertices != null
                ? BakeToCopy(mandibleSeg.Vertices, mandibleSeg.SurgicalTransform)
                : lower;

            var lcC = LeftCondyleCenter!.Value;
            var rcC = RightCondyleCenter!.Value;

            // ── 5. Screen 1: SplintSequenceWindow (sequence + intermediate autorotation) ──
            var seq1 = new SplintSequenceWindow(
                upperBase: upper, lowerBase: lower,
                upperMoved: upperMoved, lowerMoved: lowerMoved,
                leftCondyle: lcC, rightCondyle: rcC,
                isFinalOcclusion: false,
                maxillaFirstDefault: true)
            {
                Owner = Application.Current.MainWindow
            };
            seq1.ShowDialog();

            // Persist updated condyle positions if surgeon moved them
            if (seq1.UpdatedLeftCondyle.HasValue)  LeftCondyleCenter  = seq1.UpdatedLeftCondyle.Value;
            if (seq1.UpdatedRightCondyle.HasValue)  RightCondyleCenter = seq1.UpdatedRightCondyle.Value;
            lcC = LeftCondyleCenter!.Value;
            rcC = RightCondyleCenter!.Value;

            // ── 5b. Bypass → original single-splint flow ─────────────────────────
            if (seq1.BypassToOriginal)
            {
                float[] lowerForSplint = lower;
                double manualOpenDegrees = 0;
                var autoWin = new MandibleAutorotationWindow(upper, lower, lcC, rcC)
                {
                    Owner = Application.Current.MainWindow
                };
                autoWin.ShowDialog();
                if (!autoWin.Accepted) return;
                if (autoWin.RotatedMandible != null && autoWin.RotatedMandible.Length >= 9)
                    lowerForSplint = autoWin.RotatedMandible;
                manualOpenDegrees = autoWin.OpenDegrees;

                bool manualOpenApplied = Math.Abs(manualOpenDegrees) > 0.01;
                var bypassConfig = new OrthoPlanner.Core.Geometry.SplintConfig
                {
                    Type               = OrthoPlanner.Core.Geometry.SplintType.Final,
                    FirstOperated      = OrthoPlanner.Core.Geometry.MobileJaw.Maxilla,
                    Scope              = OrthoPlanner.Core.Geometry.JawScope.Bimaxillary,
                    FromIntraoralScans = fromScans,
                    LeftCondyleBox     = leftCondyleBox,
                    RightCondyleBox    = rightCondyleBox,
                    EnableAutorotation = !manualOpenApplied,
                };
                OpenSplintWindow(upper, lowerForSplint, bypassConfig);
                return;
            }

            if (!seq1.Accepted) return;

            // ── 6. Build intermediate jaw positions ──────────────────────────────
            var firstOperated = seq1.IsMaxillaFirst
                ? OrthoPlanner.Core.Geometry.MobileJaw.Maxilla
                : OrthoPlanner.Core.Geometry.MobileJaw.Mandible;

            float[] interUpper = seq1.IsMaxillaFirst ? upperMoved : upper;
            float[] interLower = seq1.RotatedMandible ?? (seq1.IsMaxillaFirst ? lower : lowerMoved);

            // ── 7. Screen 2: SplintPlannerWindow (intermediate wafer) ────────────
            var interConfig = new OrthoPlanner.Core.Geometry.SplintConfig
            {
                Type               = OrthoPlanner.Core.Geometry.SplintType.Intermediate,
                FirstOperated      = firstOperated,
                Scope              = OrthoPlanner.Core.Geometry.JawScope.Bimaxillary,
                FromIntraoralScans = fromScans,
                LeftCondyleBox     = leftCondyleBox,
                RightCondyleBox    = rightCondyleBox,
                EnableAutorotation = false, // applied manually above
            };

            bool intermediateDone = false;
            var interWin = new SplintPlannerWindow(interUpper, interLower, this, interConfig);
            interWin.Owner = Application.Current.MainWindow;
            interWin.Closed += (_, _) =>
            {
                try
                {
                    if (!interWin.Accepted || interWin.SplintVertices == null || interWin.SplintVertices.Length < 9)
                        return;
                    var labelledConfig = interConfig with { FirstOperated = interWin.ChosenFirstOperated };
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AddSplintMeshToScene(interWin.SplintVertices, labelledConfig);
                        intermediateDone = true;
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to add intermediate splint:\n{ex.Message}",
                            "Splint Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            };
            interWin.ShowDialog();

            if (!intermediateDone) return; // surgeon cancelled

            // ── 8. Screen 3: SplintSequenceWindow (final occlusion autorotation) ──
            var seq3 = new SplintSequenceWindow(
                upperBase: upper, lowerBase: lower,
                upperMoved: upperMoved, lowerMoved: lowerMoved,
                leftCondyle: lcC, rightCondyle: rcC,
                isFinalOcclusion: true,
                maxillaFirstDefault: seq1.IsMaxillaFirst)
            {
                Owner = Application.Current.MainWindow
            };
            seq3.ShowDialog();

            if (seq3.UpdatedLeftCondyle.HasValue)  LeftCondyleCenter  = seq3.UpdatedLeftCondyle.Value;
            if (seq3.UpdatedRightCondyle.HasValue)  RightCondyleCenter = seq3.UpdatedRightCondyle.Value;

            if (!seq3.Accepted) return;

            // ── 9. Build final jaw positions ────────────────────────────────────
            float[] finalUpper = upperMoved;
            float[] finalLower = seq3.RotatedMandible ?? lowerMoved;

            // Refresh condyle boxes in case positions were updated during seq3
            lcC = LeftCondyleCenter!.Value;
            rcC = RightCondyleCenter!.Value;
            if (LeftCondyleHalfExtents is { } lhe2)
                leftCondyleBox  = new OrthoPlanner.Core.Geometry.CondyleBox((float)lcC.X,(float)lcC.Y,(float)lcC.Z,(float)lhe2.X,(float)lhe2.Y,(float)lhe2.Z);
            if (RightCondyleHalfExtents is { } rhe2)
                rightCondyleBox = new OrthoPlanner.Core.Geometry.CondyleBox((float)rcC.X,(float)rcC.Y,(float)rcC.Z,(float)rhe2.X,(float)rhe2.Y,(float)rhe2.Z);

            // ── 10. Screen 4: SplintPlannerWindow (final wafer) ─────────────────
            var finalConfig = new OrthoPlanner.Core.Geometry.SplintConfig
            {
                Type               = OrthoPlanner.Core.Geometry.SplintType.Final,
                FirstOperated      = firstOperated,
                Scope              = OrthoPlanner.Core.Geometry.JawScope.Bimaxillary,
                FromIntraoralScans = fromScans,
                LeftCondyleBox     = leftCondyleBox,
                RightCondyleBox    = rightCondyleBox,
                EnableAutorotation = false,
            };
            OpenSplintWindow(finalUpper, finalLower, finalConfig);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Splint Planner:\n{ex.Message}\n\n{ex.StackTrace}",
                "Splint Planner Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Opens a SplintPlannerWindow and adds the resulting mesh to the scene on accept.</summary>
    private void OpenSplintWindow(
        float[] upper, float[] lower,
        OrthoPlanner.Core.Geometry.SplintConfig config)
    {
        var win = new SplintPlannerWindow(upper, lower, this, config);
        win.Owner = Application.Current.MainWindow;
        win.Closed += (_, _) =>
        {
            try
            {
                if (!win.Accepted || win.SplintVertices == null || win.SplintVertices.Length < 9)
                    return;
                var labelledConfig = config with { FirstOperated = win.ChosenFirstOperated };
                Application.Current.Dispatcher.Invoke(() =>
                    AddSplintMeshToScene(win.SplintVertices, labelledConfig));
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

    private void AddSplintMeshToScene(float[] verts, OrthoPlanner.Core.Geometry.SplintConfig config)
    {
        var splintMesh = new MeshViewModel
        {
            Name              = config.DisplayName,
            Vertices          = verts,
            NhpBaked          = true,  // vertices already in NHP space
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
        StatusText = $"{config.DisplayName} generated — {verts.Length / 9:N0} triangles.";
    }

    /// <summary>
    /// Applies <paramref name="tx"/> to a copy of <paramref name="baseVerts"/> without
    /// mutating the scene. Returns the original array when the transform is identity.
    /// ponytail: copies and transforms vertices — no scene mutation.
    /// </summary>
    private static float[] BakeToCopy(
        float[] baseVerts,
        System.Windows.Media.Media3D.Transform3D tx)
    {
        if (tx == null || tx.Value.IsIdentity) return baseVerts;
        var result = new float[baseVerts.Length];
        var m = tx.Value;
        for (int i = 0; i + 2 < baseVerts.Length; i += 3)
        {
            var p = m.Transform(new System.Windows.Media.Media3D.Point3D(baseVerts[i], baseVerts[i + 1], baseVerts[i + 2]));
            result[i] = (float)p.X; result[i + 1] = (float)p.Y; result[i + 2] = (float)p.Z;
        }
        return result;
    }
}
