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

public partial class BssoOsteotomyWindow : Window
{
    private List<float[]> _mandibleVerts;
    
    // Results
    public List<float[]> ProximalResult { get; private set; } = new();
    public List<float[]> DistalResult { get; private set; } = new();
    public bool Accepted { get; private set; } = false;

    // Viewport elements
    private ModelVisual3D _modelVisual = new();
    private Model3DGroup _mainGroup = new();
    private GeometryModel3D _boneMesh;
    private GeometryModel3D _polyplaneMesh;
    
    // Control points
    private List<Point3D> _lingualPoints = new();
    private List<Point3D> _buccalPoints = new();
    private List<Point3D> _lowerBorderPoints = new();
    
    private List<SphereVisual3D> _pointVisuals = new();
    
    // Interaction state
    private int _step = 1; // 1: Lingual, 2: Buccal, 3: Adjust
    private SphereVisual3D? _draggedPoint;
    private int _draggedIndex = -1;
    private int _draggedList = 0; // 0=Lingual, 1=Buccal, 2=LowerBorder
    private Plane3D? _dragPlane;

    public BssoOsteotomyWindow(List<float[]> mandibleVerts)
    {
        InitializeComponent();
        _mandibleVerts = mandibleVerts;

        MainViewport.Children.Add(_modelVisual);
        _modelVisual.Content = _mainGroup;
        _mainGroup.Children.Add(new AmbientLight(Color.FromRgb(100, 100, 100)));

        _boneMesh = CreateMeshVisual(_mandibleVerts, Color.FromRgb(245, 230, 200), 1.0);
        _mainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 100, 200), 1.0);
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
        geom.Normals = new Vector3DCollection();
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

        // 1. Check if we hit an existing handle (drag) - only allowed in step 3
        if (_step == 3)
        {
            for (int i = 0; i < _pointVisuals.Count; i++)
            {
                var vis = _pointVisuals[i];
                if (ptInfo.Value.Visual == vis || DistanceTo(ptInfo.Value.Point, vis.Center) < 2.0)
                {
                    _draggedPoint = vis;
                    
                    // Determine which list this belongs to
                    if (i < 2) { _draggedList = 0; _draggedIndex = i; }
                    else if (i < 4) { _draggedList = 1; _draggedIndex = i - 2; }
                    else { _draggedList = 2; _draggedIndex = i - 4; }
                    
                    var lookDir = MainViewport.Camera.LookDirection;
                    _dragPlane = new Plane3D(vis.Center, new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                    
                    MainViewport.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        // 2. Add points in Steps 1 and 2
        if (ptInfo.Value.Visual == _modelVisual)
        {
            if (_step == 1 && _lingualPoints.Count < 2)
            {
                AddPoint(_lingualPoints, ptInfo.Value.Point, Brushes.Green);
                if (_lingualPoints.Count == 2)
                {
                    NextBtn.IsEnabled = true;
                    StatusText.Text = "2 Lingual points placed. Click Next Step.";
                }
            }
            else if (_step == 2 && _buccalPoints.Count < 2)
            {
                AddPoint(_buccalPoints, ptInfo.Value.Point, Brushes.Blue);
                if (_buccalPoints.Count == 2)
                {
                    NextBtn.IsEnabled = true;
                    StatusText.Text = "2 Buccal points placed. Click Next Step to generate Polyplane.";
                }
            }
            e.Handled = true;
        }
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_step == 3 && _draggedPoint != null && _dragPlane != null)
        {
            var pos = e.GetPosition(MainViewport);
            var ray = Viewport3DHelper.Point2DtoRay3D(MainViewport.Viewport, pos);
            if (ray != null)
            {
                var intersect = RayPlaneIntersection(ray, _dragPlane);
                if (intersect.HasValue)
                {
                    _draggedPoint.Center = intersect.Value;
                    
                    if (_draggedList == 0) _lingualPoints[_draggedIndex] = intersect.Value;
                    else if (_draggedList == 1) _buccalPoints[_draggedIndex] = intersect.Value;
                    else _lowerBorderPoints[_draggedIndex] = intersect.Value;

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
            MainViewport.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private (Point3D Point, Visual3D? Visual)? GetHitPoint(Point p)
    {
        var hits = Viewport3DHelper.FindHits(MainViewport.Viewport, p);
        foreach (var hit in hits) return (hit.Position, hit.Visual);
        return null;
    }

    private double DistanceTo(Point3D a, Point3D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private Point3D? RayPlaneIntersection(Ray3D ray, Plane3D plane)
    {
        double nd = Vector3D.DotProduct(ray.Direction, plane.Normal);
        if (Math.Abs(nd) < 0.0001) return null;
        double t = Vector3D.DotProduct(plane.Position - ray.Origin, plane.Normal) / nd;
        if (t < 0) return null;
        return ray.Origin + ray.Direction * t;
    }

    private void AddPoint(List<Point3D> list, Point3D pt, Brush color)
    {
        list.Add(pt);
        var sphere = new SphereVisual3D { Center = pt, Radius = 2.0, Fill = color };
        _pointVisuals.Add(sphere);
        MainViewport.Children.Add(sphere);
    }

    // ═══════════════════════════════════
    // Stepping & Logic
    // ═══════════════════════════════════

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            _step = 2;
            StepTitle.Text = "BSSO: Step 2 - Place Buccal Points";
            StepInstructions.Text = "Rotate the model and left-click on the buccal side to place 2 points for the external cut.";
            StatusText.Text = "Waiting for 2 points...";
            NextBtn.IsEnabled = false;
        }
        else if (_step == 2)
        {
            _step = 3;
            StepTitle.Text = "BSSO: Step 3 - Adjust Osteotomy Curve";
            StepInstructions.Text = "Drag points to adjust. Lower border handles have been automatically added. Click Perform Cut when ready.";
            StatusText.Text = "Review and adjust polyplane handles.";
            NextBtn.Visibility = Visibility.Collapsed;
            CutBtn.Visibility = Visibility.Visible;
            
            GenerateLowerBorderHandles();
            UpdatePolyplane();
        }
    }

    private void GenerateLowerBorderHandles()
    {
        // Simple logic: Extrapolate downward from the buccal points.
        // P[0]=L1, P[1]=L2, P[2]=B1, P[3]=B2.
        // Usually B2 is the lowest point on the buccal cut. Let's add 2 points below it.
        var pLast = _buccalPoints[1];
        
        var lb1 = new Point3D(pLast.X, pLast.Y - 5, pLast.Z - 10);
        var lb2 = new Point3D(pLast.X, pLast.Y - 10, pLast.Z - 20);
        
        AddPoint(_lowerBorderPoints, lb1, Brushes.Yellow);
        AddPoint(_lowerBorderPoints, lb2, Brushes.Yellow);
    }

    private void UpdatePolyplane()
    {
        var polyplane = GetCurrentPolyplane();
        var meshVerts = polyplane.GenerateMesh(40.0);
        
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
        
        // Sequence: L1 -> L2 -> B1 -> B2 -> LB1 -> LB2
        var allPts = new List<Point3D>();
        allPts.AddRange(_lingualPoints);
        allPts.AddRange(_buccalPoints);
        allPts.AddRange(_lowerBorderPoints);
        
        polyplane.ControlPoints = allPts.Select(p => (p.X, p.Y, p.Z)).ToList();
        
        // Extrude along X (Medial-Lateral)
        polyplane.ExtrusionDir = new double[] { 1, 0, 0 };
        // Normal to cut along
        polyplane.UpVector = new double[] { 0, 1, 0 }; 
        return polyplane;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _step = 1;
        _lingualPoints.Clear();
        _buccalPoints.Clear();
        _lowerBorderPoints.Clear();
        
        foreach (var p in _pointVisuals) MainViewport.Children.Remove(p);
        _pointVisuals.Clear();
        
        ((MeshGeometry3D)_polyplaneMesh.Geometry).Positions.Clear();
        ((MeshGeometry3D)_polyplaneMesh.Geometry).TriangleIndices.Clear();
        
        NextBtn.Visibility = Visibility.Visible;
        NextBtn.IsEnabled = false;
        CutBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
        
        StepTitle.Text = "BSSO: Step 1 - Place Lingual Points";
        StepInstructions.Text = "Left-click on the lingual side of the mandible ramus to place 2 control points for the osteotomy.";
        StatusText.Text = "Waiting for 2 points...";
        
        ((DiffuseMaterial)_boneMesh.Material).Brush = new SolidColorBrush(Color.FromRgb(245, 230, 200));
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Performing BSSO cut... Please wait.";
        Cursor = Cursors.Wait;
        
        try
        {
            var polyplane = GetCurrentPolyplane();
            var (above, below) = MeshOps.SplitByPolyplane(_mandibleVerts, polyplane);

            // BSSO leaves a Proximal segment (Condyle + Ramus) and a Distal segment (Tooth-bearing)
            ProximalResult = MeshOps.CloseHoles(above);
            DistalResult = MeshOps.CloseHoles(below);

            _mainGroup.Children.Remove(_boneMesh);
            
            var proxMesh = CreateMeshVisual(ProximalResult, Color.FromRgb(200, 200, 255), 1.0);
            var distMesh = CreateMeshVisual(DistalResult, Color.FromRgb(255, 200, 200), 1.0);
            
            _mainGroup.Children.Add(proxMesh);
            _mainGroup.Children.Add(distMesh);
            
            AcceptBtn.Visibility = Visibility.Visible;
            CutBtn.Visibility = Visibility.Collapsed;
            ClearBtn.Content = "Undo Cut";
            
            _mainGroup.Children.Remove(_polyplaneMesh);
            foreach (var p in _pointVisuals) MainViewport.Children.Remove(p);
            
            StatusText.Text = "BSSO computed. Review the Proximal and Distal segments.";
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
