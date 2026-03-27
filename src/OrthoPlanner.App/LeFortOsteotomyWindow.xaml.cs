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
    private List<Vector3D> _step2ExtrusionDirs = new();
    
    // Interaction state
    private MeshGeometryModel3D? _draggedPoint;
    private int _draggedIndex = -1; // < 1000 is anterior, 1000=LP, 1001=LD, 1002=RP, 1003=RD
    private (Point3D Position, Vector3D Normal)? _dragPlane;

    private int _step = 1;
    private Point3D _leftPost = new(), _leftDrop = new();
    private Point3D _rightPost = new(), _rightDrop = new();
    private MeshGeometryModel3D? _lpVis, _ldVis, _rpVis, _rdVis;
    private GroupModel3D _polyplaneVis = new();

    public LeFortOsteotomyWindow(List<float[]> craniumVerts)
    {
        InitializeComponent();
        
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        // Track camera for coaxial headlamp — matches BSSO lighting pattern
        CompositionTarget.Rendering += (_, _) => {
            var d = SubCamera.LookDirection;
            if (d.Length > 0.001) { d.Normalize(); Headlamp.Direction = new System.Windows.Media.Media3D.Vector3D(-d.X, -d.Y, -d.Z); }
        };

        _craniumVerts = craniumVerts;

        _boneMesh = CreateMeshVisual(_craniumVerts, Color.FromRgb(245, 245, 230), 1.0);
        MainGroup.Children.Add(_boneMesh);

        _polyplaneMesh = CreateMeshVisual(new List<float[]>(), Color.FromArgb(128, 50, 200, 100), 1.0);
        _polyplaneMesh.CullMode = SharpDX.Direct3D11.CullMode.None;
        MainGroup.Children.Add(_polyplaneMesh);
        MainGroup.Children.Add(_polyplaneVis);

        Loaded += (_, _) => CenterOn(_craniumVerts);
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
            MeshGeometryModel3D?[] pVis = { _lpVis, _ldVis, _rpVis, _rdVis };
            Point3D[] pPts = { _leftPost, _leftDrop, _rightPost, _rightDrop };
            for (int i = 0; i < 4; i++)
            {
                if (ptInfo.Value.Visual == pVis[i] || DistanceTo(ptInfo.Value.Point, pPts[i]) < 2.0)
                {
                    _draggedPoint = pVis[i];
                    _draggedIndex = 1000 + i;
                    
                    var lookDir = MainViewport.Camera?.LookDirection ?? new Vector3D(0, 0, -1);
                    _dragPlane = (pPts[i], new Vector3D(-lookDir.X, -lookDir.Y, -lookDir.Z));
                    
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
                        if (id == 0) _leftPost = intersect.Value;
                        else if (id == 1) _leftDrop = intersect.Value;
                        else if (id == 2) _rightPost = intersect.Value;
                        else if (id == 3) _rightDrop = intersect.Value;
                        _draggedPoint.Transform = new TranslateTransform3D(intersect.Value.X, intersect.Value.Y, intersect.Value.Z);
                    }
                    else
                    {
                        if (_step == 2) {
                            _controlPoints[_draggedIndex] = intersect.Value - GetExtrusionDir(_controlPoints, _draggedIndex) * 15.0;
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
        StepTitle.Text = "LeFort 1: Adjust Posterior Extensions";
        StepInstructions.Text = "Drag the 4 cyan handles to adjust the posterior extension and drop depth, then Perform Cut.";
        NextBtn.Visibility = Visibility.Collapsed;
        CutBtn.Visibility = Visibility.Visible;
        CutBtn.IsEnabled = true;

        var antCurve = _controlPoints;
        
        _step2ExtrusionDirs.Clear();
        for (int i = 0; i < antCurve.Count; i++) _step2ExtrusionDirs.Add(OutwardDir(antCurve, i));

        double zMin = antCurve.Min(p => p.Z);
        double yMax = antCurve.Max(p => p.Y);
        double posteriorY = yMax + 30.0;
        double dropZ = zMin - 40.0;
        
        var ptL = antCurve.First();
        var ptR = antCurve.Last();
        
        // Handles sit at the innermost outer-edge corners of the step-shaped polyplane
        _leftPost  = new Point3D(ptL.X, posteriorY, ptL.Z);
        _leftDrop  = new Point3D(ptL.X, posteriorY, dropZ);
        _rightPost = new Point3D(ptR.X, posteriorY, ptR.Z);
        _rightDrop = new Point3D(ptR.X, posteriorY, dropZ);

        _lpVis = CreateSphere(_leftPost, 1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f)); MainGroup.Children.Add(_lpVis);
        _ldVis = CreateSphere(_leftDrop,  1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f)); MainGroup.Children.Add(_ldVis);
        _rpVis = CreateSphere(_rightPost, 1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f)); MainGroup.Children.Add(_rpVis);
        _rdVis = CreateSphere(_rightDrop, 1.5f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f)); MainGroup.Children.Add(_rdVis);
        
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
        var p00 = ac.Select((pt, i) => pt + GetExtrusionDir(ac, i) * 15.0).ToList();

        if (_step == 2) {
            for (int i = 0; i < cols; i++)
                _pointVisuals[i].Transform = new TranslateTransform3D(p00[i].X, p00[i].Y, p00[i].Z);
        }

        var lb = new HelixToolkit.SharpDX.LineBuilder();
        for (int i = 0; i < cols - 1; i++)
            lb.AddLine(Nv3(p00[i]), Nv3(p00[i + 1])); // front lip connecting Handles
            
        lb.AddLine(Nv3(p00[cols - 1]), Nv3(_rightPost));
        
        lb.AddLine(Nv3(_rightPost),    Nv3(_rightDrop));
        lb.AddLine(Nv3(_rightDrop),    Nv3(_leftDrop));   
        lb.AddLine(Nv3(_leftDrop),     Nv3(_leftPost));
        
        lb.AddLine(Nv3(_leftPost),     Nv3(p00[0]));

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
            Point3D a0 = a + outA * 15.0;
            Point3D b0 = b + outB * 15.0;

            // Interpolated posterior & drop corners
            Point3D a2 = Lerp3(_leftPost, _rightPost, uPrev);
            Point3D b2 = Lerp3(_leftPost, _rightPost, uCurr);
            Point3D a3 = Lerp3(_leftDrop, _rightDrop, uPrev);
            Point3D b3 = Lerp3(_leftDrop, _rightDrop, uCurr);

            AddQ(quads, a0, b0, b2, a2);   // roof: lip → post
            AddQ(quads, a2, b2, b3, a3); // vertical: post → drop
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

            Point3D post = Lerp3(_leftPost, _rightPost, u) + offset;
            Point3D drop = Lerp3(_leftDrop, _rightDrop, u) + offset;
            
            Vector3D outVec = GetExtrusionDir(ac, idx);
            if (c == -1 && cols > 1) outVec = GetExtrusionDir(ac, 0);
            if (c == cols && cols > 1) outVec = GetExtrusionDir(ac, cols - 1);

            Point3D lip = pt + outVec * 15.0; // The defined front lip
            Point3D extLip = pt + outVec * 100.0; // HUGE forward/outward padding

            if (step == 0) return extLip;
            if (step == 1) return lip;
            if (step == 2) return post;
            if (step == 3) return drop;
            if (step == 4) return new Point3D(drop.X, drop.Y, drop.Z - 100.0); // HUGE drop to seal bottom
            return default;
        }

        for (int i = 0; i <= cols; i++) {
            var c0 = GetColPoint(i-1, 0); var n0 = GetColPoint(i, 0);
            var c1 = GetColPoint(i-1, 1); var n1 = GetColPoint(i, 1);
            var c2 = GetColPoint(i-1, 2); var n2 = GetColPoint(i, 2);
            var c3 = GetColPoint(i-1, 3); var n3 = GetColPoint(i, 3);
            var c4 = GetColPoint(i-1, 4); var n4 = GetColPoint(i, 4);

            AddQ(quads, c0, n0, n1, c1);
            AddQ(quads, c1, n1, n2, c2);
            AddQ(quads, c2, n2, n3, c3);
            AddQ(quads, c3, n3, n4, c4);
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
        
        if (_lpVis != null) { MainGroup.Children.Remove(_lpVis); _lpVis = null; }
        if (_ldVis != null) { MainGroup.Children.Remove(_ldVis); _ldVis = null; }
        if (_rpVis != null) { MainGroup.Children.Remove(_rpVis); _rpVis = null; }
        if (_rdVis != null) { MainGroup.Children.Remove(_rdVis); _rdVis = null; }

        _step = 1;
        StepTitle.Text = "LeFort 1: Draw Cutting Curve";
        StepInstructions.Text = "Left-click on the maxilla to place control points for the osteotomy curve. Adjust points by dragging them.";
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

    private void Cut_Click(object sender, RoutedEventArgs e)
    {
        if (_controlPoints.Count < 2) return;

        StatusText.Text = "Performing osteotomy cut... Please wait.";
        Cursor = Cursors.Wait;
        
        try
        {
            int nTri = _craniumVerts.Count / 3;
            var edgeMap = new Dictionary<string, List<int>>(nTri * 2);
            for (int i = 0; i < nTri; i++) {
                for (int edge = 0; edge < 3; edge++) {
                    var kA = VKey(_craniumVerts[i * 3 + edge]);
                    var kB = VKey(_craniumVerts[i * 3 + (edge + 1) % 3]);
                    var ek = string.Compare(kA, kB) < 0 ? kA + "|" + kB : kB + "|" + kA;
                    if (!edgeMap.TryGetValue(ek, out var lst)) { lst = new List<int>(2); edgeMap[ek] = lst; }
                    lst.Add(i);
                }
            }

            // ─── Step 1: BFS on the padded math polyplane ───
            var polyplane = GetMathPolyplane();
            int seed = -1; float bestZ = float.MinValue;
            for (int i = 0; i < nTri; i++) {
                float cz = (_craniumVerts[i*3][2] + _craniumVerts[i*3+1][2] + _craniumVerts[i*3+2][2]) / 3f;
                if (cz > bestZ) { bestZ = cz; seed = i; } // High Z is Cranium top
            }

            var visited = new bool[nTri];
            if (seed >= 0) {
                var q = new Queue<int>(); q.Enqueue(seed); visited[seed] = true;
                while (q.Count > 0) {
                    int ti = q.Dequeue();
                    for (int edge = 0; edge < 3; edge++) {
                        var kA = VKey(_craniumVerts[ti * 3 + edge]);
                        var kB = VKey(_craniumVerts[ti * 3 + (edge + 1) % 3]);
                        var ek = string.Compare(kA, kB) < 0 ? kA + "|" + kB : kB + "|" + kA;
                        if (edgeMap.TryGetValue(ek, out var nbrs))
                            foreach (int ni in nbrs) {
                                if (!visited[ni]) {
                                    var cA = new double[]{ (_craniumVerts[ti*3][0]+_craniumVerts[ti*3+1][0]+_craniumVerts[ti*3+2][0])/3.0,
                                                           (_craniumVerts[ti*3][1]+_craniumVerts[ti*3+1][1]+_craniumVerts[ti*3+2][1])/3.0,
                                                           (_craniumVerts[ti*3][2]+_craniumVerts[ti*3+1][2]+_craniumVerts[ti*3+2][2])/3.0 };
                                    var cB = new double[]{ (_craniumVerts[ni*3][0]+_craniumVerts[ni*3+1][0]+_craniumVerts[ni*3+2][0])/3.0,
                                                           (_craniumVerts[ni*3][1]+_craniumVerts[ni*3+1][1]+_craniumVerts[ni*3+2][1])/3.0,
                                                           (_craniumVerts[ni*3][2]+_craniumVerts[ni*3+1][2]+_craniumVerts[ni*3+2][2])/3.0 };
                                    // Blocked by polyplane? Don't spread!
                                    if (polyplane.SegmentIntersects(cA, cB)) continue; 
                                    visited[ni] = true; q.Enqueue(ni);
                                }
                            }
                    }
                }
            }

            // ─── Step 2: Seeded Connected Component Cleanup for the Maxilla ───
            int mSeed = -1;
            double minDist = double.MaxValue;
            Point3D pt = _controlPoints[0]; // Seed the Maxilla exactly at a place where the user drew the curve.
            for (int i = 0; i < nTri; i++) {
                if (!visited[i]) {
                    float cx = (_craniumVerts[i*3][0]+_craniumVerts[i*3+1][0]+_craniumVerts[i*3+2][0]) / 3f;
                    float cy = (_craniumVerts[i*3][1]+_craniumVerts[i*3+1][1]+_craniumVerts[i*3+2][1]) / 3f;
                    float cz = (_craniumVerts[i*3][2]+_craniumVerts[i*3+1][2]+_craniumVerts[i*3+2][2]) / 3f;
                    double dist = (cx-pt.X)*(cx-pt.X) + (cy-pt.Y)*(cy-pt.Y) + (cz-pt.Z)*(cz-pt.Z);
                    if (dist < minDist) { minDist = dist; mSeed = i; }
                }
            }

            var mainMaxilla = mSeed >= 0 ? ExtractComponentFromSeed(visited, nTri, edgeMap, false, mSeed) : new HashSet<int>();
            for (int i = 0; i < nTri; i++) {
                if (!visited[i] && !mainMaxilla.Contains(i)) {
                    visited[i] = true; // Clean floaters back to Cranium
                }
            }

            // ─── Step 3: Split meshes ───
            var above = new List<float[]>();
            var below = new List<float[]>();
            for (int i = 0; i < nTri; i++) {
                if (visited[i]) { above.Add(_craniumVerts[i*3]); above.Add(_craniumVerts[i*3+1]); above.Add(_craniumVerts[i*3+2]); }
                else            { below.Add(_craniumVerts[i*3]); below.Add(_craniumVerts[i*3+1]); below.Add(_craniumVerts[i*3+2]); }
            }

            // Hole closing disabled dynamically for LeFort 1 to completely prevent "rayburst" remeshing artifacts.
            UpperMaxillaResult = above;
            LowerMaxillaResult = below;

            MainGroup.Children.Remove(_boneMesh);
            
            var upperMesh = CreateMeshVisual(UpperMaxillaResult, Color.FromRgb(245, 245, 230), 1.0); // cranium bone colour
            var lowerMesh = CreateMeshVisual(LowerMaxillaResult, Color.FromRgb(120, 220, 210), 1.0);  // teal = LeFort maxilla
            
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

    // Returns the set of triangle indices in the selected component
    private HashSet<int> ExtractComponentFromSeed(bool[] visited, int nTri, Dictionary<string, List<int>> edgeMap, bool targetSide, int seed)
    {
        var comp = new HashSet<int> { seed };
        var q = new Queue<int>(); q.Enqueue(seed);
        visited[seed] = !targetSide; // temporarily mark processed
        while (q.Count > 0) {
            int ti = q.Dequeue();
            for (int edge = 0; edge < 3; edge++) {
                var kA = VKey(_craniumVerts[ti * 3 + edge]);
                var kB = VKey(_craniumVerts[ti * 3 + (edge + 1) % 3]);
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



