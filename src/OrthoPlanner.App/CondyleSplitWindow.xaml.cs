using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class CondyleSplitWindow : Window
{
    // Input
    private readonly List<float[]> _boneVerts;
    private readonly List<float[]> _upperCastVerts;
    private readonly List<float[]> _lowerCastVerts;
    private readonly VolumeData? _ctVolume;

    // 3 user-picked points defining the split plane
    private readonly List<Point3D> _planePoints = new();
    private readonly List<SphereVisual3D> _planeMarkers = new();

    // The computed plane: Ax + By + Cz + D = 0
    private Vector3D _planeNormal;
    private double _planeD;
    private Point3D _planeCentroid;
    private RectangleVisual3D? _planeVisual;

    // Condylar bounding boxes
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private static readonly float[] DefaultHalfExtents = { 15f, 15f, 15f };
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;
    private bool _isDragging;
    private bool _draggingLeft;

    // Results
    private List<float[]>? _craniumVerts;
    private List<float[]>? _mandibleVerts;
    private int _currentStep = 1;

    // Selected point for dragging along the plane
    private int _dragPointIndex = -1;

    // Public results
    public bool Accepted { get; private set; }
    public List<float[]>? CraniumResult { get; private set; }
    public List<float[]>? MandibleResult { get; private set; }
    public (double X, double Y, double Z)? LeftCondyleCenter { get; private set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; private set; }

    public CondyleSplitWindow(
        List<float[]> boneVerts, List<float[]> upperCastVerts, List<float[]> lowerCastVerts,
        VolumeData? ctVolume = null)
    {
        InitializeComponent();
        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _upperCastVerts = upperCastVerts;
        _lowerCastVerts = lowerCastVerts;
        _ctVolume = ctVolume;
        Loaded += (_, _) => SetupStep1();
    }

    // ════════════════════════════════════════
    // STEP 1: Pick 3 points → define plane
    // ════════════════════════════════════════
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
            "Click 3 points: between the central incisors, then on the distal-occlusal of the last molar (left & right). " +
            "A green plane will appear. Drag points to adjust. Click 'Next: Condyles' when ready.";
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

        var p0 = _planePoints[0];
        var p1 = _planePoints[1];
        var p2 = _planePoints[2];

        // Normal = (p1-p0) × (p2-p0)
        var v1 = p1 - p0;
        var v2 = p2 - p0;
        _planeNormal = Vector3D.CrossProduct(v1, v2);
        _planeNormal.Normalize();

        // Ensure normal points "up" (positive Z component generally = toward cranium)
        if (_planeNormal.Z < 0) _planeNormal = -_planeNormal;

        _planeCentroid = new Point3D(
            (p0.X + p1.X + p2.X) / 3,
            (p0.Y + p1.Y + p2.Y) / 3,
            (p0.Z + p1.Z + p2.Z) / 3);

        // Plane equation: N·(P - P0) = 0  →  N·P = N·P0  →  Ax+By+Cz+D=0 where D = -N·P0
        _planeD = -Vector3D.DotProduct(_planeNormal, (Vector3D)_planeCentroid);

        ShowPlaneVisual();
    }

    private void ShowPlaneVisual()
    {
        if (_planeVisual != null) MainViewport.Children.Remove(_planeVisual);

        // Compute plane extent from the 3 points + padding
        double maxDist = 0;
        foreach (var pt in _planePoints)
        {
            double d = (_planeCentroid - pt).Length;
            if (d > maxDist) maxDist = d;
        }
        double planeSize = Math.Max(maxDist * 3, 100); // at least 100mm wide

        _planeVisual = new RectangleVisual3D
        {
            Origin = _planeCentroid,
            Normal = _planeNormal,
            Width = planeSize,
            Length = planeSize,
            Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 100))
        };
        MainViewport.Children.Add(_planeVisual);
    }

    // ════════════════════════════════════════
    // STEP 1→2: Go to condyle placement
    // ════════════════════════════════════════
    private void GoToCondyleStep()
    {
        if (_planePoints.Count < 3)
        {
            MessageBox.Show("Please place all 3 points first.", "Need 3 Points",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentStep = 2;
        _dragPointIndex = -1;

        StepTitle.Text = "Step 2: Place Condylar Bounding Boxes";
        StepInstructions.Text =
            "Click on the lateral aspect of each condyle (left, then right). " +
            "A 30mm box will grow from the click. Drag to adjust. Click 'Split' when ready.";
        StatusText.Text = "Place left condyle...";
        SplitBtn.Content = "Split";
    }

    // ════════════════════════════════════════
    // STEP 2→3: Perform split (instant!)
    // ════════════════════════════════════════
    private void PerformPlaneSplit()
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null)
        {
            MessageBox.Show("Place both condylar bounding boxes first.",
                "Missing Condyles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Split bone by plane: above = cranium, below = mandible
            var cranium = new List<float[]>();
            var mandible = new List<float[]>();

            for (int i = 0; i + 2 < _boneVerts.Count; i += 3)
            {
                float cx = (_boneVerts[i][0] + _boneVerts[i + 1][0] + _boneVerts[i + 2][0]) / 3f;
                float cy = (_boneVerts[i][1] + _boneVerts[i + 1][1] + _boneVerts[i + 2][1]) / 3f;
                float cz = (_boneVerts[i][2] + _boneVerts[i + 1][2] + _boneVerts[i + 2][2]) / 3f;

                // Signed distance from plane: positive = above (cranium side)
                double dist = _planeNormal.X * cx + _planeNormal.Y * cy + _planeNormal.Z * cz + _planeD;

                var target = dist >= 0 ? cranium : mandible;
                target.Add(new float[] { _boneVerts[i][0], _boneVerts[i][1], _boneVerts[i][2] });
                target.Add(new float[] { _boneVerts[i + 1][0], _boneVerts[i + 1][1], _boneVerts[i + 1][2] });
                target.Add(new float[] { _boneVerts[i + 2][0], _boneVerts[i + 2][1], _boneVerts[i + 2][2] });
            }

            // Condylar box regions: split their content at midZ between the two box centers
            float condyleMidZ = (_leftCondyleCenter[2] + _rightCondyleCenter[2]) / 2f;

            // For each condylar box, take triangles from the "wrong" side and reassign
            ReassignCondylarRegion(cranium, mandible, _leftCondyleCenter, condyleMidZ);
            ReassignCondylarRegion(cranium, mandible, _rightCondyleCenter, condyleMidZ);

            _craniumVerts = cranium;
            _mandibleVerts = mandible;

            // Show result
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

            // Condylar axis
            var axisLine = new LinesVisual3D { Color = Colors.Red, Thickness = 3 };
            axisLine.Points.Add(new Point3D(_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]));
            axisLine.Points.Add(new Point3D(_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]));
            MainViewport.Children.Add(axisLine);

            AddSphereMarker(_leftCondyleCenter, Colors.LimeGreen, 3);
            AddSphereMarker(_rightCondyleCenter, Colors.OrangeRed, 3);
            RebuildBoxVisuals();
            MainViewport.ZoomExtents(500);

            // Set results
            CraniumResult = _craniumVerts;
            MandibleResult = _mandibleVerts;
            LeftCondyleCenter = (_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]);
            RightCondyleCenter = (_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]);

            _currentStep = 3;
            StepTitle.Text = "Step 3: Review & Accept";
            StepInstructions.Text = "Warm = Cranium, Cool = Mandible. Red = condylar rotation axis. Accept or Cancel.";
            StatusText.Text = $"Cranium: {_craniumVerts.Count / 3} tris | Mandible: {_mandibleVerts.Count / 3} tris";
            SplitBtn.Visibility = Visibility.Collapsed;
            AcceptBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Split failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// For triangles inside a condylar bounding box, reassign them:
    /// cranium gets triangles above condyleMidZ, mandible gets those below.
    /// </summary>
    private void ReassignCondylarRegion(
        List<float[]> cranium, List<float[]> mandible, float[] boxCenter, float midZ)
    {
        float minX = boxCenter[0] - DefaultHalfExtents[0], maxX = boxCenter[0] + DefaultHalfExtents[0];
        float minY = boxCenter[1] - DefaultHalfExtents[1], maxY = boxCenter[1] + DefaultHalfExtents[1];
        float minZ = boxCenter[2] - DefaultHalfExtents[2], maxZ = boxCenter[2] + DefaultHalfExtents[2];

        // Collect triangles inside the box from both lists
        var toRemoveFromCranium = new List<int>();
        var toRemoveFromMandible = new List<int>();

        for (int i = 0; i + 2 < cranium.Count; i += 3)
        {
            float cx = (cranium[i][0] + cranium[i + 1][0] + cranium[i + 2][0]) / 3f;
            float cy = (cranium[i][1] + cranium[i + 1][1] + cranium[i + 2][1]) / 3f;
            float cz = (cranium[i][2] + cranium[i + 1][2] + cranium[i + 2][2]) / 3f;
            if (cx >= minX && cx <= maxX && cy >= minY && cy <= maxY && cz >= minZ && cz <= maxZ)
            {
                if (cz < midZ) // Should be mandible
                {
                    toRemoveFromCranium.Add(i);
                    mandible.Add(cranium[i]); mandible.Add(cranium[i + 1]); mandible.Add(cranium[i + 2]);
                }
            }
        }

        for (int i = 0; i + 2 < mandible.Count; i += 3)
        {
            float cx = (mandible[i][0] + mandible[i + 1][0] + mandible[i + 2][0]) / 3f;
            float cy = (mandible[i][1] + mandible[i + 1][1] + mandible[i + 2][1]) / 3f;
            float cz = (mandible[i][2] + mandible[i + 1][2] + mandible[i + 2][2]) / 3f;
            if (cx >= minX && cx <= maxX && cy >= minY && cy <= maxY && cz >= minZ && cz <= maxZ)
            {
                if (cz >= midZ) // Should be cranium
                {
                    toRemoveFromMandible.Add(i);
                    cranium.Add(mandible[i]); cranium.Add(mandible[i + 1]); cranium.Add(mandible[i + 2]);
                }
            }
        }

        // Remove in reverse order to preserve indices
        for (int i = toRemoveFromCranium.Count - 1; i >= 0; i--)
        {
            int idx = toRemoveFromCranium[i];
            cranium.RemoveRange(idx, 3);
        }
        for (int i = toRemoveFromMandible.Count - 1; i >= 0; i--)
        {
            int idx = toRemoveFromMandible[i];
            mandible.RemoveRange(idx, 3);
        }
    }

    // ════════════════════════════════════════
    // Mouse interaction
    // ════════════════════════════════════════
    private void Viewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        if (_currentStep == 1)
        {
            // Check if near an existing point (for dragging)
            for (int i = 0; i < _planePoints.Count; i++)
            {
                if ((_planePoints[i] - hit.Value).Length < 5)
                {
                    _dragPointIndex = i;
                    e.Handled = true;
                    return;
                }
            }

            if (_planePoints.Count < 3)
            {
                _planePoints.Add(hit.Value);

                var marker = new SphereVisual3D
                {
                    Center = hit.Value, Radius = 2,
                    Fill = new SolidColorBrush(_planePoints.Count == 1 ? Colors.Cyan :
                        _planePoints.Count == 2 ? Colors.Yellow : Colors.Magenta)
                };
                _planeMarkers.Add(marker);
                MainViewport.Children.Add(marker);

                string[] labels = { "Incisors", "Left posterior", "Right posterior" };
                StatusText.Text = $"{_planePoints.Count}/3 points: {labels[_planePoints.Count - 1]} placed";

                if (_planePoints.Count == 3) ComputePlane();
            }
            e.Handled = true;
        }
        else if (_currentStep == 2)
        {
            // Condyle drag check
            if (_leftCondyleCenter != null && DistanceTo(hit.Value, _leftCondyleCenter) < 20)
            { _isDragging = true; _draggingLeft = true; e.Handled = true; return; }
            if (_rightCondyleCenter != null && DistanceTo(hit.Value, _rightCondyleCenter) < 20)
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
                StatusText.Text = "Both condyles placed. Drag to adjust → 'Split'.";
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

        // Dragging a plane-defining point
        if (_currentStep == 1 && _dragPointIndex >= 0 && _dragPointIndex < _planePoints.Count)
        {
            // Project the new hit point onto the plane to constrain movement
            if (_planePoints.Count == 3)
            {
                double dist = _planeNormal.X * hit.Value.X + _planeNormal.Y * hit.Value.Y +
                              _planeNormal.Z * hit.Value.Z + _planeD;
                var projected = new Point3D(
                    hit.Value.X - _planeNormal.X * dist,
                    hit.Value.Y - _planeNormal.Y * dist,
                    hit.Value.Z - _planeNormal.Z * dist);
                _planePoints[_dragPointIndex] = projected;
            }
            else
            {
                _planePoints[_dragPointIndex] = hit.Value;
            }

            _planeMarkers[_dragPointIndex].Center = _planePoints[_dragPointIndex];
            if (_planePoints.Count == 3) ComputePlane();
            StatusText.Text = $"Dragging point {_dragPointIndex + 1}...";
        }

        // Dragging a condyle box
        if (_currentStep == 2 && _isDragging)
        {
            var center = _draggingLeft ? _leftCondyleCenter : _rightCondyleCenter;
            if (center != null)
            {
                center[0] = (float)hit.Value.X; center[1] = (float)hit.Value.Y; center[2] = (float)hit.Value.Z;
                RebuildBoxVisuals();
                StatusText.Text = $"{(_draggingLeft ? "Left" : "Right")} condyle: ({center[0]:F1}, {center[1]:F1}, {center[2]:F1})";
            }
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPointIndex = -1;
        _isDragging = false;
    }

    // ════════════════════════════════════════
    // Visual helpers
    // ════════════════════════════════════════
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
        { _leftBoxVisual = CreateBoxVisual(_leftCondyleCenter, DefaultHalfExtents, Colors.LimeGreen); MainViewport.Children.Add(_leftBoxVisual); }
        if (_rightCondyleCenter != null)
        { _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, DefaultHalfExtents, Colors.OrangeRed); MainViewport.Children.Add(_rightBoxVisual); }
    }

    private ModelVisual3D CreateBoxVisual(float[] center, float[] he, Color color)
    {
        double cx = center[0], cy = center[1], cz = center[2];
        double hx = he[0], hy = he[1], hz = he[2];
        var c0 = new Point3D(cx-hx,cy-hy,cz-hz); var c1 = new Point3D(cx+hx,cy-hy,cz-hz);
        var c2 = new Point3D(cx+hx,cy+hy,cz-hz); var c3 = new Point3D(cx-hx,cy+hy,cz-hz);
        var c4 = new Point3D(cx-hx,cy-hy,cz+hz); var c5 = new Point3D(cx+hx,cy-hy,cz+hz);
        var c6 = new Point3D(cx+hx,cy+hy,cz+hz); var c7 = new Point3D(cx-hx,cy+hy,cz+hz);

        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(new[]{c0,c1,c2,c3,c4,c5,c6,c7});
        mesh.TriangleIndices = new Int32Collection(new[]{0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,2,6,7,2,7,3,0,3,7,0,7,4,1,5,6,1,6,2});
        mesh.Freeze();
        var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        var corners = new[]{c0,c1,c2,c3,c4,c5,c6,c7};
        int[,] edges = {{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        var lines = new LinesVisual3D { Color = color, Thickness = 2 };
        for (int e = 0; e < 12; e++) { lines.Points.Add(corners[edges[e,0]]); lines.Points.Add(corners[edges[e,1]]); }

        var parent = new ModelVisual3D { Content = model };
        parent.Children.Add(lines);
        return parent;
    }

    private void AddSphereMarker(float[] center, Color color, double radius)
    {
        MainViewport.Children.Add(new SphereVisual3D
        {
            Center = new Point3D(center[0], center[1], center[2]),
            Radius = radius, Fill = new SolidColorBrush(color)
        });
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

    // ════════════════════════════════════════
    // Button handlers
    // ════════════════════════════════════════
    private void Split_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1) GoToCondyleStep();
        else if (_currentStep == 2) PerformPlaneSplit();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true; DialogResult = true; Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) { SetupStep1(); return; }
        Accepted = false; DialogResult = false; Close();
    }
}
