using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;

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

    // Each handle stores its OWN independent XY; Z comes from _zTop or _zBot.
    // (double X, double Y) tuples for each of 8 handle XY positions:
    // [0]rFT [1]rFB [2]lFT [3]lFB  [4]jT [5]jB(junction,XY fixed)  [6]sBT [7]sBB
    private (double X,double Y) _rFT,_rFB,_lFT,_lFB,_sBT,_sBB;
    private Point3D _junc;
    private double  _zTop,_zBot;
    private bool    _yVis;

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
        MainGroup.Children.Add(_boneMesh);
        MainGroup.Children.Add(_pg);MainGroup.Children.Add(_lg);MainGroup.Children.Add(_hg);
        Loaded+=(_,_)=>FitCam(v);
        Closed+=(_,_)=>{if(_rh!=null){CompositionTarget.Rendering-=_rh;_rh=null;}MainGroup.Children.Clear();if(MainViewport.EffectsManager is IDisposable d)d.Dispose();MainViewport.EffectsManager=null;};
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
            var pt=CamPlane(pos,_hp[_dragH]);
            if(!pt.HasValue)return;
            double nx=pt.Value.X,ny=pt.Value.Y,nz=pt.Value.Z;
            switch(_dragH)
            {
                case 0: _rFT=(nx,ny); _zTop=nz; break;
                case 1: _rFB=(nx,ny); _zBot=nz; break;
                case 2: _lFT=(nx,ny); _zTop=nz; break;
                case 3: _lFB=(nx,ny); _zBot=nz; break;
                // Junction: XY movable + adjusts Z
                case 4: _junc=new(nx,ny,_junc.Z); _zTop=nz; break;
                case 5: _junc=new(nx,ny,_junc.Z); _zBot=nz; break;
                case 6: _sBT=(nx,ny); _zTop=nz; break;
                case 7: _sBB=(nx,ny); _zBot=nz; break;
            }
            if(_zBot>_zTop-3){if(_dragH%2==0)_zTop=_zBot+3;else _zBot=_zTop-3;}
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

    private static (double X,double Y) ExtXY(double px,double py,double pivX,double pivY,double dist)
    {double dx=px-pivX,dy=py-pivY,l=Math.Sqrt(dx*dx+dy*dy);return l<.001?(px,py):(px+dx/l*dist,py+dy/l*dist);}

    private void Init()
    {
        var r0=_ctrl[0];var r1=_ctrl[1];var l0=_ctrl[2];var l1=_ctrl[3];
        double rX=(r0.X+r1.X)/2,rY=(r0.Y+r1.Y)/2;
        double lX=(l0.X+l1.X)/2,lY=(l0.Y+l1.Y)/2;
        double jX=(rX+lX)/2,jY=(rY+lY)/2+20,jZ=(r0.Z+r1.Z+l0.Z+l1.Z)/4;
        _junc=new(jX,jY,jZ);
        _zTop=new[]{r0.Z,r1.Z,l0.Z,l1.Z}.Max()+20;
        _zBot=new[]{r0.Z,r1.Z,l0.Z,l1.Z}.Min()-20;
        _rFT=_rFB=ExtXY(rX,rY,jX,jY,15);
        _lFT=_lFB=ExtXY(lX,lY,jX,jY,15);
        _sBT=_sBB=(jX,jY+30);
        _yVis=true;Rebuild();
    }

    private void Rebuild()
    {
        _pg.Children.Clear();_lg.Children.Clear();_hg.Children.Clear();
        float zt=(float)_zTop,zb=(float)_zBot;
        float jx=(float)_junc.X,jy=(float)_junc.Y;
        var mat=new PhongMaterial{DiffuseColor=new(0f,.9f,1f,.18f),EmissiveColor=new(0f,.8f,1f,.12f)};
        var lb=new HelixToolkit.SharpDX.LineBuilder();



        void ArmQuad(float atx,float aty,float abx,float aby)
        {
            // corners: (atx,aty,zt) frontTop, (jx,jy,zt) juncTop, (jx,jy,zb) juncBot, (abx,aby,zb) frontBot
            var mb=new HelixToolkit.Geometry.MeshBuilder();
            mb.AddTriangle(N(atx,aty,zt),N(jx,jy,zt),N(jx,jy,zb));
            mb.AddTriangle(N(atx,aty,zt),N(jx,jy,zb),N(abx,aby,zb));
            mb.AddTriangle(N(jx,jy,zb),N(jx,jy,zt),N(atx,aty,zt)); // back
            mb.AddTriangle(N(abx,aby,zb),N(jx,jy,zb),N(atx,aty,zt)); // back
            _pg.Children.Add(new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),Material=mat,CullMode=SharpDX.Direct3D11.CullMode.None});
            // Outline: only 4 edges of the quad
            lb.AddLine(N(atx,aty,zt),N(jx,jy,zt));   // top edge
            lb.AddLine(N(abx,aby,zb),N(jx,jy,zb));   // bot edge
            lb.AddLine(N(atx,aty,zt),N(abx,aby,zb)); // front vertical
        }

        // Stem: independent top/bot XY at back
        void StemQuad(float stx,float sty,float sbx,float sby)
        {
            var mb=new HelixToolkit.Geometry.MeshBuilder();
            mb.AddTriangle(N(jx,jy,zt),N(stx,sty,zt),N(sbx,sby,zb));
            mb.AddTriangle(N(jx,jy,zt),N(sbx,sby,zb),N(jx,jy,zb));
            mb.AddTriangle(N(sbx,sby,zb),N(stx,sty,zt),N(jx,jy,zt)); // back
            mb.AddTriangle(N(jx,jy,zb),N(sbx,sby,zb),N(jx,jy,zt)); // back
            _pg.Children.Add(new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(mb.ToMesh()),Material=mat,CullMode=SharpDX.Direct3D11.CullMode.None});
            lb.AddLine(N(jx,jy,zt),N(stx,sty,zt));   // top
            lb.AddLine(N(jx,jy,zb),N(sbx,sby,zb));   // bot
            lb.AddLine(N(stx,sty,zt),N(sbx,sby,zb)); // back vertical
        }

        ArmQuad((float)_rFT.X,(float)_rFT.Y,(float)_rFB.X,(float)_rFB.Y);
        ArmQuad((float)_lFT.X,(float)_lFT.Y,(float)_lFB.X,(float)_lFB.Y);
        StemQuad((float)_sBT.X,(float)_sBT.Y,(float)_sBB.X,(float)_sBB.Y);

        // Junction vertical (shared edge of all 3 planes)
        lb.AddLine(N(jx,jy,zt),N(jx,jy,zb));
        _lg.Children.Add(new LineGeometryModel3D{Geometry=lb.ToLineGeometry3D(),Color=Colors.Cyan,Thickness=2});

        // Handles
        _hp[0]=new(_rFT.X,_rFT.Y,_zTop); _hp[1]=new(_rFB.X,_rFB.Y,_zBot);
        _hp[2]=new(_lFT.X,_lFT.Y,_zTop); _hp[3]=new(_lFB.X,_lFB.Y,_zBot);
        _hp[4]=new(_junc.X,_junc.Y,_zTop);_hp[5]=new(_junc.X,_junc.Y,_zBot);
        _hp[6]=new(_sBT.X,_sBT.Y,_zTop); _hp[7]=new(_sBB.X,_sBB.Y,_zBot);
        var hc=new HelixToolkit.Maths.Color4(0f,1f,1f,1f);
        for(int i=0;i<NH;i++){_hm[i]=Sph(_hp[i],1.0f,hc);_hg.Children.Add(_hm[i]);}
    }

    private void Next_Click(object s,RoutedEventArgs e){StepTitle.Text="LeFort 1 — 3-Piece: Adjust Y & Cut";StepInstructions.Text="Drag handles to adjust planes. Each top/bottom handle moves independently. Click Perform Cut when ready.";NextBtn.Visibility=Visibility.Collapsed;CutBtn.Visibility=Visibility.Visible;CutBtn.IsEnabled=true;}

    private void Cut_Click(object s,RoutedEventArgs e)
    {
        StatusText.Text="Cutting...";Cursor=Cursors.Wait;
        try
        {
            int nT=_maxillaVerts.Count/3;
            var L=new List<float[]>();var R=new List<float[]>();var C=new List<float[]>();
            double jX=_junc.X,jY=_junc.Y;
            double rAx=(_rFT.X+_rFB.X)/2, rAy=(_rFT.Y+_rFB.Y)/2;
            double lAx=(_lFT.X+_lFB.X)/2, lAy=(_lFT.Y+_lFB.Y)/2;

            // Arm direction vectors from junction outward
            double jToRx=rAx-jX, jToRy=rAy-jY;
            double jToLx=lAx-jX, jToLy=lAy-jY;

            // Outward normal for RIGHT arm: perpendicular to jToR, pointing AWAY from left arm.
            // Two perpendicular candidates: (jToRy, -jToRx) and (-jToRy, jToRx)
            // Choose the one where dot with (jToL) < 0 (points away from left arm)
            double nRx, nRy;
            if ( jToRy*(jToLx) + (-jToRx)*(jToLy) < 0 ) { nRx= jToRy; nRy=-jToRx; }
            else                                          { nRx=-jToRy; nRy= jToRx; }

            // Outward normal for LEFT arm: perpendicular to jToL, pointing AWAY from right arm.
            double nLx, nLy;
            if ( jToLy*(jToRx) + (-jToLx)*(jToRy) < 0 ) { nLx= jToLy; nLy=-jToLx; }
            else                                          { nLx=-jToLy; nLy= jToLx; }

            // Stem direction: from junction toward stem back (the posterior midline cut direction)
            double sDx=(_sBT.X+_sBB.X)/2-jX, sDy=(_sBT.Y+_sBB.Y)/2-jY;

            // Stem outward normal (for R/L split of posterior region): perpendicular to stem,
            // pointing toward the same side as the RIGHT arm front.
            double nSx, nSy;
            if ( sDy*(jToRx) + (-sDx)*(jToRy) > 0 ) { nSx= sDy; nSy=-sDx; }
            else                                      { nSx=-sDy; nSy= sDx; }

            for(int i=0;i<nT;i++)
            {
                double cx=(_maxillaVerts[i*3][0]+(double)_maxillaVerts[i*3+1][0]+_maxillaVerts[i*3+2][0])/3;
                double cy=(_maxillaVerts[i*3][1]+(double)_maxillaVerts[i*3+1][1]+_maxillaVerts[i*3+2][1])/3;
                double dx=cx-jX, dy=cy-jY;
                List<float[]> t;

                // Determine which side of the junction this centroid is on (stem direction test)
                bool isPosterior = dx*sDx + dy*sDy > 0;

                if (isPosterior)
                {
                    // Posterior to junction: stem splits into R or L (no central)
                    double dS = dx*nSx + dy*nSy;
                    t = dS > 0 ? R : L;
                }
                else
                {
                    // Anterior to junction: arm normals → R, L, or C
                    double dR=dx*nRx+dy*nRy;
                    double dL=dx*nLx+dy*nLy;
                    t = dR>0 ? R : dL>0 ? L : C;
                }
                t.Add(_maxillaVerts[i*3]);t.Add(_maxillaVerts[i*3+1]);t.Add(_maxillaVerts[i*3+2]);
            }
            LeftResult=L;RightResult=R;CentralResult=C;
            MainGroup.Children.Remove(_boneMesh);
            MainGroup.Children.Add(MkMesh(L,Color.FromRgb(100,200,255),1.0));
            MainGroup.Children.Add(MkMesh(R,Color.FromRgb(120,220,210),1.0));
            MainGroup.Children.Add(MkMesh(C,Color.FromRgb(220,180,255),1.0));
            AcceptBtn.Visibility=Visibility.Visible;CutBtn.IsEnabled=false;
            StatusText.Text=$"Done — L:{L.Count/3} R:{R.Count/3} C:{C.Count/3}";
        }
        finally{Cursor=Cursors.Arrow;}
    }

    private void Clear_Click(object s,RoutedEventArgs e)
    {
        _ctrl.Clear();foreach(var v in _ctrlVis)MainGroup.Children.Remove(v);_ctrlVis.Clear();
        _yVis=false;_pg.Children.Clear();_lg.Children.Clear();_hg.Children.Clear();
        StepTitle.Text="LeFort 1 — 3-Piece: Place 2 Right Points";
        StepInstructions.Text="Step 1: 2 points on RIGHT vestibular surface, then 2 on LEFT.";
        NextBtn.Visibility=Visibility.Visible;NextBtn.IsEnabled=false;
        CutBtn.Visibility=Visibility.Collapsed;CutBtn.IsEnabled=false;
        AcceptBtn.Visibility=Visibility.Collapsed;StatusText.Text="";
    }

    private void Accept_Click(object s,RoutedEventArgs e){Accepted=true;DialogResult=true;Close();}
    private void Cancel_Click(object s,RoutedEventArgs e){DialogResult=false;Close();}

    private bool Hit(Point pos,Point3D c,double r)=>MainViewport.FindHits(pos).Any(h=>h.ModelHit is MeshGeometryModel3D&&D(new(h.PointHit.X,h.PointHit.Y,h.PointHit.Z),c)<r+2);
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
        for(int i=0;i<verts.Count;i+=3)if(i+2<verts.Count)b.AddTriangle(F(verts[i]),F(verts[i+1]),F(verts[i+2]));
        return new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),Material=new PhongMaterial{DiffuseColor=new(col.R/255f,col.G/255f,col.B/255f,(float)op)}};
    }

    private MeshGeometryModel3D Sph(Point3D pt,float r,HelixToolkit.Maths.Color4 col)
    {
        var b=new HelixToolkit.Geometry.MeshBuilder();b.AddSphere(new System.Numerics.Vector3(0,0,0),r);
        return new MeshGeometryModel3D{Geometry=HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(b.ToMesh()),Material=new PhongMaterial{DiffuseColor=col,SpecularShininess=32f},Transform=new TranslateTransform3D(pt.X,pt.Y,pt.Z)};
    }

    private static double D(Point3D a,Point3D b){double dx=a.X-b.X,dy=a.Y-b.Y,dz=a.Z-b.Z;return Math.Sqrt(dx*dx+dy*dy+dz*dz);}
    private static System.Numerics.Vector3 N(float x,float y,float z)=>new(x,y,z);
    private static System.Numerics.Vector3 F(float[] v)=>new(v[0],v[1],v[2]);
}
