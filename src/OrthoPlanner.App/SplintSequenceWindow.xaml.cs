using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

/// <summary>
/// Screen 1 (intermediate sequence + autorotation) and Screen 3 (final occlusion autorotation)
/// of the multi-step splint wizard.
///
/// When <paramref name="isFinalOcclusion"/> is false (Screen 1):
///   - Shows Maxilla-first / Mandible-first radio buttons.
///   - Viewport shows: moved jaw (surgical pos) + base jaw (CT pos).
///   - Mandible always autorotates about the condylar axis for clearance.
///   - "Original Occlusion" footer button sets BypassToOriginal and closes.
///
/// When <paramref name="isFinalOcclusion"/> is true (Screen 3):
///   - Hides sequence radios; both jaws shown at planned (moved) position.
///   - Mandible autorotation still available.
///   - Continue footer text changes to "Generate Final Splint".
/// </summary>
public partial class SplintSequenceWindow : Window
{
    // ── Jaw meshes ──────────────────────────────────────────────────────────
    private readonly float[] _upperBase;     // maxilla at CT position
    private readonly float[] _lowerBase;     // mandible at CT position
    private readonly float[]? _upperMoved;   // maxilla at surgical position (null = no surgical move)
    private readonly float[]? _lowerMoved;   // mandible at surgical position

    // ── Condyle axis ────────────────────────────────────────────────────────
    private (double X, double Y, double Z) _leftCondyle;
    private (double X, double Y, double Z) _rightCondyle;
    private (float x, float y, float z) _axisPoint;
    private (float x, float y, float z) _axisUnit;

    // ── Scene models ────────────────────────────────────────────────────────
    private MeshGeometryModel3D? _upperModel;
    private MeshGeometryModel3D? _lowerModel;
    private MeshGeometryModel3D? _leftCondyleSphere;
    private MeshGeometryModel3D? _rightCondyleSphere;
    private RotateTransform3D? _mandibleRotation;
    private EventHandler? _renderingHandler;

    // ── Clearance heightfield (built from whichever jaw is "upper") ─────────
    private float[]? _maxLowZ;
    private bool[]?  _maxHas;
    private float _gridMinX, _gridMinY, _gridCell;
    private int _gridNx, _gridNy;
    private float _baseClearance;
    private float _openSignCache;

    // ── Mode ─────────────────────────────────────────────────────────────────
    private readonly bool _isFinalOcclusion;
    private bool _isMaxillaFirst = true; // Screen 1 default

    // ── Condyle drag state ──────────────────────────────────────────────────
    private bool _adjustAxisMode;
    private enum DragTarget { None, Left, Right }
    private DragTarget _dragging = DragTarget.None;
    private Point _dragStart;
    private const float SphereRadius = 4f;

    // ── Results ─────────────────────────────────────────────────────────────
    public bool   Accepted            { get; private set; }
    public bool   IsMaxillaFirst      { get; private set; }
    public double AutorotationDegrees { get; private set; }
    public float[]? RotatedMandible   { get; private set; }
    public bool   BypassToOriginal    { get; private set; }
    public (double X, double Y, double Z)? UpdatedLeftCondyle  { get; private set; }
    public (double X, double Y, double Z)? UpdatedRightCondyle { get; private set; }

