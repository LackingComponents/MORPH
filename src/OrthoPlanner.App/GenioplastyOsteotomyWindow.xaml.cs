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

public partial class GenioplastyOsteotomyWindow : Window
{
    private List<float[]> _mandibleVerts;
    
    // Results
    public List<float[]> UpperMandibleResult { get; private set; } = new();
    public List<float[]> ChinSegmentResult { get; private set; } = new();
    public bool Accepted { get; private set; } = false;

    // Viewport elements
    private MeshGeometryModel3D _boneMesh;
    private MeshGeometryModel3D _polyplaneMesh;
    
    // Control points
    private List<Point3D> _controlPoints = new();
    private List<MeshGeometryModel3D> _pointVisuals = new();
    private List<Vector3D> _step2ExtrusionDirs = new();
    
    // Interaction state
    private MeshGeometryModel3D? _draggedPoint;
    private int _draggedIndex = -1; // < 1000 is anterior, 1000=LP, 1001=LD, 1002=RP, 1003=RD
    private (Point3D Position, Vector3D Normal)? _dragPlane;

    private int _step = 1;
    private List<Point3D> _posteriorPoints = new();
    private List<MeshGeometryModel3D> _posteriorVisuals = new();
    private GroupModel3D _polyplaneVis = new();
    private EventHandler? _renderingHandler;

