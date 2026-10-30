using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrthoPlanner.App.ViewModels.Cephalometry;

namespace OrthoPlanner.App.Views;

/// <summary>
/// "X-ray" image source for the Cephalometry view: shows a real lateral cephalogram in the DRR image area.
/// The DRR, its landmarks and measurements are never touched; they are only hidden while X-ray is shown.
/// Landmark/measurement tools are disabled in X-ray mode because they map clicks to CT 3D points,
/// a mapping that does not exist for a radiograph (until ruler calibration is implemented).
/// </summary>
public partial class CephalometryOverlay
{
    private const string XrayToolsDisabledMessage =
        "X-ray mode: landmarks and measurements will be enabled after ruler calibration";

    // Defaults for a radiograph: identity window over the [0,1] luminance, clinical polarity (no invert).
    private static readonly CephViewState DefaultXrayView = new(1.0, 0, 0, 1.0, 0.5, false);

    private readonly record struct CephViewState(double Zoom, double PanX, double PanY,
                                                 double WindowWidth, double WindowCenter, bool Inverted);

    private bool _isXrayMode;
    private bool _syncingSource;
    private bool _syncingInvert;
    private CephXrayImage? _shownXray;
    private CephViewState _drrView;                  // DRR view, stashed while X-ray is shown
    private CephViewState _xrayView = DefaultXrayView; // X-ray view, stashed while DRR is shown
    private bool _xrayNeedsFit = true;
    private bool _drrRenderPending;                  // DRR regenerated while X-ray was shown
    private bool _drrFitPending;
    private readonly Dictionary<FrameworkElement, object?> _toolTipsBeforeXray = new();

    private void InitSourceCombo()
    {
        _syncingSource = true;
        CmbSource.Items.Add("DRR");
        CmbSource.Items.Add("X-ray");
        CmbSource.SelectedIndex = 0;
        _syncingSource = false;
    }

