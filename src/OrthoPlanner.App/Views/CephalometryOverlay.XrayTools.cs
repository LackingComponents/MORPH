using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OrthoPlanner.App.ViewModels.Cephalometry;

namespace OrthoPlanner.App.Views;

/// <summary>
/// X-ray mode tools: ruler calibration (manual or automatic from the tick marks) and 2D distance / angle
/// annotations on the radiograph. Everything is stored in X-ray image pixels on CephXrayImage.Measurements;
/// nothing here touches the DRR landmarks, the DRR measurements or the CT 3D mapping.
/// </summary>
public partial class CephalometryOverlay
{
    private enum XrayTool { None, Calibrate, Distance, Angle }

    private const string XrayNeedsScaleMessage = "Calibrate the ruler first (or load a DICOM with pixel spacing)";
    private const double XrayMinSegmentPx = 5;

    private static readonly SolidColorBrush XrayRulerBrush = Frozen(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly SolidColorBrush XrayDistanceBrush = Frozen(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush XrayAngleBrush = Frozen(Color.FromRgb(0x00, 0xBC, 0xD4));
    private static readonly SolidColorBrush XrayLabelBg = Frozen(Color.FromArgb(170, 8, 12, 18));

    private XrayTool _xrayTool = XrayTool.None;
    private readonly List<Point> _xrayPending = new();   // image pixels
    private Point? _xrayRubber;                           // image pixels
    private Point _xrayCalStart, _xrayCalEnd;
    private TickDetection? _xrayCalDetection;
    private string? _xrayCalDetectionError;
    private bool _xrayCalDetectionDone;

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ── Tool selection ──────────────────────────────────────────────────────────

    private void XrayTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<XrayTool>(tag, out var tool)) return;
        CloseXrayCalibration();
        _xrayPending.Clear();
        _xrayRubber = null;
        _xrayTool = _xrayTool == tool ? XrayTool.None : tool;
        RedrawXrayAnnotations();
        UpdateXrayToolUi();
        StatusLeft.Text = XrayToolPrompt();
    }

    private void XrayClear_Click(object sender, RoutedEventArgs e)
    {
        var img = _shownXray;
        if (img == null || !img.Measurements.HasAnnotations) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            "Remove all X-ray distance and angle annotations? The ruler calibration is kept.",
            "Clear X-ray annotations", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        img.Measurements.ClearAnnotations();
        RedrawXrayAnnotations();
        UpdateXrayToolUi();
        StatusLeft.Text = "X-ray annotations cleared.";
    }

    private string XrayToolPrompt() => _xrayTool switch
    {
        XrayTool.Calibrate => _xrayPending.Count == 0
            ? "Ruler: click the first point on the ruler (Esc to cancel)"
            : "Ruler: click the second point (for Auto, keep the line along the tick marks)",
        XrayTool.Distance => _xrayPending.Count == 0 ? "Distance: click the first point" : "Distance: click the second point -- Esc to cancel",
        XrayTool.Angle => _xrayPending.Count switch
        {
            0 => "Angle: click point A",
            1 => "Angle: click the vertex -- Esc to cancel",
            _ => "Angle: click point B -- Esc to cancel",
        },
        _ => XrayToolsDisabledMessage + ". Use Calibrate ruler, then the X-ray distance / angle tools.",
    };

    /// <summary>Esc: cancels the calibration box or the clicks in progress. Returns true if anything was cancelled.</summary>
    private bool CancelXrayPending()
    {
        if (!_isXrayMode) return false;
        bool any = XrayCalibrationPopup.IsOpen || _xrayPending.Count > 0;
        CloseXrayCalibration();
        if (_xrayTool == XrayTool.Calibrate) _xrayTool = XrayTool.None;
        _xrayPending.Clear();
        _xrayRubber = null;
        RedrawXrayAnnotations();
        UpdateXrayToolUi();
        if (any) StatusLeft.Text = XrayToolPrompt();
        return any;
    }

    /// <summary>Drops in-progress clicks and the active tool (leaving X-ray mode or changing image).</summary>
    private void ResetXrayTool()
    {
        CloseXrayCalibration();
        _xrayTool = XrayTool.None;
        _xrayPending.Clear();
        _xrayRubber = null;
    }

    // ── Mouse ───────────────────────────────────────────────────────────────────

