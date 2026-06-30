using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App.ViewModels;

/// <summary>
/// Surgical Movements ÔÇö per-segment 6-DOF transforms (Maxilla, Mandible, Rami, Chin)
/// plus Occlusion STL management, multi-plan tree, and alignment.
/// </summary>
public partial class MainViewModel
{
    // ÔöÇÔöÇÔöÇ Surgical Movements ÔöÇÔöÇÔöÇ
    [ObservableProperty] private bool _isSurgicalMovementsOpen;
    [ObservableProperty] private bool _isMaxillaBasedSurgery = true;
    [ObservableProperty] private bool _isMandibleBasedSurgery;
    [ObservableProperty] private bool _isManualOcclusionSurgery;
    [ObservableProperty] private bool _isKeepOcclusionSurgery;

    public bool IsBaseSwitchEnabled => !IsManualOcclusionSurgery;
    public bool IsMaxillaMoveable   => IsMaxillaBasedSurgery  || IsManualOcclusionSurgery;
    public bool IsMandibleMoveable  => IsMandibleBasedSurgery || IsManualOcclusionSurgery;

    public ObservableCollection<MeshViewModel>          LoadedOcclusions { get; } = new();
    public ObservableCollection<OcclusionNodeViewModel> OcclusionNodes   { get; } = new();
    private OcclusionNodeViewModel? _activeOcclusionNode;

    // Maxilla Transforms
    [ObservableProperty] private double _surgMaxillaLat;
    [ObservableProperty] private double _surgMaxillaAnt;
    [ObservableProperty] private double _surgMaxillaVert;
    [ObservableProperty] private double _surgMaxillaRoll;
    [ObservableProperty] private double _surgMaxillaPitch;
    [ObservableProperty] private double _surgMaxillaYaw;

    // Mandible Transforms
    [ObservableProperty] private double _surgMandibleLat;
    [ObservableProperty] private double _surgMandibleAnt;
    [ObservableProperty] private double _surgMandibleVert;
    [ObservableProperty] private double _surgMandibleRoll;
    [ObservableProperty] private double _surgMandiblePitch;
    [ObservableProperty] private double _surgMandibleYaw;

    // Right Ramus Transforms
    [ObservableProperty] private double _surgRightRamusLat;
    [ObservableProperty] private double _surgRightRamusAnt;
    [ObservableProperty] private double _surgRightRamusVert;
    [ObservableProperty] private double _surgRightRamusRoll;
    [ObservableProperty] private double _surgRightRamusPitch;
    [ObservableProperty] private double _surgRightRamusYaw;

    // Left Ramus Transforms
    [ObservableProperty] private double _surgLeftRamusLat;
    [ObservableProperty] private double _surgLeftRamusAnt;
    [ObservableProperty] private double _surgLeftRamusVert;
    [ObservableProperty] private double _surgLeftRamusRoll;
    [ObservableProperty] private double _surgLeftRamusPitch;
    [ObservableProperty] private double _surgLeftRamusYaw;

    // Chin Transforms
    [ObservableProperty] private double _surgChinLat;
    [ObservableProperty] private double _surgChinAnt;
    [ObservableProperty] private double _surgChinVert;
    [ObservableProperty] private double _surgChinRoll;
    [ObservableProperty] private double _surgChinPitch;
    [ObservableProperty] private double _surgChinYaw;

    // Saved jaw-movement values ÔÇö preserved across mode switches so toggling back restores them.
    private (double Lat, double Ant, double Vert, double Roll, double Pitch, double Yaw)
        _savedMaxilla  = (0, 0, 0, 0, 0, 0),
        _savedMandible = (0, 0, 0, 0, 0, 0);

    private void SaveAndZeroMaxilla()
    {
        _savedMaxilla = (SurgMaxillaLat, SurgMaxillaAnt, SurgMaxillaVert, SurgMaxillaRoll, SurgMaxillaPitch, SurgMaxillaYaw);
        SurgMaxillaLat = SurgMaxillaAnt = SurgMaxillaVert = SurgMaxillaRoll = SurgMaxillaPitch = SurgMaxillaYaw = 0;
    }
    private void RestoreMaxilla()
    {
        SurgMaxillaLat   = _savedMaxilla.Lat;   SurgMaxillaAnt   = _savedMaxilla.Ant;
        SurgMaxillaVert  = _savedMaxilla.Vert;  SurgMaxillaRoll  = _savedMaxilla.Roll;
        SurgMaxillaPitch = _savedMaxilla.Pitch; SurgMaxillaYaw   = _savedMaxilla.Yaw;
    }
    private void SaveAndZeroMandible()
    {
        _savedMandible = (SurgMandibleLat, SurgMandibleAnt, SurgMandibleVert, SurgMandibleRoll, SurgMandiblePitch, SurgMandibleYaw);
        SurgMandibleLat = SurgMandibleAnt = SurgMandibleVert = SurgMandibleRoll = SurgMandiblePitch = SurgMandibleYaw = 0;
    }
    private void RestoreMandible()
    {
        SurgMandibleLat   = _savedMandible.Lat;   SurgMandibleAnt   = _savedMandible.Ant;
        SurgMandibleVert  = _savedMandible.Vert;  SurgMandibleRoll  = _savedMandible.Roll;
        SurgMandiblePitch = _savedMandible.Pitch; SurgMandibleYaw   = _savedMandible.Yaw;
    }

