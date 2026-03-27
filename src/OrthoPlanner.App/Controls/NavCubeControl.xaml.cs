using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace OrthoPlanner.App.Controls;

/// <summary>
/// Autodesk-style Navigation Cube. Each physical face of the cube comprises a 
/// square central region (for the main Face view) bounded by 4 flush trapezoids 
/// (for the Edge handles). These seamlessly tile the cubic surface with 45-degree
/// mitered corners.
/// </summary>
public sealed partial class NavCubeControl : UserControl
{
    // ── Face Definitions (0-5: Faces, 6-17: Edges) ─────────────────────────────
    public static readonly (Vector3D Normal, Vector3D CamDir, Vector3D CamUp, string Label)[] FaceDefs =
    [
        (N( 0,-1, 0), N( 0, 1, 0), N(0, 0, 1), "FRONT"),
        (N( 0, 1, 0), N( 0,-1, 0), N(0, 0, 1), "BACK"),
        (N( 1, 0, 0), N(-1, 0, 0), N(0, 0, 1), "RIGHT"),
        (N(-1, 0, 0), N( 1, 0, 0), N(0, 0, 1), "LEFT"),
        (N( 0, 0, 1), N( 0, 0,-1), N(0, 1, 0), "TOP"),     // Up points Back (+Y)
        (N( 0, 0,-1), N( 0, 0, 1), N(0,-1, 0), "BOTTOM"),  // Up points Front (-Y)
        // edges 6–17
        (N(0,-1,1),N(0,1,-1),N(0,0,1),""), (N(0,1,1),N(0,-1,-1),N(0,0,1),""),
        (N(1,0,1),N(-1,0,-1),N(0,0,1),""), (N(-1,0,1),N(1,0,-1),N(0,0,1),""),
        (N(0,-1,-1),N(0,1,1),N(0,0,1),""), (N(0,1,-1),N(0,-1,1),N(0,0,1),""),
        (N(1,0,-1),N(-1,0,1),N(0,0,1),""), (N(-1,0,-1),N(1,0,1),N(0,0,1),""),
        (N(1,-1,0),N(-1,1,0),N(0,0,1),""), (N(-1,-1,0),N(1,1,0),N(0,0,1),""),
        (N(1,1,0),N(-1,-1,0),N(0,0,1),""), (N(-1,1,0),N(1,-1,0),N(0,0,1),""),
    ];
    static Vector3D N(double x,double y,double z){var v=new Vector3D(x,y,z);v.Normalize();return v;}

    private static readonly Color[] _fc = [
        Color.FromRgb(0x5A,0x6A,0x84), Color.FromRgb(0x47,0x55,0x6B),
        Color.FromRgb(0x52,0x63,0x7C), Color.FromRgb(0x4B,0x5B,0x73),
        Color.FromRgb(0x6A,0x7C,0x97), Color.FromRgb(0x3E,0x4C,0x60),
    ];
    private static readonly Color _stripNorm = Color.FromRgb(0x3F,0x4E,0x65);
    private static readonly Color _hoverFace = Color.FromRgb(0x1B,0x98,0xE0);
    private static readonly Color _hoverStrip= Color.FromRgb(0x15,0x7F,0xBA);
    private static readonly Color _arrowCol  = Color.FromArgb(190,0xBB,0xCC,0xE4);
    private static readonly Color _arrowHov  = Color.FromArgb(255,0x1B,0x98,0xE0);

    private PerspectiveCamera          _navCam = null!;
    private readonly GeometryModel3D[] _faces  = new GeometryModel3D[6];
    private readonly DiffuseMaterial[] _faceN  = new DiffuseMaterial[6];
    private readonly DiffuseMaterial[] _faceH  = new DiffuseMaterial[6];
    private readonly GeometryModel3D[] _strips = new GeometryModel3D[24];
    private readonly int[]             _stripDef = new int[24];
    private readonly DiffuseMaterial   _sNorm;
    private readonly DiffuseMaterial   _sHov;
    private int                        _hovered = -1;

    public HelixToolkit.Wpf.SharpDX.PerspectiveCamera? MainCamera { get; set; }
    public event Action<int>?           FaceClicked;
    public event Action<double,double>? RotateRequested;

    public NavCubeControl()
    {
        _sNorm=Mat(_stripNorm); _sHov=Mat(_hoverStrip);
        InitializeComponent();
        BuildScene(); BuildArrows();
        CompositionTarget.Rendering += OnRender;
    }

