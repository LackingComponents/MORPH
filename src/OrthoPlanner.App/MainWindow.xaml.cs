using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using HelixToolkit.Wpf.SharpDX;
using OrthoPlanner.App.ViewModels;

namespace OrthoPlanner.App;

public partial class MainWindow : Window
{


    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public HelixToolkit.Wpf.SharpDX.Viewport3DX MainViewport => Viewport3D;
    public Border SharedViewportHost => ViewportHostBorder;
    public FrameworkElement Viewport3DHost => ViewportHostBorder;

    public MainWindow()
    {
        InitializeComponent();

        if (Viewport3D != null)
        {
            Viewport3D.EffectsManager = new HelixToolkit.SharpDX.DefaultEffectsManager();
        }

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += OnLoaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (_activeMeasurementTool != CustomMeasurementTool.None && _pendingMeasPts.Count > 0)
            {
                ClearPendingMeasurements();
                e.Handled = true;
            }
        }
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

                            // V-0.4: Removed dead IsNhpCommitInProgress guard.
                            // Visual-only NHP never changes BoneOnlyBounds, so this PropertyChanged
                            // only fires on DICOM load or project open — camera snap is always correct.

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

                    case nameof(ViewModels.MainViewModel.IsCephalometryOpen):
                        if (VM!.IsCephalometryOpen && VM.Volume != null)
                        {
                            CephalometryPanel.SetVolume(VM.Volume);
                            // Subscribe once so the tree rebuilds whenever measurements change
                            CephalometryPanel.MeasurementsChanged -= RebuildCephMeasurementTree;
                            CephalometryPanel.MeasurementsChanged += RebuildCephMeasurementTree;
                        }
                        break;

                    case nameof(ViewModels.MainViewModel.ShowCephLandmarksIn3D):
                        CephalometryPanel.UpdateLandmarkSphereVisibility(VM!.ShowCephLandmarksIn3D);
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

        // ── NavCube: wire to the named XAML camera (always current reference) ──
        NavCube.MainCamera = MainCamera;

        NavCube.FaceClicked += faceIdx =>
        {
            var (_, camDir, camUp, _) = Controls.NavCubeControl.FaceDefs[faceIdx];
            NavCubeFaceSnap(
                new System.Windows.Media.Media3D.Vector3D(camDir.X, camDir.Y, camDir.Z),
                new System.Windows.Media.Media3D.Vector3D(camUp.X, camUp.Y, camUp.Z));
        };

