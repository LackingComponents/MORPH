using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class DentalAlignmentWindow : Window
{
    // Mesh data
    private readonly List<float[]> _ctVertices;
    private readonly List<float[]> _stlVertices;
    private readonly List<float[]> _stlOriginalVertices; // backup for reset

    // Landmark pairs
    private readonly List<(double X, double Y, double Z)> _ctLandmarks = new();
    private readonly List<(double X, double Y, double Z)> _stlLandmarks = new();
    private readonly List<ModelVisual3D> _ctMarkers = new();
    private readonly List<ModelVisual3D> _stlMarkers = new();

    // Result
    public bool Accepted { get; private set; }
    public double[,]? FinalTransform { get; private set; }

    public DentalAlignmentWindow(List<float[]> ctVertices, List<float[]> stlVertices)
    {
        InitializeComponent();

        _ctVertices = ctVertices;
        _stlVertices = stlVertices;
        _stlOriginalVertices = stlVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();

        Loaded += (_, _) => SetupViewports();
    }

    private void SetupViewports()
    {
        // Add CT model to the left viewport
        var ctModel = MeshHelper.BuildModel3D(_ctVertices, 230, 210, 180);
        var ctVisual = new ModelVisual3D { Content = ctModel };
        CtViewport.Children.Add(ctVisual);

        // Add ambient light
        var ctLight = new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) };
        CtViewport.Children.Add(ctLight);

        // Add STL model to the right viewport
        var stlModel = MeshHelper.BuildModel3D(_stlVertices, 245, 245, 230);
        var stlVisual = new ModelVisual3D { Content = stlModel };
        StlViewport.Children.Add(stlVisual);

        var stlLight = new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) };
        StlViewport.Children.Add(stlLight);

        // Zoom to fit
        CtViewport.ZoomExtents(500);
        StlViewport.ZoomExtents(500);
    }

    private void CtViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        var pos = e.GetPosition(CtViewport);
        var hits = CtViewport.Viewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        var hit = hits[0];
        var point = hit.Position;

        _ctLandmarks.Add((point.X, point.Y, point.Z));
        AddMarker(CtViewport, _ctMarkers, point, Colors.LimeGreen, _ctLandmarks.Count);
        UpdateLandmarkUI();
        e.Handled = true;
    }

    private void StlViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        var pos = e.GetPosition(StlViewport);
        var hits = StlViewport.Viewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        var hit = hits[0];
        var point = hit.Position;

        _stlLandmarks.Add((point.X, point.Y, point.Z));
        AddMarker(StlViewport, _stlMarkers, point, Colors.OrangeRed, _stlLandmarks.Count);
        UpdateLandmarkUI();
        e.Handled = true;
    }

    private void AddMarker(HelixViewport3D viewport, List<ModelVisual3D> markerList,
        Point3D position, Color color, int number)
    {
        // Sphere marker using HelixToolkit's built-in SphereVisual3D
        var sphere = new SphereVisual3D
        {
            Center = position,
            Radius = 1.5,
            Fill = new SolidColorBrush(color)
        };
        viewport.Children.Add(sphere);
        markerList.Add(sphere);

        // Number label
        var label = new BillboardTextVisual3D
        {
            Text = number.ToString(),
            Position = new Point3D(position.X, position.Y, position.Z + 3),
            Foreground = new SolidColorBrush(color),
            FontSize = 14
        };
        viewport.Children.Add(label);
        markerList.Add(label);
    }

    private void UpdateLandmarkUI()
    {
        int pairs = Math.Min(_ctLandmarks.Count, _stlLandmarks.Count);
        LandmarkCountText.Text = $"CT: {_ctLandmarks.Count} pts | STL: {_stlLandmarks.Count} pts | Pairs: {pairs}";
        ComputeBtn.IsEnabled = pairs >= 3;
    }

    private void ClearLandmarks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var m in _ctMarkers) CtViewport.Children.Remove(m);
        foreach (var m in _stlMarkers) StlViewport.Children.Remove(m);
        _ctMarkers.Clear();
        _stlMarkers.Clear();
        _ctLandmarks.Clear();
        _stlLandmarks.Clear();
        UpdateLandmarkUI();
        RmsText.Text = "";
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    private async void ComputeAlignment_Click(object sender, RoutedEventArgs e)
    {
        ComputeBtn.IsEnabled = false;
        StepTitle.Text = "Step 2: Computing ICP Alignment...";
        StepInstructions.Text = "Running landmark registration followed by ICP refinement. Please wait...";

        try
        {
            // Step 1: Compute initial transform from landmark pairs
            var initialTransform = IcpAligner.ComputeLandmarkTransform(_stlLandmarks, _ctLandmarks);

            // Step 2: Run ICP refinement
            var result = await Task.Run(() =>
                IcpAligner.Align(_stlOriginalVertices, _ctVertices, initialTransform, maxIterations: 80, tolerance: 0.001,
                    progress: p => Dispatcher.Invoke(() => StepInstructions.Text = $"ICP iteration... {p * 100:F0}%")));

            FinalTransform = result.Transform;

            // Apply transform to the STL vertices for preview
            var previewVerts = _stlOriginalVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
            IcpAligner.TransformVertices(previewVerts, result.Transform);

            // Update the STL viewport to show aligned result overlaid on CT
            // Clear the STL viewport and show both overlaid
            StlViewport.Children.Clear();
            var stlLight = new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) };
            StlViewport.Children.Add(stlLight);

            // Show CT as translucent
            var ctModel = MeshHelper.BuildModel3D(_ctVertices, 180, 200, 220);
            var ctVisual = new ModelVisual3D { Content = ctModel };
            StlViewport.Children.Add(ctVisual);

            // Show aligned STL on top
            var alignedModel = MeshHelper.BuildModel3D(previewVerts, 245, 200, 150);
            var alignedVisual = new ModelVisual3D { Content = alignedModel };
            StlViewport.Children.Add(alignedVisual);

            StlViewport.ZoomExtents(500);

            RmsText.Text = $"RMS: {result.RmsError:F3} mm | {result.Iterations} iterations";
            StepTitle.Text = "Step 3: Review Alignment";
            StepInstructions.Text = "Right viewport shows the overlay. If the alignment looks good, click Accept. Otherwise, clear landmarks and try again.";
            AcceptBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Alignment failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StepTitle.Text = "Step 1: Pick Matching Landmarks";
            StepInstructions.Text = "Alignment failed. Try picking different landmarks.";
        }
        finally
        {
            ComputeBtn.IsEnabled = true;
        }
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
