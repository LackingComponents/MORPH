using System.IO;
using System.Runtime;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>True when a "Maxilla (LeFort 1 Separated)" segment exists ÔÇö enables the sub-osteotomy wizards.</summary>
    public bool HasLeFort1Maxilla =>
        Segments.Any(s => s.Name?.Contains("Maxilla (LeFort 1 Separated)") == true && s.IsVisible);

    /// <summary>
    /// Ensures NHP is committed before any wizard that reads Vertices in DICOM space.
    /// If the user has uncommitted NHP preview, auto-commits with a status note.
    /// </summary>
    private void EnsureNhpCommittedForWizard(string wizardName)
    {
        if (!IsNhpDirty) return;
        CommitNhp();
        StatusText = $"NHP auto-committed before {wizardName}. " + StatusText;
    }

    [RelayCommand]
    private void PlanLeFort1()
    {
        var cranium = Segments.FirstOrDefault(s => s.Name.Contains("Cranium"));
        if (cranium == null || cranium.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please isolate the Cranium segment first.", "Missing Segment", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        EnsureNhpCommittedForWizard("LeFort 1 Osteotomy");

        var wizard = new LeFortOsteotomyWindow(MeshHelper.ToVertexList(cranium.Vertices));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();

            // Hide original cranium
            cranium.IsVisible = false;

            // Add upper maxilla piece
            var upperVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name = "Cranium (LeFort Upper)",
                Vertices = MeshHelper.ToFlatArray(wizard.UpperMaxillaResult),
                ColorR = 220, ColorG = 200, ColorB = 170,
                IsVisible = true
            };
            upperVm.OnVisibilityChanged = RefreshCombinedModel;
            upperVm.BuildModel();
            Segments.Add(upperVm);

            // Add lower maxilla piece
            var lowerVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name = "Maxilla (LeFort 1 Separated)",
                Vertices = MeshHelper.ToFlatArray(wizard.LowerMaxillaResult),
                ColorR = 120, ColorG = 220, ColorB = 210,
                IsVisible = true
            };
            lowerVm.OnVisibilityChanged = RefreshCombinedModel;
            lowerVm.BuildModel();
            Segments.Add(lowerVm);

            RefreshCombinedModel();
            OnPropertyChanged(nameof(HasLeFort1Maxilla));
            StatusText = "LeFort 1 Osteotomy applied successfully.";
        }
    }

    [RelayCommand]
    private void PlanLeFort1SagittalCut()
    {
        var maxSeg = Segments.LastOrDefault(s => s.Name?.Contains("Maxilla (LeFort 1 Separated)") == true && s.IsVisible);
        if (maxSeg == null || maxSeg.Vertices == null)
        {
            System.Windows.MessageBox.Show(
                "Please perform a LeFort 1 Osteotomy first to isolate the maxilla segment.",
                "No LeFort 1 Maxilla", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        EnsureNhpCommittedForWizard("LeFort 1 Sagittal Cut");

        var wizard = new LeFort1SagittalCutWindow(MeshHelper.ToVertexList(maxSeg.Vertices));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();
            maxSeg.IsVisible = false;

            var leftVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name  = "Maxilla Left (2-Piece)",
                Vertices = MeshHelper.ToFlatArray(wizard.LeftResult),
                ColorR = 100, ColorG = 200, ColorB = 255, IsVisible = true
            };
            leftVm.OnVisibilityChanged = RefreshCombinedModel;
            leftVm.BuildModel();
            Segments.Add(leftVm);

            var rightVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name  = "Maxilla Right (2-Piece)",
                Vertices = MeshHelper.ToFlatArray(wizard.RightResult),
                ColorR = 120, ColorG = 220, ColorB = 210, IsVisible = true
            };
            rightVm.OnVisibilityChanged = RefreshCombinedModel;
            rightVm.BuildModel();
            Segments.Add(rightVm);

            RefreshCombinedModel();
            StatusText = "LeFort 1 2-Piece sagittal cut applied.";
        }
    }

    [RelayCommand]
    private void PlanLeFort1YCut()
    {
        var maxSeg = Segments.LastOrDefault(s => s.Name?.Contains("Maxilla (LeFort 1 Separated)") == true && s.IsVisible);
        if (maxSeg == null || maxSeg.Vertices == null)
        {
            System.Windows.MessageBox.Show(
                "Please perform a LeFort 1 Osteotomy first to isolate the maxilla segment.",
                "No LeFort 1 Maxilla", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        EnsureNhpCommittedForWizard("LeFort 1 Y-Cut");

        var wizard = new LeFort1YCutWindow(MeshHelper.ToVertexList(maxSeg.Vertices));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();
            maxSeg.IsVisible = false;

            var leftVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name  = "Maxilla Left (3-Piece)",
                Vertices = MeshHelper.ToFlatArray(wizard.LeftResult),
                ColorR = 100, ColorG = 200, ColorB = 255, IsVisible = true
            };
            leftVm.OnVisibilityChanged = RefreshCombinedModel;
            leftVm.BuildModel();
            Segments.Add(leftVm);

            var rightVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name  = "Maxilla Right (3-Piece)",
                Vertices = MeshHelper.ToFlatArray(wizard.RightResult),
                ColorR = 120, ColorG = 220, ColorB = 210, IsVisible = true
            };
            rightVm.OnVisibilityChanged = RefreshCombinedModel;
            rightVm.BuildModel();
            Segments.Add(rightVm);

            var centralVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name  = "Maxilla Central / Premaxilla (3-Piece)",
                Vertices = MeshHelper.ToFlatArray(wizard.CentralResult),
                ColorR = 220, ColorG = 180, ColorB = 255, IsVisible = true
            };
            centralVm.OnVisibilityChanged = RefreshCombinedModel;
            centralVm.BuildModel();
            Segments.Add(centralVm);

            RefreshCombinedModel();
            StatusText = "LeFort 1 3-Piece Y-cut applied.";
        }
    }

    [RelayCommand]
    private void PlanGenioplasty()
    {
        // Priority:
        // 1. BSSO traced ÔåÆ use the teeth-bearing distal segment (exact name "Mandible")
        // 2. Cranium/Mandible split done ÔåÆ use "Mandible (Split)"
        // 3. Whole untouched bone ÔåÆ use HardTissueModel
        var bssoDistal    = Segments.LastOrDefault(s => s.Name == "Mandible" && s.IsVisible);
        var splitMandible = Segments.LastOrDefault(s => s.Name == "Mandible (Split)" && s.IsVisible);
        var targetSeg     = (SegmentViewModel?)(bssoDistal ?? splitMandible) ?? HardTissueModel;

        if (targetSeg == null || targetSeg.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please generate the 3D model first.", "Missing Model", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        EnsureNhpCommittedForWizard("Genioplasty");

        var wizard = new GenioplastyOsteotomyWindow(MeshHelper.ToVertexList(targetSeg.Vertices));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();

            // Hide the original target segment
            targetSeg.IsVisible = false;

            // Add remaining mandible piece (posterior/superior)
            var upperVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name = targetSeg.Name + " (Chin Removed)",
                Vertices = MeshHelper.ToFlatArray(wizard.UpperMandibleResult),
                ColorR = targetSeg.ColorR, ColorG = targetSeg.ColorG, ColorB = targetSeg.ColorB,
                IsVisible = true
            };
            upperVm.OnVisibilityChanged = RefreshCombinedModel;
            upperVm.BuildModel();
            Segments.Add(upperVm);

            // Add chin piece
            var lowerVm = new SegmentViewModel
            {
                Label = (byte)(Segments.Count + 1),
                Name = "Chin Segment",
                Vertices = MeshHelper.ToFlatArray(wizard.ChinSegmentResult),
                ColorR = 120, ColorG = 220, ColorB = 160,
                IsVisible = true
            };
            lowerVm.OnVisibilityChanged = RefreshCombinedModel;
            lowerVm.BuildModel();
            Segments.Add(lowerVm);

            RefreshCombinedModel();
            StatusText = "Genioplasty applied successfully.";
        }
    }

    [RelayCommand]
    private void PlanBsso()
    {
        // For the second BSSO: operate on the remaining distal ("Mandible") from the first cut
        bool anyRamus = Segments.Any(s => s.Name?.StartsWith("Ramus") == true);
        var prevDistal   = anyRamus ? Segments.LastOrDefault(s => s.Name == "Mandible") : null;
        var origMandible = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Mandible")
                             && !s.Name.StartsWith("Ramus"));

        var inputSeg   = (SegmentViewModel?)(prevDistal ?? origMandible);
        var inputVerts = inputSeg?.Vertices;

        if (inputSeg == null || inputVerts == null || inputVerts.Length == 0)
        {
            System.Windows.MessageBox.Show("Please isolate the Mandible segment first.", "Missing Segment",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        EnsureNhpCommittedForWizard("BSSO");

        var wizard = new BssoOsteotomyWindow(MeshHelper.ToVertexList(inputVerts));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();

            // Add new proximal (condyle) ÔÇö accumulates for bilateral
            string sideName = wizard.IsLeftSide ? "Right" : "Left";
            var proxVm = new SegmentViewModel
            {
                Label    = (byte)(Segments.Count + 1),
                Name     = $"Ramus {sideName}",
                Vertices = MeshHelper.ToFlatArray(wizard.ProximalResult),
                ColorR = 120, ColorG = 160, ColorB = 240,
                IsVisible = true
            };
            proxVm.OnVisibilityChanged = RefreshCombinedModel;
            proxVm.BuildModel();
            Segments.Add(proxVm);

            if (prevDistal != null)
            {
                // Update distal in-place: replace with the smaller remainder after second cut
                prevDistal.Vertices = MeshHelper.ToFlatArray(wizard.DistalResult);
                prevDistal.Name = "Mandible";
                prevDistal.BuildModel();
                prevDistal.IsVisible = true;
            }
            else
            {
                // First BSSO: hide original mandible, add new distal
                if (origMandible != null) origMandible.IsVisible = false;
                var distVm = new SegmentViewModel
                {
                    Label    = (byte)(Segments.Count + 1),
                    Name     = "Mandible",
                    Vertices = MeshHelper.ToFlatArray(wizard.DistalResult),
                    ColorR = 220, ColorG = 140, ColorB = 120,
                    IsVisible = true
                };
                distVm.OnVisibilityChanged = RefreshCombinedModel;
                distVm.BuildModel();
                Segments.Add(distVm);
            }

            RefreshCombinedModel();
            StatusText = "BSSO Osteotomy applied successfully.";
        }
    }

    [RelayCommand]
    private async Task SplitCraniumMandibleAsync()
    {
        await Task.Yield(); // Ensure UI stays responsive

        try
        {
            // Validate: must have bone model
            var boneSegment = HardTissueModel;
            if (boneSegment?.Vertices == null || boneSegment.Vertices.Length < 100)
            {
                System.Windows.MessageBox.Show(
                    "Please run bone segmentation first to generate a bone surface.",
                    "No Bone Model", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            StatusText = "Opening Cranium/Mandible Split wizard...";

            bool HasUsableBoneMask(OrthoPlanner.Core.Segmentation.SegmentationVolume? segVolume, byte boneLabel) =>
                Volume != null &&
                segVolume != null &&
                segVolume.Width == Volume.Width &&
                segVolume.Height == Volume.Height &&
                segVolume.Depth == Volume.Depth &&
                segVolume.CountVoxels(boneLabel) > 0;

            OrthoPlanner.Core.Segmentation.SegmentationVolume? GetValidSplitTargetVolume(byte boneLabel) =>
                HasUsableBoneMask(_boneOnlySegVolume, boneLabel)
                    ? _boneOnlySegVolume
                    : HasUsableBoneMask(_segVolume, boneLabel) ? _segVolume : null;

            var splitTargetVolume = GetValidSplitTargetVolume(boneSegment.Label);

            if (splitTargetVolume == null)
            {
                System.Windows.MessageBox.Show(
                    "No valid bone segmentation mask found. Please run bone segmentation before splitting.",
                    "Bone Segmentation Required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StatusText = "Cranium/Mandible split requires a valid bone segmentation mask.";
                return;
            }


            var wizard = new CondyleSplitWindow(
                MeshHelper.ToVertexList(boneSegment.Vertices),
                Volume, splitTargetVolume, boneSegment.Label, BoneMinHU);
            wizard.Owner = System.Windows.Application.Current.MainWindow;

            if (wizard.ShowDialog() == true && wizard.Accepted)
            {
                SaveStateForUndo();

                // Store condylar axis data & midline
                // Wizard returns points in DICOM space. Bake cumulative NHP so they
                // land in the same space as the already-committed mesh vertices.
                LeftCondyleCenter  = TransformTuple(wizard.LeftCondyleCenter,  _cumulativeNhpMatrix);
                RightCondyleCenter = TransformTuple(wizard.RightCondyleCenter, _cumulativeNhpMatrix);
                DentalMidlinePoint = TransformTuple(wizard.DentalMidlinePoint, _cumulativeNhpMatrix);

                // Create Cranium segment
                if (wizard.CraniumResult != null && wizard.CraniumResult.Count > 0)
                {
                    var cranVm = new SegmentViewModel
                    {
                        Label = (byte)(Segments.Count + 1),
                        Name = "Cranium (Split)",
                        ColorR = 220, ColorG = 200, ColorB = 170,
                        Vertices = MeshHelper.ToFlatArray(wizard.CraniumResult),
                        IsVisible = true
                    };
                    cranVm.BuildModel();
                    cranVm.OnVisibilityChanged = RefreshCombinedModel;
                    Segments.Add(cranVm);
                }

                // Create Mandible segment
                if (wizard.MandibleResult != null && wizard.MandibleResult.Count > 0)
                {
                    var mandVm = new SegmentViewModel
                    {
                        Label = (byte)(Segments.Count + 1),
                        Name = "Mandible (Split)",
                        ColorR = 220, ColorG = 140, ColorB = 120,
                        Vertices = MeshHelper.ToFlatArray(wizard.MandibleResult),
                        IsVisible = true
                    };
                    mandVm.BuildModel();
                    mandVm.OnVisibilityChanged = RefreshCombinedModel;
                    Segments.Add(mandVm);
                }

                if (boneSegment != null)
                {
                    boneSegment.IsVisible = false;
                }

                RefreshCombinedModel();
                GC.Collect(2, GCCollectionMode.Optimized, false);
                StatusText = $"Split complete. Points saved: L=({LeftCondyleCenter?.X:F1},{LeftCondyleCenter?.Y:F1}), R=({RightCondyleCenter?.X:F1},{RightCondyleCenter?.Y:F1}), Mid=({DentalMidlinePoint?.X:F1},{DentalMidlinePoint?.Y:F1})";
            }
            else
            {
                StatusText = "Cranium/Mandible split cancelled.";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Cranium/Mandible split failed:\n{ex.Message}\n\n{ex.StackTrace}",
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            StatusText = "Cranium/Mandible split failed.";
        }

        // Force cleanup of wizard window's freed resources (EffectsManager, viewport geometry, mesh data)
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }
}
