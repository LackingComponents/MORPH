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

public partial class LeFort1SagittalCutWindow : Window
{
    private readonly List<float[]> _maxillaVerts;
    public List<float[]> LeftResult  { get; private set; } = new();
    public List<float[]> RightResult { get; private set; } = new();
    public bool Accepted { get; private set; }

    // Scene objects
    private MeshGeometryModel3D _boneMesh;
    private MeshGeometryModel3D _planeMesh   = new() { CullMode = SharpDX.Direct3D11.CullMode.None };
    private GroupModel3D        _linesGroup  = new();
    private GroupModel3D        _handlesGroup = new();

    // Step-1 placed control points
    private readonly List<Point3D>             _ctrl    = new();
    private readonly List<MeshGeometryModel3D> _ctrlVis = new();


    // 8 handles:
    //  [0] FrontTop  [1] FrontBot   - anterior edge corners  (cyan)
    //  [2] BackTop   [3] BackBot    - posterior edge corners  (cyan)
    //  [4] Div1Top   [5] Div1Bot    - first  divider          (orange)
    //  [6] Div2Top   [7] Div2Bot    - second divider          (orange)
    private const int NH = 8;
    private readonly MeshGeometryModel3D[] _hm = new MeshGeometryModel3D[NH];
    private readonly Point3D[]             _hp = new Point3D[NH];

    private int  _dragH = -1;
    private int  _dragC = -1;
    private bool _planeVisible;
    private EventHandler? _rh;

    public LeFort1SagittalCutWindow(List<float[]> maxillaVerts)
    {
        InitializeComponent();
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        _rh = (_, _) => {
            var d = SubCamera.LookDirection;
            if (d.Length > .001) { d.Normalize(); Headlamp.Direction = new(-d.X,-d.Y,-d.Z); Backlamp.Direction = new(d.X,d.Y,d.Z); }
        };
        CompositionTarget.Rendering += _rh;

        _maxillaVerts = maxillaVerts;
        _boneMesh = MakeMesh(_maxillaVerts, Color.FromRgb(120, 220, 210), 1.0);
        MainGroup.Children.Add(_boneMesh);
        MainGroup.Children.Add(_planeMesh);
        MainGroup.Children.Add(_linesGroup);
        MainGroup.Children.Add(_handlesGroup);

        Loaded += (_, _) => FitCamera(_maxillaVerts);
        Closed += (_, _) => {
            if (_rh != null) { CompositionTarget.Rendering -= _rh; _rh = null; }
            MainGroup.Children.Clear();
            if (MainViewport.EffectsManager is IDisposable d) d.Dispose();
            MainViewport.EffectsManager = null;
        };
    }

    // -- Mouse -------------------------------------------------------------------------

    private void Viewport_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (Accepted) return;
        var pos = e.GetPosition(MainViewport);

        if (_planeVisible)
            for (int i = 0; i < NH; i++)
                if (HitSphere(pos, _hp[i], 4.0)) { _dragH = i; MainViewport.CaptureMouse(); e.Handled = true; return; }

        for (int i = 0; i < _ctrlVis.Count; i++)
            if (HitSphere(pos, _ctrl[i], 4.0)) { _dragC = i; MainViewport.CaptureMouse(); e.Handled = true; return; }

