using System.IO;
using System.IO.Compression;
using System.Collections.ObjectModel;
using System.Runtime;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.Core.Segmentation;
using OrthoPlanner.App.ViewModels.Photogrammetry;

namespace OrthoPlanner.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
    }

    // ─── Photogrammetry ───
    public PhotogrammetryViewModel PhotogrammetrySpace { get; } = new();
    [ObservableProperty] private bool _isPhotogrammetryOpen;

    [RelayCommand]
    private void OpenPhotogrammetry()
    {
        IsPhotogrammetryOpen = true;
    }

    [RelayCommand]
    private void ClosePhotogrammetry()
    {
        IsPhotogrammetryOpen = false;
    }

    // ─── Surgical Movements ───
    [ObservableProperty] private bool _isSurgicalMovementsOpen;
    [ObservableProperty] private bool _isMaxillaBasedSurgery = true;
    [ObservableProperty] private bool _isMandibleBasedSurgery;
    [ObservableProperty] private bool _isManualOcclusionSurgery;
    [ObservableProperty] private bool _isKeepOcclusionSurgery;

    public bool IsBaseSwitchEnabled => !IsManualOcclusionSurgery;
    public bool IsMaxillaMoveable => IsMaxillaBasedSurgery || IsManualOcclusionSurgery;
    public bool IsMandibleMoveable => IsMandibleBasedSurgery || IsManualOcclusionSurgery;

    public ObservableCollection<MeshViewModel> LoadedOcclusions { get; } = new();

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

    partial void OnIsMaxillaBasedSurgeryChanged(bool value)
    {
        if (value) { IsMandibleBasedSurgery = false; }
        UpdateMoveableStates();
    }
    partial void OnIsMandibleBasedSurgeryChanged(bool value)
    {
        if (value) { IsMaxillaBasedSurgery = false; }
        UpdateMoveableStates();
    }
    partial void OnIsManualOcclusionSurgeryChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBaseSwitchEnabled));
        if (value) 
        { 
            IsMaxillaBasedSurgery = false; 
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
        double step = 0.5;
        if (param.StartsWith("Maxilla"))
        {
            if (param.Contains("Lat")) SurgMaxillaLat += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant")) SurgMaxillaAnt += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert")) SurgMaxillaVert += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll")) SurgMaxillaRoll += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgMaxillaPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw")) SurgMaxillaYaw += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("Mandible"))
        {
            if (param.Contains("Lat")) SurgMandibleLat += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant")) SurgMandibleAnt += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert")) SurgMandibleVert += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll")) SurgMandibleRoll += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgMandiblePitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw")) SurgMandibleYaw += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("RightRamus"))
        {
            if (param.Contains("Lat")) SurgRightRamusLat += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant")) SurgRightRamusAnt += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert")) SurgRightRamusVert += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll")) SurgRightRamusRoll += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgRightRamusPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw")) SurgRightRamusYaw += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("LeftRamus"))
        {
            if (param.Contains("Lat")) SurgLeftRamusLat += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant")) SurgLeftRamusAnt += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert")) SurgLeftRamusVert += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll")) SurgLeftRamusRoll += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgLeftRamusPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw")) SurgLeftRamusYaw += param.EndsWith("+") ? step : -step;
        }
        else if (param.StartsWith("Chin"))
        {
            if (param.Contains("Lat")) SurgChinLat += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Ant")) SurgChinAnt += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Vert")) SurgChinVert += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Roll")) SurgChinRoll += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Pitch")) SurgChinPitch += param.EndsWith("+") ? step : -step;
            else if (param.Contains("Yaw")) SurgChinYaw += param.EndsWith("+") ? step : -step;
        }
        UpdateSurgeryTransform();
    }

    private System.Windows.Media.Media3D.Transform3D BuildSurgeryTransform(double ant, double lat, double vert, double roll, double pitch, double yaw, System.Windows.Media.Media3D.Point3D center)
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
        // Only move the explicitly separated segments — cranium pieces are fixed reference frame
        var maxilla  = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Maxilla") && s.IsVisible);
        var mandible = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Mandible") && !s.Name.Contains("Cranium") && !s.Name.StartsWith("Ramus") && s.IsVisible);
        var rightRamus = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Ramus Right") && s.IsVisible);
        var leftRamus  = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Ramus Left") && s.IsVisible);
        var chin       = Segments.LastOrDefault(s => s.Name != null && s.Name.Contains("Chin") && s.IsVisible);

        // Helper: set surgical transform on a segment and compose with NHP
        void ApplySurgical(SegmentViewModel? seg, System.Windows.Media.Media3D.Transform3D surgTx)
        {
            if (seg == null) return;
            seg.SurgicalTransform = surgTx;
            seg.Transform = ComposeTransforms(_nhpTransform, surgTx);
        }

        var center = ModelCenter; // Fallback

        var complexCenter = DentalMidlinePoint ?? (center.X, center.Y, center.Z);
        var ptComplex = new System.Windows.Media.Media3D.Point3D(complexCenter.X, complexCenter.Y, complexCenter.Z);
        
        var maxillaTx  = BuildSurgeryTransform(SurgMaxillaAnt, SurgMaxillaLat, SurgMaxillaVert, SurgMaxillaRoll, SurgMaxillaPitch, SurgMaxillaYaw, ptComplex);
        var mandibleTx = BuildSurgeryTransform(SurgMandibleAnt, SurgMandibleLat, SurgMandibleVert, SurgMandibleRoll, SurgMandiblePitch, SurgMandibleYaw, ptComplex);
        var identityTx = System.Windows.Media.Media3D.Transform3D.Identity;

        if (IsManualOcclusionSurgery)
        {
            ApplySurgical(maxilla,  maxillaTx);
            ApplySurgical(mandible, mandibleTx);
        }
        else
        {
            if (IsMaxillaBasedSurgery)
            {
                ApplySurgical(maxilla,  maxillaTx);
                if (IsKeepOcclusionSurgery)
                {
                    var occ = LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
                    var occMat = occ != null ? occ.MandibleOcclusionTransform : System.Windows.Media.Media3D.Matrix3D.Identity;
                    // Mandible snaps into bite and follows maxilla
                    var tg = new System.Windows.Media.Media3D.Transform3DGroup();
                    tg.Children.Add(new System.Windows.Media.Media3D.MatrixTransform3D(occMat));
                    tg.Children.Add(maxillaTx);
                    ApplySurgical(mandible, tg);
                }
                else
                {
                    ApplySurgical(mandible, identityTx);
                }
            }
            else if (IsMandibleBasedSurgery)
            {
                if (IsKeepOcclusionSurgery)
                {
                    var occ = LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
                    var occMat = occ != null ? occ.MandibleOcclusionTransform : System.Windows.Media.Media3D.Matrix3D.Identity;
                    // Maxilla snaps into bite relative to mandible and follows mandible
                    var maxOcclOffsets = occMat; 
                    if (maxOcclOffsets.HasInverse) maxOcclOffsets.Invert();
                    var tg = new System.Windows.Media.Media3D.Transform3DGroup();
                    tg.Children.Add(new System.Windows.Media.Media3D.MatrixTransform3D(maxOcclOffsets));
                    tg.Children.Add(mandibleTx);
                    ApplySurgical(maxilla, tg);
                }
                else
                {
                    ApplySurgical(maxilla, identityTx);
                }
                ApplySurgical(mandible, mandibleTx);
            }
        }

        // 2. Right Ramus uses RightCondyleCenter
        var rcCenter = RightCondyleCenter ?? (center.X, center.Y, center.Z);
        ApplySurgical(rightRamus, BuildSurgeryTransform(SurgRightRamusAnt, SurgRightRamusLat, SurgRightRamusVert, SurgRightRamusRoll, SurgRightRamusPitch, SurgRightRamusYaw, new System.Windows.Media.Media3D.Point3D(rcCenter.X, rcCenter.Y, rcCenter.Z)));

        // 3. Left Ramus uses LeftCondyleCenter
        var lcCenter = LeftCondyleCenter ?? (center.X, center.Y, center.Z);
        ApplySurgical(leftRamus, BuildSurgeryTransform(SurgLeftRamusAnt, SurgLeftRamusLat, SurgLeftRamusVert, SurgLeftRamusRoll, SurgLeftRamusPitch, SurgLeftRamusYaw, new System.Windows.Media.Media3D.Point3D(lcCenter.X, lcCenter.Y, lcCenter.Z)));

        // 4. Chin uses its local centroid — follows mandible movement
        if (chin != null)
        {
            var chinPivot = new System.Windows.Media.Media3D.Point3D(center.X, center.Y, center.Z);
            if (chin.Vertices != null && chin.Vertices.Length > 0)
            {
                double cx = 0, cy = 0, cz = 0;
                for (int vi3 = 0; vi3 < chin.Vertices.Length; vi3 += 3) { cx += chin.Vertices[vi3]; cy += chin.Vertices[vi3 + 1]; cz += chin.Vertices[vi3 + 2]; }
                int chinVCount = chin.Vertices.Length / 3;
                chinPivot = new System.Windows.Media.Media3D.Point3D(cx / chinVCount, cy / chinVCount, cz / chinVCount);
            }
            var chinLocal = BuildSurgeryTransform(SurgChinAnt, SurgChinLat, SurgChinVert, SurgChinRoll, SurgChinPitch, SurgChinYaw, chinPivot);
            // Chin also follows mandible
            System.Windows.Media.Media3D.Transform3D followTx = identityTx;
            if (IsManualOcclusionSurgery || IsMandibleBasedSurgery) followTx = mandibleTx;
            else if (IsMaxillaBasedSurgery && IsKeepOcclusionSurgery) followTx = maxillaTx;
            var chinSurg = new System.Windows.Media.Media3D.Transform3DGroup();
            chinSurg.Children.Add(chinLocal);
            chinSurg.Children.Add(followTx);
            ApplySurgical(chin, chinSurg);
        }
    }

    [RelayCommand]
    private async Task LoadOcclusionAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Occlusion STL",
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

        IsLoading = true;
        StatusText = "Importing Occlusion STL...";

        foreach (var file in dialog.FileNames)
        {
            var vertices = await Task.Run(() => StlIO.LoadStl(file));

            var meshVm = new MeshViewModel
            {
                Name = Path.GetFileNameWithoutExtension(file) + " (Occlusion)",
                Vertices = vertices,
                ColorR = 150, ColorG = 255, ColorB = 150,
                ScanType = DentalScanType.Other,
                IsVisible = true
            };
            meshVm.OnVisibilityChanged = RefreshCombinedModel;
            meshVm.BuildModel();
            LoadedOcclusions.Add(meshVm);
        }

        RefreshCombinedModel();
        StatusText = $"Imported {dialog.FileNames.Length} Occlusion STL(s).";
        IsLoading = false;
    }

    [RelayCommand]
    private void DeleteLoadedOcclusion(MeshViewModel mesh)
    {
        if (mesh == null) return;
        if (System.Windows.MessageBox.Show($"Are you sure you want to delete occlusion '{mesh.Name}'?", "Confirm Delete", 
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;

        LoadedOcclusions.Remove(mesh);
        RefreshCombinedModel();
    }

    [RelayCommand]
    private async Task AlignOcclusions()
    {
        var occlusion = LoadedOcclusions.FirstOrDefault(o => o.IsVisible);
        if (occlusion == null || occlusion.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please make exactly one Occlusion STL visible to align to.", "Select Occlusion", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var maxilla = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Maxilla"));
        var mandible = Segments.FirstOrDefault(s => s.Name != null && s.Name.Contains("Mandible") && !s.Name.Contains("Cranium") && !s.Name.StartsWith("Ramus"));

        if (maxilla == null || mandible == null || maxilla.Vertices == null || mandible.Vertices == null)
        {
            System.Windows.MessageBox.Show("Maxilla or Mandible bone segments not found or segmented. Please segment them first.", "Missing Bones", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        SaveStateForUndo();
        StatusText = "Computing automated occlusion alignment... (1/2: Aligning Occlusion to Maxilla)";
        
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

                // 3. Initial transform: move Occlusion to jawCenter
                double dX = jawCenter.X - occCenter.X;
                double dY = jawCenter.Y - occCenter.Y;
                double dZ = jawCenter.Z - occCenter.Z;
                var initialTx = new double[4, 4] {
                    { 1, 0, 0, dX },
                    { 0, 1, 0, dY },
                    { 0, 0, 1, dZ },
                    { 0, 0, 0, 1 }
                };

                var maxillaVertsList = MeshHelper.ToVertexList(maxilla.Vertices);
                var mandibleVertsList = MeshHelper.ToVertexList(mandible.Vertices);
                var occVertsList = MeshHelper.ToVertexList(occlusion.Vertices);

                // 4. ICP 1: Pull Occlusion (source) to Maxilla (target)
                // We use similar params as DentalAlignmentWindow
                var resultOccToMax = OrthoPlanner.Core.Geometry.IcpAligner.Align(occVertsList, maxillaVertsList, initialTx, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.70);
                
                // Keep maxilla at identity, since we pulled the occlusion to it.
                var maxOccTxMat = System.Windows.Media.Media3D.Matrix3D.Identity;
                
                // Transform the internal occlusion points to their newly aligned position to prepare for stage 2
                OrthoPlanner.Core.Geometry.IcpAligner.TransformVertices(occVertsList, resultOccToMax.Transform);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = "Computing automated occlusion alignment... (2/2: Aligning Mandible to Occlusion)");

                // 5. ICP 2: Pull Mandible (source) to Occlusion (target)
                // The occlusion is now "Maxilla-aligned". Pull the mandible to the lower teeth of the occlusion.
                var initialManTx = new double[4, 4] { {1,0,0,0}, {0,1,0,0}, {0,0,1,0}, {0,0,0,1} };
                var resultManToOcc = OrthoPlanner.Core.Geometry.IcpAligner.Align(mandibleVertsList, occVertsList, initialManTx, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.70);

                var manOccTxMat = ConvertToMatrix3D(resultManToOcc.Transform);

                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    // Store the occlusion transforms
                    occlusion.MaxillaOcclusionTransform = maxOccTxMat;
                    occlusion.MandibleOcclusionTransform = manOccTxMat;

                    // Update GUI transform properties to force trigger UpdateTransforms()
                    // The user requested to set the default behavior based on toggle.
                    // The UpdateSurgeryTransform method will use these.
                    
                    // But first apply transformations visually to the occlusion mesh to show where it landed
                    var finalOccTx = ConvertToMatrix3D(resultOccToMax.Transform);
                    occlusion.Transform = new System.Windows.Media.Media3D.MatrixTransform3D(finalOccTx);
                    
                    UpdateSurgeryTransform();
                    StatusText = $"Successfully aligned Occlusion STL automatically. RMS=" + resultManToOcc.RmsError.ToString("0.000");
                });
            });
        }
        catch (Exception ex)
        {
            StatusText = "Error during automated occlusion alignment: " + ex.Message;
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

    // ─── Volume State ───
    [ObservableProperty] private VolumeData? _volume;
    [ObservableProperty] private bool _isVolumeLoaded;
    [ObservableProperty] private string _statusText = "Ready — Open a DICOM folder to begin";
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;
    private string? _lastDicomPath;
    
    // Original Volume to prevent additive NHP reslicing
    [ObservableProperty] private VolumeData? _originalVolume;
    private string? _originalVolumeTempPath; // GZip-compressed OriginalVolume on disk (saves ~200 MB)

    // ─── Patient Info ───
    [ObservableProperty] private string _patientName = "";
    [ObservableProperty] private string _studyDate = "";
    [ObservableProperty] private string _seriesDescription = "";
    [ObservableProperty] private string _volumeDimensions = "";

    // ─── 2D Slice Indices ───
    [ObservableProperty] private int _totalSlices;
    [ObservableProperty] private int _currentSlice;
    [ObservableProperty] private int _axialIndex;
    [ObservableProperty] private int _coronalIndex;
    [ObservableProperty] private int _sagittalIndex;

    // ─── Headlamp direction (updated by MainWindow as camera moves) ───
    [ObservableProperty] private System.Windows.Media.Media3D.Vector3D _headlampDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);

    // ─── 3D Viewport Anchors ───
    [ObservableProperty] private System.Windows.Media.Media3D.Point3D _modelCenter = new System.Windows.Media.Media3D.Point3D(0, 0, 0);

    [ObservableProperty] private int _axialMax = 1;
    [ObservableProperty] private int _coronalMax = 1;
    [ObservableProperty] private int _sagittalMax = 1;
    
    // Proportional Heights for 1:1 Anatomical Scale in UI Viewports
    [ObservableProperty] private System.Windows.GridLength _axialDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _coronalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);
    [ObservableProperty] private System.Windows.GridLength _sagittalDisplayHeight = new System.Windows.GridLength(1.0, System.Windows.GridUnitType.Star);

    // ─── Windowing ───
    [ObservableProperty] private double _windowCenter = 40;
    [ObservableProperty] private double _windowWidth = 2000;

    // ─── 3D Iso Threshold ───
    [ObservableProperty] private double _isoThreshold = 300;
    [ObservableProperty] private double _isoMin = -1024;
    [ObservableProperty] private double _isoMax = 3071;

    // ─── Slice Images ───
    [ObservableProperty] private WriteableBitmap? _axialImage;
    [ObservableProperty] private WriteableBitmap? _coronalImage;
    [ObservableProperty] private WriteableBitmap? _sagittalImage;
    [ObservableProperty] private HelixToolkit.SharpDX.Geometry3D? _geometry;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material? _material;
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    // ─── Named Anatomy ───
    public SegmentViewModel? HardTissueModel { get; private set; }
    public SegmentViewModel? SoftTissueModel { get; private set; }
    public SegmentViewModel? DentalModel { get; private set; }

    // ─── Condylar Axis (set by Split Cranium/Mandible wizard) ───
    public (double X, double Y, double Z)? LeftCondyleCenter { get; set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; set; }
    public (double X, double Y, double Z)? DentalMidlinePoint { get; set; }

    // ─── HU Histograms (Independent) ───
    [ObservableProperty] private WriteableBitmap? _boneHistogramImage;
    [ObservableProperty] private WriteableBitmap? _softHistogramImage;
    [ObservableProperty] private WriteableBitmap? _dentalHistogramImage;
    [ObservableProperty] private WriteableBitmap? _customHistogramImage;

    // ─── Segmentation (Independent Thresholds) ───
    private SegmentationVolume? _segVolume;
    private string? _boneOnlySegVolumeTempPath; // GZip-compressed bone mask on disk (saves ~105 MB)
    private byte _boneOnlySegVolumeLabel; // Store the bone label for reconstruction
    [ObservableProperty] private double _boneMinHU = 400;
    [ObservableProperty] private double _boneMaxHU = 3071;
    [ObservableProperty] private bool _showBoneOverlay;

    [ObservableProperty] private double _softMinHU = -300;
    [ObservableProperty] private double _softMaxHU = 3071;
    [ObservableProperty] private bool _showSoftOverlay;

    [ObservableProperty] private double _dentalMinHU = 2000;
    [ObservableProperty] private double _dentalMaxHU = 3071;
    [ObservableProperty] private bool _showDentalOverlay;

    [ObservableProperty] private double _customMinHU = 200;
    [ObservableProperty] private double _customMaxHU = 3071;
    [ObservableProperty] private bool _showCustomOverlay;

    [ObservableProperty] private bool _showSegmentation;

    // ─── Undo/Redo Stacks ───
    private readonly Stack<StateSnapshot> _undoStack = new();
    private readonly Stack<StateSnapshot> _redoStack = new();

    private class StateSnapshot
    {
        public List<SegmentViewModel> Segments { get; init; } = new();
        public List<MeshViewModel> ImportedMeshes { get; init; } = new();
        public int HardTissueModelIndex { get; init; } = -1;
        public int SoftTissueModelIndex { get; init; } = -1;
        public int DentalModelIndex { get; init; } = -1;
    }

    // ─── Direct Volume Rendering (Diffused View) ───
    [ObservableProperty] private bool _isVolumeRenderingEnabled;
    [ObservableProperty] private HelixToolkit.SharpDX.Model.Scene.GroupNode? _volumeNode;

    partial void OnIsVolumeRenderingEnabledChanged(bool value)
    {
        if (value && VolumeNode == null && Volume != null)
        {
            SetupVolumeMaterial();
        }
        else if (!value && VolumeNode != null)
        {
            // Release DVR texture data when disabled — saves ~420 MB
            VolumeNode = null;
        }
    }

    private void SetupVolumeMaterial()
    {
        if (Volume == null) return;

        int w = Volume.Width;
        int h = Volume.Height;
        int d = Volume.Depth;

        // Use R8G8B8A8_UNorm instead of R16G16B16A16_Float:
        // saves ~1260 MB (840 MB Half[] + 840 MB copy → 420 MB byte[], no copy)
        var pixels = new byte[Volume.Voxels.Length * 4];
        
        for (int i = 0; i < Volume.Voxels.Length; i++)
        {
            float hu = Volume.Voxels[i];
            float val = (hu + 1024f) / 4000f; 
            val = Math.Clamp(val, 0f, 1f);
            byte gray = (byte)(val * 255f);
            byte alpha = (hu > 200) ? (byte)76 : (byte)0; // 0.3 * 255 ≈ 76
            
            int j = i * 4;
            pixels[j]     = gray;  // R
            pixels[j + 1] = gray;  // G
            pixels[j + 2] = gray;  // B
            pixels[j + 3] = alpha; // A
        }

        var texParams = new HelixToolkit.SharpDX.Model.VolumeTextureParams(
            pixels, // byte[] passed directly — no MemoryMarshal copy needed
            w, h, d, SharpDX.DXGI.Format.R8G8B8A8_UNorm
        );

        // Creates a 1D gradient texture for the lookup map
        var mapPixels = new HelixToolkit.Maths.Color4[] { 
            new HelixToolkit.Maths.Color4(0f, 0f, 0f, 0f),       // Black trans
            new HelixToolkit.Maths.Color4(1f, 0.9f, 0.8f, 0.05f), // Pale bone
            new HelixToolkit.Maths.Color4(1f, 1f, 1f, 0.5f)       // Solid bone
        };

        var volumeMaterial = new HelixToolkit.SharpDX.Model.VolumeTextureRawDataMaterialCore
        {
            VolumeTexture = texParams,
            Color = new HelixToolkit.Maths.Color4(1f, 1f, 1f, 1f),
            TransferMap = mapPixels,
            SampleDistance = 0.0015,
            MaxIterations = 1500,
            IterationOffset = 1
        };

        float sizeX = (float)(w * Volume.Spacing[0]);
        float sizeY = (float)(h * Volume.Spacing[1]);
        float sizeZ = (float)(d * Volume.Spacing[2]);

        var node = new HelixToolkit.SharpDX.Model.Scene.VolumeTextureNode
        {
            Material = volumeMaterial,
            ModelMatrix = System.Numerics.Matrix4x4.CreateScale(sizeX, sizeY, sizeZ) * 
                          System.Numerics.Matrix4x4.CreateTranslation(sizeX / 2f, sizeY / 2f, sizeZ / 2f)
        };

        var group = new HelixToolkit.SharpDX.Model.Scene.GroupNode();
        group.AddChildNode(node);
        VolumeNode = group;
    }

    // ─── Live 3D Preview ───
    [ObservableProperty] private HelixToolkit.SharpDX.Geometry3D? _livePreviewGeometry;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material? _livePreviewMaterial;
    private System.Threading.CancellationTokenSource? _previewDebounceCts;

    private async void TriggerLivePreviewUpdate()
    {
        if (Volume == null || !ShowBoneOverlay) return;
        
        _previewDebounceCts?.Cancel();
        _previewDebounceCts = new System.Threading.CancellationTokenSource();
        var token = _previewDebounceCts.Token;

        try
        {
            await Task.Delay(150, token); // Debounce trailing edge by 150ms
            
            if (token.IsCancellationRequested) return;

            short min = (short)BoneMinHU;
            short max = (short)BoneMaxHU;
            
            var verts = await Task.Run(() => 
                SegmentationEngine.ExtractLivePreviewMesh(Volume, min, max, 1), token);

            if (token.IsCancellationRequested) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var positions = new List<System.Numerics.Vector3>();
                var indices = new List<int>();
                var dict = new Dictionary<(float, float, float), int>();

                for (int vi = 0; vi < verts.Length; vi += 3)
                {
                    float vx = verts[vi], vy = verts[vi + 1], vz = verts[vi + 2];
                    var key = (vx, vy, vz);
                    if (!dict.TryGetValue(key, out int idx))
                    {
                        idx = positions.Count;
                        positions.Add(new System.Numerics.Vector3(vx, vy, vz));
                        dict[key] = idx;
                    }
                    indices.Add(idx);
                }

                var builder = new HelixToolkit.Geometry.MeshBuilder(false, false);
                foreach (var p in positions) builder.Positions.Add(p);
                foreach (var i in indices) builder.TriangleIndices.Add(i);
                
                var mesh = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
                mesh.UpdateNormals();

                LivePreviewGeometry = mesh;
                
                if (LivePreviewMaterial == null)
                {
                    LivePreviewMaterial = new HelixToolkit.Wpf.SharpDX.PhongMaterial 
                    { 
                        DiffuseColor = new HelixToolkit.Maths.Color4(230/255f, 210/255f, 180/255f, 1.0f),
                        RenderEnvironmentMap = true
                    };
                }
            });
        }
        catch (TaskCanceledException) { }
    }

    // ─── Region Growing Removed ───

    // ─── NHP Parameters (Live adjusted) ───
    [ObservableProperty] private double _nhpLateral = 0.0;
    [ObservableProperty] private double _nhpAnteroposterior = 0.0;
    [ObservableProperty] private double _nhpVertical = 0.0;
    [ObservableProperty] private double _nhpRoll = 0.0;
    [ObservableProperty] private double _nhpPitch = 0.0;
    [ObservableProperty] private double _nhpYaw = 0.0;

    // ─── NHP Committed State (Baseline) ───
    private double _cLat, _cAnt, _cVert, _cRoll, _cPitch, _cYaw;
    // The current NHP delta transform (applied to ALL segments on top of their surgical offsets)
    private System.Windows.Media.Media3D.Transform3D _nhpTransform = System.Windows.Media.Media3D.Transform3D.Identity;
    public Rect3D BoneOnlyBounds { get; private set; } = Rect3D.Empty; // Bone segment bounds only, excludes imported STL meshes


    public bool IsNhpDirty => Math.Abs(NhpLateral - _cLat) > 0.01 || 
                              Math.Abs(NhpAnteroposterior - _cAnt) > 0.01 ||
                              Math.Abs(NhpVertical - _cVert) > 0.01 ||
                              Math.Abs(NhpRoll - _cRoll) > 0.01 ||
                              Math.Abs(NhpPitch - _cPitch) > 0.01 ||
                              Math.Abs(NhpYaw - _cYaw) > 0.01;

    public bool HasModelLoaded => !BoneOnlyBounds.IsEmpty || Volume != null || Segments.Count > 0;
    
    public bool IsCompletelyLoaded =>
        HasModelLoaded && (Volume != null || !BoneOnlyBounds.IsEmpty) && !IsLoading;

    // Prevent camera jumping during automated splits
    public bool IsSplitting { get; set; }

    partial void OnNhpLateralChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpAnteroposteriorChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpVerticalChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpRollChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpPitchChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }
    partial void OnNhpYawChanged(double value) { OnPropertyChanged(nameof(IsNhpDirty)); UpdateNhpTransform(); }

    [RelayCommand]
    private void AdjustNhp(string param)
    {
        double step = 0.1;
        if (param.Contains("Lat")) NhpLateral += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Ant")) NhpAnteroposterior += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Vert")) NhpVertical += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Roll")) NhpRoll += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Pitch")) NhpPitch += param.EndsWith("+") ? step : -step;
        else if (param.Contains("Yaw")) NhpYaw += param.EndsWith("+") ? step : -step;
    }

    [RelayCommand]
    private async Task CommitNhpAsync()
    {
        // Capture the uncommitted delta BEFORE locking it in as the new baseline
        double dPitch = NhpPitch - _cPitch;
        double dRoll  = NhpRoll  - _cRoll;
        double dYaw   = NhpYaw   - _cYaw;
        double dLat   = NhpLateral         - _cLat;
        double dAnt   = NhpAnteroposterior - _cAnt;
        double dVert  = NhpVertical        - _cVert;

        // Now lock in as the new committed baseline
        _cLat = NhpLateral; _cAnt = NhpAnteroposterior; _cVert = NhpVertical;
        _cRoll = NhpRoll; _cPitch = NhpPitch; _cYaw = NhpYaw;
        OnPropertyChanged(nameof(IsNhpDirty));

        // Start Reslice Engine, passing the true delta that needs to be baked
        await PerformPhysicalResliceAsync(dPitch, dRoll, dYaw, dLat, dAnt, dVert);
    }

    private void UpdateNhpTransform()
    {
        if (BoneOnlyBounds.IsEmpty) return;

        // Use bone-only bounds for camera centering (ignore imported STL meshes)
        var bounds = BoneOnlyBounds;
        var center = new Point3D(bounds.X + bounds.SizeX/2, bounds.Y + bounds.SizeY/2, bounds.Z + bounds.SizeZ/2);

        // DELTA MATH: Only visually rotate/translate by the *difference* between the current UI values
        // and the physically baked (committed) values. This prevents compounding geometry.
        var dPitch = NhpPitch - _cPitch;
        var dRoll  = NhpRoll  - _cRoll;
        var dYaw   = NhpYaw   - _cYaw;
        var dLat   = NhpLateral         - _cLat;
        var dAnt   = NhpAnteroposterior - _cAnt;
        var dVert  = NhpVertical        - _cVert;

        var nhp = new Transform3DGroup();
        nhp.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        nhp.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        nhp.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));

        // Store so surgery transforms can compose on top
        _nhpTransform = nhp;

        // Apply: NHP first, then per-segment surgical offset on top
        if (HardTissueModel != null) HardTissueModel.Transform = _nhpTransform;
        if (SoftTissueModel != null) SoftTissueModel.Transform = _nhpTransform;
        if (DentalModel != null)     DentalModel.Transform     = _nhpTransform;
        foreach (var seg  in Segments)      seg.Transform  = ComposeTransforms(_nhpTransform, seg.SurgicalTransform);
        foreach (var mesh in ImportedMeshes) mesh.Transform = _nhpTransform;

        // Dynamically enforce the freehand rotation pivot point!
        ModelCenter = nhp.Transform(center);
    }

    /// <summary>Composes two transforms: applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    private static System.Windows.Media.Media3D.Transform3D ComposeTransforms(
        System.Windows.Media.Media3D.Transform3D first,
        System.Windows.Media.Media3D.Transform3D second)
    {
        if (second == System.Windows.Media.Media3D.Transform3D.Identity) return first;
        var g = new System.Windows.Media.Media3D.Transform3DGroup();
        g.Children.Add(first);
        g.Children.Add(second);
        return g;
    }


    // ─── Viewport toggles ───
    [ObservableProperty] private bool _isOrthographic;
    [ObservableProperty] private bool _showGrid;

    // ─── MPR toggles ───
    [ObservableProperty] private bool _showCrosshairs = true;
    [ObservableProperty] private int _enlargedView; // 0=none, 1=axial, 2=coronal, 3=sagittal
    [ObservableProperty] private int _rightPanelTabIndex = 0; // 0=CT, 1=Measurements, 2=Surgery
    public ObservableCollection<SegmentViewModel> Segments { get; } = new();

    // ─── Imported Meshes ───
    public ObservableCollection<MeshViewModel> ImportedMeshes { get; } = new();

    partial void OnAxialIndexChanged(int value) => UpdateAxialSlice();
    partial void OnCoronalIndexChanged(int value) => UpdateCoronalSlice();
    partial void OnSagittalIndexChanged(int value) => UpdateSagittalSlice();
    partial void OnWindowCenterChanged(double value) => UpdateAllSlices();
    partial void OnWindowWidthChanged(double value) => UpdateAllSlices();

    partial void OnIsoThresholdChanged(double value)
    {
        // Removed Base CT thresholding
    }

    partial void OnBoneMinHUChanged(double value) { UpdateHistograms(); TriggerLivePreviewUpdate(); if (ShowBoneOverlay) UpdateAllSlices(); }
    partial void OnBoneMaxHUChanged(double value) { UpdateHistograms(); TriggerLivePreviewUpdate(); if (ShowBoneOverlay) UpdateAllSlices(); }
    partial void OnSoftMinHUChanged(double value) { UpdateHistograms(); if (ShowSoftOverlay) UpdateAllSlices(); }
    partial void OnSoftMaxHUChanged(double value) { UpdateHistograms(); if (ShowSoftOverlay) UpdateAllSlices(); }
    partial void OnDentalMinHUChanged(double value) { UpdateHistograms(); if (ShowDentalOverlay) UpdateAllSlices(); }
    partial void OnDentalMaxHUChanged(double value) { UpdateHistograms(); if (ShowDentalOverlay) UpdateAllSlices(); }
    partial void OnCustomMinHUChanged(double value) { UpdateHistograms(); if (ShowCustomOverlay) UpdateAllSlices(); }
    partial void OnCustomMaxHUChanged(double value) { UpdateHistograms(); if (ShowCustomOverlay) UpdateAllSlices(); }

    partial void OnShowBoneOverlayChanged(bool value)
    {
        if (value) { ShowSoftOverlay = false; ShowDentalOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
        TriggerLivePreviewUpdate();
    }

    [ObservableProperty] private bool _enhanceSegmentation = true;
    [ObservableProperty] private bool _closeHolesAfterMerge = false;
    [ObservableProperty] private bool _cleanDentalSegmentation = true;

    // ─── Environment Lighting ───
    [ObservableProperty] private byte _frontLightIntensity = 180;
    partial void OnFrontLightIntensityChanged(byte value) { OnPropertyChanged(nameof(FrontLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private double _frontLightZ = 0.0; // Straight frontal
    partial void OnFrontLightZChanged(double value) { OnPropertyChanged(nameof(FrontLightDirection)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _bottomLightIntensity = 100;
    partial void OnBottomLightIntensityChanged(byte value) { OnPropertyChanged(nameof(BottomLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _leftRightLightIntensity = 80;
    partial void OnLeftRightLightIntensityChanged(byte value) { OnPropertyChanged(nameof(LeftRightLightColor)); RefreshCombinedModel(); }

    [ObservableProperty] private byte _backLightIntensity = 80;
    partial void OnBackLightIntensityChanged(byte value) { OnPropertyChanged(nameof(BackLightColor)); RefreshCombinedModel(); }

    // Color properties for XAML Binding
    public Color AmbientLightColor => Color.FromRgb(30, 30, 35);
    public Color FrontLightColor => Color.FromRgb(FrontLightIntensity, FrontLightIntensity, FrontLightIntensity);
    public Color BottomLightColor => Color.FromRgb(BottomLightIntensity, BottomLightIntensity, BottomLightIntensity);
    public Color LeftRightLightColor => Color.FromRgb(LeftRightLightIntensity, LeftRightLightIntensity, LeftRightLightIntensity);
    public Color BackLightColor => Color.FromRgb(BackLightIntensity, BackLightIntensity, BackLightIntensity);
    public Vector3D FrontLightDirection => new Vector3D(0, 1, FrontLightZ);
    partial void OnShowSoftOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowDentalOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
    }
    partial void OnShowDentalOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowSoftOverlay = false; ShowCustomOverlay = false; }
        UpdateAllSlices();
    }
    partial void OnShowCustomOverlayChanged(bool value)
    {
        if (value) { ShowBoneOverlay = false; ShowSoftOverlay = false; ShowDentalOverlay = false; }
        UpdateAllSlices();
    }

    [RelayCommand]
    private void SaveProject()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save OrthoPlanner Project",
            Filter = "OrthoPlanner Project (*.orthoplan)|*.orthoplan",
            DefaultExt = ".orthoplan"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            StatusText = "Saving project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Create);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            // 1. project.json — metadata
            var meta = new
            {
                Version = "2.0",
                PatientName,
                StudyDate,
                Segmentation = new
                {
                    BoneMinHU, BoneMaxHU,
                    SoftMinHU, SoftMaxHU,
                    DentalMinHU, DentalMaxHU,
                    CustomMinHU, CustomMaxHU,
                    Segments = Segments.Select(s => new { s.Name, s.IsVisible, s.ColorR, s.ColorG, s.ColorB }).ToArray()
                },
                ImportedMeshes = ImportedMeshes.Select(m => new { m.Name, m.IsVisible, m.ColorR, m.ColorG, m.ColorB }).ToArray(),
                Volume = Volume != null ? new { Volume.Width, Volume.Height, Volume.Depth, Volume.Spacing } : null,
                WindowCenter,
                WindowWidth
            };
            var jsonEntry = zip.CreateEntry("project.json");
            using (var sw = new StreamWriter(jsonEntry.Open()))
            {
                sw.Write(System.Text.Json.JsonSerializer.Serialize(meta,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }

            // 2. volume.bin — raw voxel data
            if (Volume != null)
            {
                var volEntry = zip.CreateEntry("volume.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var volStream = volEntry.Open();
                var bytes = new byte[Volume.Voxels.Length * 2];
                Buffer.BlockCopy(Volume.Voxels, 0, bytes, 0, bytes.Length);
                volStream.Write(bytes, 0, bytes.Length);
            }

            // 3. meshes/*.bin — imported STL vertex data
            for (int i = 0; i < ImportedMeshes.Count; i++)
            {
                var mesh = ImportedMeshes[i];
                if (mesh.Vertices == null) continue;
                var meshEntry = zip.CreateEntry($"meshes/{i}_{mesh.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var ms = meshEntry.Open();
                using var bw = new BinaryWriter(ms);
                bw.Write(mesh.Vertices.Length / 3);
                for (int vi = 0; vi < mesh.Vertices.Length; vi += 3)
                    { bw.Write(mesh.Vertices[vi]); bw.Write(mesh.Vertices[vi + 1]); bw.Write(mesh.Vertices[vi + 2]); }
            }

            // 4. segments/*.bin — segmented 3D model vertex data
            for (int i = 0; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                if (seg.Vertices == null) continue;
                var segEntry = zip.CreateEntry($"segments/{i}_{seg.Name}.bin", System.IO.Compression.CompressionLevel.Fastest);
                using var ss = segEntry.Open();
                using var bw2 = new BinaryWriter(ss);
                bw2.Write(seg.Vertices.Length / 3);
                for (int vi = 0; vi < seg.Vertices.Length; vi += 3)
                    { bw2.Write(seg.Vertices[vi]); bw2.Write(seg.Vertices[vi + 1]); bw2.Write(seg.Vertices[vi + 2]); }
            }

            StatusText = $"Project saved: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (IsVolumeLoaded)
        {
            var res = System.Windows.MessageBox.Show(
                "A project is already open. Do you want to save it before opening another?",
                "Save Current Project?", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
            
            if (res == System.Windows.MessageBoxResult.Cancel) return;
            if (res == System.Windows.MessageBoxResult.Yes) SaveProject();
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open OrthoPlanner Project",
            Filter = "OrthoPlanner Project (*.orthoplan)|*.orthoplan|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            StatusText = "Loading project...";

            using var fs = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read);
            using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);

            // 1. Read project.json
            var jsonEntry = zip.GetEntry("project.json");
            if (jsonEntry == null) { StatusText = "Invalid project file"; return; }

            string json;
            using (var sr = new StreamReader(jsonEntry.Open()))
                json = await sr.ReadToEndAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            PatientName = root.GetProperty("PatientName").GetString() ?? "";
            StudyDate = root.GetProperty("StudyDate").GetString() ?? "";
            WindowCenter = root.GetProperty("WindowCenter").GetDouble();
            WindowWidth = root.GetProperty("WindowWidth").GetDouble();
            var segNode = root.GetProperty("Segmentation");
            
            // Backwards compatibility for older project files
            if (segNode.TryGetProperty("MinHU", out var minHuProp))
            {
                CustomMinHU = minHuProp.GetDouble();
                CustomMaxHU = segNode.GetProperty("MaxHU").GetDouble();
            }
            else
            {
                BoneMinHU = segNode.GetProperty("BoneMinHU").GetDouble();
                BoneMaxHU = segNode.GetProperty("BoneMaxHU").GetDouble();
                SoftMinHU = segNode.GetProperty("SoftMinHU").GetDouble();
                SoftMaxHU = segNode.GetProperty("SoftMaxHU").GetDouble();
                DentalMinHU = segNode.GetProperty("DentalMinHU").GetDouble();
                DentalMaxHU = segNode.GetProperty("DentalMaxHU").GetDouble();
                CustomMinHU = segNode.GetProperty("CustomMinHU").GetDouble();
                CustomMaxHU = segNode.GetProperty("CustomMaxHU").GetDouble();
            }

            // 2. Read volume.bin
            var volMeta = root.GetProperty("Volume");
            if (volMeta.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                int w = volMeta.GetProperty("Width").GetInt32();
                int h = volMeta.GetProperty("Height").GetInt32();
                int d = volMeta.GetProperty("Depth").GetInt32();
                var spacingArr = volMeta.GetProperty("Spacing");
                double[] spacing = new double[3];
                for (int i = 0; i < 3; i++)
                    spacing[i] = spacingArr[i].GetDouble();

                var volEntry = zip.GetEntry("volume.bin");
                if (volEntry != null)
                {
                    var vol = new VolumeData(w, h, d, spacing);
                    using var volStream = volEntry.Open();
                    var bytes = new byte[vol.Voxels.Length * 2];
                    int totalRead = 0;
                    while (totalRead < bytes.Length)
                    {
                        int read = await volStream.ReadAsync(bytes, totalRead, bytes.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    Buffer.BlockCopy(bytes, 0, vol.Voxels, 0, bytes.Length);
                    vol.PatientName = PatientName;
                    vol.StudyDate = StudyDate;
                    vol.ComputeMinMax();

                    Volume = vol;
                    OriginalVolume = null; // Reset starting position for new project
                    CleanupTempFiles(); // Delete any temp files from previous session
                    IsVolumeLoaded = true;
                    IsoMin = Math.Max(-1000, (double)vol.MinValue);
                    IsoMax = vol.MaxValue;
                    AxialMax = vol.Depth - 1;
                    CoronalMax = vol.Height - 1;
                    SagittalMax = vol.Width - 1;
                    AxialIndex = vol.Depth / 2;
                    CoronalIndex = vol.Height / 2;
                    SagittalIndex = vol.Width / 2;
                    UpdateHistograms();
                    UpdateAllSlices();
                }
            }

            // 3. Read imported meshes
            ImportedMeshes.Clear();
            var meshesArr = root.GetProperty("ImportedMeshes");
            int meshIdx = 0;
            foreach (var meshMeta in meshesArr.EnumerateArray())
            {
                string name = meshMeta.GetProperty("Name").GetString() ?? $"Mesh_{meshIdx}";
                var meshEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith($"meshes/{meshIdx}_"));
                if (meshEntry != null)
                {
                    using var ms = meshEntry.Open();
                    using var br = new BinaryReader(ms);
                    int count = br.ReadInt32();
                    var vertices = new float[count * 3];
                    for (int i = 0; i < count; i++)
                    { vertices[i * 3] = br.ReadSingle(); vertices[i * 3 + 1] = br.ReadSingle(); vertices[i * 3 + 2] = br.ReadSingle(); }

                    var meshVm = new MeshViewModel
                    {
                        Name = name,
                        Vertices = vertices,
                        ColorR = meshMeta.TryGetProperty("ColorR", out var cr) ? cr.GetByte() : (byte)245,
                        ColorG = meshMeta.TryGetProperty("ColorG", out var cg) ? cg.GetByte() : (byte)245,
                        ColorB = meshMeta.TryGetProperty("ColorB", out var cb) ? cb.GetByte() : (byte)230,
                        IsVisible = meshMeta.GetProperty("IsVisible").GetBoolean()
                    };
                    meshVm.OnVisibilityChanged = RefreshCombinedModel;
                    meshVm.BuildModel();
                    ImportedMeshes.Add(meshVm);
                }
                meshIdx++;
            }

            // 4. Read segments
            Segments.Clear();
            if (root.TryGetProperty("Segmentation", out var segProp) && segProp.TryGetProperty("Segments", out var segsArr))
            {
                int segIdx = 0;
                foreach (var segMeta in segsArr.EnumerateArray())
                {
                    string sName = segMeta.GetProperty("Name").GetString() ?? $"Segment_{segIdx}";
                    var segEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith($"segments/{segIdx}_"));
                    if (segEntry != null)
                    {
                        using var ss = segEntry.Open();
                        using var br2 = new BinaryReader(ss);
                        int cnt = br2.ReadInt32();
                        var verts = new float[cnt * 3];
                        for (int i = 0; i < cnt; i++)
                        { verts[i * 3] = br2.ReadSingle(); verts[i * 3 + 1] = br2.ReadSingle(); verts[i * 3 + 2] = br2.ReadSingle(); }

                        var segVm = new SegmentViewModel
                        {
                            Name = sName,
                            Vertices = verts,
                            ColorR = segMeta.TryGetProperty("ColorR", out var scr) ? scr.GetByte() : (byte)200,
                            ColorG = segMeta.TryGetProperty("ColorG", out var scg) ? scg.GetByte() : (byte)180,
                            ColorB = segMeta.TryGetProperty("ColorB", out var scb) ? scb.GetByte() : (byte)140,
                            IsVisible = segMeta.GetProperty("IsVisible").GetBoolean()
                        };
                        segVm.OnVisibilityChanged = RefreshCombinedModel;
                        segVm.BuildModel();
                        Segments.Add(segVm);

                        // Restore named properties
                        if (sName == "Bone" || sName.StartsWith("Bone")) HardTissueModel = segVm;
                        else if (sName == "Soft Tissue" || sName.StartsWith("Soft Tissue")) SoftTissueModel = segVm;
                        else if (sName == "Dental Scan" || sName.StartsWith("Dental")) DentalModel = segVm;
                    }
                    segIdx++;
                }
            }

            RefreshCombinedModel();
            StatusText = $"Project loaded: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task OpenDicomFolderAsync()
    {
        if (IsVolumeLoaded)
        {
            var res = System.Windows.MessageBox.Show(
                "A project is already open. Do you want to save it before starting a new session?",
                "Save Current Project?", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
            
            if (res == System.Windows.MessageBoxResult.Cancel) return;
            if (res == System.Windows.MessageBoxResult.Yes) SaveProject();
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select DICOM / CBCT Folder"
        };

        if (dialog.ShowDialog() != true) return;

        await LoadDicomAsync(dialog.FolderName);
    }

    private async Task LoadDicomAsync(string folderPath)
    {
        try
        {
            IsLoading = true;
            _lastDicomPath = folderPath;
            StatusText = "Scanning DICOM folder...";
            LoadProgress = 0;

            // Reset existing state
            Segments.Clear();
            ImportedMeshes.Clear();
            _segVolume = null;

            var seriesList = await Task.Run(() =>
                DicomLoader.ScanFolderAsync(folderPath, p =>
                    Application.Current.Dispatcher.Invoke(() => LoadProgress = p * 40)));

            if (seriesList.Count == 0)
            {
                StatusText = "No valid DICOM series found.";
                IsLoading = false;
                return;
            }

            // Always show the selector dialog to confirm Patient details and Preview
            var selectorVm = new DicomSelectorViewModel(seriesList);
            var dialog = new OrthoPlanner.App.Views.DicomSelectorWindow(selectorVm)
            {
                Owner = Application.Current.MainWindow
            };
            
            dialog.ShowDialog();

            if (!selectorVm.Accepted || selectorVm.SelectedSeries == null)
            {
                StatusText = "Load cancelled.";
                IsLoading = false;
                return;
            }

            StatusText = $"Loading series ({selectorVm.SelectedSeries.Info.ImageCount} slices)...";

            Volume = await Task.Run(() =>
                DicomLoader.LoadSeriesAsync(selectorVm.SelectedSeries.Info.FilePaths, p =>
                    Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 60)));

            OriginalVolume = null; // Reset starting position for new DICOM
            CleanupTempFiles(); // Delete any temp files from previous session

            // Update UI state
            PatientName = Volume.PatientName;
            StudyDate = Volume.StudyDate;
            SeriesDescription = Volume.SeriesDescription;
            VolumeDimensions = $"{Volume.Width} × {Volume.Height} × {Volume.Depth}";

            if (Volume == null) return;
        
            IsLoading = true;
            StatusText = "Drawing Projections...";
            
            AxialMax = Volume.Depth - 1;
            CoronalMax = Volume.Height - 1;
            SagittalMax = Volume.Width - 1;

            AxialIndex = Volume.Depth / 2;
            CoronalIndex = Volume.Height / 2;
            SagittalIndex = Volume.Width / 2;
            
            // Push the physical aspect ratios to the Grid Rows so the UI enforces 1:1 squares visually
            // Because the Grid widths are uniform "*", we scale height directly mapping to Voxel spread.
            AxialDisplayHeight = new System.Windows.GridLength(Volume.Height * Volume.Spacing[1], System.Windows.GridUnitType.Star);
            CoronalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);

            IsoMin = -1000; // Always start exactly at -1000 (air) for predictable UI
            IsoMax = Volume.MaxValue;

            IsVolumeLoaded = true;
            UpdateAllSlices();
            UpdateHistograms();
            RefreshCombinedModel(); // Force UI Camera to center on the raw Volume bounds

            StatusText = $"Loaded: {Volume.PatientName} — {Volume.Depth} slices";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 100;
        }
    }

    private void UpdateAllSlices()
    {
        UpdateAxialSlice();
        UpdateCoronalSlice();
        UpdateSagittalSlice();
    }

    private bool GetActiveThreshold(out double min, out double max)
    {
        if (ShowBoneOverlay) { min = BoneMinHU; max = BoneMaxHU; return true; }
        if (ShowSoftOverlay) { min = SoftMinHU; max = SoftMaxHU; return true; }
        if (ShowDentalOverlay) { min = DentalMinHU; max = DentalMaxHU; return true; }
        if (ShowCustomOverlay) { min = CustomMinHU; max = CustomMaxHU; return true; }
        min = 0; max = 0;
        return false;
    }

    private void UpdateAxialSlice()
    {
        if (Volume == null) return;
        if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetAxialSliceBgra(AxialIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            AxialImage = CreateBgraBitmap(data, Volume.Width, Volume.Height,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
        else
        {
            var data = Volume.GetAxialSlice(AxialIndex, WindowCenter, WindowWidth);
            AxialImage = CreateGrayscaleBitmap(data, Volume.Width, Volume.Height,
                Volume.Spacing[0], Volume.Spacing[1]);
        }
    }

    private void UpdateCoronalSlice()
    {
        if (Volume == null) return;
        if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetCoronalSliceBgra(CoronalIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            CoronalImage = CreateBgraBitmap(data, Volume.Width, Volume.Depth,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetCoronalSlice(CoronalIndex, WindowCenter, WindowWidth);
            CoronalImage = CreateGrayscaleBitmap(data, Volume.Width, Volume.Depth,
                Volume.Spacing[0], Volume.Spacing[2]);
        }
    }

    private void UpdateSagittalSlice()
    {
        if (Volume == null) return;
        if (GetActiveThreshold(out double min, out double max))
        {
            var data = Volume.GetSagittalSliceBgra(SagittalIndex, WindowCenter, WindowWidth,
                (short)min, (short)max);
            SagittalImage = CreateBgraBitmap(data, Volume.Height, Volume.Depth,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
        else
        {
            var data = Volume.GetSagittalSlice(SagittalIndex, WindowCenter, WindowWidth);
            SagittalImage = CreateGrayscaleBitmap(data, Volume.Height, Volume.Depth,
                Volume.Spacing[1], Volume.Spacing[2]);
        }
    }

    private WriteableBitmap CreateGrayscaleBitmap(byte[] pixels, int w, int h,
        double spacingCol, double spacingRow)
    {
        double minSpacing = Math.Min(spacingCol, spacingRow);
        double dpiX = 96.0 * minSpacing / spacingCol;
        double dpiY = 96.0 * minSpacing / spacingRow;
        var bmp = new WriteableBitmap(w, h, dpiX, dpiY, PixelFormats.Gray8, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w, 0);
        return bmp;
    }

    private WriteableBitmap CreateBgraBitmap(byte[] pixels, int w, int h,
        double spacingCol, double spacingRow)
    {
        double minSpacing = Math.Min(spacingCol, spacingRow);
        double dpiX = 96.0 * minSpacing / spacingCol;
        double dpiY = 96.0 * minSpacing / spacingRow;
        var bmp = new WriteableBitmap(w, h, dpiX, dpiY, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        return bmp;
    }

    /// <summary>
    /// Generate 4 separate histogram images showing HU distribution with the respective
    /// segmentation range highlighted in its specific color.
    /// </summary>
    private void UpdateHistograms()
    {
        if (Volume == null || Volume.Histogram.Length == 0) return;

        double range = Volume.MaxValue - Volume.MinValue;
        if (range <= 0) return;

        int localMax = 1;
        int limitAirBin = (int)((-800 - Volume.MinValue) / range * 512);
        limitAirBin = Math.Clamp(limitAirBin, 0, 511);

        for (int i = limitAirBin; i < 512; i++)
            if (Volume.Histogram[i] > localMax) localMax = Volume.Histogram[i];

        BoneHistogramImage = GenerateColoredHistogram(BoneMinHU, BoneMaxHU, localMax, range, 90, 130, 170);
        SoftHistogramImage = GenerateColoredHistogram(SoftMinHU, SoftMaxHU, localMax, range, 90, 130, 170);
        DentalHistogramImage = GenerateColoredHistogram(DentalMinHU, DentalMaxHU, localMax, range, 90, 130, 170);
        CustomHistogramImage = GenerateColoredHistogram(CustomMinHU, CustomMaxHU, localMax, range, 90, 130, 170);
    }

    private WriteableBitmap? GenerateColoredHistogram(double minHU, double maxHU, int localMax, double range, byte r, byte g, byte b)
    {
        if (Volume == null) return null;
        
        int histW = 512; 
        int histH = 80;
        var pixels = new byte[histW * histH * 4];

        double uiRange = IsoMax - IsoMin; // IsoMin is always -1000 here
        if (uiRange <= 0) return null;

        for (int x = 0; x < histW; x++)
        {
            double hu = IsoMin + (x * uiRange / (histW - 1));
            bool inRange = hu >= minHU && hu <= maxHU;
            
            int originalBin = (int)((hu - Volume.MinValue) / range * 511);
            int binVal = 0;
            if (originalBin >= 0 && originalBin < 512)
                binVal = Volume.Histogram[originalBin];

            int barHeight = binVal > 0 
                ? (int)(Math.Log(1 + binVal) / Math.Log(1 + localMax) * (histH - 2))
                : 0;
            if (barHeight > histH - 2) barHeight = histH - 2;

            for (int y = 0; y < histH; y++)
            {
                int row = histH - 1 - y;
                int idx = (row * histW + x) * 4;
                if (y < barHeight)
                {
                    if (inRange)
                    { pixels[idx] = b; pixels[idx+1] = g; pixels[idx+2] = r; pixels[idx+3] = 0xFF; }
                    else
                    { pixels[idx] = (byte)(b/4); pixels[idx+1] = (byte)(g/4); pixels[idx+2] = (byte)(r/4); pixels[idx+3] = 0xFF; }
                }
                else
                { pixels[idx] = 0x14; pixels[idx+1] = 0x10; pixels[idx+2] = 0x0D; pixels[idx+3] = 0xFF; }
            }
        }

        var bmp = new WriteableBitmap(histW, histH, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, histW, histH), pixels, histW * 4, 0);
        return bmp;
    }

    // ═══════════════════════════════════════
    // PHASE 2: SEGMENTATION COMMANDS
    // ═══════════════════════════════════════

    [RelayCommand]
    private async Task RunBoneSegmentAsync() => 
        await RunSegmentInternalAsync("Bone", BoneMinHU, BoneMaxHU, 230, 210, 180, HardTissueModel, enhanceThinBone: EnhanceSegmentation);

    [RelayCommand]
    private async Task RunSoftTissueSegmentAsync() => 
        await RunSegmentInternalAsync("Soft Tissue", SoftMinHU, SoftMaxHU, 210, 150, 150, SoftTissueModel);

    [RelayCommand]
    private async Task RunDentalSegmentAsync() => 
        await RunSegmentInternalAsync("Dental Model", DentalMinHU, DentalMaxHU, 245, 245, 230, DentalModel, applyNoiseRemoval: false, cleanDental: CleanDentalSegmentation);

    [RelayCommand]
    private async Task RunCustomSegmentAsync() => 
        await RunSegmentInternalAsync("Custom Segment", CustomMinHU, CustomMaxHU, 200, 180, 140, null);

    private async Task RunSegmentInternalAsync(
        string name, double minHU, double maxHU, 
        byte r, byte g, byte b, 
        SegmentViewModel? modelToOverwrite, 
        bool applyNoiseRemoval = true,
        bool enhanceThinBone = false,
        int morphologyIterations = 1,
        bool cleanDental = false)
    {
        if (Volume == null || IsLoading) return;

        if (modelToOverwrite != null)
        {
            var result = System.Windows.MessageBox.Show(
                $"A {name} model already exists. Generating a new one will overwrite it. Continue?",
                "Confirm Overwrite", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            
            if (result != System.Windows.MessageBoxResult.Yes) return;
            
            DeleteSegmentItem(modelToOverwrite);
        }

        SaveStateForUndo();

        if (_segVolume == null)
            _segVolume = new SegmentationVolume(Volume);

        byte label = (byte)(Segments.Count + 1);
        _segVolume.AddSegment(new SegmentInfo
            { Id = label, Name = $"{name} ({minHU:F0} to {maxHU:F0})", ColorR = r, ColorG = g, ColorB = b });

        IsLoading = true;
        StatusText = $"Running {name} segmentation...";
        LoadProgress = 0;
        short min = (short)minHU, max = (short)maxHU;

        await Task.Run(() =>
            SegmentationEngine.ThresholdSegment(Volume, _segVolume, label, min, max, enhanceThinBone,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = p * 40)));

        long count = _segVolume.CountVoxels(label);

        if (count == 0)
        {
            StatusText = $"No voxels found in range {min}–{max} HU";
            IsLoading = false;
            return;
        }

        if (enhanceThinBone)
        {
            StatusText = "Removing thin-bone scatter noise...";
            LoadProgress = 20;
            await Task.Run(() =>
                SegmentationEngine.RemoveSmallComponents(_segVolume, label, 50, 
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 20 + p * 10)));
            morphologyIterations = 2;
        }
        
        await Task.Run(() =>
            SegmentationEngine.MorphologicalClosing(_segVolume, label, morphologyIterations, 
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 30 + p * 10)));

        if (applyNoiseRemoval)
        {
            StatusText = "Removing noise (keeping largest component)...";
            LoadProgress = 40;
            await Task.Run(() =>
                SegmentationEngine.KeepLargestComponent(_segVolume, label,
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 10)));
            
            count = _segVolume.CountVoxels(label); // update count after noise removal
        }
        else if (cleanDental)
        {
            StatusText = "Removing 70% of smaller objects (keeping top 30%)...";
            LoadProgress = 40;
            await Task.Run(() =>
                SegmentationEngine.KeepTopPercentageComponents(_segVolume, label, 0.30,
                    p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 40 + p * 10)));
            
            count = _segVolume.CountVoxels(label); // update count after noise removal
        }

        if (count == 0)
        {
            StatusText = $"Model disappeared after noise removal. Try a lower lower threshold.";
            IsLoading = false;
            return;
        }

        StatusText = "Smoothing mask boundaries...";
        LoadProgress = 50;
        await Task.Run(() =>
            SegmentationEngine.SmoothLabelMask(_segVolume, label,
                p => Application.Current.Dispatcher.Invoke(() => LoadProgress = 50 + p * 10)));

        StatusText = $"Generating mesh from {count:N0} voxels...";
        LoadProgress = 50;

        await GenerateSegmentMeshAsync(label);

        StatusText = $"Segmented {count:N0} voxels ({min}–{max} HU)";
        LoadProgress = 100;

        // Isolate the pure bone mask so that subsequent segmentations (e.g., Dental) do not overwrite and destroy it
        // Compressed to GZip temp file instead of holding ~105 MB in memory
        if (name.Contains("Bone"))
        {
            _boneOnlySegVolumeLabel = label;
            await Task.Run(() => SaveBoneMaskToTemp(_segVolume.Labels));
        }

        // Reclaim LOH fragmentation from the two float[W*H*D] smooth/field arrays used during mesh extraction
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        IsLoading = false;
    }



    [RelayCommand]
    private async Task SplitComponentsAsync()
    {
        if (!HasModelLoaded) return;

        IsLoading = true;
        IsSplitting = true;
        StatusText = "Analyzing connected components...";
        LoadProgress = 0;

        if (_segVolume == null) return;
        
        // Only split the primary bone label (1)
        var components = await Task.Run(() =>
            SegmentationEngine.SplitConnectedComponents(_segVolume, 1, 1));

        if (components.Count < 2)
        {
            StatusText = "Only 1 connected region found — cannot split";
            IsLoading = false;
            return;
        }

        // Keep only the 2 largest components (mandible + skull/maxilla), discard small fragments
        var sorted = components.OrderByDescending(c => c.voxelCount).ToList();
        StatusText = $"Found {components.Count} components — keeping top 2...";

        // Remove all small fragment labels
        for (int i = 2; i < sorted.Count; i++)
            _segVolume.ClearLabel(sorted[i].newLabel);

        // Clear old segment ViewModels
        SaveStateForUndo();
        Segments.Clear();

        // Identify mandible vs skull using Z-centroid (mandible has LOWER Z in most CBCT/CT orientations)
        var comp1 = sorted[0];
        var comp2 = sorted[1];

        double z1 = await Task.Run(() => ComputeZCentroid(comp1.newLabel));
        double z2 = await Task.Run(() => ComputeZCentroid(comp2.newLabel));

        byte mandibleLabel, skullLabel;
        if (z1 < z2) { mandibleLabel = comp1.newLabel; skullLabel = comp2.newLabel; }
        else { mandibleLabel = comp2.newLabel; skullLabel = comp1.newLabel; }

        _segVolume.AddSegment(new SegmentInfo
            { Id = skullLabel, Name = "Maxilla / Skull", ColorR = 230, ColorG = 210, ColorB = 180 });
        _segVolume.AddSegment(new SegmentInfo
            { Id = mandibleLabel, Name = "Mandible", ColorR = 190, ColorG = 165, ColorB = 130 });

        StatusText = "Generating meshes...";
        LoadProgress = 50;
        await GenerateSegmentMeshAsync(skullLabel);
        LoadProgress = 75;
        await GenerateSegmentMeshAsync(mandibleLabel);

        StatusText = $"Split: Maxilla ({_segVolume.CountVoxels(skullLabel):N0}) + Mandible ({_segVolume.CountVoxels(mandibleLabel):N0})";
        LoadProgress = 100;
        IsLoading = false;
        IsSplitting = false;
    }

    private double ComputeZCentroid(byte label)
    {
        if (_segVolume == null) return 0;
        long sumZ = 0, count = 0;
        int w = _segVolume.Width, h = _segVolume.Height;
        for (int z = 0; z < _segVolume.Depth; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (_segVolume.GetLabel(x, y, z) == label)
            { sumZ += z; count++; }
        }
        return count > 0 ? (double)sumZ / count : 0;
    }

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
                await Task.Run(() => IcpAligner.TransformVertices(scan.Vertices, wizard.FinalTransform));
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
    private void PlanLeFort1()
    {
        var cranium = Segments.FirstOrDefault(s => s.Name.Contains("Cranium"));
        if (cranium == null || cranium.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please isolate the Cranium segment first.", "Missing Segment", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

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
            StatusText = "LeFort 1 Osteotomy applied successfully.";
        }
    }

    [RelayCommand]
    private void PlanGenioplasty()
    {
        // Priority:
        // 1. BSSO traced → use the teeth-bearing distal segment (exact name "Mandible")
        // 2. Cranium/Mandible split done → use "Mandible (Split)"
        // 3. Whole untouched bone → use HardTissueModel
        var bssoDistal    = Segments.LastOrDefault(s => s.Name == "Mandible" && s.IsVisible);
        var splitMandible = Segments.LastOrDefault(s => s.Name == "Mandible (Split)" && s.IsVisible);
        var targetSeg     = (SegmentViewModel?)(bssoDistal ?? splitMandible) ?? HardTissueModel;

        if (targetSeg == null || targetSeg.Vertices == null)
        {
            System.Windows.MessageBox.Show("Please generate the 3D model first.", "Missing Model", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

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

        var wizard = new BssoOsteotomyWindow(MeshHelper.ToVertexList(inputVerts));
        wizard.Owner = System.Windows.Application.Current.MainWindow;

        if (wizard.ShowDialog() == true && wizard.Accepted)
        {
            SaveStateForUndo();

            // Add new proximal (condyle) — accumulates for bilateral
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

            // Reload the compressed bone mask from temp if available
            SegmentationVolume? boneOnlySegVol = null;
            if (_boneOnlySegVolumeTempPath != null && File.Exists(_boneOnlySegVolumeTempPath) && Volume != null)
            {
                StatusText = "Decompressing bone mask...";
                var labels = await Task.Run(() => LoadBoneMaskFromTemp());
                if (labels != null)
                {
                    boneOnlySegVol = new SegmentationVolume(Volume);
                    Array.Copy(labels, boneOnlySegVol.Labels, labels.Length);
                }
            }
            var splitTargetVolume = boneOnlySegVol ?? _segVolume;

            var wizard = new CondyleSplitWindow(
                MeshHelper.ToVertexList(boneSegment.Vertices),
                Volume, splitTargetVolume, boneSegment.Label, BoneMinHU);
            wizard.Owner = System.Windows.Application.Current.MainWindow;

            if (wizard.ShowDialog() == true && wizard.Accepted)
            {
                SaveStateForUndo();

                // Store condylar axis data & midline
                LeftCondyleCenter = wizard.LeftCondyleCenter;
                RightCondyleCenter = wizard.RightCondyleCenter;
                DentalMidlinePoint = wizard.DentalMidlinePoint;

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
                // Delete the bone mask temp file — no longer needed after the split
                if (_boneOnlySegVolumeTempPath != null && File.Exists(_boneOnlySegVolumeTempPath))
                    try { File.Delete(_boneOnlySegVolumeTempPath); } catch { }
                _boneOnlySegVolumeTempPath = null;
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
            Owner = Application.Current.MainWindow
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

    private async Task GenerateSegmentMeshAsync(byte label, string? nameOverride = null, byte? r = null, byte? g = null, byte? b = null)
    {
        if (Volume == null || _segVolume == null) return;

        var vol = Volume;
        var segVol = _segVolume;
        // Full resolution for best quality
        int step = 1;

        var vertices = await Task.Run(() =>
            SegmentationEngine.ExtractSegmentMesh(vol, segVol, label, step,
                p => Application.Current.Dispatcher.Invoke(() =>
                    LoadProgress = Math.Min(99, LoadProgress + p * 10))));

        if (vertices.Length < 3) return;

        var info = _segVolume.Segments.GetValueOrDefault(label)
            ?? new SegmentInfo { Id = label, Name = $"Segment {label}" };
            
        string finalName = nameOverride ?? info.Name;
        byte finalR = r ?? info.ColorR;
        byte finalG = g ?? info.ColorG;
        byte finalB = b ?? info.ColorB;

        var segVm = new SegmentViewModel
        {
            Label = label,
            Name = finalName,
            Vertices = vertices,
            ColorR = finalR,
            ColorG = finalG,
            ColorB = finalB,
            IsVisible = true
        };
        segVm.OnVisibilityChanged = RefreshCombinedModel;
        segVm.BuildModel();
        Segments.Add(segVm);

        // Auto-assign to named properties
        if (info.Name == "Bone" || segVm.Name.StartsWith("Bone")) HardTissueModel = segVm;
        else if (info.Name == "Soft Tissue" || segVm.Name.StartsWith("Soft Tissue")) SoftTissueModel = segVm;
        else if (info.Name == "Dental Scan" || segVm.Name.StartsWith("Dental")) DentalModel = segVm;

        RefreshCombinedModel();
    }

    private void RefreshCombinedModel()
    {
        Rect3D newBounds = Rect3D.Empty;

        // Force the camera frame to ALWAYS lock onto the overall global DICOM volume
        // rather than jumping or dynamically shrinking toward individual bone segments.
        if (Volume != null)
        {
            newBounds = new Rect3D(0, 0, 0,
                Volume.Width * Volume.Spacing[0],
                Volume.Height * Volume.Spacing[1],
                Volume.Depth * Volume.Spacing[2]);
        }

        if (newBounds == BoneOnlyBounds) 
        {
            // Bounds haven't changed, prevent unnecessary camera snap, just push transforms
            UpdateNhpTransform();
            return;
        }

        BoneOnlyBounds = newBounds;

        // Keep ModelCenter in sync with the bone bounds so camera can orbit around it
        if (!BoneOnlyBounds.IsEmpty)
        {
            ModelCenter = new Point3D(
                BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
            OnPropertyChanged(nameof(ModelCenter));
        }

        OnPropertyChanged(nameof(BoneOnlyBounds));
        UpdateNhpTransform();
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

    // ═══════════════════════════════════════
    // UNDO / REDO STATE MANAGEMENT
    // ═══════════════════════════════════════
    private void SaveStateForUndo()
    {
        _undoStack.Push(CreateStateSnapshot());
        _redoStack.Clear();
        // Keep at most 2 undo entries to minimize mesh memory retained in stale snapshots
        while (_undoStack.Count > 2)
        {
            var kept = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 1; i >= 0; i--) _undoStack.Push(kept[i]);
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_undoStack.Pop());
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CreateStateSnapshot());
        RestoreStateSnapshot(_redoStack.Pop());
    }

    private StateSnapshot CreateStateSnapshot()
    {
        // Clone VMs without Geometry/Material to avoid retaining ~48 MB per segment in undo stack.
        // Geometry is rebuilt from Vertices via BuildModel() on restore.
        int hardIdx = HardTissueModel != null ? Segments.IndexOf(HardTissueModel) : -1;
        int softIdx = SoftTissueModel != null ? Segments.IndexOf(SoftTissueModel) : -1;
        int dentalIdx = DentalModel != null ? Segments.IndexOf(DentalModel) : -1;

        return new StateSnapshot
        {
            Segments = Segments.Select(s => new SegmentViewModel
            {
                Label = s.Label, Name = s.Name, Vertices = s.Vertices,
                ColorR = s.ColorR, ColorG = s.ColorG, ColorB = s.ColorB,
                IsVisible = s.IsVisible, Opacity = s.Opacity,
                SurgicalTransform = s.SurgicalTransform
                // Geometry and Material intentionally omitted
            }).ToList(),
            ImportedMeshes = ImportedMeshes.Select(m => new MeshViewModel
            {
                Name = m.Name, Vertices = m.Vertices,
                ColorR = m.ColorR, ColorG = m.ColorG, ColorB = m.ColorB,
                IsVisible = m.IsVisible, ScanType = m.ScanType
                // Geometry and Material intentionally omitted
            }).ToList(),
            HardTissueModelIndex = hardIdx,
            SoftTissueModelIndex = softIdx,
            DentalModelIndex = dentalIdx
        };
    }

    private void RestoreStateSnapshot(StateSnapshot snapshot)
    {
        Segments.Clear();
        foreach (var s in snapshot.Segments)
        {
            s.OnVisibilityChanged = RefreshCombinedModel;
            s.BuildModel(); // Rebuild geometry from Vertices
            Segments.Add(s);
        }

        ImportedMeshes.Clear();
        foreach (var m in snapshot.ImportedMeshes)
        {
            m.OnVisibilityChanged = RefreshCombinedModel;
            m.BuildModel();
            ImportedMeshes.Add(m);
        }

        HardTissueModel = snapshot.HardTissueModelIndex >= 0 && snapshot.HardTissueModelIndex < Segments.Count
            ? Segments[snapshot.HardTissueModelIndex] : null;
        SoftTissueModel = snapshot.SoftTissueModelIndex >= 0 && snapshot.SoftTissueModelIndex < Segments.Count
            ? Segments[snapshot.SoftTissueModelIndex] : null;
        DentalModel = snapshot.DentalModelIndex >= 0 && snapshot.DentalModelIndex < Segments.Count
            ? Segments[snapshot.DentalModelIndex] : null;

        RefreshCombinedModel();
    }

    [RelayCommand]
    private void OpenLightingConfig()
    {
        var window = new LightingWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }

    private async Task PerformPhysicalResliceAsync(
        double dPitch = 0, double dRoll = 0, double dYaw = 0,
        double dLat   = 0, double dAnt  = 0, double dVert = 0)
    {
        // On first reslice: capture + serialize OriginalVolume to temp, then null in-memory copy
        if (OriginalVolume == null)
        {
            // Try reloading from temp file first (saved ~200 MB by keeping on disk between reslices)
            if (_originalVolumeTempPath != null && File.Exists(_originalVolumeTempPath))
            {
                StatusText = "Reloading baseline volume from temp...";
                OriginalVolume = await Task.Run(() => LoadOriginalVolumeFromTemp());
            }
            else
            {
                OriginalVolume = Volume;
                if (Volume != null)
                {
                    StatusText = "Saving baseline volume to temp...";
                    await Task.Run(() => SaveOriginalVolumeToTemp(Volume));
                }
            }
        }
        if (OriginalVolume == null || BoneOnlyBounds.IsEmpty) return;

        StatusText = "Calculating exact physical volume bounds...";
        IsLoading = true;
        
        // --- 1. Calculate Spatial Centroid Pivot ---
        // Crucial: Use the bounds of the original bone to pivot from the original space
        var bounds = BoneOnlyBounds;
        Point3D center;
        if (!bounds.IsEmpty)
            center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        else
        {
            var dims = OriginalVolume.GetPhysicalDimensions();
            center = new Point3D(dims.Width / 2, dims.Height / 2, dims.Depth / 2);
        }

        // --- 2. Build the STRICT SOURCE -> TARGET Matrix (exactly matching visual UpdateNhpTransform) ---
        var visualGroup = new Transform3DGroup();
        visualGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), NhpPitch)));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), NhpRoll)));
        visualGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), NhpYaw)));
        visualGroup.Children.Add(new TranslateTransform3D(center.X + NhpLateral, center.Y + NhpAnteroposterior, center.Z + NhpVertical));
        
        var sourceToTargetMatrix = visualGroup.Value;
        
        // Target -> Source is exactly the inverse of the above
        var targetToSourceMatrix = sourceToTargetMatrix;
        if (targetToSourceMatrix.HasInverse) targetToSourceMatrix.Invert();

        // SegmentationEngine ResliceVolume parameter 2 is 'transform', mathematically mapping Target to Source!
        var transform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = targetToSourceMatrix.M11, M12 = targetToSourceMatrix.M12, M13 = targetToSourceMatrix.M13, M14 = targetToSourceMatrix.M14,
            M21 = targetToSourceMatrix.M21, M22 = targetToSourceMatrix.M22, M23 = targetToSourceMatrix.M23, M24 = targetToSourceMatrix.M24,
            M31 = targetToSourceMatrix.M31, M32 = targetToSourceMatrix.M32, M33 = targetToSourceMatrix.M33, M34 = targetToSourceMatrix.M34,
            M41 = targetToSourceMatrix.OffsetX, M42 = targetToSourceMatrix.OffsetY, M43 = targetToSourceMatrix.OffsetZ, M44 = targetToSourceMatrix.M44
        };
        
        // SegmentationEngine ResliceVolume parameter 3 is 'inverseTransform', mapping Source to Target to find bounds!
        var inverseTransform = new OrthoPlanner.Core.Imaging.NhpTransform
        {
            M11 = sourceToTargetMatrix.M11, M12 = sourceToTargetMatrix.M12, M13 = sourceToTargetMatrix.M13, M14 = sourceToTargetMatrix.M14,
            M21 = sourceToTargetMatrix.M21, M22 = sourceToTargetMatrix.M22, M23 = sourceToTargetMatrix.M23, M24 = sourceToTargetMatrix.M24,
            M31 = sourceToTargetMatrix.M31, M32 = sourceToTargetMatrix.M32, M33 = sourceToTargetMatrix.M33, M34 = sourceToTargetMatrix.M34,
            M41 = sourceToTargetMatrix.OffsetX, M42 = sourceToTargetMatrix.OffsetY, M43 = sourceToTargetMatrix.OffsetZ, M44 = sourceToTargetMatrix.M44
        };
        


        StatusText = "Reslicing volume matrix...";
        
        // Pass both transforms so the Engine can determine exact physical boundaries and pad without waste!
        // Reslice from the ORIGINAL volume so angles are absolute!
        var resliced = await Task.Run(() => SegmentationEngine.ResliceVolume(OriginalVolume, transform, inverseTransform));
        
        IsLoading = false;
        
        // dPitch/Roll/Yaw/Lat/Ant/Vert are the delta values passed in from CommitNhpAsync
        // (captured before _cXxx was updated, so they are the true increment to bake)
        var deltaGroup = new Transform3DGroup();
        deltaGroup.Children.Add(new TranslateTransform3D(-center.X, -center.Y, -center.Z));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), dPitch)));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), dRoll)));
        deltaGroup.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), dYaw)));
        deltaGroup.Children.Add(new TranslateTransform3D(center.X + dLat, center.Y + dAnt, center.Z + dVert));

        var m = deltaGroup.Value;
        
        var allSegmentModels = Segments.ToList();
        if (HardTissueModel != null && !allSegmentModels.Contains(HardTissueModel)) allSegmentModels.Add(HardTissueModel);
        if (SoftTissueModel != null && !allSegmentModels.Contains(SoftTissueModel)) allSegmentModels.Add(SoftTissueModel);
        if (DentalModel != null && !allSegmentModels.Contains(DentalModel)) allSegmentModels.Add(DentalModel);

        foreach (var seg in allSegmentModels)
        {
            if (seg.Vertices != null)
            {
                var p = new Point3D();
                for (int i = 0; i < seg.Vertices.Length; i += 3)
                {
                    p.X = seg.Vertices[i]; p.Y = seg.Vertices[i + 1]; p.Z = seg.Vertices[i + 2];
                    var t = m.Transform(p);
                    seg.Vertices[i] = (float)t.X; seg.Vertices[i + 1] = (float)t.Y; seg.Vertices[i + 2] = (float)t.Z;
                }
            }
        }
        foreach (var mesh in ImportedMeshes)
        {
            if (mesh.Vertices != null)
            {
                var p = new Point3D();
                for (int i = 0; i < mesh.Vertices.Length; i += 3)
                {
                    p.X = mesh.Vertices[i]; p.Y = mesh.Vertices[i + 1]; p.Z = mesh.Vertices[i + 2];
                    var t = m.Transform(p);
                    mesh.Vertices[i] = (float)t.X; mesh.Vertices[i + 1] = (float)t.Y; mesh.Vertices[i + 2] = (float)t.Z;
                }
            }
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
            Volume = resliced;
            
            // Re-initialize segmentation volume for the new dimensions (Air Padded array)
            _segVolume = new SegmentationVolume(Volume);

            // Rebuild models since we mutated their vertices physically
            foreach (var seg in allSegmentModels) seg.BuildModel();
            foreach (var mesh in ImportedMeshes) mesh.BuildModel();

            // Reset all transforms to identity — vertices are now physically at the correct NHP position
            _nhpTransform = System.Windows.Media.Media3D.Transform3D.Identity;
            foreach (var seg in allSegmentModels)
            {
                seg.SurgicalTransform = System.Windows.Media.Media3D.Transform3D.Identity;
                seg.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            }
            foreach (var mesh in ImportedMeshes)
                mesh.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (HardTissueModel != null) HardTissueModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (SoftTissueModel != null) SoftTissueModel.Transform = System.Windows.Media.Media3D.Transform3D.Identity;
            if (DentalModel != null)     DentalModel.Transform     = System.Windows.Media.Media3D.Transform3D.Identity;

            // CRITICAL: sync BoneOnlyBounds to the new resliced volume NOW.
            // Without this, the first visibility toggle after commit would find
            // newBounds != BoneOnlyBounds (old dims) and snap ModelCenter to the
            // new padded-volume center, causing a visible caudal translation.
            BoneOnlyBounds = new Rect3D(0, 0, 0,
                Volume.Width  * Volume.Spacing[0],
                Volume.Height * Volume.Spacing[1],
                Volume.Depth  * Volume.Spacing[2]);
            ModelCenter = new Point3D(
                BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
            OnPropertyChanged(nameof(BoneOnlyBounds));
            OnPropertyChanged(nameof(ModelCenter));
            
            // Refresh 2D Slices
            AxialMax = Volume.Depth - 1;
            CoronalMax = Volume.Height - 1;
            SagittalMax = Volume.Width - 1;
            
            // Push updated aspect ratios out
            AxialDisplayHeight = new System.Windows.GridLength(Volume.Height * Volume.Spacing[1], System.Windows.GridUnitType.Star);
            CoronalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            SagittalDisplayHeight = new System.Windows.GridLength(Volume.Depth * Volume.Spacing[2], System.Windows.GridUnitType.Star);
            
            UpdateAllSlices();
            UpdateHistograms();
            
            StatusText = "NHP Alignment Complete. Model frozen.";
            OnPropertyChanged(nameof(IsNhpDirty));
        });

        // Release OriginalVolume from managed heap — it lives in the temp file now (~200 MB saved)
        OriginalVolume = null;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
    }

    // ─── GZip Temp File Helpers (bone mask + OriginalVolume) ───

    private void SaveBoneMaskToTemp(byte[] labels)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ortho_bone_mask_{Guid.NewGuid():N}.gz");
        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            gz.Write(labels, 0, labels.Length);
        _boneOnlySegVolumeTempPath = path;
    }

    private byte[]? LoadBoneMaskFromTemp()
    {
        if (_boneOnlySegVolumeTempPath == null || !File.Exists(_boneOnlySegVolumeTempPath)) return null;
        using var fs = File.OpenRead(_boneOnlySegVolumeTempPath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }

    private void SaveOriginalVolumeToTemp(VolumeData vol)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ortho_orig_vol_{Guid.NewGuid():N}.gz");
        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        using (var bw = new BinaryWriter(gz))
        {
            bw.Write(vol.Width);
            bw.Write(vol.Height);
            bw.Write(vol.Depth);
            bw.Write(vol.Spacing[0]);
            bw.Write(vol.Spacing[1]);
            bw.Write(vol.Spacing[2]);
            // Write voxels as raw bytes
            var bytes = new byte[vol.Voxels.Length * 2];
            Buffer.BlockCopy(vol.Voxels, 0, bytes, 0, bytes.Length);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
        _originalVolumeTempPath = path;
    }

    private VolumeData? LoadOriginalVolumeFromTemp()
    {
        if (_originalVolumeTempPath == null || !File.Exists(_originalVolumeTempPath)) return null;
        using var fs = File.OpenRead(_originalVolumeTempPath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var br = new BinaryReader(gz);
        int w = br.ReadInt32();
        int h = br.ReadInt32();
        int d = br.ReadInt32();
        double sx = br.ReadDouble();
        double sy = br.ReadDouble();
        double sz = br.ReadDouble();
        var vol = new VolumeData(w, h, d, new[] { sx, sy, sz });
        int byteLen = br.ReadInt32();
        var bytes = br.ReadBytes(byteLen);
        Buffer.BlockCopy(bytes, 0, vol.Voxels, 0, byteLen);
        vol.ComputeMinMax();
        return vol;
    }

    private void CleanupTempFiles()
    {
        if (_boneOnlySegVolumeTempPath != null)
            try { if (File.Exists(_boneOnlySegVolumeTempPath)) File.Delete(_boneOnlySegVolumeTempPath); } catch { }
        _boneOnlySegVolumeTempPath = null;

        if (_originalVolumeTempPath != null)
            try { if (File.Exists(_originalVolumeTempPath)) File.Delete(_originalVolumeTempPath); } catch { }
        _originalVolumeTempPath = null;
    }
}

// ─── Helper ViewModels ───

public partial class SegmentViewModel : ObservableObject
{
    [ObservableProperty] private byte _label;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isSelectedForExport = true;
    [ObservableProperty] private byte _colorR = 200, _colorG = 180, _colorB = 140;

    private double _opacity = 1.0;
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, value))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                OnPropertyChanged(nameof(IsTransparent));
                if (Material is HelixToolkit.Wpf.SharpDX.PhongMaterial phong)
                {
                    phong.DiffuseColor = new HelixToolkit.Maths.Color4(ColorR / 255f, ColorG / 255f, ColorB / 255f, (float)_opacity);
                }
            }
        }
    }

    public double OpacityPercent
    {
        get => _opacity * 100.0;
        set
        {
            double clamped = Math.Max(0, Math.Min(100, value));
            Opacity = clamped / 100.0;
        }
    }

    public bool IsTransparent => _opacity < 1.0;

    public float[]? Vertices { get; set; }
    public HelixToolkit.SharpDX.Geometry3D? Geometry { get; set; }
    public HelixToolkit.Wpf.SharpDX.Material? Material { get; set; }
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    /// <summary>The surgical movement component of this segment's transform (NHP-independent).</summary>
    public System.Windows.Media.Media3D.Transform3D SurgicalTransform { get; set; } = System.Windows.Media.Media3D.Transform3D.Identity;

    /// <summary>Callback so the parent ViewModel can refresh 3D when visibility toggles.</summary>
    public Action? OnVisibilityChanged { get; set; }

    partial void OnIsVisibleChanged(bool value) => OnVisibilityChanged?.Invoke();

    public void BuildModel()
    {
        if (Vertices == null || Vertices.Length < 3) return;
        MeshHelper.BuildModel3D(Vertices, ColorR, ColorG, ColorB, out var geom, out var mat, (byte)(Opacity * 255.0));
        Geometry = (HelixToolkit.SharpDX.Geometry3D)geom;
        Material = mat;
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(Material));
    }
}

