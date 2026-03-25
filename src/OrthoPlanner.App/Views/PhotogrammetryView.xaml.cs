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
///   screenPt = imagePt * _zoom + (_offX, _offY)
///   imagePt  = (screenPt - (_offX, _offY)) / _zoom
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
    }

    // ── DataContext / ViewModel wiring ────────────────────────────────────────────
    private void OnDataContextChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PhotogrammetryViewModel old) old.PropertyChanged -= OnVmChanged;
        if (e.NewValue is PhotogrammetryViewModel nv)  nv.PropertyChanged  += OnVmChanged;
        FullRedraw();
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotogrammetryViewModel.ActivePhoto))
        {
            CloseCalibration(); CancelLineTool(); CancelAngleTool();
            SubscribePhoto(); FitToViewport(); FullRedraw();
        }
        else if (e.PropertyName == nameof(PhotogrammetryViewModel.ShowGridOverlay))
        {
            DrawGrid();
        }
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
                FullRedraw(); break;
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
    private void DrawGrid()
    {
        for (int i = MasterCanvas.Children.Count - 1; i >= 0; i--)
            if (Panel.GetZIndex(MasterCanvas.Children[i]) == 5)
                MasterCanvas.Children.RemoveAt(i);

        var photo = Active;
        if (photo == null || !Vm!.ShowGridOverlay || !photo.IsNormalized) return;

        double s1 = photo.PixelsPerMm * _zoom;
        double s5  = s1 * 5;
        double s10 = s1 * 10;
        double vw  = ViewportHost.ActualWidth;
        double vh  = ViewportHost.ActualHeight;

        double loopStep;
        if      (s1  >= 5) loopStep = s1;
        else if (s5  >= 5) loopStep = s5;
        else if (s10 >= 5) loopStep = s10;
        else return;

        var b1  = Fr(255,255,255,35); var b5 = Fr(255,255,255,75); var b10 = Fr(255,255,255,140);

        double ox = _offX % loopStep; if (ox < 0) ox += loopStep;
        for (double x = ox; x < vw; x += loopStep)
        {
            (Brush br, double th) = GBrush(x - _offX, s1, s5, s10, b1, b5, b10);
            if (br == null!) continue;
            var ln = new Line { X1=x,Y1=0,X2=x,Y2=vh, Stroke=br, StrokeThickness=th };
            Panel.SetZIndex(ln, 5); MasterCanvas.Children.Add(ln);
        }
        double oy = _offY % loopStep; if (oy < 0) oy += loopStep;
        for (double y = oy; y < vh; y += loopStep)
        {
            (Brush br, double th) = GBrush(y - _offY, s1, s5, s10, b1, b5, b10);
            if (br == null!) continue;
            var ln = new Line { X1=0,Y1=y,X2=vw,Y2=y, Stroke=br, StrokeThickness=th };
            Panel.SetZIndex(ln, 5); MasterCanvas.Children.Add(ln);
        }
    }

    private static SolidColorBrush Fr(byte a,byte r,byte g,byte b) { var br=new SolidColorBrush(Color.FromArgb(a,r,g,b)); br.Freeze(); return br; }
    private static (Brush,double) GBrush(double d,double s1,double s5,double s10,Brush b1,Brush b5,Brush b10)
    {
        double abs=Math.Abs(d); const double T=0.6;
        if(s10>0&&abs%s10<T) return(b10,1.2);
        if(s5>0&&abs%s5<T&&s5>=5) return(b5,0.8);
        if(s1>0&&abs%s1<T&&s1>=5) return(b1,0.5);
        return(null!,0);
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────────
    private Point I2S(Point p) => new(p.X * _zoom + _offX, p.Y * _zoom + _offY);
    private Point S2I(Point p) => new((p.X - _offX) / Math.Max(1e-9, _zoom),
                                      (p.Y - _offY) / Math.Max(1e-9, _zoom));
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
        if (Active?.ImageSource == null) return;
        Point sp = e.GetPosition(MasterCanvas);

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

        // ── Priority 3: start pan or start first click of tool ────────────────────
        if (Vm!.ActiveTool == PhotogrammetryToolMode.Pan)
        {
            _isPanning = true;
            _panStart  = sp;
            ViewportHost.CaptureMouse();
        }
        else
        {
            // 1st click: place P1 and show live preview
            _lineWaiting = true;
            _lineP1      = S2I(sp);

            var colour = Vm.ActiveTool switch
            {
                PhotogrammetryToolMode.Measure  => Color.FromRgb(0x4C, 0xAF, 0x50),
                PhotogrammetryToolMode.Angle    => Color.FromRgb(0x00, 0xBC, 0xD4),
                PhotogrammetryToolMode.DrawLine => Color.FromRgb(0xFF, 0xCC, 0x00),
                _                               => Color.FromRgb(0x1B, 0x98, 0xE0),
            };
            var p1s = I2S(_lineP1);
            _linePreview = new Line
            {
                X1=p1s.X, Y1=p1s.Y, X2=p1s.X, Y2=p1s.Y,
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
        e.Handled = true;
    }

    private void Viewport_MouseMove(object s, MouseEventArgs e)
    {
        if (Active?.ImageSource == null) return;
        Point sp = e.GetPosition(MasterCanvas);

        // Pan drag
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            _offX += sp.X - _panStart.X;
            _offY += sp.Y - _panStart.Y;
            _panStart = sp;
            FullRedraw();
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

    private void Viewport_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ViewportHost.ReleaseMouseCapture();
        }
    }

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
                photo.Measurements.Add(new MeasurementViewModel
                {
                    Name       = $"M{photo.Measurements.Count+1}",
                    StartPoint = imgP1, EndPoint = imgP2,
                    DistanceMm = dist / photo.PixelsPerMm,
                });
                FullRedraw();
                break;

            case PhotogrammetryToolMode.DrawLine:
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
        double deg  = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * (180.0/Math.PI);
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
            Active?.NormalizeScale(_pendingScalePixelDist, mm);
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
