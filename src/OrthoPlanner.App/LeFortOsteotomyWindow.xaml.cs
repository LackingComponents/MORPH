using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;

namespace OrthoPlanner.App;

public partial class LeFortOsteotomyWindow : Window
{
    private List<float[]> _craniumVerts;
    
    // Results
    public List<float[]> UpperMaxillaResult { get; private set; } = new();
    public List<float[]> LowerMaxillaResult { get; private set; } = new();
    public bool Accepted { get; private set; } = false;

    // Viewport elements
    private MeshGeometryModel3D _boneMesh;
    private MeshGeometryModel3D _polyplaneMesh;
    
    // Control points
    private List<Point3D> _controlPoints = new();
    private List<MeshGeometryModel3D> _pointVisuals = new();
    
    // Interaction state
    private MeshGeometryModel3D? _draggedPoint;
    private int _draggedIndex = -1;
    private (Point3D Position, Vector3D Normal)? _dragPlane; // Plane for moving points in 3D

    public LeFortOsteotomyWindow(List<float[]> craniumVerts)
    {
        InitializeComponent();
        
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _craniumVerts = craniumVerts;

        _boneMesh = CreateMeshVisual(_craniumVerts, Color.FromRgb(245, 245, 230), 1.0);
        MainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 200, 100), 1.0);
        
        // HelixToolkit.SharpDX does not have BackMaterial, but rather IsDoubleSided flag
        // However, setting it via geometry or group is not standard.
        // What we do is set IsDoubleSided = true on the MeshGeometryModel3D:
        _polyplaneMesh.CullMode = SharpDX.Direct3D11.CullMode.None; // Make it double-sided
        
        MainGroup.Children.Add(_polyplaneMesh);
    }

    private MeshGeometryModel3D CreateMeshVisual(List<float[]> verts, Color color, double opacity)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i < verts.Count; i += 3)
        {
            if (i + 2 < verts.Count)
            {
                builder.AddTriangle(
                    new System.Numerics.Vector3(verts[i][0], verts[i][1], verts[i][2]),
                    new System.Numerics.Vector3(verts[i + 1][0], verts[i + 1][1], verts[i + 1][2]),
                    new System.Numerics.Vector3(verts[i + 2][0], verts[i + 2][1], verts[i + 2][2]));
            }
        }
        
        var geom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(color.R / 255f, color.G / 255f, color.B / 255f, (float)opacity) };
        return new MeshGeometryModel3D { Geometry = geom, Material = mat };
    }

    // ═══════════════════════════════════
    // Core Interaction
    // ═══════════════════════════════════

    private void Viewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Accepted) return;

        var ptInfo = GetHitPoint(e.GetPosition(MainViewport));
        if (ptInfo == null) return;

        // 1. Check if we hit an existing handle (drag)
        for (int i = 0; i < _pointVisuals.Count; i++)
        {
            var vis = _pointVisuals[i];
            var center = _controlPoints[i];
            
            if (ptInfo.Value.Visual == vis || DistanceTo(ptInfo.Value.Point, center) < 2.0)
            {
                _draggedPoint = vis;
                _draggedIndex = i;
                
                var lookDir = MainViewport.Camera.LookDirection;
                _dragPlane = (center, new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                
                MainViewport.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // 2. Otherwise add a new point
        if (ptInfo.Value.Visual == _boneMesh)
        {
            AddControlPoint(ptInfo.Value.Point);
            e.Handled = true;
        }
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPoint != null && _dragPlane != null)
        {
            var pos = e.GetPosition(MainViewport);
            var rayResult = MainViewport.UnProject(pos);
            if (rayResult.Direction.LengthSquared() > 0)
            {
                var ray = rayResult;
                var intersect = RayPlaneIntersection(
                    new Point3D(ray.Position.X, ray.Position.Y, ray.Position.Z), 
                    new Vector3D(ray.Direction.X, ray.Direction.Y, ray.Direction.Z), 
                    _dragPlane.Value.Position, 
                    _dragPlane.Value.Normal);
                if (intersect.HasValue)
                {
                    _controlPoints[_draggedIndex] = intersect.Value;
                    _draggedPoint.Transform = new TranslateTransform3D(intersect.Value.X, intersect.Value.Y, intersect.Value.Z);
                    UpdatePolyplane();
                }
            }
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedPoint != null)
        {
            _draggedPoint = null;
            _draggedIndex = -1;
            MainViewport.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private (Point3D Point, Element3D? Visual)? GetHitPoint(Point p)
    {
        var hits = MainViewport.FindHits(p);
        var hit = hits.FirstOrDefault(h => h.ModelHit is Element3D);
        if (hit != null && hit.ModelHit is Element3D model)
        {
             return (new Point3D(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z), model);
        }
        return null;
    }

    private double DistanceTo(Point3D a, Point3D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // ═══════════════════════════════════
    // Actions
    // ═══════════════════════════════════

    private Point3D? RayPlaneIntersection(Point3D rayOrigin, Vector3D rayDirection, Point3D planePosition, Vector3D planeNormal)
    {
        double nd = Vector3D.DotProduct(rayDirection, planeNormal);
        if (Math.Abs(nd) < 0.0001) return null;
        double t = Vector3D.DotProduct(planePosition - rayOrigin, planeNormal) / nd;
        if (t < 0) return null;
        return rayOrigin + rayDirection * t;
    }

    private void AddControlPoint(Point3D pt)
    {
        _controlPoints.Add(pt);
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), 2f);
        var sphereGeom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(1f, 0f, 0f, 1f) };

        var sphere = new MeshGeometryModel3D 
        { 
            Geometry = sphereGeom,
            Material = mat,
            Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z)
        };

        _pointVisuals.Add(sphere);
        MainGroup.Children.Add(sphere);
        
        if (_controlPoints.Count >= 2)
        {
            CutBtn.IsEnabled = true;
            UpdatePolyplane();
        }
    }

    private void UpdatePolyplane()
    {
        if (_controlPoints.Count < 2) return;

        var polyplane = GetCurrentPolyplane();
        var meshVerts = polyplane.GenerateMesh(100.0);
        
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i < meshVerts.Count; i += 3)
        {
            if (i + 2 < meshVerts.Count)
            {
                builder.AddTriangle(
                    new System.Numerics.Vector3(meshVerts[i][0], meshVerts[i][1], meshVerts[i][2]),
                    new System.Numerics.Vector3(meshVerts[i + 1][0], meshVerts[i + 1][1], meshVerts[i + 1][2]),
                    new System.Numerics.Vector3(meshVerts[i + 2][0], meshVerts[i + 2][1], meshVerts[i + 2][2]));
            }
        }
        _polyplaneMesh.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
    }

    private Polyplane GetCurrentPolyplane()
    {
        var polyplane = new Polyplane();
        polyplane.ControlPoints = _controlPoints.Select(p => (p.X, p.Y, p.Z)).ToList();
        
        polyplane.ExtrusionDir = new double[] { 0, 1, 0 };
        polyplane.UpVector = new double[] { 0, 0, 1 }; 
        return polyplane;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _controlPoints.Clear();
        foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);
        _pointVisuals.Clear();
        
        _polyplaneMesh.Geometry = null;
        
        CutBtn.IsEnabled = false;
        AcceptBtn.Visibility = Visibility.Collapsed;
        
        if (_boneMesh.Material is PhongMaterial pm)
            pm.DiffuseColor = new HelixToolkit.Maths.Color4(245/255f, 245/255f, 230/255f, 1.0f);
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (_controlPoints.Count < 2) return;

        StatusText.Text = "Performing osteotomy cut... Please wait.";
        Cursor = Cursors.Wait;
        
        try
        {
            var polyplane = GetCurrentPolyplane();
            var (above, below) = MeshOps.SplitByPolyplane(_craniumVerts, polyplane);

            UpperMaxillaResult = MeshOps.CloseHoles(above);
            LowerMaxillaResult = MeshOps.CloseHoles(below);

            MainGroup.Children.Remove(_boneMesh);
            
            var upperMesh = CreateMeshVisual(UpperMaxillaResult, Color.FromRgb(200, 200, 255), 1.0);
            var lowerMesh = CreateMeshVisual(LowerMaxillaResult, Color.FromRgb(255, 200, 200), 1.0);
            
            MainGroup.Children.Add(upperMesh);
            MainGroup.Children.Add(lowerMesh);
            
            AcceptBtn.Visibility = Visibility.Visible;
            CutBtn.IsEnabled = false;
            ClearBtn.Content = "Undo Cut";
            
            MainGroup.Children.Remove(_polyplaneMesh);
            foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);
            
            StatusText.Text = "Osteotomy computed. Review the upper (blue) and lower (red) segments.";
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}



