using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.Core.Geometry;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Segmentation;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class CondyleSplitWindow : Window
{
    // Input
    private readonly List<float[]> _boneVerts;
    private readonly VolumeData? _ctVolume;
    private readonly SegmentationVolume? _segVolume;
    private readonly byte _boneLabel;
    private readonly double _boneMinHu;

    // 3 user-picked points defining the split plane
    private readonly List<Point3D> _planePoints = new(); // Used to be list of 3
    private readonly Point3D?[] _splitPoints = new Point3D?[5];
    private readonly MeshGeometryModel3D?[] _splitMarkers = new MeshGeometryModel3D?[5];

    // Plane: Ax + By + Cz + D = 0
    private Vector3D _planeNormal;
    private double _planeD;
    private Point3D _planeCentroid;
    private GroupModel3D? _planeTriangleVisual;

    // Condylar bounding boxes: smaller default, adjustable
    private float[]? _leftCondyleCenter;
    private float[]? _rightCondyleCenter;
    private float[] _leftHalfExtents = { 15f, 10f, 10f };
    private float[] _rightHalfExtents = { 15f, 10f, 10f };
    private GroupModel3D? _leftBoxVisual;
    private GroupModel3D? _rightBoxVisual;
    private LineGeometryModel3D? _condylarAxisVisual;

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

    // Track the order of point placement: RN=0, RP=1, IN=2, LP=3, LN=4
    private readonly string[] _pointNames = { 
        "Right Condyle", "Right posterior teeth", "Interincisal", 
        "Left posterior teeth", "Left Condyle" 
    };

    // Public results
    public bool Accepted { get; private set; }
    public List<float[]>? CraniumResult { get; private set; }
    public List<float[]>? MandibleResult { get; private set; }
    public (double X, double Y, double Z)? LeftCondyleCenter { get; private set; }
    public (double X, double Y, double Z)? RightCondyleCenter { get; private set; }

    private void CenterViewportOnBone(System.Windows.Media.Media3D.Vector3D? lookDir = null)
    {
        if (_boneVerts == null || _boneVerts.Count == 0 || MainViewport.Camera == null) return;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in _boneVerts)
        {
            if (v[0] < minX) minX = v[0]; if (v[0] > maxX) maxX = v[0];
            if (v[1] < minY) minY = v[1]; if (v[1] > maxY) maxY = v[1];
            if (v[2] < minZ) minZ = v[2]; if (v[2] > maxZ) maxZ = v[2];
        }

        var pivot = new System.Windows.Media.Media3D.Point3D(
            (minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        double diagonal = Math.Sqrt(
            (maxX - minX) * (maxX - minX) +
            (maxY - minY) * (maxY - minY) +
            (maxZ - minZ) * (maxZ - minZ));
        double distance = Math.Max(diagonal * 0.9, 10);

        // Default to Anterior if no lookDir specified
        var dir = lookDir ?? new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        dir.Normalize();

        MainViewport.Camera.Position = new System.Windows.Media.Media3D.Point3D(
            pivot.X - dir.X * distance, pivot.Y - dir.Y * distance, pivot.Z - dir.Z * distance);
        MainViewport.Camera.LookDirection = dir * distance;
        MainViewport.Camera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);

        MainViewport.FixedRotationPointEnabled = true;
        MainViewport.FixedRotationPoint = pivot;
    }

    public CondyleSplitWindow(
        List<float[]> boneVerts,
        VolumeData? ctVolume = null, SegmentationVolume? segVolume = null, byte boneLabel = 1, double boneMinHu = 400.0)
    {
        InitializeComponent();
        
        MainViewport.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();

        // Track camera for coaxial headlamp — CompositionTarget.Rendering fires every frame
        System.Windows.Media.CompositionTarget.Rendering += (s, _) =>
        {
            var dir = SubCamera.LookDirection;
            if (dir.Length > 0.001) { dir.Normalize(); Headlamp.Direction = new System.Windows.Media.Media3D.Vector3D(-dir.X, -dir.Y, -dir.Z); Backlamp.Direction = new System.Windows.Media.Media3D.Vector3D(dir.X, dir.Y, dir.Z); }
        };

        _boneVerts = boneVerts.Select(v => new float[] { v[0], v[1], v[2] }).ToList();
        _ctVolume = ctVolume;
        _segVolume = segVolume;
        _boneLabel = boneLabel;
        _boneMinHu = boneMinHu;
        Loaded += (_, _) => SetupStep1();
    }

    // ═══════════════════════════════════
    // STEP 1: Pick 5 points
    // ═══════════════════════════════════
    private void SetupStep1()
    {
        _currentStep = 1;
        
        for (int i = 0; i < 5; i++)
        {
            _splitPoints[i] = null;
            if (_splitMarkers[i] != null) MainGroup.Children.Remove(_splitMarkers[i]);
            _splitMarkers[i] = null;
        }

        _planePoints.Clear();
        if (_planeTriangleVisual != null) MainGroup.Children.Remove(_planeTriangleVisual);
        
        _leftCondyleCenter = null;
        _rightCondyleCenter = null;
        _leftCondyleClickPoint = null;
        _rightCondyleClickPoint = null;
        _dragPointIndex = -1;

        MainGroup.Children.Clear();

        // Build bone model with 255 alpha (fully opaque) and exact MainViewport default color 245,245,230
        var boneModel = MeshHelper.BuildModel3D(_boneVerts, 245, 245, 230, 255);
        MainGroup.Children.Add(boneModel);

        // Start: look from right profile (Vector3D(1, 0, 0))
        CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(1, 0, 0));

        StepTitle.Text = "Step 1: Define Plane & Condyles";
        UpdateStep1Instructions();
        
        SplitBtn.Visibility = Visibility.Visible;
        SplitBtn.Content = "Next: Review";
        SplitBtn.IsEnabled = false; // Need 5 points
        ConfirmBtn.Visibility = Visibility.Collapsed;
        AcceptBtn.Visibility = Visibility.Collapsed;
    }

    private int GetNextEmptySlot()
    {
        for (int i = 0; i < 5; i++)
            if (_splitPoints[i] == null) return i;
        return -1;
    }

    private void UpdateStep1Instructions()
    {
        int total = 0;
        for (int i = 0; i < 5; i++) if (_splitPoints[i] != null) total++;

        int nextSlot = GetNextEmptySlot();
        if (nextSlot != -1)
        {
            StatusText.Text = $"Placed {total}/5. Please click: {_pointNames[nextSlot]}";
            StepInstructions.Text = "Click sequence: (1) Right Condyle, (2) Right Posterior, (3) Interincisal, (4) Left Posterior, (5) Left Condyle. Right-click points to delete.";
            SplitBtn.IsEnabled = false;
        }
        else
        {
            StatusText.Text = "All 5 points placed.";
            StepInstructions.Text = "Click 'Next: Review' to view and adjust bounding boxes and the separation plane.";
            SplitBtn.IsEnabled = true;
        }
    }

    private void ComputePlane()
    {
        // 1=RP, 2=IN, 3=LP
        if (_splitPoints[1] == null || _splitPoints[2] == null || _splitPoints[3] == null) return;
        var p0 = _splitPoints[2]!.Value; // Interincisal
        var p1 = _splitPoints[3]!.Value; // Left Posterior
        var p2 = _splitPoints[1]!.Value; // Right Posterior
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
        if (_planeTriangleVisual != null) MainGroup.Children.Remove(_planeTriangleVisual);
        if (_splitPoints[1] == null || _splitPoints[2] == null || _splitPoints[3] == null) return;

        // Create local U, V vectors for the plane
        var u = (_splitPoints[1]!.Value - _splitPoints[3]!.Value); u.Normalize();
        var v = Vector3D.CrossProduct(_planeNormal, u); v.Normalize();

        List<Point3D> planePts = new List<Point3D> { _splitPoints[2]!.Value, _splitPoints[3]!.Value, _splitPoints[1]!.Value };

        // Project the 3 points onto U, V to find the bounding box in local plane coordinates
        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;

        foreach (var pt in planePts)
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

        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddQuad(
            new System.Numerics.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z),
            new System.Numerics.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z),
            new System.Numerics.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z),
            new System.Numerics.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z));

        var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(0, 1.0f, 100/255f, 60/255f) };
        var model = new MeshGeometryModel3D { Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()), Material = mat };

        var lineBuilder = new HelixToolkit.SharpDX.LineBuilder();
        lineBuilder.AddLine(new System.Numerics.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z), new System.Numerics.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z));
        lineBuilder.AddLine(new System.Numerics.Vector3((float)c1.X, (float)c1.Y, (float)c1.Z), new System.Numerics.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z));
        lineBuilder.AddLine(new System.Numerics.Vector3((float)c2.X, (float)c2.Y, (float)c2.Z), new System.Numerics.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z));
        lineBuilder.AddLine(new System.Numerics.Vector3((float)c3.X, (float)c3.Y, (float)c3.Z), new System.Numerics.Vector3((float)c0.X, (float)c0.Y, (float)c0.Z));
        
        var border = new LineGeometryModel3D { Geometry = lineBuilder.ToLineGeometry3D(), Color = System.Windows.Media.Colors.Cyan, Thickness = 2 };

        var parent = new GroupModel3D();
        parent.Children.Add(model);
        parent.Children.Add(border);
        _planeTriangleVisual = parent;
        MainGroup.Children.Add(parent);
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
        if (GetNextEmptySlot() != -1)
        {
            MessageBox.Show("Please place all 5 points first.", "Need Points",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentStep = 2;
        _dragPointIndex = -1;

        // Compute and draw the plane
        ComputePlane();

        // Populate condylar boxes
        // 10mm medial shift: right side (+x) shifts -10, left side (-x) shifts +10 (assuming X origin near midline)
        float midlineX = (float)(_splitPoints[2]!.Value.X);

        if (_splitPoints[0] != null)
        {
            var pRight = _splitPoints[0]!.Value;
            float startX = (float)pRight.X;
            startX += (startX > midlineX) ? -10f : 10f; // Shift medial

            _rightCondyleCenter = new[] { startX, (float)pRight.Y, (float)pRight.Z };
            _rightCondyleClickPoint = new[] { (float)pRight.X, (float)pRight.Y, (float)pRight.Z };
        }
        if (_splitPoints[4] != null)
        {
            var pLeft = _splitPoints[4]!.Value;
            float startX = (float)pLeft.X;
            startX += (startX > midlineX) ? -10f : 10f; // Shift medial

            _leftCondyleCenter = new[] { startX, (float)pLeft.Y, (float)pLeft.Z };
            _leftCondyleClickPoint = new[] { (float)pLeft.X, (float)pLeft.Y, (float)pLeft.Z };
        }
        RebuildBoxVisuals();

        CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(0, 1, 0)); // Resnap to anterior

        StepTitle.Text = "Step 2: Review Condylar Bounding Boxes & Plane";
        StepInstructions.Text =
            "Review the bounding boxes and cut plane. Drag the box center to move, drag corners to resize. Then click 'Split'.";
        StatusText.Text = "Ready to split";
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

            bool hardSeparation = Dispatcher.Invoke(() => HardSeparationCheck.IsChecked == true);

            var (cranium, mandible) = await Task.Run(() =>
                SplitVoxelMask(normal, planeD, leftC, rightC,
                    _leftCondyleClickPoint ?? leftC, _rightCondyleClickPoint ?? rightC,
                    leftHE, rightHE, segVol, ctVol, boneLabel, hardSeparation));

            _craniumVerts = cranium;
            _mandibleVerts = mandible;

            Dispatcher.Invoke(() =>
            {
                MainGroup.Children.Clear();

                if (_craniumVerts != null && _craniumVerts.Count > 0)
                {
                    var cranModel = MeshHelper.BuildModel3D(_craniumVerts, 220, 200, 170);
                    MainGroup.Children.Add(cranModel);
                }
                if (_mandibleVerts != null && _mandibleVerts.Count > 0)
                {
                    var mandModel = MeshHelper.BuildModel3D(_mandibleVerts, 220, 140, 120);
                    MainGroup.Children.Add(mandModel);
                }


                AddSphereMarker(leftC,  System.Windows.Media.Colors.Cyan, 2);
                AddSphereMarker(rightC, System.Windows.Media.Colors.Cyan, 2);
                RebuildBoxVisuals();

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
        SegmentationVolume inputSegVol, VolumeData ctVol, byte boneLabel, bool hardSeparationMode)
    {
        byte cranLabel = 200; // Cranium top inside box
        byte mandLabel = 201; // Condyle bottom inside box
        byte mandBodyLabel = 202; // Mandible Body below plane
        byte unassignedAboveLabel = 203; // Bone above plane outside boxes (ramus maxilla)

        int w = ctVol.Width, h = ctVol.Height, d = ctVol.Depth;
        double sx = ctVol.Spacing[0], sy = ctVol.Spacing[1], sz = ctVol.Spacing[2];

        // Create a deep clone of the segmentation mask to prevent permanent global state mutation across runs!
        var segVol = new SegmentationVolume(ctVol);
        Array.Copy(inputSegVol.Labels, segVol.Labels, inputSegVol.Labels.Length);

        Dispatcher.Invoke(() => StatusText.Text = "Generating pristine bone mask...");

        short rawThreshold = (short)Math.Max(100, _boneMinHu);

        // 1. Recreate Pristine Mask
        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int idx = x + y * w + z * w * h;
            if (inputSegVol.Labels[idx] != boneLabel) continue;
            
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
                for(int zoff=-8; zoff<=8; zoff++)
                for(int yoff=-8; yoff<=8; yoff++)
                for(int xoff=-8; xoff<=8; xoff++)
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
                        // Hard Separation Mode: Ellipsoid constraint
                        if (hardSeparationMode)
                        {
                            float pvx = (float)(nx * sx);
                            float pvy = (float)(ny * sy);
                            float pvz = (float)(nz * sz);

                            // Determine which box we are closer to
                            double distL = (pvx - leftC[0]) * (pvx - leftC[0]) + (pvy - leftC[1]) * (pvy - leftC[1]) + (pvz - leftC[2]) * (pvz - leftC[2]);
                            double distR = (pvx - rightC[0]) * (pvx - rightC[0]) + (pvy - rightC[1]) * (pvy - rightC[1]) + (pvz - rightC[2]) * (pvz - rightC[2]);

                            float[] curC = distL < distR ? leftC : rightC;
                            float[] curHE = distL < distR ? leftHE : rightHE;

                            // If we are in the vertical Z range of the chosen condylar box, constrain to ellipsoid
                            if (pvz >= curC[2] - curHE[2] && pvz <= curC[2] + curHE[2])
                            {
                                float dx = (pvx - curC[0]) / curHE[0];
                                float dy = (pvy - curC[1]) / curHE[1];
                                float dz = (pvz - curC[2]) / curHE[2];
                                if (dx * dx + dy * dy + dz * dz > 1.0f) continue; // Outside rugby ball
                            }
                        }

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
            for (int i = 0; i < 5; i++)
            {
                if (_splitPoints[i] != null && (_splitPoints[i]!.Value - hit.Value).Length < 5)
                { _dragPointIndex = i; e.Handled = true; return; }
            }

            int nextSlot = GetNextEmptySlot();
            if (nextSlot != -1)
            {
                _splitPoints[nextSlot] = hit.Value;

                var builder = new HelixToolkit.Geometry.MeshBuilder();
                builder.AddSphere(new System.Numerics.Vector3(0, 0, 0), 2f);
                
                // Color points cyan initially, but conditionally color Condyles vs plane points? Let's just use Cyan
                var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(0, 1, 1, 1) };
                
                var marker = new MeshGeometryModel3D
                {
                    Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
                    Material = mat,
                    Transform = new TranslateTransform3D(hit.Value.X, hit.Value.Y, hit.Value.Z)
                };
                
                _splitMarkers[nextSlot] = marker;
                MainGroup.Children.Add(marker);

                UpdateStep1Instructions();

                int nextAfter = GetNextEmptySlot();
                // Sequence: 0: RN, 1: RP, 2: IN, 3: LP, 4: LN
                if (nextAfter != -1)
                {
                    if (nextAfter == 1) CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(0.707, 0.707, 0)); // Right 45
                    else if (nextAfter == 2) CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(0, 1, 0)); // Anterior
                    else if (nextAfter == 3) CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(-0.707, 0.707, 0)); // Left 45
                    else if (nextAfter == 4) CenterViewportOnBone(new System.Windows.Media.Media3D.Vector3D(-1, 0, 0)); // Left Profile
                }
            }
            e.Handled = true;
            return;
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

        if (_currentStep == 1 && _dragPointIndex >= 0 && _dragPointIndex < 5 && hit != null)
        {
            // Drag point along the plane
            if (_splitPoints[_dragPointIndex] != null)
            {
                var pt = hit.Value;
                _splitPoints[_dragPointIndex] = new Point3D(pt.X, pt.Y, _splitPoints[_dragPointIndex]!.Value.Z);
                var marker = _splitMarkers[_dragPointIndex];
                if (marker != null) marker.Transform = new TranslateTransform3D(pt.X, pt.Y, _splitPoints[_dragPointIndex]!.Value.Z);
                
                // Realtime plane and box updates if all 5 points already exist and we're just tweaking
                if (GetNextEmptySlot() == -1)
                {
                    ComputePlane();
                    RebuildBoxVisuals(); // This won't work perfectly until Step 2 but it's fine for the plane visual
                }
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
                    var pt2RValue = MainViewport.UnProject(pos);
                    var cam = MainViewport.Camera as HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

                    if (cam != null)
                    {
                        var pt2RayOrigin = new Point3D(pt2RValue.Position.X, pt2RValue.Position.Y, pt2RValue.Position.Z);
                        var pt2RayDirection = new Vector3D(pt2RValue.Direction.X, pt2RValue.Direction.Y, pt2RValue.Direction.Z);
                        var normal = cam.LookDirection; normal.Normalize();
                        var planeP = new Point3D(_dragStartCenter[0], _dragStartCenter[1], _dragStartCenter[2]);
                        
                        double denom = Vector3D.DotProduct(normal, pt2RayDirection);
                        if (Math.Abs(denom) > 1e-6)
                        {
                            double t = Vector3D.DotProduct(normal, planeP - pt2RayOrigin) / denom;
                            var planeHit = pt2RayOrigin + pt2RayDirection * t;

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
        double midlineX = _planeCentroid.X;
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

    // Use right-click to reset extents to 15,10,10 or delete a point
    private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainViewport);
        var hit = GetHitPoint(pos);
        if (hit == null) return;

        if (_currentStep == 1)
        {
            for (int i = 0; i < 5; i++)
            {
                if (_splitPoints[i] != null && (_splitPoints[i]!.Value - hit.Value).Length < 5)
                {
                    _splitPoints[i] = null;
                    if (_splitMarkers[i] != null) MainGroup.Children.Remove(_splitMarkers[i]);
                    _splitMarkers[i] = null;
                    
                    if (_planeTriangleVisual != null) { MainGroup.Children.Remove(_planeTriangleVisual); _planeTriangleVisual = null; }
                    UpdateStep1Instructions();
                    e.Handled = true;
                    return;
                }
            }
        }
        else if (_currentStep == 2)
        {
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
    }

    // ═══════════════════════════════════
    // Visual helpers
    // ═══════════════════════════════════
    private void AddLighting()
    {
    }

    private void RebuildBoxVisuals()
    {
        if (_leftBoxVisual != null) MainGroup.Children.Remove(_leftBoxVisual);
        if (_rightBoxVisual != null) MainGroup.Children.Remove(_rightBoxVisual);
        if (_condylarAxisVisual != null) MainGroup.Children.Remove(_condylarAxisVisual);

        // Render solid Axis first so it correctly depth tests behind opaque bone but behind the transparent boxes
        if (_leftCondyleCenter != null && _rightCondyleCenter != null)
        {
            // X-axis lateral extensions, sticking out an extra 15mm past the actual box edges
            float leftDx = (_leftCondyleCenter[0] > _rightCondyleCenter[0]) ? _leftHalfExtents[0] + 15f : -(_leftHalfExtents[0] + 15f);
            float rightDx = (_rightCondyleCenter[0] > _leftCondyleCenter[0]) ? _rightHalfExtents[0] + 15f : -(_rightHalfExtents[0] + 15f);

            var p1 = new System.Numerics.Vector3(_leftCondyleCenter[0] + leftDx, _leftCondyleCenter[1], _leftCondyleCenter[2]);
            var p2 = new System.Numerics.Vector3(_rightCondyleCenter[0] + rightDx, _rightCondyleCenter[1], _rightCondyleCenter[2]);

            var lineBuilder = new HelixToolkit.SharpDX.LineBuilder();
            lineBuilder.AddLine(p1, p2);

            _condylarAxisVisual = new LineGeometryModel3D 
            { 
                Geometry = lineBuilder.ToLineGeometry3D(), 
                Color = System.Windows.Media.Colors.Red, 
                Thickness = 4
            };
            MainGroup.Children.Add(_condylarAxisVisual);
        }

        // Render transparent Boxes AFTER axis so they naturally blend over it
        if (_leftCondyleCenter != null)
        { _leftBoxVisual  = CreateBoxVisual(_leftCondyleCenter,  _leftHalfExtents,  System.Windows.Media.Colors.Cyan); MainGroup.Children.Add(_leftBoxVisual); }
        if (_rightCondyleCenter != null)
        { _rightBoxVisual = CreateBoxVisual(_rightCondyleCenter, _rightHalfExtents, System.Windows.Media.Colors.Cyan); MainGroup.Children.Add(_rightBoxVisual); }
    }

    private GroupModel3D CreateBoxVisual(float[] c, float[] he, System.Windows.Media.Color color)
    {
        double cx = c[0], cy = c[1], cz = c[2], hx = he[0], hy = he[1], hz = he[2];
        var pts = new[]
        {
            new System.Numerics.Vector3((float)(cx-hx),(float)(cy-hy),(float)(cz-hz)), new System.Numerics.Vector3((float)(cx+hx),(float)(cy-hy),(float)(cz-hz)),
            new System.Numerics.Vector3((float)(cx+hx),(float)(cy+hy),(float)(cz-hz)), new System.Numerics.Vector3((float)(cx-hx),(float)(cy+hy),(float)(cz-hz)),
            new System.Numerics.Vector3((float)(cx-hx),(float)(cy-hy),(float)(cz+hz)), new System.Numerics.Vector3((float)(cx+hx),(float)(cy-hy),(float)(cz+hz)),
            new System.Numerics.Vector3((float)(cx+hx),(float)(cy+hy),(float)(cz+hz)), new System.Numerics.Vector3((float)(cx-hx),(float)(cy+hy),(float)(cz+hz))
        };
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        int[] tris = {0,1,2,0,2,3,4,6,5,4,7,6,0,4,5,0,5,1,2,6,7,2,7,3,0,3,7,0,7,4,1,5,6,1,6,2};
        for (int i = 0; i < tris.Length; i+=3) {
            builder.AddTriangle(pts[tris[i]], pts[tris[i+1]], pts[tris[i+2]]);
        }
        var mat = new PhongMaterial { DiffuseColor = new HelixToolkit.Maths.Color4(color.R/255f, color.G/255f, color.B/255f, 40f/255f) };
        var model = new MeshGeometryModel3D { Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()), Material = mat };

        int[,] edges = {{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        var lineBuilder = new HelixToolkit.SharpDX.LineBuilder();
        for (int e = 0; e < 12; e++) { lineBuilder.AddLine(pts[edges[e,0]], pts[edges[e,1]]); }
        var lines = new LineGeometryModel3D { Geometry = lineBuilder.ToLineGeometry3D(), Color = color, Thickness = 2 };

        var parent = new GroupModel3D();
        parent.Children.Add(model);
        parent.Children.Add(lines);

        // Add corner handle on the lateral side
        double midlineX = _planePoints.Count == 3 ? (_planePoints[0].X + _planePoints[1].X + _planePoints[2].X) / 3.0 : 0;
        double signX = cx > midlineX ? 1.0 : -1.0; // Lateral side
        
        var sbuild = new HelixToolkit.Geometry.MeshBuilder();
        sbuild.AddSphere(new System.Numerics.Vector3(0,0,0), 3f);
        var cornerSphere = new MeshGeometryModel3D
        { 
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(sbuild.ToMesh()),
            Material = new PhongMaterial { 
                DiffuseColor = new HelixToolkit.Maths.Color4(0f, 1f, 1f, 1f),
                SpecularColor = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f),
                SpecularShininess = 32f
            },
            Transform = new TranslateTransform3D(cx + hx * signX, cy + hy, cz + hz)
        };
        parent.Children.Add(cornerSphere);

        return parent;
    }

    private void AddSphereMarker(float[] c, System.Windows.Media.Color color, double r)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(new System.Numerics.Vector3(0,0,0), (float)r);
        var sphere = new MeshGeometryModel3D
        {
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material = new PhongMaterial { 
                DiffuseColor = new HelixToolkit.Maths.Color4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f),
                SpecularColor = new HelixToolkit.Maths.Color4(0.8f, 0.8f, 0.8f, 1f),
                SpecularShininess = 32f
            },
            Transform = new TranslateTransform3D(c[0], c[1], c[2])
        };
        MainGroup.Children.Add(sphere);
    }

    private Point3D? GetHitPoint(Point screenPos)
    {
        var result = MainViewport.FindHits(screenPos);
        if (result != null && result.Count > 0) return new Point3D(result[0].PointHit.X, result[0].PointHit.Y, result[0].PointHit.Z);
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




