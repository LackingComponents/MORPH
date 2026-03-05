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
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class CondyleSplitWindow : Window
{
    // Input data
    private readonly List<float[]> _boneVerts;
    private readonly List<float[]> _upperCastVerts;
    private readonly List<float[]> _lowerCastVerts;
    private readonly VolumeData? _ctVolume; // CT bone mask for future use

    // User-picked arch landmarks
    private readonly List<(double X, double Y, double Z)> _archLandmarks = new();
    private readonly List<Visual3D> _archMarkerVisuals = new();

    // Condylar bounding boxes
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private static readonly float[] DefaultHalfExtents = { 15f, 15f, 15f }; // 30mm cube

    // Intermediate mesh after arch subtraction
    private List<float[]>? _archSubtractedBone;

    // Final results
    private List<float[]>? _craniumVerts;
    private List<float[]>? _mandibleVerts;

    // Interactive drag state
    private bool _isDragging;
    private bool _draggingLeft;

    // Visuals for bounding boxes
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;

    // 1=pick arch, 2=place condyles, 3=review
    private int _currentStep = 1;

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
    // STEP 1: Pick arch landmarks
    // ════════════════════════════════════════
    private void SetupStep1()
    {
        _currentStep = 1;
        _archLandmarks.Clear();
        _archMarkerVisuals.Clear();
        _leftCondyleCenter = null;
        _rightCondyleCenter = null;
        _archSubtractedBone = null;

        MainViewport.Children.Clear();
        AddLighting();

        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 200, 190, 180, 220);
        MainViewport.Children.Add(new ModelVisual3D { Content = boneModel });

        var upperModel = MeshHelper.BuildModel3D(_upperCastVerts, 255, 220, 80, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = upperModel });

        var lowerModel = MeshHelper.BuildModel3D(_lowerCastVerts, 80, 160, 255, 180);
        MainViewport.Children.Add(new ModelVisual3D { Content = lowerModel });

        MainViewport.ZoomExtents(500);

        StepTitle.Text = "Step 1: Pick Occlusal Arch Points";
        StepInstructions.Text =
            "Click 3–5 points along the dental arch: between the central incisors + " +
            "occlusal surface of most posterior teeth (left & right). Then click 'Remove Occlusal Region'.";
        StatusText.Text = "0 arch points placed (need at least 3)";
        SplitBtn.Visibility = Visibility.Visible;
        SplitBtn.Content = "Remove Occlusal Region";
        SplitBtn.IsEnabled = true;
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    // ════════════════════════════════════════
    // STEP 1→2: Arch subtraction (dental cast removal + arch tube)
    // ════════════════════════════════════════
    private async void PerformArchSubtraction()
    {
        if (_archLandmarks.Count < 3)
        {
            MessageBox.Show("Please pick at least 3 arch points.", "Need More Points",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SplitBtn.IsEnabled = false;
        StatusText.Text = "Computing arch subtraction... please wait.";

        try
        {
            // Run heavy computation on background thread
            var archLandmarksCopy = _archLandmarks.ToList();
            var boneVertsCopy = _boneVerts;
            var upperCopy = _upperCastVerts;
            var lowerCopy = _lowerCastVerts;

            var result = await Task.Run(() =>
            {
                // 1. Build dense 3D Catmull-Rom spline through the arch landmarks
                var spline3D = SplineHelper.ComputeCatmullRom3D(archLandmarksCopy, stepsPerSegment: 50);

                // 2. Remove bone triangles near dental casts (proximity subtraction)
                var allCastVerts = MeshOps.MergeVertices(upperCopy, lowerCopy);
                var castTree = new KdTree();
                castTree.Build(allCastVerts);
                var cleaned = MeshOps.SubtractByProximity(boneVertsCopy, castTree, 2.0f);

                // 3. Remove bone inside the arch tube (10mm radius = 20mm diameter in all directions)
                cleaned = MeshOps.SubtractByArchVolume(cleaned, spline3D, 10.0f);

                return cleaned;
            });

            _archSubtractedBone = result;

            // Show the result
            Dispatcher.Invoke(() =>
            {
                MainViewport.Children.Clear();
                AddLighting();

                var boneModel = MeshHelper.BuildModel3D(_archSubtractedBone, 200, 190, 180);
                MainViewport.Children.Add(new ModelVisual3D { Content = boneModel });

                // Dental casts faintly
                var upperModel = MeshHelper.BuildModel3D(_upperCastVerts, 255, 220, 80, 100);
                MainViewport.Children.Add(new ModelVisual3D { Content = upperModel });
                var lowerModel = MeshHelper.BuildModel3D(_lowerCastVerts, 80, 160, 255, 100);
                MainViewport.Children.Add(new ModelVisual3D { Content = lowerModel });

                MainViewport.ZoomExtents(500);

                _currentStep = 2;
                StepTitle.Text = "Step 2: Place Condylar Bounding Boxes";
                StepInstructions.Text =
                    "Click on the lateral aspect of each condyle (left, then right) to grow " +
                    "a 30mm bounding box. Drag boxes to reposition. Then click 'Split'.";
                StatusText.Text = $"Arch subtracted. {_archSubtractedBone.Count / 3} tris remaining. Place condylar boxes now.";
                SplitBtn.Content = "Split";
                SplitBtn.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Arch subtraction failed:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SplitBtn.IsEnabled = true;
                StatusText.Text = "Error during arch subtraction. Try again.";
            });
        }
    }

    // ════════════════════════════════════════
    // STEP 2→3: Final split using condylar boxes + CC labeling
    // ════════════════════════════════════════
    private async void PerformFinalSplit()
    {
        if (_leftCondyleCenter == null || _rightCondyleCenter == null)
        {
            MessageBox.Show("Please place both condylar bounding boxes first.",
                "Missing Condyles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_archSubtractedBone == null) return;

        SplitBtn.IsEnabled = false;
        StatusText.Text = "Computing final split... please wait.";

        try
        {
            var bone = _archSubtractedBone;
            var leftC = _leftCondyleCenter;
            var rightC = _rightCondyleCenter;

            var (cranium, mandible) = await Task.Run(() =>
            {
                // Remove triangles inside each condylar bounding box to disconnect the TMJ
                var separated = MeshOps.ExcludeBoundingBox(bone, leftC, DefaultHalfExtents);
                separated = MeshOps.ExcludeBoundingBox(separated, rightC, DefaultHalfExtents);

                // Connected component labeling to find the pieces
                var components = MeshOps.LabelConnectedComponents(separated);

                // Largest = cranium, second = mandible
                var cran = components.Count > 0 ? components[0] : new List<float[]>();
                var mand = components.Count > 1 ? components[1] : new List<float[]>();

                // Re-add condylar box contents: above midZ → cranium, below → mandible
                float midZ = (leftC[2] + rightC[2]) / 2f;
                var leftBoxTris = MeshOps.ClipToBoundingBox(bone, leftC, DefaultHalfExtents);
                var rightBoxTris = MeshOps.ClipToBoundingBox(bone, rightC, DefaultHalfExtents);
                var condyleTris = MeshOps.MergeVertices(leftBoxTris, rightBoxTris);
                var (above, below) = MeshOps.SplitByZPlane(condyleTris, midZ);

                cran = MeshOps.MergeVertices(cran, above);
                mand = MeshOps.MergeVertices(mand, below);

                return (cran, mand);
            });

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

                // Condylar axis
                var axisLine = new LinesVisual3D { Color = Colors.Red, Thickness = 3 };
                axisLine.Points.Add(new Point3D(_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]));
                axisLine.Points.Add(new Point3D(_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]));
                MainViewport.Children.Add(axisLine);

                AddSphereMarker(_leftCondyleCenter, Colors.LimeGreen, 3);
                AddSphereMarker(_rightCondyleCenter, Colors.OrangeRed, 3);
                RebuildBoxVisuals();
                MainViewport.ZoomExtents(500);

                // Set public results
                CraniumResult = _craniumVerts;
                MandibleResult = _mandibleVerts;
                LeftCondyleCenter = (_leftCondyleCenter[0], _leftCondyleCenter[1], _leftCondyleCenter[2]);
                RightCondyleCenter = (_rightCondyleCenter[0], _rightCondyleCenter[1], _rightCondyleCenter[2]);

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
                StatusText.Text = "Error during split. Try again.";
            });
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
            _archLandmarks.Add((hit.Value.X, hit.Value.Y, hit.Value.Z));
            var marker = new SphereVisual3D
            {
                Center = hit.Value, Radius = 1.5,
                Fill = new SolidColorBrush(Colors.Yellow)
            };
            _archMarkerVisuals.Add(marker);
            MainViewport.Children.Add(marker);

            // Draw spline preview if 2+ points
            if (_archLandmarks.Count >= 2) DrawSplinePreview();

            StatusText.Text = $"{_archLandmarks.Count} arch points placed (need at least 3)";
            e.Handled = true;
        }
        else if (_currentStep == 2)
        {
            // Drag existing box?
            if (_leftCondyleCenter != null && DistanceTo(hit.Value, _leftCondyleCenter) < 20)
            { _isDragging = true; _draggingLeft = true; e.Handled = true; return; }
            if (_rightCondyleCenter != null && DistanceTo(hit.Value, _rightCondyleCenter) < 20)
            { _isDragging = true; _draggingLeft = false; e.Handled = true; return; }

            // Place new condyle (grow box from click point)
            if (_leftCondyleCenter == null)
            {
                _leftCondyleCenter = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Left condyle placed. Now click the RIGHT lateral condyle.";
            }
            else if (_rightCondyleCenter == null)
            {
                _rightCondyleCenter = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Both condyles placed. Drag to adjust, then click 'Split'.";
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
        center[0] = (float)hit.Value.X; center[1] = (float)hit.Value.Y; center[2] = (float)hit.Value.Z;
        RebuildBoxVisuals();
        StatusText.Text = $"{(_draggingLeft ? "Left" : "Right")} condyle: ({center[0]:F1}, {center[1]:F1}, {center[2]:F1})";
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
    }

    // ════════════════════════════════════════
    // Visual helpers
    // ════════════════════════════════════════
    private Visual3D? _splinePreviewVisual;

    private void DrawSplinePreview()
    {
        if (_splinePreviewVisual != null) MainViewport.Children.Remove(_splinePreviewVisual);

        var spline3D = SplineHelper.ComputeCatmullRom3D(_archLandmarks, 20);
        var lines = new LinesVisual3D { Color = Colors.Lime, Thickness = 2 };
        for (int i = 0; i < spline3D.Count - 1; i++)
        {
            lines.Points.Add(new Point3D(spline3D[i].X, spline3D[i].Y, spline3D[i].Z));
            lines.Points.Add(new Point3D(spline3D[i + 1].X, spline3D[i + 1].Y, spline3D[i + 1].Z));
        }
        _splinePreviewVisual = lines;
        MainViewport.Children.Add(lines);
    }

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
        if (_currentStep == 1) PerformArchSubtraction();
        else if (_currentStep == 2) PerformFinalSplit();
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
