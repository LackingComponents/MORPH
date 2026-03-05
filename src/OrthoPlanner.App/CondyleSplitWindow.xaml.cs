using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Segmentation;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class CondyleSplitWindow : Window
{
    // Input
    private readonly List<float[]> _boneVerts;
    private readonly List<float[]> _upperCastVerts;
    private readonly List<float[]> _lowerCastVerts;
    private readonly VolumeData? _ctVolume;
    private readonly SegmentationVolume? _segVolume;
    private readonly byte _boneLabel;

    // 3 user-picked points defining the split plane
    private readonly List<Point3D> _planePoints = new();
    private readonly List<SphereVisual3D> _planeMarkers = new();

    // Plane: Ax + By + Cz + D = 0
    private Vector3D _planeNormal;
    private double _planeD;
    private Point3D _planeCentroid;
    private ModelVisual3D? _planeTriangleVisual;

    // Condylar bounding boxes: smaller default, adjustable
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private float[] _leftHalfExtents = { 10f, 10f, 10f };
    private float[] _rightHalfExtents = { 10f, 10f, 10f };
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;

    // Drag state
    private int _dragPointIndex = -1;       // Step 1: dragging plane points
    private bool _isDragging;               // Step 2: dragging condyle box
    private bool _draggingLeft;
    private int _dragCornerIdx = -1;        // Step 2: resizing via corner
    private int _dragFaceAxis = -1;         // Step 2: move perpendicular to face

    // Results
    private List<float[]>? _craniumVerts;
    private List<float[]>? _mandibleVerts;
    private int _currentStep = 1;

    // Public results
    public bool Accepted { get; private set; }
    public List<float[]>? CraniumResult { get; private set; }
    public List<float[]>? MandibleResult { get; private set; }
    public (double X, double Y, double Z)? LeftCondyleCenter { get; private set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; private set; }

    public CondyleSplitWindow(
        List<float[]> boneVerts, List<float[]> upperCastVerts, List<float[]> lowerCastVerts,
        VolumeData? ctVolume = null, SegmentationVolume? segVolume = null, byte boneLabel = 1)
    {
        InitializeComponent();
        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _upperCastVerts = upperCastVerts;
        _lowerCastVerts = lowerCastVerts;
        _ctVolume = ctVolume;
        _segVolume = segVolume;
        _boneLabel = boneLabel;
        Loaded += (_, _) => SetupStep1();
    }

    // ═══════════════════════════════════
    // STEP 1: Pick 3 points → plane
    // ═══════════════════════════════════
    private void SetupStep1()
    {
        _currentStep = 1;
        _planePoints.Clear();
        _planeMarkers.Clear();
        _leftCondyleCenter = null;
        _rightCondyleCenter = null;
        _dragPointIndex = -1;

        MainViewport.Children.Clear();
        AddLighting();

        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180, 220);
        MainViewport.Children.Add(new ModelVisual3D { Content = boneModel });

        var upperModel = MeshHelper.BuildModel3D(_upperCastVerts, 255, 220, 80, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = upperModel });

        var lowerModel = MeshHelper.BuildModel3D(_lowerCastVerts, 80, 160, 255, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = lowerModel });

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 1: Define Separation Plane";
        StepInstructions.Text =
            "Click 3 points: (1) between central incisors, (2) distal-occlusal of last left molar, " +
            "(3) distal-occlusal of last right molar. Drag points to widen. Then 'Next: Condyles'.";
        StatusText.Text = "0/3 points placed";
        SplitBtn.Visibility = Visibility.Visible;
        SplitBtn.Content = "Next: Condyles";
        SplitBtn.IsEnabled = true;
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    private void ComputePlane()
    {
        if (_planePoints.Count < 3) return;
        var p0 = _planePoints[0]; var p1 = _planePoints[1]; var p2 = _planePoints[2];
        var v1 = p1 - p0; var v2 = p2 - p0;
        _planeNormal = Vector3D.CrossProduct(v1, v2);
        if (_planeNormal.Length < 1e-10) return;
        _planeNormal.Normalize();

        // Ensure plane normal points "up" (toward cranium)
        if (_planeNormal.Z < 0) _planeNormal = -_planeNormal;

        _planeCentroid = new Point3D(
            (p0.X + p1.X + p2.X) / 3, (p0.Y + p1.Y + p2.Y) / 3, (p0.Z + p1.Z + p2.Z) / 3);
        _planeD = -Vector3D.DotProduct(_planeNormal, (Vector3D)_planeCentroid);

        ShowPlaneTriangle();
    }

    /// <summary>
    /// Show the plane as a filled triangle through the 3 user-picked points,
    /// extended outward to form a larger triangle that visually shows the plane extent.
    /// </summary>
    private void ShowPlaneTriangle()
    {
        if (_planeTriangleVisual != null) MainViewport.Children.Remove(_planeTriangleVisual);
        if (_planePoints.Count < 3) return;

        // Extend each point outward from centroid by ~3x to show a large plane region
        double scale = 3.0;
        var ext0 = ExtendFromCentroid(_planePoints[0], scale);
        var ext1 = ExtendFromCentroid(_planePoints[1], scale);
        var ext2 = ExtendFromCentroid(_planePoints[2], scale);

        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(new[] { ext0, ext1, ext2 });
        mesh.TriangleIndices = new Int32Collection(new[] { 0, 1, 2, 0, 2, 1 }); // double-sided
        mesh.Freeze();

        var brush = new SolidColorBrush(Color.FromArgb(60, 0, 255, 100)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        // Also draw the actual triangle border (cyan lines through the actual points)
        var border = new LinesVisual3D { Color = Colors.Cyan, Thickness = 2 };
        border.Points.Add(_planePoints[0]); border.Points.Add(_planePoints[1]);
        border.Points.Add(_planePoints[1]); border.Points.Add(_planePoints[2]);
        border.Points.Add(_planePoints[2]); border.Points.Add(_planePoints[0]);

        var parent = new ModelVisual3D { Content = model };
        parent.Children.Add(border);
        _planeTriangleVisual = parent;
        MainViewport.Children.Add(parent);
    }

    private Point3D ExtendFromCentroid(Point3D pt, double scale)
    {
        return new Point3D(
            _planeCentroid.X + (pt.X - _planeCentroid.X) * scale,
            _planeCentroid.Y + (pt.Y - _planeCentroid.Y) * scale,
            _planeCentroid.Z + (pt.Z - _planeCentroid.Z) * scale);
    }

    // ═══════════════════════════════════
    // STEP 1→2: condyle placement
    // ═══════════════════════════════════
    private void GoToCondyleStep()
    {
        if (_planePoints.Count < 3)
        {
            MessageBox.Show("Please place all 3 points.", "Need 3 Points",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _currentStep = 2;
        _dragPointIndex = -1;

        StepTitle.Text = "Step 2: Condylar Bounding Boxes";
        StepInstructions.Text =
            "Click the lateral aspect of each condyle (left, right). " +
            "Drag center to move, drag corners to resize. Then 'Split'.";
        StatusText.Text = "Place left condyle...";
        SplitBtn.Content = "Split";
    }

    // ═══════════════════════════════════
    // STEP 2→3: Perform the actual split
    // ═══════════════════════════════════
    private async void PerformSplit()
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null)
        {
            MessageBox.Show("Place both condylar bounding boxes first.",
                "Missing Condyles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SplitBtn.IsEnabled = false;
        StatusText.Text = "Splitting...";

        try
        {
            var leftC = _leftCondyleCenter.ToArray();
            var rightC = _rightCondyleCenter.ToArray();
            var leftHE = _leftHalfExtents.ToArray();
            var rightHE = _rightHalfExtents.ToArray();
            var normal = _planeNormal;
            var planeD = _planeD;
            var boneVerts = _boneVerts;
            var segVol = _segVolume;
            var ctVol = _ctVolume;
            var boneLabel = _boneLabel;

            var (cranium, mandible) = await Task.Run(() =>
                SplitWithPlaneAndBoxes(boneVerts, normal, planeD, leftC, rightC,
                    leftHE, rightHE, segVol, ctVol, boneLabel));

            _craniumVerts = cranium;
            _mandibleVerts = mandible;

            Dispatcher.Invoke(() =>
            {
                MainViewport.Children.Clear();
                AddLighting();

                if (_craniumVerts.Count > 0)
                {
                    var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
                    MainViewport.Children.Add(new ModelVisual3D { Content = cranModel });
                }
                if (_mandibleVerts.Count > 0)
                {
                    var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 180, 200, 220);
                    MainViewport.Children.Add(new ModelVisual3D { Content = mandModel });
                }

                // Condylar axis line
                var axis = new LinesVisual3D { Color = Colors.Red, Thickness = 3 };
                axis.Points.Add(new Point3D(leftC[0], leftC[1], leftC[2]));
                axis.Points.Add(new Point3D(rightC[0], rightC[1], rightC[2]));
                MainViewport.Children.Add(axis);
                AddSphereMarker(leftC, Colors.LimeGreen, 2);
                AddSphereMarker(rightC, Colors.OrangeRed, 2);
                RebuildBoxVisuals();
                MainViewport.ZoomExtents(500);

                CraniumResult = _craniumVerts;
                MandibleResult = _mandibleVerts;
                LeftCondyleCenter = (leftC[0], leftC[1], leftC[2]);
                RightCondyleCenter = (rightC[0], rightC[1], rightC[2]);

                _currentStep = 3;
                StepTitle.Text = "Step 3: Review & Accept";
                StepInstructions.Text = "Warm = Cranium, Cool = Mandible. Red = condylar axis. Accept or Cancel.";
                StatusText.Text = $"Cranium: {_craniumVerts.Count / 3} tris | Mandible: {_mandibleVerts.Count / 3} tris";
                SplitBtn.Visibility = Visibility.Collapsed;
                AcceptBtn.Visibility = Visibility.Visible;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Split failed:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SplitBtn.IsEnabled = true;
            });
        }
    }

    /// <summary>
    /// The core algorithm — fast, runs on background thread:
    /// 1) Split bone mesh by plane: above = cranium, below = mandible.
    /// 2) For condylar boxes: triangles inside boxes that are above the plane
    ///    get split by midZ of the box centers — above midZ = fossa (cranium),
    ///    below midZ = condyle (mandible). This handles the TMJ separation.
    /// </summary>
    private static (List<float[]> cranium, List<float[]> mandible) SplitWithPlaneAndBoxes(
        List<float[]> boneVerts, Vector3D planeNormal, double planeD,
        float[] leftC, float[] rightC, float[] leftHE, float[] rightHE,
        SegmentationVolume? segVol, VolumeData? ctVol, byte boneLabel)
    {
        var cranium = new List<float[]>();
        var mandible = new List<float[]>();

        float condyleMidZ = (leftC[2] + rightC[2]) / 2f;

        for (int i = 0; i + 2 < boneVerts.Count; i += 3)
        {
            float cx = (boneVerts[i][0] + boneVerts[i + 1][0] + boneVerts[i + 2][0]) / 3f;
            float cy = (boneVerts[i][1] + boneVerts[i + 1][1] + boneVerts[i + 2][1]) / 3f;
            float cz = (boneVerts[i][2] + boneVerts[i + 1][2] + boneVerts[i + 2][2]) / 3f;

            float[] v0 = boneVerts[i], v1 = boneVerts[i + 1], v2 = boneVerts[i + 2];

            // Check if inside either condylar box
            bool inLeftBox = IsInBox(cx, cy, cz, leftC, leftHE);
            bool inRightBox = IsInBox(cx, cy, cz, rightC, rightHE);

            if (inLeftBox || inRightBox)
            {
                // Inside a condylar box — split by midZ between the two condyle centers
                var target = cz >= condyleMidZ ? cranium : mandible;
                target.Add(Clone(v0)); target.Add(Clone(v1)); target.Add(Clone(v2));
            }
            else
            {
                // Normal plane split
                double dist = planeNormal.X * cx + planeNormal.Y * cy + planeNormal.Z * cz + planeD;
                var target = dist >= 0 ? cranium : mandible;
                target.Add(Clone(v0)); target.Add(Clone(v1)); target.Add(Clone(v2));
            }
        }

        return (cranium, mandible);
    }

    private static bool IsInBox(float x, float y, float z, float[] center, float[] he)
    {
        return x >= center[0] - he[0] && x <= center[0] + he[0] &&
               y >= center[1] - he[1] && y <= center[1] + he[1] &&
               z >= center[2] - he[2] && z <= center[2] + he[2];
    }

    private static float[] Clone(float[] v) => new float[] { v[0], v[1], v[2] };

    // ═══════════════════════════════════
    // Mouse interaction
    // ═══════════════════════════════════
    private void Viewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        if (_currentStep == 1)
        {
            // Check existing points for drag
            for (int i = 0; i < _planePoints.Count; i++)
            {
                if ((_planePoints[i] - hit.Value).Length < 5)
                { _dragPointIndex = i; e.Handled = true; return; }
            }

            if (_planePoints.Count < 3)
            {
                _planePoints.Add(hit.Value);
                var colors = new[] { Colors.Cyan, Colors.Yellow, Colors.Magenta };
                var marker = new SphereVisual3D
                {
                    Center = hit.Value, Radius = 2,
                    Fill = new SolidColorBrush(colors[_planePoints.Count - 1])
                };
                _planeMarkers.Add(marker);
                MainViewport.Children.Add(marker);

                string[] labels = { "Incisors", "Left posterior", "Right posterior" };
                StatusText.Text = $"{_planePoints.Count}/3: {labels[_planePoints.Count - 1]} placed";
                if (_planePoints.Count == 3) ComputePlane();
            }
            e.Handled = true;
        }
        else if (_currentStep == 2)
        {
            // Drag existing box center?
            if (_leftCondyleCenter != null && DistanceTo(hit.Value, _leftCondyleCenter) < 15)
            { _isDragging = true; _draggingLeft = true; e.Handled = true; return; }
            if (_rightCondyleCenter != null && DistanceTo(hit.Value, _rightCondyleCenter) < 15)
            { _isDragging = true; _draggingLeft = false; e.Handled = true; return; }

            // Place condyle
            if (_leftCondyleCenter == null)
            {
                _leftCondyleCenter = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Left condyle placed. Now click RIGHT condyle.";
            }
            else if (_rightCondyleCenter == null)
            {
                _rightCondyleCenter = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Both placed. Drag center to move, corners to resize → 'Split'.";
            }
            RebuildBoxVisuals();
            e.Handled = true;
        }
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragPointIndex = -1; _isDragging = false; return; }

        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        if (_currentStep == 1 && _dragPointIndex >= 0 && _dragPointIndex < _planePoints.Count)
        {
            // Drag point along the plane
            if (_planePoints.Count == 3)
            {
                double dist = _planeNormal.X * hit.Value.X + _planeNormal.Y * hit.Value.Y +
                              _planeNormal.Z * hit.Value.Z + _planeD;
                _planePoints[_dragPointIndex] = new Point3D(
                    hit.Value.X - _planeNormal.X * dist,
                    hit.Value.Y - _planeNormal.Y * dist,
                    hit.Value.Z - _planeNormal.Z * dist);
            }
            else _planePoints[_dragPointIndex] = hit.Value;

            _planeMarkers[_dragPointIndex].Center = _planePoints[_dragPointIndex];
            if (_planePoints.Count == 3) ComputePlane();
        }

        if (_currentStep == 2 && _isDragging)
        {
            var center = _draggingLeft ? _leftCondyleCenter : _rightCondyleCenter;
            if (center != null)
            {
                center[0] = (float)hit.Value.X; center[1] = (float)hit.Value.Y; center[2] = (float)hit.Value.Z;
                RebuildBoxVisuals();
                StatusText.Text = $"{(_draggingLeft ? "L" : "R")}: ({center[0]:F1}, {center[1]:F1}, {center[2]:F1})";
            }
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPointIndex = -1; _isDragging = false; _dragCornerIdx = -1; _dragFaceAxis = -1;
    }

    // Use right-click to resize the nearest condyle box (increase extents)
    private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentStep != 2) return;

        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        // Find which box is closest
        float[]? center = null; float[]? he = null; string side = "";
        if (_leftCondyleCenter != null && (_rightCondyleCenter == null ||
            DistanceTo(hit.Value, _leftCondyleCenter) < DistanceTo(hit.Value, _rightCondyleCenter!)))
        { center = _leftCondyleCenter; he = _leftHalfExtents; side = "Left"; }
        else if (_rightCondyleCenter != null)
        { center = _rightCondyleCenter; he = _rightHalfExtents; side = "Right"; }

        if (center == null || he == null) return;

        // Toggle between two sizes: 10mm and 15mm half-extent
        float newSize = he[0] < 12f ? 15f : 10f;
        he[0] = he[1] = he[2] = newSize;

        if (side == "Left") _leftHalfExtents = he;
        else _rightHalfExtents = he;

        RebuildBoxVisuals();
        StatusText.Text = $"{side} box size: {newSize * 2:F0}mm. Right-click to toggle.";
        e.Handled = true;
    }

    // ═══════════════════════════════════
    // Visual helpers
    // ═══════════════════════════════════
    private void AddLighting()
    {
        MainViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(100, 100, 100)) });
        MainViewport.Children.Add(new ModelVisual3D { Content = new DirectionalLight(Color.FromRgb(160, 155, 145), new Vector3D(-1, -1, -0.5)) });
        MainViewport.Children.Add(new ModelVisual3D { Content = new DirectionalLight(Color.FromRgb(80, 80, 90), new Vector3D(1, 0.5, 0.3)) });
        MainViewport.Children.Add(new ModelVisual3D { Content = new DirectionalLight(Color.FromRgb(60, 60, 70), new Vector3D(0, 1, 0.5)) });
    }

    private void RebuildBoxVisuals()
    {
        if (_leftBoxVisual != null) MainViewport.Children.Remove(_leftBoxVisual);
        if (_rightBoxVisual != null) MainViewport.Children.Remove(_rightBoxVisual);
        if (_leftCondyleCenter != null)
        { _leftBoxVisual = CreateBoxVisual(_leftCondyleCenter, _leftHalfExtents, Colors.LimeGreen); MainViewport.Children.Add(_leftBoxVisual); }
        if (_rightCondyleCenter != null)
        { _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, _rightHalfExtents, Colors.OrangeRed); MainViewport.Children.Add(_rightBoxVisual); }
    }

    private ModelVisual3D CreateBoxVisual(float[] c, float[] he, Color color)
    {
        double cx = c[0], cy = c[1], cz = c[2], hx = he[0], hy = he[1], hz = he[2];
        var pts = new[]
        {
            new Point3D(cx-hx,cy-hy,cz-hz), new Point3D(cx+hx,cy-hy,cz-hz),
            new Point3D(cx+hx,cy+hy,cz-hz), new Point3D(cx-hx,cy+hy,cz-hz),
            new Point3D(cx-hx,cy-hy,cz+hz), new Point3D(cx+hx,cy-hy,cz+hz),
            new Point3D(cx+hx,cy+hy,cz+hz), new Point3D(cx-hx,cy+hy,cz+hz)
        };
        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(pts);
        mesh.TriangleIndices = new Int32Collection(new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,2,6,7,2,7,3,0,3,7,0,7,4,1,5,6,1,6,2});
        mesh.Freeze();
        var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        int[,] edges = {{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        var lines = new LinesVisual3D { Color = color, Thickness = 2 };
        for (int e = 0; e < 12; e++) { lines.Points.Add(pts[edges[e,0]]); lines.Points.Add(pts[edges[e,1]]); }

        var parent = new ModelVisual3D { Content = model };
        parent.Children.Add(lines);
        return parent;
    }

    private void AddSphereMarker(float[] c, Color color, double r)
    {
        MainViewport.Children.Add(new SphereVisual3D
        { Center = new Point3D(c[0], c[1], c[2]), Radius = r, Fill = new SolidColorBrush(color) });
    }

    private Point3D? GetHitPoint(Point screenPos)
    {
        var result = Viewport3DHelper.FindHits(MainViewport.Viewport, screenPos);
        if (result != null && result.Count > 0) return result[0].Position;
        return null;
    }

    private float DistanceTo(Point3D p, float[] c)
    {
        double dx = p.X-c[0], dy = p.Y-c[1], dz = p.Z-c[2];
        return (float)Math.Sqrt(dx*dx+dy*dy+dz*dz);
    }

    // ═══════════════════════════════════
    // Button handlers
    // ═══════════════════════════════════
    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1) GoToCondyleStep();
        else if (_currentStep == 2) PerformSplit();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { }

    private void Accept_Click(object sender, RoutedEventArgs e)
    { Accepted = true; DialogResult = true; Close(); }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) { SetupStep1(); return; }
        Accepted = false; DialogResult = false; Close();
    }
}
