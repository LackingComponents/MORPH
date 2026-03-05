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
    private readonly double _boneMinHu;

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
    private float[] _leftHalfExtents = { 15f, 10f, 10f };
    private float[] _rightHalfExtents = { 15f, 10f, 10f };
    private ModelVisual3D? _leftBoxVisual;
    private ModelVisual3D? _rightBoxVisual;

    // Drag state
    private int _dragPointIndex = -1;       // Step 1: dragging plane points
    private bool _isDragging;               // Step 2: dragging condyle box
    private bool _draggingLeft;
    private int _dragCornerIdx = -1;        // Step 2: resizing via corner (-1 = none, 0 = left, 1 = right)
    private int _dragFaceAxis = -1;         // Step 2: move perpendicular to face (0=x, 1=y, 2=z, -1=none)
    private Point3D _dragStartPoint;
    private float[] _dragStartCenter = { 0, 0, 0 };
    private float[] _dragStartExtents = { 0, 0, 0 };

    private float[]? _leftCondyleClickPoint;
    private float[]? _rightCondyleClickPoint;

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
        VolumeData? ctVolume = null, SegmentationVolume? segVolume = null, byte boneLabel = 1, double boneMinHu = 400.0)
    {
        InitializeComponent();
        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _upperCastVerts = upperCastVerts;
        _lowerCastVerts = lowerCastVerts;
        _ctVolume = ctVolume;
        _segVolume = segVolume;
        _boneLabel = boneLabel;
        _boneMinHu = boneMinHu;
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
        _leftCondyleClickPoint = null;
        _rightCondyleClickPoint = null;
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
    /// Show the plane as a bounded rectangular region that aligns with the 3 points
    /// </summary>
    private void ShowPlaneTriangle()
    {
        if (_planeTriangleVisual != null) MainViewport.Children.Remove(_planeTriangleVisual);
        if (_planePoints.Count < 3) return;

        // Create local U, V vectors for the plane
        var u = (_planePoints[2] - _planePoints[1]); u.Normalize();
        var v = Vector3D.CrossProduct(_planeNormal, u); v.Normalize();

        // Project the 3 points onto U, V to find the bounding box in local plane coordinates
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;

        foreach (var pt in _planePoints)
        {
            var pVec = pt - _planeCentroid;
            double pu = Vector3D.DotProduct(pVec, u);
            double pv = Vector3D.DotProduct(pVec, v);
            minU = Math.Min(minU, pu); maxU = Math.Max(maxU, pu);
            minV = Math.Min(minV, pv); maxV = Math.Max(maxV, pv);
        }

        // Add some padding
        double pad = 15.0;
        minU -= pad; maxU += pad;
        minV -= pad; maxV += pad;

        // Compute the 4 corners of the local bounding box
        var c0 = _planeCentroid + u * minU + v * minV; // Bottom-Left
        var c1 = _planeCentroid + u * maxU + v * minV; // Bottom-Right
        var c2 = _planeCentroid + u * maxU + v * maxV; // Top-Right
        var c3 = _planeCentroid + u * minU + v * maxV; // Top-Left

        var mesh = new MeshGeometry3D();
        mesh.Positions = new Point3DCollection(new[] { c0, c1, c2, c3 });
        mesh.TriangleIndices = new Int32Collection(new[] { 0, 1, 2, 0, 2, 3 }); // double-sided (with BackMaterial)
        mesh.Freeze();

        var brush = new SolidColorBrush(Color.FromArgb(60, 0, 255, 100)); brush.Freeze();
        var mat = new DiffuseMaterial(brush); mat.Freeze();
        var model = new GeometryModel3D(mesh, mat) { BackMaterial = mat }; model.Freeze();

        // Border
        var border = new LinesVisual3D { Color = Colors.Cyan, Thickness = 2 };
        border.Points.Add(c0); border.Points.Add(c1);
        border.Points.Add(c1); border.Points.Add(c2);
        border.Points.Add(c2); border.Points.Add(c3);
        border.Points.Add(c3); border.Points.Add(c0);

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

        if (_segVolume == null || _ctVolume == null)
        {
            MessageBox.Show("Cannot perform voxel split without CT volume and segmentation mask.",
                "Missing Data", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var segVol = _segVolume;
            var ctVol = _ctVolume;
            var boneLabel = _boneLabel;

            var (cranium, mandible) = await Task.Run(() =>
                SplitVoxelMask(normal, planeD, leftC, rightC,
                    _leftCondyleClickPoint ?? leftC, _rightCondyleClickPoint ?? rightC,
                    leftHE, rightHE, segVol, ctVol, boneLabel));

            _craniumVerts = cranium;
            _mandibleVerts = mandible;

            Dispatcher.Invoke(() =>
            {
                MainViewport.Children.Clear();
                AddLighting();

                if (_craniumVerts != null && _craniumVerts.Count > 0)
                {
                    var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
                    MainViewport.Children.Add(new ModelVisual3D { Content = cranModel });
                }
                if (_mandibleVerts != null && _mandibleVerts.Count > 0)
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
                StatusText.Text = $"Cranium: {(_craniumVerts?.Count ?? 0) / 3} tris | Mandible: {(_mandibleVerts?.Count ?? 0) / 3} tris";
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
    /// Voxel-based separation logic on the CT mask
    /// </summary>
    private (List<float[]> cranium, List<float[]> mandible) SplitVoxelMask(
        Vector3D planeNormal, double planeD,
        float[] leftC, float[] rightC, float[] leftAnchor, float[] rightAnchor, float[] leftHE, float[] rightHE,
        SegmentationVolume segVol, VolumeData ctVol, byte boneLabel)
    {
        byte cranLabel = 200; // Cranium top inside box
        byte mandLabel = 201; // Condyle bottom inside box
        byte mandBodyLabel = 202; // Mandible Body below plane
        byte unassignedAboveLabel = 203; // Bone above plane outside boxes (ramus maxilla)

        int w = ctVol.Width, h = ctVol.Height, d = ctVol.Depth;
        double sx = ctVol.Spacing[0], sy = ctVol.Spacing[1], sz = ctVol.Spacing[2];

        Dispatcher.Invoke(() => StatusText.Text = "Generating pristine bone mask...");

        short rawThreshold = (short)Math.Max(100, _boneMinHu);

        // 1. Recreate Pristine Mask
        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (segVol.Labels[idx] != boneLabel) continue;
            
            // Drop software-bridged soft tissue voxels that fall below the physical bone HU threshold
            if (ctVol.Voxels[idx] < rawThreshold)
            {
                segVol.Labels[idx] = 0;
                continue;
            }

            float vx = (float)(x * sx);
            float vy = (float)(y * sy);
            float vz = (float)(z * sz);

            double dist = planeNormal.X * vx + planeNormal.Y * vy + planeNormal.Z * vz + planeD;
            if (dist < 0) // Below plane
                segVol.Labels[idx] = mandBodyLabel;
            else
                segVol.Labels[idx] = unassignedAboveLabel;
        }

        Dispatcher.Invoke(() => StatusText.Text = "Growing Condyles to Plane cut...");

        var queue = new Queue<int>();

        // 2. Seed Queue EXACTLY at anchors
        Action<float[]> seedAnchor = (float[] anchor) =>
        {
            int cx = Math.Clamp((int)Math.Round(anchor[0] / sx), 0, w - 1);
            int cy = Math.Clamp((int)Math.Round(anchor[1] / sy), 0, h - 1);
            int cz = Math.Clamp((int)Math.Round(anchor[2] / sz), 0, d - 1);
            int idx = cx + cy * w + cz * w * h;

            if (segVol.Labels[idx] != unassignedAboveLabel && segVol.Labels[idx] != mandBodyLabel)
            {
                double minDist = double.MaxValue;
                for(int zoff=-5; zoff<=5; zoff++)
                for(int yoff=-5; yoff<=5; yoff++)
                for(int xoff=-5; xoff<=5; xoff++)
                {
                    int nx = cx+xoff, ny = cy+yoff, nz = cz+zoff;
                    if (nx>=0 && nx<w && ny>=0 && ny<h && nz>=0 && nz<d)
                    {
                        int nIdx = nx + ny * w + nz * w * h;
                        if (segVol.Labels[nIdx] == unassignedAboveLabel || segVol.Labels[nIdx] == mandBodyLabel)
                        {
                            double distSqr = xoff*xoff*sx*sx + yoff*yoff*sy*sy + zoff*zoff*sz*sz;
                            if (distSqr < minDist) { minDist = distSqr; idx = nIdx; }
                        }
                    }
                }
            }
            
            if (segVol.Labels[idx] == unassignedAboveLabel || segVol.Labels[idx] == mandBodyLabel)
            {
                segVol.Labels[idx] = mandLabel;
                queue.Enqueue(idx);
            }
        };

        seedAnchor(leftAnchor);
        seedAnchor(rightAnchor);

        // 3. Flood Fill Downward
        int[][] n6dirs = { new[]{1,0,0}, new[]{-1,0,0}, new[]{0,1,0}, new[]{0,-1,0}, new[]{0,0,1}, new[]{0,0,-1} };
        
        while (queue.Count > 0)
        {
            int curr = queue.Dequeue();
            int cz = curr / (w * h), crem = curr % (w * h), cy = crem / w, cx = crem % w;

            foreach (var n in n6dirs)
            {
                int nx = cx + n[0], ny = cy + n[1], nz = cz + n[2];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d)
                {
                    int nIdx = nx + ny * w + nz * w * h;
                    
                    if (segVol.Labels[nIdx] == unassignedAboveLabel)
                    {
                        // Bridge Rejection (>= 10 bone voxels in 3x3x3)
                        int boneNeighbors = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int tx = nx + dx, ty = ny + dy, tz = nz + dz;
                            if (tx >= 0 && tx < w && ty >= 0 && ty < h && tz >= 0 && tz < d)
                            {
                                byte l = segVol.Labels[tx + ty * w + tz * w * h];
                                if (l != 0) boneNeighbors++;
                            }
                        }

                        if (boneNeighbors >= 10)
                        {
                            segVol.Labels[nIdx] = mandLabel;
                            queue.Enqueue(nIdx);
                        }
                    }
                }
            }
        }

        Dispatcher.Invoke(() => StatusText.Text = "Finalizing Components...");

        byte finalMandibleLabel = 205;
        // Group the grown condyles and the body below the plane together
        for (int i = 0; i < segVol.Labels.Length; i++)
        {
            if (segVol.Labels[i] == mandLabel || segVol.Labels[i] == mandBodyLabel)
                segVol.Labels[i] = finalMandibleLabel;
        }

        // Keep largest component of Mandible (removes spine/noise that fell below plane)
        SegmentationEngine.KeepLargestComponent(segVol, finalMandibleLabel);

        // Everything else that was originally bone becomes Cranium
        for (int i = 0; i < segVol.Labels.Length; i++)
        {
            if (segVol.Labels[i] == finalMandibleLabel) continue;
            if (segVol.Labels[i] == 0) continue; 
            
            if (segVol.Labels[i] == unassignedAboveLabel || segVol.Labels[i] == cranLabel)
            {
                segVol.Labels[i] = cranLabel;
            }
        }

        Dispatcher.Invoke(() => StatusText.Text = "Extracting meshes...");

        var craniumMesh = SegmentationEngine.ExtractSegmentMesh(ctVol, segVol, cranLabel, 1);
        var mandibleMesh = SegmentationEngine.ExtractSegmentMesh(ctVol, segVol, finalMandibleLabel, 1);

        for (int i = 0; i < segVol.Labels.Length; i++)
        {
            if (segVol.Labels[i] == cranLabel || segVol.Labels[i] == finalMandibleLabel)
                segVol.Labels[i] = boneLabel; 
        }

        return (craniumMesh, mandibleMesh);
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
            // Check which box corner/face was clicked
            if (_leftCondyleCenter != null)
            {
                if (CheckBoxHit(hit.Value, _leftCondyleCenter, _leftHalfExtents, ref _dragCornerIdx, ref _dragFaceAxis))
                {
                    _isDragging = true; _draggingLeft = true;
                    _dragCornerIdx = _dragCornerIdx != -1 ? 0 : -1; // 0 = left
                    _dragStartPoint = hit.Value;
                    Array.Copy(_leftCondyleCenter, _dragStartCenter, 3);
                    Array.Copy(_leftHalfExtents, _dragStartExtents, 3);
                    e.Handled = true; return;
                }
            }
            if (_rightCondyleCenter != null)
            {
                if (CheckBoxHit(hit.Value, _rightCondyleCenter, _rightHalfExtents, ref _dragCornerIdx, ref _dragFaceAxis))
                {
                    _isDragging = true; _draggingLeft = false;
                    _dragCornerIdx = _dragCornerIdx != -1 ? 1 : -1; // 1 = right
                    _dragStartPoint = hit.Value;
                    Array.Copy(_rightCondyleCenter, _dragStartCenter, 3);
                    Array.Copy(_rightHalfExtents, _dragStartExtents, 3);
                    e.Handled = true; return;
                }
            }

            if (_rightCondyleCenter == null)
            {
                float midlineX = (float)(_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3f;
                float startX = (float)hit.Value.X;
                startX += (startX > midlineX) ? -10f : 10f; // Shift medial

                _rightCondyleCenter = new[] { startX, (float)hit.Value.Y, (float)hit.Value.Z };
                _rightCondyleClickPoint = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Right condyle placed. Now click LEFT condyle.";
            }
            else if (_leftCondyleCenter == null)
            {
                float midlineX = (float)(_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3f;
                float startX = (float)hit.Value.X;
                startX += (startX > midlineX) ? -10f : 10f; // Shift medial

                _leftCondyleCenter = new[] { startX, (float)hit.Value.Y, (float)hit.Value.Z };
                _leftCondyleClickPoint = new[] { (float)hit.Value.X, (float)hit.Value.Y, (float)hit.Value.Z };
                StatusText.Text = "Both placed. Drag corners to resize, drag face to move along normal. Then 'Split'.";
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
        if (hit == null && _dragFaceAxis == -1) return; // Need hit point unless we use ray-plane intersection for face drag

        if (_currentStep == 1 && _dragPointIndex >= 0 && _dragPointIndex < _planePoints.Count && hit != null)
        {
            // Drag point along the plane
            if (_planePoints.Count == 3)
            {
                var pt = hit.Value;
                _planePoints[_dragPointIndex] = new Point3D(pt.X, pt.Y, _planePoints[_dragPointIndex].Z);
                ShowPlaneTriangle();
            }
        }

        if (_currentStep == 2 && _isDragging)
        {
            var center = _draggingLeft ? _leftCondyleCenter : _rightCondyleCenter;
            var extents = _draggingLeft ? _leftHalfExtents : _rightHalfExtents;

            if (center != null && extents != null)
            {
                if (_dragCornerIdx != -1 && hit != null)
                {
                    // Resizing via corner: change the extents based on distance from center
                    extents[0] = Math.Max(2f, Math.Abs((float)hit.Value.X - center[0]));
                    extents[1] = Math.Max(2f, Math.Abs((float)hit.Value.Y - center[1]));
                    extents[2] = Math.Max(2f, Math.Abs((float)hit.Value.Z - center[2]));
                    StatusText.Text = $"Resizing ({(_draggingLeft ? "L" : "R")} box): {extents[0] * 2:F1} x {extents[1] * 2:F1} x {extents[2] * 2:F1} mm";
                }
                else if (_dragFaceAxis != -1)
                {
                    // Moving along face normal using standard ray projection to camera-parallel plane
                    var pt2Ray = Viewport3DHelper.Point2DtoRay3D(MainViewport.Viewport, pos);
                    var cam = MainViewport.Viewport.Camera as PerspectiveCamera;

                    if (pt2Ray != null && cam != null)
                    {
                        var normal = cam.LookDirection; normal.Normalize();
                        var planeP = new Point3D(_dragStartCenter[0], _dragStartCenter[1], _dragStartCenter[2]);
                        
                        double denom = Vector3D.DotProduct(normal, pt2Ray.Direction);
                        if (Math.Abs(denom) > 1e-6)
                        {
                            double t = Vector3D.DotProduct(normal, planeP - pt2Ray.Origin) / denom;
                            var planeHit = pt2Ray.Origin + pt2Ray.Direction * t;

                            float delta = 0;
                            if (_dragFaceAxis == 0) delta = (float)(planeHit.X - _dragStartPoint.X);
                            else if (_dragFaceAxis == 1) delta = (float)(planeHit.Y - _dragStartPoint.Y);
                            else if (_dragFaceAxis == 2) delta = (float)(planeHit.Z - _dragStartPoint.Z);

                            center[0] = _dragStartCenter[0] + (_dragFaceAxis == 0 ? delta : 0);
                            center[1] = _dragStartCenter[1] + (_dragFaceAxis == 1 ? delta : 0);
                            center[2] = _dragStartCenter[2] + (_dragFaceAxis == 2 ? delta : 0);

                            StatusText.Text = $"Moving {(_dragFaceAxis == 0 ? "X" : _dragFaceAxis == 1 ? "Y" : "Z")} ({(_draggingLeft ? "L" : "R")} box): ({center[0]:F1}, {center[1]:F1}, {center[2]:F1})";
                        }
                    }
                }

                RebuildBoxVisuals();
            }
        }
    }

    private bool CheckBoxHit(Point3D hit, float[] c, float[] he, ref int cornerIdx, ref int faceAxis)
    {
        // 1. Check if clicking the lateral corner sphere 
        double midlineX = _planePoints.Count == 3 ? (_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3.0 : 0;
        float signX = c[0] > midlineX ? 1f : -1f; // Lateral side
        Point3D corner = new Point3D(c[0] + he[0] * signX, c[1] + he[1], c[2] + he[2]);
        if ((hit - corner).Length < 5.0)
        {
            cornerIdx = 1; faceAxis = -1;
            return true;
        }

        // 2. Check if clicking any face (must be within box with a tight tolerance to one axis)
        if (IsInBox((float)hit.X, (float)hit.Y, (float)hit.Z, c, new float[] { he[0] + 1, he[1] + 1, he[2] + 1 }))
        {
            // Find which face is closest to the hit point
            float dx = Math.Min(Math.Abs((float)hit.X - (c[0] - he[0])), Math.Abs((float)hit.X - (c[0] + he[0])));
            float dy = Math.Min(Math.Abs((float)hit.Y - (c[1] - he[1])), Math.Abs((float)hit.Y - (c[1] + he[1])));
            float dz = Math.Min(Math.Abs((float)hit.Z - (c[2] - he[2])), Math.Abs((float)hit.Z - (c[2] + he[2])));

            if (dx < dy && dx < dz) faceAxis = 0; // X axis
            else if (dy < dx && dy < dz) faceAxis = 1; // Y axis
            else faceAxis = 2; // Z axis

            cornerIdx = -1;
            return true;
        }

        return false;
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragPointIndex = -1; _isDragging = false; _dragCornerIdx = -1; _dragFaceAxis = -1;
    }

    // Use right-click to reset extents to 15,10,10
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

        he[0] = 15f; he[1] = 10f; he[2] = 10f;

        if (side == "Left") _leftHalfExtents = he;
        else _rightHalfExtents = he;

        RebuildBoxVisuals();
        StatusText.Text = $"{side} box reset to 30x20x20mm.";
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

        // Add corner handle on the lateral side
        double midlineX = _planePoints.Count == 3 ? (_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3.0 : 0;
        double signX = cx > midlineX ? 1.0 : -1.0; // Lateral side
        var cornerSphere = new SphereVisual3D
        { 
            Center = new Point3D(cx + hx * signX, cy + hy, cz + hz), 
            Radius = 3, 
            Fill = new SolidColorBrush(Colors.Yellow) 
        };
        parent.Children.Add(cornerSphere);

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
