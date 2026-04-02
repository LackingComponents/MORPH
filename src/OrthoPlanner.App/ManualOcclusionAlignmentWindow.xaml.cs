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

public partial class ManualOcclusionAlignmentWindow : Window
{
    private readonly SegmentViewModel _maxilla;
    private readonly SegmentViewModel _mandible;
    private readonly MeshViewModel _occlusion;
    
    private List<float[]> _maxVerts;
    private List<float[]> _manVerts;
    private List<float[]> _occVerts; // We will manipulate this locally in steps
    
    public Matrix3D MaxillaTransform { get; private set; } = Matrix3D.Identity;
    public Matrix3D MandibleTransform { get; private set; } = Matrix3D.Identity;
    public Matrix3D FinalOcclusionTransform { get; private set; } = Matrix3D.Identity;
    public bool Accepted { get; private set; }

    private enum WorkflowStep { AlignMaxilla, AlignMandible, Done }
    private WorkflowStep _currentStep = WorkflowStep.AlignMaxilla;

    private List<(double X, double Y, double Z)?> _boneLandmarks = new();
    private List<(double X, double Y, double Z)?> _occLandmarks = new();
    private List<Element3D> _boneMarkerVisuals = new();
    private List<Element3D> _occMarkerVisuals = new();

    private EventHandler? _renderingHandler;

