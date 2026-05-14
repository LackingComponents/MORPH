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
                Name = Path.GetFileNameWithoutExtension(entry.FilePath) + (scanType != DentalScanType.Other ? $" ({scanType})" : ""),
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

            // Prefer an already split Mandible or Maxilla, then fallback to general bone
            var mandible = Segments.FirstOrDefault(s => s.Name.Contains("Mandible"));
            var maxilla = Segments.FirstOrDefault(s => s.Name.Contains("Maxilla") || s.Name.Contains("Cranium"));

            bool modifiedAny = false;

            foreach (var scan in scansToMerge)
            {
                SegmentViewModel? targetBone = null;

                if (scan.ScanType == DentalScanType.Lower) targetBone = mandible ?? HardTissueModel;
                else if (scan.ScanType == DentalScanType.Upper) targetBone = maxilla ?? HardTissueModel;

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
}
