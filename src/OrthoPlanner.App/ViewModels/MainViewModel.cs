using System.IO;
using System.Collections.ObjectModel;
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
        // Re-evaluate HasLeFort1Maxilla whenever the segments collection changes
        // (covers project load, undo/redo, and segment deletion).
        Segments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLeFort1Maxilla));

        // NHP Ledger: auto-apply NHP transform to any object entering the viewport collections
        InitNhpLedger();
        InitializeThreeDModelsPanel();
    }

    // ÔöÇÔöÇÔöÇ Photogrammetry ÔöÇÔöÇÔöÇ
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

    // ÔöÇÔöÇÔöÇ Surgical Movements ÔåÆ SurgeryViewModel.cs ÔöÇÔöÇÔöÇ




    // ÔöÇÔöÇÔöÇ Volume State (see DicomViewModel.cs) ÔöÇÔöÇÔöÇ
    // _volume, _isVolumeLoaded, _originalVolume, _lastDicomPath ÔåÆ DicomViewModel.cs
    // PatientName, StudyDate, SeriesDescription, VolumeDimensions ÔåÆ DicomViewModel.cs
    // TotalSlices, CurrentSlice, AxialIndex, CoronalIndex, SagittalIndex ÔåÆ DicomViewModel.cs
    // AxialMax, CoronalMax, SagittalMax, *DisplayHeight ÔåÆ DicomViewModel.cs
    // WindowCenter, WindowWidth ÔåÆ DicomViewModel.cs
    // IsoThreshold, IsoMin, IsoMax ÔåÆ DicomViewModel.cs
    // AxialImage, CoronalImage, SagittalImage ÔåÆ DicomViewModel.cs

    // ÔöÇÔöÇÔöÇ Viewport (headlamp, modelCenter, geometry, toggles, lighting) ÔåÆ ViewportViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ Status / Loading (shared, kept here) ÔöÇÔöÇÔöÇ
    [ObservableProperty] private string _statusText = "Ready \u2014 Open a DICOM folder to begin";
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;

    // ÔöÇÔöÇÔöÇ Named Anatomy ÔöÇÔöÇÔöÇ
    public SegmentViewModel? HardTissueModel { get; private set; }
    public SegmentViewModel? SoftTissueModel { get; private set; }
    public SegmentViewModel? DentalModel { get; private set; }

    // ÔöÇÔöÇÔöÇ Condylar Axis (set by Split Cranium/Mandible wizard) ÔöÇÔöÇÔöÇ
    public (double X, double Y, double Z)? LeftCondyleCenter { get; set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; set; }
    public (double X, double Y, double Z)? LeftCondyleHalfExtents { get; set; }
    public (double X, double Y, double Z)? RightCondyleHalfExtents { get; set; }
    public (double X, double Y, double Z)? DentalMidlinePoint { get; set; }

    // ÔöÇÔöÇÔöÇ Segmentation Internal Volumes ÔöÇÔöÇÔöÇ
    // BoneHistogramImage, SoftHistogramImage, DentalHistogramImage, CustomHistogramImage ÔåÆ DicomViewModel.cs
    // BoneMinHU/Max, SoftMinHU/Max, DentalMinHU/Max, CustomMinHU/Max, Show*Overlay, ShowSegmentation ÔåÆ DicomViewModel.cs
    // _segVolume, _boneOnlySegVolumeTempPath ÔåÆ SegmentationViewModel.cs / OsteotomyViewModel.cs

    // ÔöÇÔöÇÔöÇ Undo/Redo ÔåÆ UndoRedoViewModel.cs ÔöÇÔöÇÔöÇ
    // ÔöÇÔöÇÔöÇ Volume Rendering ÔåÆ VolumeRenderingViewModel.cs ÔöÇÔöÇÔöÇ
    // ÔöÇÔöÇÔöÇ NHP ÔåÆ NhpViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ MultiSeeds, ClearSeeds ÔåÆ SegmentationViewModel.cs ÔöÇÔöÇÔöÇ
    // ÔöÇÔöÇÔöÇ Viewport toggles, lighting ÔåÆ ViewportViewModel.cs ÔöÇÔöÇÔöÇ

    public ObservableCollection<SegmentViewModel> Segments { get; } = new();

    // ─── Imported Meshes ───
    public ObservableCollection<MeshViewModel> ImportedMeshes { get; } = new();

    // ─── Cephalometry landmark persistence ───
    // Populated by CephalometryOverlay whenever landmarks change; consumed by ProjectViewModel.
    public List<CephLandmarkSave> SavedCephLandmarks { get; set; } = new();

    // ─── Volume Pivot (set once on DICOM load, persists across reslices) ───
    // Nullable: null means "not yet set" — avoids false positive from (0,0,0) origin volumes
    [ObservableProperty] private System.Windows.Media.Media3D.Point3D? _volumePivot;

    // ─── Segmentation flags → SegmentationViewModel.cs ───

    // ÔöÇÔöÇÔöÇ SaveProject, OpenProjectAsync ÔåÆ ProjectViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ OpenDicomFolderAsync + LoadDicomAsync ÔåÆ DicomViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ Slice update methods, bitmap helpers, histograms ÔåÆ DicomViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ Segmentation ÔåÆ SegmentationViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ STL import/align/export/delete ÔåÆ StlViewModel.cs ÔöÇÔöÇÔöÇ
    // ÔöÇÔöÇÔöÇ Osteotomy (LeFort1, BSSO, Genioplasty, SplitCraniumMandible) ÔåÆ OsteotomyViewModel.cs ÔöÇÔöÇÔöÇ

    // ÔöÇÔöÇÔöÇ GenerateSegmentMeshAsync ÔåÆ SegmentationViewModel.cs ÔöÇÔöÇÔöÇ

    private void RefreshCombinedModel()
    {
        OnPropertyChanged(nameof(HasLeFort1Maxilla));
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
            // Phase 0: Use baked VolumePivot when available (stable across reslices)
            if (VolumePivot == null)
            {
                ModelCenter = new Point3D(
                    BoneOnlyBounds.X + BoneOnlyBounds.SizeX / 2,
                    BoneOnlyBounds.Y + BoneOnlyBounds.SizeY / 2,
                    BoneOnlyBounds.Z + BoneOnlyBounds.SizeZ / 2);
            }
            else
            {
                ModelCenter = VolumePivot.Value;
            }
            OnPropertyChanged(nameof(ModelCenter));
        }

        OnPropertyChanged(nameof(BoneOnlyBounds));
        UpdateNhpTransform();
    }

    // ÔöÇÔöÇÔöÇ Undo/Redo ÔåÆ UndoRedoViewModel.cs ÔöÇÔöÇÔöÇ
    // ÔöÇÔöÇÔöÇ OpenLightingConfig, _isCephalometryOpen ÔåÆ ViewportViewModel.cs ÔöÇÔöÇÔöÇ

    // ─── NHP (visual-only; no physical reslicing) → NhpViewModel.cs ───
}

