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

    // Computed results
    private float _zCut;
    private List<float[]>? _craniumVerts;
    private List<float[]>? _mandibleVerts;

    // Condylar bounding boxes
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private static readonly float[] DefaultHalfExtents = { 12.5f, 12.5f, 12.5f }; // 25mm cube

    // Interactive drag state
    private bool _isDragging;
    private bool _draggingLeft; // true = left condyle box, false = right
    private Point _dragStart;

    // Visuals for bounding boxes
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;

    // Step state
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
        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList(); // Deep copy
        _upperCastVerts = upperCastVerts;
        _lowerCastVerts = lowerCastVerts;

        Loaded += (_, _) => SetupStep1();
    }

    // ════════════════════════════════════════
    // STEP 1: Preview bone + dental casts
    // ════════════════════════════════════════
    private void SetupStep1()
    {
        MainViewport.Children.Clear();
        AddLighting();

        // Compute separation Z plane
        float upperZ = MeshOps.AverageZ(_upperCastVerts);
        float lowerZ = MeshOps.AverageZ(_lowerCastVerts);
        _zCut = (upperZ + lowerZ) / 2f;

        // Bone mesh (gray)
        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = boneModel });

        // Upper cast (gold)
        var upperModel = MeshHelper.BuildModel3D(_upperCastVerts, 255, 220, 80, 200);
        MainViewport.Children.Add(new ModelVisual3D { Content = upperModel });

        // Lower cast (light blue)
        var lowerModel = MeshHelper.BuildModel3D(_lowerCastVerts, 80, 160, 255, 200);
        MainViewport.Children.Add(new ModelVisual3D { Content = lowerModel });

        // Green separation plane visual
        AddSeparationPlane(_zCut);

        MainViewport.ZoomExtents(500);
        StatusText.Text = $"Separation Z = {_zCut:F1} mm | Bone: {_boneVerts.Count / 3} tris | Upper: {_upperCastVerts.Count / 3} tris | Lower: {_lowerCastVerts.Count / 3} tris";
    }

    // ════════════════════════════════════════
    // STEP 2: Split and show condyle placement
    // ════════════════════════════════════════
    private void PerformSplit()
    {
        // 1. Build KdTree from dental casts
        var allCastVerts = MeshOps.MergeVertices(_upperCastVerts, _lowerCastVerts);
        var castTree = new KdTree();
        castTree.Build(allCastVerts);

        // 2. Remove bone triangles that overlap with dental casts
        var cleanedBone = MeshOps.SubtractByProximity(_boneVerts, castTree, 2.0f);

        // 3. Fuse dental casts into cleaned bone
        var fusedWithUpper = MeshOps.MergeVertices(cleanedBone, _upperCastVerts);
        var fusedComplete = MeshOps.MergeVertices(fusedWithUpper, _lowerCastVerts);

        // 4. Split at Z plane
        var (above, below) = MeshOps.SplitByZPlane(fusedComplete, _zCut);
        _craniumVerts = above;
        _mandibleVerts = below;

        // 5. Show result
        MainViewport.Children.Clear();
        AddLighting();

        // Cranium (warm bone)
        if (_craniumVerts.Count > 0)
        {
            var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
            MainViewport.Children.Add(new ModelVisual3D { Content = cranModel });
        }

        // Mandible (cooler)
        if (_mandibleVerts.Count > 0)
        {
            var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 180, 200, 220);
            MainViewport.Children.Add(new ModelVisual3D { Content = mandModel });
        }

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 2: Place Condylar Bounding Boxes";
        StepInstructions.Text = "Click on each lateral condyle (left and right) to place a bounding box. Drag boxes to adjust. Then click 'Confirm Condyles'.";
        StatusText.Text = $"Cranium: {(_craniumVerts?.Count ?? 0) / 3} tris | Mandible: {(_mandibleVerts?.Count ?? 0) / 3} tris";
        SplitBtn.Visibility = Visibility.Collapsed;
        ConfirmBtn.Visibility = Visibility.Visible;
        _currentStep = 2;
    }

    // ════════════════════════════════════════
    // STEP 3: Finalize
    // ════════════════════════════════════════
    private void FinalizeCondyles()
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null ||
            _craniumVerts == null || _mandibleVerts == null)
            return;

        // For each bounding box region: triangles above zCut stay with cranium, below with mandible
        // (They should already be split at the Z-plane, so this just ensures clean assignment)

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

        // Show condylar axis as a red line connecting the two box centers
        var axisLine = new LinesVisual3D
        {
            Color = Colors.Red,
            Thickness = 3
        };
        axisLine.Points.Add(new Point3D(_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]));
        axisLine.Points.Add(new Point3D(_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]));
        MainViewport.Children.Add(axisLine);

        // Show markers at each center
        AddCondyleMarker(_leftCondyleCenter, Colors.LimeGreen, "L");
        AddCondyleMarker(_rightCondyleCenter, Colors.OrangeRed, "R");

        // Keep bounding boxes visible
        RebuildBoxVisuals();

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 3: Review & Accept";
        StepInstructions.Text = "Red line = condylar rotation axis. Green/Orange spheres = fulcrum centers. Click Accept to finalize.";
        StatusText.Text = $"Left condyle: ({_leftCondyleCenter[0]:F1}, {_leftCondyleCenter[1]:F1}, {_leftCondyleCenter[2]:F1}) | " +
                          $"Right condyle: ({_rightCondyleCenter[0]:F1}, {_rightCondyleCenter[1]:F1}, {_rightCondyleCenter[2]:F1})";
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Visible;
        _currentStep = 3;
    }

    // ════════════════════════════════════════
    // Mouse interaction for bounding box placement
    // ════════════════════════════════════════

    private void Viewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentStep != 2) return;

        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        // Check if clicking near an existing box center to start drag
        if (_leftCondyleCenter != null && DistanceTo(hit.Value, _leftCondyleCenter) < 15)
        {
            _isDragging = true;
            _draggingLeft = true;
            _dragStart = pos;
            e.Handled = true;
            return;
        }
        if (_rightCondyleCenter != null && DistanceTo(hit.Value, _rightCondyleCenter) < 15)
        {
            _isDragging = true;
            _draggingLeft = false;
            _dragStart = pos;
            e.Handled = true;
            return;
        }

        // Place new box
        if (_leftCondyleCenter == null)
        {
            _leftCondyleCenter = new float[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
            StatusText.Text = $"Left condyle placed at ({hit.Value.X:F1}, {hit.Value.Y:F1}, {hit.Value.Z:F1}). Now click on the RIGHT lateral condyle.";
        }
        else if (_rightCondyleCenter == null)
        {
            _rightCondyleCenter = new float[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
            StatusText.Text = $"Both condyles placed. Drag to adjust, then click 'Confirm Condyles'.";
        }

        RebuildBoxVisuals();
        e.Handled = true;
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

    private void AddSeparationPlane(float z)
    {
        var planeVisual = new RectangleVisual3D
        {
            Origin = new Point3D(0, 0, z),
            Normal = new Vector3D(0, 0, 1),
            Width = 300,
            Length = 300,
            Fill = new SolidColorBrush(Color.FromArgb(60, 0, 255, 100))
        };
        MainViewport.Children.Add(planeVisual);
    }

    private void RebuildBoxVisuals()
    {
        // Remove old visuals
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

        // 8 corners
        var c0 = new Point3D(cx - hx, cy - hy, cz - hz);
        var c1 = new Point3D(cx + hx, cy - hy, cz - hz);
        var c2 = new Point3D(cx + hx, cy + hy, cz - hz);
        var c3 = new Point3D(cx - hx, cy + hy, cz - hz);
        var c4 = new Point3D(cx - hx, cy - hy, cz + hz);
        var c5 = new Point3D(cx + hx, cy - hy, cz + hz);
        var c6 = new Point3D(cx + hx, cy + hy, cz + hz);
        var c7 = new Point3D(cx - hx, cy + hy, cz + hz);

        // Semi-transparent solid box (6 faces, 12 triangles)
        var boxMesh = new MeshGeometry3D();
        boxMesh.Positions = new Point3DCollection(new[] { c0, c1, c2, c3, c4, c5, c6, c7 });
        boxMesh.TriangleIndices = new Int32Collection(new[]
        {
            0,1,2, 0,2,3,  // bottom
            4,6,5, 4,7,6,  // top
            0,4,5, 0,5,1,  // front
            2,6,7, 2,7,3,  // back
            0,3,7, 0,7,4,  // left
            1,5,6, 1,6,2   // right
        });
        boxMesh.Freeze();

        var brush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        var boxModel = new GeometryModel3D(boxMesh, material) { BackMaterial = material };
        boxModel.Freeze();

        // Wireframe edges via LinesVisual3D
        var corners = new[] { c0, c1, c2, c3, c4, c5, c6, c7 };
        int[,] edges = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
        var lines = new LinesVisual3D { Color = color, Thickness = 2 };
        for (int e = 0; e < 12; e++)
        {
            lines.Points.Add(corners[edges[e, 0]]);
            lines.Points.Add(corners[edges[e, 1]]);
        }

        // Combine into a parent visual
        var parent = new ModelVisual3D { Content = boxModel };
        parent.Children.Add(lines);
        return parent;
    }

    private void AddCondyleMarker(float[] center, Color color, string label)
    {
        var sphere = new SphereVisual3D
        {
            Center = new Point3D(center[0], center[1], center[2]),
            Radius = 3,
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

    // ════════════════════════════════════════
    // Button handlers
    // ════════════════════════════════════════

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        PerformSplit();
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
            // Go back to step 1
            _leftCondyleCenter = null;
            _rightCondyleCenter = null;
            _craniumVerts = null;
            _mandibleVerts = null;
            _currentStep = 1;
            SplitBtn.Visibility = Visibility.Visible;
            ConfirmBtn.Visibility = Visibility.Collapsed;
            AcceptBtn.Visibility = Visibility.Collapsed;
            SetupStep1();
            return;
        }

        Accepted = false;
        DialogResult = false;
        Close();
    }
}