    // ── 3D Scene Assembly ──────────────────────────────────────────────────────
    private void BuildScene()
    {
        _navCam = new PerspectiveCamera {
            Position=new Point3D(0,-4,0), LookDirection=new Vector3D(0,4,0),
            UpDirection=new Vector3D(0,0,1), FieldOfView=36,
            NearPlaneDistance=0.1, FarPlaneDistance=100
        };
        _viewport.Camera = _navCam;

        var world = new Model3DGroup();
        world.Children.Add(new AmbientLight(Color.FromRgb(155,162,172)));
        world.Children.Add(new DirectionalLight(Color.FromRgb(150,160,175),new Vector3D(-1,-2,-3)));

        const double s=0.54, c=0.38; // s = cube radius, c = inner face radius
        // 0.38 leaves 0.16 thickness for edge strips

        int eIdx = 0;
        void BuildFace(int fi, int topE, int botE, int rightE, int leftE, Vector3D n, Vector3D up, Vector3D right)
        {
            Point3D P(double x, double y) => (Point3D)(n*s + right*x + up*y);

            // Inner corners (Center label face)
            Point3D cbl = P(-c,-c), cbr = P(c,-c), ctr = P(c,c), ctl = P(-c,c);
            // Outer corners (edges of the cube)
            Point3D obl = P(-s,-s), obr = P(s,-s), otr = P(s,s), otl = P(-s,s);

            // Center square
            _faceN[fi] = FaceMat(_fc[fi], FaceDefs[fi].Label);
            _faceH[fi] = FaceMat(_hoverFace, FaceDefs[fi].Label);
            var fg = QuadMesh(cbl, cbr, ctr, ctl);
            // Unified standard UV works for all faces provided 'up' and 'right' accurately map to screen axes.
            fg.TextureCoordinates = new PointCollection([new(0,1),new(1,1),new(1,0),new(0,0)]);
            _faces[fi] = new GeometryModel3D(fg, _faceN[fi]) { BackMaterial = _faceN[fi] };
            world.Children.Add(_faces[fi]);

            // Top edge strip (trapezoid tiling to 45 degree corners)
            var tg = QuadMesh(ctl, ctr, otr, otl);
            _stripDef[eIdx] = topE;
            _strips[eIdx] = new GeometryModel3D(tg, _sNorm) { BackMaterial = _sNorm };
            world.Children.Add(_strips[eIdx++]);

            // Bottom edge strip
            var bg = QuadMesh(obl, obr, cbr, cbl);
            _stripDef[eIdx] = botE;
            _strips[eIdx] = new GeometryModel3D(bg, _sNorm) { BackMaterial = _sNorm };
            world.Children.Add(_strips[eIdx++]);

            // Right edge strip
            var rg = QuadMesh(cbr, obr, otr, ctr);
            _stripDef[eIdx] = rightE;
            _strips[eIdx] = new GeometryModel3D(rg, _sNorm) { BackMaterial = _sNorm };
            world.Children.Add(_strips[eIdx++]);

            // Left edge strip
            var lg = QuadMesh(obl, cbl, ctl, otl);
            _stripDef[eIdx] = leftE;
            _strips[eIdx] = new GeometryModel3D(lg, _sNorm) { BackMaterial = _sNorm };
            world.Children.Add(_strips[eIdx++]);
        }

        // Each face's Up/Right vectors determine standard text orientation.
        // Mapped adjacent edges corresponding exactly to FaceDefs (6-17).
        BuildFace(0,  6, 10, 14, 15, N(0,-1,0),  N(0,0,1),  N(1,0,0));  // FRONT
        BuildFace(1,  7, 11, 17, 16, N(0,1,0),   N(0,0,1),  N(-1,0,0)); // BACK
        BuildFace(2,  8, 12, 16, 14, N(1,0,0),   N(0,0,1),  N(0,1,0));  // RIGHT
        BuildFace(3,  9, 13, 15, 17, N(-1,0,0),  N(0,0,1),  N(0,-1,0)); // LEFT
        // TOP and BOTTOM must point +Y (Back) and -Y (Front) so "FRONT" face remains towards bottom of screen
        BuildFace(4,  7,  6,  8,  9, N(0,0,1),   N(0,1,0),  N(1,0,0));  // TOP
        BuildFace(5, 10, 11, 12, 13, N(0,0,-1),  N(0,-1,0), N(1,0,0));  // BOTTOM

        _viewport.Children.Add(new ModelVisual3D{Content=world});
    }

    private static MeshGeometry3D QuadMesh(Point3D a,Point3D b,Point3D c,Point3D d)=>new() {
        Positions=new Point3DCollection([a,b,c,d]), TriangleIndices=new Int32Collection{0,1,2,0,2,3}
    };

    private static DiffuseMaterial FaceMat(Color bg, string label)
    {
        const int sz=128;
        var b=new Border{Width=sz,Height=sz,Background=new SolidColorBrush(bg),
            Child=new TextBlock{Text=label,FontSize=26,FontWeight=FontWeights.Bold,
                Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,
                VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center}};
        b.Measure(new Size(sz,sz)); b.Arrange(new Rect(0,0,sz,sz));
        return new DiffuseMaterial(new VisualBrush(b){Stretch=Stretch.Fill});
    }
    private static DiffuseMaterial Mat(Color c) => new(new SolidColorBrush(c));

