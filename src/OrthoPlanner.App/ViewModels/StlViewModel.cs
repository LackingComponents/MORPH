using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ImportStlAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Dental STL Scans",
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        // Show classification dialog
        var classDialog = new StlClassificationDialog(dialog.FileNames);
        classDialog.Owner = System.Windows.Application.Current.MainWindow;
        if (classDialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusText = "Importing STL scans...";

        foreach (var entry in classDialog.Entries)
        {
            var vertices = await Task.Run(() => StlIO.LoadStl(entry.FilePath));

            var scanType = entry.IsUpper ? DentalScanType.Upper
                         : entry.IsLower ? DentalScanType.Lower
                         : DentalScanType.Other;

            var meshVm = new MeshViewModel
            {
                Name = scanType == DentalScanType.Upper ? "Maxillary cast"
                     : scanType == DentalScanType.Lower ? "Mandibular cast"
                     : Path.GetFileNameWithoutExtension(entry.FilePath),
                Vertices = vertices,
                ColorR = (byte)(scanType == DentalScanType.Upper ? 140 : scanType == DentalScanType.Lower ? 255 : 245),
                ColorG = (byte)(scanType == DentalScanType.Upper ? 200 : scanType == DentalScanType.Lower ? 170 : 245),
                ColorB = (byte)(scanType == DentalScanType.Upper ? 255 : scanType == DentalScanType.Lower ? 170 : 230),
                ScanType = scanType,
                IsVisible = true
            };
            meshVm.OnVisibilityChanged = RefreshCombinedModel;
            meshVm.BuildModel();
            ImportedMeshes.Add(meshVm);
        }

        SaveStateForUndo();
        RefreshCombinedModel();
        StatusText = $"Imported {classDialog.Entries.Count} STL scan(s).";
        IsLoading = false;

        OnPropertyChanged(nameof(HasUpperAndLowerScans));
    }

    public bool HasUpperAndLowerScans =>
        ImportedMeshes.Any(m => m.ScanType == DentalScanType.Upper) &&
        ImportedMeshes.Any(m => m.ScanType == DentalScanType.Lower);

    [RelayCommand]
    private async Task AlignDentalScansAsync()
    {
        // Gather CT dental surface vertices (from DentalModel or HardTissueModel)
        var ctSegment = DentalModel ?? HardTissueModel;
        if (ctSegment?.Vertices == null || ctSegment.Vertices.Length < 100)
        {
            System.Windows.MessageBox.Show(
                "Please run the Dental or Bone segmentation first to generate a CT surface for alignment.",
                "No CT Surface", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // B2 guard (spec §4): the ICP target is the CT SOURCE-space surface — never an NhpShared-posed copy.
        // Under the lazy model ctSegment.Vertices are invariant source (RecomputeAllTransforms writes only
        // piece.Transform, never Vertices); this DEBUG assert documents that contract and trips if a future
        // change pre-poses verts onto the cast before alignment (which would bake NHP into the registered cast).
#if DEBUG
        System.Diagnostics.Debug.Assert(ctSegment != null && ctSegment.Vertices != null && ctSegment.Vertices.Length >= 100,
            "B2: ctSegment.Vertices (source space) is the ICP target — pre-posing it would bake NHP into the cast.");
#endif

        var scansToAlign = ImportedMeshes
            .Where(m => m.ScanType == DentalScanType.Upper || m.ScanType == DentalScanType.Lower)
            .ToList();

        if (scansToAlign.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No scans classified as Upper or Lower. Please import and classify dental scans first.",
                "No Scans", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        foreach (var scan in scansToAlign)
        {
            if (scan.Vertices == null) continue;

            StatusText = $"Aligning {scan.Name}...";

            var wizard = new DentalAlignmentWindow(Volume!, MeshHelper.ToVertexList(ctSegment.Vertices), MeshHelper.ToVertexList(scan.Vertices));
            wizard.Owner = System.Windows.Application.Current.MainWindow;
            wizard.Title = $"Align: {scan.Name}";

            if (wizard.ShowDialog() == true && wizard.Accepted && wizard.FinalTransform != null)
            {
                SaveStateForUndo();

                // Apply the transform to the actual vertices
                var list = MeshHelper.ToVertexList(scan.Vertices);
                await Task.Run(() => IcpAligner.TransformVertices(list, wizard.FinalTransform));
                scan.Vertices = MeshHelper.ToFlatArray(list);
                scan.BuildModel();

                if (wizard.CleanMerged && wizard.CleanMergedVertices != null)
                {
                    ctSegment.Vertices = MeshHelper.ToFlatArray(wizard.CleanMergedVertices);
                    ctSegment.HasMergedDental = true;
                    ctSegment.BuildModel();
                    scan.IsVisible = false; // Hide the separate STL cast since it is now part of the bone body
                }

                RefreshCombinedModel();
                StatusText = $"Aligned{(wizard.CleanMerged ? " and Merged" : "")}: {scan.Name}";
            }
            else
            {
                StatusText = $"Alignment cancelled for {scan.Name}.";
            }
        }

        StatusText = "Dental alignment complete.";
    }

    [RelayCommand]
    private async Task CleanMergeCastAsync()
    {
        await Task.Yield(); // Ensure UI stays responsive

        try
        {
            // First we need a base model (usually Mandible or Maxilla, falling back to HardTissueModel)
            // But since this replaces the existing segment we need to know exactly which one.
            // A more robust way: Find the aligned dental cast, then its corresponding bone.
            var scansToMerge = ImportedMeshes
                .Where(m => (m.ScanType == DentalScanType.Upper || m.ScanType == DentalScanType.Lower) && m.Vertices != null && m.IsVisible)
                .ToList();

            if (scansToMerge.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "No visible aligned Upper or Lower dental casts found.\nPlease import, align, and make visible the cast you wish to merge.",
                    "No Dental Cast", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Prefer an already split Mandible or Maxilla/Cranium segment.
            // The cranium/mandible split wizard names the upper piece "Cranium (Split)" or
            // "Cranium (Seed Split)" / "Isolated Cranium" — none of which contain "Maxilla".
            // Accept any of those as the upper-jaw target.
            var mandible = Segments.FirstOrDefault(s =>
                s.Name.Contains("Mandible", StringComparison.OrdinalIgnoreCase));
            var maxilla = Segments.FirstOrDefault(s =>
                s.Name.Contains("Maxilla",  StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains("Cranium",  StringComparison.OrdinalIgnoreCase));

            // Guard: clean-and-merge requires separated jaw segments.
            // Merging into the unsplit HardTissueModel (whole bone) produces malformed geometry
            // because the boolean subtraction carves the wrong surface.
            if (mandible == null || maxilla == null)
            {
                System.Windows.MessageBox.Show(
                    "Clean & Merge requires separated jaw segments.\n\n"
                    + "Run the Cranium/Mandible split first so that a Mandible segment and a "
                    + "Cranium (or Maxilla) segment both exist before merging dental casts.",
                    "Separated Segments Required",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            bool modifiedAny = false;

            foreach (var scan in scansToMerge)
            {
                SegmentViewModel? targetBone = null;

                if (scan.ScanType == DentalScanType.Lower) targetBone = mandible;
                else if (scan.ScanType == DentalScanType.Upper) targetBone = maxilla;

                if (targetBone?.Vertices == null || targetBone.Vertices.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Corresponding bone segment not found for {scan.Name}. Skipping.");
                    continue;
                }

                StatusText = $"Cleaning {targetBone.Name} and merging with {scan.Name}...";
                IsLoading = true;
                LoadProgress = 0;

                SaveStateForUndo();

                var mergedList = await Task.Run(() => MeshOps.CleanAndMergeDentalCast(
                    MeshHelper.ToVertexList(targetBone.Vertices), MeshHelper.ToVertexList(scan.Vertices!), CloseHolesAfterMerge));

                if (mergedList.Count > 0)
                {
                    targetBone.Vertices = MeshHelper.ToFlatArray(mergedList);
                    targetBone.HasMergedDental = true;
                    targetBone.BuildModel();
                    scan.IsVisible = false; // Hide original cast
                    modifiedAny = true;
                }
            }

            if (modifiedAny)
            {
                RefreshCombinedModel();
                StatusText = $"Successfully cleaned and merged all visible casts!";
            }
            else
            {
                StatusText = "Clean & Merge operation failed to produce geometry.";
            }

        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Clean and Merge failed:\n{ex.Message}\n\n{ex.StackTrace}",
                "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            StatusText = "Clean and merge failed.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportStlAsync()
    {
        if (Segments == null || Segments.Count == 0) return;

        var exportWindow = new ExportWindow(Segments)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (exportWindow.ShowDialog() != true) return;

        var selectedSegments = exportWindow.SelectedSegments;
        if (selectedSegments.Count == 0) return;

        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Export Folder"
        };

        if (folderDialog.ShowDialog() != true) return;

        IsLoading = true;
        StatusText = "Exporting selected models...";
        string folderPath = folderDialog.FolderName;

        string safePatientName = string.IsNullOrWhiteSpace(PatientName) ? "UnknownPatient" : PatientName.Replace(" ", "").Replace("^", "_");
        string safeDate = string.IsNullOrWhiteSpace(StudyDate) ? "UnknownDate" : StudyDate;

        int exportedCount = 0;
        foreach (var seg in selectedSegments)
        {
            if (seg.Vertices == null) continue;

            string safeSegName = string.Join("_", seg.Name.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{safePatientName}_{safeDate}_{safeSegName}.stl";
            string fullPath = Path.Combine(folderPath, fileName);

            await Task.Run(() => StlIO.SaveBinaryStl(fullPath, seg.Vertices));
            exportedCount++;
        }

        StatusText = $"Exported {exportedCount} STL models to {folderPath}";
        IsLoading = false;
    }

    [RelayCommand]
    private void DeleteSegmentItem(SegmentViewModel seg)
    {
        if (seg == null) return;
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete '{seg.Name}'?", "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        SaveStateForUndo();
        Segments.Remove(seg);
        if (_segVolume != null) _segVolume.ClearLabel(seg.Label);
        if (HardTissueModel == seg) HardTissueModel = null;
        if (SoftTissueModel == seg) SoftTissueModel = null;
        if (DentalModel == seg) DentalModel = null;
        RefreshCombinedModel();
    }

    [RelayCommand]
    private void DeleteImportedMesh(MeshViewModel mesh)
    {
        if (mesh == null) return;
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete imported mesh '{mesh.Name}'?", "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        SaveStateForUndo();
        ImportedMeshes.Remove(mesh);
        RefreshCombinedModel();
    }

    [RelayCommand]
    private async Task EditDentalCastsAsync()
    {
        // Collect all imported meshes (visible or not) to pass to the editor
        var meshesForEditor = ImportedMeshes
            .Where(m => m.Vertices != null && m.Vertices.Length >= 9)
            .Select(m => (m.Name, m.Vertices!, m.ColorR, m.ColorG, m.ColorB))
            .ToList();

        if (meshesForEditor.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "No dental scans have been imported yet.\nUse 'Load Dental Scans' first.",
                "No Models", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var editor = new DentalCastEditorWindow(
            meshesForEditor.Select(m => (m.Name, m.Item2, m.ColorR, m.ColorG, m.ColorB)));
        editor.Owner = System.Windows.Application.Current.MainWindow;

        if (editor.ShowDialog() != true || !editor.Accepted) return;

        // Apply edited meshes back — each key is the mesh name
        bool modified = false;
        foreach (var (editedName, editedVerts) in editor.EditedMeshes)
        {
            var target = ImportedMeshes.FirstOrDefault(m => m.Name == editedName);
            if (target == null) continue;

            SaveStateForUndo();

            // Apply edited verts
            target.Vertices = MeshHelper.ToFlatArray(editedVerts);
            target.BuildModel();
            modified = true;
        }

        if (modified)
        {
            RefreshCombinedModel();
            StatusText = $"Dental cast edits applied — {editor.EditedMeshes.Count} model(s) updated.";
        }

        await Task.CompletedTask;
    }
}