    public SplintSequenceWindow(
        float[] upperBase, float[] lowerBase,
        float[]? upperMoved, float[]? lowerMoved,
        (double X, double Y, double Z) leftCondyle,
        (double X, double Y, double Z) rightCondyle,
        bool isFinalOcclusion = false,
        bool maxillaFirstDefault = true)
    {
        InitializeComponent();

        _upperBase    = upperBase;
        _lowerBase    = lowerBase;
        _upperMoved   = upperMoved;
        _lowerMoved   = lowerMoved;
        _leftCondyle  = leftCondyle;
        _rightCondyle = rightCondyle;
        _isFinalOcclusion = isFinalOcclusion;
        _isMaxillaFirst   = maxillaFirstDefault;

        if (isFinalOcclusion)
        {
            SequenceSection.Visibility    = Visibility.Collapsed;
            OriginalOcclusionBtn.Visibility = Visibility.Collapsed;
            HeaderTitle.Text    = "Step 3: Final occlusion — autorotation";
            HeaderSubtitle.Text = "Both jaws at planned surgical position. Set mandibular opening for the final wafer, then click Generate Final Splint.";
            ContinueBtn.Content = "✔ Generate Final Splint";
        }
        else
        {
            MaxillaFirstRadio.IsChecked  = maxillaFirstDefault;
            MandibleFirstRadio.IsChecked = !maxillaFirstDefault;
        }

        RebuildAxis();

        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        _renderingHandler = (s, _) => UpdateHeadlamp(MainCamera, Headlamp, Backlamp);
        CompositionTarget.Rendering += _renderingHandler;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  AXIS MATH
    // ════════════════════════════════════════════════════════════════════════
    private void RebuildAxis()
    {
        float ax = (float)(_leftCondyle.X - _rightCondyle.X);
        float ay = (float)(_leftCondyle.Y - _rightCondyle.Y);
        float az = (float)(_leftCondyle.Z - _rightCondyle.Z);
        float len = MathF.Sqrt(ax * ax + ay * ay + az * az);
        if (len < 1e-6f) { ax = 1; ay = 0; az = 0; len = 1; }
        _axisUnit  = (ax / len, ay / len, az / len);
        _axisPoint = ((float)((_leftCondyle.X + _rightCondyle.X) * 0.5),
                      (float)((_leftCondyle.Y + _rightCondyle.Y) * 0.5),
                      (float)((_leftCondyle.Z + _rightCondyle.Z) * 0.5));
        _openSignCache = 0; // invalidate cache

        CondyleLeftLabel.Text  = $"L: ({_leftCondyle.X:F1}, {_leftCondyle.Y:F1}, {_leftCondyle.Z:F1})";
        CondyleRightLabel.Text = $"R: ({_rightCondyle.X:F1}, {_rightCondyle.Y:F1}, {_rightCondyle.Z:F1})";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SCENE LOADING
    // ════════════════════════════════════════════════════════════════════════
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildScene();
        BuildMaxillaHeightfield();
        _baseClearance = ClearanceForSignedAngle(0f);
        CenterLateral();
        UpdateAngle();
    }

    private float[] CurrentUpperMesh => _isFinalOcclusion
        ? (_upperMoved ?? _upperBase)
        : (_isMaxillaFirst ? (_upperMoved ?? _upperBase) : _upperBase);

    private float[] CurrentLowerMesh => _isFinalOcclusion
        ? (_lowerMoved ?? _lowerBase)
        : _lowerBase; // mandible always from base, rotated for clearance

    private void RebuildScene()
    {
        SceneGroup.Children.Clear();
        _upperModel = _lowerModel = null;
        _leftCondyleSphere = _rightCondyleSphere = null;

        // Jaw meshes
        _upperModel = MeshHelper.BuildModel3D(CurrentUpperMesh, 235, 225, 205);
        SceneGroup.Children.Add(_upperModel);

        _mandibleRotation = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z), 0),
            _axisPoint.x, _axisPoint.y, _axisPoint.z);
        _lowerModel = MeshHelper.BuildModel3D(CurrentLowerMesh, 210, 215, 235);
        _lowerModel.Transform = _mandibleRotation;
        SceneGroup.Children.Add(_lowerModel);

        // Axis line between condyle markers
        AddAxisLine();

        // Condyle sphere markers
        _leftCondyleSphere  = BuildSphere((float)_leftCondyle.X,  (float)_leftCondyle.Y,  (float)_leftCondyle.Z,  SphereRadius, 220, 160, 80);
        _rightCondyleSphere = BuildSphere((float)_rightCondyle.X, (float)_rightCondyle.Y, (float)_rightCondyle.Z, SphereRadius, 80, 160, 220);
        SceneGroup.Children.Add(_leftCondyleSphere);
        SceneGroup.Children.Add(_rightCondyleSphere);

        // Reset heightfield cache
        _maxLowZ = null; _maxHas = null;
        BuildMaxillaHeightfield();
        _openSignCache = 0;
        _baseClearance = ClearanceForSignedAngle(0f);

        // Reapply current angle
        double currentAngle = Math.Round(AngleSlider.Value, 1);
        if (_mandibleRotation != null)
            ((AxisAngleRotation3D)_mandibleRotation.Rotation).Angle = _openSignOrDefault() * currentAngle;

        UpdateClearanceDisplay(currentAngle);
    }

    private static MeshGeometryModel3D BuildSphere(float cx, float cy, float cz, float r, byte red, byte green, byte blue)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(cx, cy, cz), r, 12, 8);
        var geom = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh());
        return new MeshGeometryModel3D
        {
            Geometry = geom,
            Material = MeshHelper.CreatePhongMaterial(red, green, blue)
        };
    }

    private void AddAxisLine()
    {
        // Draw axis as a thin cylinder between the two condyle centers
        float lx = (float)_leftCondyle.X,  ly = (float)_leftCondyle.Y,  lz = (float)_leftCondyle.Z;
        float rx = (float)_rightCondyle.X, ry = (float)_rightCondyle.Y, rz = (float)_rightCondyle.Z;
        float len = MathF.Sqrt((lx-rx)*(lx-rx) + (ly-ry)*(ly-ry) + (lz-rz)*(lz-rz));
        if (len < 1f) return;

        // Build a thin cylinder along the axis
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddCylinder(
            new System.Numerics.Vector3(lx, ly, lz),
            new System.Numerics.Vector3(rx, ry, rz),
            1.2f, 8);
        var lineModel = new MeshGeometryModel3D
        {
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material = MeshHelper.CreatePhongMaterial(255, 220, 80, 160)
        };
        SceneGroup.Children.Add(lineModel);
    }

    private void CenterLateral()
    {
        float minX=float.MaxValue,minY=float.MaxValue,minZ=float.MaxValue;
        float maxX=float.MinValue,maxY=float.MinValue,maxZ=float.MinValue;
        void Acc(float[] m){ for(int i=0;i+2<m.Length;i+=3){
            float x=m[i],y=m[i+1],z=m[i+2];
            if(x<minX)minX=x; if(x>maxX)maxX=x;
            if(y<minY)minY=y; if(y>maxY)maxY=y;
            if(z<minZ)minZ=z; if(z>maxZ)maxZ=z; } }
        Acc(CurrentUpperMesh); Acc(CurrentLowerMesh);
        if (minX>maxX) return;

        var pivot = new Point3D((minX+maxX)/2.0,(minY+maxY)/2.0,(minZ+maxZ)/2.0);
        double diag = Math.Sqrt(Math.Pow(maxX-minX,2)+Math.Pow(maxY-minY,2)+Math.Pow(maxZ-minZ,2));
        double dist = Math.Max(diag*1.1,40);

        var look = new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z);
        look.Normalize();
        MainCamera.Position      = new Point3D(pivot.X - look.X*dist, pivot.Y - look.Y*dist, pivot.Z - look.Z*dist);
        MainCamera.LookDirection = look * dist;
        MainCamera.UpDirection   = new Vector3D(0,0,1);
        MainViewport.FixedRotationPointEnabled = true;
        MainViewport.FixedRotationPoint = pivot;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        SceneGroup.Children.Clear();
        if (MainViewport.EffectsManager is IDisposable d) { d.Dispose(); MainViewport.EffectsManager = null!; }
    }

    private static void UpdateHeadlamp(
        HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam, DirectionalLight3D front, DirectionalLight3D back)
    {
        if (cam == null) return;
        var dir = cam.LookDirection;
        if (dir.Length < 0.001) return;
        dir.Normalize();
        var f = new Vector3D(dir.X, dir.Y, dir.Z);
        var b = new Vector3D(-dir.X, -dir.Y, -dir.Z);
        if (Math.Abs(front.Direction.X - f.X) > 1e-4 ||
            Math.Abs(front.Direction.Y - f.Y) > 1e-4 ||
            Math.Abs(front.Direction.Z - f.Z) > 1e-4)
        { front.Direction = f; back.Direction = b; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SEQUENCE RADIO
    // ════════════════════════════════════════════════════════════════════════
    private void SequenceRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool nowMaxillaFirst = MaxillaFirstRadio.IsChecked == true;
        if (nowMaxillaFirst == _isMaxillaFirst) return;
        _isMaxillaFirst = nowMaxillaFirst;

        // Swap which jaw mesh is shown as the stable arch
        _openSignCache = 0;
        RebuildScene();
        CenterLateral();
        UpdateAngle();

        ViewportLabel.Text = _isMaxillaFirst
            ? "Lateral view — maxilla at surgical position, mandible at CT position"
            : "Lateral view — mandible at surgical position, maxilla at CT position";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ANGLE CONTROLS
    // ════════════════════════════════════════════════════════════════════════
    private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateAngle();
    private void IncFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value + 0.1);
    private void DecFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value - 0.1);
    private void IncCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value + 1.0);
    private void DecCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value - 1.0);
    private double Clamp(double v) => Math.Round(Math.Clamp(v, AngleSlider.Minimum, AngleSlider.Maximum), 1);

    private void UpdateAngle()
    {
        if (_mandibleRotation == null) return;
        double mag = Math.Round(AngleSlider.Value, 1);
        double signed = _openSignOrDefault() * mag;
        AngleLabel.Text = $"{mag:F1}°";
        ((AxisAngleRotation3D)_mandibleRotation.Rotation).Angle = signed;
        UpdateClearanceDisplay(mag);
    }

    private void UpdateClearanceDisplay(double mag)
    {
        double signed = _openSignOrDefault() * mag;
        float clearance = ClearanceForSignedAngle((float)signed);
        if (float.IsNaN(clearance))
        {
            ClearanceLabel.Text = "—";
            ClearanceDetail.Text = "No occlusal overlap detected";
            WarningLabel.Text = "";
        }
        else if (clearance < 0)
        {
            ClearanceLabel.Text    = $"⚠ {clearance:F1} mm";
            ClearanceLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0));
            ClearanceDetail.Text   = $"Closed baseline: {_baseClearance:F1} mm";
            WarningLabel.Text      = $"Meshes intersecting — premature contact at {Math.Abs(clearance):F1} mm overlap";
        }
        else
        {
            ClearanceLabel.Text = $"≈ {clearance:F1} mm";
            ClearanceLabel.Foreground = new SolidColorBrush(Colors.White);
            ClearanceDetail.Text = $"Closed baseline: {_baseClearance:F1} mm";
            WarningLabel.Text = "";
        }
    }

    private double _openSignOrDefault()
    {
        float s = OpenSign;
        return s != 0 ? s : 1.0;
    }

    private float OpenSign
    {
        get
        {
            if (_openSignCache != 0) return _openSignCache;
            if (_maxLowZ == null) return 0;
            float cPlus  = ClearanceForSignedAngle(+1.0f);
            float cMinus = ClearanceForSignedAngle(-1.0f);
            float vp = float.IsNaN(cPlus)  ? -1f : cPlus;
            float vm = float.IsNaN(cMinus) ? -1f : cMinus;
            _openSignCache = vp >= vm ? 1f : -1f;
            return _openSignCache;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CLEARANCE HEIGHTFIELD (mirrors MandibleAutorotationWindow exactly)
    // ════════════════════════════════════════════════════════════════════════
    private void BuildMaxillaHeightfield()
    {
        var mesh = CurrentUpperMesh;
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        for (int i=0;i+2<mesh.Length;i+=3){
            float x=mesh[i],y=mesh[i+1];
            if(x<minX)minX=x; if(x>maxX)maxX=x;
            if(y<minY)minY=y; if(y>maxY)maxY=y;
        }
        if (minX>maxX) return;
        _gridCell = 2.0f;
        _gridMinX = minX; _gridMinY = minY;
        _gridNx = Math.Max(1,(int)((maxX-minX)/_gridCell)+1);
        _gridNy = Math.Max(1,(int)((maxY-minY)/_gridCell)+1);
        _maxLowZ = new float[_gridNx*_gridNy];
        _maxHas  = new bool [_gridNx*_gridNy];
        for (int i=0;i<_maxLowZ.Length;i++) _maxLowZ[i]=float.MaxValue;
        for (int i=0;i+2<mesh.Length;i+=3){
            float x=mesh[i],y=mesh[i+1],z=mesh[i+2];
            int gx=(int)((x-_gridMinX)/_gridCell),gy=(int)((y-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if(z<_maxLowZ[idx]){_maxLowZ[idx]=z;_maxHas[idx]=true;}
        }
    }

    private float ClearanceForSignedAngle(float signedDegrees)
    {
        if (_maxLowZ==null||_maxHas==null) return float.NaN;
        var mandibleMesh = CurrentLowerMesh;
        var manHiZ = new float[_gridNx*_gridNy];
        var manHas = new bool [_gridNx*_gridNy];
        for (int i=0;i<manHiZ.Length;i++) manHiZ[i]=float.MinValue;
        float rad=signedDegrees*MathF.PI/180f;
        float c=MathF.Cos(rad),s=MathF.Sin(rad);
        float kx=_axisUnit.x,ky=_axisUnit.y,kz=_axisUnit.z;
        for (int i=0;i+2<mandibleMesh.Length;i+=3){
            float px=mandibleMesh[i]-_axisPoint.x,py=mandibleMesh[i+1]-_axisPoint.y,pz=mandibleMesh[i+2]-_axisPoint.z;
            float dot=kx*px+ky*py+kz*pz;
            float crx=ky*pz-kz*py,cry=kz*px-kx*pz,crz=kx*py-ky*px;
            float rx=_axisPoint.x+px*c+crx*s+kx*dot*(1-c);
            float ry=_axisPoint.y+py*c+cry*s+ky*dot*(1-c);
            float rz=_axisPoint.z+pz*c+crz*s+kz*dot*(1-c);
            int gx=(int)((rx-_gridMinX)/_gridCell),gy=(int)((ry-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if(rz>manHiZ[idx]){manHiZ[idx]=rz;manHas[idx]=true;}
        }
        float minGap=float.MaxValue;
        for (int i=0;i<manHiZ.Length;i++)
            if(_maxHas[i]&&manHas[i]){ float gap=_maxLowZ[i]-manHiZ[i]; if(gap<minGap)minGap=gap; }
        return minGap==float.MaxValue?float.NaN:minGap;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONDYLE DRAG GIZMO
    // ════════════════════════════════════════════════════════════════════════
    private void AdjustAxisToggle_Checked(object sender, RoutedEventArgs e)
    {
        _adjustAxisMode = true;
        AxisHintLabel.Text = "Drag the orange (L) or blue (R) condyle spheres in the viewport to adjust the axis.";
        if (_leftCondyleSphere  != null) _leftCondyleSphere.Material  = MeshHelper.CreatePhongMaterial(255, 180, 40);
        if (_rightCondyleSphere != null) _rightCondyleSphere.Material = MeshHelper.CreatePhongMaterial(40, 180, 255);
    }

    private void AdjustAxisToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _adjustAxisMode = false;
        _dragging = DragTarget.None;
        AxisHintLabel.Text = "";
        // Restore normal sphere colors
        if (_leftCondyleSphere  != null) _leftCondyleSphere.Material  = MeshHelper.CreatePhongMaterial(220, 160, 80);
        if (_rightCondyleSphere != null) _rightCondyleSphere.Material = MeshHelper.CreatePhongMaterial(80, 160, 220);
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_adjustAxisMode || e.ChangedButton != MouseButton.Left) return;

        // Project both condyle positions to screen and pick whichever is closer to click
        var clickPos = e.GetPosition(MainViewport);
        double distL = ProjectedScreenDist(_leftCondyle,  clickPos);
        double distR = ProjectedScreenDist(_rightCondyle, clickPos);

        double hitRadius = 25.0; // pixels
        if (distL < hitRadius && distL <= distR)
        {
            _dragging  = DragTarget.Left;
            _dragStart = clickPos;
            MainViewport.IsRotationEnabled = false;
            MainViewport.IsPanEnabled      = false;
            e.Handled = true;
        }
        else if (distR < hitRadius && distR < distL)
        {
            _dragging  = DragTarget.Right;
            _dragStart = clickPos;
            MainViewport.IsRotationEnabled = false;
            MainViewport.IsPanEnabled      = false;
            e.Handled = true;
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == DragTarget.None || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(MainViewport);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;
        _dragStart = pos;

        // Scale pixels to world units based on camera distance
        double pixelsPerMm = EstimatePixelsPerMm();
        if (pixelsPerMm < 1e-6) return;
        double wx = dx / pixelsPerMm;
        double wy = -dy / pixelsPerMm; // screen Y is inverted vs world Z

        // Determine which axes are screen-X and screen-Y (from camera right/up)
        var camRight = GetCameraRight();
        var camUp    = GetCameraUp();

        double nx = camRight.X * wx + camUp.X * wy;
        double ny = camRight.Y * wx + camUp.Y * wy;
        double nz = camRight.Z * wx + camUp.Z * wy;

        if (_dragging == DragTarget.Left)
            _leftCondyle  = (_leftCondyle.X  + nx, _leftCondyle.Y  + ny, _leftCondyle.Z  + nz);
        else
            _rightCondyle = (_rightCondyle.X + nx, _rightCondyle.Y + ny, _rightCondyle.Z + nz);

        // Reposition sphere model
        if (_dragging == DragTarget.Left  && _leftCondyleSphere  != null)
            _leftCondyleSphere.Transform  = TranslateFromDelta(_leftCondyleSphere.Transform,  nx, ny, nz);
        if (_dragging == DragTarget.Right && _rightCondyleSphere != null)
            _rightCondyleSphere.Transform = TranslateFromDelta(_rightCondyleSphere.Transform, nx, ny, nz);


        RebuildAxis();
        RebuildAxisLineOnly();
        UpdateMandibleRotationAxis();
        UpdateAngle();
        e.Handled = true;
    }

    private static Transform3D TranslateFromDelta(Transform3D current, double dx, double dy, double dz)
    {
        var m = current.Value;
        m.Translate(new Vector3D(dx, dy, dz));
        return new MatrixTransform3D(m);
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == DragTarget.None) return;
        _dragging = DragTarget.None;
        MainViewport.IsRotationEnabled = true;
        MainViewport.IsPanEnabled      = true;
        e.Handled = true;
    }

    private void RebuildAxisLineOnly()
    {
        // Remove old axis line (it's always the 3rd child: upper, lower, axisLine, sphereL, sphereR)
        // Find by tag approach: rebuild the whole scene is simpler given low complexity
        // Just rebuild scene without resetting camera
        var savedAngle = AngleSlider.Value;
        RebuildScene();
        AngleSlider.Value = savedAngle;
    }

    private void UpdateMandibleRotationAxis()
    {
        if (_mandibleRotation?.Rotation is not AxisAngleRotation3D rot) return;
        rot.Axis = new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z);
        _mandibleRotation.CenterX = _axisPoint.x;
        _mandibleRotation.CenterY = _axisPoint.y;
        _mandibleRotation.CenterZ = _axisPoint.z;
    }

    // ── Camera helpers for drag ──────────────────────────────────────────────
    private double ProjectedScreenDist((double X, double Y, double Z) worldPt, Point screenPt)
    {
        // Rough projection: ignore perspective distortion, use camera right/up
        var toPoint = new Vector3D(worldPt.X - MainCamera.Position.X,
                                   worldPt.Y - MainCamera.Position.Y,
                                   worldPt.Z - MainCamera.Position.Z);
        double dist = toPoint.Length;
        if (dist < 1e-6) return double.MaxValue;

        double pixPerMm = EstimatePixelsPerMm();
        var right = GetCameraRight();
        var up    = GetCameraUp();
        double sx = Vector3D.DotProduct(toPoint, right) * pixPerMm + MainViewport.ActualWidth  * 0.5;
        double sy = -Vector3D.DotProduct(toPoint, up)  * pixPerMm + MainViewport.ActualHeight * 0.5;
        return Math.Sqrt(Math.Pow(sx - screenPt.X, 2) + Math.Pow(sy - screenPt.Y, 2));
    }

    private double EstimatePixelsPerMm()
    {
        var look = MainCamera.LookDirection;
        double camDist = look.Length;
        if (camDist < 1e-6) return 1;
        double fovRad  = MainCamera.FieldOfView * Math.PI / 180.0;
        double worldH  = 2.0 * camDist * Math.Tan(fovRad * 0.5);
        return worldH > 0 ? MainViewport.ActualHeight / worldH : 1;
    }

    private Vector3D GetCameraRight()
    {
        var look = MainCamera.LookDirection; look.Normalize();
        var up   = MainCamera.UpDirection;   up.Normalize();
        var right = Vector3D.CrossProduct(look, up); right.Normalize();
        return right;
    }

    private Vector3D GetCameraUp()
    {
        var up = MainCamera.UpDirection; up.Normalize();
        return up;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FOOTER BUTTONS
    // ════════════════════════════════════════════════════════════════════════
    private void OriginalOcclusion_Click(object sender, RoutedEventArgs e)
    {
        BypassToOriginal = true;
        Accepted = false;
        Close();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        double mag    = Math.Round(AngleSlider.Value, 1);
        double signed = _openSignOrDefault() * mag;

        AutorotationDegrees = signed;
        IsMaxillaFirst      = _isMaxillaFirst;

        RotatedMandible = Math.Abs(signed) < 0.01
            ? CurrentLowerMesh
            : SplintEngine.RotateMesh(CurrentLowerMesh, _axisPoint, _axisUnit, (float)signed);

        if (_adjustAxisMode)
        {
            UpdatedLeftCondyle  = _leftCondyle;
            UpdatedRightCondyle = _rightCondyle;
        }

        Accepted = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