    partial void OnIsMaxillaBasedSurgeryChanged(bool value)
    {
        if (value)
        {
            IsMandibleBasedSurgery = false; // triggers SaveAndZeroMandible via OnIsMandibleBasedSurgeryChanged(false)
            RestoreMaxilla();
        }
        else
        {
            SaveAndZeroMaxilla();
        }
        UpdateMoveableStates();
    }
    partial void OnIsMandibleBasedSurgeryChanged(bool value)
    {
        if (value)
        {
            IsMaxillaBasedSurgery = false; // triggers SaveAndZeroMaxilla via OnIsMaxillaBasedSurgeryChanged(false)
            RestoreMandible();
        }
        else
        {
            SaveAndZeroMandible();
        }
        UpdateMoveableStates();
    }
    partial void OnIsManualOcclusionSurgeryChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBaseSwitchEnabled));
        if (value)
        {
            IsMaxillaBasedSurgery  = false;
            IsMandibleBasedSurgery = false;
            IsKeepOcclusionSurgery = false;
        }
        UpdateMoveableStates();
    }
    partial void OnIsKeepOcclusionSurgeryChanged(bool value)
    {
        UpdateMoveableStates();
    }

    private void UpdateMoveableStates()
    {
        OnPropertyChanged(nameof(IsMaxillaMoveable));
        OnPropertyChanged(nameof(IsMandibleMoveable));
        UpdateSurgeryTransform();
    }

    [RelayCommand]
    private void CloseSurgicalMovements()
    {
        IsSurgicalMovementsOpen = false;
    }

    [RelayCommand]
    private void AdjustSurgery(string param)
    {
        if (NeedsCondyleFulcrum(param) && !EnsureCondyleFulcrum())
            return;

        double step = 0.1;
        if (param.StartsWith("Maxilla"))
        {
            if (param.Contains("Lat"))        SurgMaxillaLat   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant"))   SurgMaxillaAnt   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert"))  SurgMaxillaVert  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll"))  SurgMaxillaRoll  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgMaxillaPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw"))   SurgMaxillaYaw   += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("Mandible"))
        {
            if (param.Contains("Lat"))        SurgMandibleLat   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant"))   SurgMandibleAnt   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert"))  SurgMandibleVert  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll"))  SurgMandibleRoll  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgMandiblePitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw"))   SurgMandibleYaw   += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("RightRamus"))
        {
            if (param.Contains("Lat"))        SurgRightRamusLat   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant"))   SurgRightRamusAnt   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert"))  SurgRightRamusVert  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll"))  SurgRightRamusRoll  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgRightRamusPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw"))   SurgRightRamusYaw   += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("LeftRamus"))
        {
            if (param.Contains("Lat"))        SurgLeftRamusLat   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant"))   SurgLeftRamusAnt   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert"))  SurgLeftRamusVert  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll"))  SurgLeftRamusRoll  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgLeftRamusPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw"))   SurgLeftRamusYaw   += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("Chin"))
        {
            if (param.Contains("Lat"))        SurgChinLat   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant"))   SurgChinAnt   += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert"))  SurgChinVert  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll"))  SurgChinRoll  += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgChinPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw"))   SurgChinYaw   += param.EndsWith("+") ? step : -step;
        }
        UpdateSurgeryTransform();
    }

    private bool NeedsCondyleFulcrum(string param)
    {
        bool isRamusRotation =
            (param.StartsWith("RightRamus") || param.StartsWith("LeftRamus")) &&
            (param.Contains("Roll") || param.Contains("Pitch") || param.Contains("Yaw"));

        if (!isRamusRotation) return false;
        if (param.StartsWith("RightRamus")) return RightCondyleCenter == null;
        if (param.StartsWith("LeftRamus")) return LeftCondyleCenter == null;
        return false;
    }

    private bool EnsureCondyleFulcrum()
    {
        if (LeftCondyleCenter != null && RightCondyleCenter != null)
            return true;

        var res = System.Windows.MessageBox.Show(
            "Mandible rotation fulcrums are not defined yet.\n\nOpen the condyle selection wizard now and save the left/right condyle bounding boxes?",
            "Condyle Fulcrum Required",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Information);
        if (res != System.Windows.MessageBoxResult.OK)
            return false;

        var (referenceVerts, referenceGeometry) = ResolveCondyleReference();
        if (referenceVerts.Count < 100)
        {
            System.Windows.MessageBox.Show(
                "No suitable bone surface is available for condyle selection. Please generate or load a cranium/mandible split first.",
                "Missing Bone Surface",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var wizard = new CondyleSplitWindow(referenceVerts, boneGeometry: referenceGeometry, landmarkOnlyMode: true)
        {
            Owner = System.Windows.Application.Current.MainWindow,
            Title = "Select Mandible Rotation Fulcrums"
        };

        if (wizard.ShowDialog() != true || !wizard.Accepted)
        {
            StatusText = "Condyle fulcrum selection cancelled.";
            return false;
        }

        SaveStateForUndo();
        LeftCondyleCenter = wizard.LeftCondyleCenter;
        RightCondyleCenter = wizard.RightCondyleCenter;
        LeftCondyleHalfExtents = wizard.LeftCondyleHalfExtents;
        RightCondyleHalfExtents = wizard.RightCondyleHalfExtents;
        DentalMidlinePoint = wizard.DentalMidlinePoint ?? DentalMidlinePoint;
        StatusText = $"Condyle fulcrums saved: L=({LeftCondyleCenter?.X:F1},{LeftCondyleCenter?.Y:F1},{LeftCondyleCenter?.Z:F1}), R=({RightCondyleCenter?.X:F1},{RightCondyleCenter?.Y:F1},{RightCondyleCenter?.Z:F1}).";
        return LeftCondyleCenter != null && RightCondyleCenter != null;
    }

    private (List<float[]> vertices, HelixToolkit.SharpDX.Geometry3D? geometry) ResolveCondyleReference()
    {
        var source = ResolveCondyleReferenceSegment();
        if (source?.Vertices is not { Length: >= 300 } verts)
            return (new List<float[]>(), null);

        // Always rebuild so secondary viewports get validated indexed geometry.
        source.BuildModel();

        return (MeshHelper.ToVertexList(verts), source.Geometry);
    }

    private SegmentViewModel? ResolveCondyleReferenceSegment()
    {
        static bool HasVertices(SegmentViewModel s) => s.Vertices is { Length: >= 300 };

        static bool IsSeedSplitMandible(SegmentViewModel s) =>
            HasVertices(s)
            && s.Name != null
            && s.Name.Contains("Seed Split", StringComparison.OrdinalIgnoreCase)
            && s.Name.Contains("Mandible", StringComparison.OrdinalIgnoreCase)
            && !s.Name.Contains("Cranium", StringComparison.OrdinalIgnoreCase);

        static bool IsBoneSegment(SegmentViewModel s) =>
            s.IsVisible
            && HasVertices(s)
            && s.Name != null
            && (s.Name.Contains("Cranium", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Mandible", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Ramus", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Bone", StringComparison.OrdinalIgnoreCase));

        static bool IsMandibleSegment(SegmentViewModel s) =>
            IsBoneSegment(s)
            && s.Name!.Contains("Mandible", StringComparison.OrdinalIgnoreCase)
            && !s.Name.Contains("Cranium", StringComparison.OrdinalIgnoreCase);

        // Splint / ramus rotation fulcrums must use the seed-split mandible when present.
        var seedSplitMandible = Segments.LastOrDefault(IsSeedSplitMandible);
        if (seedSplitMandible != null)
            return seedSplitMandible;

        var mandible = Segments.LastOrDefault(IsMandibleSegment);
        if (mandible != null)
            return mandible;

        if (HardTissueModel?.Vertices is { Length: >= 300 })
            return HardTissueModel;

        return Segments
            .Where(IsBoneSegment)
            .OrderByDescending(s => s.Vertices!.Length)
            .FirstOrDefault();
    }

    private System.Windows.Media.Media3D.Transform3D BuildSurgeryTransform(
        double ant, double lat, double vert,
        double roll, double pitch, double yaw,
        System.Windows.Media.Media3D.Point3D center)
    {
        var group = new System.Windows.Media.Media3D.Transform3DGroup();
        group.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(-center.X, -center.Y, -center.Z));
        group.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(1, 0, 0), pitch)));
        group.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), roll)));
        group.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 0, 1), yaw)));
        // Invert ant so positive values push forward (-ant)
        group.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(center.X + lat, center.Y - ant, center.Z + vert));
        return group;
    }

    private void UpdateSurgeryTransform()
    {
        // Only move the explicitly separated segments ÔÇö cranium pieces are fixed reference frame
        var maxilla    = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Maxilla")  && s.IsVisible);
        var mandible   = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Mandible") && !s.Name.Contains("Cranium") && !s.Name.StartsWith("Ramus") && s.IsVisible);
        var rightRamus = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Ramus Right") && s.IsVisible);
        var leftRamus  = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Ramus Left")  && s.IsVisible);
        var chin       = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Chin")         && s.IsVisible);

        void ApplySurgical(SegmentViewModel? seg, System.Windows.Media.Media3D.Transform3D surgTx)
        {
            if (seg == null) return;
            seg.SurgicalTransform = surgTx;
            seg.Transform = ComposeTransforms(_nhpTransform, surgTx);
        }

        var center = ModelCenter;

        var complexCenter = DentalMidlinePoint ?? (center.X, center.Y, center.Z);
        var ptComplex = new System.Windows.Media.Media3D.Point3D(complexCenter.X, complexCenter.Y, complexCenter.Z);

        var maxillaTx  = BuildSurgeryTransform(SurgMaxillaAnt,  SurgMaxillaLat,  SurgMaxillaVert,  SurgMaxillaRoll,  SurgMaxillaPitch,  SurgMaxillaYaw,  ptComplex);
        var mandibleTx = BuildSurgeryTransform(SurgMandibleAnt, SurgMandibleLat, SurgMandibleVert, SurgMandibleRoll, SurgMandiblePitch, SurgMandibleYaw, ptComplex);
        var identityTx = System.Windows.Media.Media3D.Transform3D.Identity;

        if (IsManualOcclusionSurgery)
        {
            ApplySurgical(maxilla,  maxillaTx);
            ApplySurgical(mandible, mandibleTx);
        }
        else
        {
            var occ    = _activeOcclusionNode?.Occlusion ?? LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
            var occMat = occ?.MandibleOcclusionTransform ?? System.Windows.Media.Media3D.Matrix3D.Identity;

            if (IsMaxillaBasedSurgery)
            {
                // Maxilla is the surgical driver ÔÇö moves with its sliders.
                ApplySurgical(maxilla, maxillaTx);

                if (IsKeepOcclusionSurgery)
                {
                    // "Original occlusion" ON: remove the surgical bite ÔÇö mandible follows maxilla
                    // freely as in its original (pre-treatment) CT position.
                    ApplySurgical(mandible, maxillaTx);
                }
                else
                {
                    // Default: apply the surgical bite from the dental scan so mandible snaps
                    // into planned occlusion and tracks the maxilla movement.
                    var mandibleTg = new System.Windows.Media.Media3D.Transform3DGroup();
                    mandibleTg.Children.Add(new System.Windows.Media.Media3D.MatrixTransform3D(occMat));
                    mandibleTg.Children.Add(maxillaTx);
                    ApplySurgical(mandible, mandibleTg);
                }
            }
            else if (IsMandibleBasedSurgery)
            {
                // Mandible is the driver ÔÇö moves with its sliders (whole complex follows it).
                ApplySurgical(mandible, mandibleTx);

                if (IsKeepOcclusionSurgery)
                {
                    // "Original occlusion" ON: maxilla follows mandible freely (no bite snap).
                    ApplySurgical(maxilla, mandibleTx);
                }
                else
                {
                    // Default: maxilla snaps to inverse bite then follows mandible.
                    var invOccMat = occMat;
                    if (invOccMat.HasInverse) invOccMat.Invert();
                    var maxillaTg = new System.Windows.Media.Media3D.Transform3DGroup();
                    maxillaTg.Children.Add(new System.Windows.Media.Media3D.MatrixTransform3D(invOccMat));
                    maxillaTg.Children.Add(mandibleTx);
                    ApplySurgical(maxilla, maxillaTg);
                }
            }
            else
            {
                // No mode ÔÇö all bones at original CT position.
                ApplySurgical(maxilla,  identityTx);
                ApplySurgical(mandible, identityTx);
            }
        }

        // 2. Right Ramus uses RightCondyleCenter
        var rcCenter = RightCondyleCenter ?? (center.X, center.Y, center.Z);
        ApplySurgical(rightRamus, BuildSurgeryTransform(
            SurgRightRamusAnt, SurgRightRamusLat, SurgRightRamusVert,
            SurgRightRamusRoll, SurgRightRamusPitch, SurgRightRamusYaw,
            new System.Windows.Media.Media3D.Point3D(rcCenter.X, rcCenter.Y, rcCenter.Z)));

        // 3. Left Ramus uses LeftCondyleCenter
        var lcCenter = LeftCondyleCenter ?? (center.X, center.Y, center.Z);
        ApplySurgical(leftRamus, BuildSurgeryTransform(
            SurgLeftRamusAnt, SurgLeftRamusLat, SurgLeftRamusVert,
            SurgLeftRamusRoll, SurgLeftRamusPitch, SurgLeftRamusYaw,
            new System.Windows.Media.Media3D.Point3D(lcCenter.X, lcCenter.Y, lcCenter.Z)));

        // 4. Chin follows mandible movement in all modes
        if (chin != null)
        {
            var chinPivot = new System.Windows.Media.Media3D.Point3D(center.X, center.Y, center.Z);
            if (chin.Vertices != null && chin.Vertices.Length > 0)
            {
                double cx = 0, cy = 0, cz = 0;
                for (int vi3 = 0; vi3 < chin.Vertices.Length; vi3 += 3)
                    { cx += chin.Vertices[vi3]; cy += chin.Vertices[vi3 + 1]; cz += chin.Vertices[vi3 + 2]; }
                int chinVCount = chin.Vertices.Length / 3;
                chinPivot = new System.Windows.Media.Media3D.Point3D(cx / chinVCount, cy / chinVCount, cz / chinVCount);
            }
            var chinLocal  = BuildSurgeryTransform(SurgChinAnt, SurgChinLat, SurgChinVert, SurgChinRoll, SurgChinPitch, SurgChinYaw, chinPivot);
            System.Windows.Media.Media3D.Transform3D followTx = identityTx;
            if (IsManualOcclusionSurgery)  followTx = mandibleTx;
            else if (IsMandibleBasedSurgery) followTx = mandibleTx;
            else if (IsMaxillaBasedSurgery)  followTx = maxillaTx; // mandible tracks maxilla in maxilla-based sub-modes
            var chinSurg = new System.Windows.Media.Media3D.Transform3DGroup();
            chinSurg.Children.Add(chinLocal);
            chinSurg.Children.Add(followTx);
            ApplySurgical(chin, chinSurg);
        }
    }

    // ÔöÇÔöÇÔöÇ Occlusion STL Management ÔöÇÔöÇÔöÇ

    [RelayCommand]
    private async Task LoadAndAlignOcclusionAsync()
    {
        await LoadOcclusionAsync();
        if (LoadedOcclusions.Any(o => o.IsVisible))
        {
            await AlignOcclusions();
        }
    }

    [RelayCommand]
    private async Task LoadOcclusionAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title     = "Import Occlusion STL",
            Filter    = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        IsLoading  = true;
        StatusText = "Importing Occlusion STL...";

        foreach (var file in dialog.FileNames)
        {
            var vertices = await Task.Run(() => StlIO.LoadStl(file));

            var meshVm = new MeshViewModel
            {
                Name     = Path.GetFileNameWithoutExtension(file) + " (Occlusion)",
                Vertices = vertices,
                ColorR   = 150, ColorG = 255, ColorB = 150,
                ScanType = DentalScanType.Other,
                IsVisible = true
            };
            meshVm.OnVisibilityChanged = RefreshCombinedModel;
            meshVm.BuildModel();
            LoadedOcclusions.Add(meshVm);

            // Create a tree node for this occlusion
            var node = new OcclusionNodeViewModel
            {
                Name      = $"Occlusion {OcclusionNodes.Count + 1}",
                Occlusion = meshVm
            };
            OcclusionNodes.Add(node);
            if (_activeOcclusionNode == null) SetActiveOcclusionNode(node);
        }

        RefreshCombinedModel();
        StatusText = $"Imported {dialog.FileNames.Length} Occlusion STL(s).";
        IsLoading  = false;
    }

    [RelayCommand]
    private void DeleteLoadedOcclusion(MeshViewModel mesh)
    {
        if (mesh == null) return;
        if (System.Windows.MessageBox.Show(
                $"Are you sure you want to delete occlusion '{mesh.Name}'?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        var node = OcclusionNodes.FirstOrDefault(n => n.Occlusion == mesh);
        if (node != null) OcclusionNodes.Remove(node);
        if (_activeOcclusionNode == node) SetActiveOcclusionNode(OcclusionNodes.FirstOrDefault());
        LoadedOcclusions.Remove(mesh);
        RefreshCombinedModel();
    }

    // ÔöÇÔöÇÔöÇ Occlusion node helpers ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void SetActiveOcclusionNode(OcclusionNodeViewModel? node)
    {
        foreach (var n in OcclusionNodes) n.IsActive = false;
        _activeOcclusionNode = node;
        if (node != null) node.IsActive = true;
        UpdateSurgeryTransform();
    }

    private OcclusionPlanViewModel SnapshotCurrentPlan(string name) => new()
    {
        Name = name,
        IsMaxillaBasedSurgery  = IsMaxillaBasedSurgery,
        IsMandibleBasedSurgery = IsMandibleBasedSurgery,
        IsManualOcclusionSurgery = IsManualOcclusionSurgery,
        IsKeepOcclusionSurgery = IsKeepOcclusionSurgery,
        MaxillaLat   = SurgMaxillaLat,   MaxillaAnt   = SurgMaxillaAnt,   MaxillaVert  = SurgMaxillaVert,
        MaxillaRoll  = SurgMaxillaRoll,  MaxillaPitch = SurgMaxillaPitch, MaxillaYaw   = SurgMaxillaYaw,
        MandibleLat  = SurgMandibleLat,  MandibleAnt  = SurgMandibleAnt,  MandibleVert = SurgMandibleVert,
        MandibleRoll = SurgMandibleRoll, MandiblePitch = SurgMandiblePitch, MandibleYaw = SurgMandibleYaw,
        RightRamusLat   = SurgRightRamusLat,   RightRamusAnt   = SurgRightRamusAnt,   RightRamusVert  = SurgRightRamusVert,
        RightRamusRoll  = SurgRightRamusRoll,  RightRamusPitch = SurgRightRamusPitch, RightRamusYaw   = SurgRightRamusYaw,
        LeftRamusLat    = SurgLeftRamusLat,    LeftRamusAnt    = SurgLeftRamusAnt,    LeftRamusVert   = SurgLeftRamusVert,
        LeftRamusRoll   = SurgLeftRamusRoll,   LeftRamusPitch  = SurgLeftRamusPitch,  LeftRamusYaw    = SurgLeftRamusYaw,
        ChinLat  = SurgChinLat,  ChinAnt  = SurgChinAnt,  ChinVert  = SurgChinVert,
        ChinRoll = SurgChinRoll, ChinPitch = SurgChinPitch, ChinYaw  = SurgChinYaw,
        SavedMaxillaLat    = _savedMaxilla.Lat,    SavedMaxillaAnt    = _savedMaxilla.Ant,
        SavedMaxillaVert   = _savedMaxilla.Vert,   SavedMaxillaRoll   = _savedMaxilla.Roll,
        SavedMaxillaPitch  = _savedMaxilla.Pitch,  SavedMaxillaYaw    = _savedMaxilla.Yaw,
        SavedMandibleLat   = _savedMandible.Lat,   SavedMandibleAnt   = _savedMandible.Ant,
        SavedMandibleVert  = _savedMandible.Vert,  SavedMandibleRoll  = _savedMandible.Roll,
        SavedMandiblePitch = _savedMandible.Pitch, SavedMandibleYaw   = _savedMandible.Yaw,
    };

    private void OverwritePlanFromCurrent(OcclusionPlanViewModel plan)
    {
        var snap = SnapshotCurrentPlan(plan.Name);
        plan.IsMaxillaBasedSurgery  = snap.IsMaxillaBasedSurgery;
        plan.IsMandibleBasedSurgery = snap.IsMandibleBasedSurgery;
        plan.IsManualOcclusionSurgery = snap.IsManualOcclusionSurgery;
        plan.IsKeepOcclusionSurgery = snap.IsKeepOcclusionSurgery;
        plan.MaxillaLat = snap.MaxillaLat; plan.MaxillaAnt = snap.MaxillaAnt; plan.MaxillaVert = snap.MaxillaVert;
        plan.MaxillaRoll = snap.MaxillaRoll; plan.MaxillaPitch = snap.MaxillaPitch; plan.MaxillaYaw = snap.MaxillaYaw;
        plan.MandibleLat = snap.MandibleLat; plan.MandibleAnt = snap.MandibleAnt; plan.MandibleVert = snap.MandibleVert;
        plan.MandibleRoll = snap.MandibleRoll; plan.MandiblePitch = snap.MandiblePitch; plan.MandibleYaw = snap.MandibleYaw;
        plan.RightRamusLat = snap.RightRamusLat; plan.RightRamusAnt = snap.RightRamusAnt; plan.RightRamusVert = snap.RightRamusVert;
        plan.RightRamusRoll = snap.RightRamusRoll; plan.RightRamusPitch = snap.RightRamusPitch; plan.RightRamusYaw = snap.RightRamusYaw;
        plan.LeftRamusLat = snap.LeftRamusLat; plan.LeftRamusAnt = snap.LeftRamusAnt; plan.LeftRamusVert = snap.LeftRamusVert;
        plan.LeftRamusRoll = snap.LeftRamusRoll; plan.LeftRamusPitch = snap.LeftRamusPitch; plan.LeftRamusYaw = snap.LeftRamusYaw;
        plan.ChinLat = snap.ChinLat; plan.ChinAnt = snap.ChinAnt; plan.ChinVert = snap.ChinVert;
        plan.ChinRoll = snap.ChinRoll; plan.ChinPitch = snap.ChinPitch; plan.ChinYaw = snap.ChinYaw;
        plan.SavedMaxillaLat = snap.SavedMaxillaLat; plan.SavedMaxillaAnt = snap.SavedMaxillaAnt;
        plan.SavedMaxillaVert = snap.SavedMaxillaVert; plan.SavedMaxillaRoll = snap.SavedMaxillaRoll;
        plan.SavedMaxillaPitch = snap.SavedMaxillaPitch; plan.SavedMaxillaYaw = snap.SavedMaxillaYaw;
        plan.SavedMandibleLat = snap.SavedMandibleLat; plan.SavedMandibleAnt = snap.SavedMandibleAnt;
        plan.SavedMandibleVert = snap.SavedMandibleVert; plan.SavedMandibleRoll = snap.SavedMandibleRoll;
        plan.SavedMandiblePitch = snap.SavedMandiblePitch; plan.SavedMandibleYaw = snap.SavedMandibleYaw;
    }

    private void ApplyPlan(OcclusionPlanViewModel plan)
    {
        // Let mode properties update UI state first; movement values are restored after
        // so mode-switch zero/restore logic cannot erase the saved plan.
        IsManualOcclusionSurgery = plan.IsManualOcclusionSurgery;
        IsMaxillaBasedSurgery  = plan.IsMaxillaBasedSurgery;
        IsMandibleBasedSurgery = plan.IsMandibleBasedSurgery;
        IsKeepOcclusionSurgery = plan.IsKeepOcclusionSurgery;

        // Restore saved backups first (so mode switches don't clobber them)
        _savedMaxilla  = (plan.SavedMaxillaLat, plan.SavedMaxillaAnt, plan.SavedMaxillaVert, plan.SavedMaxillaRoll, plan.SavedMaxillaPitch, plan.SavedMaxillaYaw);
        _savedMandible = (plan.SavedMandibleLat, plan.SavedMandibleAnt, plan.SavedMandibleVert, plan.SavedMandibleRoll, plan.SavedMandiblePitch, plan.SavedMandibleYaw);
        // Active movement values
        SurgMaxillaLat   = plan.MaxillaLat;   SurgMaxillaAnt   = plan.MaxillaAnt;   SurgMaxillaVert  = plan.MaxillaVert;
        SurgMaxillaRoll  = plan.MaxillaRoll;  SurgMaxillaPitch = plan.MaxillaPitch; SurgMaxillaYaw   = plan.MaxillaYaw;
        SurgMandibleLat  = plan.MandibleLat;  SurgMandibleAnt  = plan.MandibleAnt;  SurgMandibleVert = plan.MandibleVert;
        SurgMandibleRoll = plan.MandibleRoll; SurgMandiblePitch = plan.MandiblePitch; SurgMandibleYaw = plan.MandibleYaw;
        SurgRightRamusLat = plan.RightRamusLat; SurgRightRamusAnt = plan.RightRamusAnt; SurgRightRamusVert = plan.RightRamusVert;
        SurgRightRamusRoll = plan.RightRamusRoll; SurgRightRamusPitch = plan.RightRamusPitch; SurgRightRamusYaw = plan.RightRamusYaw;
        SurgLeftRamusLat = plan.LeftRamusLat; SurgLeftRamusAnt = plan.LeftRamusAnt; SurgLeftRamusVert = plan.LeftRamusVert;
        SurgLeftRamusRoll = plan.LeftRamusRoll; SurgLeftRamusPitch = plan.LeftRamusPitch; SurgLeftRamusYaw = plan.LeftRamusYaw;
        SurgChinLat = plan.ChinLat; SurgChinAnt = plan.ChinAnt; SurgChinVert = plan.ChinVert;
        SurgChinRoll = plan.ChinRoll; SurgChinPitch = plan.ChinPitch; SurgChinYaw = plan.ChinYaw;
        UpdateMoveableStates();
    }

    // ÔöÇÔöÇÔöÇ Plan tree commands ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    [RelayCommand]
    private void AddPlan(OcclusionNodeViewModel node)
    {
        var plan = SnapshotCurrentPlan($"Plan {node.Plans.Count + 1}");
        foreach (var p in node.Plans) p.IsSelected = false;
        plan.IsSelected = true;
        node.Plans.Add(plan);
        SetActiveOcclusionNode(node);
    }

    [RelayCommand]
    private void SelectPlan(OcclusionPlanViewModel plan)
    {
        var node = OcclusionNodes.FirstOrDefault(n => n.Plans.Contains(plan));
        if (node == null) return;
        foreach (var n in OcclusionNodes)
            foreach (var p in n.Plans) p.IsSelected = false;
        plan.IsSelected = true;
        SetActiveOcclusionNode(node);
        ApplyPlan(plan);
    }

    [RelayCommand]
    private void SavePlan(OcclusionPlanViewModel plan)
    {
        OverwritePlanFromCurrent(plan);
        StatusText = $"Plan '{plan.Name}' saved.";
    }

    /// <summary>Load a new STL and immediately open the manual alignment wizard to create a new node.</summary>
    [RelayCommand]
    private async Task AddOcclusionNodeAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Import Occlusion STL",
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        IsLoading  = true;
        StatusText = "Importing new occlusion STL...";
        var vertices = await Task.Run(() => StlIO.LoadStl(dialog.FileName));

        var meshVm = new MeshViewModel
        {
            Name     = Path.GetFileNameWithoutExtension(dialog.FileName) + " (Occlusion)",
            Vertices = vertices, ColorR = 150, ColorG = 255, ColorB = 150,
            ScanType = DentalScanType.Other, IsVisible = true
        };
        meshVm.OnVisibilityChanged = RefreshCombinedModel;
        meshVm.BuildModel();
        LoadedOcclusions.Add(meshVm);

        var node = new OcclusionNodeViewModel { Name = $"Occlusion {OcclusionNodes.Count + 1}", Occlusion = meshVm };
        OcclusionNodes.Add(node);
        SetActiveOcclusionNode(node);
        IsLoading = false;

        // Immediately open the manual alignment wizard for the new occlusion
        await RealignOcclusionAsync(node);
    }

    /// <summary>Re-open the manual alignment wizard for an existing occlusion node (right-click -> Realign).</summary>
    [RelayCommand]
    private async Task RealignOcclusionAsync(OcclusionNodeViewModel node)
    {
        var occlusion = node.Occlusion;
        if (occlusion?.Vertices == null) return;

        var maxilla  = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Maxilla"));
        var mandible = ResolveTeethBearingMandible();
        if (maxilla == null || mandible == null || maxilla.Vertices == null || mandible.Vertices == null)
        {
            System.Windows.MessageBox.Show("Maxilla or Mandible segments not found. Please segment them first.",
                "Missing Bones", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var wizard = new ManualOcclusionAlignmentWindow(maxilla, mandible, occlusion)
        {
            Owner = Application.Current.MainWindow
        };

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();
            if (wizard.FinalOcclusionTransform != System.Windows.Media.Media3D.Matrix3D.Identity)
            {
                var txArr = ToDoubleMatrix(wizard.FinalOcclusionTransform);
                await Task.Run(() => OrthoPlanner.Core.Geometry.IcpAligner.TransformVertices(occlusion.Vertices, txArr));
                occlusion.BuildModel();
            }
            occlusion.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            occlusion.MaxillaOcclusionTransform  = wizard.MaxillaTransform;
            occlusion.MandibleOcclusionTransform = wizard.MandibleTransform;
            SetActiveOcclusionNode(node);
            UpdateSurgeryTransform();
            RefreshCombinedModel();
            StatusText = $"'{node.Name}' realigned successfully.";
        }
    }

    [RelayCommand]
    private async Task AlignOcclusions()
    {
        var occlusion = LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
        if (occlusion == null || occlusion.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please make exactly one Occlusion STL visible to align to.",
                "Select Occlusion", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var maxilla  = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Maxilla"));
        var mandible = ResolveTeethBearingMandible();

        if (maxilla == null || mandible == null || maxilla.Vertices == null || mandible.Vertices == null)
        {
            System.Windows.MessageBox.Show(
                "Maxilla or Mandible bone segments not found or segmented. Please segment them first.",
                "Missing Bones", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        SaveStateForUndo();
        StatusText = "Computing automated occlusion alignment... (1/2: Aligning Occlusion to Maxilla)";

        List<float[]>? maxillaVertsList = null;
        List<float[]>? mandibleVertsList = null;
        List<float[]>? occVertsList = null;
        double[,]? occToMaxTransform = null;
        double[,]? manToOccTransform = null;
        double manRmsError = 0;

        try
        {
            await Task.Run(() =>
            {
                // 1. Center of combined Maxilla and Mandible
                double mxMinX = double.MaxValue, mxMinY = double.MaxValue, mxMinZ = double.MaxValue;
                double mxMaxX = double.MinValue, mxMaxY = double.MinValue, mxMaxZ = double.MinValue;
                void ExpandBounds(float[] v)
                {
                    for (int i = 0; i < v.Length; i += 3)
                    {
                        if (v[i] < mxMinX) mxMinX = v[i]; if (v[i] > mxMaxX) mxMaxX = v[i];
                        if (v[i + 1] < mxMinY) mxMinY = v[i + 1]; if (v[i + 1] > mxMaxY) mxMaxY = v[i + 1];
                        if (v[i + 2] < mxMinZ) mxMinZ = v[i + 2]; if (v[i + 2] > mxMaxZ) mxMaxZ = v[i + 2];
                    }
                }
                ExpandBounds(maxilla.Vertices);
                ExpandBounds(mandible.Vertices);
                var jawCenter = new System.Windows.Media.Media3D.Point3D((mxMinX + mxMaxX) / 2, (mxMinY + mxMaxY) / 2, (mxMinZ + mxMaxZ) / 2);

                // 2. Center of Occlusion
                double oMinX = double.MaxValue, oMinY = double.MaxValue, oMinZ = double.MaxValue;
                double oMaxX = double.MinValue, oMaxY = double.MinValue, oMaxZ = double.MinValue;
                for (int i = 0; i < occlusion.Vertices.Length; i += 3)
                {
                    if (occlusion.Vertices[i] < oMinX) oMinX = occlusion.Vertices[i]; if (occlusion.Vertices[i] > oMaxX) oMaxX = occlusion.Vertices[i];
                    if (occlusion.Vertices[i + 1] < oMinY) oMinY = occlusion.Vertices[i + 1]; if (occlusion.Vertices[i + 1] > oMaxY) oMaxY = occlusion.Vertices[i + 1];
                    if (occlusion.Vertices[i + 2] < oMinZ) oMinZ = occlusion.Vertices[i + 2]; if (occlusion.Vertices[i + 2] > oMaxZ) oMaxZ = occlusion.Vertices[i + 2];
                }
                var occCenter = new System.Windows.Media.Media3D.Point3D((oMinX + oMaxX) / 2, (oMinY + oMaxY) / 2, (oMinZ + oMaxZ) / 2);

                // 3. Initial transform: move Occlusion centroid to jaw centroid
                double dX = jawCenter.X - occCenter.X;
                double dY = jawCenter.Y - occCenter.Y;
                double dZ = jawCenter.Z - occCenter.Z;
                var initialTx = new double[4, 4] {
                    { 1, 0, 0, dX },
                    { 0, 1, 0, dY },
                    { 0, 0, 1, dZ },
                    { 0, 0, 0, 1  }
                };

                maxillaVertsList  = MeshHelper.ToVertexList(maxilla.Vertices);
                mandibleVertsList = MeshHelper.ToVertexList(mandible.Vertices);
                occVertsList      = MeshHelper.ToVertexList(occlusion.Vertices);

                // 4. ICP 1: Pull Occlusion (source) to Maxilla (target)
                var resultOccToMax = OrthoPlanner.Core.Geometry.IcpAligner.AlignRobust(
                    occVertsList, maxillaVertsList, initialTx,
                    maxIterations: 500,
                    tolerance: 0,
                    targetCullRatio: 0.40,
                    sourceCullRatio: 0.50,
                    sigmaEnd: 1.0);

                occToMaxTransform = resultOccToMax.Transform;
                OrthoPlanner.Core.Geometry.IcpAligner.TransformVertices(occVertsList, occToMaxTransform);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "Computing automated occlusion alignment... (2/2: Aligning Mandible to Occlusion)");

                // 5. ICP 2: Pull Mandible (source) to the maxilla-aligned Occlusion (target)
                double manMinX = double.MaxValue, manMinY = double.MaxValue, manMinZ = double.MaxValue;
                double manMaxX = double.MinValue, manMaxY = double.MinValue, manMaxZ = double.MinValue;
                double occMinX2 = double.MaxValue, occMinY2 = double.MaxValue, occMinZ2 = double.MaxValue;
                double occMaxX2 = double.MinValue, occMaxY2 = double.MinValue, occMaxZ2 = double.MinValue;
                foreach (var v in mandibleVertsList)
                {
                    if (v[0] < manMinX) manMinX = v[0]; if (v[0] > manMaxX) manMaxX = v[0];
                    if (v[1] < manMinY) manMinY = v[1]; if (v[1] > manMaxY) manMaxY = v[1];
                    if (v[2] < manMinZ) manMinZ = v[2]; if (v[2] > manMaxZ) manMaxZ = v[2];
                }
                foreach (var v in occVertsList)
                {
                    if (v[0] < occMinX2) occMinX2 = v[0]; if (v[0] > occMaxX2) occMaxX2 = v[0];
                    if (v[1] < occMinY2) occMinY2 = v[1]; if (v[1] > occMaxY2) occMaxY2 = v[1];
                    if (v[2] < occMinZ2) occMinZ2 = v[2]; if (v[2] > occMaxZ2) occMaxZ2 = v[2];
                }
                double manCx = (manMinX + manMaxX) / 2, manCy = (manMinY + manMaxY) / 2, manCz = (manMinZ + manMaxZ) / 2;
                double occCx = (occMinX2 + occMaxX2) / 2, occCy = (occMinY2 + occMaxY2) / 2, occCz = (occMinZ2 + occMaxZ2) / 2;
                var initialManTx = new double[4, 4] {
                    { 1, 0, 0, occCx - manCx },
                    { 0, 1, 0, occCy - manCy },
                    { 0, 0, 1, occCz - manCz },
                    { 0, 0, 0, 1             }
                };
                var resultManToOcc = OrthoPlanner.Core.Geometry.IcpAligner.AlignRobust(
                    mandibleVertsList, occVertsList, initialManTx,
                    maxIterations: 500,
                    tolerance: 0,
                    targetCullRatio: 0.50,
                    sourceCullRatio: 0.20);

                manToOccTransform = resultManToOcc.Transform;
                manRmsError       = resultManToOcc.RmsError;
            });

            if (occToMaxTransform == null || manToOccTransform == null ||
                maxillaVertsList == null || mandibleVertsList == null || occVertsList == null)
            {
                StatusText = "Automated occlusion alignment failed (null result).";
                return;
            }

            StatusText = "Review alignment in the Checker Window...";
            var wizard = new OcclusionCheckerWindow(
                maxillaVertsList,
                mandibleVertsList,
                occVertsList,
                manRmsError)
            {
                Owner = Application.Current.MainWindow
            };

            if (wizard.ShowDialog() == true && wizard.Accepted)
            {
                occlusion.MaxillaOcclusionTransform  = System.Windows.Media.Media3D.Matrix3D.Identity;
                occlusion.MandibleOcclusionTransform = ConvertToMatrix3D(manToOccTransform);
                var finalOccTx = ConvertToMatrix3D(occToMaxTransform);
                occlusion.Transform = new System.Windows.Media.Media3D.MatrixTransform3D(finalOccTx);
                UpdateSurgeryTransform();
                StatusText = $"Successfully aligned Occlusion STL automatically. RMS={manRmsError:0.000}";
            }
            else
            {
                StatusText = "Automated occlusion alignment cancelled.";
            }
        }
        catch (Exception ex)
        {
            StatusText = "Error during automated occlusion alignment: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenManualOcclusionAlignment()
    {
        var occlusion = LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
        if (occlusion == null || occlusion.Vertices == null)
        {
            System.Windows.MessageBox.Show("No occlusion STL is currently loaded. Use 'Load & Align Occlusion' first.",
                "No Occlusion", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var maxilla  = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Maxilla"));
        var mandible = ResolveTeethBearingMandible();

        if (maxilla == null || mandible == null || maxilla.Vertices == null || mandible.Vertices == null)
        {
            System.Windows.MessageBox.Show("Maxilla or Mandible bone segments not found. Please segment them first.",
                "Missing Bones", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var manualWizard = new ManualOcclusionAlignmentWindow(maxilla, mandible, occlusion)
        {
            Owner = Application.Current.MainWindow
        };

        if (manualWizard.ShowDialog() == true && manualWizard.Accepted)
        {
            SaveStateForUndo();
            try
            {
                if (manualWizard.FinalOcclusionTransform != System.Windows.Media.Media3D.Matrix3D.Identity)
                {
                    var txArr = ToDoubleMatrix(manualWizard.FinalOcclusionTransform);
                    await Task.Run(() => OrthoPlanner.Core.Geometry.IcpAligner.TransformVertices(occlusion.Vertices, txArr));
                    occlusion.BuildModel();
                }
                occlusion.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
                occlusion.MaxillaOcclusionTransform  = manualWizard.MaxillaTransform;
                occlusion.MandibleOcclusionTransform = manualWizard.MandibleTransform;
                UpdateSurgeryTransform();
                RefreshCombinedModel();
                StatusText = "Occlusion STL aligned manually.";
            }
            catch (Exception ex)
            {
                StatusText = "Error applying manual occlusion alignment: " + ex.Message;
            }
        }
        else
        {
            StatusText = "Manual alignment cancelled.";
        }
    }

    private System.Windows.Media.Media3D.Matrix3D ConvertToMatrix3D(double[,] m)
    {
        return new System.Windows.Media.Media3D.Matrix3D(
            m[0,0], m[1,0], m[2,0], m[3,0],
            m[0,1], m[1,1], m[2,1], m[3,1],
            m[0,2], m[1,2], m[2,2], m[3,2],
            m[0,3], m[1,3], m[2,3], m[3,3]);
    }

    /// <summary>
    /// Converts a WPF Matrix3D (row-vector convention) back to a double[4,4]
    /// (column-vector convention) compatible with IcpAligner.TransformVertices.
    /// </summary>
    private static double[,] ToDoubleMatrix(System.Windows.Media.Media3D.Matrix3D m) =>
        new double[4, 4]
        {
            { m.M11, m.M21, m.M31, m.OffsetX },
            { m.M12, m.M22, m.M32, m.OffsetY },
            { m.M13, m.M23, m.M33, m.OffsetZ },
            { 0,     0,     0,     1          }
        };

    /// <summary>
    /// Returns the teeth-bearing mandible segment with the correct priority:
    ///   1. "Mandible"           - the BSSO distal/teeth-bearing fragment.
    ///   2. "Mandible (Split)"   - after condyle-split but no BSSO.
    ///   3. Any visible segment whose name contains "Mandible" (excluding Cranium / Ramus).
    /// </summary>
    private SegmentViewModel? ResolveTeethBearingMandible()
    {
        var bssoDistal = Segments.FirstOrDefault(s =>
            s.Name != null &&
            string.Equals(s.Name, "Mandible", StringComparison.OrdinalIgnoreCase) &&
            s.Vertices != null);
        if (bssoDistal != null) return bssoDistal;

        var splitMandible = Segments.FirstOrDefault(s =>
            s.Name != null &&
            string.Equals(s.Name, "Mandible (Split)", StringComparison.OrdinalIgnoreCase) &&
            s.Vertices != null);
        if (splitMandible != null) return splitMandible;

        return Segments.FirstOrDefault(s =>
            s.Name != null &&
            s.Name.IndexOf("Mandible", StringComparison.OrdinalIgnoreCase) >= 0 &&
            !s.Name.Contains("Cranium") &&
            !s.Name.StartsWith("Ramus") &&
            s.Vertices != null);
    }
}
