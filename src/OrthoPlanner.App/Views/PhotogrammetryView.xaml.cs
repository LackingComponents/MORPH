using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OrthoPlanner.App.ViewModels.Photogrammetry;

namespace OrthoPlanner.App.Views;

/// <summary>
/// Single-Canvas rendering.
///
/// Coordinate spaces:
///   Screen = MasterCanvas pixels (origin top-left of ViewportHost)
///   Image  = BitmapSource pixels (0..PixelWidth , 0..PixelHeight)
///
///   screenPt = R(rotation about the image centre)(imagePt) * _zoom + (_offX, _offY)
///   imagePt  = inverse of the above (see ImageToScreen)
///   The photo rotation (RotationAngle) is a true scene rotation: the photo, annotations, overlay and mouse
///   input all go through ImageToScreen; only the grid stays screen-aligned.
///
/// Mouse interaction model (unified 2-click for all tools):
///   • Pan  : left-drag as usual.
///   • Tools: 1st click  → place P1, live dashed preview starts from P1.
///            mouse move → preview updates freely (no button held).
///            2nd click  → place P2, tool fires.
///   • Angle: additionally waits for a 3rd click (P3) using _angleWaiting.
/// </summary>
public partial class PhotogrammetryView : UserControl
{
    // ── Viewport ──────────────────────────────────────────────────────────────────
    private double _zoom = 1.0;
    private double _offX = 0;
    private double _offY = 0;

    // ── Pan state ─────────────────────────────────────────────────────────────────
    private bool  _isPanning = false;
    private Point _panStart;

    // ── Unified 2-click line state (Normalize / Horizon / Measure / DrawLine / Angle-arm1)
    private bool  _lineWaiting = false;   // true after 1st click, waiting for 2nd
    private Point _lineP1;                // 1st click image coords
    private Line? _linePreview = null;    // live dashed line

    // Calibration popup: pixel distance stored until Apply is pressed
    private double _pendingScalePixelDist = 0;

    // ── Angle tool phase-2 (waiting for 3rd point P3) ─────────────────────────────
    private bool  _angleWaiting    = false;
    private Point _angleP1;               // arm-1 start (image)
    private Point _angleP2;               // vertex (image)
    private Line? _anglePersistLine1 = null;
    private Line? _anglePreviewLine2 = null;

    // ── Permanent image element ───────────────────────────────────────────────────
    private readonly Image _imgElement = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private PhotogrammetryViewModel? Vm     => DataContext as PhotogrammetryViewModel;
    private PhotoViewModel?          Active => Vm?.ActivePhoto;

    // ═══════════════════════════════════════════════════════════════════════════════
    public PhotogrammetryView()
    {
        InitializeComponent();
        MasterCanvas.Children.Add(_imgElement);
        Panel.SetZIndex(_imgElement, 0);
        DataContextChanged += OnDataContextChanged;
        PreviewKeyDown += View_PreviewKeyDown;
        ViewportHost.MouseDown += Viewport_MiddleDown;
        ViewportHost.MouseUp   += Viewport_MiddleUp;
        InitProfilePanel();
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────────────
    // Keys only reach this view when focus is inside it: ViewportHost takes focus on click/hover
    // (see FocusViewport). Text boxes keep their own keys — the shortcut never fires while typing.
    private void View_PreviewKeyDown(object s, KeyEventArgs e)
    {
        // O: show / hide the reference overlay, only while the photo viewport itself has focus
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.None && ViewportHost.IsKeyboardFocused)
        {
            if (ToggleOverlayVisibility()) e.Handled = true;
            return;
        }
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (!IsVisible || Active?.ImageSource == null) return;

        FitToViewport();
        FullRedraw();
        e.Handled = true;
    }

    /// <summary>Gives the photo area keyboard focus, unless the user is typing in a text box.</summary>
    private void FocusViewport()
    {
        if (ViewportHost.IsKeyboardFocused || CalibrationPopup.IsOpen) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        ViewportHost.Focus();
    }

