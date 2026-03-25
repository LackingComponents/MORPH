using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OrthoPlanner.App;

public partial class MainWindow : Window
{


    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();

        if (Viewport3D != null)
        {
            Viewport3D.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        }

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += OnLoaded;
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
        // ── Center camera on model when bone bounds change ──
        if (VM != null)
        {
            VM.PropertyChanged += (s, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(ViewModels.MainViewModel.BoneOnlyBounds):
                        if (VM != null && !VM.BoneOnlyBounds.IsEmpty)
                        {
                            // Do not hijack the camera if the bounds simply changed due to a bone split
                            if (VM.IsSplitting) return;

                            var b = VM.BoneOnlyBounds;
                            var centroid = new Point3D(
                                b.X + b.SizeX / 2,
                                b.Y + b.SizeY / 2,
                                b.Z + b.SizeZ / 2);
                            Viewport3D.FixedRotationPointEnabled = true;
                            Viewport3D.FixedRotationPoint = centroid;

                            // Robust centering: wait briefly for HelixScene mapping, then snap to Anterior View
                            Dispatcher.InvokeAsync(async () =>
                            {
                                await System.Threading.Tasks.Task.Delay(250);
                                CenterCamera(new System.Windows.Media.Media3D.Vector3D(0, 1, 0));
                            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        break;

                    case nameof(ViewModels.MainViewModel.ShowGrid):
                        Dispatcher.InvokeAsync(() =>
                        {
                            var show = VM?.ShowGrid == true;
                            GridOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                            if (show) DrawGrid();
                            else GridDragButton.IsChecked = false;
                        });
                        break;

                    case nameof(ViewModels.MainViewModel.ShowCrosshairs):
                    case nameof(ViewModels.MainViewModel.IsVolumeLoaded):
                        // Use ApplicationIdle so MPR canvas has time to layout
                        Dispatcher.InvokeAsync(UpdateCrosshairs, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        break;
                }
            };
        }

        // ── Initialize grid state ──
        GridOverlay.Visibility = (VM?.ShowGrid == true) ? Visibility.Visible : Visibility.Collapsed;
        if (VM?.ShowGrid == true) DrawGrid();

        // ── Redraw grid on resize ──
        GridOverlay.SizeChanged += (_, __) => { if (GridOverlay.Visibility == Visibility.Visible) DrawGrid(); };

        // ── Crosshair updates on slice index changes ──
        SetupCrosshairUpdates();

        // ── Headlamp setup: poll on render frame for bullet-proof tracking ──
        System.Windows.Media.CompositionTarget.Rendering += OnHeadlampRendering;
    }

    private void OnHeadlampRendering(object? sender, EventArgs e)
    {
        var cam = Viewport3D.Camera;
        if (cam == null) return;
        var dir = cam.LookDirection;
        if (dir.Length > 0.001)
        {
            dir.Normalize();
            // SharpDX Direction = where light comes FROM, so negate look direction.
            MainHeadlamp.Direction = new Vector3D(-dir.X, -dir.Y, -dir.Z);
        }
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

        if (isOrtho && currentCam is HelixToolkit.Wpf.SharpDX.PerspectiveCamera pc)
        {
            // Switch to orthographic, preserving orientation
            var newOrtho = new HelixToolkit.Wpf.SharpDX.OrthographicCamera {
                Position = pc.Position,
                LookDirection = pc.LookDirection,
                UpDirection = pc.UpDirection,
                Width = 300, // default orthographic width in mm
                NearPlaneDistance = pc.NearPlaneDistance,
                FarPlaneDistance = pc.FarPlaneDistance
            };
            Viewport3D.Camera = newOrtho;
        }
        else if (!isOrtho && currentCam is HelixToolkit.Wpf.SharpDX.OrthographicCamera oc)
        {
            // Switch back to perspective
            var newPersp = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
                Position = oc.Position,
                LookDirection = oc.LookDirection,
                UpDirection = oc.UpDirection,
                FieldOfView = 45,
                NearPlaneDistance = oc.NearPlaneDistance,
                FarPlaneDistance = oc.FarPlaneDistance
            };
            Viewport3D.Camera = newPersp;
        }

        // Force viewport to refresh with the new camera
        Viewport3D.InvalidateRender();
    }

    // ═══ Grid overlay ═══
    private System.Windows.Point _gridCenter = new System.Windows.Point(-1, -1);
    private System.Windows.Point _newCenter = new System.Windows.Point(-1, -1);
    private bool _isDraggingGrid = false;
    private System.Windows.Point _gridDragStart;
    private System.Windows.Point _initialGridCenter;

    private void OnGridToggled(object sender, RoutedEventArgs e)
    {
        var show = (sender as CheckBox)?.IsChecked == true;
        GridOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) 
        {
            Dispatcher.InvokeAsync(DrawGrid, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private void OnGridDragToggled(object sender, RoutedEventArgs e)
    {
        var dragging = (sender as ToggleButton)?.IsChecked == true;
        GridOverlay.IsHitTestVisible = dragging;
    }

    private void SetAsNewCenter_Click(object sender, RoutedEventArgs e)
    {
        _newCenter = _gridCenter;
    }

    private void Recentre_Click(object sender, RoutedEventArgs e)
    {
        _gridCenter = _newCenter.X >= 0 ? _newCenter : new System.Windows.Point(GridOverlay.ActualWidth / 2.0, GridOverlay.ActualHeight / 2.0);
        DrawGrid();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _gridCenter = new System.Windows.Point(-1, -1);
        _newCenter = new System.Windows.Point(-1, -1);
        DrawGrid();
    }

    private void GridOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _isDraggingGrid = true;
            _gridDragStart = e.GetPosition(this);
            _initialGridCenter = _gridCenter.X >= 0 ? _gridCenter : new System.Windows.Point(GridOverlay.ActualWidth / 2.0, GridOverlay.ActualHeight / 2.0);
            GridOverlay.CaptureMouse();
            e.Handled = true;
        }
    }

    private void GridOverlay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingGrid)
        {
            var currentPos = e.GetPosition(this);
            var delta = currentPos - _gridDragStart;
            _gridCenter = new System.Windows.Point(_initialGridCenter.X + delta.X, _initialGridCenter.Y + delta.Y);
            DrawGrid();
            e.Handled = true;
        }
    }

    private void GridOverlay_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingGrid)
        {
            _isDraggingGrid = false;
            GridOverlay.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void DrawGrid()
    {
        GridOverlay.Children.Clear();

        double w = GridOverlay.ActualWidth;
        double h = GridOverlay.ActualHeight;
        if (w < 10 || h < 10) return;

        double cx = _gridCenter.X >= 0 ? _gridCenter.X : w / 2.0;
        double cy = _gridCenter.Y >= 0 ? _gridCenter.Y : h / 2.0;

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

        if (VM == null) return;

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
        Dispatcher.InvokeAsync(UpdateCrosshairs, System.Windows.Threading.DispatcherPriority.ContextIdle);
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
                nameof(ViewModels.MainViewModel.SagittalIndex) or
                nameof(ViewModels.MainViewModel.IsVolumeLoaded))
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

        // Extreme fail-safe for the "crosshairs don't draw on load" bug. 
        // Whenever layout updates, if crosshairs should be on but canvas is fully empty, redraw.
        AxialCrosshairCanvas.LayoutUpdated += (_, _) =>
        {
            if (VM != null && VM.ShowCrosshairs && VM.IsVolumeLoaded && AxialCrosshairCanvas.Children.Count == 0)
            {
                UpdateCrosshairs();
            }
        };
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



    private void CenterCamera(System.Windows.Media.Media3D.Vector3D? lookDirection = null)
    {
        if (Viewport3D.Camera == null) return;

        // Determine camera look direction
        var dir = lookDirection ?? Viewport3D.Camera.LookDirection;
        if (dir.Length < 0.001) dir = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        dir.Normalize();

        // Compute orbit pivot: prefer ModelCenter from loaded bone, else use scene centre
        System.Windows.Media.Media3D.Point3D pivot;
        if (VM != null && !VM.BoneOnlyBounds.IsEmpty)
        {
            var mc = VM.ModelCenter;
            pivot = new System.Windows.Media.Media3D.Point3D(mc.X, mc.Y, mc.Z);
        }
        else
        {
            // Nothing loaded — fallback to viewport extent centre
            HelixToolkit.Wpf.SharpDX.ViewportExtensions.ZoomExtents(Viewport3D, 500);
            return;
        }

        // Estimate a sensible distance: diagonal of bounding box, scaled so model fills more of view
        var b = VM!.BoneOnlyBounds;
        double diagonal = Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ);
        double distance = diagonal * 0.75;
        if (distance < 10) distance = 300;

        // Position camera so it points FROM pivot outward
        var camPos = new System.Windows.Media.Media3D.Point3D(
            pivot.X - dir.X * distance,
            pivot.Y - dir.Y * distance,
            pivot.Z - dir.Z * distance);

        Viewport3D.Camera.Position      = camPos;
        // IMPORTANT: LookDirection length = distance to pivot. SharpDX rotates around
        // Position + LookDirection, so this must be dir * distance, NOT a unit vector.
        Viewport3D.Camera.LookDirection = dir * distance;
        Viewport3D.Camera.UpDirection   = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
    }

    private void CenterCamera_Click(object sender, RoutedEventArgs e)
    {
        CenterCamera();
    }

    private void AnteriorView_Click(object sender, RoutedEventArgs e)
    {
        CenterCamera(new System.Windows.Media.Media3D.Vector3D(0, 1, 0));
    }

    private void RightProfile_Click(object sender, RoutedEventArgs e)
    {
        CenterCamera(new System.Windows.Media.Media3D.Vector3D(1, 0, 0));
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