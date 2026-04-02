using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class OcclusionCheckerWindow : Window
{
    private EventHandler? _renderingHandler;
    public bool Accepted { get; private set; }

    public OcclusionCheckerWindow(List<float[]> maxillaVertices, List<float[]> mandibleVertices, List<float[]> occlusionVertices, double rmsError)
    {
        InitializeComponent();
        
        CheckerViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        RmsText.Text = $"RMS Error: {rmsError:F3}";

        _renderingHandler = (s, e) =>
        {
            if (MainCamera != null && Headlamp != null && Backlamp != null)
            {
                var dir = MainCamera.LookDirection;
                if (dir.Length > 0.001)
                {
                    dir.Normalize();
                    var tdFront = new System.Windows.Media.Media3D.Vector3D(-dir.X, -dir.Y, -dir.Z);
                    var tdBack = new System.Windows.Media.Media3D.Vector3D(dir.X, dir.Y, dir.Z);
                    if (Math.Abs(Headlamp.Direction.X - tdFront.X) > 1e-4 || 
                        Math.Abs(Headlamp.Direction.Y - tdFront.Y) > 1e-4 || 
                        Math.Abs(Headlamp.Direction.Z - tdFront.Z) > 1e-4)
                    {
                        Headlamp.Direction = tdFront;
                        Backlamp.Direction = tdBack;
                    }
                }
            }
        };
        System.Windows.Media.CompositionTarget.Rendering += _renderingHandler;

        Loaded += (_, _) => SetupViewport(maxillaVertices, mandibleVertices, occlusionVertices);
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        ViewGroup.Children.Clear();
        if (CheckerViewport.EffectsManager is IDisposable d) d.Dispose();
        CheckerViewport.EffectsManager = null;
    }

    private void SetupViewport(List<float[]> maxilla, List<float[]> mandible, List<float[]> occlusion)
    {
        ViewGroup.Children.Clear();

        // Dark Blue translucent Maxilla and Mandible (Opacity 160)
        if (maxilla.Count > 0)
            ViewGroup.Children.Add(MeshHelper.BuildModel3D(maxilla, 80, 160, 255, 160));
        
        if (mandible.Count > 0)
            ViewGroup.Children.Add(MeshHelper.BuildModel3D(mandible, 80, 160, 255, 160));

        // Bright Golden solid Occlusion model
        if (occlusion.Count > 0)
            ViewGroup.Children.Add(MeshHelper.BuildModel3D(occlusion, 255, 230, 90));

        CenterCamera(maxilla.Concat(mandible).ToList());
    }

    private void CenterCamera(List<float[]> vertices)
    {
        if (vertices.Count == 0 || MainCamera == null) return;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in vertices)
        {
            if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
            if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
            if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
        }

        var pivot = new System.Windows.Media.Media3D.Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        double diag = Math.Sqrt((maxX - minX)*(maxX-minX) + (maxY-minY)*(maxY-minY) + (maxZ-minZ)*(maxZ-minZ));
        double dist = Math.Max(diag * 1.5, 10);

        var dir = new System.Windows.Media.Media3D.Vector3D(0, 1, -0.3);
        dir.Normalize();

        MainCamera.Position = new System.Windows.Media.Media3D.Point3D(pivot.X - dir.X * dist, pivot.Y - dir.Y * dist, pivot.Z - dir.Z * dist);
        MainCamera.LookDirection = dir * dist;
        MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);

        CheckerViewport.FixedRotationPointEnabled = true;
        CheckerViewport.FixedRotationPoint = pivot;
    }

    private void Accept_Click(object sender, RoutedEventArgs e) { Accepted = true; DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
