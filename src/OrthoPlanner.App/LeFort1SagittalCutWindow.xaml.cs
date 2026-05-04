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

    // Plane bounds (world units)
    // X = mediolateral split position
    // Y = anterior(front) → posterior(back) dimension
    // Z = inferior(bot)  → superior(top) dimension
    private double _xMid, _yFront, _yBack, _zTop, _zBot;
    // Two AP-direction section dividers (vertical lines on the plane)
    private double _yDiv1, _yDiv2;

    // 8 handles:
    //  [0] FrontTop  [1] FrontBot   – anterior edge corners  (cyan)
    //  [2] BackTop   [3] BackBot    – posterior edge corners  (cyan)
    //  [4] Div1Top   [5] Div1Bot    – first  divider          (orange)
    //  [6] Div2Top   [7] Div2Bot    – second divider          (orange)
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

    // ── Mouse ──────────────────────────────────────────────────────────────────

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
            var pt = RayOnPlaneX(pos, _xMid);
            if (!pt.HasValue) return;
            double ny = pt.Value.Y, nz = pt.Value.Z;
            switch (_dragH)
            {
                // Anterior edge corners: control yFront + zTop/zBot
                case 0: _yFront = ny; _zTop = nz; break;
                case 1: _yFront = ny; _zBot = nz; break;
                // Posterior edge corners: control yBack + zTop/zBot
                case 2: _yBack = ny; _zTop = nz; break;
                case 3: _yBack = ny; _zBot = nz; break;
                // Divider 1 (drag Y only)
                case 4: case 5: _yDiv1 = ny; break;
                // Divider 2 (drag Y only)
                case 6: case 7: _yDiv2 = ny; break;
            }
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

    // ── Control point placement ────────────────────────────────────────────────

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
        _xMid   = (p0.X + p1.X) / 2.0;
        _yFront = Math.Min(p0.Y, p1.Y) - 15.0;
        _yBack  = Math.Max(p0.Y, p1.Y) + 50.0;
        _zTop   = Math.Max(p0.Z, p1.Z) + 20.0;
        _zBot   = Math.Min(p0.Z, p1.Z) - 20.0;
        double span = _yBack - _yFront;
        _yDiv1  = _yFront + span / 3.0;
        _yDiv2  = _yFront + 2.0 * span / 3.0;
        _planeVisible = true;
        RebuildPlane();
    }

    private void ClampBounds()
    {
        if (_yFront > _yBack - 10) _yFront = _yBack - 10;
        if (_zBot > _zTop - 5)     _zBot   = _zTop - 5;
        _yDiv1 = Math.Clamp(_yDiv1, _yFront + 1, _yBack - 1);
        _yDiv2 = Math.Clamp(_yDiv2, _yDiv1  + 1, _yBack - 1);
    }

    // ── Plane rendering ────────────────────────────────────────────────────────

    private void RebuildPlane()
    {
        float x   = (float)_xMid;
        float yf  = (float)_yFront, yd1 = (float)_yDiv1, yd2 = (float)_yDiv2, yb = (float)_yBack;
        float zt  = (float)_zTop, zb = (float)_zBot;

        // Flat plane: 3 sections side-by-side in the AP (Y) direction.
        // Dividers are VERTICAL lines running top-to-bottom.
        var mb = new HelixToolkit.Geometry.MeshBuilder();
        Action<float, float> addSection = (ya, yc) => {
            var TF = Nv3f(x, ya, zt); var TB = Nv3f(x, yc, zt);
            var BF = Nv3f(x, ya, zb); var BB = Nv3f(x, yc, zb);
            mb.AddTriangle(TF, TB, BB); mb.AddTriangle(TF, BB, BF);  // front winding
            mb.AddTriangle(TF, BB, TB); mb.AddTriangle(TF, BF, BB);  // back winding
        };
        addSection(yf,  yd1);  // anterior section
        addSection(yd1, yd2);  // middle section
        addSection(yd2, yb);   // posterior section

        _planeMesh.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh());
        _planeMesh.Material = new PhongMaterial {
            DiffuseColor  = new HelixToolkit.Maths.Color4(0f, 0.9f, 1f, 0.18f),
            EmissiveColor = new HelixToolkit.Maths.Color4(0f, 0.8f, 1f, 0.12f) };

        // Outline + 2 vertical divider lines
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(Nv3f(x, yf,  zt), Nv3f(x, yb,  zt));  // top edge    (anterior→posterior)
        lb.AddLine(Nv3f(x, yb,  zt), Nv3f(x, yb,  zb));  // back edge   (top→bottom)
        lb.AddLine(Nv3f(x, yb,  zb), Nv3f(x, yf,  zb));  // bottom edge (posterior→anterior)
        lb.AddLine(Nv3f(x, yf,  zb), Nv3f(x, yf,  zt));  // front edge  (bottom→top)
        lb.AddLine(Nv3f(x, yd1, zt), Nv3f(x, yd1, zb));  // divider 1   (vertical)
        lb.AddLine(Nv3f(x, yd2, zt), Nv3f(x, yd2, zb));  // divider 2   (vertical)
        _linesGroup.Children.Clear();
        _linesGroup.Children.Add(new LineGeometryModel3D { Geometry = lb.ToLineGeometry3D(), Color = Colors.Cyan, Thickness = 2 });

        // Place handles
        _hp[0] = new(_xMid, _yFront, _zTop);  _hp[1] = new(_xMid, _yFront, _zBot);
        _hp[2] = new(_xMid, _yBack,  _zTop);  _hp[3] = new(_xMid, _yBack,  _zBot);
        _hp[4] = new(_xMid, _yDiv1,  _zTop);  _hp[5] = new(_xMid, _yDiv1,  _zBot);
        _hp[6] = new(_xMid, _yDiv2,  _zTop);  _hp[7] = new(_xMid, _yDiv2,  _zBot);
        _handlesGroup.Children.Clear();
        for (int i = 0; i < NH; i++)
        {
            var col = new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f); // all handles = cyan
            _hm[i] = Sphere(_hp[i], 1.0f, col);
            _handlesGroup.Children.Add(_hm[i]);
        }
    }

    // ── Buttons ────────────────────────────────────────────────────────────────

    private void Next_Click(object s, RoutedEventArgs e)
    {
        StepTitle.Text = "LeFort 1 — 2-Piece: Adjust Plane & Cut";
        StepInstructions.Text = "Drag cyan handles to adjust extent, orange handles to slide section dividers (AP). Click Perform Cut when ready.";
        NextBtn.Visibility = Visibility.Collapsed;
        CutBtn.Visibility  = Visibility.Visible;
        CutBtn.IsEnabled   = true;
    }

    private void Cut_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "Cutting..."; Cursor = Cursors.Wait;
        try
        {
            int nTri = _maxillaVerts.Count / 3;
            var L = new List<float[]>(); var R = new List<float[]>();

            // Classify every triangle by which side of X = _xMid its centroid falls.
            // Simple centroid test works for both connected and disconnected components.
            for (int i = 0; i < nTri; i++)
            {
                double cx = (_maxillaVerts[i*3][0] + _maxillaVerts[i*3+1][0] + _maxillaVerts[i*3+2][0]) / 3.0;
                var tgt = cx >= _xMid ? R : L;
                tgt.Add(_maxillaVerts[i*3]); tgt.Add(_maxillaVerts[i*3+1]); tgt.Add(_maxillaVerts[i*3+2]);
            }

            LeftResult = L; RightResult = R;
            MainGroup.Children.Remove(_boneMesh);
            MainGroup.Children.Add(MakeMesh(L, Color.FromRgb(100, 200, 255), 1.0));   // left  = blue
            MainGroup.Children.Add(MakeMesh(R, Color.FromRgb(120, 220, 210), 1.0));   // right = teal (original bone colour)
            AcceptBtn.Visibility = Visibility.Visible;
            CutBtn.IsEnabled = false;
            StatusText.Text = $"Cut complete — L: {L.Count/3} / R: {R.Count/3} triangles. Accept or Clear to redo.";
        }
        finally { Cursor = Cursors.Arrow; }
    }

    private void Clear_Click(object s, RoutedEventArgs e)
    {
        _ctrl.Clear();
        foreach (var v in _ctrlVis) MainGroup.Children.Remove(v);
        _ctrlVis.Clear();
        _planeVisible = false; _planeMesh.Geometry = null;
        _linesGroup.Children.Clear(); _handlesGroup.Children.Clear();
        StepTitle.Text = "LeFort 1 — 2-Piece: Place 2 Vestibular Points";
        StepInstructions.Text = "Left-click on the vestibular surface to place 2 points.";
        NextBtn.Visibility = Visibility.Visible; NextBtn.IsEnabled = false;
        CutBtn.Visibility = Visibility.Collapsed; CutBtn.IsEnabled = false;
        AcceptBtn.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void Accept_Click(object s, RoutedEventArgs e) { Accepted = true; DialogResult = true; Close(); }
    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }

    // ── Helpers ────────────────────────────────────────────────────────────────

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

    private Point3D? RayOnPlaneX(Point screenPos, double xPlane)
    {
        var ray = MainViewport.UnProject(screenPos);
        double dx = ray.Direction.X;
        if (Math.Abs(dx) < 0.0001) return null;
        double t = (xPlane - ray.Position.X) / dx;
        return t < 0 ? null : new(xPlane, ray.Position.Y + t * ray.Direction.Y, ray.Position.Z + t * ray.Direction.Z);
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
