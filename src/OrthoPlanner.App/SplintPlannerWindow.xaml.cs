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

    // ── Clinical config (pose/labelling carried from caller; geometry merged
    //    from the sliders at Generate time) ─────────────────────────────────
    private readonly SplintConfig _config;

    // ── Arch curves ───────────────────────────────────────────────────────
    private readonly ArchCurve _upperArch = new();
    private readonly ArchCurve _lowerArch = new();

    // ── Marker 3D models ─────────────────────────────────────────────────
    private readonly List<MeshGeometryModel3D> _upperMarkers = new();
    private readonly List<MeshGeometryModel3D> _lowerMarkers = new();

    // ── Curve preview lines ───────────────────────────────────────────────
    private LineGeometryModel3D? _upperCurveLine;
    private LineGeometryModel3D? _lowerCurveLine;

    // ── Ribbon preview (labio-lingual width footprint) ────────────────────
    private MeshGeometryModel3D? _upperRibbon;
    private MeshGeometryModel3D? _lowerRibbon;

    // ── Splint preview ────────────────────────────────────────────────────
    private MeshGeometryModel3D? _splintUpperPreview;
    private MeshGeometryModel3D? _splintLowerPreview;

    // ── Headlamp handler ────────────────────────────────────────────────────
    private EventHandler? _renderingHandler;

    // ── Drag state ────────────────────────────────────────────────────────
    private bool _draggingUpper, _draggingLower;
    private int  _dragIdxUpper = -1, _dragIdxLower = -1;

    // ── Result ────────────────────────────────────────────────────────────
    public bool   Accepted       { get; private set; }
    public float[]? SplintVertices { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    public SplintPlannerWindow(float[] upperMesh, float[] lowerMesh, MainViewModel _,
        SplintConfig? config = null)
    {
        InitializeComponent();

        _upperMesh = upperMesh;
        _lowerMesh = lowerMesh;
        _config    = config ?? new SplintConfig();

        // Drive the title from the clinical config (no hard-coded "Final Occlusion").
        Title = $"Splint Planner — {_config.DisplayName}";

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
        double zoomFactor = lookFromBelow ? 1.55 : 1.15; // Upper further, Lower closer
        double dist = Math.Max(diag * zoomFactor, 30);

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
    //  MOUSE — LEFT CLICK (add point) / DRAG (move point)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseLeft(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var hits = UpperViewport.FindHits(e.GetPosition(UpperViewport));
        if (hits == null || hits.Count == 0) return;

        // Clicked on an existing marker? → start drag
        foreach (var hit in hits)
        {
            int idx = _upperMarkers.IndexOf(hit.ModelHit as MeshGeometryModel3D);
            if (idx >= 0) { _draggingUpper = true; _dragIdxUpper = idx; e.Handled = true; return; }
        }

        // Clicked on the mesh → add new point
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

        foreach (var hit in hits)
        {
            int idx = _lowerMarkers.IndexOf(hit.ModelHit as MeshGeometryModel3D);
            if (idx >= 0) { _draggingLower = true; _dragIdxLower = idx; e.Handled = true; return; }
        }

        var pt = hits[0].PointHit;
        _lowerArch.AddPoint((float)pt.X, (float)pt.Y, (float)pt.Z);
        AddMarker(LowerGroup, _lowerMarkers, pt, isUpper: false);
        RefreshLowerCurve();
        UpdateUI();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  MOUSE — DRAG (move existing point)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingUpper || _dragIdxUpper < 0) return;
        var hits = UpperViewport.FindHits(e.GetPosition(UpperViewport));
        if (hits == null) return;
        // Skip hits on markers
        foreach (var hit in hits)
        {
            if (_upperMarkers.Contains(hit.ModelHit as MeshGeometryModel3D)) continue;
            var pt = hit.PointHit;
            _upperArch.UpdatePoint(_dragIdxUpper, (float)pt.X, (float)pt.Y, (float)pt.Z);
            _upperMarkers[_dragIdxUpper].Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z);
            RefreshUpperCurve();
            e.Handled = true;
            break;
        }
    }

    private void LowerViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingLower || _dragIdxLower < 0) return;
        var hits = LowerViewport.FindHits(e.GetPosition(LowerViewport));
        if (hits == null) return;
        foreach (var hit in hits)
        {
            if (_lowerMarkers.Contains(hit.ModelHit as MeshGeometryModel3D)) continue;
            var pt = hit.PointHit;
            _lowerArch.UpdatePoint(_dragIdxLower, (float)pt.X, (float)pt.Y, (float)pt.Z);
            _lowerMarkers[_dragIdxLower].Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z);
            RefreshLowerCurve();
            e.Handled = true;
            break;
        }
    }

    private void UpperViewport_MouseLeftUp(object sender, MouseButtonEventArgs e)
    { _draggingUpper = false; _dragIdxUpper = -1; }

    private void LowerViewport_MouseLeftUp(object sender, MouseButtonEventArgs e)
    { _draggingLower = false; _dragIdxLower = -1; }

    // ═══════════════════════════════════════════════════════════
    //  MOUSE — RIGHT CLICK (remove nearest point)
    // ═══════════════════════════════════════════════════════════
    private void UpperViewport_MouseRight(object sender, MouseButtonEventArgs e)
    {
        int idx = FindNearestMarker(_upperArch, e.GetPosition(UpperViewport), UpperViewport);
        if (idx < 0) return;
        UpperGroup.Children.Remove(_upperMarkers[idx]);
        _upperMarkers.RemoveAt(idx);
        _upperArch.RemoveAt(idx);
        RefreshUpperCurve();
        UpdateUI();
        e.Handled = true;
    }

    private void LowerViewport_MouseRight(object sender, MouseButtonEventArgs e)
    {
        int idx = FindNearestMarker(_lowerArch, e.GetPosition(LowerViewport), LowerViewport);
        if (idx < 0) return;
        LowerGroup.Children.Remove(_lowerMarkers[idx]);
        _lowerMarkers.RemoveAt(idx);
        _lowerArch.RemoveAt(idx);
        RefreshLowerCurve();
        UpdateUI();
        e.Handled = true;
    }

    /// <summary>Returns the index of the control point nearest to the clicked screen position.</summary>
    private static int FindNearestMarker(ArchCurve arch, System.Windows.Point screenPt, Viewport3DX vp)
    {
        int best = -1;
        float bestDist = float.MaxValue;
        // Use nearest in 3D to any hit point
        var hits = vp.FindHits(screenPt);
        if (hits == null || hits.Count == 0) return -1;
        var clickPt3 = hits[0].PointHit;
        for (int i = 0; i < arch.ControlPointCount; i++)
        {
            var p = arch.GetPoint(i);
            float dx=(float)(p.x-clickPt3.X), dy=(float)(p.y-clickPt3.Y), dz=(float)(p.z-clickPt3.Z);
            float d = dx*dx+dy*dy+dz*dz;
            if (d < bestDist) { bestDist=d; best=i; }
        }
        return best;
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
        var color = System.Windows.Media.Color.FromRgb(100, 220, 255);  // cyan-blue for both
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
        var sampled = _upperArch.Sample(120);
        _upperCurveLine = BuildCurveLine(sampled, System.Windows.Media.Color.FromRgb(0, 188, 212));
        UpperGroup.Children.Add(_upperCurveLine);
        RefreshUpperRibbon(sampled);
    }

    private void RefreshLowerCurve()
    {
        if (_lowerCurveLine != null) { LowerGroup.Children.Remove(_lowerCurveLine); _lowerCurveLine = null; }
        if (_lowerArch.ControlPointCount < 2) return;
        var sampled = _lowerArch.Sample(120);
        _lowerCurveLine = BuildCurveLine(sampled, System.Windows.Media.Color.FromRgb(0, 188, 212));
        LowerGroup.Children.Add(_lowerCurveLine);
        RefreshLowerRibbon(sampled);
    }

    private void RefreshUpperRibbon(List<(float x,float y,float z)>? sampled = null)
    {
        if (_upperRibbon != null) { UpperGroup.Children.Remove(_upperRibbon); _upperRibbon = null; }
        if (_upperArch.ControlPointCount < 2) return;
        var pts = sampled ?? _upperArch.Sample(120);
        float w = (float)ThicknessSlider.Value;
        float bias = (float)LingualBuccalBiasSlider.Value;
        var ribbonMesh = SplintEngine.GenerateRibbonMesh(pts, w, bias);
        if (ribbonMesh.Length < 9) return;
        _upperRibbon = MeshHelper.BuildModel3D(ribbonMesh, 0, 188, 212, 100);
        UpperGroup.Children.Add(_upperRibbon);
    }

    private void RefreshLowerRibbon(List<(float x,float y,float z)>? sampled = null)
    {
        if (_lowerRibbon != null) { LowerGroup.Children.Remove(_lowerRibbon); _lowerRibbon = null; }
        if (_lowerArch.ControlPointCount < 2) return;
        var pts = sampled ?? _lowerArch.Sample(120);
        float w = (float)ThicknessSlider.Value;
        float bias = (float)LingualBuccalBiasSlider.Value;
        var ribbonMesh = SplintEngine.GenerateRibbonMesh(pts, w, bias);
        if (ribbonMesh.Length < 9) return;
        _lowerRibbon = MeshHelper.BuildModel3D(ribbonMesh, 0, 188, 212, 100);
        LowerGroup.Children.Add(_lowerRibbon);
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
    {
        if (ThicknessLabel != null) ThicknessLabel.Text = $"{e.NewValue:F1} mm";
        // Live ribbon update when slider moves
        if (_upperArch?.ControlPointCount >= 2) RefreshUpperRibbon();
        if (_lowerArch?.ControlPointCount >= 2) RefreshLowerRibbon();
    }

    private void UpperPenetrationSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    { if (UpperPenetrationLabel != null) UpperPenetrationLabel.Text = $"{e.NewValue:F1} mm"; }

    private void LowerPenetrationSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    { if (LowerPenetrationLabel != null) LowerPenetrationLabel.Text = $"{e.NewValue:F1} mm"; }

    private void BridgeThicknessSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    { if (BridgeThicknessLabel != null) BridgeThicknessLabel.Text = $"{e.NewValue:F1} mm"; }

    private void LingualBuccalBiasSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LingualBuccalBiasLabel != null) LingualBuccalBiasLabel.Text = $"{e.NewValue:F1} mm";
        // Live ribbon update when slider moves
        if (_upperArch?.ControlPointCount >= 2) RefreshUpperRibbon();
        if (_lowerArch?.ControlPointCount >= 2) RefreshLowerRibbon();
    }

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

        var upperSampled  = _upperArch.Sample(160);
        var lowerSampled  = _lowerArch.Sample(160);
        float[] uMesh = _upperMesh, lMesh = _lowerMesh;

        // Merge live slider geometry into the clinical config carried from the caller.
        var genConfig = _config with
        {
            LabiolingualMm      = (float)ThicknessSlider.Value,
            UpperPenetrationMm  = (float)UpperPenetrationSlider.Value,
            LowerPenetrationMm  = (float)LowerPenetrationSlider.Value,
            LingualBuccalBiasMm = (float)LingualBuccalBiasSlider.Value,
            BridgeThicknessMm   = (float)BridgeThicknessSlider.Value,
            SampleCount         = 160,
        };

        SplintResult result;
        try
        {
            result = await Task.Run(() => SplintEngine.GenerateSplint(
                upperSampled, lowerSampled, genConfig,
                upperMesh: uMesh, lowerMesh: lMesh));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            GenerateBtn.IsEnabled = true;
            return;
        }

        float[] splint = result.Vertices;
        if (splint.Length < 9)
        {
            StatusText.Text = result.Warnings.Count > 0
                ? result.Warnings[0]
                : "No geometry produced.";
            GenerateBtn.IsEnabled = true;
            return;
        }

        SplintVertices = splint;

        // Build splint model
        var splintModel = MeshHelper.BuildModel3D(splint, 80, 160, 255, 140);

        // Combined view: show BOTH meshes + splint in BOTH viewports
        // Upper viewport: upper mesh (already there) + lower mesh (translucent) + splint
        // Lower viewport: lower mesh (already there) + upper mesh (translucent) + splint
        void AddIfNotNull(GroupModel3D grp, float[]? mesh, byte r, byte g, byte b, byte a,
            ref MeshGeometryModel3D? slot)
        {
            if (slot != null) grp.Children.Remove(slot);
            if (mesh == null || mesh.Length < 9) { slot = null; return; }
            slot = MeshHelper.BuildModel3D(mesh, r, g, b, a);
            grp.Children.Add(slot);
        }
        AddIfNotNull(UpperGroup, splint, 80, 160, 255, 140, ref _splintUpperPreview);
        AddIfNotNull(LowerGroup, splint, 80, 160, 255, 140, ref _splintLowerPreview);

        // Add opposing arch to each viewport (translucent, grey)
        MeshGeometryModel3D? _lowerInUpper = null, _upperInLower = null;
        AddIfNotNull(UpperGroup, _lowerMesh, 200, 200, 195, 120, ref _lowerInUpper);
        AddIfNotNull(LowerGroup, _upperMesh, 200, 200, 195, 120, ref _upperInLower);

        StepTitle.Text = "Step 2: Review Splint";
        StepInstructions.Text = "Blue solid = splint. Grey = opposing arch. Rotate to inspect. Click Accept.";

        StatusText.Text = $"{splint.Length / 9:N0} triangles";
        if (result.IsManifold)
            QualityText.Text = "✔ Splint is a closed, printable manifold";
        else
            QualityText.Text = $"⚠ Splint: {result.OpenEdgeFraction:P0} open edges — review before printing";

        // Surface any clinical/repair warnings (scan fallback, residual open edges, …).
        if (result.Warnings.Count > 0)
            MessageBox.Show(string.Join("\n\n", result.Warnings),
                "Splint Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);

        AcceptBtn.Visibility  = Visibility.Visible;
        GenerateBtn.IsEnabled = true;
    }

    // ═══════════════════════════════════════════════════════════
    //  ACCEPT / CANCEL
    // ═══════════════════════════════════════════════════════════
    private void AcceptBtn_Click(object s, RoutedEventArgs e)
    {
        Accepted = true;
        Close();  // window was opened with Show(), not ShowDialog() — no DialogResult
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
        Close();
    }
}
