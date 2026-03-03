using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

/// <summary>
/// Data model for a single landmark pair displayed in the pairs list.
/// </summary>
public class LandmarkPairItem : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string Label => $"#{Index + 1}";
    public string CtText { get; set; } = "—";
    public string StlText { get; set; } = "—";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CtText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StlText)));
    }
}

public partial class DentalAlignmentWindow : Window
{
    // Mesh data
    private readonly List<float[]> _ctVertices;
    private readonly List<float[]> _stlVertices;
    private readonly List<float[]> _stlOriginalVertices; // backup for reset

    // Landmark data
    private readonly List<(double X, double Y, double Z)?> _ctLandmarks = new();
    private readonly List<(double X, double Y, double Z)?> _stlLandmarks = new();
    private readonly List<Visual3D> _ctMarkerVisuals = new();   // 2 visuals per marker (sphere + label)
    private readonly List<Visual3D> _stlMarkerVisuals = new();

    // Pairs list for the UI
    private readonly ObservableCollection<LandmarkPairItem> _pairs = new();

    // Result
    public bool Accepted { get; private set; }
    public double[,]? FinalTransform { get; private set; }

    public DentalAlignmentWindow(List<float[]> ctVertices, List<float[]> stlVertices)
    {
        InitializeComponent();
        _ctVertices = ctVertices;
        _stlVertices = stlVertices;
        _stlOriginalVertices = stlVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();

        PairsList.ItemsSource = _pairs;
        Loaded += (_, _) => SetupViewports();
    }