public enum DentalScanType { Other, Upper, Lower }

public partial class MeshViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private byte _colorR = 245, _colorG = 245, _colorB = 230;
    [ObservableProperty] private DentalScanType _scanType = DentalScanType.Other;
    public float[]? Vertices { get; set; }
    public object? Geometry { get; set; }
    public HelixToolkit.Wpf.SharpDX.Material? Material { get; set; }
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    // Relative transforms based on occlusion
    [ObservableProperty] private System.Windows.Media.Media3D.Matrix3D _maxillaOcclusionTransform = System.Windows.Media.Media3D.Matrix3D.Identity;
    [ObservableProperty] private System.Windows.Media.Media3D.Matrix3D _mandibleOcclusionTransform = System.Windows.Media.Media3D.Matrix3D.Identity;

    public Action? OnVisibilityChanged { get; set; }
    partial void OnIsVisibleChanged(bool value) => OnVisibilityChanged?.Invoke();

    public void BuildModel()
    {
        if (Vertices == null || Vertices.Length < 3) return;
        MeshHelper.BuildModel3D(Vertices, ColorR, ColorG, ColorB, out var geom, out var mat);
        Geometry = geom;
        Material = mat;
        OnPropertyChanged(nameof(Geometry));
    }
}

public static class MeshHelper
{
    /// <summary>Convert a flat float[] (stride 3) to a List of float[3] arrays (for legacy APIs).</summary>
    public static List<float[]> ToVertexList(float[] flat)
    {
        var list = new List<float[]>(flat.Length / 3);
        for (int i = 0; i < flat.Length; i += 3)
            list.Add(new float[] { flat[i], flat[i + 1], flat[i + 2] });
        return list;
    }