    private void HandleXrayLeftClick(Point canvasPos)
    {
        var img = _shownXray;
        if (img == null) { StatusLeft.Text = "X-ray mode: click \"Load X-ray...\" to load a lateral cephalogram"; return; }
        if (XrayCalibrationPopup.IsOpen) return;
        if (_xrayTool == XrayTool.None) { StatusLeft.Text = XrayToolPrompt(); return; }
        if (ScreenToXrayImage(canvasPos) is not Point p) return;
        if (p.X < 0 || p.Y < 0 || p.X >= img.OriginalPixelWidth || p.Y >= img.OriginalPixelHeight) return;

        _xrayPending.Add(p);
        int needed = _xrayTool == XrayTool.Angle ? 3 : 2;
        if (_xrayPending.Count >= needed)
        {
            var pts = _xrayPending.ToArray();
            _xrayPending.Clear();
            _xrayRubber = null;
            CompleteXrayTool(img, pts);
        }
        RedrawXrayAnnotations();
        UpdateXrayToolUi();
        if (!XrayCalibrationPopup.IsOpen) StatusLeft.Text = XrayToolPrompt();
    }

    private void UpdateXrayRubberBand(Point canvasPos)
    {
        if (_xrayPending.Count == 0) return;
        _xrayRubber = ScreenToXrayImage(canvasPos);
        RedrawXrayAnnotations();
    }

    private void CompleteXrayTool(CephXrayImage img, Point[] pts)
    {
        switch (_xrayTool)
        {
            case XrayTool.Calibrate:
                if ((pts[1] - pts[0]).Length < XrayMinSegmentPx) { StatusLeft.Text = "Ruler segment too short."; return; }
                OpenXrayCalibration(pts[0], pts[1]);
                break;
            case XrayTool.Distance:
                if ((pts[1] - pts[0]).Length < XrayMinSegmentPx) { StatusLeft.Text = "Distance too short, ignored."; return; }
                img.Measurements.Distances.Add(new XrayDistanceAnnotation(pts[0], pts[1]));
                break;
            case XrayTool.Angle:
                if ((pts[0] - pts[1]).Length < 1 || (pts[2] - pts[1]).Length < 1) { StatusLeft.Text = "Angle arm too short, ignored."; return; }
                img.Measurements.Angles.Add(new XrayAngleAnnotation(pts[0], pts[1], pts[2]));
                break;
        }
    }

    // ── Calibration box ─────────────────────────────────────────────────────────

