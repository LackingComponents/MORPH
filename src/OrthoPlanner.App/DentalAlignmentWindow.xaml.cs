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
    private readonly List<float[]> _ctVertices;
    private readonly List<float[]> _stlVertices;
    private readonly List<float[]> _stlOriginalVertices;

    private readonly List<(double X, double Y, double Z)?> _ctLandmarks = new();
    private readonly List<(double X, double Y, double Z)?> _stlLandmarks = new();
    private readonly List<Visual3D> _ctMarkerVisuals = new();
    private readonly List<Visual3D> _stlMarkerVisuals = new();

    private readonly ObservableCollection<LandmarkPairItem> _pairs = new();

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
        // CT Model
        var ctModel = MeshHelper.BuildModel3D(_ctVertices, 240, 230, 210);
        CtViewport.Children.Add(new ModelVisual3D { Content = ctModel });
        AddStandardLighting(CtViewport);

        // STL Model
        var stlModel = MeshHelper.BuildModel3D(_stlVertices, 245, 245, 230);
        StlViewport.Children.Add(new ModelVisual3D { Content = stlModel });
        AddStandardLighting(StlViewport);

        CtViewport.ZoomExtents(500);
        StlViewport.ZoomExtents(500);
    }

    private void AddStandardLighting(HelixViewport3D viewport)
    {
        // Mimic MainViewModel lighting exactly: 
        // Very low ambient light to barely prevent pitch black shadows, letting Headlamp do the work
        viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(30, 30, 35)) });
        // (The HelixViewport3D has IsHeadLightEnabled="True" in XAML, exactly like the Main viewport)
    }

    // ═══ Left-Click Add ═══

    private void CtViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return; // allow shift/ctrl to pan/zoom if Helix uses it
        
        var pos = e.GetPosition(CtViewport);
        var hits = Viewport3DHelper.FindHits(CtViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        SetCtLandmark(GetNextCtIndex(), hits[0].Position);
        e.Handled = true; // Consume to prevent Helix picking up the left click for orbital rotation
    }

    private void StlViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var pos = e.GetPosition(StlViewport);
        var hits = Viewport3DHelper.FindHits(StlViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        SetStlLandmark(GetNextStlIndex(), hits[0].Position);
        e.Handled = true;
    }

    // ═══ Right-Click Remove ═══

    private void CtViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CtViewport);
        var hits = Viewport3DHelper.FindHits(CtViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        var clickPos = hits[0].Position;
        int closestIdx = FindClosestLandmark(_ctLandmarks, clickPos);

        if (closestIdx >= 0)
        {
            RemoveCtMarker(closestIdx);
            _ctLandmarks[closestIdx] = null;
            UpdatePairItem(closestIdx);
            UpdateLandmarkUI();
            e.Handled = true; // Prevent context menu
        }
    }

    private void StlViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(StlViewport);
        var hits = Viewport3DHelper.FindHits(StlViewport.Viewport, pos);
        if (hits == null || hits.Count == 0) return;

        var clickPos = hits[0].Position;
        int closestIdx = FindClosestLandmark(_stlLandmarks, clickPos);

        if (closestIdx >= 0)
        {
            RemoveStlMarker(closestIdx);
            _stlLandmarks[closestIdx] = null;
            UpdatePairItem(closestIdx);
            UpdateLandmarkUI();
            e.Handled = true;
        }
    }

    private int FindClosestLandmark(List<(double X, double Y, double Z)?> landmarks, Point3D point, double maxRadius = 5.0)
    {
        int bestIdx = -1;
        double bestDistSq = maxRadius * maxRadius;

        for (int i = 0; i < landmarks.Count; i++)
        {
            if (landmarks[i] == null) continue;
            var l = landmarks[i]!.Value;
            double dx = l.X - point.X, dy = l.Y - point.Y, dz = l.Z - point.Z;
            double distSq = dx * dx + dy * dy + dz * dz;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    // ═══ Marker Management ═══

    private int GetNextCtIndex()
    {
        for (int i = 0; i < _ctLandmarks.Count; i++)
            if (_ctLandmarks[i] == null) return i;
        return _ctLandmarks.Count;
    }

    private int GetNextStlIndex()
    {
        for (int i = 0; i < _stlLandmarks.Count; i++)
            if (_stlLandmarks[i] == null) return i;
        return _stlLandmarks.Count;
    }

    private void SetCtLandmark(int idx, Point3D point)
    {
        while (_ctLandmarks.Count <= idx) _ctLandmarks.Add(null);
        while (_stlLandmarks.Count <= idx) _stlLandmarks.Add(null);
        while (_ctMarkerVisuals.Count <= idx * 2 + 1)
        {
            _ctMarkerVisuals.Add(null!);
            _ctMarkerVisuals.Add(null!);
        }

        RemoveCtMarker(idx);
        _ctLandmarks[idx] = (point.X, point.Y, point.Z);

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
        var sphere = new SphereVisual3D { Center = position, Radius = 1.5, Fill = new SolidColorBrush(color) };
        var label = new BillboardTextVisual3D { Text = number.ToString(), Position = new Point3D(position.X, position.Y, position.Z + 3), Foreground = new SolidColorBrush(color), FontSize = 14 };
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

    // ═══ Pairs List ═══

    private void EnsurePairItem(int idx)
    {
        while (_pairs.Count <= idx) _pairs.Add(new LandmarkPairItem { Index = _pairs.Count });
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

    private void UpdateLandmarkUI()
    {
        int ctCount = _ctLandmarks.Count(l => l.HasValue);
        int stlCount = _stlLandmarks.Count(l => l.HasValue);
        int pairs = 0;
        int maxIdx = Math.Max(_ctLandmarks.Count, _stlLandmarks.Count);
        for (int i = 0; i < maxIdx; i++)
            if (i < _ctLandmarks.Count && _ctLandmarks[i].HasValue && i < _stlLandmarks.Count && _stlLandmarks[i].HasValue)
                pairs++;

        LandmarkCountText.Text = $"CT: {ctCount} | STL: {stlCount} | Complete pairs: {pairs}";
        ComputeBtn.IsEnabled = pairs >= 3;
    }

    private void ClearLandmarks_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < _ctMarkerVisuals.Count; i++)
            if (_ctMarkerVisuals[i] != null) CtViewport.Children.Remove(_ctMarkerVisuals[i]);
        for (int i = 0; i < _stlMarkerVisuals.Count; i++)
            if (_stlMarkerVisuals[i] != null) StlViewport.Children.Remove(_stlMarkerVisuals[i]);

        _ctMarkerVisuals.Clear(); _stlMarkerVisuals.Clear();
        _ctLandmarks.Clear(); _stlLandmarks.Clear();
        _pairs.Clear();
        UpdateLandmarkUI();
        RmsText.Text = "";
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    // ═══ ICP Compute & Vivid Overlay ═══

    private async void ComputeAlignment_Click(object sender, RoutedEventArgs e)
    {
        ComputeBtn.IsEnabled = false;
        StepTitle.Text = "Step 2: Computing ICP Alignment...";
        StepInstructions.Text = "Running landmark registration + trimmed ICP refinement. Please wait...";

        try
        {
            var srcLandmarks = new List<(double, double, double)>();
            var tgtLandmarks = new List<(double, double, double)>();
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

            var initialTransform = IcpAligner.ComputeLandmarkTransform(srcLandmarks, tgtLandmarks);

            // Trim out worst 75% of points to ensure convergence over only matching teeth
            var result = await Task.Run(() =>
                IcpAligner.Align(_stlOriginalVertices, _ctVertices, initialTransform, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.25,
                    progress: p => Dispatcher.Invoke(() => StepInstructions.Text = $"ICP iteration... {p * 100:F0}%")));

            FinalTransform = result.Transform;

            var previewVerts = _stlOriginalVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
            IcpAligner.TransformVertices(previewVerts, result.Transform);

            // ──Vivid Visualization ── 
            StlViewport.Children.Clear();
            AddStandardLighting(StlViewport);

            // Dark Blue translucent CT model using new alpha parameter (140 alpha)
            var ctModel = MeshHelper.BuildModel3D(_ctVertices, 80, 160, 255, 140);
            StlViewport.Children.Add(new ModelVisual3D { Content = ctModel });

            // Bright Golden solid STL model (alpha defaults to 255)
            var alignedModel = MeshHelper.BuildModel3D(previewVerts, 255, 230, 90);
            StlViewport.Children.Add(new ModelVisual3D { Content = alignedModel });

            StlViewport.ZoomExtents(500);

            RmsText.Text = $"RMS: {result.RmsError:F3} mm | {result.Iterations} iters";
            StepTitle.Text = "Step 3: Review Alignment";
            StepInstructions.Text = "Review the right viewport! (Blue = CT, Gold = Scan). Pan/Rotate to check accuracy. If good, click Accept.";
            AcceptBtn.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Alignment failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StepTitle.Text = "Step 1: Pick Matching Landmarks";
            StepInstructions.Text = "Alignment failed. Use right-click to fix bad landmarks and retry.";
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
