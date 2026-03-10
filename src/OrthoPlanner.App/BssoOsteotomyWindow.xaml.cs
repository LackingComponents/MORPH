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

public partial class BssoOsteotomyWindow : Window
{
    private List<float[]> _mandibleVerts;
    
    // Results
    public List<float[]> ProximalResult { get; private set; } = new();
    public List<float[]> DistalResult { get; private set; } = new();
    public bool Accepted { get; private set; } = false;

    // Viewport elements
    private MeshGeometryModel3D _boneMesh;
    private MeshGeometryModel3D _polyplaneMesh;
    
    // Control points
    private List<Point3D> _lingualPoints = new();
    private List<Point3D> _buccalPoints = new();
    private List<Point3D> _lowerBorderPoints = new();
    
    private List<MeshGeometryModel3D> _pointVisuals = new();
    
    // Interaction state
    private int _step = 1; // 1: Lingual, 2: Buccal, 3: Adjust
    private MeshGeometryModel3D? _draggedPoint;
    private int _draggedIndex = -1;
    private int _draggedList = 0; // 0=Lingual, 1=Buccal, 2=LowerBorder
    private (Point3D Position, Vector3D Normal)? _dragPlane;

    public BssoOsteotomyWindow(List<float[]> mandibleVerts)
    {
        InitializeComponent();
        
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _mandibleVerts = mandibleVerts;

        _boneMesh = CreateMeshVisual(_mandibleVerts, Color.FromRgb(245, 230, 200), 1.0);
        MainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 100, 200), 1.0);
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

        // 1. Check if we hit an existing handle (drag) - only allowed in step 3
        if (_step == 3)
        {
            for (int i = 0; i < _pointVisuals.Count; i++)
            {
                var vis = _pointVisuals[i];
                var center = GetPointCenter(i);
                
                if (ptInfo.Value.Visual == vis || DistanceTo(ptInfo.Value.Point, center) < 2.0)
                {
                    _draggedPoint = vis;
                    
                    // Determine which list this belongs to
                    if (i < 2) { _draggedList = 0; _draggedIndex = i; }
                    else if (i < 4) { _draggedList = 1; _draggedIndex = i - 2; }
                    else { _draggedList = 2; _draggedIndex = i - 4; }
                    
                    var lookDir = MainViewport.Camera.LookDirection;
                    _dragPlane = (center, new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                    
                    MainViewport.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        // 2. Add points in Steps 1 and 2
        if (ptInfo.Value.Visual == _boneMesh)
        {
            if (_step == 1 && _lingualPoints.Count < 2)
            {
                AddPoint(_lingualPoints, ptInfo.Value.Point, System.Windows.Media.Colors.Green);
                if (_lingualPoints.Count == 2)
                {
                    NextBtn.IsEnabled = true;
                    StatusText.Text = "2 Lingual points placed. Click Next Step.";
                }
            }
            else if (_step == 2 && _buccalPoints.Count < 2)
            {
                AddPoint(_buccalPoints, ptInfo.Value.Point, System.Windows.Media.Colors.Blue);
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
                    _draggedPoint.Transform = new TranslateTransform3D(intersect.Value.X, intersect.Value.Y, intersect.Value.Z);
                    
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
    
    private Point3D GetPointCenter(int globalIndex)
    {
        if (globalIndex < 2) return _lingualPoints[globalIndex];
        if (globalIndex < 4) return _buccalPoints[globalIndex - 2];
        return _lowerBorderPoints[globalIndex - 4];
    }

    private Point3D? RayPlaneIntersection(Point3D rayOrigin, Vector3D rayDirection, Point3D planePosition, Vector3D planeNormal)
    {
        double nd = Vector3D.DotProduct(rayDirection, planeNormal);
        if (Math.Abs(nd) < 0.0001) return null;
        double t = Vector3D.DotProduct(planePosition - rayOrigin, planeNormal) / nd;
        if (t < 0) return null;
        return rayOrigin + rayDirection * t;
    }

    private void AddPoint(List<Point3D> list, Point3D pt, System.Windows.Media.Color color)
    {
        list.Add(pt);
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), 2f);
        var sphereGeom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(color.R/255f, color.G/255f, color.B/255f, color.A/255f) };

        var sphere = new MeshGeometryModel3D 
        { 
            Geometry = sphereGeom,
            Material = mat,
            Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z)
        };

        _pointVisuals.Add(sphere);
        MainGroup.Children.Add(sphere);
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
        var pLast = _buccalPoints[1];
        
        var lb1 = new Point3D(pLast.X, pLast.Y - 5, pLast.Z - 10);
        var lb2 = new Point3D(pLast.X, pLast.Y - 10, pLast.Z - 20);
        
        AddPoint(_lowerBorderPoints, lb1, System.Windows.Media.Colors.Yellow);
        AddPoint(_lowerBorderPoints, lb2, System.Windows.Media.Colors.Yellow);
    }

    private void UpdatePolyplane()
    {
        var polyplane = GetCurrentPolyplane();
        var meshVerts = polyplane.GenerateMesh(40.0);
        
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
        
        var allPts = new List<Point3D>();
        allPts.AddRange(_lingualPoints);
        allPts.AddRange(_buccalPoints);
        allPts.AddRange(_lowerBorderPoints);
        
        polyplane.ControlPoints = allPts.Select(p => (p.X, p.Y, p.Z)).ToList();
        
        polyplane.ExtrusionDir = new double[] { 1, 0, 0 };
        polyplane.UpVector = new double[] { 0, 1, 0 }; 
        return polyplane;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _step = 1;
        _lingualPoints.Clear();
        _buccalPoints.Clear();
        _lowerBorderPoints.Clear();
        
        foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);
        _pointVisuals.Clear();
        
        _polyplaneMesh.Geometry = null;
        
        NextBtn.Visibility = Visibility.Visible;
        NextBtn.IsEnabled = false;
        CutBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
        
        StepTitle.Text = "BSSO: Step 1 - Place Lingual Points";
        StepInstructions.Text = "Left-click on the lingual side of the mandible ramus to place 2 control points for the osteotomy.";
        StatusText.Text = "Waiting for 2 points...";
        
        if (_boneMesh.Material is PhongMaterial pm)
            pm.DiffuseColor = new HelixToolkit.Maths.Color4(245/255f, 230/255f, 200/255f, 1.0f);
    }

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Performing BSSO cut... Please wait.";
        Cursor = Cursors.Wait;
        
        try
        {
            var polyplane = GetCurrentPolyplane();
            var (above, below) = MeshOps.SplitByPolyplane(_mandibleVerts, polyplane);

            ProximalResult = MeshOps.CloseHoles(above);
            DistalResult = MeshOps.CloseHoles(below);

            MainGroup.Children.Remove(_boneMesh);
            
            var proxMesh = CreateMeshVisual(ProximalResult, Color.FromRgb(200, 200, 255), 1.0);
            var distMesh = CreateMeshVisual(DistalResult, Color.FromRgb(255, 200, 200), 1.0);
            
            MainGroup.Children.Add(proxMesh);
            MainGroup.Children.Add(distMesh);
            
            AcceptBtn.Visibility = Visibility.Visible;
            CutBtn.Visibility = Visibility.Collapsed;
            ClearBtn.Content = "Undo Cut";
            
            MainGroup.Children.Remove(_polyplaneMesh);
            foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);
            
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




