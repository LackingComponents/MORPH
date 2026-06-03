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

public partial class LeFort1YCutWindow : Window
{
    private readonly List<float[]> _maxillaVerts;
    public List<float[]> LeftResult    { get; private set; } = new();
    public List<float[]> RightResult   { get; private set; } = new();
    public List<float[]> CentralResult { get; private set; } = new();
    public bool Accepted { get; private set; }

    private MeshGeometryModel3D _boneMesh;
    private GroupModel3D _pg=new(),_lg=new(),_hg=new();
    private readonly List<Point3D>             _ctrl    = new();
    private readonly List<MeshGeometryModel3D> _ctrlVis = new();

    // 8 handles: [0]rFT [1]rFB [2]lFT [3]lFB [4]jT [5]jB [6]sBT [7]sBB
    // All stored as full 3D points. Init places all 8 inside the flat plane through placed points.
    private Point3D _rFT,_rFB,_lFT,_lFB,_jT,_jB,_sBT,_sBB;
    private bool _yVis;
    private const int NH=8;
    private readonly MeshGeometryModel3D[] _hm=new MeshGeometryModel3D[NH];
    private readonly Point3D[]             _hp=new Point3D[NH];
    private int _dragH=-1,_dragC=-1;
    private EventHandler? _rh;

    public LeFort1YCutWindow(List<float[]> v)
    {
        InitializeComponent();
        MainViewport.EffectsManager=new HelixToolkit.SharpDX.DefaultEffectsManager();
        _rh=(_,_)=>{var d=SubCamera.LookDirection;if(d.Length>.001){d.Normalize();Headlamp.Direction=new(-d.X,-d.Y,-d.Z);Backlamp.Direction=new(d.X,d.Y,d.Z);}};
        CompositionTarget.Rendering+=_rh;
        _maxillaVerts=v;
        _boneMesh=MkMesh(v,Color.FromRgb(120,220,210),1.0);
        MainGroup.Children.Add(_boneMesh);MainGroup.Children.Add(_pg);MainGroup.Children.Add(_lg);MainGroup.Children.Add(_hg);
        Loaded+=(_,_)=>FitCam(v);
        Closed+=(_,_)=>{if(_rh!=null){CompositionTarget.Rendering-=_rh;_rh=null;}MainGroup.Children.Clear();if(MainViewport.EffectsManager is IDisposable d2)d2.Dispose();MainViewport.EffectsManager=null;};
    }

