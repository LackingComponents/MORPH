using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OrthoPlanner.App;

public partial class MainWindow : Window
{
    private DispatcherTimer? _styleCubeTimer;
    private int _styleAttempts;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int useImmersiveDarkMode = 1;
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }
        catch { /* Ignore on older OS */ }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is INotifyPropertyChanged vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ViewModels.MainViewModel.BoneModel))
                {
                    Dispatcher.InvokeAsync(() => {
                        Viewport3D.ZoomExtents();
                        
                        // Force rotation center to model centroid
                        var bounds = Rect3D.Empty;
                        foreach (var child in Viewport3D.Children)
                        {
                            var childBounds = HelixToolkit.Wpf.Visual3DHelper.FindBounds(child, Transform3D.Identity);
                            if (!childBounds.IsEmpty)
                            {
                                if (bounds.IsEmpty) bounds = childBounds;
                                else bounds.Union(childBounds);
                            }
                        }

                        if (!bounds.IsEmpty)
                        {
                            var centroid = new Point3D(
                                bounds.X + bounds.SizeX / 2,
                                bounds.Y + bounds.SizeY / 2,
                                bounds.Z + bounds.SizeZ / 2);
                            
                            // Align camera target to centroid
                            if (Viewport3D.Camera is ProjectionCamera cam)
                            {
                                var dist = cam.LookDirection.Length;
                                var dir = cam.LookDirection;
                                dir.Normalize();
                                cam.Position = new Point3D(centroid.X - dir.X * dist, centroid.Y - dir.Y * dist, centroid.Z - dir.Z * dist);
                                cam.LookDirection = new Vector3D(dir.X * dist, dir.Y * dist, dir.Z * dist);
                            }
                            
                            // Prevent right-click pan from detaching the pivot
                            Viewport3D.FixedRotationPointEnabled = true;
                            Viewport3D.FixedRotationPoint = centroid;
                        }
                    });
                }
            };
        }

        // The ViewCube overlay viewport gets created lazily by HelixToolkit.
        _styleAttempts = 0;
        _styleCubeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _styleCubeTimer.Tick += (s, _) =>
        {
            _styleAttempts++;
            bool found = TryStyleViewCube(Viewport3D);
            if (found || _styleAttempts > 15)
            {
                _styleCubeTimer.Stop();
                _styleCubeTimer = null;
            }
        };
        _styleCubeTimer.Start();

        // Redraw grid on resize
        GridOverlay.SizeChanged += (_, __) => { if (GridOverlay.Visibility == Visibility.Visible) DrawGrid(); };

        // Wire crosshair updates to slice index changes
        SetupCrosshairUpdates();
    }

    // ═══ Logo context menu ═══

    private void LogoMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.HorizontalOffset = 0;
            btn.ContextMenu.VerticalOffset = 4;
            btn.ContextMenu.IsOpen = true;
        }
    }

    // ═══ Projection toggle ═══

    private void OnProjectionChanged(object sender, RoutedEventArgs e)
    {
        var isOrtho = (sender as CheckBox)?.IsChecked == true;
        var currentCam = Viewport3D.Camera;

        if (isOrtho && currentCam is PerspectiveCamera pc)
        {
            // Switch to orthographic, preserving orientation
            var ortho = new OrthographicCamera
            {
                Position = pc.Position,
                LookDirection = pc.LookDirection,
                UpDirection = pc.UpDirection,
                Width = 300, // default orthographic width in mm
                NearPlaneDistance = pc.NearPlaneDistance,
                FarPlaneDistance = pc.FarPlaneDistance
            };
            Viewport3D.Camera = ortho;
        }
        else if (!isOrtho && currentCam is OrthographicCamera oc)
        {
            // Switch back to perspective
            var persp = new PerspectiveCamera
            {
                Position = oc.Position,
                LookDirection = oc.LookDirection,
                UpDirection = oc.UpDirection,
                FieldOfView = 45,
                NearPlaneDistance = oc.NearPlaneDistance,
                FarPlaneDistance = oc.FarPlaneDistance
            };
            Viewport3D.Camera = persp;
        }
    }

    // ═══ Grid overlay ═══

    private void OnGridToggled(object sender, RoutedEventArgs e)
    {
        var show = (sender as CheckBox)?.IsChecked == true;
        GridOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) DrawGrid();
    }

    private void DrawGrid()
    {
        GridOverlay.Children.Clear();

        double w = GridOverlay.ActualWidth;
        double h = GridOverlay.ActualHeight;
        if (w < 10 || h < 10) return;

        double cx = w / 2.0;
        double cy = h / 2.0;

        // Grid spacing in pixels (fixed screen-space grid)
        const double spacing = 20.0; // pixels per unit cell

        // Thin lines (every cell)
        var thinBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        thinBrush.Freeze();
        // Semi-thick every 5
        var midBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
        midBrush.Freeze();
        // Thick every 10
        var thickBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
        thickBrush.Freeze();
        // Crosshair
        var crossBrush = new SolidColorBrush(Color.FromArgb(100, 100, 200, 255));
        crossBrush.Freeze();

        // Vertical lines (from center outward)
        for (double x = cx % spacing; x < w; x += spacing)
        {
            int idx = (int)Math.Round((x - cx) / spacing);
            Brush brush; double thick;
            if (idx == 0) { brush = crossBrush; thick = 1.5; }
            else if (idx % 10 == 0) { brush = thickBrush; thick = 1.5; }
            else if (idx % 5 == 0) { brush = midBrush; thick = 1.0; }
            else { brush = thinBrush; thick = 0.5; }

            var line = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = brush, StrokeThickness = thick
            };
            GridOverlay.Children.Add(line);
        }

        // Horizontal lines
        for (double y = cy % spacing; y < h; y += spacing)
        {
            int idx = (int)Math.Round((y - cy) / spacing);
            Brush brush; double thick;
            if (idx == 0) { brush = crossBrush; thick = 1.5; }
            else if (idx % 10 == 0) { brush = thickBrush; thick = 1.5; }
            else if (idx % 5 == 0) { brush = midBrush; thick = 1.0; }
            else { brush = thinBrush; thick = 0.5; }

            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = brush, StrokeThickness = thick
            };
            GridOverlay.Children.Add(line);
        }
    }

    // ═══ MPR: Mouse wheel scroll ═══

    private ViewModels.MainViewModel? VM => DataContext as ViewModels.MainViewModel;

    private void AxialPanel_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (VM == null) return;
        VM.AxialIndex = Math.Clamp(VM.AxialIndex + (e.Delta > 0 ? 1 : -1), 0, VM.AxialMax);
        e.Handled = true;
    }

    private void CoronalPanel_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (VM == null) return;
        VM.CoronalIndex = Math.Clamp(VM.CoronalIndex + (e.Delta > 0 ? 1 : -1), 0, VM.CoronalMax);
        e.Handled = true;
    }

    private void SagittalPanel_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (VM == null) return;
        VM.SagittalIndex = Math.Clamp(VM.SagittalIndex + (e.Delta > 0 ? 1 : -1), 0, VM.SagittalMax);
        e.Handled = true;
    }

    // ═══ MPR: Keyboard Navigation ═══
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (VM == null || !VM.IsVolumeLoaded) return;

        bool isUp = e.Key == System.Windows.Input.Key.Up;
        bool isDown = e.Key == System.Windows.Input.Key.Down;

        if (isUp || isDown)
        {
            int delta = isUp ? 1 : -1;
            
            if (AxialPanel.IsMouseOver)
            {
                VM.AxialIndex = Math.Clamp(VM.AxialIndex + delta, 0, VM.AxialMax);
                e.Handled = true;
            }
            else if (CoronalPanel.IsMouseOver)
            {
                VM.CoronalIndex = Math.Clamp(VM.CoronalIndex + delta, 0, VM.CoronalMax);
                e.Handled = true;
            }
            else if (SagittalPanel.IsMouseOver)
            {
                VM.SagittalIndex = Math.Clamp(VM.SagittalIndex + delta, 0, VM.SagittalMax);
                e.Handled = true;
            }
            else if (EnlargedGrid.IsMouseOver)
            {
                if (VM.EnlargedView == 1) VM.AxialIndex = Math.Clamp(VM.AxialIndex + delta, 0, VM.AxialMax);
                else if (VM.EnlargedView == 2) VM.CoronalIndex = Math.Clamp(VM.CoronalIndex + delta, 0, VM.CoronalMax);
                else if (VM.EnlargedView == 3) VM.SagittalIndex = Math.Clamp(VM.SagittalIndex + delta, 0, VM.SagittalMax);
                e.Handled = true;
            }
        }
    }

    // ═══ MPR: Right-click W/L and Left-click Navigation ═══

    private System.Windows.Point _rightClickOrigin;
    private double _origWC, _origWW;
    private bool _rightDragging;
    private async void SlicePanel_LeftDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VM == null || !VM.IsVolumeLoaded) return;
        
        // If clicking while in Region Grow Mode, capture seed and spawn new mesh region
        if (VM.IsRegionGrowMode && !VM.IsLoading)
        {
            var grid = sender as System.Windows.Controls.Grid;
            if (grid == null) return;

            var pos = e.GetPosition(grid);
            double rx = Math.Clamp(pos.X / grid.ActualWidth, 0, 1);
            double ry = Math.Clamp(pos.Y / grid.ActualHeight, 0, 1);

            int viewType = 0;
            if (grid.Name == "AxialPanel") viewType = 1;
            else if (grid.Name == "CoronalPanel") viewType = 2;
            else if (grid.Name == "SagittalPanel") viewType = 3;
            else if (grid.Name == "EnlargedGrid") viewType = VM.EnlargedView;

            int targetX = VM.SagittalIndex;
            int targetY = VM.CoronalIndex;
            int targetZ = VM.AxialIndex;

            switch (viewType)
            {
                case 1: // Axial
                    targetX = (int)(rx * VM.SagittalMax);
                    targetY = (int)(ry * VM.CoronalMax);
                    break;
                case 2: // Coronal
                    targetX = (int)(rx * VM.SagittalMax);
                    targetZ = VM.AxialMax - (int)(ry * VM.AxialMax);
                    break;
                case 3: // Sagittal
                    targetY = (int)(rx * VM.CoronalMax);
                    targetZ = VM.AxialMax - (int)(ry * VM.AxialMax);
                    break;
            }

            e.Handled = true;
            await VM.AddSeedPointAsync(targetX, targetY, targetZ);
            return;
        }

        // Standard behavior: Move Crosshair
        UpdateSliceFromClick(sender, e);
        e.Handled = true;
    }

    private void UpdateSliceFromClick(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var grid = sender as Grid;
        if (grid == null) return;

        var pos = e.GetPosition(grid);
        double rx = Math.Clamp(pos.X / grid.ActualWidth, 0, 1);
        double ry = Math.Clamp(pos.Y / grid.ActualHeight, 0, 1);

        int viewType = 0;
        if (grid.Name == "AxialPanel") viewType = 1;
        else if (grid.Name == "CoronalPanel") viewType = 2;
        else if (grid.Name == "SagittalPanel") viewType = 3;
        else if (grid.Name == "EnlargedGrid") viewType = VM.EnlargedView;

        switch (viewType)
        {
            case 1: // Axial (X=Sagittal, Y=Coronal)
                VM.SagittalIndex = (int)(rx * VM.SagittalMax);
                VM.CoronalIndex = (int)(ry * VM.CoronalMax);
                break;
            case 2: // Coronal (X=Sagittal, Y=Axial inverted)
                VM.SagittalIndex = (int)(rx * VM.SagittalMax);
                VM.AxialIndex = VM.AxialMax - (int)(ry * VM.AxialMax);
                break;
            case 3: // Sagittal (X=Coronal, Y=Axial inverted)
                VM.CoronalIndex = (int)(rx * VM.CoronalMax);
                VM.AxialIndex = VM.AxialMax - (int)(ry * VM.AxialMax);
                break;
        }
    }

    private void SlicePanel_RightDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VM == null) return;
        _rightClickOrigin = e.GetPosition((IInputElement)sender);
        _origWC = VM.WindowCenter;
        _origWW = VM.WindowWidth;
        _rightDragging = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void SlicePanel_RightUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _rightDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SlicePanel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (VM == null) return;

        if (_rightDragging)
        {
            var pos = e.GetPosition((IInputElement)sender);
            double dx = pos.X - _rightClickOrigin.X;
            double dy = pos.Y - _rightClickOrigin.Y;

            // Horizontal drag = Window Width, Vertical drag = Window Center
            VM.WindowWidth = Math.Clamp(_origWW + dx * 4, 1, 8000);
            VM.WindowCenter = Math.Clamp(_origWC - dy * 4, -2048, 4096);
        }
    }

    // ═══ MPR: Crosshairs (throttled) ═══

    private DispatcherTimer? _crosshairThrottle;
    private static readonly SolidColorBrush _chGreen;
    private static readonly SolidColorBrush _chBlue;
    private static readonly SolidColorBrush _chRed;

    static MainWindow()
    {
        _chGreen = new SolidColorBrush(Color.FromArgb(150, 0, 200, 0)); _chGreen.Freeze();
        _chBlue = new SolidColorBrush(Color.FromArgb(150, 80, 130, 255)); _chBlue.Freeze();
        _chRed = new SolidColorBrush(Color.FromArgb(150, 255, 80, 80)); _chRed.Freeze();
    }

    private void OnCrosshairsToggled(object sender, RoutedEventArgs e)
    {
        UpdateCrosshairs();
    }

    private void SetupCrosshairUpdates()
    {
        if (VM == null) return;

        // Throttle: coalesce rapid updates into one redraw per ~16ms (60fps)
        _crosshairThrottle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _crosshairThrottle.Tick += (_, _) => { _crosshairThrottle.Stop(); UpdateCrosshairs(); };

        VM.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName is nameof(ViewModels.MainViewModel.AxialIndex) or
                nameof(ViewModels.MainViewModel.CoronalIndex) or
                nameof(ViewModels.MainViewModel.SagittalIndex))
            {
                if (!_crosshairThrottle.IsEnabled)
                    _crosshairThrottle.Start();
            }
        };

        // Redraw on resize
        AxialCrosshairCanvas.SizeChanged += (_, _) => UpdateCrosshairs();
        CoronalCrosshairCanvas.SizeChanged += (_, _) => UpdateCrosshairs();
        SagittalCrosshairCanvas.SizeChanged += (_, _) => UpdateCrosshairs();
        EnlargedCrosshairCanvas.SizeChanged += (_, _) => UpdateCrosshairs();
    }

    private void UpdateCrosshairs()
    {
        if (AxialCrosshairCanvas == null || CoronalCrosshairCanvas == null || SagittalCrosshairCanvas == null)
            return;

        AxialCrosshairCanvas.Children.Clear();
        CoronalCrosshairCanvas.Children.Clear();
        SagittalCrosshairCanvas.Children.Clear();
        EnlargedCrosshairCanvas.Children.Clear();

        if (VM == null || !VM.ShowCrosshairs || !VM.IsVolumeLoaded) return;

        // AXIAL view: shows X (sagittal position) and Y (coronal position)
        DrawCrosshair(AxialCrosshairCanvas,
            VM.SagittalIndex, VM.Volume!.Width - 1,
            VM.CoronalIndex, VM.Volume.Height - 1,
            _chBlue, _chGreen);

        // CORONAL view: shows X (sagittal position) and Z (axial position, inverted)
        int coronalH = VM.Volume.Depth;
        DrawCrosshair(CoronalCrosshairCanvas,
            VM.SagittalIndex, VM.Volume.Width - 1,
            coronalH - 1 - VM.AxialIndex, coronalH - 1,
            _chBlue, _chRed);

        // SAGITTAL view: shows Y (coronal position) and Z (axial position, inverted)
        int sagH = VM.Volume.Depth;
        DrawCrosshair(SagittalCrosshairCanvas,
            VM.CoronalIndex, VM.Volume.Height - 1,
            sagH - 1 - VM.AxialIndex, sagH - 1,
            _chGreen, _chRed);

        // Enlarged view crosshairs
        if (VM.EnlargedView > 0 && EnlargedOverlay.Visibility == Visibility.Visible)
        {
            switch (VM.EnlargedView)
            {
                case 1: // Axial
                    DrawCrosshair(EnlargedCrosshairCanvas,
                        VM.SagittalIndex, VM.Volume.Width - 1,
                        VM.CoronalIndex, VM.Volume.Height - 1,
                        _chBlue, _chGreen);
                    break;
                case 2: // Coronal
                    DrawCrosshair(EnlargedCrosshairCanvas,
                        VM.SagittalIndex, VM.Volume.Width - 1,
                        coronalH - 1 - VM.AxialIndex, coronalH - 1,
                        _chBlue, _chRed);
                    break;
                case 3: // Sagittal
                    DrawCrosshair(EnlargedCrosshairCanvas,
                        VM.CoronalIndex, VM.Volume.Height - 1,
                        sagH - 1 - VM.AxialIndex, sagH - 1,
                        _chGreen, _chRed);
                    break;
            }
        }
    }

    private void DrawCrosshair(Canvas canvas, int vIdx, int vMax, int hIdx, int hMax,
        Brush vBrush, Brush hBrush)
    {
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 5 || h < 5 || vMax <= 0 || hMax <= 0) return;

        double vx = (vIdx / (double)vMax) * w;
        double hy = (hIdx / (double)hMax) * h;

        canvas.Children.Add(new Line { X1 = vx, Y1 = 0, X2 = vx, Y2 = h, Stroke = vBrush, StrokeThickness = 1 });
        canvas.Children.Add(new Line { X1 = 0, Y1 = hy, X2 = w, Y2 = hy, Stroke = hBrush, StrokeThickness = 1 });
    }

    // ═══ MPR: Enlarge ═══

    private void EnlargeAxial_Click(object sender, RoutedEventArgs e) => ToggleEnlarge(1);
    private void EnlargeCoronal_Click(object sender, RoutedEventArgs e) => ToggleEnlarge(2);
    private void EnlargeSagittal_Click(object sender, RoutedEventArgs e) => ToggleEnlarge(3);
    private void CloseEnlarged_Click(object sender, RoutedEventArgs e) => ToggleEnlarge(0);

    private void ToggleEnlarge(int view)
    {
        if (VM == null) return;
        VM.EnlargedView = VM.EnlargedView == view ? 0 : view;
        UpdateEnlargedView();
    }

    private void UpdateEnlargedView()
    {
        if (VM == null || VM.EnlargedView == 0)
        {
            EnlargedOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        EnlargedOverlay.Visibility = Visibility.Visible;

        switch (VM.EnlargedView)
        {
            case 1:
                EnlargedImage.SetBinding(Image.SourceProperty,
                    new System.Windows.Data.Binding("AxialImage") { Source = VM });
                EnlargedLabel.Text = "AXIAL";
                break;
            case 2:
                EnlargedImage.SetBinding(Image.SourceProperty,
                    new System.Windows.Data.Binding("CoronalImage") { Source = VM });
                EnlargedLabel.Text = "CORONAL";
                break;
            case 3:
                EnlargedImage.SetBinding(Image.SourceProperty,
                    new System.Windows.Data.Binding("SagittalImage") { Source = VM });
                EnlargedLabel.Text = "SAGITTAL";
                break;
        }
    }

    private void EnlargedPanel_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (VM == null) return;
        int delta = e.Delta > 0 ? 1 : -1;
        switch (VM.EnlargedView)
        {
            case 1: VM.AxialIndex = Math.Clamp(VM.AxialIndex + delta, 0, VM.AxialMax); break;
            case 2: VM.CoronalIndex = Math.Clamp(VM.CoronalIndex + delta, 0, VM.CoronalMax); break;
            case 3: VM.SagittalIndex = Math.Clamp(VM.SagittalIndex + delta, 0, VM.SagittalMax); break;
        }
        e.Handled = true;
    }

    // ═══ ViewCube styling ═══

    private bool TryStyleViewCube(DependencyObject parent)
    {
        bool found = false;
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is System.Windows.Controls.Viewport3D vp3d)
            {
                foreach (Visual3D v3d in vp3d.Children)
                {
                    if (v3d.GetType().Name == "ViewCubeVisual3D" && v3d is ModelVisual3D vcMv)
                    {
                        StyleViewCubeModel(vcMv);
                        found = true;
                    }
                }
            }

            if (TryStyleViewCube(child))
                found = true;
        }
        return found;
    }

    private void StyleViewCubeModel(ModelVisual3D vcVisual)
    {
        if (vcVisual.Content is Model3DGroup grp)
            StyleModel3DGroup(grp);

        foreach (Visual3D child in vcVisual.Children)
        {
            if (child is ModelVisual3D childMv)
                StyleViewCubeModel(childMv);
        }
    }

    private void StyleModel3DGroup(Model3DGroup grp)
    {
        var grey = new SolidColorBrush(Color.FromRgb(75, 75, 75));
        grey.Freeze();
        var greyBorder = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        greyBorder.Freeze();

        foreach (var m3d in grp.Children)
        {
            if (m3d is GeometryModel3D gm)
            {
                if (gm.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 30)
                {
                    gm.Material = new DiffuseMaterial(Brushes.Transparent);
                    gm.BackMaterial = new DiffuseMaterial(Brushes.Transparent);
                }
                else if (gm.Material is DiffuseMaterial dm)
                {
                    if (dm.Brush is VisualBrush vb)
                    {
                        if (vb.Visual is Border border)
                        {
                            border.Background = grey;
                            border.BorderBrush = greyBorder;
                            if (border.Child is TextBlock tb)
                                tb.Foreground = Brushes.White;
                        }
                    }
                    else if (dm.Brush is SolidColorBrush)
                    {
                        gm.Material = new DiffuseMaterial(grey);
                        if (gm.BackMaterial != null)
                            gm.BackMaterial = new DiffuseMaterial(grey);
                    }
                }
            }
            else if (m3d is Model3DGroup subGrp)
            {
                StyleModel3DGroup(subGrp);
            }
        }
    }

    // ═══ Viewport Navigation ═══

    private void Viewport3D_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Viewport3D vp3d)
        {
            var hitParams = new PointHitTestParameters(e.GetPosition(vp3d));
            bool hitCube = false;

            VisualTreeHelper.HitTest(vp3d, null, result =>
            {
                if (result is RayMeshGeometry3DHitTestResult meshHit)
                {
                    DependencyObject? current = meshHit.ModelHit;
                    while (current != null)
                    {
                        if (current.GetType().Name == "ViewCubeVisual3D")
                        {
                            hitCube = true;
                            return HitTestResultBehavior.Stop;
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
                return HitTestResultBehavior.Continue;
            }, hitParams);

            if (hitCube)
            {
                if (vp3d.Camera is ProjectionCamera cam)
                {
                    // Find the actual physical bounding box of all 3D objects in the scene
                    var bounds = Rect3D.Empty;
                    foreach (var child in vp3d.Children)
                    {
                        var childBounds = HelixToolkit.Wpf.Visual3DHelper.FindBounds(child, Transform3D.Identity);
                        if (!childBounds.IsEmpty)
                        {
                            if (bounds.IsEmpty) bounds = childBounds;
                            else bounds.Union(childBounds);
                        }
                    }
                    
                    
                    if (!bounds.IsEmpty)
                    {
                        // Calculate the spatial centroid of the DICOM mesh structure
                        var centroid = new Point3D(
                            bounds.X + bounds.SizeX / 2,
                            bounds.Y + bounds.SizeY / 2,
                            bounds.Z + bounds.SizeZ / 2);

                        // Calculate an appropriate safe viewing distance
                        double distance = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) * 1.5;
                        if (distance < 100) distance = 300.0;
                        
                        // Keep current camera look-direction but normalize the vector
                        var dir = cam.LookDirection;
                        dir.Normalize();

                        // Enforce the specific Face direction triggered by Helix Toolkit 
                        // Instead of backing out from the 'current' position (which keeps the pan offset),
                        // we completely discard translation offsets by looking strictly from the centroid outwards!
                        cam.Position = new Point3D(
                            centroid.X - dir.X * distance, 
                            centroid.Y - dir.Y * distance, 
                            centroid.Z - dir.Z * distance);
                        
                        cam.LookDirection = new Vector3D(dir.X * distance, dir.Y * distance, dir.Z * distance);
                    }
                }
            }
        }
    }

    private void NhpTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down)
        {
            if (sender is TextBox tb && DataContext is ViewModels.MainViewModel vm)
            {
                string tag = tb.Tag?.ToString() ?? "";
                string direction = (e.Key == System.Windows.Input.Key.Up) ? "+" : "-";
                
                if (!string.IsNullOrEmpty(tag))
                {
                    vm.AdjustNhpCommand.Execute(tag + direction);
                    e.Handled = true;
                }
            }
        }
    }

    // ═══ Accordion Expander Logic ═══
    private void SingleExpanderOnly_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expandedExpander && expandedExpander.Parent is StackPanel parentPanel)
        {
            foreach (var child in parentPanel.Children)
            {
                if (child is Expander ex && ex != expandedExpander)
                {
                    ex.IsExpanded = false;
                }
            }
        }
    }
}