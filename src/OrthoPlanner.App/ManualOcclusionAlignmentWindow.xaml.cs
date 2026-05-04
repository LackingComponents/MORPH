using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

// ── Shared pair item for the landmark list ─────────────────────────────────
public class OcclusionPairItem : INotifyPropertyChanged
{
    public int  Index { get; set; }
    public string Label     => $"#{Index + 1}";
    public string LeftText  { get; set; } = "—";
    public string RightText { get; set; } = "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightText)));
    }
}

// ── Window ─────────────────────────────────────────────────────────────────
public partial class ManualOcclusionAlignmentWindow : Window
{
    // ── Input data ────────────────────────────────────────────────────────
    private readonly SegmentViewModel _maxilla;
    private readonly SegmentViewModel _mandible;
    private readonly MeshViewModel    _occlusion;

    // Working vertex copies — both _occVerts and _manVerts are mutated in-place per step
    private readonly List<float[]> _maxVerts;
    private readonly List<float[]> _manVerts;
    private readonly List<float[]> _occVerts;           // current (possibly transformed) state
    private readonly List<float[]> _occOriginal;        // pristine snapshot for back-tracking
    private readonly List<float[]> _manOriginal;        // pristine snapshot of mandible for back-tracking

    // ── Public outputs ────────────────────────────────────────────────────
    public bool      Accepted              { get; private set; }
    public Matrix3D  MaxillaTransform      { get; private set; } = Matrix3D.Identity;
    public Matrix3D  MandibleTransform     { get; private set; } = Matrix3D.Identity;
    public Matrix3D  FinalOcclusionTransform { get; private set; } = Matrix3D.Identity;

    // ── Workflow state ────────────────────────────────────────────────────
    private enum Step { PickMaxilla, ReviewMaxilla, PickMandible, ReviewMandible }
    private Step _step = Step.PickMaxilla;

    // Stored ICP results (double[4,4])
    private double[,]? _maxIcpTransform;
    private double[,]? _manIcpTransform;

    // ── Landmark lists ────────────────────────────────────────────────────
    private readonly List<(double X, double Y, double Z)?> _boneLandmarks = new();
    private readonly List<(double X, double Y, double Z)?> _occLandmarks  = new();
    private readonly List<Element3D> _boneMarkerVisuals = new();
    private readonly List<Element3D> _occMarkerVisuals  = new();
    private readonly ObservableCollection<OcclusionPairItem> _pairs = new();

    // ── Rendering handler (stored for proper disposal) ────────────────────
    private EventHandler? _renderingHandler;

    // ─────────────────────────────────────────────────────────────────────
    public ManualOcclusionAlignmentWindow(
        SegmentViewModel maxilla,
        SegmentViewModel mandible,
        MeshViewModel    occlusion)
    {
        InitializeComponent();

        _maxilla   = maxilla;
        _mandible  = mandible;
        _occlusion = occlusion;

        _maxVerts   = maxilla.Vertices   != null ? MeshHelper.ToVertexList(maxilla.Vertices)   : new();
        _manVerts   = mandible.Vertices  != null ? MeshHelper.ToVertexList(mandible.Vertices)  : new();
        _occVerts   = occlusion.Vertices != null ? MeshHelper.ToVertexList(occlusion.Vertices) : new();
        _occOriginal = _occVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _manOriginal = _manVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();

        // Set up EffectsManagers
        BoneViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        OccViewport.EffectsManager  = new HelixToolkit.SharpDX.DefaultEffectsManager();

        PairsList.ItemsSource = _pairs;

        // Headlamp tracking — identical pattern to DentalAlignmentWindow
        _renderingHandler = (_, _) =>
        {
            TrackCamera(BoneCamera, BoneHeadlamp, BoneBacklamp);
            TrackCamera(OccCamera,  OccHeadlamp,  OccBacklamp);
        };
        System.Windows.Media.CompositionTarget.Rendering += _renderingHandler;

        Loaded += (_, _) => LoadStep();
        Closed += OnWindowClosed;
    }