    public GenioplastyOsteotomyWindow(List<float[]> mandibleVerts)
    {
        InitializeComponent();
        
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _renderingHandler = (_, _) => {
            var d = SubCamera.LookDirection;
            if (d.Length > 0.001) { d.Normalize(); Headlamp.Direction = new System.Windows.Media.Media3D.Vector3D(-d.X, -d.Y, -d.Z); Backlamp.Direction = new System.Windows.Media.Media3D.Vector3D(d.X, d.Y, d.Z); }
        };
        CompositionTarget.Rendering += _renderingHandler;

        _mandibleVerts = mandibleVerts;

        _boneMesh = CreateMeshVisual(_mandibleVerts, Color.FromRgb(245, 245, 230), 1.0);
        MainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 200, 100), 1.0);
        _polyplaneMesh.CullMode = SharpDX.Direct3D11.CullMode.None;
        MainGroup.Children.Add(_polyplaneMesh);
        MainGroup.Children.Add(_polyplaneVis);

        Loaded += (_, _) => CenterOn(_mandibleVerts);
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        MainGroup.Children.Clear();
        if (MainViewport.EffectsManager is IDisposable disposable)
            disposable.Dispose();
        MainViewport.EffectsManager = null;
    }

    private void CenterOn(List<float[]> v) {
        if(v==null||v.Count==0||MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        double mnX=double.MaxValue,mnY=double.MaxValue,mnZ=double.MaxValue;
        double mxX=double.MinValue,mxY=double.MinValue,mxZ=double.MinValue;
        foreach(var u in v){if(u[0]<mnX)mnX=u[0];if(u[0]>mxX)mxX=u[0];if(u[1]<mnY)mnY=u[1];if(u[1]>mxY)mxY=u[1];if(u[2]<mnZ)mnZ=u[2];if(u[2]>mxZ)mxZ=u[2];}
        var c=new Point3D((mnX+mxX)/2,(mnY+mxY)/2,(mnZ+mxZ)/2);
        double dist=Math.Sqrt(Math.Pow(mxX-mnX,2)+Math.Pow(mxY-mnY,2)+Math.Pow(mxZ-mnZ,2))*1.2;
        var dir=new Vector3D(0,1,0);
        cam.Position=new Point3D(c.X-dir.X*dist,c.Y-dir.Y*dist,c.Z-dir.Z*dist);
        cam.LookDirection=dir*dist; cam.UpDirection=new Vector3D(0,0,1);
        MainViewport.FixedRotationPointEnabled=true; MainViewport.FixedRotationPoint=c;
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

        // 1. Check if we hit an anterior handle
        for (int i = 0; i < _pointVisuals.Count; i++)
        {
            var vis = _pointVisuals[i];
            var center = _controlPoints[i];
            Point3D handlePos = _step == 2 ? (center + GetExtrusionDir(_controlPoints, i) * 15.0) : center;
            
            if (ptInfo.Value.Visual == vis || DistanceTo(ptInfo.Value.Point, handlePos) < 2.0)
            {
                _draggedPoint = vis;
                _draggedIndex = i;
                
                var lookDir = MainViewport.Camera?.LookDirection ?? new Vector3D(0, 0, -1);
                _dragPlane = (handlePos, new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                
                MainViewport.CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // 2. Check if we hit a posterior handle (Step 2)
        if (_step == 2)
        {
            for (int i = 0; i < _posteriorVisuals.Count; i++)
            {
                if (ptInfo.Value.Visual == _posteriorVisuals[i] || DistanceTo(ptInfo.Value.Point, _posteriorPoints[i]) < 2.0)
                {
                    _draggedPoint = _posteriorVisuals[i];
                    _draggedIndex = 1000 + i;
                    
                    var lookDir = MainViewport.Camera?.LookDirection ?? new Vector3D(0, 0, -1);
                    _dragPlane = (_posteriorPoints[i], new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                    
                    MainViewport.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        // 3. Otherwise add a new point
        if (_step == 1 && ptInfo.Value.Visual == _boneMesh)
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
                    if (_draggedIndex >= 1000)
                    {
                        int id = _draggedIndex - 1000;
                        _posteriorPoints[id] = intersect.Value;
                        _draggedPoint.Transform = new TranslateTransform3D(intersect.Value.X, intersect.Value.Y, intersect.Value.Z);
                    }
                    else
                    {
                        if (_step == 2) {
                            _controlPoints[_draggedIndex] = intersect.Value - GetExtrusionDir(_controlPoints, _draggedIndex) * 10.0;
                        } else {
                            _controlPoints[_draggedIndex] = intersect.Value;
                            _draggedPoint.Transform = new TranslateTransform3D(intersect.Value.X, intersect.Value.Y, intersect.Value.Z);
                        }
                    }
                    
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

    private static System.Numerics.Vector3 Nv3(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

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
        var sphere = CreateSphere(pt, 1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f));
        _pointVisuals.Add(sphere);
        MainGroup.Children.Add(sphere);
        
        if (_controlPoints.Count >= 2)
        {
            NextBtn.IsEnabled = true;
            UpdatePolyplane();
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_controlPoints.Count < 2) return;
        _step = 2;
        StepTitle.Text = "Genioplasty: Adjust Posterior Extensions";
        StepInstructions.Text = "Drag the 4 cyan handles to adjust the posterior extension and drop depth, then Perform Cut.";
        NextBtn.Visibility = Visibility.Collapsed;
        CutBtn.Visibility = Visibility.Visible;
        CutBtn.IsEnabled = true;

        var antCurve = _controlPoints;
        
        _step2ExtrusionDirs.Clear();
        for (int i = 0; i < antCurve.Count; i++) _step2ExtrusionDirs.Add(OutwardDir(antCurve, i));

        _posteriorPoints.Clear(); _posteriorVisuals.Clear();

        for (int i = 0; i < antCurve.Count; i++)
        {
            Point3D postPt = new Point3D(antCurve[i].X, antCurve[i].Y + 25.0, antCurve[i].Z);
            
            _posteriorPoints.Add(postPt);
            
            var sp = CreateSphere(postPt, 1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f));
            _posteriorVisuals.Add(sp); MainGroup.Children.Add(sp);
        }
        
        UpdatePolyplane();
    }
    
    private MeshGeometryModel3D CreateSphere(Point3D pt, float r, HelixToolkit.Maths.Color4 color)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), r);
        var sphereGeom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        var mat = new PhongMaterial { 
            DiffuseColor = color, 
            SpecularColor = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f),
            SpecularShininess = 32f
        };
        return new MeshGeometryModel3D { Geometry = sphereGeom, Material = mat, Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z) };
    }

    private static readonly HelixToolkit.Maths.Color4 CyanFill = new(0f, 1f, 1f, 0.12f);

    private void UpdatePolyplane()
    {
        if (_step == 1) return;
        if (_controlPoints.Count < 2) return;

        var polyplane = GetCurrentPolyplane();
        var meshVerts = polyplane.MeshVertices;

        var mb = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i + 2 < meshVerts.Count; i += 3)
            mb.AddTriangle(
                new System.Numerics.Vector3(meshVerts[i][0],   meshVerts[i][1],   meshVerts[i][2]),
                new System.Numerics.Vector3(meshVerts[i+1][0], meshVerts[i+1][1], meshVerts[i+1][2]),
                new System.Numerics.Vector3(meshVerts[i+2][0], meshVerts[i+2][1], meshVerts[i+2][2]));

        _polyplaneMesh.Material = new PhongMaterial { 
            DiffuseColor = new HelixToolkit.Maths.Color4(0f, 1f, 1f, 0.25f),
            EmissiveColor = new HelixToolkit.Maths.Color4(0f, 1f, 1f, 0.25f)
        };
        _polyplaneMesh.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh());

        // Outer perimeter contour tracing the handles strictly:
        var ac = _controlPoints;
        int cols = ac.Count;
        var p00 = ac.Select((pt, i) => pt + GetExtrusionDir(ac, i) * 10.0).ToList();

        if (_step == 2) {
            for (int i = 0; i < cols; i++) {
                _pointVisuals[i].Transform = new TranslateTransform3D(p00[i].X, p00[i].Y, p00[i].Z);
                _posteriorVisuals[i].Transform = new TranslateTransform3D(_posteriorPoints[i].X, _posteriorPoints[i].Y, _posteriorPoints[i].Z);
            }
        }

        var lb = new HelixToolkit.SharpDX.LineBuilder();
        for (int i = 0; i < cols - 1; i++)
            lb.AddLine(Nv3(p00[i]), Nv3(p00[i + 1])); // front lip connecting Handles
            
        if (_step == 2)
        {
            lb.AddLine(Nv3(p00[cols-1]), Nv3(_posteriorPoints[cols-1]));
            for (int i = cols - 1; i > 0; i--) lb.AddLine(Nv3(_posteriorPoints[i]), Nv3(_posteriorPoints[i-1]));
            lb.AddLine(Nv3(_posteriorPoints[0]), Nv3(p00[0]));
        }

        _polyplaneVis.Children.Clear();
        _polyplaneVis.Children.Add(new LineGeometryModel3D
        {
            Geometry  = lb.ToLineGeometry3D(),
            Color     = Colors.Cyan,
            Thickness = 2
        });
    }

    // Visual polyplane: simple roof (anterior to post) and vertical drop (post to drop)
    private Polyplane GetCurrentPolyplane()
    {
        var quads = new List<(float[], float[], float[], float[])>();
        int cols = _controlPoints.Count;
        if (cols < 2) return new Polyplane(0);
        var ac = _controlPoints;

        for (int i = 1; i < cols; i++)
        {
            double uPrev = (double)(i - 1) / (cols - 1);
            double uCurr = (double)i / (cols - 1);

            Point3D a  = ac[i - 1];
            Point3D b  = ac[i];
            
            Vector3D outA = GetExtrusionDir(ac, i - 1);
            Vector3D outB = GetExtrusionDir(ac, i);
            Point3D a0 = a + outA * 10.0;
            Point3D b0 = b + outB * 10.0;
            Point3D a2 = _step == 2 ? _posteriorPoints[i - 1] : new Point3D(a.X, a.Y + 25.0, a.Z);
            Point3D b2 = _step == 2 ? _posteriorPoints[i] : new Point3D(b.X, b.Y + 25.0, b.Z);

            AddQ(quads, a0, b0, b2, a2);
        }

        var pp = new Polyplane(0.0);
        pp.SetMeshFromQuads(quads);
        return pp;
    }

    private Vector3D GetExtrusionDir(List<Point3D> ac, int i)
    {
        if (_step == 2 && i < _step2ExtrusionDirs.Count)
            return _step2ExtrusionDirs[i];
        return OutwardDir(ac, i);
    }

    private Polyplane GetMathPolyplane()
    {
        var quads = new List<(float[], float[], float[], float[])>();
        var ac = _controlPoints;
        int cols = ac.Count;
        if (cols < 2) return new Polyplane(0);

        Point3D GetColPoint(int c, int step) {
            double u = cols > 1 ? Math.Clamp((double)c / (cols - 1), 0.0, 1.0) : 0.0;
            int idx = Math.Clamp(c, 0, cols - 1);
            Point3D pt = ac[idx];
            
            Vector3D offset = default;
            if (c == -1 && cols > 1) {
                var v = new Vector3D(ac[0].X - ac[1].X, ac[0].Y - ac[1].Y, 0);
                if (v.LengthSquared > 0) { v.Normalize(); offset = v * 100.0; }
            }
            if (c == cols && cols > 1) {
                var v = new Vector3D(ac[cols-1].X - ac[cols-2].X, ac[cols-1].Y - ac[cols-2].Y, 0);
                if (v.LengthSquared > 0) { v.Normalize(); offset = v * 100.0; }
            }
            pt += offset;

            Point3D post = _step == 2 ? _posteriorPoints[idx] : new Point3D(pt.X, pt.Y + 25.0, pt.Z);
            
            Vector3D outVec = GetExtrusionDir(ac, idx);
            if (c == -1 && cols > 1) outVec = GetExtrusionDir(ac, 0);
            if (c == cols && cols > 1) outVec = GetExtrusionDir(ac, cols - 1);

            Point3D lip = pt + outVec * 10.0; // Reduced front lip

            if (step == 0) return lip + offset; // No forward padding anymore, bound tightly to visual plane
            if (step == 1) return pt + offset;
            if (step == 2) return post + offset;  // No backward padding anymore, cut ends exactly
            
            return default;
        }

        for (int i = 0; i <= cols; i++) {
            var c0 = GetColPoint(i-1, 0); var n0 = GetColPoint(i, 0);
            var c1 = GetColPoint(i-1, 1); var n1 = GetColPoint(i, 1);
            var c2 = GetColPoint(i-1, 2); var n2 = GetColPoint(i, 2);

            AddQ(quads, c0, n0, n1, c1);
            AddQ(quads, c1, n1, n2, c2);
        }

        var pp = new Polyplane(0.0);
        pp.SetMeshFromQuads(quads);
        return pp;
    }

    private static Vector3D OutwardDir(List<Point3D> ac, int i)
    {
        int n = ac.Count;
        if (n == 1) return new Vector3D(0, -1, 0);
        if (i == 0) {
            var v = new Vector3D(ac[0].X - ac[1].X, ac[0].Y - ac[1].Y, 0);
            v.Normalize();
            return new Vector3D(-v.Y, v.X, 0);
        }
        if (i == n - 1) {
            var v = new Vector3D(ac[n-1].X - ac[n-2].X, ac[n-1].Y - ac[n-2].Y, 0);
            v.Normalize();
            return new Vector3D(v.Y, -v.X, 0);
        }
        return new Vector3D(0, -1, 0);
    }

    private static Point3D Lerp3(Point3D a, Point3D b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static Point3D ExtendPoint(List<Point3D> ac, int idx, int dir, double dist)
    {
        // Extend first or last point outward from the curve
        if (ac.Count < 2) return ac[idx];
        Point3D a = idx == 0 ? ac[0] : ac[ac.Count - 1];
        Point3D b = idx == 0 ? ac[1] : ac[ac.Count - 2];
        var v = new Vector3D(a.X - b.X, 0, 0);
        v.Normalize();
        return a + v * dist * dir;
    }

    private static void AddQ(List<(float[], float[], float[], float[])> quads,
        Point3D a, Point3D b, Point3D c, Point3D d) =>
        quads.Add((P3(a), P3(b), P3(c), P3(d)));

    private static float[] P3(Point3D p) => new[] { (float)p.X, (float)p.Y, (float)p.Z };

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _controlPoints.Clear();
        foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);
        _pointVisuals.Clear();
        
        foreach (var v in _posteriorVisuals) MainGroup.Children.Remove(v);
        _posteriorVisuals.Clear(); _posteriorPoints.Clear();

        _step = 1;
        StepTitle.Text = "Genioplasty: Draw Cutting Curve";
        StepInstructions.Text = "Left-click on the chin to place control points for the osteotomy curve. Adjust points by dragging them.";
        NextBtn.Visibility = Visibility.Visible;
        NextBtn.IsEnabled = false;
        CutBtn.Visibility = Visibility.Collapsed;
        CutBtn.IsEnabled = false;
        AcceptBtn.Visibility = Visibility.Collapsed;
        
        _polyplaneMesh.Geometry = null;
        _polyplaneVis.Children.Clear();
        
        if (_boneMesh.Material is PhongMaterial pm)
            pm.DiffuseColor = new HelixToolkit.Maths.Color4(245/255f, 245/255f, 230/255f, 1.0f);
    }

    private string VKey(float[] v) => $"{Math.Round(v[0],2)}|{Math.Round(v[1],2)}|{Math.Round(v[2],2)}";

    private async void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (_controlPoints.Count < 2) return;

        StatusText.Text = "True-slicing genioplasty… (may take a moment)";
        Cursor = Cursors.Wait;
        CutBtn.IsEnabled = false;

        // Build the exact polyplane the user sees, then snapshot for the background thread
        var polyplane     = GetMathPolyplane();
        var mandibleVerts = _mandibleVerts;

        List<float[]> above, below;
        try
        {
            (above, below) = await System.Threading.Tasks.Task.Run(() =>
            {
                // Reference: highest-Z vertex = mandible body (always superior to chin)
                double bestZ = double.MinValue;
                double[] bodyRef = { 0, 0, 0 };
                foreach (var v in mandibleVerts)
                    if (v[2] > bestZ) { bestZ = v[2]; bodyRef = new double[]{ v[0], v[1], v[2] }; }

                return MeshOps.TrueSliceByPolyplane(mandibleVerts, polyplane, bodyRef, capEnds: true);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cut failed: {ex.Message}";
            CutBtn.IsEnabled = true;
            Cursor = Cursors.Arrow;
            return;
        }

        // "above" = mandible body (same parity as bodyRef), "below" = chin segment
        UpperMandibleResult = above;
        ChinSegmentResult   = below;

        MainGroup.Children.Remove(_boneMesh);

        var upperMesh = CreateMeshVisual(UpperMandibleResult, Color.FromRgb(245, 245, 230), 1.0);
        var lowerMesh = CreateMeshVisual(ChinSegmentResult,   Color.FromRgb(120, 220, 210), 1.0);

        MainGroup.Children.Add(upperMesh);
        MainGroup.Children.Add(lowerMesh);

        AcceptBtn.Visibility = Visibility.Visible;
        CutBtn.IsEnabled     = false;
        ClearBtn.Content     = "Undo Cut";

        MainGroup.Children.Remove(_polyplaneMesh);
        foreach (var p in _pointVisuals) MainGroup.Children.Remove(p);

        StatusText.Text = $"Done — Mandible (bone): {above.Count/3} tris | Chin (teal): {below.Count/3} tris";
        Cursor = Cursors.Arrow;
    }


    // Returns the set of triangle indices in the selected component
    private HashSet<int> ExtractComponentFromSeed(bool[] visited, int nTri, Dictionary<string, List<int>> edgeMap, bool targetSide, int seed)
    {
        var comp = new HashSet<int> { seed };
        var q = new Queue<int>(); q.Enqueue(seed);
        visited[seed] = !targetSide; // temporarily mark processed
        while (q.Count > 0) {
            int ti = q.Dequeue();
            for (int edge = 0; edge < 3; edge++) {
                var kA = VKey(_mandibleVerts[ti * 3 + edge]);
                var kB = VKey(_mandibleVerts[ti * 3 + (edge + 1) % 3]);
                var ek = string.Compare(kA, kB) < 0 ? kA + "|" + kB : kB + "|" + kA;
                if (edgeMap.TryGetValue(ek, out var nbrs))
                    foreach (int ni in nbrs)
                        if (visited[ni] == targetSide && !comp.Contains(ni)) { 
                            comp.Add(ni); q.Enqueue(ni); 
                        }
            }
        }
        // Restore visited state naturally for the extracted component
        foreach (int c in comp) visited[c] = targetSide;
        return comp;
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



