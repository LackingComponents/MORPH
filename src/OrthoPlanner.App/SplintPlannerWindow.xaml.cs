using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class SplintPlannerWindow : Window
{
    // ── Input ─────────────────────────────────────────────────────────────
    private readonly float[] _upperMesh;  // maxilla / upper dental cast
    private readonly float[] _lowerMesh;  // mandible / lower dental cast

    // ── Arch curves ───────────────────────────────────────────────────────
    private readonly ArchCurve _upperArch = new();
    private readonly ArchCurve _lowerArch = new();

    // ── Marker 3D models ─────────────────────────────────────────────────
    private readonly List<MeshGeometryModel3D> _upperMarkers = new();
    private readonly List<MeshGeometryModel3D> _lowerMarkers = new();

    // ── Curve preview lines ───────────────────────────────────────────────
    private LineGeometryModel3D? _upperCurveLine;
    private LineGeometryModel3D? _lowerCurveLine;

    // ── Splint preview ────────────────────────────────────────────────────
    private MeshGeometryModel3D? _splintUpperPreview;
    private MeshGeometryModel3D? _splintLowerPreview;

    // ── Headlamp handler ─────────────────────────────────────────────────
    private EventHandler? _renderingHandler;

    // ── Result ────────────────────────────────────────────────────────────
    public bool   Accepted       { get; private set; }
    public float[]? SplintVertices { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    public SplintPlannerWindow(float[] upperMesh, float[] lowerMesh, MainViewModel _)
    {
        InitializeComponent();

        _upperMesh = upperMesh;
        _lowerMesh = lowerMesh;

        // EffectsManagers — set directly, not via binding
        UpperViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        LowerViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        // Headlamp tracking (coaxial with camera, both viewports)
        _renderingHandler = (s, _) =>
        {
            UpdateHeadlamp(UpperCamera, UpperHeadlamp, UpperBacklamp);
            UpdateHeadlamp(LowerCamera, LowerHeadlamp, LowerBacklamp);
        };
        CompositionTarget.Rendering += _renderingHandler;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private static void UpdateHeadlamp(
        HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam,
        DirectionalLight3D front, DirectionalLight3D back)
    {
        if (cam == null) return;
        var dir = cam.LookDirection;
        if (dir.Length < 0.001) return;
        dir.Normalize();
        var f = new Vector3D(-dir.X, -dir.Y, -dir.Z);
        var b = new Vector3D( dir.X,  dir.Y,  dir.Z);
        if (Math.Abs(front.Direction.X - f.X) > 1e-4 ||
            Math.Abs(front.Direction.Y - f.Y) > 1e-4 ||
            Math.Abs(front.Direction.Z - f.Z) > 1e-4)
        {
            front.Direction = f;
            back.Direction  = b;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  LOAD / CLOSE
    // ═══════════════════════════════════════════════════════════
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Load meshes into viewports
        UpperGroup.Children.Add(MeshHelper.BuildModel3D(_upperMesh, 240, 230, 210));
        LowerGroup.Children.Add(MeshHelper.BuildModel3D(_lowerMesh, 240, 230, 210));

        // Camera: upper arch is viewed from BELOW (camera on -Z side, looking +Z)
        // i.e. the occlusal face of the maxilla faces downward; we look up at it.
        CenterCamera(UpperViewport, _upperMesh, lookFromBelow: true);
        CenterCamera(LowerViewport, _lowerMesh, lookFromBelow: false);

        // Watertight score
        float us = SplintEngine.WatertightScore(_upperMesh);
        float ls = SplintEngine.WatertightScore(_lowerMesh);
        if (us > 0.02f || ls > 0.02f)
            QualityText.Text = $"⚠ Mesh quality: Upper {us:P0} open | Lower {ls:P0} open — tooth imprint may vary";

        UpdateUI();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        UpperGroup.Children.Clear();
        LowerGroup.Children.Clear();
        if (UpperViewport.EffectsManager is IDisposable u) { u.Dispose(); UpperViewport.EffectsManager = null!; }
        if (LowerViewport.EffectsManager is IDisposable l) { l.Dispose(); LowerViewport.EffectsManager = null!; }
    }

    // ═══════════════════════════════════════════════════════════
    //  CAMERA
    // ═══════════════════════════════════════════════════════════
    private static void CenterCamera(Viewport3DX vp, float[] mesh, bool lookFromBelow)
    {
        if (mesh.Length < 3 || vp.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;

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
        // Use XY diagonal for distance since we're looking along Z
        double diag = Math.Sqrt(Math.Pow(maxX - minX, 2) + Math.Pow(maxY - minY, 2));
        double dist = Math.Max(diag * 0.85, 30);

        // Upper: camera below model looking up (+Z). Lower: camera above looking down (-Z).
        double zOffset = lookFromBelow ? (cz - dist) : (cz + dist);
        double lookZ   = lookFromBelow ? 1.0 : -1.0;

        cam.Position      = new Point3D(cx, cy, zOffset);
        cam.LookDirection = new Vector3D(0, 0, lookZ * dist);
        cam.UpDirection   = new Vector3D(0, -1, 0);  // -Y up so arch faces correct way

        vp.FixedRotationPointEnabled = true;
        vp.FixedRotationPoint = new Point3D(cx, cy, cz);
    }

    // ═══════════════════════════════════════════════════════════
    //  MOUSE — LEFT CLICK (add point)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseLeft(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var hits = UpperViewport.FindHits(e.GetPosition(UpperViewport));
        if (hits == null || hits.Count == 0) return;
        var pt = hits[0].PointHit;
        _upperArch.AddPoint((float)pt.X, (float)pt.Y, (float)pt.Z);
        AddMarker(UpperGroup, _upperMarkers, pt, isUpper: true);
        RefreshUpperCurve();
        UpdateUI();
        e.Handled = true;
    }

    private void LowerViewport_MouseLeft(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var hits = LowerViewport.FindHits(e.GetPosition(LowerViewport));
        if (hits == null || hits.Count == 0) return;
        var pt = hits[0].PointHit;
        _lowerArch.AddPoint((float)pt.X, (float)pt.Y, (float)pt.Z);
        AddMarker(LowerGroup, _lowerMarkers, pt, isUpper: false);
        RefreshLowerCurve();
        UpdateUI();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  MOUSE — RIGHT CLICK (remove last point)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseRight(object sender, MouseButtonEventArgs e)
    {
        if (_upperMarkers.Count == 0) return;
        var last = _upperMarkers[^1];
        UpperGroup.Children.Remove(last);
        _upperMarkers.RemoveAt(_upperMarkers.Count - 1);
        _upperArch.RemoveLast();
        RefreshUpperCurve();
        UpdateUI();
        e.Handled = true;
    }

    private void LowerViewport_MouseRight(object sender, MouseButtonEventArgs e)
    {
        if (_lowerMarkers.Count == 0) return;
        var last = _lowerMarkers[^1];
        LowerGroup.Children.Remove(last);
        _lowerMarkers.RemoveAt(_lowerMarkers.Count - 1);
        _lowerArch.RemoveLast();
        RefreshLowerCurve();
        UpdateUI();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  MARKERS
    // ═══════════════════════════════════════════════════════════
    private static MeshGeometryModel3D CreateSphere(System.Numerics.Vector3 pos,
        System.Windows.Media.Color color)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(System.Numerics.Vector3.Zero, 1.5f);
        var mat = new PhongMaterial
        {
            DiffuseColor     = new HelixToolkit.Maths.Color4(color.R/255f, color.G/255f, color.B/255f, 1f),
            SpecularColor    = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f),
            SpecularShininess = 32f
        };
        return new MeshGeometryModel3D
        {
            Geometry  = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material  = mat,
            Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
        };
    }

    private static void AddMarker(GroupModel3D group, List<MeshGeometryModel3D> list,
        System.Numerics.Vector3 pt, bool isUpper)
    {
        var color = isUpper
            ? System.Windows.Media.Color.FromRgb(100, 220, 255)  // cyan-blue for upper
            : System.Windows.Media.Color.FromRgb(255, 160, 60);  // amber for lower
        var sphere = CreateSphere(pt, color);
        list.Add(sphere);
        group.Children.Add(sphere);
    }

    // ═══════════════════════════════════════════════════════════
    //  CURVE PREVIEW
    // ═══════════════════════════════════════════════════════════
    private void RefreshUpperCurve()
    {
        if (_upperCurveLine != null) { UpperGroup.Children.Remove(_upperCurveLine); _upperCurveLine = null; }
        if (_upperArch.ControlPointCount < 2) return;
        _upperCurveLine = BuildCurveLine(_upperArch.Sample(120),
            System.Windows.Media.Color.FromRgb(100, 220, 255));
        UpperGroup.Children.Add(_upperCurveLine);
    }

    private void RefreshLowerCurve()
    {
        if (_lowerCurveLine != null) { LowerGroup.Children.Remove(_lowerCurveLine); _lowerCurveLine = null; }
        if (_lowerArch.ControlPointCount < 2) return;
        _lowerCurveLine = BuildCurveLine(_lowerArch.Sample(120),
            System.Windows.Media.Color.FromRgb(255, 160, 60));
        LowerGroup.Children.Add(_lowerCurveLine);
    }

    private static LineGeometryModel3D BuildCurveLine(
        List<(float x, float y, float z)> pts, System.Windows.Media.Color color)
    {
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        for (int i = 0; i < pts.Count - 1; i++)
            lb.AddLine(
                new System.Numerics.Vector3(pts[i].x,   pts[i].y,   pts[i].z),
                new System.Numerics.Vector3(pts[i+1].x, pts[i+1].y, pts[i+1].z));
        return new LineGeometryModel3D { Geometry = lb.ToLineGeometry3D(), Color = color, Thickness = 2.5 };
    }

    // ═══════════════════════════════════════════════════════════
    //  SLIDERS
    // ═══════════════════════════════════════════════════════════
    private void ThicknessSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => ThicknessLabel.Text = $"{e.NewValue:F1} mm";

    private void PenetrationSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
        => PenetrationLabel.Text = $"{e.NewValue:F1} mm";

    // ═══════════════════════════════════════════════════════════
    //  CLEAR
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
    //  UI STATE
    // ═══════════════════════════════════════════════════════════
    private void UpdateUI()
    {
        int u = _upperArch.ControlPointCount;
        int l = _lowerArch.ControlPointCount;
        PointCountText.Text = $"Upper: {u} pts  |  Lower: {l} pts";
        GenerateBtn.IsEnabled = u >= 3 && l >= 3;

        if (u < 3)
            StepInstructions.Text = $"Place ≥ 3 points on the UPPER arch ({u} placed). Click from molar to molar, front of arch.";
        else if (l < 3)
            StepInstructions.Text = $"Place ≥ 3 points on the LOWER arch ({l} placed). Click from molar to molar, front of arch.";
        else
            StepInstructions.Text = $"Both arches defined ({u} upper / {l} lower). Adjust sliders then click Generate.";
    }

    // ═══════════════════════════════════════════════════════════
    //  GENERATE
    // ═══════════════════════════════════════════════════════════
    private async void GenerateBtn_Click(object s, RoutedEventArgs e)
    {
        GenerateBtn.IsEnabled = false;
        AcceptBtn.Visibility  = Visibility.Collapsed;
        StatusText.Text = "Generating…";

        float thickness   = (float)ThicknessSlider.Value;
        float penetration = (float)PenetrationSlider.Value;
        var upperSampled  = _upperArch.Sample(160);
        var lowerSampled  = _lowerArch.Sample(160);
        float[] uMesh = _upperMesh, lMesh = _lowerMesh;

        float[]? splint = null;
        try
        {
            splint = await Task.Run(() => SplintEngine.GenerateSplint(
                upperSampled, lowerSampled,
                labiolingualMm: thickness,
                penetrationMm:  penetration,
                upperMesh: uMesh, lowerMesh: lMesh,
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
            StatusText.Text = "No geometry produced.";
            GenerateBtn.IsEnabled = true;
            return;
        }

        SplintVertices = splint;

        // Show translucent preview in both viewports
        void ShowPreview(GroupModel3D group, ref MeshGeometryModel3D? prev)
        {
            if (prev != null) group.Children.Remove(prev);
            prev = MeshHelper.BuildModel3D(splint, 80, 160, 255, 130);
            group.Children.Add(prev);
        }
        ShowPreview(UpperGroup, ref _splintUpperPreview);
        ShowPreview(LowerGroup, ref _splintLowerPreview);

        StepTitle.Text = "Step 2: Review Splint";
        StepInstructions.Text = "Blue = splint solid (translucent). Rotate to inspect. Click Accept to add to the model list.";
        StatusText.Text = $"{splint.Length / 9:N0} triangles";
        AcceptBtn.Visibility  = Visibility.Visible;
        GenerateBtn.IsEnabled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  ACCEPT / CANCEL
    // ═══════════════════════════════════════════════════════════
    private void AcceptBtn_Click(object s, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object s, RoutedEventArgs e)
    {
        if (AcceptBtn.Visibility == Visibility.Visible)
        {
            // Step back: remove preview, let user tweak
            if (_splintUpperPreview != null) { UpperGroup.Children.Remove(_splintUpperPreview); _splintUpperPreview = null; }
            if (_splintLowerPreview != null) { LowerGroup.Children.Remove(_splintLowerPreview); _splintLowerPreview = null; }
            SplintVertices = null;
            AcceptBtn.Visibility = Visibility.Collapsed;
            StepTitle.Text = "Step 1: Place ≥ 3 points on each arch";
            StepInstructions.Text = "Adjust points or sliders then click Generate again.";
            return;
        }
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