    // ── DataContext / ViewModel wiring ────────────────────────────────────────────
    private void OnDataContextChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PhotogrammetryViewModel old) { old.PropertyChanged -= OnVmChanged; old.HistoryRestored -= OnHistoryRestored; }
        if (e.NewValue is PhotogrammetryViewModel nv)  { nv.PropertyChanged  += OnVmChanged; nv.HistoryRestored  += OnHistoryRestored; }
        FullRedraw();
        OnCephActivePhotoChanged();
        UpdateProfilePanel();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotogrammetryViewModel.ActivePhoto))
        {
            CloseCalibration(); CancelLineTool(); CancelAngleTool(); EndGridDrag();
            SubscribePhoto(); FitToViewport(); FullRedraw();
            OnCephActivePhotoChanged();
            _profileError = null; EndLandmarkDrag(); UpdateProfilePanel();
        }
        else if (e.PropertyName == nameof(PhotogrammetryViewModel.ShowGridOverlay))
        {
            if (Vm?.ShowGridOverlay != true) BtnGridMove.IsChecked = false;   // move mode only with the grid on
            DrawGrid();
        }
    }

    /// <summary>Undo/redo restored a snapshot: drop half-finished tool clicks and redraw (view zoom/pan kept).</summary>
    private void OnHistoryRestored(object? s, EventArgs e)
    {
        CloseCalibration(); CancelLineTool(); CancelAngleTool(); EndGridDrag();
        SubscribePhoto(); FullRedraw();
        OnCephActivePhotoChanged();
        EndLandmarkDrag(); UpdateProfilePanel();
    }

    private PhotoViewModel? _sub;
    private void SubscribePhoto()
    {
        if (_sub != null) _sub.PropertyChanged -= OnPhotoChanged;
        _sub = Active;
        if (_sub != null) _sub.PropertyChanged += OnPhotoChanged;
    }

    private void OnPhotoChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PhotoViewModel.ImageSource):
                FitToViewport(); FullRedraw(); break;
            case nameof(PhotoViewModel.RotationAngle):
                FullRedraw(); UpdateCephPanel(); UpdateProfilePanel(); break;
            case nameof(PhotoViewModel.CephOverlay):
                SubscribeCephState(); FullRedraw(); UpdateCephPanel(); break;
            case nameof(PhotoViewModel.PixelsPerMm):
            case nameof(PhotoViewModel.IsNormalized):
                DrawGrid(); UpdateProfilePanel(); break;   // the grid switches between screen and mm
            case nameof(PhotoViewModel.IsNhpOriented):
                UpdateProfilePanel(); break;
            case nameof(PhotoViewModel.ProfileLandmarks):
                FullRedraw(); UpdateProfilePanel(); break;
            case nameof(PhotoViewModel.ZoomScale) when Active?.ZoomScale == 0:
                FitToViewport(); FullRedraw(); break;
        }
    }

    // ── Sizing ────────────────────────────────────────────────────────────────────
    private void Viewport_SizeChanged(object s, SizeChangedEventArgs e) { FitToViewport(); FullRedraw(); }

    private void FitToViewport()
    {
        var photo = Active;
        if (photo?.ImageSource == null) return;

        double vw = ViewportHost.ActualWidth;
        double vh = ViewportHost.ActualHeight;
        if (vw <= 0 || vh <= 0) return;

        double imgW = photo.ImageSource.PixelWidth;
        double imgH = photo.ImageSource.PixelHeight;
        if (imgW <= 0 || imgH <= 0) return;

        _zoom = Math.Min(vw / imgW, vh / imgH) * 0.95;
        _offX = (vw - imgW * _zoom) / 2.0;
        _offY = (vh - imgH * _zoom) / 2.0;

        photo.ZoomScale = _zoom;
        photo.OffsetX   = _offX;
        photo.OffsetY   = _offY;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────────
    private void ClearOverlays()
    {
        for (int i = MasterCanvas.Children.Count - 1; i >= 0; i--)
            if (MasterCanvas.Children[i] != _imgElement)
                MasterCanvas.Children.RemoveAt(i);
        _anglePersistLine1 = null;
        _anglePreviewLine2 = null;
        _linePreview = null;
    }

    private void FullRedraw()
    {
        if (!IsLoaded) return;
        var photo = Active;
        if (photo?.ImageSource == null) { _imgElement.Source = null; ClearOverlays(); return; }

        _imgElement.Source = photo.ImageSource;
        _imgElement.RenderTransformOrigin = new Point(0.5, 0.5);
        _imgElement.RenderTransform = new RotateTransform(photo.RotationAngle);
        _imgElement.Width  = photo.ImageSource.PixelWidth  * _zoom;
        _imgElement.Height = photo.ImageSource.PixelHeight * _zoom;
        Canvas.SetLeft(_imgElement, _offX);
        Canvas.SetTop(_imgElement,  _offY);

        ClearOverlays();
        DrawGrid();
        RedrawAnnotations(photo);
        DrawCephOverlay(photo);
        DrawProfileLayer(photo);

        // Restore in-progress angle phase-1 line (if visible before redraw)
        if (_angleWaiting && _anglePersistLine1 == null)
        {
            _anglePersistLine1 = MkLine(I2S(_angleP1), I2S(_angleP2), "#FF00BCD4", 1.5, 18);
            MasterCanvas.Children.Add(_anglePersistLine1);
        }
    }

    private void RedrawAnnotations(PhotoViewModel photo)
    {
        foreach (var m in photo.Measurements)
        {
            var s = I2S(m.StartPoint); var e = I2S(m.EndPoint);
            MasterCanvas.Children.Add(MkLine(s, e, "#FF4CAF50", 1.5, 8));
            MasterCanvas.Children.Add(MkLabel($"{m.DistanceMm:F1} mm", Lerp(s, e), "#FF4CAF50", 9));
        }
        foreach (var a in photo.AngleAnnotations)
        {
            var l1s = I2S(a.L1Start); var l1e = I2S(a.L1End);
            var l2s = I2S(a.L2Start); var l2e = I2S(a.L2End);
            MasterCanvas.Children.Add(MkLine(l1s, l1e, "#FF00BCD4", 1.5, 8));
            MasterCanvas.Children.Add(MkLine(l2s, l2e, "#FF00BCD4", 1.5, 8));
            MasterCanvas.Children.Add(MkLabel($"∠ {a.AngleDeg:F1}°", Lerp(l1s, l1e), "#FF00BCD4", 9));
        }
        foreach (var l in photo.LineAnnotations)
        {
            var ls = I2S(l.StartPoint); var le = I2S(l.EndPoint);
            MasterCanvas.Children.Add(MkLine(ls, le, l.Color, 1.5, 8));
        }
    }

    private Line MkLine(Point s, Point e, string hex, double thick, int z)
    {
        var c  = (Color)ColorConverter.ConvertFromString(hex);
        var ln = new Line { X1=s.X, Y1=s.Y, X2=e.X, Y2=e.Y,
            Stroke = new SolidColorBrush(c), StrokeThickness = thick };
        Panel.SetZIndex(ln, z);
        return ln;
    }

    private TextBlock MkLabel(string text, Point pos, string hex, int z)
    {
        var c  = (Color)ColorConverter.ConvertFromString(hex);
        var tb = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(c),
            FontSize   = 11, FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromArgb(170, 8, 12, 18)),
            Padding    = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(tb, pos.X + 5);
        Canvas.SetTop(tb,  pos.Y - 14);
        Panel.SetZIndex(tb, z + 1);
        return tb;
    }

    // ── Grid ──────────────────────────────────────────────────────────────────────
    // Same behaviour as the 3D viewport grid (MainWindow.DrawGrid), adapted to photos:
    //   calibrated photo   → mm grid anchored to the image (centre stored in IMAGE pixels)
    //   uncalibrated photo → screen-space grid, 20 px cells (centre stored in SCREEN pixels)
    // Centre / "new center" are kept per photo, in memory only. All grid elements use z-index 5.
    private const int GridZ = 5;
    private const double ScreenGridCellPx = 20.0;
    private const double MinGridSpacingPx = 5.0;

    /// <summary>Per-photo grid centre. Null = default (image centre in mm mode, viewport centre otherwise).</summary>
    private sealed class PhotoGridState
    {
        public (Point P, bool InImage)? Center;
        public (Point P, bool InImage)? NewCenter;   // "Set as new center" → used by "Recentre"
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<PhotoViewModel, PhotoGridState> _gridStates = new();
    private bool  _gridDragging;
    private Point _gridDragStart;
    private Point _gridDragCenterStart;

    private static readonly Brush GridScreenThin  = Fr(30, 255, 255, 255);
    private static readonly Brush GridScreenMid   = Fr(55, 255, 255, 255);
    private static readonly Brush GridScreenThick = Fr(80, 255, 255, 255);
    private static readonly Brush GridMmThin      = Fr(35, 255, 255, 255);   // brighter: photos are light
    private static readonly Brush GridMmMid       = Fr(75, 255, 255, 255);
    private static readonly Brush GridMmThick     = Fr(140, 255, 255, 255);
    private static readonly Brush GridCross       = Fr(100, 100, 200, 255);

    private PhotoGridState GridState(PhotoViewModel photo) => _gridStates.GetOrCreateValue(photo);

    private static bool GridInMm(PhotoViewModel photo) => photo.IsNormalized && photo.PixelsPerMm > 0;

    private Point ViewportCentre => new(ViewportHost.ActualWidth / 2.0, ViewportHost.ActualHeight / 2.0);

    /// <summary>Grid centre in screen pixels for the current view.</summary>
    private Point GridCentreScreen(PhotoViewModel photo)
    {
        if (GridState(photo).Center is { } c) return c.InImage ? I2S(c.P) : c.P;
        if (GridInMm(photo) && photo.ImageSource != null)
            return I2S(new Point(photo.ImageSource.PixelWidth / 2.0, photo.ImageSource.PixelHeight / 2.0));
        return ViewportCentre;
    }

    /// <summary>Stores a centre given in screen pixels, in the space the current grid mode is anchored to.</summary>
    private (Point P, bool InImage) GridPointFromScreen(PhotoViewModel photo, Point screen) =>
        GridInMm(photo) ? (S2I(screen), true) : (screen, false);

    private void DrawGrid()
    {
        for (int i = MasterCanvas.Children.Count - 1; i >= 0; i--)
            if (Panel.GetZIndex(MasterCanvas.Children[i]) == GridZ)
                MasterCanvas.Children.RemoveAt(i);

        var photo = Active;
        double vw = ViewportHost.ActualWidth, vh = ViewportHost.ActualHeight;
        if (photo?.ImageSource == null || Vm?.ShowGridOverlay != true || vw < 10 || vh < 10) return;

        var c = GridCentreScreen(photo);
        string label;
        if (GridInMm(photo))
        {
            double pxPerMm = photo.PixelsPerMm * _zoom;
            int stepMm = pxPerMm >= MinGridSpacingPx ? 1 : pxPerMm * 5 >= MinGridSpacingPx ? 5 : 10;
            bool any = pxPerMm * stepMm >= MinGridSpacingPx;
            if (any) DrawGridLines(c, pxPerMm * stepMm, stepMm, vw, vh, GridMmThin, GridMmMid, GridMmThick);
            else DrawGridCrosshair(c, vw, vh);
            label = !any ? "Grid: zoom in to see the mm lines"
                  : stepMm == 1 ? "Grid: 1 / 5 / 10 mm" : stepMm == 5 ? "Grid: 5 / 10 mm" : "Grid: 10 mm";
        }
        else
        {
            DrawGridLines(c, ScreenGridCellPx, 1, vw, vh, GridScreenThin, GridScreenMid, GridScreenThick);
            label = "Grid not in mm: set the scale first (Scale or Reference overlay)";
        }
        AddGridLabel(label, vh);
    }

    /// <summary>
    /// Lines every <paramref name="stepPx"/> screen px, i.e. every <paramref name="stepUnits"/> units (mm or cells),
    /// counted from the centre: every 10 units thick, every 5 medium, others thin, the centre lines blue.
    /// </summary>
    private void DrawGridLines(Point c, double stepPx, int stepUnits, double vw, double vh,
                               Brush thin, Brush mid, Brush thick)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool vertical = pass == 0;
            double centre = vertical ? c.X : c.Y, extent = vertical ? vw : vh;
            int kMin = (int)Math.Ceiling(-centre / stepPx), kMax = (int)Math.Floor((extent - centre) / stepPx);
            for (int k = kMin; k <= kMax; k++)
            {
                long units = (long)k * stepUnits;
                (Brush br, double th) = units == 0 ? (GridCross, 1.5)
                                      : units % 10 == 0 ? (thick, 1.5)
                                      : units % 5 == 0 ? (mid, 1.0) : (thin, 0.5);
                double v = centre + k * stepPx;
                AddGridLine(vertical ? new Point(v, 0) : new Point(0, v),
                            vertical ? new Point(v, vh) : new Point(vw, v), br, th);
            }
        }
    }

    private void DrawGridCrosshair(Point c, double vw, double vh)
    {
        AddGridLine(new Point(c.X, 0), new Point(c.X, vh), GridCross, 1.5);
        AddGridLine(new Point(0, c.Y), new Point(vw, c.Y), GridCross, 1.5);
    }

    private void AddGridLine(Point a, Point b, Brush br, double th)
    {
        var ln = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = br, StrokeThickness = th, IsHitTestVisible = false };
        Panel.SetZIndex(ln, GridZ);
        MasterCanvas.Children.Add(ln);
    }

    private void AddGridLabel(string text, double vh)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD3, 0xDC)),
            Background = new SolidColorBrush(Color.FromArgb(170, 8, 12, 18)), Padding = new Thickness(5, 2, 5, 2),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, 8);
        Canvas.SetTop(tb, vh - 26);
        Panel.SetZIndex(tb, GridZ);
        MasterCanvas.Children.Add(tb);
    }

    private static SolidColorBrush Fr(byte a,byte r,byte g,byte b) { var br=new SolidColorBrush(Color.FromArgb(a,r,g,b)); br.Freeze(); return br; }

    // ── Grid move mode and context menu (same semantics as MainWindow) ────────────
    private bool GridMoveMode => BtnGridMove.IsChecked == true;

    private void GridMove_Changed(object s, RoutedEventArgs e)
    {
        if (GridMoveMode) { CancelLineTool(); CancelAngleTool(); }   // tools resume when move mode is turned off
        else EndGridDrag();
        ViewportHost.Cursor = GridMoveMode ? Cursors.SizeAll : null;
    }

    private void StartGridDrag(Point sp)
    {
        _gridDragging = true;
        _gridDragStart = sp;
        _gridDragCenterStart = GridCentreScreen(Active!);
        ViewportHost.CaptureMouse();
    }

    private void DragGrid(Point sp)
    {
        var photo = Active;
        if (photo == null) return;
        var screen = new Point(_gridDragCenterStart.X + sp.X - _gridDragStart.X, _gridDragCenterStart.Y + sp.Y - _gridDragStart.Y);
        GridState(photo).Center = GridPointFromScreen(photo, screen);
        DrawGrid();
    }

    private void EndGridDrag()
    {
        if (!_gridDragging) return;
        _gridDragging = false;
        ViewportHost.ReleaseMouseCapture();
    }

    private void GridSetAsNewCenter_Click(object s, RoutedEventArgs e)
    {
        if (Active is not { } photo) return;
        var st = GridState(photo);
        st.NewCenter = st.Center;   // MainWindow: _newCenter = _gridCenter (null when still at the default)
    }

    private void GridRecentre_Click(object s, RoutedEventArgs e)
    {
        if (Active is not { } photo) return;
        var st = GridState(photo);
        // MainWindow: back to the saved new center, otherwise to the centre of the viewport
        st.Center = st.NewCenter is { } nc ? nc : GridPointFromScreen(photo, ViewportCentre);
        DrawGrid();
    }

    private void GridReset_Click(object s, RoutedEventArgs e)
    {
        if (Active is not { } photo) return;
        var st = GridState(photo);
        st.Center = null;
        st.NewCenter = null;
        DrawGrid();
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────────
    /// <summary>Image px -> screen px: rotate about the image centre (RotationAngle), then zoom and offset.</summary>
    private Matrix ImageToScreen()
    {
        var m = Matrix.Identity;
        if (Active is { ImageSource: { } img } photo && photo.RotationAngle != 0)
            m.RotateAt(photo.RotationAngle, img.PixelWidth / 2.0, img.PixelHeight / 2.0);
        m.Scale(_zoom, _zoom);
        m.Translate(_offX, _offY);
        return m;
    }

    private Point I2S(Point p) => ImageToScreen().Transform(p);

    private Point S2I(Point p)
    {
        var m = ImageToScreen();
        if (!m.HasInverse) return p;
        m.Invert();
        return m.Transform(p);
    }
    private static Point Lerp(Point a, Point b) => new((a.X+b.X)/2,(a.Y+b.Y)/2);

    // ─────────────────────────────────────────────────────────────────────────────
    //  MOUSE EVENTS
    // ─────────────────────────────────────────────────────────────────────────────

    private void Viewport_MouseWheel(object s, MouseWheelEventArgs e)
    {
        if (Active?.ImageSource == null) return;
        double factor  = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        Point  cursor  = e.GetPosition(MasterCanvas);
        double newZoom = Math.Clamp(_zoom * factor, 0.02, 50.0);
        double ratio   = newZoom / _zoom;
        _offX = cursor.X - (cursor.X - _offX) * ratio;
        _offY = cursor.Y - (cursor.Y - _offY) * ratio;
        _zoom = newZoom;
        FullRedraw();
        e.Handled = true;
    }

    private void Viewport_MouseDown(object s, MouseButtonEventArgs e)
    {
        FocusViewport();
        if (Active?.ImageSource == null) return;
        Point sp = e.GetPosition(MasterCanvas);

        // ── Grid move mode: left-drag moves the grid centre, pan and tools are suspended
        if (GridMoveMode)
        {
            StartGridDrag(sp);
            e.Handled = true;
            return;
        }

        // ── Priority 0: Shift+left-drag pans with any tool (tool clicks in progress are kept)
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            StartPan(sp);
            e.Handled = true;
            return;
        }

        // ── Priority 1: angle phase-2, waiting for P3 ─────────────────────────────
        if (_angleWaiting)
        {
            FinaliseAngle(Active!, S2I(sp));
            e.Handled = true;
            return;
        }

        // ── Priority 2: line-tool waiting for 2nd click ────────────────────────────
        if (_lineWaiting)
        {
            FinaliseLine(Active!, _lineP1, S2I(sp));
            e.Handled = true;
            return;
        }

        // ── Priority 3: drag a profile landmark (snaps to the contour, Alt = free) ──
        if (TryStartLandmarkDrag(sp))
        {
            e.Handled = true;
            return;
        }

        // ── Priority 4: start pan or start first click of tool ────────────────────
        if (Vm!.ActiveTool == PhotogrammetryToolMode.Pan)
        {
            StartPan(sp);
        }
        else
        {
            // 1st click: place P1 and show live preview
            _lineWaiting = true;
            _lineP1      = S2I(sp);
            ShowLinePreview(I2S(_lineP1));
        }
        e.Handled = true;
    }

    /// <summary>Dashed preview from P1 to <paramref name="to"/> plus the P1 dot (re-created after a redraw).</summary>
    private void ShowLinePreview(Point to)
    {
            var colour = Vm!.ActiveTool switch
            {
                PhotogrammetryToolMode.Measure  => Color.FromRgb(0x4C, 0xAF, 0x50),
                PhotogrammetryToolMode.Angle    => Color.FromRgb(0x00, 0xBC, 0xD4),
                PhotogrammetryToolMode.DrawLine => Color.FromRgb(0xFF, 0xCC, 0x00),
                _                               => Color.FromRgb(0x1B, 0x98, 0xE0),
            };
            var p1s = I2S(_lineP1);
            _linePreview = new Line
            {
                X1=p1s.X, Y1=p1s.Y, X2=to.X, Y2=to.Y,
                Stroke = new SolidColorBrush(colour),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            };
            Panel.SetZIndex(_linePreview, 20);
            MasterCanvas.Children.Add(_linePreview);

            // Small dot at P1
            var dot = new Ellipse { Width=6, Height=6, Fill=new SolidColorBrush(colour), Tag="p1dot" };
            Canvas.SetLeft(dot, p1s.X - 3); Canvas.SetTop(dot, p1s.Y - 3);
            Panel.SetZIndex(dot, 21);
            MasterCanvas.Children.Add(dot);
    }

    private void StartPan(Point sp)
    {
        _isPanning = true;
        _panStart  = sp;
        ViewportHost.Cursor = Cursors.SizeAll;
        ViewportHost.CaptureMouse();
    }

    private void StopPan()
    {
        if (!_isPanning) return;
        _isPanning = false;
        ViewportHost.Cursor = GridMoveMode ? Cursors.SizeAll : null;
        ViewportHost.ReleaseMouseCapture();
    }

    private void Viewport_MiddleDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || Active?.ImageSource == null) return;
        FocusViewport();
        StartPan(e.GetPosition(MasterCanvas));
        e.Handled = true;
    }

    private void Viewport_MiddleUp(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        StopPan();
        e.Handled = true;
    }

    private void Viewport_MouseMove(object s, MouseEventArgs e)
    {
        FocusViewport();
        if (Active?.ImageSource == null) return;
        Point sp = e.GetPosition(MasterCanvas);

        if (_gridDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            DragGrid(sp);
            return;
        }

        if (_lmDragId != null && e.LeftButton == MouseButtonState.Pressed && DragLandmark(sp)) return;
        if (!_isPanning && !GridMoveMode && !_lineWaiting && !_angleWaiting)
            ViewportHost.Cursor = LandmarkAt(sp) != null ? Cursors.Hand : null;   // hint: landmarks can be dragged

        // Pan drag
        if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
        {
            _offX += sp.X - _panStart.X;
            _offY += sp.Y - _panStart.Y;
            _panStart = sp;
            FullRedraw();
            if (_lineWaiting) ShowLinePreview(sp); // FullRedraw cleared the in-progress preview
            return;
        }

        // Live preview for line tools (1st click placed)
        if (_lineWaiting && _linePreview != null)
        {
            var p1s = I2S(_lineP1);   // recalculate in case pan happened
            _linePreview.X1 = p1s.X;
            _linePreview.Y1 = p1s.Y;
            _linePreview.X2 = sp.X;
            _linePreview.Y2 = sp.Y;
        }

        // Live preview for angle arm-2
        if (_angleWaiting)
        {
            if (_anglePreviewLine2 == null)
            {
                _anglePreviewLine2 = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 188, 212)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };
                Panel.SetZIndex(_anglePreviewLine2, 20);
                MasterCanvas.Children.Add(_anglePreviewLine2);
            }
            var p2s = I2S(_angleP2);
            _anglePreviewLine2.X1 = p2s.X; _anglePreviewLine2.Y1 = p2s.Y;
            _anglePreviewLine2.X2 = sp.X;  _anglePreviewLine2.Y2 = sp.Y;
        }
    }

    private void Viewport_MouseUp(object s, MouseButtonEventArgs e) { EndLandmarkDrag(); EndGridDrag(); StopPan(); }

    // ─────────────────────────────────────────────────────────────────────────────
    //  TOOL FINALISATION
    // ─────────────────────────────────────────────────────────────────────────────

    private void CancelLineTool()
    {
        _lineWaiting = false;
        if (_linePreview != null) { MasterCanvas.Children.Remove(_linePreview); _linePreview = null; }
        // Remove P1 dot
        for (int i = MasterCanvas.Children.Count-1; i >= 0; i--)
            if (MasterCanvas.Children[i] is Ellipse el && el.Tag as string == "p1dot")
                MasterCanvas.Children.RemoveAt(i);
    }

    private void FinaliseLine(PhotoViewModel photo, Point imgP1, Point imgP2)
    {
        CancelLineTool();

        double dx = imgP2.X - imgP1.X;
        double dy = imgP2.Y - imgP1.Y;
        double dist = Math.Sqrt(dx*dx + dy*dy);
        if (dist < 5) return;   // too short

        switch (Vm!.ActiveTool)
        {
            case PhotogrammetryToolMode.Normalize:
                _pendingScalePixelDist = dist;
                CalibrationInput.Text  = "";
                CalibrationPopup.IsOpen = true;
                Dispatcher.BeginInvoke(() => CalibrationInput.Focus());
                break;

            case PhotogrammetryToolMode.Horizon:
                Vm.SaveUndo("Horizon correction");
                ApplyHorizon(photo, imgP1, imgP2);
                FullRedraw();
                break;

            case PhotogrammetryToolMode.Measure:
                if (!photo.IsNormalized)
                {
                    MessageBox.Show("Calibrate scale first (📏 Scale tool).",
                        "Not calibrated", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
                Vm.SaveUndo("Measurement");
                photo.Measurements.Add(new MeasurementViewModel
                {
                    Name       = $"M{photo.Measurements.Count+1}",
                    StartPoint = imgP1, EndPoint = imgP2,
                    DistanceMm = dist / photo.PixelsPerMm,
                });
                FullRedraw();
                break;

            case PhotogrammetryToolMode.DrawLine:
                Vm.SaveUndo("Line");
                photo.LineAnnotations.Add(new LineAnnotationViewModel
                {
                    StartPoint = imgP1, EndPoint = imgP2, Color = "#FFFFCC00"
                });
                FullRedraw();
                break;

            case PhotogrammetryToolMode.Angle:
                HandleAngleTool(photo, imgP1, imgP2);
                break;
        }
    }

    // ── Angle 3-point ─────────────────────────────────────────────────────────────
    private void HandleAngleTool(PhotoViewModel photo, Point imgP1, Point imgP2)
    {
        _angleP1 = imgP1;
        _angleP2 = imgP2;
        _angleWaiting = true;

        _anglePersistLine1 = MkLine(I2S(imgP1), I2S(imgP2), "#FF00BCD4", 1.5, 18);
        MasterCanvas.Children.Add(_anglePersistLine1);

        // Vertex dot
        var vp  = I2S(imgP2);
        var dot = new Ellipse { Width=6, Height=6, Fill=new SolidColorBrush(Color.FromRgb(0,188,212)), Tag="angleDot" };
        Canvas.SetLeft(dot, vp.X-3); Canvas.SetTop(dot, vp.Y-3);
        Panel.SetZIndex(dot, 19);
        MasterCanvas.Children.Add(dot);
    }

    private void FinaliseAngle(PhotoViewModel photo, Point imgP3)
    {
        double v1x = _angleP1.X - _angleP2.X, v1y = _angleP1.Y - _angleP2.Y;
        double v2x = imgP3.X   - _angleP2.X,  v2y = imgP3.Y   - _angleP2.Y;
        double mag1 = Math.Sqrt(v1x*v1x + v1y*v1y);
        double mag2 = Math.Sqrt(v2x*v2x + v2y*v2y);

        if (mag1 < 1 || mag2 < 1) { CancelAngleTool(); return; }

        double dot  = v1x*v2x + v1y*v2y;
        double cosA = Math.Clamp(dot / (mag1*mag2), -1.0, 1.0);
        double ang  = Math.Acos(cosA) * (180.0/Math.PI);

        Vm?.SaveUndo("Angle");
        photo.AngleAnnotations.Add(new AngleAnnotationViewModel
        {
            Name    = $"A{photo.AngleAnnotations.Count+1}",
            L1Start = _angleP1, L1End = _angleP2,
            L2Start = _angleP2, L2End = imgP3,
            AngleDeg = ang,
        });

        CancelAngleTool();
        FullRedraw();
    }

    private void CancelAngleTool()
    {
        _angleWaiting = false;
        if (_anglePreviewLine2 != null) { MasterCanvas.Children.Remove(_anglePreviewLine2); _anglePreviewLine2 = null; }
        if (_anglePersistLine1 != null) { MasterCanvas.Children.Remove(_anglePersistLine1); _anglePersistLine1 = null; }
        for (int i = MasterCanvas.Children.Count-1; i >= 0; i--)
        {
            var child = MasterCanvas.Children[i];
            if ((child is Ellipse el && el.Tag as string == "angleDot") ||
                (child is TextBlock tb && tb.Tag as string == "angleHint"))
                MasterCanvas.Children.RemoveAt(i);
        }
    }

    // ── Horizon ───────────────────────────────────────────────────────────────────
    private static void ApplyHorizon(PhotoViewModel photo, Point p1, Point p2)
    {
        // p1/p2 are image pixels (S2I removes the rotation): on-screen angle = image angle + current rotation
        double deg  = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * (180.0/Math.PI) + photo.RotationAngle;
        double corr = deg % 180.0;
        if (corr >  90) corr -= 180;
        if (corr < -90) corr += 180;
        photo.RotationAngle = (photo.RotationAngle - corr + 360) % 360;
    }

    // ── CalibrationPopup handlers ─────────────────────────────────────────────────
    private void CalibrationApply_Click(object s, RoutedEventArgs e)
    {
        if (double.TryParse(CalibrationInput.Text.Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out double mm) && mm > 0)
        {
            if (Active != null)
            {
                Vm?.SaveUndo("Scale");
                Active.NormalizeScale(_pendingScalePixelDist, mm);
            }
            CloseCalibration();
            FullRedraw();
        }
        else
        {
            CalibrationInput.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 60, 60));
            CalibrationInput.Focus();
        }
    }

    private void CalibrationCancel_Click(object s, RoutedEventArgs e) => CloseCalibration();

    private void CalibrationInput_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Return) CalibrationApply_Click(s, e);
        if (e.Key == Key.Escape) CloseCalibration();
    }

    private void CloseCalibration()
    {
        CalibrationPopup.IsOpen = false;
        _pendingScalePixelDist  = 0;
        CalibrationInput.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 64, 96));
    }
}