    /// <summary>Convert a List of float[3] arrays to a flat float[] (stride 3).</summary>
    public static float[] ToFlatArray(List<float[]> list)
    {
        var flat = new float[list.Count * 3];
        for (int i = 0; i < list.Count; i++)
        { flat[i * 3] = list[i][0]; flat[i * 3 + 1] = list[i][1]; flat[i * 3 + 2] = list[i][2]; }
        return flat;
    }

    // ─── Compatibility overloads for windows that still use List<float[]> ───
    public static HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D BuildModel3D(List<float[]> vertices, byte r, byte g, byte b, byte a = 255)
        => BuildModel3D(ToFlatArray(vertices), r, g, b, a);

    public static void BuildModel3D(List<float[]> vertices, byte r, byte g, byte b, out object geometry, out HelixToolkit.Wpf.SharpDX.Material material, byte a = 255)
        => BuildModel3D(ToFlatArray(vertices), r, g, b, out geometry, out material, a);

    public static HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D BuildModel3D(float[] vertices, byte r, byte g, byte b, byte a = 255)
    {
        BuildModel3D(vertices, r, g, b, out var geom, out var mat, a);
        return new HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D
        {
            Geometry = (HelixToolkit.SharpDX.Geometry3D)geom,
            Material = mat
        };
    }

    public static void BuildModel3D(float[] vertices, byte r, byte g, byte b, out object geometry, out HelixToolkit.Wpf.SharpDX.Material material, byte a = 255)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i + 8 < vertices.Length; i += 9)
        {
            builder.AddTriangle(
                new System.Numerics.Vector3(vertices[i],     vertices[i + 1], vertices[i + 2]),
                new System.Numerics.Vector3(vertices[i + 3], vertices[i + 4], vertices[i + 5]),
                new System.Numerics.Vector3(vertices[i + 6], vertices[i + 7], vertices[i + 8]));
        }
        geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        
        material = new HelixToolkit.Wpf.SharpDX.PhongMaterial()
        {
            DiffuseColor = new HelixToolkit.Maths.Color4(r / 255f, g / 255f, b / 255f, a / 255f),
            SpecularColor = new HelixToolkit.Maths.Color4(0.1f, 0.1f, 0.1f, 1f),
            SpecularShininess = 1f
        };
    }
}


