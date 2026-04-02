using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class ManualOcclusionAlignmentWindow : Window
{
    private readonly List<float[]> _maxillaVertices;
    private readonly List<float[]> _mandibleVertices;
    private readonly List<float[]> _occlusionVertices;

    // We keep track of landmarks for whichever bone is currently selected
    private List<(double X, double Y, double Z)?> _boneLandmarks = new();
    private List<(double X, double Y, double Z)?> _occLandmarks = new();
    private List<Element3D> _boneMarkerVisuals = new();
    private List<Element3D> _occMarkerVisuals = new();

    public double[,]? InitialLandmarkTransform { get; private set; }
    public bool IsMaxillaSelected => BoneSelector.SelectedIndex == 0;
    public bool Accepted { get; private set; }
    private EventHandler? _renderingHandler;

    public ManualOcclusionAlignmentWindow(List<float[]> maxillaVertices, List<float[]> mandibleVertices, List<float[]> occlusionVertices)
    {
        InitializeComponent();

        BoneViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        OccViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _maxillaVertices = maxillaVertices ?? new List<float[]>();
        _mandibleVertices = mandibleVertices ?? new List<float[]>();
        _occlusionVertices = occlusionVertices ?? new List<float[]>();

        _renderingHandler = (s, e) =>
        {
            UpdateLighting(BoneCamera, BoneHeadlamp, BoneBacklamp);
            UpdateLighting(OccCamera, OccHeadlamp, OccBacklamp);
        };
        System.Windows.Media.CompositionTarget.Rendering += _renderingHandler;

        Loaded += (_, _) => LoadCurrentBone();
        Closed += OnWindowClosed;
    }

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
        if (OccViewport.EffectsManager is IDisposable d2) d2.Dispose();
        BoneViewport.EffectsManager = null;
        OccViewport.EffectsManager = null;
    }

    private void UpdateLighting(HelixToolkit.Wpf.SharpDX.PerspectiveCamera camera, DirectionalLight3D headlamp, DirectionalLight3D backlamp)
    {
        if (camera != null && headlamp != null && backlamp != null)
        {
            var dir = camera.LookDirection;
            if (dir.Length > 0.001)
            {
                dir.Normalize();
                headlamp.Direction = new Vector3D(-dir.X, -dir.Y, -dir.Z);
                backlamp.Direction = new Vector3D(dir.X, dir.Y, dir.Z);
            }
        }
    }

    private void LoadCurrentBone()
    {
        BoneGroup.Children.Clear();
        OccGroup.Children.Clear();
        ClearMarkers();

        bool isMaxilla = IsMaxillaSelected;
        var boneVerts = isMaxilla ? _maxillaVertices : _mandibleVertices;
        BoneLabel.Text = isMaxilla ? "🦴 Maxilla Surface" : "🦴 Mandible Surface";

        if (boneVerts.Count > 0)
        {
            BoneGroup.Children.Add(MeshHelper.BuildModel3D(boneVerts, 240, 230, 210));
            CenterViewportOnMesh(BoneViewport, boneVerts, 1.2);
        }

        if (_occlusionVertices.Count > 0)
        {
            OccGroup.Children.Add(MeshHelper.BuildModel3D(_occlusionVertices, 245, 245, 230));
            CenterViewportOnMesh(OccViewport, _occlusionVertices, 1.5);
        }
    }

    private void BoneSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) LoadCurrentBone();
    }

    private void CenterViewportOnMesh(HelixToolkit.Wpf.SharpDX.Viewport3DX viewport, List<float[]> vertices, double zoomMultiplier)
    {
        if (vertices == null || vertices.Count == 0 || viewport.Camera == null) return;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in vertices)
        {
            if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
            if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
            if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
        }

        var pivot = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        double distance = Math.Max(Math.Sqrt((maxX - minX)*(maxX - minX) + (maxY - minY)*(maxY - minY) + (maxZ - minZ)*(maxZ - minZ)) * zoomMultiplier, 10);
        
        if (viewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        var dir = new Vector3D(0, 1, -0.3); dir.Normalize();
        cam.Position = new Point3D(pivot.X - dir.X * distance, pivot.Y - dir.Y * distance, pivot.Z - dir.Z * distance);
        cam.LookDirection = dir * distance;
    }

    private void BoneViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits?.Count > 0) { SetLandmark(_boneLandmarks, _boneMarkerVisuals, BoneGroup, hits[0].PointHit, System.Windows.Media.Colors.LimeGreen); e.Handled = true; }
    }
    private void OccViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var hits = OccViewport.FindHits(e.GetPosition(OccViewport));
        if (hits?.Count > 0) { SetLandmark(_occLandmarks, _occMarkerVisuals, OccGroup, hits[0].PointHit, System.Windows.Media.Colors.OrangeRed); e.Handled = true; }
    }
    private void BoneViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits?.Count > 0) { RemoveClosestLandmark(_boneLandmarks, _boneMarkerVisuals, BoneGroup, hits[0].PointHit); e.Handled = true; }
    }
    private void OccViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hits = OccViewport.FindHits(e.GetPosition(OccViewport));
        if (hits?.Count > 0) { RemoveClosestLandmark(_occLandmarks, _occMarkerVisuals, OccGroup, hits[0].PointHit); e.Handled = true; }
    }

    private void SetLandmark(List<(double,double,double)?> list, List<Element3D> visuals, GroupModel3D group, System.Numerics.Vector3 pt, System.Windows.Media.Color c)
    {
        int idx = list.FindIndex(l => l == null);
        if (idx == -1) { idx = list.Count; list.Add(null); visuals.Add(null!); visuals.Add(null!); }
        list[idx] = (pt.X, pt.Y, pt.Z);
        
        if (visuals[idx*2] != null) { group.Children.Remove(visuals[idx*2]); group.Children.Remove(visuals[idx*2+1]); }

        var builder = new HelixToolkit.Geometry.MeshBuilder(); builder.AddSphere(new System.Numerics.Vector3(0,0,0), 1.5f);
        var sphere = new MeshGeometryModel3D { Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()), Material = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(c.R/255f, c.G/255f, c.B/255f, 1f) }, Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z) };
        var text = new BillboardTextModel3D { Geometry = new HelixToolkit.SharpDX.BillboardText3D() };
        ((HelixToolkit.SharpDX.BillboardText3D)text.Geometry).TextInfo.Add(new HelixToolkit.SharpDX.TextInfo((idx+1).ToString(), new System.Numerics.Vector3(pt.X, pt.Y, pt.Z + 3f)));
        
        group.Children.Add(sphere); group.Children.Add(text);
        visuals[idx*2] = sphere; visuals[idx*2+1] = text;
        UpdateStatus();
    }

    private void RemoveClosestLandmark(List<(double X, double Y, double Z)?> list, List<Element3D> visuals, GroupModel3D group, System.Numerics.Vector3 pt)
    {
        int bestIdx = -1; double bestDist = 25;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null) continue;
            var l = list[i]!.Value;
            double d = (l.X-pt.X)*(l.X-pt.X) + (l.Y-pt.Y)*(l.Y-pt.Y) + (l.Z-pt.Z)*(l.Z-pt.Z);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }
        if (bestIdx >= 0)
        {
            list[bestIdx] = null;
            if (visuals[bestIdx*2] != null) group.Children.Remove(visuals[bestIdx*2]);
            if (visuals[bestIdx*2+1] != null) group.Children.Remove(visuals[bestIdx*2+1]);
            visuals[bestIdx*2] = null!; visuals[bestIdx*2+1] = null!;
            UpdateStatus();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ClearMarkers();
    private void ClearMarkers()
    {
        foreach(var v in _boneMarkerVisuals.Where(v => v != null)) BoneGroup.Children.Remove(v);
        foreach(var v in _occMarkerVisuals.Where(v => v != null)) OccGroup.Children.Remove(v);
        _boneLandmarks.Clear(); _occLandmarks.Clear();
        _boneMarkerVisuals.Clear(); _occMarkerVisuals.Clear();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int p = Math.Min(_boneLandmarks.Count(l => l.HasValue), _occLandmarks.Count(l => l.HasValue));
        StatusText.Text = $"Selected {p} pairs. Need at least 3 to compute.";
        ComputeBtn.IsEnabled = p >= 3;
    }

    private void Compute_Click(object sender, RoutedEventArgs e)
    {
        var src = new List<(double,double,double)>(); 
        var tgt = new List<(double,double,double)>();
        
        for (int i = 0; i < Math.Min(_boneLandmarks.Count, _occLandmarks.Count); i++)
        {
            if (_boneLandmarks[i].HasValue && _occLandmarks[i].HasValue)
            {
                // We want to return the transform that moves OCCLUSION to BONE
                src.Add(_occLandmarks[i]!.Value);
                tgt.Add(_boneLandmarks[i]!.Value);
            }
        }
        
        if (src.Count >= 3)
        {
            InitialLandmarkTransform = IcpAligner.ComputeLandmarkTransform(src, tgt);
            Accepted = true;
            DialogResult = true;
            Close();
        }
    }
}