    public ManualOcclusionAlignmentWindow(SegmentViewModel maxilla, SegmentViewModel mandible, MeshViewModel occlusion)
    {
        InitializeComponent();

        _maxilla = maxilla;
        _mandible = mandible;
        _occlusion = occlusion;

        _maxVerts = _maxilla.Vertices != null ? MeshHelper.ToVertexList(_maxilla.Vertices) : new List<float[]>();
        _manVerts = _mandible.Vertices != null ? MeshHelper.ToVertexList(_mandible.Vertices) : new List<float[]>();
        _occVerts = _occlusion.Vertices != null ? MeshHelper.ToVertexList(_occlusion.Vertices) : new List<float[]>();

        BoneViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        OccViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _renderingHandler = (s, e) =>
        {
            UpdateLighting(BoneCamera, BoneHeadlamp, BoneBacklamp);
            UpdateLighting(OccCamera, OccHeadlamp, OccBacklamp);
        };
        System.Windows.Media.CompositionTarget.Rendering += _renderingHandler;

        Loaded += (_, _) => LoadStepView();
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

    private void LoadStepView()
    {
        BoneGroup.Children.Clear();
        OccGroup.Children.Clear();
        ClearMarkers();

        if (_currentStep == WorkflowStep.AlignMaxilla)
        {
            StepTitle.Text = "Step 1: Align Maxilla";
            StepInstructions.Text = "Place at least 3 point-pairs on the Maxilla and the UPPER teeth of the Occlusion model to establish the bite alignment.";
            BoneLabel.Text = "🦴 Maxilla Surface";
            ComputeBtn.Content = "Compute Maxilla ICP";

            if (_maxVerts.Count > 0)
            {
                BoneGroup.Children.Add(MeshHelper.BuildModel3D(_maxVerts, (byte)_maxilla.ColorR, (byte)_maxilla.ColorG, (byte)_maxilla.ColorB, 255));
                CenterViewportOnMesh(BoneViewport, _maxVerts, 1.2);
            }
        }
        else if (_currentStep == WorkflowStep.AlignMandible)
        {
            StepTitle.Text = "Step 2: Align Mandible";
            StepInstructions.Text = "Place at least 3 point-pairs on the Mandible and the LOWER teeth of the aligned Occlusion model to bring the jaw into the bite.";
            BoneLabel.Text = "🦴 Mandible Surface";
            ComputeBtn.Content = "Compute Mandible ICP";

            if (_manVerts.Count > 0)
            {
                BoneGroup.Children.Add(MeshHelper.BuildModel3D(_manVerts, (byte)_mandible.ColorR, (byte)_mandible.ColorG, (byte)_mandible.ColorB, 255));
                CenterViewportOnMesh(BoneViewport, _manVerts, 1.2);
            }
        }
        else if (_currentStep == WorkflowStep.Done)
        {
            StepTitle.Text = "Alignment Complete";
            StepInstructions.Text = "Both jaws have been structurally aligned via ICP mapping. Click Accept to return.";
            BoneLabel.Text = "🦴 Final Jaw Assembly";
            ComputeBtn.Content = "Accept & Finish";
            ComputeBtn.IsEnabled = true;

            if (_maxVerts.Count > 0) BoneGroup.Children.Add(MeshHelper.BuildModel3D(_maxVerts, (byte)_maxilla.ColorR, (byte)_maxilla.ColorG, (byte)_maxilla.ColorB, 255));
            if (_manVerts.Count > 0) BoneGroup.Children.Add(MeshHelper.BuildModel3D(_manVerts, (byte)_mandible.ColorR, (byte)_mandible.ColorG, (byte)_mandible.ColorB, 255));
            CenterViewportOnMesh(BoneViewport, _maxVerts, 1.5);
        }

        if (_occVerts.Count > 0)
        {
            // The occlusion geometry updates natively throughout the steps
            OccGroup.Children.Add(MeshHelper.BuildModel3D(_occVerts, 245, 245, 230, 255));
            CenterViewportOnMesh(OccViewport, _occVerts, 1.5);
        }
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
        if (Keyboard.Modifiers != ModifierKeys.None || _currentStep == WorkflowStep.Done) return;
        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits?.Count > 0) { SetLandmark(_boneLandmarks, _boneMarkerVisuals, BoneGroup, hits[0].PointHit, System.Windows.Media.Colors.LimeGreen); e.Handled = true; }
    }
    private void OccViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || _currentStep == WorkflowStep.Done) return;
        var hits = OccViewport.FindHits(e.GetPosition(OccViewport));
        if (hits?.Count > 0) { SetLandmark(_occLandmarks, _occMarkerVisuals, OccGroup, hits[0].PointHit, System.Windows.Media.Colors.OrangeRed); e.Handled = true; }
    }
    private void BoneViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentStep == WorkflowStep.Done) return;
        var hits = BoneViewport.FindHits(e.GetPosition(BoneViewport));
        if (hits?.Count > 0) { RemoveClosestLandmark(_boneLandmarks, _boneMarkerVisuals, BoneGroup, hits[0].PointHit); e.Handled = true; }
    }
    private void OccViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentStep == WorkflowStep.Done) return;
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
        if (_currentStep == WorkflowStep.Done) return;
        int p = Math.Min(_boneLandmarks.Count(l => l.HasValue), _occLandmarks.Count(l => l.HasValue));
        StatusText.Text = $"Selected {p} pairs. Need at least 3 to compute.";
        ComputeBtn.IsEnabled = p >= 3;
    }

    private async void Compute_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == WorkflowStep.Done)
        {
            Accepted = true;
            DialogResult = true;
            Close();
            return;
        }

        var src = new List<(double,double,double)>(); 
        var tgt = new List<(double,double,double)>();
        
        for (int i = 0; i < Math.Min(_boneLandmarks.Count, _occLandmarks.Count); i++)
        {
            if (_boneLandmarks[i].HasValue && _occLandmarks[i].HasValue)
            {
                // In Step 1: Target=Maxilla, Source=Occlusion
                // In Step 2: Target=Occlusion, Source=Mandible
                if (_currentStep == WorkflowStep.AlignMaxilla)
                {
                    src.Add(_occLandmarks[i]!.Value);
                    tgt.Add(_boneLandmarks[i]!.Value);
                }
                else
                {
                    src.Add(_boneLandmarks[i]!.Value);
                    tgt.Add(_occLandmarks[i]!.Value);
                }
            }
        }
        
        if (src.Count < 3) return;

        ComputeBtn.IsEnabled = false;
        StatusText.Text = "Running ICP...";

        var initialForm = IcpAligner.ComputeLandmarkTransform(src, tgt);
        
        try
        {
            if (_currentStep == WorkflowStep.AlignMaxilla)
            {
                // Align Occlusion matching Maxilla
                var result = await Task.Run(() => IcpAligner.Align(_occVerts, _maxVerts, initialForm, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.70));
                FinalOcclusionTransform = new Matrix3D(
                    result.Transform[0,0], result.Transform[1,0], result.Transform[2,0], result.Transform[3,0],
                    result.Transform[0,1], result.Transform[1,1], result.Transform[2,1], result.Transform[3,1],
                    result.Transform[0,2], result.Transform[1,2], result.Transform[2,2], result.Transform[3,2],
                    result.Transform[0,3], result.Transform[1,3], result.Transform[2,3], result.Transform[3,3]);

                // Update vertices strictly inline
                IcpAligner.TransformVertices(_occVerts, result.Transform);
                RmsText.Text = $"Maxilla-to-Occ RMS: {result.RmsError:F3}";

                // Advance stage
                _currentStep = WorkflowStep.AlignMandible;
                LoadStepView();
            }
            else if (_currentStep == WorkflowStep.AlignMandible)
            {
                // Align Mandible matching to the now locked Occlusion
                var result = await Task.Run(() => IcpAligner.Align(_manVerts, _occVerts, initialForm, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.70));
                
                MandibleTransform = new Matrix3D(
                    result.Transform[0,0], result.Transform[1,0], result.Transform[2,0], result.Transform[3,0],
                    result.Transform[0,1], result.Transform[1,1], result.Transform[2,1], result.Transform[3,1],
                    result.Transform[0,2], result.Transform[1,2], result.Transform[2,2], result.Transform[3,2],
                    result.Transform[0,3], result.Transform[1,3], result.Transform[2,3], result.Transform[3,3]);

                RmsText.Text += $" | Mandible-to-Occ RMS: {result.RmsError:F3}";
                
                // Maxilla transform handles identically to auto-sequence
                MaxillaTransform = Matrix3D.Identity;

                // Move Mandible inline just for the final visual
                IcpAligner.TransformVertices(_manVerts, result.Transform);

                _currentStep = WorkflowStep.Done;
                LoadStepView();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ICP Error: {ex.Message}");
            UpdateStatus();
        }
    }
}
