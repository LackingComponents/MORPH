using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class SplintPlannerWindow : Window
{
    // ── Input meshes ──────────────────────────────────────────────────────
    private readonly float[] _upperMesh;
    private readonly float[] _lowerMesh;
    private readonly MainViewModel _vm;

    // ── Arch curves ───────────────────────────────────────────────────────
    private readonly ArchCurve _upperArch = new();
    private readonly ArchCurve _lowerArch = new();

    // ── Marker models ─────────────────────────────────────────────────────
    private readonly List<MeshGeometryModel3D> _upperMarkers = new();
    private readonly List<MeshGeometryModel3D> _lowerMarkers = new();

    // ── Curve preview lines ───────────────────────────────────────────────
    private LineGeometryModel3D? _upperCurveLine;
    private LineGeometryModel3D? _lowerCurveLine;

    // ── Generated splint preview (shown in both viewports) ────────────────
    private MeshGeometryModel3D? _splintUpperPreview;
    private MeshGeometryModel3D? _splintLowerPreview;

    // ── Result ─────────────────────────────────────────────────────────────
    public bool Accepted { get; private set; }
    public float[]? SplintVertices { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    public SplintPlannerWindow(float[] upperMesh, float[] lowerMesh, MainViewModel vm)
    {
        DataContext = this;
        InitializeComponent();

        _upperMesh = upperMesh;
        _lowerMesh = lowerMesh;
        _vm        = vm;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpperViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        LowerViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        LoadMeshIntoViewport(UpperGroup, _upperMesh, 220, 200, 170);
        LoadMeshIntoViewport(LowerGroup, _lowerMesh, 220, 140, 120);

        CenterCamera(UpperViewport, UpperCamera, _upperMesh, lookFromBelow: true);
        CenterCamera(LowerViewport, LowerCamera, _lowerMesh, lookFromBelow: false);

        // Report mesh quality
        float upperScore = SplintEngine.WatertightScore(_upperMesh);
        float lowerScore = SplintEngine.WatertightScore(_lowerMesh);
        UpperQualityText.Text = upperScore < 0.02f
            ? $"Upper: ✔ Watertight ({upperScore:P1} open)"
            : $"Upper: ⚠ {upperScore:P1} open edges";
        LowerQualityText.Text = lowerScore < 0.02f
            ? $"Lower: ✔ Watertight ({lowerScore:P1} open)"
            : $"Lower: ⚠ {lowerScore:P1} open edges";

        UpperQualityText.Foreground = upperScore < 0.02f
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 203, 196))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 183, 77));
        LowerQualityText.Foreground = lowerScore < 0.02f
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 203, 196))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 183, 77));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        UpperGroup.Children.Clear();
        LowerGroup.Children.Clear();
        if (UpperViewport.EffectsManager is IDisposable ud) { ud.Dispose(); UpperViewport.EffectsManager = null!; }
        if (LowerViewport.EffectsManager is IDisposable ld) { ld.Dispose(); LowerViewport.EffectsManager = null!; }
    }

    // ═══════════════════════════════════════════════════════════
    //  MESH LOADING
    // ═══════════════════════════════════════════════════════════
    private static void LoadMeshIntoViewport(GroupModel3D group, float[] mesh,
        byte r, byte g, byte b)
    {
        group.Children.Clear();
        var model = MeshHelper.BuildModel3D(mesh, r, g, b, 200);
        group.Children.Add(model);
    }

    // ═══════════════════════════════════════════════════════════
    //  CAMERA SETUP
    // ═══════════════════════════════════════════════════════════
    private static void CenterCamera(Viewport3DX vp, HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam,
        float[] mesh, bool lookFromBelow)
    {
        if (mesh.Length < 3) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        for (int i = 0; i + 2 < mesh.Length; i += 3)
        {
            float x = mesh[i], y = mesh[i+1], z = mesh[i+2];
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }

        double cx = (minX + maxX) / 2.0;
        double cy = (minY + maxY) / 2.0;
        double cz = (minZ + maxZ) / 2.0;
        double diag = Math.Sqrt(
            Math.Pow(maxX - minX, 2) +
            Math.Pow(maxY - minY, 2)) * 0.8;
        double dist = Math.Max(diag, 20);

        // Upper: look from below (+Z away), down → LookDirection 0,0,+1
        // Lower: look from above (-Z away), down → LookDirection 0,0,-1
        double zOffset = lookFromBelow ? (cz - dist) : (cz + dist);
        double lookZ   = lookFromBelow ? 1.0 : -1.0;

        cam.Position      = new Point3D(cx, cy, zOffset);
        cam.LookDirection = new Vector3D(0, 0, lookZ);
        cam.UpDirection   = new Vector3D(0, -1, 0);

        vp.FixedRotationPointEnabled = true;
        vp.FixedRotationPoint = new Point3D(cx, cy, cz);
    }

    // ═══════════════════════════════════════════════════════════
    //  MOUSE — POINT PLACEMENT (raycasting)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var pos = e.GetPosition(UpperViewport);
        var hit = UpperViewport.FindHits(pos).FirstOrDefault(h => h.ModelHit is Element3D);
        if (hit == null) return;

        float x = (float)hit.PointHit.X, y = (float)hit.PointHit.Y, z = (float)hit.PointHit.Z;
        _upperArch.AddPoint(x, y, z);
        AddMarker(UpperGroup, _upperMarkers, x, y, z, isUpper: true);
        RefreshUpperCurve();
        UpdateUI();
        e.Handled = true;
    }

    private void LowerViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var pos = e.GetPosition(LowerViewport);
        var hit = LowerViewport.FindHits(pos).FirstOrDefault(h => h.ModelHit is Element3D);
        if (hit == null) return;

        float x = (float)hit.PointHit.X, y = (float)hit.PointHit.Y, z = (float)hit.PointHit.Z;
        _lowerArch.AddPoint(x, y, z);
        AddMarker(LowerGroup, _lowerMarkers, x, y, z, isUpper: false);
        RefreshLowerCurve();
        UpdateUI();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  MARKERS
    // ═══════════════════════════════════════════════════════════
    private static void AddMarker(GroupModel3D group, List<MeshGeometryModel3D> list,
        float x, float y, float z, bool isUpper)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), 1f);
        var mat = new PhongMaterial
        {
            DiffuseColor = isUpper
                ? new HelixToolkit.Maths.Color4(0.3f, 0.9f, 1f, 1f)    // cyan-blue
                : new HelixToolkit.Maths.Color4(1f,   0.7f, 0.2f, 1f)  // amber
        };
        var model = new MeshGeometryModel3D
        {
            Geometry  = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material  = mat,
            Transform = new TranslateTransform3D(x, y, z)
        };
        list.Add(model);
        group.Children.Add(model);
    }

    // ═══════════════════════════════════════════════════════════
    //  CURVE PREVIEW LINES
    // ═══════════════════════════════════════════════════════════
    private void RefreshUpperCurve()
    {
        if (_upperCurveLine != null) UpperGroup.Children.Remove(_upperCurveLine);
        _upperCurveLine = null;
        if (_upperArch.ControlPointCount < 2) return;

        var pts = _upperArch.Sample(100);
        _upperCurveLine = BuildCurveLine(SplintEngine.CurveToLineStrip(pts),
            new HelixToolkit.Maths.Color4(0.3f, 0.9f, 1f, 1f));
        UpperGroup.Children.Add(_upperCurveLine);
    }

    private void RefreshLowerCurve()
    {
        if (_lowerCurveLine != null) LowerGroup.Children.Remove(_lowerCurveLine);
        _lowerCurveLine = null;
        if (_lowerArch.ControlPointCount < 2) return;

        var pts = _lowerArch.Sample(100);
        _lowerCurveLine = BuildCurveLine(SplintEngine.CurveToLineStrip(pts),
            new HelixToolkit.Maths.Color4(1f, 0.7f, 0.2f, 1f));
        LowerGroup.Children.Add(_lowerCurveLine);
    }

    private static LineGeometryModel3D BuildCurveLine(float[] lineStrip,
        HelixToolkit.Maths.Color4 color)
    {
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        for (int i = 0; i + 5 < lineStrip.Length; i += 6)
            lb.AddLine(
                new System.Numerics.Vector3(lineStrip[i],   lineStrip[i+1], lineStrip[i+2]),
                new System.Numerics.Vector3(lineStrip[i+3], lineStrip[i+4], lineStrip[i+5]));
        return new LineGeometryModel3D
        {
            Geometry  = lb.ToLineGeometry3D(),
            Color     = System.Windows.Media.Color.FromArgb(
                (byte)(color.Alpha * 255), (byte)(color.Red * 255),
                (byte)(color.Green * 255), (byte)(color.Blue * 255)),
            Thickness = 2.5
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  UI STATE
    // ═══════════════════════════════════════════════════════════
    private void UpdateUI()
    {
        // Refresh point lists
        UpperPointsList.Items.Clear();
        var uSample = _upperArch.Sample(1000);  // just count; reuse sampled pts
        for (int i = 0; i < _upperMarkers.Count; i++)
            UpperPointsList.Items.Add($"U{i+1}");

        LowerPointsList.Items.Clear();
        for (int i = 0; i < _lowerMarkers.Count; i++)
            LowerPointsList.Items.Add($"L{i+1}");

        bool ready = _upperArch.ControlPointCount >= 3 && _lowerArch.ControlPointCount >= 3;
        GenerateBtn.IsEnabled = ready;

        if (_upperArch.ControlPointCount < 3)
            InstructionText.Text = $"Place ≥ 3 points on UPPER arch. ({_upperArch.ControlPointCount} placed)";
        else if (_lowerArch.ControlPointCount < 3)
            InstructionText.Text = $"Place ≥ 3 points on LOWER arch. ({_lowerArch.ControlPointCount} placed)";
        else
            InstructionText.Text = $"Both arches defined ({_upperArch.ControlPointCount}U / {_lowerArch.ControlPointCount}L). Click Generate.";
    }

    // ═══════════════════════════════════════════════════════════
    //  SLIDER CALLBACKS
    // ═══════════════════════════════════════════════════════════
    private void ThicknessSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => ThicknessLabel.Text = e.NewValue.ToString("F1");

    private void PenetrationSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => PenetrationLabel.Text = e.NewValue.ToString("F1");

    // ═══════════════════════════════════════════════════════════
    //  CLEAR BUTTONS
    // ═══════════════════════════════════════════════════════════
    private void ClearUpper_Click(object s, RoutedEventArgs e)
    {
        foreach (var m in _upperMarkers) UpperGroup.Children.Remove(m);
        _upperMarkers.Clear();
        if (_upperCurveLine != null) { UpperGroup.Children.Remove(_upperCurveLine); _upperCurveLine = null; }
        _upperArch.Clear();
        UpdateUI();
    }

    private void ClearLower_Click(object s, RoutedEventArgs e)
    {
        foreach (var m in _lowerMarkers) LowerGroup.Children.Remove(m);
        _lowerMarkers.Clear();
        if (_lowerCurveLine != null) { LowerGroup.Children.Remove(_lowerCurveLine); _lowerCurveLine = null; }
        _lowerArch.Clear();
        UpdateUI();
    }

    // ═══════════════════════════════════════════════════════════
    //  GENERATE
    // ═══════════════════════════════════════════════════════════
    private async void GenerateBtn_Click(object s, RoutedEventArgs e)
    {
        GenerateBtn.IsEnabled = false;
        AcceptBtn.IsEnabled   = false;
        StatusText.Text = "Generating splint geometry…";

        float thickness   = (float)ThicknessSlider.Value;
        float penetration = (float)PenetrationSlider.Value;

        var upperSampled = _upperArch.Sample(160);
        var lowerSampled = _lowerArch.Sample(160);
        float[] uMesh = _upperMesh;
        float[] lMesh = _lowerMesh;

        float[]? splint = null;
        try
        {
            splint = await Task.Run(() => SplintEngine.GenerateSplint(
                upperSampled, lowerSampled,
                labiolingualMm: thickness,
                penetrationMm:  penetration,
                upperMesh: uMesh,
                lowerMesh: lMesh,
                sampleCount: 160));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            GenerateBtn.IsEnabled = true;
            return;
        }

        if (splint == null || splint.Length < 9)
        {
            StatusText.Text = "Generation failed — no geometry produced.";
            GenerateBtn.IsEnabled = true;
            return;
        }

        SplintVertices = splint;

        // Show translucent preview in both viewports
        void ShowPreview(GroupModel3D group, ref MeshGeometryModel3D? prev)
        {
            if (prev != null) group.Children.Remove(prev);
            prev = MeshHelper.BuildModel3D(splint, 150, 200, 255, 140);
            group.Children.Add(prev);
        }

        ShowPreview(UpperGroup, ref _splintUpperPreview);
        ShowPreview(LowerGroup, ref _splintLowerPreview);

        int triCount = splint.Length / 9;
        StatusText.Text = $"Splint generated: {triCount:N0} triangles.\nReview and click Accept.";
        AcceptBtn.IsEnabled   = true;
        GenerateBtn.IsEnabled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  ACCEPT / CANCEL
    // ═══════════════════════════════════════════════════════════
    private void AcceptBtn_Click(object s, RoutedEventArgs e)
    {
        Accepted = true;
        Close();
    }

    private void CancelBtn_Click(object s, RoutedEventArgs e)
    {
        Accepted = false;
        SplintVertices = null;
        Close();
    }
}