        NavCube.RotateRequested += (dAz, dEl) => OrbitCamera(dAz, dEl);
    }

    /// <summary>
    /// Snaps the main viewport camera to look along <paramref name="lookDir"/>
    /// while preserving the current look-at point and distance.
    /// Works even if no model is loaded.
    /// </summary>
    private void NavCubeFaceSnap(
        System.Windows.Media.Media3D.Vector3D lookDir,
        System.Windows.Media.Media3D.Vector3D upDir)
    {
        var cam = Viewport3D.Camera;
        if (cam == null) return;

        double dist = cam.LookDirection.Length;
        if (dist < 0.001) dist = 300;

        lookDir.Normalize();

        // Preserve the current look-at point (pivot)
        var lookAt = cam.Position + cam.LookDirection;
        cam.Position      = lookAt - lookDir * dist;
        cam.LookDirection = lookDir * dist;
        cam.UpDirection   = upDir;

        Viewport3D.FixedRotationPointEnabled = true;
        Viewport3D.FixedRotationPoint = lookAt;
    }

    private void OnHeadlampRendering(object? sender, EventArgs e)
    {
        var cam = Viewport3D.Camera;
        if (cam == null) return;
        var dir = cam.LookDirection;
        if (dir.Length > 0.001)
        {
            dir.Normalize();
            // SharpDX Direction = where light comes FROM, so negate look direction for front, direct for back.
            MainHeadlamp.Direction = new Vector3D(-dir.X, -dir.Y, -dir.Z);
            if (MainBacklamp != null) 
            {
                MainBacklamp.Direction = new Vector3D(dir.X, dir.Y, dir.Z);
            }
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
            NavCube.MainCamera = null; // NavCube only supports PerspectiveCamera
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
            NavCube.MainCamera = newPersp;
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
    private void SlicePanel_LeftDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VM == null || !VM.IsVolumeLoaded) return;
        
        // Standard behavior: Move Crosshair
        UpdateSliceFromClick(sender, e);
        e.Handled = true;
    }

    private void UpdateSliceFromClick(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var grid = sender as Grid;
        if (grid == null) return;

        var pos = e.GetPosition(grid);
        double cw = grid.ActualWidth;
        double ch = grid.ActualHeight;

        if (VM == null || VM.Volume == null || cw < 5 || ch < 5) return;

        int viewType = 0;
        if (grid.Name == "AxialPanel") viewType = 1;
        else if (grid.Name == "CoronalPanel") viewType = 2;
        else if (grid.Name == "SagittalPanel") viewType = 3;
        else if (grid.Name == "EnlargedGrid") viewType = VM.EnlargedView;

        if (viewType == 0) return;

        // Determine which MPR orientation and get its physical bounds
        MprOrientation orient = viewType switch
        {
            1 => MprOrientation.Axial,
            2 => MprOrientation.Coronal,
            3 => MprOrientation.Sagittal,
            _ => MprOrientation.Axial
        };
        VM.GetMprPhysicalBounds(orient,
            out double hMin, out double hMax, out double vMin, out double vMax);

        // Compute the Uniform-stretch image render rect (same logic as DrawCrosshairPhysical)
        double hRange = hMax - hMin;
        double vRange = vMax - vMin;
        double physAspect = hRange / vRange;
        double containerAspect = cw / ch;
        double imgW, imgH, offX, offY;
        if (physAspect > containerAspect)
        {
            imgW = cw; imgH = cw / physAspect;
            offX = 0; offY = (ch - imgH) / 2;
        }
        else
        {
            imgH = ch; imgW = ch * physAspect;
            offX = (cw - imgW) / 2; offY = 0;
        }

        // Convert click position to image-local fraction [0,1]
        double rx = Math.Clamp((pos.X - offX) / imgW, 0, 1);
        double ry = Math.Clamp((pos.Y - offY) / imgH, 0, 1);

        // Map fraction to physical coordinate, then to slice index
        double hPhys = hMin + rx * hRange;
        double vPhys = vMin + ry * vRange;
        var vol = VM.Volume;

        switch (viewType)
        {
            case 1: // Axial: H=X, V=Y
                VM.SagittalIndex = (int)Math.Clamp(Math.Round(hPhys / vol.Spacing[0]), 0, VM.SagittalMax);
                VM.CoronalIndex = (int)Math.Clamp(Math.Round(vPhys / vol.Spacing[1]), 0, VM.CoronalMax);
                break;
            case 2: // Coronal: H=X, V=Z (display order: vMin=maxZ, vMax=minZ)
                VM.SagittalIndex = (int)Math.Clamp(Math.Round(hPhys / vol.Spacing[0]), 0, VM.SagittalMax);
                VM.AxialIndex = (int)Math.Clamp(Math.Round(vPhys / vol.Spacing[2]), 0, VM.AxialMax);
                break;
            case 3: // Sagittal: H=Y, V=Z (display order)
                VM.CoronalIndex = (int)Math.Clamp(Math.Round(hPhys / vol.Spacing[1]), 0, VM.CoronalMax);
                VM.AxialIndex = (int)Math.Clamp(Math.Round(vPhys / vol.Spacing[2]), 0, VM.AxialMax);
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

        var vol = VM.Volume!;
        // Physical coordinates of the 3 slice planes (mm)
        double xMm = VM.SagittalIndex * vol.Spacing[0];
        double yMm = VM.CoronalIndex * vol.Spacing[1];
        double zMm = VM.AxialIndex * vol.Spacing[2];

        // AXIAL view: H=X (sagittal), V=Y (coronal)
        VM.GetMprPhysicalBounds(MprOrientation.Axial,
            out double axHmin, out double axHmax, out double axVmin, out double axVmax);
        DrawCrosshairPhysical(AxialCrosshairCanvas,
            xMm, axHmin, axHmax,
            yMm, axVmin, axVmax,
            _chBlue, _chGreen);

        // CORONAL view: H=X (sagittal), V=Z (axial) — bounds are in display order (vMin=maxZ)
        VM.GetMprPhysicalBounds(MprOrientation.Coronal,
            out double coHmin, out double coHmax, out double coVmin, out double coVmax);
        DrawCrosshairPhysical(CoronalCrosshairCanvas,
            xMm, coHmin, coHmax,
            zMm, coVmin, coVmax,
            _chBlue, _chRed);

        // SAGITTAL view: H=Y (coronal), V=Z (axial) — bounds in display order
        VM.GetMprPhysicalBounds(MprOrientation.Sagittal,
            out double saHmin, out double saHmax, out double saVmin, out double saVmax);
        DrawCrosshairPhysical(SagittalCrosshairCanvas,
            yMm, saHmin, saHmax,
            zMm, saVmin, saVmax,
            _chGreen, _chRed);

        // Enlarged view crosshairs
        if (VM.EnlargedView > 0 && EnlargedOverlay.Visibility == Visibility.Visible)
        {
            switch (VM.EnlargedView)
            {
                case 1: // Axial
                    DrawCrosshairPhysical(EnlargedCrosshairCanvas,
                        xMm, axHmin, axHmax, yMm, axVmin, axVmax,
                        _chBlue, _chGreen);
                    break;
                case 2: // Coronal
                    DrawCrosshairPhysical(EnlargedCrosshairCanvas,
                        xMm, coHmin, coHmax, zMm, coVmin, coVmax,
                        _chBlue, _chRed);
                    break;
                case 3: // Sagittal
                    DrawCrosshairPhysical(EnlargedCrosshairCanvas,
                        yMm, saHmin, saHmax, zMm, saVmin, saVmax,
                        _chGreen, _chRed);
                    break;
            }
        }
    }

    /// <summary>
    /// Draws a crosshair on the canvas at the given physical coordinate,
    /// mapped to the NHP-padded bitmap display area (accounting for Uniform letterboxing).
    /// </summary>
    private void DrawCrosshairPhysical(Canvas canvas,
        double hPhys, double hMin, double hMax,
        double vPhys, double vMin, double vMax,
        Brush vBrush, Brush hBrush)
    {
        double cw = canvas.ActualWidth;
        double ch = canvas.ActualHeight;
        if (cw < 5 || ch < 5) return;

        double hRange = hMax - hMin;
        double vRange = vMax - vMin;
        if (hRange <= 0 || vRange <= 0) return;

        // Physical aspect ratio of the NHP-padded bitmap
        double physAspect = hRange / vRange;
        double containerAspect = cw / ch;

        // Uniform stretch: image fills one axis, letterbox on the other
        double imgW, imgH, offX, offY;
        if (physAspect > containerAspect)
        {
            // Image is wider → fills container width, letterbox on top/bottom
            imgW = cw;
            imgH = cw / physAspect;
            offX = 0;
            offY = (ch - imgH) / 2;
        }
        else
        {
            // Image is taller → fills container height, letterbox on left/right
            imgH = ch;
            imgW = ch * physAspect;
            offX = (cw - imgW) / 2;
            offY = 0;
        }

        // Map physical coordinate to fraction within the NHP-padded extent
        double hFrac = (hPhys - hMin) / hRange;
        double vFrac = (vPhys - vMin) / vRange;

        // Convert to canvas pixel position within the image render rect
        double vx = offX + hFrac * imgW;
        double vy = offY + vFrac * imgH;

        // Vertical line (sagittal / coronal index → X or Y position)
        canvas.Children.Add(new Line
        {
            X1 = vx, Y1 = offY, X2 = vx, Y2 = offY + imgH,
            Stroke = vBrush, StrokeThickness = 1
        });
        // Horizontal line (coronal / axial index → Y or Z position)
        canvas.Children.Add(new Line
        {
            X1 = offX, Y1 = vy, X2 = offX + imgW, Y2 = vy,
            Stroke = hBrush, StrokeThickness = 1
        });
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

        // Remember current UpDirection
        var currentUp = Viewport3D.Camera.UpDirection;

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
        
        // Only force UpDirection true-to-world if we are doing an explicit new view snap.
        // Otherwise, maintain our current camera roll orientation.
        Viewport3D.Camera.UpDirection = lookDirection.HasValue ? new System.Windows.Media.Media3D.Vector3D(0, 0, 1) : currentUp;
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

    private void OptionsTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down)
        {
            if (sender is TextBox tb)
            {
                int delta = (e.Key == System.Windows.Input.Key.Up) ? 1 : -1;
                if (int.TryParse(tb.Text, out int val))
                {
                    tb.Text = Math.Max(0, val + delta).ToString();
                    var bindingExpression = tb.GetBindingExpression(TextBox.TextProperty);
                    bindingExpression?.UpdateSource();
                    e.Handled = true;
                }
            }
        }
    }

    // ═══ NavCube: Orbital arrow rotation ═══

    private void OrbitCamera(double dAzimuthDeg, double dElevationDeg)
    {
        if (Viewport3D.Camera == null) return;

        var cam = Viewport3D.Camera;
        var lookDir = cam.LookDirection;
        var upDir   = cam.UpDirection;
        double dist = lookDir.Length;
        if (dist < 0.001) return;
        lookDir.Normalize(); upDir.Normalize();

        var right = System.Windows.Media.Media3D.Vector3D.CrossProduct(lookDir, upDir);
        right.Normalize();

        // Rotate look direction around up axis (azimuth) then right axis (elevation)
        var qAz = new System.Windows.Media.Media3D.Quaternion(upDir,   dAzimuthDeg);
        var qEl = new System.Windows.Media.Media3D.Quaternion(right, dElevationDeg);
        var q = qAz * qEl;

        var mat = new System.Windows.Media.Media3D.Matrix3D();
        mat.Rotate(q);
        var newLook = mat.Transform(lookDir);
        var newUp   = mat.Transform(upDir);

        var lookAt = cam.Position + cam.LookDirection;
        cam.Position      = lookAt - newLook * dist;
        cam.LookDirection = newLook * dist;
        cam.UpDirection   = newUp;
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

    // ═══ Measurements Tab: Cephalometry tree ═══

    /// <summary>
    /// Called whenever the ceph overlay adds, removes, or changes measurements.
    /// Rebuilds the four sub-group panels in the Measurements tab.
    /// </summary>
    private void RebuildCephMeasurementTree()
    {
        // Must run on the UI thread (event may fire from any context)
        Dispatcher.InvokeAsync(() =>
        {
            var measurements = CephalometryPanel.GetMeasurements();

            CephPointsPanel.Children.Clear();
            CephPlanesPanel.Children.Clear();
            CephAnglesPanel.Children.Clear();
            CephLinearPanel.Children.Clear();

            foreach (var m in measurements)
            {
                // Classify into the correct group
                var targetPanel = m.ToolType switch
                {
                    OrthoPlanner.Core.Imaging.CephTool.CustomPoint => CephPointsPanel,
                    OrthoPlanner.Core.Imaging.CephTool.InfinitePlane => CephPlanesPanel,
                    OrthoPlanner.Core.Imaging.CephTool.AnglePlanes => CephAnglesPanel,
                    OrthoPlanner.Core.Imaging.CephTool.Angle3Points => CephAnglesPanel,
                    OrthoPlanner.Core.Imaging.CephTool.DistancePoints => CephLinearPanel,
                    OrthoPlanner.Core.Imaging.CephTool.DistancePointPlane => CephLinearPanel,
                    OrthoPlanner.Core.Imaging.CephTool.Line => CephLinearPanel,
                    _ => CephLinearPanel
                };

                var mCaptured = m; // capture for lambda
                var itemColor = Color.FromRgb(m.ColorR, m.ColorG, m.ColorB);

                // Color swatch
                var swatch = new Ellipse
                {
                    Width = 7, Height = 7,
                    Fill = new SolidColorBrush(itemColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };

                // Label text
                var labelText = string.IsNullOrEmpty(m.Unit)
                    ? m.Label
                    : $"{m.Label}: {m.Value:F1} {m.Unit}";

                var label = new System.Windows.Controls.TextBlock
                {
                    Text = labelText,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD8, 0xE0)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 140
                };

                // Visibility checkbox
                var visCheck = new System.Windows.Controls.CheckBox
                {
                    IsChecked = m.IsVisible,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                visCheck.Checked   += (_, _) => CephalometryPanel.SetMeasurementVisible(mCaptured, true);
                visCheck.Unchecked += (_, _) => CephalometryPanel.SetMeasurementVisible(mCaptured, false);

                // Delete button
                var delBtn = new System.Windows.Controls.Button
                {
                    Content = "✕",
                    FontSize = 8,
                    Padding = new Thickness(2, 0, 2, 0),
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Delete measurement"
                };
                delBtn.Click += (_, _) =>
                {
                    CephalometryPanel.DeleteMeasurementFromTree(mCaptured);
                    // Tree will rebuild via MeasurementsChanged event
                };

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                row.Children.Add(visCheck);
                row.Children.Add(swatch);
                row.Children.Add(label);
                row.Children.Add(delBtn);

                targetPanel.Children.Add(row);
            }

            // Show "(none)" placeholder in empty groups
            SetEmptyPlaceholder(CephPointsPanel, "No points placed");
            SetEmptyPlaceholder(CephPlanesPanel,  "No planes traced");
            SetEmptyPlaceholder(CephAnglesPanel,  "No angles measured");
            SetEmptyPlaceholder(CephLinearPanel,  "No linear measurements");
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private static void SetEmptyPlaceholder(StackPanel panel, string message)
    {
        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x7F, 0x90)),
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }

    /// <summary>Toggles visibility of ALL cephalometry measurements (top-level eye checkbox).</summary>
    private void CephAllVisibility_Changed(object sender, RoutedEventArgs e)
    {
        // Guard: CephalometryPanel may not be initialized yet during InitializeComponent (XAML default IsChecked fires early)
        if (CephalometryPanel == null) return;
        bool visible = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        foreach (var m in CephalometryPanel.GetMeasurements())
            CephalometryPanel.SetMeasurementVisible(m, visible);
        // Sync sub-group checkboxes
        CephPtsGroupCheck.IsChecked    = visible;
        CephPlanesGroupCheck.IsChecked = visible;
        CephAnglesGroupCheck.IsChecked = visible;
        CephLinearGroupCheck.IsChecked = visible;
    }

    /// <summary>Toggles visibility of one sub-group (Points / Planes / Angles / Linear).</summary>
    private void CephGroupVisibility_Changed(object sender, RoutedEventArgs e)
    {
        // Guard: may fire during InitializeComponent before CephalometryPanel is ready
        if (CephalometryPanel == null) return;
        if (sender is not System.Windows.Controls.CheckBox cb) return;
        string group = cb.Tag?.ToString() ?? "";
        bool visible = cb.IsChecked == true;

        foreach (var m in CephalometryPanel.GetMeasurements())
        {
            bool inGroup = group switch
            {
                "Points" => m.ToolType == OrthoPlanner.Core.Imaging.CephTool.CustomPoint,
                "Planes" => m.ToolType == OrthoPlanner.Core.Imaging.CephTool.InfinitePlane,
                "Angles" => m.ToolType is OrthoPlanner.Core.Imaging.CephTool.Angle3Points
                                       or OrthoPlanner.Core.Imaging.CephTool.AnglePlanes,
                "Linear" => m.ToolType is OrthoPlanner.Core.Imaging.CephTool.Line
                                       or OrthoPlanner.Core.Imaging.CephTool.DistancePoints
                                       or OrthoPlanner.Core.Imaging.CephTool.DistancePointPlane,
                _ => false
            };
            if (inGroup) CephalometryPanel.SetMeasurementVisible(m, visible);
        }
        RebuildCephMeasurementTree();
    }

    // ════════════════════════════════════════════════════════════════════
    // 3D Custom Measurements
    // ════════════════════════════════════════════════════════════════════

    public enum CustomMeasurementTool { None, Distance, Angle }
    private CustomMeasurementTool _activeMeasurementTool = CustomMeasurementTool.None;
    private readonly List<System.Numerics.Vector3> _pendingMeasPts = new();
    private readonly List<HelixToolkit.Wpf.SharpDX.Element3D> _pendingMeasVisuals = new();
    private readonly List<CustomMeas3D> _customMeasurements = new();
    private int _measCounter = 1;

    private class CustomMeas3D
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public System.Windows.Media.Color Color { get; set; }
        public List<HelixToolkit.Wpf.SharpDX.Element3D> Visuals { get; } = new();
        public bool IsVisible { get; set; } = true;
    }

    private void OnMeasurementToolChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox chk && chk.IsChecked == true)
        {
            if (chk == DistanceMeasButton)
            {
                _activeMeasurementTool = CustomMeasurementTool.Distance;
                AngleMeasButton.IsChecked = false;
            }
            else if (chk == AngleMeasButton)
            {
                _activeMeasurementTool = CustomMeasurementTool.Angle;
                DistanceMeasButton.IsChecked = false;
            }
            ClearPendingMeasurements();
            Viewport3D.PreviewMouseLeftButtonDown -= Viewport3D_PreviewMouseLeftButtonDown;
            Viewport3D.PreviewMouseLeftButtonDown += Viewport3D_PreviewMouseLeftButtonDown;
        }
        else
        {
            if (DistanceMeasButton.IsChecked == false && AngleMeasButton.IsChecked == false)
            {
                _activeMeasurementTool = CustomMeasurementTool.None;
                ClearPendingMeasurements();
                Viewport3D.PreviewMouseLeftButtonDown -= Viewport3D_PreviewMouseLeftButtonDown;
            }
        }
    }

    private void Viewport3D_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_activeMeasurementTool == CustomMeasurementTool.None) return;
        var hit = Viewport3D.FindHits(e.GetPosition(Viewport3D)).FirstOrDefault();
        if (hit != null && hit.IsValid)
        {
            var pt = (System.Numerics.Vector3)hit.PointHit;
            _pendingMeasPts.Add(pt);
            
            var sphere = Make3DSphere(pt, 1.2f, System.Windows.Media.Colors.Yellow);
            _pendingMeasVisuals.Add(sphere);
            Viewport3D.Items.Add(sphere);

            if (_activeMeasurementTool == CustomMeasurementTool.Distance && _pendingMeasPts.Count == 2)
            {
                FinishDistanceMeasurement();
            }
            else if (_activeMeasurementTool == CustomMeasurementTool.Angle && _pendingMeasPts.Count == 3)
            {
                FinishAngleMeasurement();
            }
            e.Handled = true;
        }
    }

    private void FinishDistanceMeasurement()
    {
        var p1 = _pendingMeasPts[0];
        var p2 = _pendingMeasPts[1];
        double dist = (p2 - p1).Length();
        var col = System.Windows.Media.Color.FromRgb(0, 229, 255);
        
        var meas = new CustomMeas3D { Label = $"D{_measCounter++}", Value = $"{dist:F1} mm", Color = col };
        meas.Visuals.Add(Make3DSphere(p1, 1.2f, col));
        meas.Visuals.Add(Make3DSphere(p2, 1.2f, col));
        meas.Visuals.Add(Make3DLine(p1, p2, col));
        
        foreach (var v in meas.Visuals) Viewport3D.Items.Add(v);
        _customMeasurements.Add(meas);
        
        ClearPendingMeasurements();
        DistanceMeasButton.IsChecked = false;
        RebuildCxMeasurementTree();
    }

    private void FinishAngleMeasurement()
    {
        var a = _pendingMeasPts[0];
        var vtx = _pendingMeasPts[1];
        var b = _pendingMeasPts[2];
        var v1 = System.Numerics.Vector3.Normalize(a - vtx);
        var v2 = System.Numerics.Vector3.Normalize(b - vtx);
        double dot = Math.Clamp(System.Numerics.Vector3.Dot(v1, v2), -1.0, 1.0);
        double angle = Math.Acos(dot) * 180.0 / Math.PI;
        var col = System.Windows.Media.Color.FromRgb(255, 180, 0);

        var meas = new CustomMeas3D { Label = $"A{_measCounter++}", Value = $"{angle:F1}°", Color = col };
        meas.Visuals.Add(Make3DSphere(a, 1f, col));
        meas.Visuals.Add(Make3DSphere(vtx, 1.5f, col));
        meas.Visuals.Add(Make3DSphere(b, 1f, col));
        meas.Visuals.Add(Make3DLine(a, vtx, col));
        meas.Visuals.Add(Make3DLine(b, vtx, col));
        
        foreach (var v in meas.Visuals) Viewport3D.Items.Add(v);
        _customMeasurements.Add(meas);

        ClearPendingMeasurements();
        AngleMeasButton.IsChecked = false;
        RebuildCxMeasurementTree();
    }

    private void ClearPendingMeasurements()
    {
        foreach (var s in _pendingMeasVisuals) Viewport3D.Items.Remove(s);
        _pendingMeasVisuals.Clear();
        _pendingMeasPts.Clear();
    }

    private HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D Make3DSphere(System.Numerics.Vector3 center, float radius, System.Windows.Media.Color col)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(center, radius);
        return new HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D
        {
            Geometry = HelixToolkit.SharpDX.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material = new HelixToolkit.Wpf.SharpDX.PhongMaterial
            {
                DiffuseColor = new HelixToolkit.Maths.Color4(col.R / 255f, col.G / 255f, col.B / 255f, 1f)
            },
            IsHitTestVisible = false
        };
    }

    private HelixToolkit.Wpf.SharpDX.LineGeometryModel3D Make3DLine(System.Numerics.Vector3 p1, System.Numerics.Vector3 p2, System.Windows.Media.Color col)
    {
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(p1, p2);
        return new HelixToolkit.Wpf.SharpDX.LineGeometryModel3D
        {
            Geometry = lb.ToLineGeometry3D(),
            Color = col,
            Thickness = 1.5,
            IsHitTestVisible = false
        };
    }

    private void RebuildCxMeasurementTree()
    {
        if (CxMeasListPanel == null || CxMeasGroupCheck == null) return;

        CxMeasListPanel.Children.Clear();
        bool globalVisible = CxMeasGroupCheck.IsChecked == true;

        foreach (var m in _customMeasurements)
        {
            var row = CreateCxMeasurementRow(m);
            CxMeasListPanel.Children.Add(row);

            foreach (var v in m.Visuals)
            {
                v.IsRendering = globalVisible && m.IsVisible;
            }
        }
    }

    private Border CreateCxMeasurementRow(CustomMeas3D m)
    {
        var indicator = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = new SolidColorBrush(m.Color),
            Margin = new Thickness(4, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var labelText = new TextBlock
        {
            Text = m.Label,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 216, 224)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center
        };

        var valText = new TextBlock
        {
            Text = m.Value,
            Foreground = Brushes.White,
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var visCheck = new CheckBox
        {
            IsChecked = m.IsVisible,
            ToolTip = "Show/hide measurement",
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        visCheck.Checked += (_, _) => { m.IsVisible = true; RebuildCxMeasurementTree(); };
        visCheck.Unchecked += (_, _) => { m.IsVisible = false; RebuildCxMeasurementTree(); };
        rightStack.Children.Add(visCheck);

        var delBtn = new Button
        {
            Content = "├×",
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 180, 192)),
            BorderThickness = new Thickness(0),
            FontSize = 10,
            Cursor = Cursors.Hand,
            ToolTip = "Delete measurement",
            Padding = new Thickness(2,0,2,0)
        };
        delBtn.Click += (_, _) =>
        {
            foreach (var v in m.Visuals) Viewport3D.Items.Remove(v);
            _customMeasurements.Remove(m);
            RebuildCxMeasurementTree();
        };
        rightStack.Children.Add(delBtn);

        var dock = new DockPanel { LastChildFill = false };
        dock.Children.Add(indicator);
        dock.Children.Add(labelText);
        dock.Children.Add(valText);
        DockPanel.SetDock(rightStack, Dock.Right);
        dock.Children.Add(rightStack);

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 1, 0, 0),
            Child = dock
        };
    }

    private void CxMeasGroupVisibility_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.IsLoaded && _customMeasurements != null && CxMeasListPanel != null)
            {
                RebuildCxMeasurementTree();
            }
        }
        catch (Exception)
        {
            // Ignore during initialization
        }
    }
}