    // ── Rotation Arrows ────────────────────────────────────────────────────────
    private void BuildArrows()
    {
        const double aw=10,ah=7,cw=110,gap=10; // Slightly smaller arrows (10x7), moved outward slightly (gap 10 instead of 15)
        // ↑ at BOTTOM  → orbit +90° elevation
        Arrow([new(0,ah),new(aw,ah),new(aw/2,0)],    (cw-aw)/2, cw-ah-gap, 0,+90);
        // ↓ at TOP     → orbit -90° elevation
        Arrow([new(0,0), new(aw,0), new(aw/2,ah)],   (cw-aw)/2, gap,       0,-90);
        // ← at RIGHT   → orbit -90° azimuth
        Arrow([new(ah,0),new(ah,aw),new(0,aw/2)],    cw-ah-gap, (cw-aw)/2,-90, 0);
        // → at LEFT    → orbit +90° azimuth
        Arrow([new(0,0), new(0,aw), new(ah,aw/2)],   gap,       (cw-aw)/2,+90, 0);
    }
    private void Arrow(System.Windows.Point[] pts,double l,double t,double az,double el)
    {
        var p=new Polygon{Points=new PointCollection(pts),
            Fill=new SolidColorBrush(_arrowCol),Stroke=Brushes.Transparent,Cursor=Cursors.Hand};
        p.MouseEnter+=(_,_)=>p.Fill=new SolidColorBrush(_arrowHov);
        p.MouseLeave+=(_,_)=>p.Fill=new SolidColorBrush(_arrowCol);
        p.MouseLeftButtonDown+=(_,e)=>{RotateRequested?.Invoke(az,el);e.Handled=true;};
        Canvas.SetLeft(p,l); Canvas.SetTop(p,t);
        _arrowCanvas.Children.Add(p);
    }

    // ── Interaction ─────────────────────────────────────────────────────────────
    private static readonly int[] _opposites = [
        1, 0, 3, 2, 5, 4,       // 0-5   (FRONT<->BACK, RIGHT<->LEFT, TOP<->BOTTOM)
        11, 10, 13, 12,         // 6-9   (TF<->BB, TB<->BF, TR<->BL, TL<->BR)
        7, 6, 9, 8,             // 10-13 (BF<->TB, BB<->TF, BR<->TL, BL<->TR)
        17, 16, 15, 14          // 14-17 (FR<->BL, FL<->BR, BR<->FL, BL<->FR)
    ];

    private void OnRender(object? s,EventArgs e)
    {
        if(MainCamera==null) return;
        var ld=MainCamera.LookDirection; double len=ld.Length;
        if(len<0.0001) return;
        const double d=4.0;
        _navCam.Position=new Point3D(-ld.X/len*d,-ld.Y/len*d,-ld.Z/len*d);
        _navCam.LookDirection=new Vector3D(ld.X/len*d,ld.Y/len*d,ld.Z/len*d);
        _navCam.UpDirection=MainCamera.UpDirection;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h=Hit(e.GetPosition(_viewport));
        if(h==_hovered) return;
        Highlight(_hovered,false); Highlight(h,true); _hovered=h;
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    { base.OnMouseLeave(e); Highlight(_hovered,false); _hovered=-1; }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int h=Hit(e.GetPosition(_viewport));
        if(h>=0)
        {
            FaceClicked?.Invoke(e.ClickCount == 2 ? _opposites[h] : h);
            e.Handled=true;
        }
    }

    private void Highlight(int idx,bool on)
    {
        if(idx<0) return;
        if(idx<6){var m=on?_faceH[idx]:_faceN[idx];_faces[idx].Material=_faces[idx].BackMaterial=m;}
        else
            for(int i=0;i<24;i++)
                if(_stripDef[i]==idx)
                    _strips[i].Material=_strips[i].BackMaterial=on?_sHov:_sNorm;
    }

    private int Hit(Point pos)
    {
        int r=-1;
        VisualTreeHelper.HitTest(_viewport,null,res=>{
            if(res is RayMeshGeometry3DHitTestResult h){
                for(int i=0;i<6;i++)  if(h.ModelHit==_faces[i]){r=i;return HitTestResultBehavior.Stop;}
                for(int i=0;i<24;i++) if(h.ModelHit==_strips[i]){r=_stripDef[i];return HitTestResultBehavior.Stop;}
            }
            return HitTestResultBehavior.Stop;
        },new PointHitTestParameters(pos));
        return r;
    }
}
