using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

/// <summary>
/// Step 0 of splint generation: a lateral view of the maxilla + mandible where the
/// surgeon hinges the mandible open about the condylar axis (fulcrum at the condyle
/// centers) in tenths of a degree, to create inter-arch clearance for the wafer.
/// Clicking Apply rotates the mandible by the chosen angle and hands the opened pose
/// to the splint planner.
/// </summary>
public partial class MandibleAutorotationWindow : Window
{
    private readonly float[] _maxillaMesh;     // stable jaw (occlusal surface superior side)
    private readonly float[] _mandibleMesh;    // mobile jaw — rotated live

    private readonly (float x, float y, float z) _axisPoint;  // a point on the condylar axis
    private readonly (float x, float y, float z) _axisUnit;   // unit direction of the condylar axis

    // Cheap occlusal clearance estimate from a coarse XY heightfield of the maxilla.
    private float[]? _maxLowZ;   // min maxilla Z per cell (occlusal surface)
    private bool[]?  _maxHas;
    private float _gridMinX, _gridMinY, _gridCell;
    private int _gridNx, _gridNy;
    private float _baseClearance;

    private MeshGeometryModel3D? _mandibleModel;
    private RotateTransform3D? _mandibleRotation;
    private EventHandler? _renderingHandler;

    // ── Results ──
    public bool   Accepted        { get; private set; }
    public double OpenDegrees     { get; private set; }   // signed angle actually applied
    public float[]? RotatedMandible { get; private set; }

    public MandibleAutorotationWindow(
        float[] maxillaMesh, float[] mandibleMesh,
        (double X, double Y, double Z) leftCondyle,
        (double X, double Y, double Z) rightCondyle,
        float suggestedOpenDegrees = 0f)
    {
        InitializeComponent();

        _maxillaMesh  = maxillaMesh;
        _mandibleMesh = mandibleMesh;

        // Condylar axis = line through the two condyle centers; fulcrum point = midpoint.
        float ax = (float)(leftCondyle.X - rightCondyle.X);
        float ay = (float)(leftCondyle.Y - rightCondyle.Y);
        float az = (float)(leftCondyle.Z - rightCondyle.Z);
        float len = MathF.Sqrt(ax*ax + ay*ay + az*az);
        if (len < 1e-6f) { ax = 1; ay = 0; az = 0; len = 1; }
        _axisUnit  = (ax/len, ay/len, az/len);
        _axisPoint = ((float)((leftCondyle.X + rightCondyle.X) * 0.5),
                      (float)((leftCondyle.Y + rightCondyle.Y) * 0.5),
                      (float)((leftCondyle.Z + rightCondyle.Z) * 0.5));

        FulcrumLabel.Text =
            $"L ({leftCondyle.X:F0}, {leftCondyle.Y:F0}, {leftCondyle.Z:F0})\n" +
            $"R ({rightCondyle.X:F0}, {rightCondyle.Y:F0}, {rightCondyle.Z:F0})";

        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        _renderingHandler = (s, _) => UpdateHeadlamp(MainCamera, Headlamp, Backlamp);
        CompositionTarget.Rendering += _renderingHandler;

        Loaded += OnLoaded;
        Closed += OnClosed;

        // Apply any caller-suggested opening once the scene is set up.
        if (suggestedOpenDegrees > 0.05f)
            Loaded += (_, _) => AngleSlider.Value = Math.Min(suggestedOpenDegrees, AngleSlider.Maximum);
    }