    // ── Disposal ──────────────────────────────────────────────────────────
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        BoneGroup.Children.Clear();
        OccGroup.Children.Clear();
        if (BoneViewport.EffectsManager is IDisposable d1) d1.Dispose();
        if (OccViewport.EffectsManager  is IDisposable d2) d2.Dispose();
        BoneViewport.EffectsManager = null;
        OccViewport.EffectsManager  = null;
    }

    // ── Camera tracking (headlamp follows gaze — identical to Dental) ─────
    private static void TrackCamera(
        HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam,
        DirectionalLight3D headlamp,
        DirectionalLight3D backlamp)
    {
        if (cam == null || headlamp == null || backlamp == null) return;
        var dir = cam.LookDirection;
        if (dir.Length < 0.001) return;
        dir.Normalize();
        var front = new Vector3D(-dir.X, -dir.Y, -dir.Z);
        var back  = new Vector3D( dir.X,  dir.Y,  dir.Z);
        if (Math.Abs(headlamp.Direction.X - front.X) > 1e-4 ||
            Math.Abs(headlamp.Direction.Y - front.Y) > 1e-4 ||
            Math.Abs(headlamp.Direction.Z - front.Z) > 1e-4)
        {
            headlamp.Direction = front;
            backlamp.Direction = back;
        }
    }

    // ── Step loader ───────────────────────────────────────────────────────
    private void LoadStep()
    {
        // Clear everything from both viewports
        BoneGroup.Children.Clear();
        OccGroup.Children.Clear();
        ClearMarkersInternal();

        switch (_step)
        {
            // ── Step 1: pick landmarks on Maxilla vs Occlusion (upper teeth) ─
            case Step.PickMaxilla:
                StepTitle.Text        = "Step 1: Align Maxilla — Pick Matching Landmarks";
                StepInstructions.Text = "Left-click the Maxilla bone, then the matching upper-teeth point on the Occlusion scan. " +
                                        "Spread at least 3 pairs across the arch for a stable rotation. Right-click to remove.";
                BoneLabel.Text = "🦴 Maxilla";
                OccLabel.Text  = "🦷 Occlusion (upper teeth)";

                if (_maxVerts.Count > 0)
                {
                    BoneGroup.Children.Add(MeshHelper.BuildModel3D(
                        _maxVerts, (byte)_maxilla.ColorR, (byte)_maxilla.ColorG, (byte)_maxilla.ColorB));
                    CenterViewport(BoneViewport, _maxVerts, 1.2);
                }
                if (_occVerts.Count > 0)
                {
                    OccGroup.Children.Add(MeshHelper.BuildModel3D(_occVerts, 245, 245, 230));
                    CenterViewport(OccViewport, _occVerts, 1.5);
                }

                ComputeBtn.Content    = "⚡ Compute Maxilla ICP";
                ComputeBtn.IsEnabled  = false;
                ComputeBtn.Visibility = Visibility.Visible;
                NextStepBtn.Visibility = Visibility.Collapsed;
                AcceptBtn.Visibility   = Visibility.Collapsed;
                RmsText.Text = "";
                break;

            // ── Step 2: review Maxilla result (vivid overlay, right pane) ────
            case Step.ReviewMaxilla:
                StepTitle.Text        = "Step 2: Review Maxilla Alignment";
                StepInstructions.Text = "Blue = Maxilla bone; Gold = Occlusion scan (aligned). " +
                                        "Check the fit. Click 'Next Step' to continue with the Mandible, or 'Back' to re-pick.";
                ComputeBtn.Visibility  = Visibility.Collapsed;
                NextStepBtn.Visibility = Visibility.Visible;
                NextStepBtn.IsEnabled  = true;
                AcceptBtn.Visibility   = Visibility.Collapsed;

                // Left pane: show the original Maxilla in its natural colour
                if (_maxVerts.Count > 0)
                {
                    BoneGroup.Children.Add(MeshHelper.BuildModel3D(
                        _maxVerts, (byte)_maxilla.ColorR, (byte)_maxilla.ColorG, (byte)_maxilla.ColorB));
                    CenterViewport(BoneViewport, _maxVerts, 1.2);
                }

                // Right pane: vivid overlay — dark-blue Maxilla + gold aligned Occlusion
                {
                    var blueMax = MeshHelper.BuildModel3D(_maxVerts, 80, 160, 255, 180);
                    OccGroup.Children.Add(blueMax);

                    var goldOcc = MeshHelper.BuildModel3D(_occVerts, 255, 230, 90);
                    OccGroup.Children.Add(goldOcc);

                    CenterViewport(OccViewport, _maxVerts, 1.0);
                }
                break;

            // ── Step 3: pick landmarks on Mandible vs Occlusion (lower teeth) ─
            case Step.PickMandible:
                StepTitle.Text        = "Step 3: Align Mandible — Pick Matching Landmarks";
                StepInstructions.Text = "The Occlusion scan is now locked to the Maxilla. " +
                                        "Left-click the Mandible bone and the matching lower-teeth point on the Occlusion scan. " +
                                        "Spread at least 3 pairs. Right-click to remove.";
                BoneLabel.Text = "🦴 Mandible";
                OccLabel.Text  = "🦷 Occlusion (lower teeth — Maxilla-aligned)";

                if (_manVerts.Count > 0)
                {
                    BoneGroup.Children.Add(MeshHelper.BuildModel3D(
                        _manVerts, (byte)_mandible.ColorR, (byte)_mandible.ColorG, (byte)_mandible.ColorB));
                    CenterViewport(BoneViewport, _manVerts, 1.2);
                }
                if (_occVerts.Count > 0)
                {
                    OccGroup.Children.Add(MeshHelper.BuildModel3D(_occVerts, 245, 245, 230));
                    CenterViewport(OccViewport, _occVerts, 1.5);
                }

                ComputeBtn.Content    = "⚡ Compute Mandible ICP";
                ComputeBtn.IsEnabled  = false;
                ComputeBtn.Visibility = Visibility.Visible;
                NextStepBtn.Visibility = Visibility.Collapsed;
                AcceptBtn.Visibility   = Visibility.Collapsed;
                RmsText.Text = _maxIcpTransform != null
                    ? RmsText.Text   // keep whatever maxilla RMS was shown
                    : "";
                break;

            // ── Step 4: review Mandible result ───────────────────────────────
            case Step.ReviewMandible:
                StepTitle.Text        = "Step 4: Review Mandible Alignment";
                StepInstructions.Text = "Blue = Mandible bone; Gold = Occlusion scan. " +
                                        "Check the bite fit. Click 'Accept & Finish' to apply, or 'Back' to re-pick.";
                ComputeBtn.Visibility  = Visibility.Collapsed;
                NextStepBtn.Visibility = Visibility.Collapsed;
                AcceptBtn.Visibility   = Visibility.Visible;
                AcceptBtn.IsEnabled    = true;

                // Left pane: Mandible in natural colour
                if (_manVerts.Count > 0)
                {
                    BoneGroup.Children.Add(MeshHelper.BuildModel3D(
                        _manVerts, (byte)_mandible.ColorR, (byte)_mandible.ColorG, (byte)_mandible.ColorB));
                    CenterViewport(BoneViewport, _manVerts, 1.2);
                }

                // Right pane: vivid overlay — dark-blue Mandible + gold aligned Occlusion
                {
                    var blueMand = MeshHelper.BuildModel3D(_manVerts, 80, 160, 255, 180);
                    OccGroup.Children.Add(blueMand);

                    var goldOcc = MeshHelper.BuildModel3D(_occVerts, 255, 230, 90);
                    OccGroup.Children.Add(goldOcc);

                    CenterViewport(OccViewport, _manVerts, 1.0);

                    // ── Debug: show centroids to verify transform was applied ──────
                    double mx = 0, my = 0, mz = 0;
                    foreach (var v in _manVerts) { mx += v[0]; my += v[1]; mz += v[2]; }
                    if (_manVerts.Count > 0) { mx /= _manVerts.Count; my /= _manVerts.Count; mz /= _manVerts.Count; }

                    double ox = 0, oy = 0, oz = 0;
                    foreach (var v in _occVerts) { ox += v[0]; oy += v[1]; oz += v[2]; }
                    if (_occVerts.Count > 0) { ox /= _occVerts.Count; oy /= _occVerts.Count; oz /= _occVerts.Count; }

                    double dx = mx-ox, dy = my-oy, dz = mz-oz;
                    double dist = Math.Sqrt(dx*dx + dy*dy + dz*dz);
                    StepInstructions.Text = $"Blue = Mandible bone (centroid {mx:F1},{my:F1},{mz:F1}); " +
                                           $"Gold = Occlusion (centroid {ox:F1},{oy:F1},{oz:F1}). " +
                                           $"Centroid dist: {dist:F1} mm";
                }
                break;
        }

        UpdateLandmarkUI();
    }

    // ── Camera centering (identical to DentalAlignmentWindow) ─────────────
    private static void CenterViewport(
        HelixToolkit.Wpf.SharpDX.Viewport3DX viewport,
        List<float[]> verts,
        double zoomMult)
    {
        if (verts == null || verts.Count == 0 || viewport.Camera == null) return;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in verts)
        {
            if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
            if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
            if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
        }

        var pivot    = new Point3D((minX+maxX)/2, (minY+maxY)/2, (minZ+maxZ)/2);
        double diag  = Math.Sqrt((maxX-minX)*(maxX-minX) + (maxY-minY)*(maxY-minY) + (maxZ-minZ)*(maxZ-minZ));
        double dist  = Math.Max(diag * zoomMult, 10);

        var dir = new Vector3D(0, 1, -0.3);
        dir.Normalize();

        if (viewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        cam.Position        = new Point3D(pivot.X - dir.X*dist, pivot.Y - dir.Y*dist, pivot.Z - dir.Z*dist);
        cam.LookDirection   = dir * dist;
        cam.UpDirection     = new Vector3D(0, 0, 1);

        viewport.FixedRotationPointEnabled = true;
        viewport.FixedRotationPoint        = pivot;
    }

    // ── Mouse input — Bone viewport ───────────────────────────────────────
    private void BoneViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_step != Step.PickMaxilla && _step != Step.PickMandible) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits == null || hits.Count == 0) return;

        int idx = GetNextIndex(_boneLandmarks);
        SetBoneLandmark(idx, new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z));
        e.Handled = true;
    }

    private void BoneViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_step != Step.PickMaxilla && _step != Step.PickMandible) return;

        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits == null || hits.Count == 0) return;

        var click = new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z);
        int idx   = FindClosest(_boneLandmarks, click);
        if (idx >= 0) { RemoveBoneMarker(idx); _boneLandmarks[idx] = null; UpdatePairItem(idx); UpdateLandmarkUI(); e.Handled = true; }
    }

    // ── Mouse input — Occlusion viewport ─────────────────────────────────
    private void OccViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_step != Step.PickMaxilla && _step != Step.PickMandible) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var hits = OccViewport.FindHits(e.GetPosition(OccViewport));
        if (hits == null || hits.Count == 0) return;

        int idx = GetNextIndex(_occLandmarks);
        SetOccLandmark(idx, new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z));
        e.Handled = true;
    }

    private void OccViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_step != Step.PickMaxilla && _step != Step.PickMandible) return;

        var hits = OccViewport.FindHits(e.GetPosition(OccViewport));
        if (hits == null || hits.Count == 0) return;

        var click = new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z);
        int idx   = FindClosest(_occLandmarks, click);
        if (idx >= 0) { RemoveOccMarker(idx); _occLandmarks[idx] = null; UpdatePairItem(idx); UpdateLandmarkUI(); e.Handled = true; }
    }

    // ── Landmark management (mirror of DentalAlignmentWindow pattern) ─────
    private static int GetNextIndex(List<(double,double,double)?> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == null) return i;
        return list.Count;
    }

    private void SetBoneLandmark(int idx, Point3D pt)
    {
        EnsureList(_boneLandmarks, _boneMarkerVisuals, idx);
        RemoveBoneMarker(idx);
        _boneLandmarks[idx] = (pt.X, pt.Y, pt.Z);

        var (sphere, label) = CreateMarker(pt, System.Windows.Media.Colors.LimeGreen, idx + 1);
        BoneGroup.Children.Add(sphere);
        BoneGroup.Children.Add(label);
        _boneMarkerVisuals[idx * 2]     = sphere;
        _boneMarkerVisuals[idx * 2 + 1] = label;

        EnsurePairItem(idx); UpdatePairItem(idx); UpdateLandmarkUI();
    }

    private void SetOccLandmark(int idx, Point3D pt)
    {
        EnsureList(_occLandmarks, _occMarkerVisuals, idx);
        RemoveOccMarker(idx);
        _occLandmarks[idx] = (pt.X, pt.Y, pt.Z);

        var (sphere, label) = CreateMarker(pt, System.Windows.Media.Colors.OrangeRed, idx + 1);
        OccGroup.Children.Add(sphere);
        OccGroup.Children.Add(label);
        _occMarkerVisuals[idx * 2]     = sphere;
        _occMarkerVisuals[idx * 2 + 1] = label;

        EnsurePairItem(idx); UpdatePairItem(idx); UpdateLandmarkUI();
    }

    private static void EnsureList(
        List<(double,double,double)?> landmarks,
        List<Element3D> visuals,
        int idx)
    {
        while (landmarks.Count <= idx)           landmarks.Add(null);
        while (visuals.Count <= idx * 2 + 1) { visuals.Add(null!); visuals.Add(null!); }
    }

    private void RemoveBoneMarker(int idx)
    {
        if (idx * 2 + 1 < _boneMarkerVisuals.Count)
        {
            if (_boneMarkerVisuals[idx*2]   != null) BoneGroup.Children.Remove(_boneMarkerVisuals[idx*2]);
            if (_boneMarkerVisuals[idx*2+1] != null) BoneGroup.Children.Remove(_boneMarkerVisuals[idx*2+1]);
        }
    }

    private void RemoveOccMarker(int idx)
    {
        if (idx * 2 + 1 < _occMarkerVisuals.Count)
        {
            if (_occMarkerVisuals[idx*2]   != null) OccGroup.Children.Remove(_occMarkerVisuals[idx*2]);
            if (_occMarkerVisuals[idx*2+1] != null) OccGroup.Children.Remove(_occMarkerVisuals[idx*2+1]);
        }
    }

    // ── Marker visual factory (identical radius/colour to DentalAlignmentWindow) ──
    private static (MeshGeometryModel3D sphere, BillboardTextModel3D label)
        CreateMarker(Point3D pos, System.Windows.Media.Color col, int number)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), 1.5f);
        var c4 = new HelixToolkit.Maths.Color4(col.R/255f, col.G/255f, col.B/255f, col.A/255f);

        var sphere = new MeshGeometryModel3D
        {
            Geometry  = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material  = new PhongMaterial
            {
                DiffuseColor      = c4,
                SpecularColor     = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f),
                SpecularShininess = 32f
            },
            Transform = new TranslateTransform3D(pos.X, pos.Y, pos.Z)
        };

        var text3D = new HelixToolkit.SharpDX.BillboardText3D();
        text3D.TextInfo.Add(new HelixToolkit.SharpDX.TextInfo(
            number.ToString(),
            new System.Numerics.Vector3((float)pos.X, (float)pos.Y, (float)pos.Z + 3f)));
        var label = new BillboardTextModel3D { Geometry = text3D };

        return (sphere, label);
    }

    // ── Pairs list ────────────────────────────────────────────────────────
    private void EnsurePairItem(int idx)
    {
        while (_pairs.Count <= idx)
            _pairs.Add(new OcclusionPairItem { Index = _pairs.Count });
    }

    private void UpdatePairItem(int idx)
    {
        if (idx >= _pairs.Count) return;
        var pair = _pairs[idx];
        pair.Index = idx;
        var b = idx < _boneLandmarks.Count ? _boneLandmarks[idx] : null;
        var o = idx < _occLandmarks.Count  ? _occLandmarks[idx]  : null;
        pair.LeftText  = b.HasValue ? $"Bone ({b.Value.X:F1}, {b.Value.Y:F1}, {b.Value.Z:F1})" : "—";
        pair.RightText = o.HasValue ? $"Occ  ({o.Value.X:F1}, {o.Value.Y:F1}, {o.Value.Z:F1})" : "—";
        pair.Refresh();
    }

    private void UpdateLandmarkUI()
    {
        int bCount = _boneLandmarks.Count(l => l.HasValue);
        int oCount = _occLandmarks.Count(l  => l.HasValue);
        int pairs  = 0;
        int maxIdx = Math.Max(_boneLandmarks.Count, _occLandmarks.Count);
        for (int i = 0; i < maxIdx; i++)
            if (i < _boneLandmarks.Count && _boneLandmarks[i].HasValue &&
                i < _occLandmarks.Count  && _occLandmarks[i].HasValue)
                pairs++;

        LandmarkCountText.Text = $"Bone: {bCount} | Occ: {oCount} | Complete pairs: {pairs}";
        ComputeBtn.IsEnabled   = pairs >= 3 &&
                                 (_step == Step.PickMaxilla || _step == Step.PickMandible);
    }

    // ── Spread check (identical to DentalAlignmentWindow) ─────────────────
    private static double GetMaxDist(List<(double X, double Y, double Z)> pts)
    {
        double maxSq = 0;
        for (int i = 0; i < pts.Count; i++)
            for (int j = i + 1; j < pts.Count; j++)
            {
                double dx = pts[i].X - pts[j].X, dy = pts[i].Y - pts[j].Y, dz = pts[i].Z - pts[j].Z;
                double sq = dx*dx + dy*dy + dz*dz;
                if (sq > maxSq) maxSq = sq;
            }
        return Math.Sqrt(maxSq);
    }

    // ── Clear ─────────────────────────────────────────────────────────────
    private void ClearLandmarks_Click(object sender, RoutedEventArgs e) => ClearMarkersAndReload();

    private void ClearMarkersAndReload()
    {
        ClearMarkersInternal();
        _pairs.Clear();
        RmsText.Text = "";
        LoadStep();
    }

    private void ClearMarkersInternal()
    {
        foreach (var v in _boneMarkerVisuals) if (v != null) BoneGroup.Children.Remove(v);
        foreach (var v in _occMarkerVisuals)  if (v != null) OccGroup.Children.Remove(v);
        _boneMarkerVisuals.Clear();
        _occMarkerVisuals.Clear();
        _boneLandmarks.Clear();
        _occLandmarks.Clear();
    }

    // ── Closest landmark finder (same radius logic as DentalAlignmentWindow) ─
    private static int FindClosest(List<(double X, double Y, double Z)?> list, Point3D pt, double maxR = 5.0)
    {
        int    best   = -1;
        double bestSq = maxR * maxR;
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].HasValue) continue;
            var l = list[i]!.Value;
            double sq = (l.X-pt.X)*(l.X-pt.X) + (l.Y-pt.Y)*(l.Y-pt.Y) + (l.Z-pt.Z)*(l.Z-pt.Z);
            if (sq < bestSq) { bestSq = sq; best = i; }
        }
        return best;
    }

    // ── Compute ICP ───────────────────────────────────────────────────────
    private async void Compute_Click(object sender, RoutedEventArgs e)
    {
        // ── Collect landmark pairs with correct direction per step ────────
        // Step 1 (Maxilla): occlusion (source) moves TO maxilla (target)
        // Step 2 (Mandible): mandible (source) moves TO occlusion (target)
        var srcPts = new List<(double, double, double)>();
        var tgtPts = new List<(double, double, double)>();
        int maxIdx = Math.Max(_boneLandmarks.Count, _occLandmarks.Count);
        for (int i = 0; i < maxIdx; i++)
            if (i < _boneLandmarks.Count && _boneLandmarks[i].HasValue &&
                i < _occLandmarks.Count  && _occLandmarks[i].HasValue)
            {
                if (_step == Step.PickMaxilla)
                {
                    srcPts.Add(_occLandmarks[i]!.Value);   // source = occlusion
                    tgtPts.Add(_boneLandmarks[i]!.Value);  // target = maxilla
                }
                else
                {
                    srcPts.Add(_boneLandmarks[i]!.Value);  // source = mandible
                    tgtPts.Add(_occLandmarks[i]!.Value);   // target = occlusion (already maxilla-aligned)
                }
            }

        if (srcPts.Count < 3) return;

        if (GetMaxDist(srcPts) < 15.0 || GetMaxDist(tgtPts) < 15.0)
        {
            MessageBox.Show(
                "Landmarks too clustered. Spread them across the arch (e.g. left molar, right molar, incisors).",
                "Unstable Landmarks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ComputeBtn.IsEnabled  = false;
        bool skipIcp = SkipIcpCheck.IsChecked == true;
        StepInstructions.Text = skipIcp ? "Applying landmark registration only…"
                                        : "Running landmark registration + ICP refinement…";
        try
        {
            var initial = IcpAligner.ComputeLandmarkTransform(srcPts, tgtPts);

            if (_step == Step.PickMaxilla)
            {
                // ── Step 1: move OCCLUSION to MAXILLA ─────────────────────
                double[,] finalTx;
                string rmsLabel;
                if (skipIcp)
                {
                    finalTx  = initial;
                    rmsLabel = "Maxilla: landmark-only";
                }
                else
                {
                    var res = await Task.Run(() =>
                        IcpAligner.AlignRobust(
                            _occVerts, _maxVerts, initial,
                            maxIterations: 500,
                            tolerance: 0,
                            targetCullRatio: 0.40,
                            sourceCullRatio: 0.50,
                            sigmaEnd: 1.0,
                            progress: p => Dispatcher.Invoke(() =>
                                StepInstructions.Text = $"ICP… {p*100:F0}%")));
                    finalTx  = res.Transform;
                    rmsLabel = $"Maxilla RMS: {res.RmsError:F3} mm | {res.Iterations} iters";
                    _maxIcpTransform = res.Transform;
                }
                IcpAligner.TransformVertices(_occVerts, finalTx);
                _maxIcpTransform      = finalTx;
                FinalOcclusionTransform = ToMatrix3D(finalTx);
                RmsText.Text          = rmsLabel;
                _step                 = Step.ReviewMaxilla;
            }
            else
            {
                // ── Step 2: move MANDIBLE to OCCLUSION (already maxilla-aligned) ──
                // Restore occVerts to pristine, then re-apply maxilla transform so the
                // occlusion is in its maxilla-aligned state for the mandible ICP.
                RestoreOccVerts();
                if (_maxIcpTransform != null)
                    IcpAligner.TransformVertices(_occVerts, _maxIcpTransform);
                RestoreManVerts();

                // Compute initial rigid transform from landmarks (Horn's quaternion method)
                initial = IcpAligner.ComputeLandmarkTransform(srcPts, tgtPts);

                double[,] finalTx;
                string rmsLabel;
                if (skipIcp)
                {
                    finalTx  = initial;
                    rmsLabel = " | Mandible: landmark-only";
                }
                else
                {
                    var manSnap = _manVerts.Select(v => new float[]{v[0],v[1],v[2]}).ToList();
                    var occSnap = _occVerts.Select(v => new float[]{v[0],v[1],v[2]}).ToList();
                    var res = await Task.Run(() =>
                        IcpAligner.AlignRobust(
                            manSnap, occSnap, initial,
                            maxIterations: 500,
                            tolerance: 0,
                            targetCullRatio: 0.50,
                            sourceCullRatio: 0.20,
                            progress: p => Dispatcher.Invoke(() =>
                                StepInstructions.Text = $"ICP… {p*100:F0}%")));
                    finalTx  = res.Transform;
                    rmsLabel = $" | Mandible RMS: {res.RmsError:F3} mm | {res.Iterations} iters";
                }
                IcpAligner.TransformVertices(_manVerts, finalTx);
                _manIcpTransform  = finalTx;
                MandibleTransform = ToMatrix3D(finalTx);
                RmsText.Text     += rmsLabel;
                _step             = Step.ReviewMandible;
            }



            LoadStep();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Alignment failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StepInstructions.Text = "Alignment failed. Fix landmarks and retry.";
        }
        finally
        {
            ComputeBtn.IsEnabled = _step == Step.PickMaxilla || _step == Step.PickMandible;
        }
    }

    // ── "Next Step" — move from ReviewMaxilla to PickMandible ─────────────
    private void NextStep_Click(object sender, RoutedEventArgs e)
    {
        _step = Step.PickMandible;
        _pairs.Clear();
        RmsText.Text = _maxIcpTransform != null
            ? $"Maxilla aligned ✓"
            : "";
        LoadStep();
    }

    // ── Accept ────────────────────────────────────────────────────────────
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        // MaxillaTransform = identity (occlusion was pulled towards maxilla;
        // the main viewmodel will set occlusion.MaxillaOcclusionTransform = Identity
        // and occlusion.MandibleOcclusionTransform = MandibleTransform)
        MaxillaTransform = Matrix3D.Identity;

        Accepted     = true;
        DialogResult = true;
        Close();
    }

    // ── Cancel ────────────────────────────────────────────────────────────
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // If we are in a review step, go back to picking
        if (_step == Step.ReviewMaxilla)
        {
            // Reset occVerts to original, discard Maxilla ICP
            RestoreOccVerts();
            _maxIcpTransform      = null;
            FinalOcclusionTransform = Matrix3D.Identity;
            _step = Step.PickMaxilla;
            _pairs.Clear();
            RmsText.Text = "";
            LoadStep();
            return;
        }
        if (_step == Step.ReviewMandible)
        {
            // Revert mandible to original, discard Mandible ICP
            RestoreManVerts();
            _manIcpTransform  = null;
            MandibleTransform = Matrix3D.Identity;
            _step = Step.PickMandible;
            _pairs.Clear();
            LoadStep();
            return;
        }

        Accepted     = false;
        DialogResult = false;
        Close();
    }

    // ── Vertex restoration helpers ────────────────────────────────────────
    private void RestoreOccVerts()
    {
        for (int i = 0; i < _occVerts.Count; i++)
        {
            _occVerts[i][0] = _occOriginal[i][0];
            _occVerts[i][1] = _occOriginal[i][1];
            _occVerts[i][2] = _occOriginal[i][2];
        }
    }

    private void RestoreOccToPostMaxilla()
    {
        if (_maxIcpTransform == null) { RestoreOccVerts(); return; }
        RestoreOccVerts();
        IcpAligner.TransformVertices(_occVerts, _maxIcpTransform);
    }

    private void RestoreManVerts()
    {
        for (int i = 0; i < _manVerts.Count; i++)
        {
            _manVerts[i][0] = _manOriginal[i][0];
            _manVerts[i][1] = _manOriginal[i][1];
            _manVerts[i][2] = _manOriginal[i][2];
        }
    }

    // ── 4×4 → Matrix3D converter ──────────────────────────────────────────
    private static Matrix3D ToMatrix3D(double[,] m) =>
        new(m[0,0], m[1,0], m[2,0], m[3,0],
            m[0,1], m[1,1], m[2,1], m[3,1],
            m[0,2], m[1,2], m[2,2], m[3,2],
            m[0,3], m[1,3], m[2,3], m[3,3]);
}