    // ── 3D math helpers ─────────────────────────────────────────────────────
    static Point3D   Add(Point3D a,Vector3D b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    static Vector3D  Sub(Point3D a,Point3D b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    static Vector3D  Cross(Vector3D a,Vector3D b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    static double    Dot(Vector3D a,Vector3D b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    static Vector3D  Norm(Vector3D v){double l=v.Length;return l<1e-9?v:new(v.X/l,v.Y/l,v.Z/l);}
    static Point3D   Lerp(Point3D a,Point3D b,double t)=>new(a.X+t*(b.X-a.X),a.Y+t*(b.Y-a.Y),a.Z+t*(b.Z-a.Z));

    // Project point P onto plane through origin 'o' with normal 'n'
    static Point3D ProjectToPlane(Point3D p,Point3D o,Vector3D n)
    { var d=Dot(Sub(p,o),n); return new(p.X-d*n.X,p.Y-d*n.Y,p.Z-d*n.Z); }

    // Compute 4 handles lying in the flat plane through p0,p1,pivot.
    // Returns: front-top, front-bottom, back-top, back-bottom — all in the plane.
    static (Point3D ft,Point3D fb,Point3D bt,Point3D bb)
        FlatArmHandles(Point3D p0,Point3D p1,Point3D pivot,double vestExt=15,double backExt=5)
    {
        var e01=Sub(p1,p0); var ep=Sub(pivot,p0);
        var n=Norm(Cross(e01,ep));
        if(n.Length<1e-9) n=new(0,0,1); // degenerate: default to vertical
        // In-plane outward direction (vestibular): away from pivot projected in-plane
        var toPivot=Norm(ep-Dot(ep,n)*n); // in-plane toward pivot
        var outward=-toPivot; // in-plane away from pivot (vestibular)
        // In-plane vertical direction: closest in-plane to world Z-up
        var zUp=new Vector3D(0,0,1);
        var ipUp=Norm(zUp-Dot(zUp,n)*n);
        if(ipUp.Length<1e-9) ipUp=Norm(e01); // fallback

        // Height: span from p0 and p1 in the in-plane-up direction
        double h0=Dot(Sub(p0,p0),ipUp); // =0 by definition
        double h1=Dot(Sub(p1,p0),ipUp);
        double hPiv=Dot(ep,ipUp);
        double hTop=Math.Max(h0,Math.Max(h1,hPiv))+15;
        double hBot=Math.Min(h0,Math.Min(h1,hPiv))-15;

        // Back edge: project pivot into plane (it IS in the plane) + backExt outward
        var backBase=Add(pivot,backExt*outward); // slightly past pivot in junction direction... wait: outward is AWAY from pivot
        // Actually back = pivot side, so we go backExt in toPivot direction past pivot
        var backT=Add(Add(p0,hTop*ipUp),backExt*toPivot+Dot(ep,outward)*outward);
        var backB=Add(Add(p0,hBot*ipUp),backExt*toPivot+Dot(ep,outward)*outward);

        // Simpler: back handles = project (pivot ± ipUp*20) to remain in plane (they already are)
        var bt=Add(pivot,15*ipUp);
        var bb=Add(pivot,-15*ipUp);
        // Front handles: p0/p1 extended vestibularly, clamped to top/bottom
        var ft=Add(Add(p0,hTop*ipUp),vestExt*outward);
        var fb=Add(Add(p0,hBot*ipUp),vestExt*outward);

        // Verify all 4 in plane (project to enforce)
        ft=ProjectToPlane(ft,p0,n);fb=ProjectToPlane(fb,p0,n);
        bt=ProjectToPlane(bt,p0,n);bb=ProjectToPlane(bb,p0,n);
        return(ft,fb,bt,bb);
    }

    private void Init()
    {
        var r0=_ctrl[0];var r1=_ctrl[1];var l0=_ctrl[2];var l1=_ctrl[3];
        double jX=(r0.X+r1.X+l0.X+l1.X)/4, jY=(r0.Y+r1.Y+l0.Y+l1.Y)/4+20, jZ=(r0.Z+r1.Z+l0.Z+l1.Z)/4;
        var junc=new Point3D(jX,jY,jZ);

        var (rft,rfb,rjt,rjb)=FlatArmHandles(r0,r1,junc);
        var (lft,lfb,ljt,ljb)=FlatArmHandles(l0,l1,junc);
        _rFT=rft;_rFB=rfb;_jT=rjt;_jB=rjb;
        _lFT=lft;_lFB=lfb;
        // Stem
        _sBT=Add(junc, new Vector3D(0, 30, 15));
        _sBB=Add(junc, new Vector3D(0, 30,-15));
        _yVis=true;Rebuild();
    }

    private void Rebuild()
    {
        _pg.Children.Clear();_lg.Children.Clear();_hg.Children.Clear();
        var mat=new PhongMaterial{DiffuseColor=new(0f,.9f,1f,.18f),EmissiveColor=new(0f,.8f,1f,.12f)};
        var lb=new HelixToolkit.SharpDX.LineBuilder();

        void DrawQuad(Point3D a,Point3D b,Point3D c,Point3D d)
        {
            var mb=new HelixToolkit.Geometry.MeshBuilder();
            mb.AddTriangle(N3(a),N3(b),N3(c));mb.AddTriangle(N3(a),N3(c),N3(d));
            mb.AddTriangle(N3(c),N3(b),N3(a));mb.AddTriangle(N3(d),N3(c),N3(a));
            _pg.Children.Add(new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),Material=mat,CullMode=SharpDX.Direct3D11.CullMode.None});
            lb.AddLine(N3(a),N3(b));lb.AddLine(N3(b),N3(c));lb.AddLine(N3(c),N3(d));lb.AddLine(N3(d),N3(a));lb.AddLine(N3(a),N3(c));
        }

        DrawQuad(_rFT,_jT,_jB,_rFB);
        DrawQuad(_lFT,_jT,_jB,_lFB);
        DrawQuad(_jT,_sBT,_sBB,_jB);
        lb.AddLine(N3(_jT),N3(_jB));
        _lg.Children.Add(new LineGeometryModel3D{Geometry=lb.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=2});

        _hp[0]=_rFT;_hp[1]=_rFB;_hp[2]=_lFT;_hp[3]=_lFB;
        _hp[4]=_jT; _hp[5]=_jB; _hp[6]=_sBT;_hp[7]=_sBB;
        var hc=new HelixToolkit.Maths.Color4(0f,1f,1f,1f);
        for(int i=0;i<NH;i++){_hm[i]=Sph(_hp[i],1.0f,hc);_hg.Children.Add(_hm[i]);}
    }

    private void Viewport_PreviewMouseLeftButtonDown(object s,MouseButtonEventArgs e)
    {
        if(Accepted)return;
        var pos=e.GetPosition(MainViewport);
        if(_yVis)for(int i=0;i<NH;i++)if(Hit(pos,_hp[i],4)){_dragH=i;MainViewport.CaptureMouse();e.Handled=true;return;}
        for(int i=0;i<_ctrlVis.Count;i++)if(Hit(pos,_ctrl[i],4)){_dragC=i;MainViewport.CaptureMouse();e.Handled=true;return;}
        if(_ctrl.Count<4){var h=HitBone(pos);if(h.HasValue){Place(h.Value);e.Handled=true;}}
    }

    private void Viewport_PreviewMouseMove(object s,MouseEventArgs e)
    {
        if(!MainViewport.IsMouseCaptured)return;
        var pos=e.GetPosition(MainViewport);
        if(_dragH>=0)
        {
            var pt=CamPlane(pos,_hp[_dragH]);if(!pt.HasValue)return;
            switch(_dragH){
                case 0:_rFT=pt.Value;break;case 1:_rFB=pt.Value;break;
                case 2:_lFT=pt.Value;break;case 3:_lFB=pt.Value;break;
                case 4:_jT=pt.Value;break; case 5:_jB=pt.Value;break;
                case 6:_sBT=pt.Value;break;case 7:_sBB=pt.Value;break;
            }
            Rebuild();
        }
        else if(_dragC>=0)
        {
            var h=HitBone(pos);if(!h.HasValue)return;
            _ctrl[_dragC]=h.Value;_ctrlVis[_dragC].Transform=new TranslateTransform3D(h.Value.X,h.Value.Y,h.Value.Z);
            if(_yVis)Init();
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object s,MouseButtonEventArgs e){_dragH=-1;_dragC=-1;MainViewport.ReleaseMouseCapture();}

    private void Place(Point3D pt)
    {
        _ctrl.Add(pt);
        var sp=Sph(pt,1.0f,new HelixToolkit.Maths.Color4(0f,1f,1f,1f));
        _ctrlVis.Add(sp);MainGroup.Children.Add(sp);
        int n=_ctrl.Count;
        StatusText.Text=n<2?$"Right: {n}/2":n<4?$"Right ✓  Left: {n-2}/2":"All 4 points placed.";
        if(n==4){NextBtn.IsEnabled=true;Init();}
    }

    private void Next_Click(object s,RoutedEventArgs e){StepTitle.Text="LeFort 1 — 3-Piece: Adjust Y & Cut";StepInstructions.Text="Drag any handle to adjust. Each handle moves independently in all 3D directions. Click Perform Cut when ready.";NextBtn.Visibility=Visibility.Collapsed;CutBtn.Visibility=Visibility.Visible;CutBtn.IsEnabled=true;}

    // Orient plane normal of triangle (a,b,c) to point AWAY from 'away'
    static Vector3D TriNorm(Point3D a,Point3D b,Point3D c,Point3D away)
    {
        var n=Norm(Cross(Sub(b,a),Sub(c,a)));
        if(Dot(n,Sub(away,a))>0)n=new(-n.X,-n.Y,-n.Z);
        return n;
    }

    // Signed distance from point P to plane through 'o' with normal 'n'
    static double PlaneD(Point3D p,Point3D o,Vector3D n)=>Dot(Sub(p,o),n);

    // Returns true if point P, projected orthogonally onto the plane of triangle (a,b,c), lands inside it.
    static bool InsideTri(Point3D P,Point3D a,Point3D b,Point3D c,Vector3D n)
    {
        // Project P onto triangle plane
        double dist=Dot(Sub(P,a),n);
        var Pp=new Point3D(P.X-dist*n.X,P.Y-dist*n.Y,P.Z-dist*n.Z);
        // Barycentric test via cross products (same-side method)
        var ab=Sub(b,a);var bc=Sub(c,b);var ca=Sub(a,c);
        var ap=Sub(Pp,a);var bp=Sub(Pp,b);var cp=Sub(Pp,c);
        double d0=Dot(n,Cross(ab,ap));
        double d1=Dot(n,Cross(bc,bp));
        double d2=Dot(n,Cross(ca,cp));
        return (d0>=0&&d1>=0&&d2>=0)||(d0<=0&&d1<=0&&d2<=0);
    }


    private async void Cut_Click(object s, RoutedEventArgs e)
    {
        StatusText.Text = "True-slicing Le Fort 1 3-piece... (may take a moment)";
        Cursor = Cursors.Wait;
        CutBtn.IsEnabled = false;

        // Snapshot handle positions for background thread
        var maxillaVerts = _maxillaVerts;
        var rFT = _rFT; var rFB = _rFB;
        var lFT = _lFT; var lFB = _lFB;
        var jT  = _jT;  var jB  = _jB;
        var sBT = _sBT; var sBB = _sBB;

        // Build 3 separate single-plane Polyplanes: right arm, left arm, stem.
        // Each arm cut passes the stem as secondaryPlane so a vertex is "above"
        // only if it's above the arm AND above the stem. This correctly bounds
        // the central piece anteriorly (arms) AND posteriorly (stem).
        // All use plane equation (IsSinglePlane) — O(1), exact, infinite.
        float[] F(Point3D p) => new float[]{ (float)p.X, (float)p.Y, (float)p.Z };
        var ppRight = new Polyplane(0.0);
        ppRight.SetMeshFromQuads(new List<(float[],float[],float[],float[])>{
            (F(rFT), F(jT), F(jB), F(rFB))
        });
        var ppLeft = new Polyplane(0.0);
        ppLeft.SetMeshFromQuads(new List<(float[],float[],float[],float[])>{
            (F(lFT), F(jT), F(jB), F(lFB))
        });
        var ppStem = new Polyplane(0.0);
        ppStem.SetMeshFromQuads(new List<(float[],float[],float[],float[])>{
            (F(jT), F(sBT), F(sBB), F(jB))
        });

        List<float[]> L, R, C;
        try
        {
            (L, R, C) = await System.Threading.Tasks.Task.Run(() =>
            {
                // Reference: highest-Z vertex = cranial/central (above both arm planes)
                double bestZ = double.MinValue;
                double[] crRef = { 0, 0, 0 };
                foreach (var v in maxillaVerts)
                    if (v[2] > bestZ) { bestZ = v[2]; crRef = new double[]{ v[0], v[1], v[2] }; }

                // ── Step 1: cut along right arm, with stem as secondary ─────────
                // "above" = above right arm AND above stem = central + left lateral
                // "below" = right lateral segment
                var (central1, rightSeg) = MeshOps.TrueSliceByPolyplane(
                    maxillaVerts, ppRight, crRef, capEnds: true, secondaryPlane: ppStem);

                // ── Step 2: cut along left arm, with stem as secondary ──────────
                double bestZ2 = double.MinValue;
                double[] crRef2 = crRef;
                foreach (var v in central1)
                    if (v[2] > bestZ2) { bestZ2 = v[2]; crRef2 = new double[]{ v[0], v[1], v[2] }; }

                var (centralSeg, leftSeg) = MeshOps.TrueSliceByPolyplane(
                    central1, ppLeft, crRef2, capEnds: true, secondaryPlane: ppStem);

                // Verify handedness: rFT should be on the "right" side of ppRight
                // If rightSeg is actually larger than leftSeg in X, swap left/right
                double rSumX = rightSeg.Count > 0 ? rightSeg.Average(v => v[0]) : 0;
                double lSumX = leftSeg.Count  > 0 ? leftSeg.Average(v  => v[0]) : 0;
                // On a left-side maxilla the right segment has larger X; if reversed, swap
                if (rFT.X < lFT.X && rSumX > lSumX)
                    return (leftSeg, rightSeg, centralSeg);

                return (leftSeg, rightSeg, centralSeg);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cut failed: {ex.Message}";
            CutBtn.IsEnabled = true;
            Cursor = Cursors.Arrow;
            return;
        }

        LeftResult    = L;
        RightResult   = R;
        CentralResult = C;

        MainGroup.Children.Remove(_boneMesh);
        // Add opaque bone segments first
        MainGroup.Children.Add(MkMesh(L, Color.FromRgb(100, 200, 255), 1.0));
        MainGroup.Children.Add(MkMesh(R, Color.FromRgb(120, 220, 210), 1.0));
        MainGroup.Children.Add(MkMesh(C, Color.FromRgb(220, 180, 255), 1.0));
        // Move transparent polyplane quads to end so they render on top correctly
        if (MainGroup.Children.Contains(_pg)) { MainGroup.Children.Remove(_pg); MainGroup.Children.Add(_pg); }
        if (MainGroup.Children.Contains(_lg)) { MainGroup.Children.Remove(_lg); MainGroup.Children.Add(_lg); }
        if (MainGroup.Children.Contains(_hg)) { MainGroup.Children.Remove(_hg); MainGroup.Children.Add(_hg); }
        AcceptBtn.Visibility = Visibility.Visible;
        CutBtn.IsEnabled = false;
        StatusText.Text = $"Done -- L:{L.Count/3} R:{R.Count/3} C:{C.Count/3}";
        Cursor = Cursors.Arrow;
    }


    private void Clear_Click(object s,RoutedEventArgs e)
    {
        _ctrl.Clear();foreach(var v in _ctrlVis)MainGroup.Children.Remove(v);_ctrlVis.Clear();
        _yVis=false;_pg.Children.Clear();_lg.Children.Clear();_hg.Children.Clear();
        StepTitle.Text="LeFort 1 — 3-Piece: Place 4 Points";
        StepInstructions.Text="Step 1: 2 points on RIGHT vestibular surface, then 2 on LEFT.";
        NextBtn.Visibility=Visibility.Visible;NextBtn.IsEnabled=false;
        CutBtn.Visibility=Visibility.Collapsed;CutBtn.IsEnabled=false;
        AcceptBtn.Visibility=Visibility.Collapsed;StatusText.Text="";
    }

    private void Accept_Click(object s,RoutedEventArgs e){Accepted=true;DialogResult=true;Close();}
    private void Cancel_Click(object s,RoutedEventArgs e){DialogResult=false;Close();}

    private bool Hit(Point pos,Point3D c,double r)=>MainViewport.FindHits(pos).Any(h=>h.ModelHit is MeshGeometryModel3D&&Dist(new(h.PointHit.X,h.PointHit.Y,h.PointHit.Z),c)<r+2);
    private Point3D? HitBone(Point pos){var h=MainViewport.FindHits(pos).FirstOrDefault(x=>x.ModelHit==_boneMesh);return h==null?null:new(h.PointHit.X,h.PointHit.Y,h.PointHit.Z);}

    private Point3D? CamPlane(Point pos,Point3D anchor)
    {
        var look=SubCamera.LookDirection;look.Normalize();
        var pn=new Vector3D(-look.X,-look.Y,-look.Z);
        var ray=MainViewport.UnProject(pos);
        double nd=pn.X*ray.Direction.X+pn.Y*ray.Direction.Y+pn.Z*ray.Direction.Z;
        if(Math.Abs(nd)<.0001)return null;
        double t=(pn.X*(anchor.X-ray.Position.X)+pn.Y*(anchor.Y-ray.Position.Y)+pn.Z*(anchor.Z-ray.Position.Z))/nd;
        return t<0?null:new(ray.Position.X+t*ray.Direction.X,ray.Position.Y+t*ray.Direction.Y,ray.Position.Z+t*ray.Direction.Z);
    }

    private void FitCam(List<float[]> v)
    {
        if(v==null||v.Count==0||MainViewport.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam)return;
        double mnX=9e9,mnY=9e9,mnZ=9e9,mxX=-9e9,mxY=-9e9,mxZ=-9e9;
        foreach(var u in v){if(u[0]<mnX)mnX=u[0];if(u[0]>mxX)mxX=u[0];if(u[1]<mnY)mnY=u[1];if(u[1]>mxY)mxY=u[1];if(u[2]<mnZ)mnZ=u[2];if(u[2]>mxZ)mxZ=u[2];}
        var c=new Point3D((mnX+mxX)/2,(mnY+mxY)/2,(mnZ+mxZ)/2);
        double dist=Math.Sqrt(Math.Pow(mxX-mnX,2)+Math.Pow(mxY-mnY,2)+Math.Pow(mxZ-mnZ,2))*1.2;
        cam.Position=new(c.X,c.Y-dist,c.Z);cam.LookDirection=new(0,dist,0);cam.UpDirection=new(0,0,1);
        MainViewport.FixedRotationPointEnabled=true;MainViewport.FixedRotationPoint=c;
    }

    private MeshGeometryModel3D MkMesh(List<float[]> verts,Color col,double op)
    {
        var b=new HelixToolkit.Geometry.MeshBuilder();
        for(int i=0;i<verts.Count;i+=3)if(i+2<verts.Count)b.AddTriangle(N3f(verts[i]),N3f(verts[i+1]),N3f(verts[i+2]));
        return new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),Material=new PhongMaterial{DiffuseColor=new(col.R/255f,col.G/255f,col.B/255f,(float)op)}};
    }

    private MeshGeometryModel3D Sph(Point3D pt,float r,HelixToolkit.Maths.Color4 col)
    {
        var b=new HelixToolkit.Geometry.MeshBuilder();b.AddSphere(new System.Numerics.Vector3(0,0,0),r);
        return new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),Material=new PhongMaterial{DiffuseColor=col,SpecularShininess=32f},Transform=new TranslateTransform3D(pt.X,pt.Y,pt.Z)};
    }

    static double Dist(Point3D a,Point3D b){var d=Sub(a,b);return Math.Sqrt(d.X*d.X+d.Y*d.Y+d.Z*d.Z);}
    static System.Numerics.Vector3 N3(Point3D p)=>new((float)p.X,(float)p.Y,(float)p.Z);
    static System.Numerics.Vector3 N3f(float[] v)=>new(v[0],v[1],v[2]);
}
