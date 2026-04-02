using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
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
    private readonly OrthoPlanner.Core.Imaging.VolumeData _ctVolume;
    private readonly List<float[]> _stlVertices;
    private readonly List<float[]> _stlOriginalVertices;

    private readonly List<(double X, double Y, double Z)?> _ctLandmarks = new();
    private readonly List<(double X, double Y, double Z)?> _stlLandmarks = new();
    private readonly List<Element3D> _ctMarkerVisuals = new();
    private readonly List<Element3D> _stlMarkerVisuals = new();

    private readonly ObservableCollection<LandmarkPairItem> _pairs = new();

    public bool Accepted { get; private set; }
    public double[,]? FinalTransform { get; private set; }
    public bool CleanMerged { get; private set; }
    public List<float[]>? CleanMergedVertices { get; private set; }
    private EventHandler? _renderingHandler;

    public DentalAlignmentWindow(OrthoPlanner.Core.Imaging.VolumeData ctVolume, List<float[]> ctVertices, List<float[]> stlVertices)
    {
        InitializeComponent();

        var ctEffManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        CtViewport.EffectsManager = ctEffManager;
        var stlEffManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        StlViewport.EffectsManager = stlEffManager;

        _ctVolume = ctVolume;
        _ctVertices = ctVertices;
        _stlVertices = stlVertices;
        _stlOriginalVertices = stlVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();

        PairsList.ItemsSource = _pairs;

        _renderingHandler = (s, _) =>
        {
            if (CtCamera != null && CtHeadlamp != null && CtBacklamp != null)
            {
                var dir = CtCamera.LookDirection;
                if (dir.Length > 0.001) 
                { 
                    dir.Normalize(); 
                    var tdFront = new Vector3D(-dir.X, -dir.Y, -dir.Z);
                    var tdBack = new Vector3D(dir.X, dir.Y, dir.Z);
                    if (Math.Abs(CtHeadlamp.Direction.X - tdFront.X) > 1e-4 || 
                        Math.Abs(CtHeadlamp.Direction.Y - tdFront.Y) > 1e-4 || 
                        Math.Abs(CtHeadlamp.Direction.Z - tdFront.Z) > 1e-4)
                    {
                        CtHeadlamp.Direction = tdFront; 
                        CtBacklamp.Direction = tdBack;
                    }
                }
            }
            if (StlCamera != null && StlHeadlamp != null && StlBacklamp != null)
            {
                var dir = StlCamera.LookDirection;
                if (dir.Length > 0.001) 
                { 
                    dir.Normalize(); 
                    var tdFront = new Vector3D(-dir.X, -dir.Y, -dir.Z);
                    var tdBack = new Vector3D(dir.X, dir.Y, dir.Z);
                    if (Math.Abs(StlHeadlamp.Direction.X - tdFront.X) > 1e-4 || 
                        Math.Abs(StlHeadlamp.Direction.Y - tdFront.Y) > 1e-4 || 
                        Math.Abs(StlHeadlamp.Direction.Z - tdFront.Z) > 1e-4)
                    {
                        StlHeadlamp.Direction = tdFront; 
                        StlBacklamp.Direction = tdBack;
                    }
                }
            }
        };
        System.Windows.Media.CompositionTarget.Rendering += _renderingHandler;

        Loaded += (_, _) => SetupViewports();
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_renderingHandler != null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= _renderingHandler;
            _renderingHandler = null;
        }
        CtGroup.Children.Clear();
        StlGroup.Children.Clear();
        if (CtViewport.EffectsManager is IDisposable d1) d1.Dispose();
        if (StlViewport.EffectsManager is IDisposable d2) d2.Dispose();
        CtViewport.EffectsManager = null;
        StlViewport.EffectsManager = null;
    }

    private void SetupViewports()
    {
        // CT Model
        var ctModel = MeshHelper.BuildModel3D(_ctVertices, 240, 230, 210);
        CtGroup.Children.Add(ctModel);

        // STL Model
        var stlModel = MeshHelper.BuildModel3D(_stlVertices, 245, 245, 230);
        StlGroup.Children.Add(stlModel);

        CenterViewportOnMesh(CtViewport, _ctVertices, 1.2);
        CenterViewportOnMesh(StlViewport, _stlVertices, 1.5);
    }

    private void CenterViewportOnMesh(HelixToolkit.Wpf.SharpDX.Viewport3DX viewport, List<float[]> vertices, double zoomMultiplier)
    {
        if (vertices == null || vertices.Count == 0 || viewport.Camera == null) return;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in vertices)
        {
            if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
            if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
            if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
        }

        var pivot = new Point3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        double diagonal = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY) + (maxZ - minZ) * (maxZ - minZ));
        double distance = Math.Max(diagonal * zoomMultiplier, 10);

        var dir = new Vector3D(0, 1, -0.3); // Model is viewed from anterior/superior
        dir.Normalize();
        
        var cam = viewport.Camera as HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
        if (cam != null)
        {
            cam.Position = new Point3D(pivot.X - dir.X * distance, pivot.Y - dir.Y * distance, pivot.Z - dir.Z * distance);
            cam.LookDirection = dir * distance;
            cam.UpDirection = new Vector3D(0, 0, 1);
        }

        viewport.FixedRotationPointEnabled = true;
        viewport.FixedRotationPoint = pivot;
    }

    // ═══ Left-Click Add ═══

    private void CtViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return; // allow shift/ctrl to pan/zoom if Helix uses it
        
        var pos = e.GetPosition(CtViewport);
        var hits = CtViewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        SetCtLandmark(GetNextCtIndex(), new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z));
        e.Handled = true; // Consume to prevent Helix picking up the left click for orbital rotation
    }

    private void StlViewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var pos = e.GetPosition(StlViewport);
        var hits = StlViewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        SetStlLandmark(GetNextStlIndex(), new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z));
        e.Handled = true;
    }

    // ═══ Right-Click Remove ═══

    private void CtViewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CtViewport);
        var hits = CtViewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        var clickPos = new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z);
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
        var hits = StlViewport.FindHits(pos);
        if (hits == null || hits.Count == 0) return;

        var clickPos = new Point3D(hits[0].PointHit.X, hits[0].PointHit.Y, hits[0].PointHit.Z);
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

        var (sphere, label) = CreateMarker(point, System.Windows.Media.Colors.LimeGreen, idx + 1);
        CtGroup.Children.Add(sphere);
        CtGroup.Children.Add(label);
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

        var (sphere, label) = CreateMarker(point, System.Windows.Media.Colors.OrangeRed, idx + 1);
        StlGroup.Children.Add(sphere);
        StlGroup.Children.Add(label);
        _stlMarkerVisuals[idx * 2] = sphere;
        _stlMarkerVisuals[idx * 2 + 1] = label;

        EnsurePairItem(idx);
        UpdatePairItem(idx);
        UpdateLandmarkUI();
    }

    private (MeshGeometryModel3D sphere, BillboardTextModel3D label) CreateMarker(Point3D position, System.Windows.Media.Color color, int number)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0,0,0), 1.5f);
        var matColor = new HelixToolkit.Maths.Color4(color.R/255f, color.G/255f, color.B/255f, color.A/255f);
        
        var sphere = new MeshGeometryModel3D { 
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()), 
            Material = new PhongMaterial { DiffuseColor = matColor, SpecularColor = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f), SpecularShininess = 32f }, 
            Transform = new TranslateTransform3D(position.X, position.Y, position.Z) 
        };

        var text3D = new HelixToolkit.SharpDX.BillboardText3D();
        text3D.TextInfo.Add(new HelixToolkit.SharpDX.TextInfo(number.ToString(), new System.Numerics.Vector3((float)position.X, (float)position.Y, (float)position.Z + 3f)));
        var label = new BillboardTextModel3D { Geometry = text3D };
        return (sphere, label);
    }

    private void RemoveCtMarker(int idx)
    {
        if (idx * 2 + 1 < _ctMarkerVisuals.Count)
        {
            if (_ctMarkerVisuals[idx * 2] != null) CtGroup.Children.Remove(_ctMarkerVisuals[idx * 2]);
            if (_ctMarkerVisuals[idx * 2 + 1] != null) CtGroup.Children.Remove(_ctMarkerVisuals[idx * 2 + 1]);
        }
    }

    private void RemoveStlMarker(int idx)
    {
        if (idx * 2 + 1 < _stlMarkerVisuals.Count)
        {
            if (_stlMarkerVisuals[idx * 2] != null) StlGroup.Children.Remove(_stlMarkerVisuals[idx * 2]);
            if (_stlMarkerVisuals[idx * 2 + 1] != null) StlGroup.Children.Remove(_stlMarkerVisuals[idx * 2 + 1]);
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
            if (_ctMarkerVisuals[i] != null) CtGroup.Children.Remove(_ctMarkerVisuals[i]);
        for (int i = 0; i < _stlMarkerVisuals.Count; i++)
            if (_stlMarkerVisuals[i] != null) StlGroup.Children.Remove(_stlMarkerVisuals[i]);

        _ctMarkerVisuals.Clear(); _stlMarkerVisuals.Clear();
        _ctLandmarks.Clear(); _stlLandmarks.Clear();
        _pairs.Clear();
        UpdateLandmarkUI();
        RmsText.Text = "";
        AcceptBtn.Visibility = Visibility.Collapsed;
        CleanMergeBtn.Visibility = Visibility.Collapsed;
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

            // --- Pre-ICP Validation: Ensure points are not clustered too closely together ---
            if (srcLandmarks.Count >= 3)
            {
                double srcMaxDist = GetMaxDistanceBetweenPoints(srcLandmarks);
                double tgtMaxDist = GetMaxDistanceBetweenPoints(tgtLandmarks);

                if (srcMaxDist < 15.0 || tgtMaxDist < 15.0)
                {
                    MessageBox.Show("The landmarks you selected are completely clustered in one area of the mouth. " +
                        "This will cause the initial gross alignment rotation to fail wildly.\n\n" +
                        "Please place landmarks that span across the dental arch (e.g., Left Molar, Right Molar, Incisors) " +
                        "to provide a stable 3D rotation basis.", "Unstable Landmarks Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    StepTitle.Text = "Step 1: Pick Matching Landmarks";
                    StepInstructions.Text = "Spread points across the dental arch before aligning.";
                    return;
                }
            }

            var initialTransform = IcpAligner.ComputeLandmarkTransform(srcLandmarks, tgtLandmarks);

            IcpAligner.AlignResult result;
            if (SkipIcpCheckBox.IsChecked == true)
            {
                result = new IcpAligner.AlignResult { Transform = initialTransform, RmsError = 0, Iterations = 0 };
                StepInstructions.Text = "Skipped ICP. Reviewing manual landmark alignment...";
            }
            else
            {
                // Trim out worst 30% of points (aiming for dense 70% overlap as requested)
                result = await Task.Run(() =>
                    IcpAligner.Align(_stlOriginalVertices, _ctVertices, initialTransform, maxIterations: 150, tolerance: 0.0005, trimRatio: 0.70,
                        progress: p => Dispatcher.Invoke(() => StepInstructions.Text = $"ICP iteration... {p * 100:F0}%")));
            }

            FinalTransform = result.Transform;

            var previewVerts = _stlOriginalVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
            IcpAligner.TransformVertices(previewVerts, result.Transform);

            // ──Vivid Visualization ── 
            StlGroup.Children.Clear();

            // Dark Blue translucent CT model (increased opacity to 180 per request)
            var ctModel = MeshHelper.BuildModel3D(_ctVertices, 80, 160, 255, 180);
            StlGroup.Children.Add(ctModel);

            // Bright Golden solid STL model (alpha defaults to 255)
            var alignedModel = MeshHelper.BuildModel3D(previewVerts, 255, 230, 90);
            StlGroup.Children.Add(alignedModel);

            CenterViewportOnMesh(StlViewport, _ctVertices, 1.0);

            RmsText.Text = $"RMS: {result.RmsError:F3} mm | {result.Iterations} iters";
            StepTitle.Text = "Step 3: Review Alignment";
            StepInstructions.Text = "Review the right viewport! (Blue = CT, Gold = Scan). Pan/Rotate to check accuracy. If good, click Accept.";
            AcceptBtn.Visibility = Visibility.Visible;
            CleanMergeBtn.Visibility = Visibility.Visible;
            CloseHolesCheckBox.Visibility = Visibility.Visible;
            SkipIcpCheckBox.Visibility = Visibility.Collapsed;


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

    private async void CleanMerge_Click(object sender, RoutedEventArgs e)
    {
        if (FinalTransform == null) return;
        
        StepTitle.Text = "Step 4: Cleaning and Merging...";
        StepInstructions.Text = "Executing complex boolean mesh operations. Please wait...";
        CleanMergeBtn.IsEnabled = false;
        AcceptBtn.IsEnabled = false;

        try
        {
            var previewVerts = _stlOriginalVertices.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
            IcpAligner.TransformVertices(previewVerts, FinalTransform);

            bool closeHoles = CloseHolesCheckBox.IsChecked == true;
            var mergedBone = await Task.Run(() => MeshOps.CleanAndMergeDentalCast(_ctVertices, previewVerts, closeHoles));

            CleanMerged = true;
            CleanMergedVertices = mergedBone;
            Accepted = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Clean & Merge failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StepTitle.Text = "Step 3: Review Alignment";
            StepInstructions.Text = "Clean & Merge failed. You can still Accept the standard alignment.";
            CleanMergeBtn.IsEnabled = true;
            AcceptBtn.IsEnabled = true;
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
        // If we are at checkout (Accept button is visible), 'Cancel' means "go back to picking points"
        if (AcceptBtn.Visibility == Visibility.Visible)
        {
            AcceptBtn.Visibility = Visibility.Collapsed;
            CleanMergeBtn.Visibility = Visibility.Collapsed;
            CloseHolesCheckBox.Visibility = Visibility.Collapsed;
            SkipIcpCheckBox.Visibility = Visibility.Visible;
            StlGroup.Children.Clear(); // Clear alignment preview
            StlGroup.Children.Add(MeshHelper.BuildModel3D(_stlOriginalVertices, 255, 230, 90)); // Restore original gold STL
            for (int i = 0; i < _stlMarkerVisuals.Count; i++)
                if (_stlMarkerVisuals[i] != null) StlGroup.Children.Add(_stlMarkerVisuals[i]);
            
            StepTitle.Text = "Step 1: Pick Matching Landmarks";
            StepInstructions.Text = "Adjust landmarks or right-click to remove. Then click Compute Alignment when ready.";
            return;
        }

        // If we are already at the picking screen, 'Cancel' closes the window.
        Accepted = false;
        DialogResult = false;
        Close();
    }

    private double GetMaxDistanceBetweenPoints(List<(double X, double Y, double Z)> points)
    {
        double maxDistSq = 0;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                var p1 = points[i];
                var p2 = points[j];
                double dx = p1.X - p2.X;
                double dy = p1.Y - p2.Y;
                double dz = p1.Z - p2.Z;
                double distSq = dx * dx + dy * dy + dz * dz;
                if (distSq > maxDistSq) maxDistSq = distSq;
            }
        }
        return Math.Sqrt(maxDistSq);
    }
}