    private void OpenXrayCalibration(Point start, Point end)
    {
        _xrayCalStart = start;
        _xrayCalEnd = end;
        _xrayCalDetection = null;
        _xrayCalDetectionError = null;
        _xrayCalDetectionDone = false;
        XrayCalMmInput.Text = "";
        XrayCalTickInput.Text = "1";
        RbXrayCalManual.IsChecked = true;
        UpdateXrayCalResult();
        XrayCalibrationPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => XrayCalMmInput.Focus());
        StatusLeft.Text = "Ruler: enter the distance in mm (Enter applies, Esc cancels), or choose Auto from ticks";
    }

    private void CloseXrayCalibration()
    {
        if (XrayCalibrationPopup.IsOpen) XrayCalibrationPopup.IsOpen = false;
    }

    private void XrayCalMode_Changed(object sender, RoutedEventArgs e)
    {
        // Also raised by InitializeComponent (XAML IsChecked="True") before the other controls exist.
        if (XrayCalibrationPopup?.IsOpen != true || XrayCalResult == null) return;
        if (RbXrayCalAuto.IsChecked == true && !_xrayCalDetectionDone)
            RunTickDetection();
        UpdateXrayCalResult();
        Dispatcher.BeginInvoke(() => (RbXrayCalAuto.IsChecked == true ? XrayCalTickInput : XrayCalMmInput).Focus());
    }

    private void RunTickDetection()
    {
        _xrayCalDetectionDone = true;
        var img = _shownXray;
        if (img == null) { _xrayCalDetectionError = "No X-ray loaded."; return; }
        try
        {
            _xrayCalDetection = RulerTickDetector.Detect(img.Luminance, img.OriginalPixelWidth, img.OriginalPixelHeight,
                _xrayCalStart.X, _xrayCalStart.Y, _xrayCalEnd.X, _xrayCalEnd.Y);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _xrayCalDetectionError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"CephalometryOverlay.RulerTickDetector: {ex.Message}");
        }
    }

    private void XrayCalInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Also raised by InitializeComponent (XAML default Text="1") before XrayCalResult exists.
        if (XrayCalResult == null || XrayCalibrationPopup?.IsOpen != true) return;
        UpdateXrayCalResult();
    }

    private void XrayCalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) { XrayCalApply_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelXrayPending(); e.Handled = true; }
    }

    private void XrayCalCancel_Click(object sender, RoutedEventArgs e) => CancelXrayPending();

    /// <summary>Proposed mm per image pixel, or null with the reason shown in the box.</summary>
    private double? ProposedXrayMmPerPixel(out string text)
    {
        if (RbXrayCalAuto.IsChecked == true)
        {
            if (_xrayCalDetection == null)
            {
                text = _xrayCalDetectionError ?? "Detecting...";
                return null;
            }
            if (!TryParseMm(XrayCalTickInput.Text, out double mmPerTick))
            {
                text = $"{_xrayCalDetection.PeriodPx:F2} px per tick. Enter a valid \"mm per tick\".";
                return null;
            }
            double pxPerMm = _xrayCalDetection.PeriodPx / mmPerTick;
            text = $"Found {_xrayCalDetection.PeriodPx:F2} px per tick ({_xrayCalDetection.SampleCount} px analysed, " +
                   $"peak {_xrayCalDetection.PeakToMedian:F0}x the median) = {pxPerMm:F2} px/mm. Apply to confirm.";
            return mmPerTick / _xrayCalDetection.PeriodPx;
        }

        double pixels = (_xrayCalEnd - _xrayCalStart).Length;
        if (!TryParseMm(XrayCalMmInput.Text, out double mm))
        {
            text = $"Line length {pixels:F1} px. Enter its real length in mm.";
            return null;
        }
        text = $"{pixels:F1} px / {mm:G} mm = {pixels / mm:F2} px/mm";
        return mm / pixels;
    }

    private static bool TryParseMm(string text, out double value)
        => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value) && value > 0;

    private void UpdateXrayCalResult()
    {
        double? proposed = ProposedXrayMmPerPixel(out string text);
        XrayCalResult.Text = text;
        XrayCalApply.IsEnabled = proposed.HasValue;
    }

    private void XrayCalApply_Click(object sender, RoutedEventArgs e)
    {
        var img = _shownXray;
        double? proposed = ProposedXrayMmPerPixel(out string text);
        if (img == null || proposed is not double mmPerPixel)
        {
            XrayCalResult.Text = text;
            return;
        }

        img.Measurements.SetRuler(mmPerPixel, _xrayCalStart, _xrayCalEnd);
        string method = RbXrayCalAuto.IsChecked == true ? "auto from ticks" : "manual";
        string msg = $"X-ray ruler calibrated ({method}): {1.0 / mmPerPixel:F2} px/mm";
        CloseXrayCalibration();
        _xrayTool = XrayTool.None;
        RedrawXrayAnnotations();
        UpdateXrayToolUi();
        StatusLeft.Text = msg;
        var vm = GetMainVm();
        if (vm != null) vm.StatusText = msg;
    }

    // ── UI state ────────────────────────────────────────────────────────────────

    private void UpdateXrayToolUi()
    {
        bool show = _isXrayMode;
        XrayToolPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        XrayScaleHud.Visibility = show && _shownXray != null ? Visibility.Visible : Visibility.Collapsed;
        XrayAnnotationCanvas.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        var img = _shownXray;
        bool hasScale = img?.Scale != null;
        BtnXrayCalibrate.IsEnabled = img != null;
        SetXrayToolEnabled(BtnXrayDistance, hasScale, "Distance in mm between two points on the X-ray");
        SetXrayToolEnabled(BtnXrayAngle, hasScale, "Angle in degrees between three points (vertex in the middle)");
        BtnXrayClear.IsEnabled = img?.Measurements.HasAnnotations == true;

        foreach (var (btn, tool) in new[] { (BtnXrayCalibrate, XrayTool.Calibrate), (BtnXrayDistance, XrayTool.Distance), (BtnXrayAngle, XrayTool.Angle) })
        {
            bool active = _xrayTool == tool;
            btn.Background = new SolidColorBrush(active ? Color.FromRgb(0x2A, 0x4A, 0x6B) : Color.FromRgb(0x1E, 0x2D, 0x3D));
            btn.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)) : TextWhite;
        }

        UpdateXrayScaleHud();
    }

    private static void SetXrayToolEnabled(Button btn, bool enabled, string tip)
    {
        btn.IsEnabled = enabled;
        btn.ToolTip = enabled ? tip : XrayNeedsScaleMessage;
        ToolTipService.SetShowOnDisabled(btn, true);
    }

    private void UpdateXrayScaleHud()
    {
        var img = _shownXray;
        if (img?.Scale is not XrayScale scale)
        {
            XrayScaleText.Text = "Scale: not calibrated";
            return;
        }
        string source = scale.Source == XrayScale.SourceRuler ? "ruler" : "DICOM";
        string text = scale.MmPerPixelX == scale.MmPerPixelY
            ? $"Scale: {1.0 / scale.MmPerPixelX:F2} px/mm ({source})"
            : $"Scale: {1.0 / scale.MmPerPixelX:F2} x {1.0 / scale.MmPerPixelY:F2} px/mm ({source})";
        if (scale.Source == XrayScale.SourceRuler && img.PixelSpacingMm is double dicom)
            text += $"  [DICOM {1.0 / dicom:F2}]";
        XrayScaleText.Text = text;
    }

    // ── Coordinates ─────────────────────────────────────────────────────────────

    /// <summary>Current on-screen placement of the X-ray: top-left corner and screen px per image px.</summary>
    private (double Left, double Top, double ScaleX, double ScaleY)? XrayLayout()
    {
        var img = _shownXray;
        double vpW = ViewportBorder.ActualWidth, vpH = ViewportBorder.ActualHeight;
        if (img == null || vpW <= 0 || vpH <= 0) return null;
        var (sx, sy) = XraySpacing();
        double scaleX = _zoom * sx, scaleY = _zoom * sy;
        double left = (vpW - img.OriginalPixelWidth * scaleX) / 2.0 + _panX;
        double top = (vpH - img.OriginalPixelHeight * scaleY) / 2.0 + _panY;
        return (left, top, scaleX, scaleY);
    }

    private Point? ScreenToXrayImage(Point screen)
        => XrayLayout() is var (l, t, sx, sy) ? new Point((screen.X - l) / sx, (screen.Y - t) / sy) : null;

    private Point XrayImageToScreen(Point image, (double Left, double Top, double ScaleX, double ScaleY) layout)
        => new(image.X * layout.ScaleX + layout.Left, image.Y * layout.ScaleY + layout.Top);

    // ── Drawing ─────────────────────────────────────────────────────────────────

    private void RedrawXrayAnnotations()
    {
        XrayAnnotationCanvas.Children.Clear();
        var img = _shownXray;
        if (!_isXrayMode || img == null || XrayLayout() is not { } layout) return;
        var scale = img.Scale;
        Point S(Point p) => XrayImageToScreen(p, layout);

        var m = img.Measurements;
        if (m.RulerStart is Point rs && m.RulerEnd is Point re && m.RulerMmPerPixel != null)
        {
            AddXrayLine(S(rs), S(re), XrayRulerBrush, dashed: true);
            AddXrayLabel("ruler", S(re), XrayRulerBrush);
        }

        foreach (var d in m.Distances)
        {
            AddXrayLine(S(d.Start), S(d.End), XrayDistanceBrush, dashed: false);
            AddXrayLabel(scale is XrayScale s ? $"{s.DistanceMm(d.Start, d.End):F1} mm" : "? mm",
                         Mid(S(d.Start), S(d.End)), XrayDistanceBrush);
        }

        foreach (var a in m.Angles)
        {
            AddXrayLine(S(a.A), S(a.Vertex), XrayAngleBrush, dashed: false);
            AddXrayLine(S(a.Vertex), S(a.B), XrayAngleBrush, dashed: false);
            double deg = scale is XrayScale s ? s.AngleDeg(a.A, a.Vertex, a.B) : double.NaN;
            AddXrayLabel(double.IsFinite(deg) ? $"{deg:F1}°" : "?°", S(a.Vertex), XrayAngleBrush);
        }

        // In-progress clicks and rubber band
        var pendingBrush = _xrayTool switch
        {
            XrayTool.Calibrate => XrayRulerBrush,
            XrayTool.Angle => XrayAngleBrush,
            _ => XrayDistanceBrush,
        };
        for (int i = 0; i < _xrayPending.Count; i++)
        {
            AddXrayDot(S(_xrayPending[i]), pendingBrush);
            if (i > 0) AddXrayLine(S(_xrayPending[i - 1]), S(_xrayPending[i]), pendingBrush, dashed: false);
        }
        if (_xrayPending.Count > 0 && _xrayRubber is Point rb)
            AddXrayLine(S(_xrayPending[^1]), S(rb), pendingBrush, dashed: true);
        if (XrayCalibrationPopup.IsOpen)
            AddXrayLine(S(_xrayCalStart), S(_xrayCalEnd), XrayRulerBrush, dashed: true);
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private void AddXrayLine(Point a, Point b, Brush brush, bool dashed)
    {
        var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = 1.5 };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 5, 3 };
        XrayAnnotationCanvas.Children.Add(line);
    }

    private void AddXrayDot(Point p, Brush brush)
    {
        var dot = new Ellipse { Width = 6, Height = 6, Fill = brush };
        Canvas.SetLeft(dot, p.X - 3);
        Canvas.SetTop(dot, p.Y - 3);
        XrayAnnotationCanvas.Children.Add(dot);
    }

    private void AddXrayLabel(string text, Point at, Brush brush)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = brush, Background = XrayLabelBg,
            FontSize = 11, FontWeight = FontWeights.SemiBold, Padding = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(tb, at.X + 6);
        Canvas.SetTop(tb, at.Y - 16);
        XrayAnnotationCanvas.Children.Add(tb);
    }
}