// ÔöÇÔöÇÔöÇ Helper ViewModels ÔöÇÔöÇÔöÇ

public partial class SegmentViewModel : ObservableObject
{
    [ObservableProperty] private byte _label;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isSelectedForExport = true;
    [ObservableProperty] private byte _colorR = 200, _colorG = 180, _colorB = 140;

    public System.Windows.Media.Brush DisplayColorBrush => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(ColorR, ColorG, ColorB));

    partial void OnColorRChanged(byte value) => ApplyColorChange();
    partial void OnColorGChanged(byte value) => ApplyColorChange();
    partial void OnColorBChanged(byte value) => ApplyColorChange();

    private void ApplyColorChange()
    {
        OnPropertyChanged(nameof(DisplayColorBrush));
        if (Material is HelixToolkit.Wpf.SharpDX.PhongMaterial phong)
            phong.DiffuseColor = new HelixToolkit.Maths.Color4(ColorR / 255f, ColorG / 255f, ColorB / 255f, (float)_opacity);
        else if (Vertices != null && Vertices.Length >= 3)
            BuildModel();
    }

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

    /// <summary>
    /// When true, the segment's vertices already have the cumulative NHP transform baked in.
    /// Managed by the NHP ledger — do NOT set manually. Use <see cref="DerivedFrom"/> instead
    /// to declare parent-child lineage; the ledger infers bake state from the parent.
    /// </summary>
    public bool NhpBaked { get; internal set; }

    /// <summary>
    /// The parent segment this was derived from (e.g., Bone → Cranium/Mandible, Mandible → Ramus).
    /// The NHP ledger checks this: if the parent is already NHP-baked, the child inherits that
    /// state automatically and the ledger skips re-baking. Set this when creating a segment
    /// whose vertices come from an already-existing segment's vertex data.
    /// </summary>
    public SegmentViewModel? DerivedFrom { get; set; }

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
    /// <summary>When true, this mesh also appears in the bottom 3D MODELS panel (e.g. splint).</summary>
    [ObservableProperty] private bool _showInModelsPanel;
    public float[]? Vertices { get; set; }
    public object? Geometry { get; set; }
    public HelixToolkit.Wpf.SharpDX.Material? Material { get; set; }
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _transform = System.Windows.Media.Media3D.Transform3D.Identity;

    /// <summary>True when vertices already have cumulative NHP baked in (set before
    /// adding to ImportedMeshes to prevent double-baking by the NHP ledger).</summary>
    public bool NhpBaked { get; set; }

    // Relative transforms based on occlusion
    [ObservableProperty] private System.Windows.Media.Media3D.Matrix3D _maxillaOcclusionTransform = System.Windows.Media.Media3D.Matrix3D.Identity;
    [ObservableProperty] private System.Windows.Media.Media3D.Matrix3D _mandibleOcclusionTransform = System.Windows.Media.Media3D.Matrix3D.Identity;

    public System.Windows.Media.Brush DisplayColorBrush => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(ColorR, ColorG, ColorB));

    partial void OnColorRChanged(byte value) => ApplyColorChange();
    partial void OnColorGChanged(byte value) => ApplyColorChange();
    partial void OnColorBChanged(byte value) => ApplyColorChange();

    private void ApplyColorChange()
    {
        OnPropertyChanged(nameof(DisplayColorBrush));
        if (Material is HelixToolkit.Wpf.SharpDX.PhongMaterial phong)
            phong.DiffuseColor = new HelixToolkit.Maths.Color4(ColorR / 255f, ColorG / 255f, ColorB / 255f, (float)_opacity);
        else if (Vertices != null && Vertices.Length >= 3)
            BuildModel();
    }

    private double _opacity = 1.0;
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, value))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                if (Material is HelixToolkit.Wpf.SharpDX.PhongMaterial phong)
                    phong.DiffuseColor = new HelixToolkit.Maths.Color4(ColorR / 255f, ColorG / 255f, ColorB / 255f, (float)_opacity);
            }
        }
    }

    public double OpacityPercent
    {
        get => _opacity * 100.0;
        set => Opacity = Math.Max(0, Math.Min(100, value)) / 100.0;
    }

    public Action? OnVisibilityChanged { get; set; }
    partial void OnIsVisibleChanged(bool value) => OnVisibilityChanged?.Invoke();

    public void BuildModel()
    {
        if (Vertices == null || Vertices.Length < 3) return;
        MeshHelper.BuildModel3D(Vertices, ColorR, ColorG, ColorB, out var geom, out var mat, (byte)(Opacity * 255.0));
        Geometry = geom;
        Material = mat;
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(Material));
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

    public static HelixToolkit.Wpf.SharpDX.PhongMaterial CreatePhongMaterial(byte r, byte g, byte b, byte a = 255)
        => new()
        {
            DiffuseColor = new HelixToolkit.Maths.Color4(r / 255f, g / 255f, b / 255f, a / 255f),
            SpecularColor = new HelixToolkit.Maths.Color4(0.1f, 0.1f, 0.1f, 1f),
            SpecularShininess = 1f
        };

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


/// <summary>
/// Serializable snapshot of a single cephalometric landmark.
/// Stored on MainViewModel.SavedCephLandmarks so ProjectViewModel can persist it.
/// </summary>
public record CephLandmarkSave(
    string Name,
    double? X2D, double? Y2D,
    double? X3D, double? Y3D, double? Z3D);