        if (!_planeVisible)
        {
            var hit = RaycastBone(pos);
            if (hit.HasValue && _ctrl.Count < 2) { PlaceCtrl(hit.Value); e.Handled = true; }
        }
    }

    private void Viewport_PreviewMouseMove(object s, MouseEventArgs e)
    {
        if (!MainViewport.IsMouseCaptured) return;
        var pos = e.GetPosition(MainViewport);

        if (_dragH >= 0)
        {
            var pt = CamPlane(pos, _hp[_dragH]);
            if (!pt.HasValue) return;
            _hp[_dragH] = pt.Value;
            ClampBounds();
            RebuildPlane();
        }
        else if (_dragC >= 0)
        {
            var hit = RaycastBone(pos);
            if (!hit.HasValue) return;
            _ctrl[_dragC] = hit.Value;
            _ctrlVis[_dragC].Transform = new TranslateTransform3D(hit.Value.X, hit.Value.Y, hit.Value.Z);
            if (_planeVisible) InitPlaneFromCtrl();
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object s, MouseButtonEventArgs e)
    {
        _dragH = -1; _dragC = -1;
        MainViewport.ReleaseMouseCapture();
    }

    // -- Control point placement -------------------------------------------------------

    private void PlaceCtrl(Point3D pt)
    {
        _ctrl.Add(pt);
        var sp = Sphere(pt, 1.0f, new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f));
        _ctrlVis.Add(sp);
        MainGroup.Children.Add(sp);
        StatusText.Text = $"Points placed: {_ctrl.Count} / 2";
        if (_ctrl.Count == 2) { NextBtn.IsEnabled = true; InitPlaneFromCtrl(); }
    }

    private void InitPlaneFromCtrl()
    {
        var p0 = _ctrl[0]; var p1 = _ctrl[1];
        double xMid   = (p0.X + p1.X) / 2.0;
        double yFront = Math.Min(p0.Y, p1.Y) - 15.0;
        double yBack  = Math.Max(p0.Y, p1.Y) + 50.0;
        double zTop   = Math.Max(p0.Z, p1.Z) + 20.0;
        double zBot   = Math.Min(p0.Z, p1.Z) - 20.0;
        double span = yBack - yFront;
        double yDiv1  = yFront + span / 3.0;
        double yDiv2  = yFront + 2.0 * span / 3.0;

        _hp[0] = new(xMid, yFront, zTop);  _hp[1] = new(xMid, yFront, zBot);
        _hp[2] = new(xMid, yBack,  zTop);  _hp[3] = new(xMid, yBack,  zBot);
        _hp[4] = new(xMid, yDiv1,  zTop);  _hp[5] = new(xMid, yDiv1,  zBot);
        _hp[6] = new(xMid, yDiv2,  zTop);  _hp[7] = new(xMid, yDiv2,  zBot);

        _planeVisible = true;
        RebuildPlane();
    }

    private void ClampBounds()
    {
        // No longer clamping bounds; handles are freely dragged in 3D.
    }

    // -- Plane rendering ---------------------------------------------------------------

    private void RebuildPlane()
    {
        var mb = new HelixToolkit.Geometry.MeshBuilder();
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        Action<Point3D, Point3D, Point3D, Point3D> addQuad = (t1, t2, b2, b1) => {
            var TF = Nv3f((float)t1.X, (float)t1.Y, (float)t1.Z);
            var TB = Nv3f((float)t2.X, (float)t2.Y, (float)t2.Z);
            var BB = Nv3f((float)b2.X, (float)b2.Y, (float)b2.Z);
            var BF = Nv3f((float)b1.X, (float)b1.Y, (float)b1.Z);
            mb.AddTriangle(TF, TB, BB); mb.AddTriangle(TF, BB, BF);  // front winding
            mb.AddTriangle(TF, BB, TB); mb.AddTriangle(TF, BF, BB);  // back winding
            lb.AddLine(TF, TB); lb.AddLine(TB, BB); lb.AddLine(BB, BF); lb.AddLine(BF, TF);
        };
        
        // Anterior section: Front(0,1) to Div1(4,5)
        addQuad(_hp[0], _hp[4], _hp[5], _hp[1]);
        // Middle section: Div1(4,5) to Div2(6,7)
        addQuad(_hp[4], _hp[6], _hp[7], _hp[5]);
        // Posterior section: Div2(6,7) to Back(2,3)
        addQuad(_hp[6], _hp[2], _hp[3], _hp[7]);

        _planeMesh.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh());
        _planeMesh.Material = new PhongMaterial {
            DiffuseColor  = new HelixToolkit.Maths.Color4(0f, 0.9f, 1f, 0.18f),
            EmissiveColor = new HelixToolkit.Maths.Color4(0f, 0.8f, 1f, 0.12f) };

        _linesGroup.Children.Clear();
        _linesGroup.Children.Add(new LineGeometryModel3D { Geometry = lb.ToLineGeometry3D(), Color = Colors.Cyan, Thickness = 2 });

        _handlesGroup.Children.Clear();
        for (int i = 0; i < NH; i++)
        {
            var col = new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f);
            _hm[i] = Sphere(_hp[i], 1.0f, col);
            _handlesGroup.Children.Add(_hm[i]);
        }
    }

    // -- Buttons -----------------------------------------------------------------------

    private void Next_Click(object s, RoutedEventArgs e)
    {
        StepTitle.Text = "LeFort 1 -- 2-Piece: Adjust Plane & Cut";
        StepInstructions.Text = "Drag cyan handles to adjust extent. Click Perform Cut when ready.";
        NextBtn.Visibility = Visibility.Collapsed;
        CutBtn.Visibility  = Visibility.Visible;
        CutBtn.IsEnabled   = true;
    }

    private async void Cut_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "True-slicing Le Fort 1 2-piece sagittal... (may take a moment)";
        Cursor = Cursors.Wait;
        CutBtn.IsEnabled = false;

        var maxillaVerts = _maxillaVerts;
        float[] F(Point3D p) => new float[] { (float)p.X, (float)p.Y, (float)p.Z };

        var polyplane = new Polyplane(0.0);
        polyplane.SetMeshFromQuads(new List<(float[], float[], float[], float[])>{
            // Anterior section
            (F(_hp[0]), F(_hp[4]), F(_hp[5]), F(_hp[1])),
            // Middle section
            (F(_hp[4]), F(_hp[6]), F(_hp[7]), F(_hp[5])),
            // Posterior section
            (F(_hp[6]), F(_hp[2]), F(_hp[3]), F(_hp[7]))
        });

        List<float[]> L, R;
        try
        {
            (R, L) = await System.Threading.Tasks.Task.Run(() =>
            {
                // Reference: highest-Z vertex = superior = right of mid-sagittal (arbitrary;
                // parity will correctly assign each side regardless of which we call "above")
                double bestZ = double.MinValue;
                double[] crRef = { 0, 0, 0 };
                foreach (var v in maxillaVerts)
                    if (v[2] > bestZ) { bestZ = v[2]; crRef = new double[]{ v[0], v[1], v[2] }; }

                // TrueSliceByPolyplane: "above" = same parity as crRef = R, "below" = L
                var (above, below) = MeshOps.TrueSliceByPolyplane(
                    maxillaVerts, polyplane, crRef, capEnds: true);
                return (above, below);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cut failed: {ex.Message}";
            CutBtn.IsEnabled = true;
            Cursor = Cursors.Arrow;
            return;
        }

        LeftResult  = L;
        RightResult = R;
        MainGroup.Children.Remove(_boneMesh);
        // Add opaque bone segments first
        MainGroup.Children.Add(MakeMesh(L, Color.FromRgb(100, 200, 255), 1.0));
        MainGroup.Children.Add(MakeMesh(R, Color.FromRgb(120, 220, 210), 1.0));
        // Move transparent plane visuals to end for proper transparency
        foreach (var g in new object[]{ _planeMesh, _linesGroup, _handlesGroup })
        {
            if (g is Element3D el && MainGroup.Children.Contains(el))
            { MainGroup.Children.Remove(el); MainGroup.Children.Add(el); }
        }
        AcceptBtn.Visibility = Visibility.Visible;
        CutBtn.IsEnabled = false;
        StatusText.Text = $"Done -- L: {L.Count/3} tris | R: {R.Count/3} tris";
        Cursor = Cursors.Arrow;
    }


    private void Clear_Click(object s, RoutedEventArgs e)
    {
        _ctrl.Clear();
        foreach (var v in _ctrlVis) MainGroup.Children.Remove(v);
        _ctrlVis.Clear();
        _planeVisible = false; _planeMesh.Geometry = null;
        _linesGroup.Children.Clear(); _handlesGroup.Children.Clear();
        StepTitle.Text = "LeFort 1 -- 2-Piece: Place 2 Vestibular Points";
        StepInstructions.Text = "Left-click on the vestibular surface to place 2 points.";
        NextBtn.Visibility = Visibility.Visible; NextBtn.IsEnabled = false;
        CutBtn.Visibility = Visibility.Collapsed; CutBtn.IsEnabled = false;
        AcceptBtn.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void Accept_Click(object s, RoutedEventArgs e) { Accepted = true; DialogResult = true; Close(); }
    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }

    // -- Helpers -----------------------------------------------------------------------

    private bool HitSphere(Point screenPos, Point3D center, double radius)
    {
        var hits = MainViewport.FindHits(screenPos);
        return hits.Any(h => {
            if (h.ModelHit is not MeshGeometryModel3D) return false;
            var hp = new Point3D(h.PointHit.X, h.PointHit.Y, h.PointHit.Z);
            return Dist(hp, center) < radius + 2.0;
        });
    }

    private Point3D? RaycastBone(Point pos)
    {
        var h = MainViewport.FindHits(pos).FirstOrDefault(x => x.ModelHit == _boneMesh);
        return h == null ? null : new(h.PointHit.X, h.PointHit.Y, h.PointHit.Z);
    }

    private Point3D? CamPlane(Point pos, Point3D anchor)
    {
        var look = SubCamera.LookDirection; look.Normalize();
        var pn = new Vector3D(-look.X, -look.Y, -look.Z);
        var ray = MainViewport.UnProject(pos);
        double nd = pn.X * ray.Direction.X + pn.Y * ray.Direction.Y + pn.Z * ray.Direction.Z;
        if (Math.Abs(nd) < 0.0001) return null;
        double t = (pn.X * (anchor.X - ray.Position.X) + pn.Y * (anchor.Y - ray.Position.Y) + pn.Z * (anchor.Z - ray.Position.Z)) / nd;
        return t < 0 ? null : new(ray.Position.X + t * ray.Direction.X, ray.Position.Y + t * ray.Direction.Y, ray.Position.Z + t * ray.Direction.Z);
    }

    private void FitCamera(List<float[]> v)
    {
        if (v == null || v.Count == 0 || MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        double mnX=9e9,mnY=9e9,mnZ=9e9,mxX=-9e9,mxY=-9e9,mxZ=-9e9;
        foreach (var u in v){if(u[0]<mnX)mnX=u[0];if(u[0]>mxX)mxX=u[0];if(u[1]<mnY)mnY=u[1];if(u[1]>mxY)mxY=u[1];if(u[2]<mnZ)mnZ=u[2];if(u[2]>mxZ)mxZ=u[2];}
        var c = new Point3D((mnX+mxX)/2,(mnY+mxY)/2,(mnZ+mxZ)/2);
        double dist = Math.Sqrt(Math.Pow(mxX-mnX,2)+Math.Pow(mxY-mnY,2)+Math.Pow(mxZ-mnZ,2))*1.2;
        cam.Position=new(c.X,c.Y-dist,c.Z); cam.LookDirection=new(0,dist,0); cam.UpDirection=new(0,0,1);
        MainViewport.FixedRotationPointEnabled=true; MainViewport.FixedRotationPoint=c;
    }

    private MeshGeometryModel3D MakeMesh(List<float[]> verts, Color col, double opacity)
    {
        var b = new HelixToolkit.Geometry.MeshBuilder();
        for (int i = 0; i < verts.Count; i += 3)
            if (i+2 < verts.Count) b.AddTriangle(V3(verts[i]), V3(verts[i+1]), V3(verts[i+2]));
        return new MeshGeometryModel3D {
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),
            Material = new PhongMaterial { DiffuseColor = new(col.R/255f, col.G/255f, col.B/255f, (float)opacity) } };
    }

    private MeshGeometryModel3D Sphere(Point3D pt, float r, HelixToolkit.Maths.Color4 col)
    {
        var b = new HelixToolkit.Geometry.MeshBuilder();
        b.AddSphere(new System.Numerics.Vector3(0,0,0), r);
        return new MeshGeometryModel3D {
            Geometry  = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),
            Material  = new PhongMaterial { DiffuseColor=col, SpecularShininess=32f },
            Transform = new TranslateTransform3D(pt.X, pt.Y, pt.Z) };
    }

    private static double Dist(Point3D a, Point3D b) { double dx=a.X-b.X,dy=a.Y-b.Y,dz=a.Z-b.Z; return Math.Sqrt(dx*dx+dy*dy+dz*dz); }
    private static System.Numerics.Vector3 V3(float[] v) => new(v[0], v[1], v[2]);
    private static System.Numerics.Vector3 Nv3f(float x, float y, float z) => new(x, y, z);
}
