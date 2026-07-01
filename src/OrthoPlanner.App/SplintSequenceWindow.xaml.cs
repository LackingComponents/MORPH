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
/// </summary>
public partial class SplintSequenceWindow : Window
{
    // ── Jaw meshes ──────────────────────────────────────────────────────────
    private readonly float[] _upperBase;
    private readonly float[] _lowerBase;
    private readonly float[]? _upperMoved;
    private readonly float[]? _lowerMoved;

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
    private LineGeometryModel3D? _axisLineVisual;
    private RotateTransform3D? _mandibleRotation;
    private EventHandler? _renderingHandler;
    private readonly float[][]? _ramiMeshes;

    // ── Clearance heightfield ────────────────────────────────────────────────
    private float[]? _maxLowZ;
    private bool[]?  _maxHas;
    private float _gridMinX, _gridMinY, _gridCell;
    private int _gridNx, _gridNy;
    private float _baseClearance;
    private float _openSignCache;

    // ── Mode ─────────────────────────────────────────────────────────────────
    private readonly bool _isFinalOcclusion;
    private bool _isMaxillaFirst = true;

    // ── Condyle drag (CamPlane style, same as osteotomy windows) ───────────
    private bool _adjustAxisMode;
    private enum DragTarget { None, Left, Right }
    private DragTarget _dragging = DragTarget.None;
    private Point3D _dragAnchor;   // world-space anchor for plane intersection
    private const float SphereRadius = 1.5f;
    private const double SphereHitPx = 28.0;  // screen-pixel pick radius

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
        bool maxillaFirstDefault = true,
        float[][]? ramiMeshes = null)
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
        _ramiMeshes       = ramiMeshes;

        if (isFinalOcclusion)
        {
            SequenceSection.Visibility      = Visibility.Collapsed;
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
        _renderingHandler = (_, _) => UpdateHeadlamp(MainCamera, Headlamp, Backlamp);
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
        _openSignCache = 0;

        if (IsLoaded)
        {
            CondyleLeftLabel.Text  = $"L: ({_leftCondyle.X:F1}, {_leftCondyle.Y:F1}, {_leftCondyle.Z:F1})";
            CondyleRightLabel.Text = $"R: ({_rightCondyle.X:F1}, {_rightCondyle.Y:F1}, {_rightCondyle.Z:F1})";
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SCENE LOADING
    // ════════════════════════════════════════════════════════════════════════
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CondyleLeftLabel.Text  = $"L: ({_leftCondyle.X:F1}, {_leftCondyle.Y:F1}, {_leftCondyle.Z:F1})";
        CondyleRightLabel.Text = $"R: ({_rightCondyle.X:F1}, {_rightCondyle.Y:F1}, {_rightCondyle.Z:F1})";

        RebuildScene();
        CenterLateral();
        UpdateAngle();
    }

    // Which mesh is the "stable" upper arch and which is the "rotating" lower?
    // In final-occlusion mode both are at surgical position.
    // In intermediate mode:
    //   maxilla-first → upper=upperMoved(surgical), lower=lowerBase(CT)
    //   mandible-first → upper=upperBase(CT), lower=lowerMoved(surgical)
    // The mandible always autorotates about the condyle regardless of which was first.
    private float[] CurrentUpperMesh =>
        _isFinalOcclusion
            ? (_upperMoved ?? _upperBase)
            : (_isMaxillaFirst ? (_upperMoved ?? _upperBase) : _upperBase);

    private float[] CurrentLowerMesh =>
        _isFinalOcclusion
            ? (_lowerMoved ?? _lowerBase)
            : (_isMaxillaFirst ? _lowerBase : (_lowerMoved ?? _lowerBase));

    private void RebuildScene()
    {
        SceneGroup.Children.Clear();
        _upperModel = _lowerModel = null;
        _leftCondyleSphere = _rightCondyleSphere = null;
        _axisLineVisual = null;
        _maxLowZ = null; _maxHas = null;

        // Upper arch
        _upperModel = MeshHelper.BuildModel3D(CurrentUpperMesh, 235, 225, 205);
        SceneGroup.Children.Add(_upperModel);

        // Rami in original (CT) position — ghost context meshes
        if (_ramiMeshes != null)
        {
            foreach (var ramus in _ramiMeshes)
            {
                if (ramus != null && ramus.Length >= 9)
                {
                    var ramusModel = MeshHelper.BuildModel3D(ramus, 200, 190, 175, 100);
                    SceneGroup.Children.Add(ramusModel);
                }
            }
        }

        // Lower arch (subject to autorotation)
        _mandibleRotation = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z), 0),
            _axisPoint.x, _axisPoint.y, _axisPoint.z);
        _lowerModel = MeshHelper.BuildModel3D(CurrentLowerMesh, 210, 215, 235);
        _lowerModel.Transform = _mandibleRotation;
        SceneGroup.Children.Add(_lowerModel);

        // Axis — red line matching CondyleSplitWindow style
        AddAxisLine();

        // Condyle sphere gizmos
        _leftCondyleSphere  = BuildSphere((float)_leftCondyle.X,  (float)_leftCondyle.Y,  (float)_leftCondyle.Z,  SphereRadius, 0, 245, 255);
        _rightCondyleSphere = BuildSphere((float)_rightCondyle.X, (float)_rightCondyle.Y, (float)_rightCondyle.Z, SphereRadius, 0, 245, 255);
        SceneGroup.Children.Add(_leftCondyleSphere);
        SceneGroup.Children.Add(_rightCondyleSphere);

        // Rebuild heightfield and open-sign cache
        BuildMaxillaHeightfield();
        _openSignCache = 0;
        _baseClearance = ClearanceForSignedAngle(0f);
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
        float lx = (float)_leftCondyle.X,  ly = (float)_leftCondyle.Y,  lz = (float)_leftCondyle.Z;
        float rx = (float)_rightCondyle.X, ry = (float)_rightCondyle.Y, rz = (float)_rightCondyle.Z;

        // Compute unit vector along axis for 15mm extensions past each condyle (matching CondyleSplitWindow)
        float dx = lx - rx, dy = ly - ry, dz = lz - rz;
        float len = MathF.Sqrt(dx*dx + dy*dy + dz*dz);
        if (len < 1f) return;
        float ux = dx/len, uy = dy/len, uz = dz/len;
        const float ext = 15f;

        var p1 = new System.Numerics.Vector3(lx + ux*ext, ly + uy*ext, lz + uz*ext);
        var p2 = new System.Numerics.Vector3(rx - ux*ext, ry - uy*ext, rz - uz*ext);

        var lineBuilder = new HelixToolkit.SharpDX.LineBuilder();
        lineBuilder.AddLine(p1, p2);

        _axisLineVisual = new LineGeometryModel3D
        {
            Geometry  = lineBuilder.ToLineGeometry3D(),
            Color     = System.Windows.Media.Color.FromRgb(0, 245, 255), // neon azure
            Thickness = 3
        };
        SceneGroup.Children.Add(_axisLineVisual);
    }

    private void CenterLateral()
    {
        float minX=float.MaxValue, minY=float.MaxValue, minZ=float.MaxValue;
        float maxX=float.MinValue, maxY=float.MinValue, maxZ=float.MinValue;
        void Acc(float[] m) {
            for (int i=0; i+2<m.Length; i+=3) {
                float x=m[i],y=m[i+1],z=m[i+2];
                if(x<minX)minX=x; if(x>maxX)maxX=x;
                if(y<minY)minY=y; if(y>maxY)maxY=y;
                if(z<minZ)minZ=z; if(z>maxZ)maxZ=z;
            }
        }
        Acc(CurrentUpperMesh); Acc(CurrentLowerMesh);
        if (minX > maxX) return;

        var pivot = new Point3D((minX+maxX)/2.0, (minY+maxY)/2.0, (minZ+maxZ)/2.0);
        double diag = Math.Sqrt(Math.Pow(maxX-minX,2)+Math.Pow(maxY-minY,2)+Math.Pow(maxZ-minZ,2));
        double dist = Math.Max(diag*1.1, 40);

        // View along the condylar axis (lateral view)
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
    //  SEQUENCE RADIO  (Screen 1 only)
    // ════════════════════════════════════════════════════════════════════════
    private void SequenceRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        // Use sender identity: WPF fires Checked on the radio that just became checked.
        // Reading MaxillaFirstRadio.IsChecked here is unreliable because WPF may not yet
        // have unchecked the sibling when this event fires.
        bool nowMaxillaFirst = ReferenceEquals(sender, MaxillaFirstRadio);
        if (nowMaxillaFirst == _isMaxillaFirst) return;
        _isMaxillaFirst = nowMaxillaFirst;

        // Rebuild scene with new jaw positions
        double savedAngle = AngleSlider.Value;
        RebuildScene();
        CenterLateral();

        // Restore angle (re-validates open sign for new arch pairing)
        AngleSlider.Value = savedAngle;
        UpdateAngle();

        ViewportLabel.Text = _isMaxillaFirst
            ? "Lateral — maxilla at surgical position, mandible at CT position"
            : "Lateral — mandible at surgical position, maxilla at CT position";
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ANGLE CONTROLS
    // ════════════════════════════════════════════════════════════════════════
    private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateAngle();

    private void IncFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value + 0.1);
    private void DecFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value - 0.1);
    private void IncCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value + 1.0);
    private void DecCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value - 1.0);
    private double Clamp(double v) => Math.Round(Math.Clamp(v, AngleSlider.Minimum, AngleSlider.Maximum), 1);

    private void UpdateAngle()
    {
        if (_mandibleRotation == null) return;
        double mag    = Math.Round(AngleSlider.Value, 1);
        double signed = OpenSignOrDefault() * mag;
        AngleLabel.Text = $"{mag:F1}°";
        ((AxisAngleRotation3D)_mandibleRotation.Rotation).Angle = signed;
        UpdateClearanceDisplay(mag, signed);
    }

    private void UpdateClearanceDisplay(double mag, double signed)
    {
        float clearance = ClearanceForSignedAngle((float)signed);
        if (float.IsNaN(clearance))
        {
            ClearanceLabel.Text = "—";
            ClearanceLabel.Foreground = new SolidColorBrush(Colors.White);
            ClearanceDetail.Text = "No occlusal overlap detected";
            WarningLabel.Text = "";
        }
        else if (clearance < 0)
        {
            ClearanceLabel.Text       = $"⚠ {clearance:F2} mm";
            ClearanceLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0));
            ClearanceDetail.Text      = $"Closed baseline: {_baseClearance:F2} mm";
            WarningLabel.Text         = $"Meshes intersecting — premature contact at {Math.Abs(clearance):F2} mm overlap";
        }
        else
        {
            ClearanceLabel.Text       = $"≈ {clearance:F2} mm";
            ClearanceLabel.Foreground = new SolidColorBrush(Colors.White);
            ClearanceDetail.Text      = $"Closed baseline: {_baseClearance:F2} mm";
            WarningLabel.Text         = clearance < 0.5f ? "⚠ Very tight clearance — consider increasing angle" : "";
        }
    }

    private double OpenSignOrDefault()
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
            // Test a small rotation in each direction; whichever gives MORE clearance is the opening direction
            float cPlus  = ClearanceForSignedAngle(+1.5f);
            float cMinus = ClearanceForSignedAngle(-1.5f);
            float vp = float.IsNaN(cPlus)  ? -999f : cPlus;
            float vm = float.IsNaN(cMinus) ? -999f : cMinus;
            _openSignCache = vp >= vm ? 1f : -1f;
            return _openSignCache;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CLEARANCE HEIGHTFIELD
    //  Uses per-column min/max Z from the two arch surfaces.
    //  Clearance = min over all (i,j) columns where both arches have verts of
    //  (upperMinZ[i,j] - rotatedLowerMaxZ[i,j]).
    //  Positive = gap, negative = interpenetration depth.
    // ════════════════════════════════════════════════════════════════════════
    private void BuildMaxillaHeightfield()
    {
        var mesh = CurrentUpperMesh;
        float minX=float.MaxValue, minY=float.MaxValue;
        float maxX=float.MinValue, maxY=float.MinValue;
        for (int i=0; i+2<mesh.Length; i+=3) {
            float x=mesh[i], y=mesh[i+1];
            if(x<minX)minX=x; if(x>maxX)maxX=x;
            if(y<minY)minY=y; if(y>maxY)maxY=y;
        }
        if (minX > maxX) return;

        _gridCell = 1.5f;   // 1.5 mm grid — finer than original 2.0 mm
        _gridMinX = minX; _gridMinY = minY;
        _gridNx   = Math.Max(1, (int)((maxX - minX) / _gridCell) + 1);
        _gridNy   = Math.Max(1, (int)((maxY - minY) / _gridCell) + 1);

        // Track the MINIMUM Z (inferior-most surface of the upper arch)
        _maxLowZ = new float[_gridNx * _gridNy];
        _maxHas  = new bool [_gridNx * _gridNy];
        for (int i=0; i<_maxLowZ.Length; i++) _maxLowZ[i] = float.MaxValue;

        for (int i=0; i+2<mesh.Length; i+=3) {
            float x=mesh[i], y=mesh[i+1], z=mesh[i+2];
            int gx=(int)((x-_gridMinX)/_gridCell), gy=(int)((y-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if (z < _maxLowZ[idx]) { _maxLowZ[idx]=z; _maxHas[idx]=true; }
        }
    }

    private float ClearanceForSignedAngle(float signedDegrees)
    {
        if (_maxLowZ == null || _maxHas == null) return float.NaN;

        var mandibleMesh = CurrentLowerMesh;
        // Build max-Z (superior-most point) for each column in the rotated mandible
        var manHiZ = new float[_gridNx * _gridNy];
        var manHas = new bool [_gridNx * _gridNy];
        for (int i=0; i<manHiZ.Length; i++) manHiZ[i] = float.MinValue;

        float rad = signedDegrees * MathF.PI / 180f;
        float c   = MathF.Cos(rad), s = MathF.Sin(rad);
        float kx  = _axisUnit.x, ky = _axisUnit.y, kz = _axisUnit.z;
        float ax  = _axisPoint.x, ay = _axisPoint.y, az = _axisPoint.z;

        for (int i=0; i+2<mandibleMesh.Length; i+=3) {
            // Rodrigues rotation about condylar axis
            float px=mandibleMesh[i]-ax, py=mandibleMesh[i+1]-ay, pz=mandibleMesh[i+2]-az;
            float dot = kx*px + ky*py + kz*pz;
            float crx = ky*pz - kz*py, cry = kz*px - kx*pz, crz = kx*py - ky*px;
            float rx = ax + px*c + crx*s + kx*dot*(1-c);
            float ry = ay + py*c + cry*s + ky*dot*(1-c);
            float rz = az + pz*c + crz*s + kz*dot*(1-c);

            int gx=(int)((rx-_gridMinX)/_gridCell), gy=(int)((ry-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if (rz > manHiZ[idx]) { manHiZ[idx]=rz; manHas[idx]=true; }
        }

        // Gap = upper-inferior-Z minus lower-superior-Z (positive = open space)
        float minGap = float.MaxValue;
        for (int i=0; i<manHiZ.Length; i++) {
            if (_maxHas[i] && manHas[i]) {
                float gap = _maxLowZ[i] - manHiZ[i];
                if (gap < minGap) minGap = gap;
            }
        }
        return minGap == float.MaxValue ? float.NaN : minGap;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONDYLE DRAG  (CamPlane style — exactly like osteotomy windows)
    // ════════════════════════════════════════════════════════════════════════
    private void AdjustAxisToggle_Checked(object sender, RoutedEventArgs e)
    {
        _adjustAxisMode = true;
        AxisHintLabel.Text = "Drag the orange (L) or blue (R) condyle spheres to reposition the axis.";
        if (_leftCondyleSphere  != null) _leftCondyleSphere.Material  = MeshHelper.CreatePhongMaterial(255, 200, 60);  // orange for L adjust
        if (_rightCondyleSphere != null) _rightCondyleSphere.Material = MeshHelper.CreatePhongMaterial(60, 200, 255);  // blue for R adjust
    }

    private void AdjustAxisToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _adjustAxisMode = false;
        _dragging = DragTarget.None;
        AxisHintLabel.Text = "";
        if (_leftCondyleSphere  != null) _leftCondyleSphere.Material  = MeshHelper.CreatePhongMaterial(0, 245, 255);  // azure
        if (_rightCondyleSphere != null) _rightCondyleSphere.Material = MeshHelper.CreatePhongMaterial(0, 245, 255);  // azure
    }

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_adjustAxisMode || e.ChangedButton != MouseButton.Left) return;

        var pos = e.GetPosition(MainViewport);
        var lPt = new Point3D(_leftCondyle.X,  _leftCondyle.Y,  _leftCondyle.Z);
        var rPt = new Point3D(_rightCondyle.X, _rightCondyle.Y, _rightCondyle.Z);

        bool hitL = SphereHit(pos, lPt);
        bool hitR = SphereHit(pos, rPt);

        if (hitL && (!hitR || Dist2D(pos, Project(lPt)) <= Dist2D(pos, Project(rPt))))
        {
            _dragging   = DragTarget.Left;
            _dragAnchor = lPt;
            MainViewport.IsRotationEnabled = false;
            MainViewport.IsPanEnabled      = false;
            MainViewport.CaptureMouse();
            e.Handled = true;
        }
        else if (hitR)
        {
            _dragging   = DragTarget.Right;
            _dragAnchor = rPt;
            MainViewport.IsRotationEnabled = false;
            MainViewport.IsPanEnabled      = false;
            MainViewport.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == DragTarget.None || e.LeftButton != MouseButtonState.Pressed) return;
        if (!MainViewport.IsMouseCaptured) return;

        var pos    = e.GetPosition(MainViewport);
        var newPt  = CamPlane(pos, _dragAnchor);
        if (!newPt.HasValue) return;

        if (_dragging == DragTarget.Left)
        {
            _leftCondyle = (newPt.Value.X, newPt.Value.Y, newPt.Value.Z);
            _dragAnchor  = newPt.Value;
        }
        else
        {
            _rightCondyle = (newPt.Value.X, newPt.Value.Y, newPt.Value.Z);
            _dragAnchor   = newPt.Value;
        }

        RebuildAxis();
        RebuildAxisLineInPlace();
        UpdateMandibleRotationAxis();
        UpdateAngle();

        CondyleLeftLabel.Text  = $"L: ({_leftCondyle.X:F1}, {_leftCondyle.Y:F1}, {_leftCondyle.Z:F1})";
        CondyleRightLabel.Text = $"R: ({_rightCondyle.X:F1}, {_rightCondyle.Y:F1}, {_rightCondyle.Z:F1})";
        e.Handled = true;
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == DragTarget.None) return;
        _dragging = DragTarget.None;
        MainViewport.ReleaseMouseCapture();
        MainViewport.IsRotationEnabled = true;
        MainViewport.IsPanEnabled      = true;
        // Rebuild heightfield for new axis position
        BuildMaxillaHeightfield();
        _openSignCache = 0;
        _baseClearance = ClearanceForSignedAngle(0f);
        UpdateAngle();
        e.Handled = true;
    }

    private void RebuildAxisLineInPlace()
    {
        // Remove the old axis visual and rebuild it.
        // Scene order: upper, [rami...], lower, axisLine, sphereL, sphereR
        if (_axisLineVisual != null)
        {
            SceneGroup.Children.Remove(_axisLineVisual);
            _axisLineVisual = null;
        }
        // Remove old sphere visuals too; AddAxisLine will add new line, then caller re-adds spheres
        if (_leftCondyleSphere != null)  { SceneGroup.Children.Remove(_leftCondyleSphere);  _leftCondyleSphere = null; }
        if (_rightCondyleSphere != null) { SceneGroup.Children.Remove(_rightCondyleSphere); _rightCondyleSphere = null; }
        AddAxisLine();
        _leftCondyleSphere  = BuildSphere((float)_leftCondyle.X,  (float)_leftCondyle.Y,  (float)_leftCondyle.Z,  SphereRadius, 255, 200, 60);
        _rightCondyleSphere = BuildSphere((float)_rightCondyle.X, (float)_rightCondyle.Y, (float)_rightCondyle.Z, SphereRadius, 60, 200, 255);
        SceneGroup.Children.Add(_leftCondyleSphere);
        SceneGroup.Children.Add(_rightCondyleSphere);
    }

    private void UpdateMandibleRotationAxis()
    {
        if (_mandibleRotation?.Rotation is not AxisAngleRotation3D rot) return;
        rot.Axis = new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z);
        _mandibleRotation.CenterX = _axisPoint.x;
        _mandibleRotation.CenterY = _axisPoint.y;
        _mandibleRotation.CenterZ = _axisPoint.z;
    }

    // ── CamPlane: project screen point onto plane through anchor, facing camera ──
    // (identical pattern to LeFort1YCutWindow.CamPlane)
    private Point3D? CamPlane(Point screenPos, Point3D anchor)
    {
        var look = MainCamera.LookDirection;
        look.Normalize();
        var pn  = new Vector3D(-look.X, -look.Y, -look.Z);
        var ray = MainViewport.UnProject(screenPos);
        double nd = pn.X*ray.Direction.X + pn.Y*ray.Direction.Y + pn.Z*ray.Direction.Z;
        if (Math.Abs(nd) < 1e-9) return null;
        double t = (pn.X*(anchor.X-ray.Position.X) +
                    pn.Y*(anchor.Y-ray.Position.Y) +
                    pn.Z*(anchor.Z-ray.Position.Z)) / nd;
        if (t < 0) return null;
        return new Point3D(
            ray.Position.X + t*ray.Direction.X,
            ray.Position.Y + t*ray.Direction.Y,
            ray.Position.Z + t*ray.Direction.Z);
    }

    // Check whether screen point falls within SphereHitPx pixels of the projected sphere center
    private bool SphereHit(Point screenPos, Point3D worldPt)
    {
        var p2 = Project(worldPt);
        return !double.IsNaN(p2.X) && Dist2D(screenPos, p2) <= SphereHitPx;
    }

    // Rough world→screen projection (perspective, not accounting for viewport matrix).
    private Point Project(Point3D worldPt)
    {
        var toPoint = worldPt - MainCamera.Position;
        var look    = MainCamera.LookDirection; look.Normalize();
        double depth = Vector3D.DotProduct(toPoint, look);
        if (depth <= 0) return new Point(double.NaN, double.NaN);

        // Camera right/up vectors
        var right = Vector3D.CrossProduct(look, MainCamera.UpDirection); right.Normalize();
        var up    = Vector3D.CrossProduct(right, look); up.Normalize();

        double fovRad   = MainCamera.FieldOfView * Math.PI / 180.0;
        double scale    = MainViewport.ActualHeight / (2.0 * Math.Tan(fovRad * 0.5) * depth);
        double sx       = Vector3D.DotProduct(toPoint, right) * scale + MainViewport.ActualWidth  * 0.5;
        double sy       = -Vector3D.DotProduct(toPoint, up)   * scale + MainViewport.ActualHeight * 0.5;
        return new Point(sx, sy);
    }

    private static double Dist2D(Point a, Point b)
        => Math.Sqrt((a.X-b.X)*(a.X-b.X) + (a.Y-b.Y)*(a.Y-b.Y));

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
        double signed = OpenSignOrDefault() * mag;

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