    private void SetupViewports()
    {
        // CT model
        var ctModel = MeshHelper.BuildModel3D(_ctVertices, 230, 210, 180);
        CtViewport.Children.Add(new ModelVisual3D { Content = ctModel });
        CtViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) });

        // STL model
        var stlModel = MeshHelper.BuildModel3D(_stlVertices, 245, 245, 230);
        StlViewport.Children.Add(new ModelVisual3D { Content = stlModel });
        StlViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) });

        CtViewport.ZoomExtents(500);
        StlViewport.ZoomExtents(500);
    }

    // ═══ Right-click landmark picking ═══

    private void CtViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CtViewport);
        var hitResult = VisualTreeHelper.HitTest(CtViewport, pos);
        if (hitResult == null) return;

        // Use Helix's viewport hit test for 3D position
        var hits = Viewport3DHelper.FindHits(CtViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        var point = hits[0].Position;

        // Determine which pair index this goes to
        int idx = GetNextCtIndex();
        SetCtLandmark(idx, point);
        e.Handled = true; // Suppress the context menu / right-click rotation
    }

    private void StlViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(StlViewport);
        var hits = Viewport3DHelper.FindHits(StlViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        var point = hits[0].Position;

        int idx = GetNextStlIndex();
        SetStlLandmark(idx, point);
        e.Handled = true;
    }

    private int GetNextCtIndex()
    {
        // Find first pair that doesn't have a CT landmark yet, or create a new pair
        for (int i = 0; i < _ctLandmarks.Count; i++)
            if (_ctLandmarks[i] == null) return i;
        return _ctLandmarks.Count; // append new
    }

    private int GetNextStlIndex()
    {
        for (int i = 0; i < _stlLandmarks.Count; i++)
            if (_stlLandmarks[i] == null) return i;
        return _stlLandmarks.Count;
    }

    private void SetCtLandmark(int idx, Point3D point)
    {
        // Extend lists if needed
        while (_ctLandmarks.Count <= idx) _ctLandmarks.Add(null);
        while (_stlLandmarks.Count <= idx) _stlLandmarks.Add(null);
        while (_ctMarkerVisuals.Count <= idx * 2 + 1)
        {
            _ctMarkerVisuals.Add(null!);
            _ctMarkerVisuals.Add(null!);
        }

        // Remove old marker if exists
        RemoveCtMarker(idx);

        _ctLandmarks[idx] = (point.X, point.Y, point.Z);

        // Add visual marker
        var (sphere, label) = CreateMarker(point, Colors.LimeGreen, idx + 1);
        CtViewport.Children.Add(sphere);
        CtViewport.Children.Add(label);
        _ctMarkerVisuals[idx * 2] = sphere;
        _ctMarkerVisuals[idx * 2 + 1] = label;

        EnsurePairItem(idx);
        UpdatePairItem(idx);
        UpdateLandmarkUI();
    }

    private void SetStlLandmark(int idx, Point3D point)
    {
        while (_ctLandmarks.Count <= idx) _ctLandmarks.Add(null);
        while (_stlLandmarks.Count <= idx) _stlLandmarks.Add(null);
        while (_stlMarkerVisuals.Count <= idx * 2 + 1)
        {
            _stlMarkerVisuals.Add(null!);
            _stlMarkerVisuals.Add(null!);
        }

        RemoveStlMarker(idx);

        _stlLandmarks[idx] = (point.X, point.Y, point.Z);

        var (sphere, label) = CreateMarker(point, Colors.OrangeRed, idx + 1);
        StlViewport.Children.Add(sphere);
        StlViewport.Children.Add(label);
        _stlMarkerVisuals[idx * 2] = sphere;
        _stlMarkerVisuals[idx * 2 + 1] = label;

        EnsurePairItem(idx);
        UpdatePairItem(idx);
        UpdateLandmarkUI();
    }

    private (SphereVisual3D sphere, BillboardTextVisual3D label) CreateMarker(Point3D position, Color color, int number)
    {
        var sphere = new SphereVisual3D
        {
            Center = position,
            Radius = 1.5,
            Fill = new SolidColorBrush(color)
        };
        var label = new BillboardTextVisual3D
        {
            Text = number.ToString(),
            Position = new Point3D(position.X, position.Y, position.Z + 3),
            Foreground = new SolidColorBrush(color),
            FontSize = 14
        };
        return (sphere, label);
    }

    private void RemoveCtMarker(int idx)
    {
        if (idx * 2 + 1 < _ctMarkerVisuals.Count)
        {
            if (_ctMarkerVisuals[idx * 2] != null) CtViewport.Children.Remove(_ctMarkerVisuals[idx * 2]);
            if (_ctMarkerVisuals[idx * 2 + 1] != null) CtViewport.Children.Remove(_ctMarkerVisuals[idx * 2 + 1]);
        }
    }

    private void RemoveStlMarker(int idx)
    {
        if (idx * 2 + 1 < _stlMarkerVisuals.Count)
        {
            if (_stlMarkerVisuals[idx * 2] != null) StlViewport.Children.Remove(_stlMarkerVisuals[idx * 2]);
            if (_stlMarkerVisuals[idx * 2 + 1] != null) StlViewport.Children.Remove(_stlMarkerVisuals[idx * 2 + 1]);
        }
    }

    // ═══ Pairs list management ═══

    private void EnsurePairItem(int idx)
    {
        while (_pairs.Count <= idx)
            _pairs.Add(new LandmarkPairItem { Index = _pairs.Count });
    }

    private void UpdatePairItem(int idx)
    {
        if (idx >= _pairs.Count) return;
        var pair = _pairs[idx];
        pair.Index = idx;
        var ct = idx < _ctLandmarks.Count ? _ctLandmarks[idx] : null;
        var stl = idx < _stlLandmarks.Count ? _stlLandmarks[idx] : null;
        pair.CtText = ct.HasValue ? $"CT({ct.Value.X:F1}, {ct.Value.Y:F1}, {ct.Value.Z:F1})" : "—";
        pair.StlText = stl.HasValue ? $"STL({stl.Value.X:F1}, {stl.Value.Y:F1}, {stl.Value.Z:F1})" : "—";
        pair.Refresh();
    }

    private void DeletePair_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int idx)
        {
            // Remove the landmarks and markers at this index
            RemoveCtMarker(idx);
            RemoveStlMarker(idx);

            if (idx < _ctLandmarks.Count) _ctLandmarks[idx] = null;
            if (idx < _stlLandmarks.Count) _stlLandmarks[idx] = null;

            // Clean up the pair item display
            if (idx < _pairs.Count)
            {
                UpdatePairItem(idx);
            }

            UpdateLandmarkUI();
        }
    }

    private void UpdateLandmarkUI()
    {
        int ctCount = _ctLandmarks.Count(l => l.HasValue);
        int stlCount = _stlLandmarks.Count(l => l.HasValue);
        int pairs = 0;
        int maxIdx = Math.Max(_ctLandmarks.Count, _stlLandmarks.Count);
        for (int i = 0; i < maxIdx; i++)
        {
            bool hasCt = i < _ctLandmarks.Count && _ctLandmarks[i].HasValue;
            bool hasStl = i < _stlLandmarks.Count && _stlLandmarks[i].HasValue;
            if (hasCt && hasStl) pairs++;
        }

        LandmarkCountText.Text = $"CT: {ctCount} | STL: {stlCount} | Complete pairs: {pairs}";
        ComputeBtn.IsEnabled = pairs >= 3;
    }

    private void ClearLandmarks_Click(object sender, RoutedEventArgs e)
    {
        // Remove all markers from viewports
        for (int i = 0; i < _ctMarkerVisuals.Count; i++)
            if (_ctMarkerVisuals[i] != null) CtViewport.Children.Remove(_ctMarkerVisuals[i]);
        for (int i = 0; i < _stlMarkerVisuals.Count; i++)
            if (_stlMarkerVisuals[i] != null) StlViewport.Children.Remove(_stlMarkerVisuals[i]);

        _ctMarkerVisuals.Clear();
        _stlMarkerVisuals.Clear();
        _ctLandmarks.Clear();
        _stlLandmarks.Clear();
        _pairs.Clear();
        UpdateLandmarkUI();
        RmsText.Text = "";
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    // ═══ Alignment computation ═══

    private async void ComputeAlignment_Click(object sender, RoutedEventArgs e)
    {
        ComputeBtn.IsEnabled = false;
        StepTitle.Text = "Step 2: Computing ICP Alignment...";
        StepInstructions.Text = "Running landmark registration followed by ICP refinement. Please wait...";

        try
        {
            // Build the matched landmark lists (only complete pairs)
            var srcLandmarks = new List<(double X, double Y, double Z)>();
            var tgtLandmarks = new List<(double X, double Y, double Z)>();
            int maxIdx = Math.Max(_ctLandmarks.Count, _stlLandmarks.Count);
            for (int i = 0; i < maxIdx; i++)
            {
                if (i < _ctLandmarks.Count && i < _stlLandmarks.Count &&
                    _ctLandmarks[i].HasValue && _stlLandmarks[i].HasValue)
                {
                    srcLandmarks.Add(_stlLandmarks[i]!.Value);
                    tgtLandmarks.Add(_ctLandmarks[i]!.Value);
                }
            }

            // Step 1: Compute initial transform from landmark pairs
            var initialTransform = IcpAligner.ComputeLandmarkTransform(srcLandmarks, tgtLandmarks);

            // Step 2: Run ICP refinement
            var result = await Task.Run(() =>
                IcpAligner.Align(_stlOriginalVertices, _ctVertices, initialTransform, maxIterations: 80, tolerance: 0.001,
                    progress: p => Dispatcher.Invoke(() => StepInstructions.Text = $"ICP iteration... {p * 100:F0}%")));

            FinalTransform = result.Transform;

            // Apply transform to the STL vertices for preview
            var previewVerts = _stlOriginalVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
            IcpAligner.TransformVertices(previewVerts, result.Transform);

            // Show overlay in the right viewport
            StlViewport.Children.Clear();
            StlViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(40, 40, 45)) });
            StlViewport.Children.Add(new ModelVisual3D { Content = MeshHelper.BuildModel3D(_ctVertices, 180, 200, 220) });
            StlViewport.Children.Add(new ModelVisual3D { Content = MeshHelper.BuildModel3D(previewVerts, 245, 200, 150) });
            StlViewport.ZoomExtents(500);

            RmsText.Text = $"RMS: {result.RmsError:F3} mm | {result.Iterations} iters";
            StepTitle.Text = "Step 3: Review Alignment";
            StepInstructions.Text = "Right viewport shows the overlay. If it looks good, click Accept. Otherwise clear landmarks and retry.";
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
