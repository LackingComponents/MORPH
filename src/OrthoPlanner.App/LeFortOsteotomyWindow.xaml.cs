using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
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
    private ModelVisual3D _modelVisual = new();
    private Model3DGroup _mainGroup = new();
    private GeometryModel3D _boneMesh;
    private GeometryModel3D _polyplaneMesh;
    
    // Control points
    private List<Point3D> _controlPoints = new();
    private List<SphereVisual3D> _pointVisuals = new();
    
    // Interaction state
    private SphereVisual3D? _draggedPoint;
    private int _draggedIndex = -1;
    private Plane3D? _dragPlane; // Plane for moving points in 3D

    public LeFortOsteotomyWindow(List<float[]> craniumVerts)
    {
        InitializeComponent();
        _craniumVerts = craniumVerts;

        MainViewport.Children.Add(_modelVisual);
        _modelVisual.Content = _mainGroup;

        // Background lights
        _mainGroup.Children.Add(new AmbientLight(Color.FromRgb(100, 100, 100)));

        _boneMesh = CreateMeshVisual(_craniumVerts, Color.FromRgb(245, 245, 230), 1.0);
        _mainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 200, 100), 1.0);
        _polyplaneMesh.BackMaterial = _polyplaneMesh.Material; // Double-sided
        _mainGroup.Children.Add(_polyplaneMesh);
    }

    private GeometryModel3D CreateMeshVisual(List<float[]> verts, Color color, double opacity)
    {
        var pos = new Point3DCollection(verts.Count);
        var idx = new Int32Collection(verts.Count);
        for (int i = 0; i < verts.Count; i++)
        {
            pos.Add(new Point3D(verts[i][0], verts[i][1], verts[i][2]));
            idx.Add(i);
        }
        
        var geom = new MeshGeometry3D { Positions = pos, TriangleIndices = idx };
        geom.Normals = new Vector3DCollection(); // Auto-compute is fine if normals are missing, but let's just leave empty.
        
        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)));
        
        return new GeometryModel3D { Geometry = geom, Material = mat };
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
            if (ptInfo.Value.Visual == vis || DistanceTo(ptInfo.Value.Point, vis.Center) < 2.0)
            {
                _draggedPoint = vis;
                _draggedIndex = i;
                
                // Establish a drag plane facing the camera, passing through the sphere
                var lookDir = MainViewport.Camera.LookDirection;
                _dragPlane = new Plane3D(vis.Center, new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                
                MainViewport.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // 2. Otherwise add a new point
        if (ptInfo.Value.Visual == _modelVisual)
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
            var ray = Viewport3DHelper.Point2DtoRay3D(MainViewport.Viewport, pos);
            if (ray != null)
            {
                var intersect = RayPlaneIntersection(ray, _dragPlane);
                if (intersect.HasValue)
                {
                    _draggedPoint.Center = intersect.Value;
                    _controlPoints[_draggedIndex] = intersect.Value;
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

    private (Point3D Point, Visual3D? Visual)? GetHitPoint(Point p)
    {
        var hits = Viewport3DHelper.FindHits(MainViewport.Viewport, p);
        foreach (var hit in hits)
        {
            return (hit.Position, hit.Visual);
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

    private Point3D? RayPlaneIntersection(Ray3D ray, Plane3D plane)
    {
        double nd = Vector3D.DotProduct(ray.Direction, plane.Normal);
        if (Math.Abs(nd) < 0.0001) return null;
        double t = Vector3D.DotProduct(plane.Position - ray.Origin, plane.Normal) / nd;
        if (t < 0) return null;
        return ray.Origin + ray.Direction * t;
    }

    private void AddControlPoint(Point3D pt)
    {
        _controlPoints.Add(pt);
        var sphere = new SphereVisual3D { Center = pt, Radius = 2.0, Fill = Brushes.Red };
        _pointVisuals.Add(sphere);
        MainViewport.Children.Add(sphere);
        
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
        
        // Update the visual geometry
        var pos = new Point3DCollection(meshVerts.Count);
        var idx = new Int32Collection(meshVerts.Count);
        for (int i = 0; i < meshVerts.Count; i++)
        {
            pos.Add(new Point3D(meshVerts[i][0], meshVerts[i][1], meshVerts[i][2]));
            idx.Add(i);
        }
        
        ((MeshGeometry3D)_polyplaneMesh.Geometry).Positions = pos;
        ((MeshGeometry3D)_polyplaneMesh.Geometry).TriangleIndices = idx;
    }

    private Polyplane GetCurrentPolyplane()
    {
        var polyplane = new Polyplane();
        polyplane.ControlPoints = _controlPoints.Select(p => (p.X, p.Y, p.Z)).ToList();
        
        // Extrude along Y (anterior-posterior) inside the maxilla
        polyplane.ExtrusionDir = new double[] { 0, 1, 0 };
        // Z is Up
        polyplane.UpVector = new double[] { 0, 0, 1 }; 
        return polyplane;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _controlPoints.Clear();
        foreach (var p in _pointVisuals) MainViewport.Children.Remove(p);
        _pointVisuals.Clear();
        
        ((MeshGeometry3D)_polyplaneMesh.Geometry).Positions.Clear();
        ((MeshGeometry3D)_polyplaneMesh.Geometry).TriangleIndices.Clear();
        
        CutBtn.IsEnabled = false;
        AcceptBtn.Visibility = Visibility.Collapsed;
        
        // Reset bone colors if we cut
        ((DiffuseMaterial)_boneMesh.Material).Brush = new SolidColorBrush(Color.FromRgb(245, 245, 230));
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

            // Optional: Close holes for watertightness
            UpperMaxillaResult = MeshOps.CloseHoles(above);
            LowerMaxillaResult = MeshOps.CloseHoles(below);

            // Hide original bone, show two new pieces in different colors
            _mainGroup.Children.Remove(_boneMesh);
            
            var upperMesh = CreateMeshVisual(UpperMaxillaResult, Color.FromRgb(200, 200, 255), 1.0);
            var lowerMesh = CreateMeshVisual(LowerMaxillaResult, Color.FromRgb(255, 200, 200), 1.0);
            
            _mainGroup.Children.Add(upperMesh);
            _mainGroup.Children.Add(lowerMesh);
            
            // Allow accepting
            AcceptBtn.Visibility = Visibility.Visible;
            CutBtn.IsEnabled = false;
            ClearBtn.Content = "Undo Cut"; // Allows resetting the visuals and trying again
            
            // Hide the handles and polyplane while reviewing the cut
            _mainGroup.Children.Remove(_polyplaneMesh);
            foreach (var p in _pointVisuals) MainViewport.Children.Remove(p);
            
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
