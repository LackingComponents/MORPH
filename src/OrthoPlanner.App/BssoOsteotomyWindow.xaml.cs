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
    private List<float[]> _halfDisplayVerts = new();
    public bool IsLeftSide => _isLeftSide;
    public List<float[]> ProximalResult { get; private set; } = new();
    public List<float[]> DistalResult   { get; private set; } = new();
    public bool Accepted { get; private set; } = false;

    private int  _step = 0;
    private bool _isLeftSide = true;
    private float _latDirX = 1f;
    private float _medDirX = -1f;

    private List<Point3D> _rawLingual = new();
    private List<Point3D> _rawBuccal  = new();
    private List<MeshGeometryModel3D> _rawDots = new();

    // lc[0]=lAnt(surf ant), lc[1]=lPost(surf post), lc[2]=lPost+medX, lc[3]=lAnt+medX
    private Point3D[] _lc = new Point3D[4];
    // bc[0]=bSup(surf sup), bc[1]=bInf(surf inf), bc[2]=bInf+latX, bc[3]=bSup+latX
    private Point3D[] _bc = new Point3D[4];

    // sagTop[0]=lPost, sagTop[1]=lAnt, sagTop[2]=sagMid
    private Point3D[] _sagTop = new Point3D[3];
    // sagBot[0]=lPostBot, sagBot[1]=lAntBot
    private Point3D[] _sagBot = new Point3D[2];
    private Point3D _postArmTip;

    private MeshGeometryModel3D[] _lHandles = new MeshGeometryModel3D[4];
    private MeshGeometryModel3D[] _bHandles = new MeshGeometryModel3D[4];
    private MeshGeometryModel3D?  _sagMidH;
    private MeshGeometryModel3D[] _sagBotH  = new MeshGeometryModel3D[2];
    private MeshGeometryModel3D?  _postH;     // kept for Clear() cleanup compat, not created
    private MeshGeometryModel3D?  _armBotH;  // new: inferior-medial corner of posterior arm
    private Point3D               _armBot;   // stored position of that corner

    private MeshGeometryModel3D _boneMesh;
    private MeshGeometryModel3D _hoveredHalf;
    private GroupModel3D _lingualVis  = new();
    private GroupModel3D _sagittalVis = new();
    private GroupModel3D _postArmVis  = new();
    private GroupModel3D _buccalVis   = new();

    private MeshGeometryModel3D? _dragging;
    private int _dragGroup = -1, _dragIdx = -1;
    private Point3D  _dragPlanePos;
    private Vector3D _dragPlaneNormal;

    private const float ExtLat = 10f;
    private const float ExtInf = 10f;  // kept as fallback; sagBot Z is now driven by bInf.Z
    private const float ArmExt = 25f;

    private static readonly HelixToolkit.Maths.Color4 CyanFill = new(0f, 1f, 1f, 0.35f);

    public BssoOsteotomyWindow(List<float[]> mandibleVerts)
    {
        InitializeComponent();
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        CompositionTarget.Rendering += (_, _) => {
            var d = SubCamera.LookDirection;
            if (d.Length > 0.001) { d.Normalize(); Headlamp.Direction = new Vector3D(-d.X,-d.Y,-d.Z); Backlamp.Direction = new Vector3D(d.X,d.Y,d.Z); }
        };
        _mandibleVerts = mandibleVerts;
        _boneMesh = MkBone(_mandibleVerts, new HelixToolkit.Maths.Color4(245/255f,230/255f,200/255f,1f));
        MainGroup.Children.Add(_boneMesh);
        _hoveredHalf = new MeshGeometryModel3D {
            Material = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(0f,0.8f,1f,0.30f) },
            CullMode = SharpDX.Direct3D11.CullMode.None,
            IsTransparent = true
        };
        MainGroup.Children.Add(_hoveredHalf);
        foreach (var g in new GroupModel3D[]{_lingualVis,_sagittalVis,_postArmVis,_buccalVis})
            MainGroup.Children.Add(g);
        Loaded += (_, _) => CenterOn(_mandibleVerts);
    }

    // ── Side selection ──────────────────────────────────────────────────────
    private void LeftOverlay_MouseEnter(object s, MouseEventArgs e)  { if(_step!=0)return; HiHalf(true);  OvH(true,true);  }
    private void RightOverlay_MouseEnter(object s, MouseEventArgs e) { if(_step!=0)return; HiHalf(false); OvH(false,true); }
    private void SideOverlay_MouseLeave(object s, MouseEventArgs e)  { if(_step!=0)return; _hoveredHalf.Geometry=null; OvH(true,false); OvH(false,false); }
    private void LeftOverlay_Click(object s, MouseButtonEventArgs e)  { if(_step!=0)return; DoSelectSide(true);  }
    private void RightOverlay_Click(object s, MouseButtonEventArgs e) { if(_step!=0)return; DoSelectSide(false); }

    private void HiHalf(bool left) {
        float mx = _mandibleVerts.Count>0 ? _mandibleVerts.Average(v=>v[0]) : 0f;
        var h = HalfV(left,mx);
        var b = new HelixToolkit.Geometry.MeshBuilder();
        for(int i=0;i+2<h.Count;i+=3) b.AddTriangle(Nv(h[i]),Nv(h[i+1]),Nv(h[i+2]));
        _hoveredHalf.Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh());
    }
    private void OvH(bool left, bool show) {
        var brd = (FindName(left?"LeftOverlay":"RightOverlay") as System.Windows.Controls.Border)!;
        var lbl = (FindName(left?"LeftLabel":"RightLabel") as System.Windows.Controls.TextBlock)!;
        brd.Background = show ? new SolidColorBrush(Color.FromArgb(40,0,220,255)) : Brushes.Transparent;
        lbl.Opacity = show ? 1 : 0;
    }
    private void DoSelectSide(bool left) {
        _isLeftSide = left;
        // Hardcode medial/lateral X direction from side — immune to unusual anatomy
        _latDirX = left ? -1f : 1f;   // lateral  (buccal) direction
        _medDirX = -_latDirX;          // medial (lingual) direction
        float mx = _mandibleVerts.Count>0 ? _mandibleVerts.Average(v=>v[0]) : 0f;
        _halfDisplayVerts = HalfV(left, mx);
        MainGroup.Children.Remove(_boneMesh);
        _boneMesh = MkBone(_halfDisplayVerts, new HelixToolkit.Maths.Color4(245/255f,230/255f,200/255f,1f));
        MainGroup.Children.Add(_boneMesh);
        _hoveredHalf.Geometry = null;
        (FindName("SideOverlay") as System.Windows.Controls.Grid)!.Visibility = Visibility.Collapsed;
        _step = 1; string sd = left?"Left":"Right";
        StepTitle.Text = $"BSSO ({sd}): Step 1 – Lingual Points";
        StepInstructions.Text = "Click 2 points on the LINGUAL (medial) cortex of the ramus.";
        StatusText.Text = "Place 2 lingual points…";
        NextBtn.Visibility=Visibility.Visible; NextBtn.IsEnabled=false; ClearBtn.Visibility=Visibility.Visible;
        // Auto-orient to lingual (medial) surface
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => {
            CenterOn(_halfDisplayVerts);
            LookFromSide(false);
        });
    }

    // ── Mouse ───────────────────────────────────────────────────────────────
    private void Viewport_PreviewMouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if(Accepted||_step==0) return;
        var hit = HitTest(e.GetPosition(MainViewport)); if(hit==null) return;
        if(_step==3 && TryDrag(hit.Value.Visual, hit.Value.Point)) { e.Handled=true; return; }
        if(hit.Value.Visual != _boneMesh) return;
        var pt = hit.Value.Point;

        if(_step==1 && _rawLingual.Count<2) {
            _rawLingual.Add(pt);
            var d=Sph(pt,1.5f); _rawDots.Add(d); MainGroup.Children.Add(d);
            if(_rawLingual.Count==2) { NextBtn.IsEnabled=true; StatusText.Text="2 lingual points. Click Next."; }
            e.Handled=true;
        } else if(_step==2 && _rawBuccal.Count<2) {
            _rawBuccal.Add(pt);
            var d=Sph(pt,1.5f); _rawDots.Add(d); MainGroup.Children.Add(d);
            if(_rawBuccal.Count==2) { ComputeAllCorners(); NextBtn.IsEnabled=true; StatusText.Text="2 buccal points. Click Next."; }
            e.Handled=true;
        }
    }

    private void Viewport_PreviewMouseMove(object s, MouseEventArgs e)
    {
        if(_dragging==null) return;
        var ray = MainViewport.UnProject(e.GetPosition(MainViewport));
        var ip = RayPl(new Point3D(ray.Position.X,ray.Position.Y,ray.Position.Z),
                       new Vector3D(ray.Direction.X,ray.Direction.Y,ray.Direction.Z),
                       _dragPlanePos, _dragPlaneNormal);
        if(ip==null) return; var np = ip.Value;

        switch(_dragGroup) {
            case 0: // lingual
                _lc[_dragIdx] = np; _lHandles[_dragIdx].Transform = Tt(np);
                if(_dragIdx==0) _sagTop[1]=np;
                else if(_dragIdx==1) _sagTop[0]=np;
                else if(_dragIdx==2) {
                    // _lc[2] is hinged to _postArmTip — keep in sync, drag armBot X/Y with it
                    _postArmTip = np;
                    _armBot = new Point3D(np.X, np.Y, _armBot.Z);
                    if(_armBotH!=null) _armBotH.Transform = Tt(_armBot);
                }
                break;
            case 1: // buccal
                _bc[_dragIdx] = np; _bHandles[_dragIdx].Transform = Tt(np);
                break;
            case 2: // sagMidTop
                _sagTop[2] = np; _sagMidH!.Transform = Tt(np);
                break;
            case 3: // sagBot[i]
                _sagBot[_dragIdx] = np; _sagBotH[_dragIdx].Transform = Tt(np);
                break;
            case 5: // armBot (inferior-medial corner of posterior arm)
                _armBot = np; _armBotH!.Transform = Tt(np);
                break;
        }
        RebuildPlanes();
    }

    private void Viewport_PreviewMouseLeftButtonUp(object s, MouseButtonEventArgs e)
    { if(_dragging!=null) { _dragging=null; MainViewport.ReleaseMouseCapture(); } }

    private bool TryDrag(Element3D? vis, Point3D hit)
    {
        var ld = MainViewport.Camera?.LookDirection ?? new Vector3D(0,0,-1);
        var pn = new Vector3D(-ld.X,-ld.Y,-ld.Z);
        for(int i=0;i<4;i++) if(_lHandles[i]==vis||Dist(hit,_lc[i])<5) return SD(_lHandles[i],0,i,_lc[i],pn);
        for(int i=0;i<4;i++) if(_bHandles[i]==vis||Dist(hit,_bc[i])<5) return SD(_bHandles[i],1,i,_bc[i],pn);
        if(_sagMidH!=null&&(_sagMidH==vis||Dist(hit,_sagTop[2])<5)) return SD(_sagMidH,2,0,_sagTop[2],pn);
        for(int i=0;i<2;i++) if(_sagBotH[i]!=null&&(_sagBotH[i]==vis||Dist(hit,_sagBot[i])<5)) return SD(_sagBotH[i],3,i,_sagBot[i],pn);
        if(_armBotH!=null&&(_armBotH==vis||Dist(hit,_armBot)<5)) return SD(_armBotH,5,0,_armBot,pn);
        return false;
    }
    private bool SD(MeshGeometryModel3D h, int g, int i, Point3D pos, Vector3D pn)
    { _dragging=h; _dragGroup=g; _dragIdx=i; _dragPlanePos=pos; _dragPlaneNormal=pn; MainViewport.CaptureMouse(); return true; }

    // ── Geometry setup ──────────────────────────────────────────────────────
    private void ComputeAllCorners()
    {
        var lAnt  = _rawLingual.OrderBy(p => p.Y).First();
        var lPost = _rawLingual.OrderByDescending(p => p.Y).First();
        var bSup  = _rawBuccal.OrderByDescending(p => p.Z).First();
        var bInf  = _rawBuccal.OrderBy(p => p.Z).First();

        // Direction already hardcoded in DoSelectSide; bInf.Z drives sagittal inferior extent
        _sagTop[0] = lPost; _sagTop[1] = lAnt;
        _sagBot[0] = new Point3D(lPost.X, lPost.Y, bInf.Z);
        _sagBot[1] = new Point3D(lAnt.X,  lAnt.Y,  bInf.Z);

        _lc[0] = lAnt;  _lc[1] = lPost;
        _lc[2] = new Point3D(lPost.X + _medDirX*ExtLat, lPost.Y, lPost.Z);
        _lc[3] = new Point3D(lAnt.X  + _medDirX*ExtLat, lAnt.Y,  lAnt.Z);

        _bc[0] = bSup;  _bc[1] = bInf;
        _bc[2] = new Point3D(bInf.X + _latDirX*ExtLat, bInf.Y, bInf.Z);
        _bc[3] = new Point3D(bSup.X + _latDirX*ExtLat, bSup.Y, bSup.Z);

        for(int i=0;i<4;i++) { if(_lHandles[i]!=null) MainGroup.Children.Remove(_lHandles[i]); _lHandles[i]=Sph(_lc[i]); MainGroup.Children.Add(_lHandles[i]); }
        for(int i=0;i<4;i++) { if(_bHandles[i]!=null) MainGroup.Children.Remove(_bHandles[i]); _bHandles[i]=Sph(_bc[i]); MainGroup.Children.Add(_bHandles[i]); }
        RebuildPlanes();
    }

    private void InitSagittal()
    {
        foreach(var d in _rawDots) MainGroup.Children.Remove(d);
        _rawDots.Clear();

        var bSup = _bc[0]; var bInf = _bc[1];
        _sagTop[2] = Lerp(_sagTop[1], bSup, 0.5);

        // Project sagTop[2] down along the buccal inclination vector to reach bInf.Z
        // so that the sagTop[2]→sagBot[1] edge is parallel to the buccal cut plane.
        double bZspan = bInf.Z - bSup.Z;
        if (Math.Abs(bZspan) > 0.001) {
            double scale = (bInf.Z - _sagTop[2].Z) / bZspan;
            _sagBot[1] = new Point3D(
                _sagTop[2].X + scale * (bInf.X - bSup.X),
                _sagTop[2].Y + scale * (bInf.Y - bSup.Y),
                bInf.Z);
        }

        if(_sagMidH!=null) MainGroup.Children.Remove(_sagMidH);
        _sagMidH = Sph(_sagTop[2]); MainGroup.Children.Add(_sagMidH);

        for(int i=0;i<2;i++) {
            if(_sagBotH[i]!=null) MainGroup.Children.Remove(_sagBotH[i]);
            _sagBotH[i] = Sph(_sagBot[i]); MainGroup.Children.Add(_sagBotH[i]);
        }

        // Hinge: _postArmTip coincides with _lc[2] (shared handle, no separate _postH)
        _postArmTip = _lc[2];
        _postH = null; // _lHandles[2] is the shared visual handle

        // New: inferior-medial corner handle
        _armBot = new Point3D(_postArmTip.X, _postArmTip.Y, _sagBot[0].Z);
        if(_armBotH!=null) MainGroup.Children.Remove(_armBotH);
        _armBotH = Sph(_armBot); MainGroup.Children.Add(_armBotH);
        RebuildPlanes();
    }

    // ── Plane rendering ─────────────────────────────────────────────────────
    private void RebuildPlanes()
    {
        if(_lc[0]!=default) BuildGP(_lingualVis, new[]{_lc[0],_lc[1],_lc[2],_lc[3]});
        else _lingualVis.Children.Clear();

        if(_bc[0]!=default) BuildGP(_buccalVis, new[]{_bc[0],_bc[1],_bc[2],_bc[3]});
        else _buccalVis.Children.Clear();

        if(_step<3) { _sagittalVis.Children.Clear(); _postArmVis.Children.Clear(); return; }

        var bSup = _bc[0]; var bInf = _bc[1];
        var mb = new HelixToolkit.Geometry.MeshBuilder();
        var lb = new HelixToolkit.SharpDX.LineBuilder();

        AddQuad(mb, _sagTop[0], _sagTop[1], _sagBot[1], _sagBot[0]); // S1
        AddQuad(mb, _sagTop[1], _sagTop[2], bInf, _sagBot[1]);        // Bridge
        mb.AddTriangle(Nv3(_sagTop[2]),Nv3(bSup),Nv3(bInf)); // CullMode.None handles back-side

        lb.AddLine(Nv3(_sagTop[0]),Nv3(_sagTop[1]));
        lb.AddLine(Nv3(_sagTop[1]),Nv3(_sagTop[2]));
        lb.AddLine(Nv3(_sagTop[2]),Nv3(bSup));
        lb.AddLine(Nv3(bSup),Nv3(bInf));
        lb.AddLine(Nv3(bInf),Nv3(_sagBot[1]));
        lb.AddLine(Nv3(_sagBot[1]),Nv3(_sagBot[0]));
        lb.AddLine(Nv3(_sagBot[0]),Nv3(_sagTop[0]));
        lb.AddLine(Nv3(_sagTop[1]),Nv3(_sagBot[1]));

        _sagittalVis.Children.Clear();
        _sagittalVis.Children.Add(new MeshGeometryModel3D{
            Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),
            Material=new PhongMaterial{DiffuseColor=CyanFill, EmissiveColor=CyanFill},
            CullMode=SharpDX.Direct3D11.CullMode.None,
            IsTransparent=true
        });
        _sagittalVis.Children.Add(new LineGeometryModel3D{Geometry=lb.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=2});

        BuildGP(_postArmVis, new[]{ _sagTop[0], _postArmTip, _armBot, _sagBot[0] });
    }

    private static void AddQuad(HelixToolkit.Geometry.MeshBuilder mb, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        mb.AddTriangle(Nv3(a),Nv3(b),Nv3(c)); mb.AddTriangle(Nv3(a),Nv3(c),Nv3(d));
        // No reverse winding needed: CullMode.None handles back-side visibility
    }

    private void BuildGP(GroupModel3D grp, Point3D[] c)
    {
        grp.Children.Clear();
        var mb = new HelixToolkit.Geometry.MeshBuilder();
        mb.AddTriangle(Nv3(c[0]),Nv3(c[1]),Nv3(c[2])); mb.AddTriangle(Nv3(c[0]),Nv3(c[2]),Nv3(c[3]));
        // No reverse winding needed: CullMode.None handles back-side visibility
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(Nv3(c[0]),Nv3(c[1])); lb.AddLine(Nv3(c[1]),Nv3(c[2]));
        lb.AddLine(Nv3(c[2]),Nv3(c[3])); lb.AddLine(Nv3(c[3]),Nv3(c[0]));
        grp.Children.Add(new MeshGeometryModel3D{
            Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),
            Material=new PhongMaterial{DiffuseColor=CyanFill, EmissiveColor=CyanFill},
            CullMode=SharpDX.Direct3D11.CullMode.None,
            IsTransparent=true
        });
        grp.Children.Add(new LineGeometryModel3D {Geometry=lb.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=2});
    }

    // ── Buttons ─────────────────────────────────────────────────────────────
    private void Next_Click(object s, RoutedEventArgs e)
    {
        string sd = _isLeftSide?"Left":"Right";
        if(_step==1) {
            _step=2; StepTitle.Text=$"BSSO ({sd}): Step 2 – Buccal Points";
            StepInstructions.Text="Click 2 points on the BUCCAL (lateral) cortex (one superior, one inferior).";
            StatusText.Text="Place 2 buccal points…"; NextBtn.IsEnabled=false;
            LookFromSide(true); // rotate to buccal (lateral) view
        } else if(_step==2) {
            _step=3; StepTitle.Text=$"BSSO ({sd}): Step 3 – Adjust";
            StepInstructions.Text="Drag handles to adjust. Click Perform Cut when ready.";
            StatusText.Text="Adjust handles, then cut.";
            NextBtn.Visibility=Visibility.Collapsed; CutBtn.Visibility=Visibility.Visible;
            InitSagittal();
        }
    }

    private void Clear_Click(object s, RoutedEventArgs e)
    {
        _rawLingual.Clear(); _rawBuccal.Clear();
        foreach(var d in _rawDots) MainGroup.Children.Remove(d); _rawDots.Clear();
        for(int i=0;i<4;i++){if(_lHandles[i]!=null){MainGroup.Children.Remove(_lHandles[i]);_lHandles[i]=null!;}}
        for(int i=0;i<4;i++){if(_bHandles[i]!=null){MainGroup.Children.Remove(_bHandles[i]);_bHandles[i]=null!;}}
        for(int i=0;i<2;i++){if(_sagBotH[i]!=null){MainGroup.Children.Remove(_sagBotH[i]);_sagBotH[i]=null!;}}
        if(_sagMidH!=null){MainGroup.Children.Remove(_sagMidH);_sagMidH=null;}
        if(_postH!=null){MainGroup.Children.Remove(_postH);_postH=null;}
        if(_armBotH!=null){MainGroup.Children.Remove(_armBotH);_armBotH=null;}
        _lc=new Point3D[4]; _bc=new Point3D[4]; _sagTop=new Point3D[3]; _sagBot=new Point3D[2];
        _lingualVis.Children.Clear(); _sagittalVis.Children.Clear(); _postArmVis.Children.Clear(); _buccalVis.Children.Clear();
        _step=1; NextBtn.Visibility=Visibility.Visible; NextBtn.IsEnabled=false;
        CutBtn.Visibility=Visibility.Collapsed; AcceptBtn.Visibility=Visibility.Collapsed;
        string sd=_isLeftSide?"Left":"Right";
        StepTitle.Text=$"BSSO ({sd}): Step 1 – Lingual Points";
        StepInstructions.Text="Click 2 points on the LINGUAL (medial) cortex.";
        StatusText.Text="Place 2 lingual points…";
    }

    private async void Cut_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "True-slicing osteotomy… (may take a moment)";
        Cursor = Cursors.Wait;
        CutBtn.IsEnabled = false;

        // Snapshot all WPF state needed by the background thread
        var mandibleVerts = _mandibleVerts;
        var lc     = (System.Windows.Media.Media3D.Point3D[])_lc.Clone();
        var bc     = (System.Windows.Media.Media3D.Point3D[])_bc.Clone();
        var sagTop = (System.Windows.Media.Media3D.Point3D[])_sagTop.Clone();
        var sagBot = (System.Windows.Media.Media3D.Point3D[])_sagBot.Clone();

        List<float[]> proximal, distal;
        try
        {
            (proximal, distal) = await System.Threading.Tasks.Task.Run(() =>
            {
                // ── Step 1: Pre-filter operated side vs. contralateral ─────────────
                // The contralateral side receives no osteotomy; it goes straight to Distal.
                float midX    = mandibleVerts.Count > 0 ? mandibleVerts.Average(v => v[0]) : 0f;
                float cutCX   = (float)((lc[0].X + lc[1].X + bc[0].X + bc[1].X) / 4.0);
                float cutSide = Math.Sign(cutCX - midX);

                var operated = new List<float[]>();
                var other    = new List<float[]>();
                for (int i = 0; i + 2 < mandibleVerts.Count; i += 3)
                {
                    float cx = (mandibleVerts[i][0] + mandibleVerts[i+1][0] + mandibleVerts[i+2][0]) / 3f;
                    bool isOperated = Math.Sign(cx - midX) == cutSide || cx == midX;
                    var bucket = isOperated ? operated : other;
                    bucket.Add(mandibleVerts[i]);
                    bucket.Add(mandibleVerts[i+1]);
                    bucket.Add(mandibleVerts[i+2]);
                }

                // ── Step 2: Compute the 3 BSSO cutting-plane equations ─────────────
                //   Plane equation: nx·x + ny·y + nz·z + d = 0
                //   Each plane is defined by 3 anatomical points; the normal direction
                //   is determined by the cross-product of two edge vectors in that plane.
                (double nx, double ny, double nz, double d) PlaneEq(
                    System.Windows.Media.Media3D.Point3D p0,
                    System.Windows.Media.Media3D.Point3D p1,
                    System.Windows.Media.Media3D.Point3D p2)
                {
                    double ax = p1.X-p0.X, ay = p1.Y-p0.Y, az = p1.Z-p0.Z;
                    double bx = p2.X-p0.X, by = p2.Y-p0.Y, bz = p2.Z-p0.Z;
                    double nx_ = ay*bz - az*by, ny_ = az*bx - ax*bz, nz_ = ax*by - ay*bx;
                    double len = Math.Sqrt(nx_*nx_ + ny_*ny_ + nz_*nz_);
                    if (len < 1e-9) return (0, 0, 1, 0);
                    nx_ /= len; ny_ /= len; nz_ /= len;
                    return (nx_, ny_, nz_, -(nx_*p0.X + ny_*p0.Y + nz_*p0.Z));
                }

                var planes = new[]
                {
                    // Lingual cortex plate cut — horizontal, along the medial cortex of the ramus
                    PlaneEq(lc[0], lc[1], lc[2]),
                    // Buccal cortex plate cut — angled, along the lateral cortex of the ramus
                    PlaneEq(bc[0], bc[1], bc[2]),
                    // Sagittal marrow split — roughly vertical, separating ramus from body
                    PlaneEq(sagTop[0], sagTop[1], sagBot[0]),
                };

                // ── Step 3: True multi-plane triangle slicing ──────────────────────
                //   MeshPlaneCut (geometry3Sharp) slices every straddling triangle
                //   at its exact intersection edge and caps the resulting open boundary
                //   loops with flat fills — creating proper closed meshes on both sides.
                //   NOTE: These are infinite planes; the condyle-seed classification
                //   in Step 4 correctly handles any spurious extra components produced
                //   by the planes extending beyond the anatomical cut extent.
                var components = OrthoPlanner.Core.Geometry.MeshOps.TrueSliceByMultiplePlanes(
                    operated, planes, capEnds: true);

                // ── Step 4: Identify the condyle component ─────────────────────────
                //   The ramus/condyle fragment has the highest mean (Y+Z) centroid:
                //   Y is posterior (condyle is the most posterior point of the mandible)
                //   Z is superior  (condyle is the highest point on the operated side)
                int condyleIdx = -1; double bestScore = double.MinValue;
                for (int ci = 0; ci < components.Count; ci++)
                {
                    var m = components[ci].Mesh;
                    if (m.Count == 0) continue;
                    double sum = 0;
                    foreach (var v in m) sum += v[1] + v[2];   // Y + Z per vertex
                    double avg = sum / m.Count;
                    if (avg > bestScore) { bestScore = avg; condyleIdx = ci; }
                }

                // ── Step 5: Classify all components into Proximal / Distal ─────────
                //   • Condyle component                           → Proximal (ramus)
                //   • Components on same lingual+sagittal side    → Proximal
                //     (isolated cortical fragments that stayed on the ramus side)
                //   • All remaining components + contralateral    → Distal
                bool[]? condAbove = condyleIdx >= 0
                    ? components[condyleIdx].AbovePlanes
                    : null;

                var prox = new List<float[]>();
                var dist = new List<float[]>();

                for (int ci = 0; ci < components.Count; ci++)
                {
                    var comp = components[ci];
                    bool toProximal = (ci == condyleIdx);

                    if (!toProximal && condAbove != null)
                    {
                        // A component is still part of the ramus if it sits on the
                        // same side of both the lingual and the sagittal planes.
                        bool sameLingual  = comp.AbovePlanes[0] == condAbove[0];
                        bool sameSagittal = comp.AbovePlanes[2] == condAbove[2];
                        toProximal = sameLingual && sameSagittal;
                    }

                    (toProximal ? prox : dist).AddRange(comp.Mesh);
                }

                dist.AddRange(other);   // contralateral half always Distal
                return (prox, dist);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cut failed: {ex.Message}";
            CutBtn.IsEnabled = true;
            Cursor = Cursors.Arrow;
            return;
        }

        ProximalResult = proximal;
        DistalResult   = distal;
        MainGroup.Children.Remove(_boneMesh);
        MainGroup.Children.Add(MkBone(ProximalResult, new HelixToolkit.Maths.Color4(120/255f, 160/255f, 240/255f, 1f)));
        MainGroup.Children.Add(MkBone(DistalResult,   new HelixToolkit.Maths.Color4(220/255f, 140/255f, 120/255f, 1f)));
        _lingualVis.Children.Clear(); _sagittalVis.Children.Clear();
        _postArmVis.Children.Clear(); _buccalVis.Children.Clear();
        AcceptBtn.Visibility = Visibility.Visible;
        CutBtn.Visibility    = Visibility.Collapsed;
        StatusText.Text = $"Done — Ramus (blue): {proximal.Count/3} tris | Mandible (red): {distal.Count/3} tris";
        Cursor = Cursors.Arrow;
    }

    private void Accept_Click(object s, RoutedEventArgs e) { Accepted=true; DialogResult=true; Close(); }


    private void Cancel_Click(object s, RoutedEventArgs e) { DialogResult=false; Close(); }

    // ── Utilities ────────────────────────────────────────────────────────────
    private MeshGeometryModel3D MkBone(List<float[]> v, HelixToolkit.Maths.Color4 c) {
        var b = new HelixToolkit.Geometry.MeshBuilder();
        for(int i=0;i+2<v.Count;i+=3) b.AddTriangle(Nv(v[i]),Nv(v[i+1]),Nv(v[i+2]));
        return new MeshGeometryModel3D{ Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()), Material=new PhongMaterial{DiffuseColor=c} };
    }
    private MeshGeometryModel3D Sph(Point3D c, float r=2.2f) {
        var b = new HelixToolkit.Geometry.MeshBuilder(); b.AddSphere(new System.Numerics.Vector3(0,0,0),r);
        return new MeshGeometryModel3D{ Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()), Material=new PhongMaterial{DiffuseColor=new HelixToolkit.Maths.Color4(0f,1f,1f,1f), SpecularColor=new HelixToolkit.Maths.Color4(0.8f,0.8f,0.8f,1f), SpecularShininess=32f}, Transform=Tt(c) };
    }
    private static TranslateTransform3D Tt(Point3D p) => new(p.X,p.Y,p.Z);
    private static System.Numerics.Vector3 Nv(float[] v) => new(v[0],v[1],v[2]);
    private static System.Numerics.Vector3 Nv3(Point3D p) => new((float)p.X,(float)p.Y,(float)p.Z);
    private static float[] Fv(Point3D p) => new float[]{(float)p.X,(float)p.Y,(float)p.Z};
    private static double Dist(Point3D a,Point3D b){double dx=a.X-b.X,dy=a.Y-b.Y,dz=a.Z-b.Z;return Math.Sqrt(dx*dx+dy*dy+dz*dz);}
    private static Point3D Lerp(Point3D a,Point3D b,double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
    private static string VKey(float[] v) => $"{Math.Round(v[0],1)},{Math.Round(v[1],1)},{Math.Round(v[2],1)}";

    private List<float[]> HalfV(bool left, float mx) {
        var r = new List<float[]>();
        for(int i=0;i+2<_mandibleVerts.Count;i+=3){
            float ax=(_mandibleVerts[i][0]+_mandibleVerts[i+1][0]+_mandibleVerts[i+2][0])/3f;
            if((left&&ax<mx)||(!left&&ax>=mx)){r.Add(_mandibleVerts[i]);r.Add(_mandibleVerts[i+1]);r.Add(_mandibleVerts[i+2]);}
        }
        return r;
    }
    private (Point3D Point, Element3D? Visual)? HitTest(Point p) {
        var h = MainViewport.FindHits(p).FirstOrDefault(x=>x.ModelHit is Element3D);
        if(h!=null&&h.ModelHit is Element3D m) return (new Point3D(h.PointHit.X,h.PointHit.Y,h.PointHit.Z),m);
        return null;
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
    private Point3D? RayPl(Point3D o,Vector3D d,Point3D pp,Vector3D pn){
        double nd=Vector3D.DotProduct(d,pn); if(Math.Abs(nd)<0.0001) return null;
        double t=Vector3D.DotProduct(pp-o,pn)/nd; return t<0?null:o+d*t;
    }
    private void LookFromSide(bool buccal) {
        if (MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;
        var v = _halfDisplayVerts.Count > 0 ? _halfDisplayVerts : _mandibleVerts;
        if (v == null || v.Count == 0) return;
        double mnX=v[0][0], mxX=v[0][0], mnY=v[0][1], mxY=v[0][1], mnZ=v[0][2], mxZ=v[0][2];
        foreach(var u in v){if(u[0]<mnX)mnX=u[0];if(u[0]>mxX)mxX=u[0];if(u[1]<mnY)mnY=u[1];if(u[1]>mxY)mxY=u[1];if(u[2]<mnZ)mnZ=u[2];if(u[2]>mxZ)mxZ=u[2];}
        var center = new Point3D((mnX+mxX)/2,(mnY+mxY)/2,(mnZ+mxZ)/2);
        double span = Math.Sqrt(Math.Pow(mxX-mnX,2)+Math.Pow(mxY-mnY,2)+Math.Pow(mxZ-mnZ,2));
        double dist = span * 0.95;
        // Camera position: medial side for lingual, lateral side for buccal
        float viewDirX = buccal ? _latDirX : _medDirX;
        cam.Position = new Point3D(center.X + viewDirX * dist, center.Y, center.Z);
        cam.LookDirection = new Vector3D(-viewDirX * dist, 0, 0);
        cam.UpDirection = new Vector3D(0, 0, 1);
        MainViewport.FixedRotationPointEnabled = true;
        MainViewport.FixedRotationPoint = center;
    }
}