    /// <summary>Update header copy when casts are shown in the planned final-occlusion pose.</summary>
    public void UseFinalOcclusionPose(string occlusionName)
    {
        StepInstructions.Text =
            $"Dental casts are positioned in the planned final occlusion ({occlusionName}). "
            + "The mandible hinges open around the condylar axis (fulcrum at the condyle centers). "
            + "Set the opening angle in tenths of a degree, then click Apply to proceed to splint generation.";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Build maxilla (static) + mandible (rotatable) models.
        var maxModel = MeshHelper.BuildModel3D(_maxillaMesh, 235, 225, 205);
        SceneGroup.Children.Add(maxModel);

        _mandibleRotation = new RotateTransform3D(
            new AxisAngleRotation3D(
                new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z), 0),
            _axisPoint.x, _axisPoint.y, _axisPoint.z);
        _mandibleModel = MeshHelper.BuildModel3D(_mandibleMesh, 210, 215, 235);
        _mandibleModel.Transform = _mandibleRotation;
        SceneGroup.Children.Add(_mandibleModel);

        BuildMaxillaHeightfield();
        _baseClearance = ClearanceForSignedAngle(0f);

        CenterLateral();
        UpdateAngle();
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
        {
            front.Direction = f;
            back.Direction  = b;
        }
    }

    // ── Lateral camera: look ALONG the condylar (hinge) axis so opening is in-plane ──
    private void CenterLateral()
    {
        float minX=float.MaxValue,minY=float.MaxValue,minZ=float.MaxValue;
        float maxX=float.MinValue,maxY=float.MinValue,maxZ=float.MinValue;
        void Acc(float[] m){ for(int i=0;i+2<m.Length;i+=3){
            float x=m[i],y=m[i+1],z=m[i+2];
            if(x<minX)minX=x; if(x>maxX)maxX=x;
            if(y<minY)minY=y; if(y>maxY)maxY=y;
            if(z<minZ)minZ=z; if(z>maxZ)maxZ=z; } }
        Acc(_maxillaMesh); Acc(_mandibleMesh);
        if (minX>maxX) return;

        var pivot = new Point3D((minX+maxX)/2.0, (minY+maxY)/2.0, (minZ+maxZ)/2.0);
        double diag = Math.Sqrt(Math.Pow(maxX-minX,2)+Math.Pow(maxY-minY,2)+Math.Pow(maxZ-minZ,2));
        double dist = Math.Max(diag * 1.1, 40);

        var look = new Vector3D(_axisUnit.x, _axisUnit.y, _axisUnit.z);
        look.Normalize();
        MainCamera.Position      = new Point3D(pivot.X - look.X*dist, pivot.Y - look.Y*dist, pivot.Z - look.Z*dist);
        MainCamera.LookDirection = look * dist;
        MainCamera.UpDirection   = new Vector3D(0, 0, 1);

        MainViewport.FixedRotationPointEnabled = true;
        MainViewport.FixedRotationPoint = pivot;
    }

    // ════════════════════════════════════════════════════════════
    //  ANGLE CONTROLS
    // ════════════════════════════════════════════════════════════
    private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateAngle();
    private void IncFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value + 0.1);
    private void DecFine_Click(object s, RoutedEventArgs e)   => AngleSlider.Value = Clamp(AngleSlider.Value - 0.1);
    private void IncCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value + 1.0);
    private void DecCoarse_Click(object s, RoutedEventArgs e) => AngleSlider.Value = Clamp(AngleSlider.Value - 1.0);
    private void Reset_Click(object s, RoutedEventArgs e)     => AngleSlider.Value = 0;

    private double Clamp(double v) => Math.Round(Math.Clamp(v, AngleSlider.Minimum, AngleSlider.Maximum), 1);

    private void UpdateAngle()
    {
        if (_mandibleRotation == null) return;
        double mag = Math.Round(AngleSlider.Value, 1);
        double signed = _openSignOrDefault() * mag;

        AngleLabel.Text = $"{mag:F1}°";
        ((AxisAngleRotation3D)_mandibleRotation.Rotation).Angle = signed;

        float clearance = ClearanceForSignedAngle((float)signed);
        if (float.IsNaN(clearance))
            ClearanceLabel.Text = "no occlusal overlap detected";
        else
            ClearanceLabel.Text = $"≈ {clearance:F1} mm (closed: {_baseClearance:F1} mm)";

        WarningLabel.Text = "";
    }

    private double _openSignOrDefault()
    {
        float s = OpenSign;
        return s != 0 ? s : 1.0;
    }

    // ── Determine which rotation sign opens the bite (increases clearance) ──
    // Lazily evaluated after the heightfield exists.
    private float _openSignCache;
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

    // ════════════════════════════════════════════════════════════
    //  CLEARANCE ESTIMATE (coarse occlusal heightfield)
    // ════════════════════════════════════════════════════════════
    private void BuildMaxillaHeightfield()
    {
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        for (int i=0;i+2<_maxillaMesh.Length;i+=3){
            float x=_maxillaMesh[i],y=_maxillaMesh[i+1];
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
        for (int i=0;i+2<_maxillaMesh.Length;i+=3){
            float x=_maxillaMesh[i],y=_maxillaMesh[i+1],z=_maxillaMesh[i+2];
            int gx=(int)((x-_gridMinX)/_gridCell), gy=(int)((y-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if(z<_maxLowZ[idx]){_maxLowZ[idx]=z;_maxHas[idx]=true;}
        }
    }

    /// <summary>Min vertical gap (maxilla occlusal − mandible occlusal) over the overlap,
    /// for the mandible rotated by the given SIGNED angle. NaN if no overlap.</summary>
    private float ClearanceForSignedAngle(float signedDegrees)
    {
        if (_maxLowZ == null || _maxHas == null) return float.NaN;
        // Highest mandible Z per cell after rotation (its occlusal surface).
        var manHiZ = new float[_gridNx*_gridNy];
        var manHas = new bool [_gridNx*_gridNy];
        for (int i=0;i<manHiZ.Length;i++) manHiZ[i]=float.MinValue;

        float rad = signedDegrees * MathF.PI / 180f;
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        float kx=_axisUnit.x, ky=_axisUnit.y, kz=_axisUnit.z;
        for (int i=0;i+2<_mandibleMesh.Length;i+=3){
            float px=_mandibleMesh[i]-_axisPoint.x, py=_mandibleMesh[i+1]-_axisPoint.y, pz=_mandibleMesh[i+2]-_axisPoint.z;
            float dot=kx*px+ky*py+kz*pz;
            float crx=ky*pz-kz*py, cry=kz*px-kx*pz, crz=kx*py-ky*px;
            float rx=_axisPoint.x + px*c + crx*s + kx*dot*(1-c);
            float ry=_axisPoint.y + py*c + cry*s + ky*dot*(1-c);
            float rz=_axisPoint.z + pz*c + crz*s + kz*dot*(1-c);
            int gx=(int)((rx-_gridMinX)/_gridCell), gy=(int)((ry-_gridMinY)/_gridCell);
            if(gx<0||gx>=_gridNx||gy<0||gy>=_gridNy) continue;
            int idx=gy*_gridNx+gx;
            if(rz>manHiZ[idx]){manHiZ[idx]=rz;manHas[idx]=true;}
        }

        float minGap=float.MaxValue;
        for (int i=0;i<manHiZ.Length;i++){
            if(_maxHas[i]&&manHas[i]){
                float gap=_maxLowZ[i]-manHiZ[i];
                if(gap<minGap) minGap=gap;
            }
        }
        return minGap==float.MaxValue ? float.NaN : minGap;
    }

    // ════════════════════════════════════════════════════════════
    //  APPLY / CANCEL
    // ════════════════════════════════════════════════════════════
    private void ApplyBtn_Click(object s, RoutedEventArgs e)
    {
        double mag = Math.Round(AngleSlider.Value, 1);
        double signed = _openSignOrDefault() * mag;
        OpenDegrees = signed;
        RotatedMandible = Math.Abs(signed) < 0.01
            ? _mandibleMesh
            : SplintEngine.RotateMesh(_mandibleMesh, _axisPoint, _axisUnit, (float)signed);
        Accepted = true;
        Close();
    }

    private void CancelBtn_Click(object s, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
