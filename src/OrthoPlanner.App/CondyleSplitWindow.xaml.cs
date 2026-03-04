using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class CondyleSplitWindow : Window
{
    // Input data
    private readonly List<float[]> _boneVerts;
    private readonly List<float[]> _upperCastVerts;
    private readonly List<float[]> _lowerCastVerts;

    // User-picked arch landmarks (3D points on the bone surface along the occlusal arch)
    private readonly List<(double X, double Y, double Z)> _archLandmarks = new();
    private readonly List<Visual3D> _archMarkerVisuals = new();

    // Condylar bounding boxes
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private static readonly float[] DefaultHalfExtents = { 12.5f, 12.5f, 12.5f }; // 25mm cube

    // Computed results
    private List<float[]>? _craniumVerts;
    private List<float[]>? _mandibleVerts;

    // Interactive drag state
    private bool _isDragging;
    private bool _draggingLeft;

    // Visuals for bounding boxes
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;

    // Step state: 1 = pick arch, 2 = place condyles, 3 = review
    private int _currentStep = 1;

    // Public results
    public bool Accepted { get; private set; }
    public List<float[]>? CraniumResult { get; private set; }
    public List<float[]>? MandibleResult { get; private set; }
    public (double X, double Y, double Z)? LeftCondyleCenter { get; private set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; private set; }

    public CondyleSplitWindow(List<float[]> boneVerts, List<float[]> upperCastVerts, List<float[]> lowerCastVerts)
    {
        InitializeComponent();
        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _upperCastVerts = upperCastVerts;
        _lowerCastVerts = lowerCastVerts;

        Loaded += (_, _) => SetupStep1();
    }

    // ════════════════════════════════════════
    // STEP 1: Preview bone + casts, pick arch landmarks
    // ════════════════════════════════════════
    private void SetupStep1()
    {
        _currentStep = 1;
        _archLandmarks.Clear();
        _archMarkerVisuals.Clear();
        _leftCondyleCenter = null;
        _rightCondyleCenter = null;

        MainViewport.Children.Clear();
        AddLighting();

        // Bone mesh (gray, slightly transparent so dental casts peek through)
        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180, 220);
        MainViewport.Children.Add(new ModelVisual3D { Content = boneModel });

        // Upper cast (gold)
        var upperModel = MeshHelper.BuildModel3D(_upperCastVerts, 255, 220, 80, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = upperModel });

        // Lower cast (light blue)
        var lowerModel = MeshHelper.BuildModel3D(_lowerCastVerts, 80, 160, 255, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = lowerModel });

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 1: Pick Occlusal Arch Points";
        StepInstructions.Text = "Click 3–5 points along the dental arch: between the central incisors plus the occlusal surface of the most posterior teeth (left & right). Then click 'Compute Separation'.";
        StatusText.Text = $"Bone: {_boneVerts.Count / 3} tris | 0 arch points placed";
        SplitBtn.Visibility = Visibility.Visible;
        SplitBtn.Content = "Compute Separation";
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    // ════════════════════════════════════════
    // STEP 2: Perform arch subtraction + show condyle placement
    // ════════════════════════════════════════
    private void PerformArchSeparation()
    {
        if (_archLandmarks.Count < 3)
        {
            MessageBox.Show("Please pick at least 3 arch points before computing separation.",
                "Need More Points", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Computing separation...";

            // 1. Build dense 3D spline through the arch landmarks
            var curveXY = _archLandmarks.Select(p => (p.X, p.Y)).ToList();
            var spline2D = SplineHelper.ComputeCatmullRom2D(curveXY, stepsPerSegment: 40);

            // Interpolate Z along the spline by using the same parametric fraction
            var spline3D = new List<(double X, double Y, double Z)>();
            int totalSteps = spline2D.Count;
            for (int i = 0; i < totalSteps; i++)
            {
                double t = (double)i / Math.Max(1, totalSteps - 1);
                // Linearly interpolate Z through the landmarks
                double z = InterpolateZ(_archLandmarks, t);
                spline3D.Add((spline2D[i].X, spline2D[i].Y, z));
            }

            // 2. Remove bone triangles near dental casts (proximity subtraction)
            var allCastVerts = MeshOps.MergeVertices(_upperCastVerts, _lowerCastVerts);
            var castTree = new KdTree();
            castTree.Build(allCastVerts);
            var cleanedBone = MeshOps.SubtractByProximity(_boneVerts, castTree, 2.0f);

            // 3. Remove bone triangles inside the arch tube volume (10mm radius = 20mm diameter)
            cleanedBone = MeshOps.SubtractByArchVolume(cleanedBone, spline3D, 10.0f);

            // 4. Find connected components
            var components = MeshOps.LabelConnectedComponents(cleanedBone);

            if (components.Count < 2)
            {
                MessageBox.Show(
                    $"Separation produced {components.Count} piece(s) instead of 2+. Try adjusting your arch points or increasing the subtraction radius.",
                    "Separation Incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Still show what we got
            }

            // 5. Assign: largest = cranium, second largest = mandible
            _craniumVerts = components.Count > 0 ? components[0] : new List<float[]>();
            _mandibleVerts = components.Count > 1 ? components[1] : new List<float[]>();

            // 6. Show result
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

            MainViewport.ZoomExtents(500);

            StepTitle.Text = "Step 2: Place Condylar Bounding Boxes";
            StepInstructions.Text = "Click on each lateral condyle (left, then right) to place a bounding box. Drag boxes to adjust. Then click 'Confirm Condyles'.";
            StatusText.Text = $"Cranium: {_craniumVerts.Count / 3} tris | Mandible: {_mandibleVerts.Count / 3} tris | {components.Count} total components";
            SplitBtn.Visibility = Visibility.Collapsed;
            ConfirmBtn.Visibility = Visibility.Visible;
            _currentStep = 2;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Separation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ════════════════════════════════════════
    // STEP 3: Finalize
    // ════════════════════════════════════════
    private void FinalizeCondyles()
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null ||
            _craniumVerts == null || _mandibleVerts == null)
            return;

        CraniumResult = _craniumVerts;
        MandibleResult = _mandibleVerts;
        LeftCondyleCenter = (_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]);
        RightCondyleCenter = (_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]);

        // Show final preview
        MainViewport.Children.Clear();
        AddLighting();

        var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
        MainViewport.Children.Add(new ModelVisual3D { Content = cranModel });

        var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 180, 200, 220);
        MainViewport.Children.Add(new ModelVisual3D { Content = mandModel });

        // Red condylar axis line
        var axisLine = new LinesVisual3D { Color = Colors.Red, Thickness = 3 };
        axisLine.Points.Add(new Point3D(_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]));
        axisLine.Points.Add(new Point3D(_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]));
        MainViewport.Children.Add(axisLine);

        // Condyle markers
        AddSphereMarker(_leftCondyleCenter, Colors.LimeGreen, 3);
        AddSphereMarker(_rightCondyleCenter, Colors.OrangeRed, 3);

        // Keep bounding boxes
        RebuildBoxVisuals();

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 3: Review & Accept";
        StepInstructions.Text = "Red line = condylar rotation axis. Green/Orange = fulcrum centers. Click Accept to finalize.";
        StatusText.Text = $"L: ({_leftCondyleCenter[0]:F1}, {_leftCondyleCenter[1]:F1}, {_leftCondyleCenter[2]:F1}) | R: ({_rightCondyleCenter[0]:F1}, {_rightCondyleCenter[1]:F1}, {_rightCondyleCenter[2]:F1})";
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Visible;
        _currentStep = 3;
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
            // Place arch landmark
            _archLandmarks.Add((hit.Value.X, hit.Value.Y, hit.Value.Z));
            var marker = new SphereVisual3D
            {
                Center = hit.Value,
                Radius = 1.5,
                Fill = new SolidColorBrush(Colors.Yellow)
            };
            _archMarkerVisuals.Add(marker);
            MainViewport.Children.Add(marker);

            StatusText.Text = $"{_archLandmarks.Count} arch point(s) placed. Need at least 3.";
            e.Handled = true;
        }
        else if (_currentStep == 2)
        {
            // Check for condyle drag
            if (_leftCondyleCenter != null && DistanceTo(hit.Value, _leftCondyleCenter) < 15)
            {
                _isDragging = true;
                _draggingLeft = true;
                e.Handled = true;
                return;
            }
            if (_rightCondyleCenter != null && DistanceTo(hit.Value, _rightCondyleCenter) < 15)
            {
                _isDragging = true;
                _draggingLeft = false;
                e.Handled = true;
                return;
            }

            // Place new condyle box
            if (_leftCondyleCenter == null)
            {
                _leftCondyleCenter = new float[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = $"Left condyle placed. Now click on the RIGHT lateral condyle.";
            }
            else if (_rightCondyleCenter == null)
            {
                _rightCondyleCenter = new float[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Both condyles placed. Drag to adjust, then click 'Confirm Condyles'.";
            }

            RebuildBoxVisuals();
            e.Handled = true;
        }
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _currentStep != 2) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _isDragging = false; return; }

        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        var center = _draggingLeft ? _leftCondyleCenter : _rightCondyleCenter;
        if (center == null) return;

        center[0] = (float)hit.Value.X;
        center[1] = (float)hit.Value.Y;
        center[2] = (float)hit.Value.Z;

        RebuildBoxVisuals();
        string side = _draggingLeft ? "Left" : "Right";
        StatusText.Text = $"{side} condyle moved to ({center[0]:F1}, {center[1]:F1}, {center[2]:F1})";
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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
        {
            _leftBoxVisual = CreateBoxVisual(_leftCondyleCenter, DefaultHalfExtents, Colors.LimeGreen);
            MainViewport.Children.Add(_leftBoxVisual);
        }
        if (_rightCondyleCenter != null)
        {
            _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, DefaultHalfExtents, Colors.OrangeRed);
            MainViewport.Children.Add(_rightBoxVisual);
        }
    }

    private ModelVisual3D CreateBoxVisual(float[] center, float[] halfExtents, Color color)
    {
        double cx = center[0], cy = center[1], cz = center[2];
        double hx = halfExtents[0], hy = halfExtents[1], hz = halfExtents[2];

        var c0 = new Point3D(cx - hx, cy - hy, cz - hz);
        var c1 = new Point3D(cx + hx, cy - hy, cz - hz);
        var c2 = new Point3D(cx + hx, cy + hy, cz - hz);
        var c3 = new Point3D(cx - hx, cy + hy, cz - hz);
        var c4 = new Point3D(cx - hx, cy - hy, cz + hz);
        var c5 = new Point3D(cx + hx, cy - hy, cz + hz);
        var c6 = new Point3D(cx + hx, cy + hy, cz + hz);
        var c7 = new Point3D(cx - hx, cy + hy, cz + hz);

        var boxMesh = new MeshGeometry3D();
        boxMesh.Positions = new Point3DCollection(new[] { c0, c1, c2, c3, c4, c5, c6, c7 });
        boxMesh.TriangleIndices = new Int32Collection(new[]
        {
            0,1,2, 0,2,3, 4,6,5, 4,7,6,
            0,4,5, 0,5,1, 2,6,7, 2,7,3,
            0,3,7, 0,7,4, 1,5,6, 1,6,2
        });
        boxMesh.Freeze();

        var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        var boxModel = new GeometryModel3D(boxMesh, material) { BackMaterial = material };
        boxModel.Freeze();

        var corners = new[] { c0, c1, c2, c3, c4, c5, c6, c7 };
        int[,] edges = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
        var lines = new LinesVisual3D { Color = color, Thickness = 2 };
        for (int e = 0; e < 12; e++)
        {
            lines.Points.Add(corners[edges[e, 0]]);
            lines.Points.Add(corners[edges[e, 1]]);
        }

        var parent = new ModelVisual3D { Content = boxModel };
        parent.Children.Add(lines);
        return parent;
    }

    private void AddSphereMarker(float[] center, Color color, double radius)
    {
        var sphere = new SphereVisual3D
        {
            Center = new Point3D(center[0], center[1], center[2]),
            Radius = radius,
            Fill = new SolidColorBrush(color)
        };
        MainViewport.Children.Add(sphere);
    }

    private Point3D? GetHitPoint(Point screenPos)
    {
        var result = Viewport3DHelper.FindHits(MainViewport.Viewport, screenPos);
        if (result != null && result.Count > 0)
            return result[0].Position;
        return null;
    }

    private float DistanceTo(Point3D p, float[] center)
    {
        double dx = p.X - center[0], dy = p.Y - center[1], dz = p.Z - center[2];
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Linearly interpolate Z across the landmark list by parametric fraction t in [0,1].
    /// </summary>
    private double InterpolateZ(List<(double X, double Y, double Z)> landmarks, double t)
    {
        if (landmarks.Count == 1) return landmarks[0].Z;
        double idx = t * (landmarks.Count - 1);
        int lo = Math.Max(0, (int)Math.Floor(idx));
        int hi = Math.Min(landmarks.Count - 1, lo + 1);
        double frac = idx - lo;
        return landmarks[lo].Z * (1 - frac) + landmarks[hi].Z * frac;
    }

    // ════════════════════════════════════════
    // Button handlers
    // ════════════════════════════════════════
    private void Split_Click(object sender, RoutedEventArgs e)
    {
        PerformArchSeparation();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null)
        {
            MessageBox.Show("Please click on both the LEFT and RIGHT lateral condyles before confirming.",
                "Missing Condyles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        FinalizeCondyles();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            SetupStep1();
            return;
        }

        Accepted = false;
        DialogResult = false;
        Close();
    }
}