    private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSource) return;
        var source = CmbSource.SelectedIndex == 1 ? CephImageSource.Xray : CephImageSource.Drr;
        var vm = GetMainVm();
        if (vm != null) vm.CephSource = source;
        SetSourceMode(source == CephImageSource.Xray);
    }

    /// <summary>Brings the view in line with MainViewModel.CephXray / CephSource (project open, reset).</summary>
    private void SyncXrayFromVm()
    {
        var vm = GetMainVm();
        if (vm == null) return;
        if (!ReferenceEquals(vm.CephXray, _shownXray))
            ShowXrayImage(vm.CephXray);

        bool xray = vm.CephSource == CephImageSource.Xray;
        _syncingSource = true;
        CmbSource.SelectedIndex = xray ? 1 : 0;
        _syncingSource = false;
        SetSourceMode(xray);
    }

    private void SetSourceMode(bool xray)
    {
        if (xray == _isXrayMode) { UpdateXrayUi(); return; }

        if (xray)
        {
            _drrView = CaptureView();
            if (Btn3DToggle.IsChecked == true) Btn3DToggle.IsChecked = false; // 3D placement is CT-only
            CancelInteractionsForXray();

            _isXrayMode = true;
            ApplyView(_xrayView);
            DrrImageControl.Visibility = Visibility.Collapsed;
            MeasurementsCanvas.Visibility = Visibility.Collapsed;
            LandmarkCanvas.Visibility = Visibility.Collapsed;
            XrayImageControl.Visibility = Visibility.Visible;
            RenderXray();
            UpdateXrayTransform();
            StatusLeft.Text = _shownXray == null
                ? "X-ray mode: click \"Load X-ray...\" to load a lateral cephalogram"
                : XrayToolsDisabledMessage;
        }
        else
        {
            ResetXrayTool();
            _xrayView = CaptureView();
            _isXrayMode = false;
            ApplyView(_drrView);
            XrayImageControl.Visibility = Visibility.Collapsed;
            DrrImageControl.Visibility = Visibility.Visible;
            MeasurementsCanvas.Visibility = Visibility.Visible;
            LandmarkCanvas.Visibility = Visibility.Visible;

            if (_drrRenderPending)
            {
                _drrRenderPending = false;
                RenderDrr();
            }
            if (_drrFitPending)
            {
                _drrFitPending = false;
                FitToViewport();
            }
            else
            {
                UpdateImageTransform();
            }
            UpdateToolStatus();
        }
        UpdateXrayUi();
    }

    /// <summary>Called by GenerateDrrAsync while X-ray is shown: keep the new DRR, render it on return.</summary>
    private void DeferDrrRender(bool resetView)
    {
        _drrRenderPending = true;
        if (resetView)
        {
            _drrView = _drrView with { Zoom = 1.0, PanX = 0, PanY = 0, WindowWidth = 1.0, WindowCenter = 0.5 };
            _drrFitPending = true;
        }
    }

    private void ShowXrayImage(CephXrayImage? image)
    {
        ResetXrayTool();
        _shownXray = image;
        _xrayNeedsFit = true;
        if (!_isXrayMode)
        {
            _xrayView = DefaultXrayView;
            if (image == null) XrayImageControl.Source = null;
            return;
        }

        ApplyView(DefaultXrayView);
        RenderXray();
        UpdateXrayTransform();
    }

    private async void LoadXray_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load lateral cephalogram",
            Filter = "X-ray images (*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.dcm)|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.dcm" +
                     "|DICOM without extension (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var vm = GetMainVm();
        string name = System.IO.Path.GetFileName(dialog.FileName);
        StatusLeft.Text = $"Loading X-ray {name}...";
        BtnLoadXray.IsEnabled = false;
        try
        {
            string path = dialog.FileName;
            var image = await Task.Run(() => CephXrayImage.LoadFile(path));
            ShowXrayImage(image);
            if (vm != null) vm.CephXray = image;

            string spacing = image.PixelSpacingMm is double sx
                ? $"{sx:F3}x{image.PixelSpacingRowMm ?? sx:F3} mm/px from DICOM"
                : "not calibrated";
            string msg = $"X-ray loaded: {name} ({image.OriginalPixelWidth}x{image.OriginalPixelHeight}, {spacing})";
            StatusLeft.Text = $"{msg} -- {XrayToolsDisabledMessage}";
            if (vm != null) vm.StatusText = msg;
        }
        catch (Exception ex)
        {
            string msg = $"X-ray load failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"CephalometryOverlay.LoadXray: {dialog.FileName}: {ex}");
            StatusLeft.Text = msg;
            if (vm != null) vm.StatusText = msg;
        }
        finally
        {
            UpdateXrayUi();
        }
    }

    // ── View state ──────────────────────────────────────────────────────────────

    private CephViewState CaptureView() =>
        new(_zoom, _panX, _panY, _windowWidth, _windowCenter, _inverted);

    private void ApplyView(CephViewState v)
    {
        _zoom = v.Zoom;
        _panX = v.PanX;
        _panY = v.PanY;
        _windowWidth = v.WindowWidth;
        _windowCenter = v.WindowCenter;
        _inverted = v.Inverted;
        _syncingInvert = true;
        ChkInvert.IsChecked = v.Inverted;
        _syncingInvert = false;
    }

    private void CancelInteractionsForXray()
    {
        _toolState.CancelPending();
        _rubberBandEnd = null;
        _isDraggingLandmark = false;
        _draggingDot = null;
        _draggingLandmark = null;
        _isPanning = false;
        _isWlDragging = false;
        ImageCanvas.ReleaseMouseCapture();
        RefreshMeasurementOverlay();
    }

    private void UpdateXrayUi()
    {
        bool x = _isXrayMode;
        BtnLoadXray.IsEnabled = x;
        CmbProjection.IsEnabled = !x;
        foreach (var child in ToolButtonPanel.Children.OfType<Button>())
            SetXrayLocked(child, x);
        SetXrayLocked(BtnResetCeph, x);
        SetXrayLocked(Btn3DToggle, x);
        SetXrayLocked(LandmarkListPanel, x);
        SetXrayLocked(MeasurementListPanel, x);
        SetXrayLocked(AnalysisTabControl, x);
        UpdateXrayToolUi();
        RedrawXrayAnnotations();
    }

    private void SetXrayLocked(FrameworkElement element, bool locked)
    {
        if (locked)
        {
            if (!_toolTipsBeforeXray.ContainsKey(element))
                _toolTipsBeforeXray[element] = element.ToolTip;
            element.IsEnabled = false;
            element.ToolTip = XrayToolsDisabledMessage;
            ToolTipService.SetShowOnDisabled(element, true);
        }
        else
        {
            element.IsEnabled = true;
            if (_toolTipsBeforeXray.Remove(element, out var tip))
                element.ToolTip = tip;
        }
    }

    // ── Rendering / transform ───────────────────────────────────────────────────

    private (double Sx, double Sy) XraySpacing()
    {
        // Uncalibrated images are shown 1 px = 1 unit; DICOM spacing keeps the physical aspect.
        double sx = _shownXray?.PixelSpacingMm ?? 1.0;
        double sy = _shownXray?.PixelSpacingRowMm ?? sx;
        return (sx, sy);
    }

    private void RenderXray()
    {
        var img = _shownXray;
        if (img == null)
        {
            XrayImageControl.Source = null;
            UpdateXrayStatusInfo();
            return;
        }

        int w = img.OriginalPixelWidth, h = img.OriginalPixelHeight;
        float[] src = img.Luminance;
        var px = new byte[w * h];

        double lo = _windowCenter - _windowWidth * 0.5;
        double hi = _windowCenter + _windowWidth * 0.5;
        double range = hi - lo;
        double invRange = range > 0 ? 255.0 / range : 0;

        for (int i = 0; i < src.Length; i++)
        {
            double v = src[i];
            double clamped = v <= lo ? 0 : v >= hi ? 255 : (v - lo) * invRange;
            px[i] = _inverted
                ? (byte)Math.Clamp(255.0 - clamped, 0, 255)
                : (byte)Math.Clamp(clamped, 0, 255);
        }

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w, 0);
        XrayImageControl.Source = bmp;
        XrayImageControl.Width = w;
        XrayImageControl.Height = h;
        UpdateXrayStatusInfo();
    }

    private void FitXrayToViewport()
    {
        var img = _shownXray;
        if (img == null) return;
        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        if (vpW <= 0 || vpH <= 0) return; // retried on the next SizeChanged

        var (sx, sy) = XraySpacing();
        _zoom = Math.Min(vpW / (img.OriginalPixelWidth * sx), vpH / (img.OriginalPixelHeight * sy)) * 0.95;
        _panX = 0;
        _panY = 0;
        _xrayNeedsFit = false;
        UpdateXrayTransform();
    }

    private void UpdateXrayTransform()
    {
        var img = _shownXray;
        if (img == null) { UpdateXrayStatusInfo(); return; }
        if (_xrayNeedsFit)
        {
            // Applies the transform itself once the viewport has a size; otherwise retried on SizeChanged.
            FitXrayToViewport();
            return;
        }

        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        if (vpW <= 0 || vpH <= 0) return;

        var (sx, sy) = XraySpacing();
        double scaleX = _zoom * sx;
        double scaleY = _zoom * sy;
        double left = (vpW - img.OriginalPixelWidth * scaleX) / 2.0 + _panX;
        double top = (vpH - img.OriginalPixelHeight * scaleY) / 2.0 + _panY;

        XrayImageControl.RenderTransform = new ScaleTransform(scaleX, scaleY);
        Canvas.SetLeft(XrayImageControl, left);
        Canvas.SetTop(XrayImageControl, top);
        UpdateXrayStatusInfo();
        RedrawXrayAnnotations();
    }

    private void UpdateXrayStatusInfo()
    {
        var img = _shownXray;
        if (img == null)
        {
            StatusCenter.Text = "X-ray: none loaded";
        }
        else
        {
            string spacing = img.Scale is XrayScale sc
                ? $"{sc.MmPerPixelX:F4}x{sc.MmPerPixelY:F4} mm/px ({sc.Source})"
                : "not calibrated";
            StatusCenter.Text = $"X-ray {img.OriginalPixelWidth}x{img.OriginalPixelHeight}  |  {spacing}  |  {_zoom * 100:F0}%";
        }
        StatusRight.Text = $"W: {_windowWidth:F2}  L: {_windowCenter:F2}";
        WlHudW.Text = $"W  {_windowWidth:F3}";
        WlHudL.Text = $"L  {_windowCenter:F3}";
    }
}
