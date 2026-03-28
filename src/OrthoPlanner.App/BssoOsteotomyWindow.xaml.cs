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

    private const float ExtLat = 20f;
    private const float ExtInf = 10f;  // 10 mm inferior extension on sagittal
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
            CullMode = SharpDX.Direct3D11.CullMode.None
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

        float lAvgX = (float)((lAnt.X + lPost.X) / 2.0);
        float bAvgX = (float)((bSup.X + bInf.X) / 2.0);
        _latDirX = Math.Sign(lAvgX - bAvgX);
        if(_latDirX == 0) _latDirX = 1f;
        _medDirX = -_latDirX;

        _sagTop[0] = lPost; _sagTop[1] = lAnt;
        _sagBot[0] = new Point3D(lPost.X, lPost.Y, lPost.Z - ExtInf);
        _sagBot[1] = new Point3D(lAnt.X,  lAnt.Y,  lAnt.Z  - ExtInf);

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

        var bSup = _bc[0];
        _sagTop[2] = Lerp(_sagTop[1], bSup, 0.5);

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
        mb.AddTriangle(Nv3(_sagTop[2]),Nv3(bSup),Nv3(bInf));
        mb.AddTriangle(Nv3(bInf),Nv3(bSup),Nv3(_sagTop[2]));          // Triangle

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
            CullMode=SharpDX.Direct3D11.CullMode.None
        });
        _sagittalVis.Children.Add(new LineGeometryModel3D{Geometry=lb.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=2});

        BuildGP(_postArmVis, new[]{ _sagTop[0], _postArmTip, _armBot, _sagBot[0] });
    }

    private static void AddQuad(HelixToolkit.Geometry.MeshBuilder mb, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        mb.AddTriangle(Nv3(a),Nv3(b),Nv3(c)); mb.AddTriangle(Nv3(a),Nv3(c),Nv3(d));
        mb.AddTriangle(Nv3(c),Nv3(b),Nv3(a)); mb.AddTriangle(Nv3(d),Nv3(c),Nv3(a));
    }

    private void BuildGP(GroupModel3D grp, Point3D[] c)
    {
        grp.Children.Clear();
        var mb = new HelixToolkit.Geometry.MeshBuilder();
        mb.AddTriangle(Nv3(c[0]),Nv3(c[1]),Nv3(c[2])); mb.AddTriangle(Nv3(c[0]),Nv3(c[2]),Nv3(c[3]));
        mb.AddTriangle(Nv3(c[2]),Nv3(c[1]),Nv3(c[0])); mb.AddTriangle(Nv3(c[3]),Nv3(c[2]),Nv3(c[0]));
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(Nv3(c[0]),Nv3(c[1])); lb.AddLine(Nv3(c[1]),Nv3(c[2]));
        lb.AddLine(Nv3(c[2]),Nv3(c[3])); lb.AddLine(Nv3(c[3]),Nv3(c[0]));
        grp.Children.Add(new MeshGeometryModel3D{
            Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),
            Material=new PhongMaterial{DiffuseColor=CyanFill, EmissiveColor=CyanFill}
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

    private void Cut_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text="Cutting…"; Cursor=Cursors.Wait;
        try {
            // Pre-filter: operated-side triangles (other side → distal)
            float midX = _mandibleVerts.Count > 0 ? _mandibleVerts.Average(v => v[0]) : 0f;
            float cutCX = (float)((_lc[0].X+_lc[1].X+_bc[0].X+_bc[1].X)/4.0);
            float cutSide = Math.Sign(cutCX - midX);
            var operated = new List<float[]>(); var other = new List<float[]>();
            for(int i = 0; i+2 < _mandibleVerts.Count; i += 3) {
                float cx = (_mandibleVerts[i][0]+_mandibleVerts[i+1][0]+_mandibleVerts[i+2][0])/3f;
                if(Math.Sign(cx - midX) == cutSide || cx == midX) {
                    operated.Add(_mandibleVerts[i]); operated.Add(_mandibleVerts[i+1]); operated.Add(_mandibleVerts[i+2]);
                } else {
                    other.Add(_mandibleVerts[i]); other.Add(_mandibleVerts[i+1]); other.Add(_mandibleVerts[i+2]);
                }
            }

            // Build polyplane with 2.0mm precise influence — extruding cuts into marrow
            var bSup = _bc[0]; var bInf = _bc[1];
            // armBot is now a stored draggable field (initialized in InitSagittal)
            
            float medX = -cutSide * 20f; // medial extrusion
            float latX =  cutSide * 20f; // lateral extrusion
            var quads = new List<(float[],float[],float[],float[])>();
            
            // Lingual: extrude medially
            for(int i=0; i<3; i++) {
                var p0 = Fv(_lc[i]); var p1 = Fv(_lc[i+1]);
                var p0_ex = new float[]{p0[0]+medX, p0[1], p0[2]};
                var p1_ex = new float[]{p1[0]+medX, p1[1], p1[2]};
                quads.Add((p0_ex, p1_ex, p1, p0));
            }
            
            // Buccal: extrude laterally
            for(int i=0; i<3; i++) {
                var p0 = Fv(_bc[i]); var p1 = Fv(_bc[i+1]);
                var p0_ex = new float[]{p0[0]+latX, p0[1], p0[2]};
                var p1_ex = new float[]{p1[0]+latX, p1[1], p1[2]};
                quads.Add((p0, p1, p1_ex, p0_ex));
            }
            
            // Sagittal (Vertical sheet)
            quads.Add((Fv(_sagTop[0]),Fv(_sagTop[1]),Fv(_sagBot[1]),Fv(_sagBot[0])));
            quads.Add((Fv(_sagTop[1]),Fv(_sagTop[2]),Fv(bInf),Fv(_sagBot[1])));
            // Anterior border of sagittal split
            quads.Add((Fv(_sagTop[2]),Fv(bSup),Fv(bInf),Fv(_sagTop[2])));
            // Posterior arm
            quads.Add((Fv(_sagTop[0]),Fv(_postArmTip),Fv(_armBot),Fv(_sagBot[0])));
            
            var poly = new Polyplane(0.0); // No distance barrier needed anymore, using exact intersection
            poly.SetMeshFromQuads(quads);

            int nTri = operated.Count / 3;

            var ctrs  = new double[nTri][];
            for(int i = 0; i < nTri; i++) {
                ctrs[i] = new double[] {
                    (operated[i*3][0]+operated[i*3+1][0]+operated[i*3+2][0])/3.0,
                    (operated[i*3][1]+operated[i*3+1][1]+operated[i*3+2][1])/3.0,
                    (operated[i*3][2]+operated[i*3+1][2]+operated[i*3+2][2])/3.0 };
            }

            // Edge adjacency
            var edgeMap = new Dictionary<string, List<int>>(nTri * 2);
            for(int i = 0; i < nTri; i++) {
                for(int edge = 0; edge < 3; edge++) {
                    var kA = VKey(operated[i*3+edge]);
                    var kB = VKey(operated[i*3+(edge+1)%3]);
                    var ek = string.Compare(kA,kB)<0 ? kA+"|"+kB : kB+"|"+kA;
                    if(!edgeMap.TryGetValue(ek, out var lst)) { lst = new List<int>(2); edgeMap[ek]=lst; }
                    lst.Add(i);
                }
            }

            // Determine condyle seed from highest Y+Z (Most Posterior + Superior)
            int seed = -1; float bestScore = float.MinValue;
            for(int i = 0; i < nTri; i++) {
                float cy = (float)ctrs[i][1];
                float cz = (float)ctrs[i][2];
                float score = cy + cz; // Y+ is posterior, Z+ is superior
                if(score > bestScore) { bestScore=score; seed=i; }
            }

            // BFS from condyle — stop EXACTLY when edge crosses kerf polyplane
            var visited = new bool[nTri];
            if(seed >= 0) {
                var q = new Queue<int>(); q.Enqueue(seed); visited[seed]=true;
                while(q.Count > 0) {
                    int ti = q.Dequeue();
                    for(int edge = 0; edge < 3; edge++) {
                        var kA = VKey(operated[ti*3+edge]);
                        var kB = VKey(operated[ti*3+(edge+1)%3]);
                        var ek = string.Compare(kA,kB)<0 ? kA+"|"+kB : kB+"|"+kA;
                        if(edgeMap.TryGetValue(ek, out var nbrs))
                            foreach(int ni in nbrs) if(!visited[ni]) { 
                                // Exact graph cut: if the line connecting the two centroids crosses the cutting surface, the boundary is blocked
                                if (poly.SegmentIntersects(ctrs[ti], ctrs[ni])) continue;
                                visited[ni]=true; q.Enqueue(ni); 
                            }
                    }
                }
            }


            // ─── Floater reclassification: connected-component approach ───
            // Find all connected components of unvisited (distal) triangles.
            // The LARGEST component = the main mandible body → leave it alone.
            // All smaller orphan components get classified by their centroid against the 3 planes.

            // Helper: signed distance from a point to a plane defined by 3 reference points.
            static double PlaneSide(double[] pt, double[] p0, double[] p1, double[] p2)
            {
                double ax = p1[0]-p0[0], ay = p1[1]-p0[1], az = p1[2]-p0[2];
                double bx = p2[0]-p0[0], by = p2[1]-p0[1], bz = p2[2]-p0[2];
                double nx = ay*bz-az*by, ny = az*bx-ax*bz, nz = ax*by-ay*bx;
                return nx*(pt[0]-p0[0]) + ny*(pt[1]-p0[1]) + nz*(pt[2]-p0[2]);
            }

            // Build connected components of unvisited triangles
            var compMark = new int[nTri]; // 0 = unprocessed
            var components = new List<List<int>>();
            for (int i = 0; i < nTri; i++)
            {
                if (visited[i] || compMark[i] != 0) continue;
                var comp = new List<int>();
                var compQ = new Queue<int>();
                compQ.Enqueue(i); compMark[i] = components.Count + 1;
                while (compQ.Count > 0)
                {
                    int ti = compQ.Dequeue();
                    comp.Add(ti);
                    for (int edge = 0; edge < 3; edge++)
                    {
                        var kA = VKey(operated[ti*3+edge]);
                        var kB = VKey(operated[ti*3+(edge+1)%3]);
                        var ek = string.Compare(kA,kB)<0 ? kA+"|"+kB : kB+"|"+kA;
                        if (edgeMap.TryGetValue(ek, out var nbrs))
                            foreach (int ni in nbrs)
                                if (!visited[ni] && compMark[ni] == 0)
                                    { compMark[ni] = components.Count + 1; compQ.Enqueue(ni); }
                    }
                }
                components.Add(comp);
            }

            // Find largest component (main mandible body)
            int largestIdx = 0;
            for (int ci = 1; ci < components.Count; ci++)
                if (components[ci].Count > components[largestIdx].Count) largestIdx = ci;

            // Compute ramus centroid (all BFS-visited triangles)
            double ramusCx = 0, ramusCy = 0, ramusCz = 0; int ramusN = 0;
            for (int i = 0; i < nTri; i++) { if (!visited[i]) continue; ramusCx += ctrs[i][0]; ramusCy += ctrs[i][1]; ramusCz += ctrs[i][2]; ramusN++; }
            if (ramusN > 0) { ramusCx /= ramusN; ramusCy /= ramusN; ramusCz /= ramusN; }

            // Compute mandible body centroid (largest unvisited component)
            double mandCx = 0, mandCy = 0, mandCz = 0;
            foreach (int tri in components[largestIdx]) { mandCx += ctrs[tri][0]; mandCy += ctrs[tri][1]; mandCz += ctrs[tri][2]; }
            mandCx /= components[largestIdx].Count; mandCy /= components[largestIdx].Count; mandCz /= components[largestIdx].Count;

            // Plane reference points (using condyle seed sign to determine correct sides)
            var lingP0 = new double[]{_lc[0].X,_lc[0].Y,_lc[0].Z};
            var lingP1 = new double[]{_lc[1].X,_lc[1].Y,_lc[1].Z};
            var lingP2 = new double[]{_lc[2].X,_lc[2].Y,_lc[2].Z};
            double lingualSeedSign = PlaneSide(ctrs[seed], lingP0, lingP1, lingP2);

            var buccP0 = new double[]{_bc[0].X,_bc[0].Y,_bc[0].Z};
            var buccP1 = new double[]{_bc[1].X,_bc[1].Y,_bc[1].Z};
            var buccP2 = new double[]{_bc[2].X,_bc[2].Y,_bc[2].Z};
            double buccalSeedSign = PlaneSide(ctrs[seed], buccP0, buccP1, buccP2);

            var sagP0 = new double[]{_sagTop[0].X,_sagTop[0].Y,_sagTop[0].Z};
            var sagP1 = new double[]{_sagTop[1].X,_sagTop[1].Y,_sagTop[1].Z};
            var sagP2 = new double[]{_sagBot[0].X,_sagBot[0].Y,_sagBot[0].Z};
            double sagittalSeedSign = PlaneSide(ctrs[seed], sagP0, sagP1, sagP2);

            // Classify each orphan component:
            // Primary: 3-plane anatomical test
            // Fallback: nearest centroid (ramus vs mandible body)
            for (int ci = 0; ci < components.Count; ci++)
            {
                if (ci == largestIdx) continue; // Main mandible body — leave as distal
                var comp = components[ci];

                double cx = 0, cy = 0, cz = 0;
                foreach (int tri in comp) { cx += ctrs[tri][0]; cy += ctrs[tri][1]; cz += ctrs[tri][2]; }
                cx /= comp.Count; cy /= comp.Count; cz /= comp.Count;
                var cc = new double[] { cx, cy, cz };

                bool aboveLingual    = Math.Sign(PlaneSide(cc, lingP0, lingP1, lingP2)) == Math.Sign(lingualSeedSign);
                bool behindBuccal    = Math.Sign(PlaneSide(cc, buccP0, buccP1, buccP2)) == Math.Sign(buccalSeedSign);
                bool lateralSagittal = Math.Sign(PlaneSide(cc, sagP0, sagP1, sagP2)) == Math.Sign(sagittalSeedSign);
                bool planesRamus     = (behindBuccal && lateralSagittal) || aboveLingual;

                // Nearest-centroid fallback: closer to ramus than to mandible body?
                double dxR = cx-ramusCx, dyR = cy-ramusCy, dzR = cz-ramusCz;
                double dxM = cx-mandCx,  dyM = cy-mandCy,  dzM = cz-mandCz;
                bool nearerRamus = (dxR*dxR+dyR*dyR+dzR*dzR) < (dxM*dxM+dyM*dyM+dzM*dzM);

                if (planesRamus || nearerRamus)
                    foreach (int tri in comp) visited[tri] = true; // → Ramus
                // else stays distal
            }


            var proximal = new List<float[]>(); var distal = new List<float[]>();
            for(int i = 0; i < nTri; i++) {
                (visited[i] ? proximal : distal).Add(operated[i*3]);
                (visited[i] ? proximal : distal).Add(operated[i*3+1]);
                (visited[i] ? proximal : distal).Add(operated[i*3+2]);
            }
            distal.AddRange(other);
            ProximalResult = proximal; DistalResult = distal;

            MainGroup.Children.Remove(_boneMesh);
            MainGroup.Children.Add(MkBone(ProximalResult, new HelixToolkit.Maths.Color4(120/255f,160/255f,240/255f,1f)));
            MainGroup.Children.Add(MkBone(DistalResult,   new HelixToolkit.Maths.Color4(220/255f,140/255f,120/255f,1f)));
            _lingualVis.Children.Clear(); _sagittalVis.Children.Clear(); _postArmVis.Children.Clear(); _buccalVis.Children.Clear();
            AcceptBtn.Visibility=Visibility.Visible; CutBtn.Visibility=Visibility.Collapsed;
            StatusText.Text="Done. Ramus=blue, Mandible=red.";
        } finally { Cursor=Cursors.Arrow; }
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
}
