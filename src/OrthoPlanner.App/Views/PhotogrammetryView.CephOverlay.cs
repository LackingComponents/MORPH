using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OrthoPlanner.App.ViewModels.Cephalometry;
using OrthoPlanner.App.ViewModels.Photogrammetry;
using OrthoPlanner.Core.Photogrammetry.ProfileMatching;

namespace OrthoPlanner.App.Views;

/// <summary>
/// "Ceph overlay" tool: aligns the lateral radiograph on the active profile photo (CephOverlayService) and draws it
/// under the annotations. Panel only, not a mouse tool. Overlay elements are rebuilt by FullRedraw (z 2-3).
/// </summary>
public partial class PhotogrammetryView
{
    private const int CephImageZ = 2;
    private const int CephProfileZ = 3;
    private const double ScaleChangeTolerance = 0.0005;

    private bool _cephBusy;
    private bool _cephPanelUpdating;
    private string? _cephTransientError;            // refusal of the last click (overlay left unchanged)
    private (PhotoViewModel Photo, string Text)? _cephStatus;
    private CephOverlayState? _cephStateSub;
    private (CephXrayImage Xray, BitmapSource Bitmap)? _cephBitmapCache;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── Wiring called from the main code-behind ───────────────────────────────────
    private void OnCephActivePhotoChanged()
    {
        _cephTransientError = null;
        SubscribeCephState();
        UpdateCephPanel();
    }

    private void SubscribeCephState()
    {
        if (_cephStateSub != null) _cephStateSub.PropertyChanged -= OnCephStateChanged;
        _cephStateSub = Active?.CephOverlay;
        if (_cephStateSub != null) _cephStateSub.PropertyChanged += OnCephStateChanged;
    }

    private void OnCephStateChanged(object? s, PropertyChangedEventArgs e) => FullRedraw();

    // ── Rendering (called by FullRedraw after ClearOverlays) ──────────────────────
    private void DrawCephOverlay(PhotoViewModel photo)
    {
        var state = photo.CephOverlay;
        if (state == null || photo.RotationAngle != 0) return; // rotation is visual only: overlay would be misplaced
        var result = state.Result;

        if (state.IsVisible && result.HasMatrix)
        {
            var m = result.Registration!.XrayToPhotoMatrix;
            var xray = result.Xray!;
            var img = new Image
            {
                Source = XrayBitmap(xray),
                Width = xray.OriginalPixelWidth,
                Height = xray.OriginalPixelHeight,
                Stretch = Stretch.Fill,
                Opacity = Math.Clamp(state.Opacity, 0, 1),
                IsHitTestVisible = false,
                // X-ray px -> photo px (matrix, mirror included) -> screen (zoom + offset)
                RenderTransform = new MatrixTransform(new Matrix(
                    m[0] * _zoom, m[3] * _zoom, m[1] * _zoom, m[4] * _zoom,
                    m[2] * _zoom + _offX, m[5] * _zoom + _offY)),
            };
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);
            Panel.SetZIndex(img, CephImageZ);
            MasterCanvas.Children.Add(img);
        }

        if (state.ShowProfiles)
        {
            AddProfile(result.PhotoRoi, Color.FromRgb(0xFF, 0xD7, 0x00));
            AddProfile(result.XrayRoiInPhoto, Color.FromRgb(0x4C, 0xFF, 0x50));
        }
    }

    private void AddProfile(IReadOnlyList<Point> pts, Color color)
    {
        if (pts.Count < 2) return;
        var line = new Polyline
        {
            Points = new PointCollection(pts.Select(I2S)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        };
        Panel.SetZIndex(line, CephProfileZ);
        MasterCanvas.Children.Add(line);
    }

    /// <summary>Cyan-tinted radiograph (B = G = lum, R = 0.3 lum), built once per X-ray.</summary>
    private BitmapSource XrayBitmap(CephXrayImage xray)
    {
        if (_cephBitmapCache is { } c && ReferenceEquals(c.Xray, xray)) return c.Bitmap;
        int w = xray.OriginalPixelWidth, h = xray.OriginalPixelHeight;
        var buf = new byte[w * h * 4];
        var lum = xray.Luminance;
        for (int i = 0; i < w * h; i++)
        {
            byte v = (byte)Math.Clamp(lum[i] * 255f, 0f, 255f);
            buf[4 * i] = v;
            buf[4 * i + 1] = v;
            buf[4 * i + 2] = (byte)(v * 0.3);
            buf[4 * i + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
        bmp.Freeze();
        _cephBitmapCache = (xray, bmp);
        return bmp;
    }

    // ── Panel ─────────────────────────────────────────────────────────────────────
    private void CephOverlayToggle_Changed(object s, RoutedEventArgs e)
    {
        CephOverlayPanel.Visibility = BtnCephOverlay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateCephPanel();
    }

    private void UpdateCephPanel()
    {
        var photo = Active;
        BtnCephOverlay.IsEnabled = photo?.ImageSource != null;
        if (!BtnCephOverlay.IsEnabled && BtnCephOverlay.IsChecked == true) BtnCephOverlay.IsChecked = false;

        var state = photo?.CephOverlay;
        var result = state?.Result;
        var reg = result?.Registration;

        CephAlignButton.IsEnabled = !_cephBusy && photo != null;
        CephAlignButton.Content = _cephBusy ? "Aligning…" : "Auto-align";

        string? error = _cephTransientError ?? (result == null ? null : reg == null ? result.Message
                                                : reg.Success ? null : reg.FailureReason);
        CephErrorText.Text = error ?? "";
        CephErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;

        FillResultBlock(reg, result);
        var warnings = result?.AllWarnings.ToList() ?? new List<string>();
        if (state != null && photo!.RotationAngle != 0)
            warnings.Insert(0, "The photo has been rotated: overlay hidden. Reset the rotation to 0° to see it.");
        CephWarningsList.ItemsSource = warnings;

        _cephPanelUpdating = true;
        CephDisplayBlock.IsEnabled = state != null;
        CephOpacitySlider.Value = (state?.Opacity ?? 0.5) * 100;
        CephOpacityText.Text = $"{CephOpacitySlider.Value:F0}%";
        CephShowXray.IsChecked = state?.IsVisible ?? true;
        CephShowProfiles.IsChecked = state?.ShowProfiles ?? true;
        _cephPanelUpdating = false;

        CephApplyButton.IsEnabled = !_cephBusy && result?.CanApplyScale == true;
        CephRemoveButton.IsEnabled = !_cephBusy && state != null;
        bool showStatus = _cephStatus is { } st && ReferenceEquals(st.Photo, photo);
        CephStatusText.Text = showStatus ? _cephStatus!.Value.Text : "";
        CephStatusText.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillResultBlock(ProfileRegistrationResult? reg, CephOverlayResult? result)
    {
        var s = reg?.Symmetric;
        CephResultBlock.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        if (reg == null || s == null) return;

        CephScaleText.Text = reg.Success
            ? $"Scale: {F(reg.PhotoPixelsPerMm, "F3")} px/mm  (95% range {F(reg.ScaleCi95LowPxPerMm, "F2")}–{F(reg.ScaleCi95HighPxPerMm, "F2")})"
            : "Scale: not usable (see the message above)";
        CephFitText.Text = $"Fit: RMS {F(s.RmsBest80Mm, "F2")} mm, worst 5% {F(s.P95Mm, "F2")} mm";
        CephAgreeText.Text = $"Directions agree within {F(Math.Abs(reg.ScaleDifferenceAvsBPercent), "F2")}%";
        var z = s.ZoneMedianMm;
        CephZonesText.Text = $"Nose / lips / chin: {F(z[0], "F2")} / {F(z[1], "F2")} / {F(z[2], "F2")} mm";
        string source = result?.Xray?.Scale?.Source ?? "?";
        CephSourceText.Text = $"X-ray scale source: {source}";
        CephSourceText.ToolTip = string.Join("\n", new[] { result?.RulerDiagnostic, $"Computed in {result?.ElapsedMs} ms" }
            .Concat(reg.Diagnostics).Where(t => !string.IsNullOrEmpty(t)));
        CephRotationText.Text = $"Rotation {F(reg.RotationDeg, "F1")}°, mirrored: {(reg.PhotoMirrored ? "yes" : "no")}";
    }

    private static string F(double v, string fmt) => v.ToString(fmt, Inv);

    private FacingOverride SelectedFacing => CephFacingCombo.SelectedIndex switch
    {
        1 => FacingOverride.Left,
        2 => FacingOverride.Right,
        _ => FacingOverride.Auto,
    };

    private async void CephAlign_Click(object s, RoutedEventArgs e)
    {
        var photo = Active;
        if (_cephBusy || photo == null) return;
        _cephTransientError = null;
        var refusal = CephOverlayService.Validate(photo, Vm?.CephXrayProvider?.Invoke(), SelectedFacing, out var input);
        if (refusal != null)
        {
            _cephTransientError = refusal.Message;
            UpdateCephPanel();
            return;
        }

        _cephBusy = true;
        UpdateCephPanel();
        try
        {
            var result = await Task.Run(() => CephOverlayService.Run(input!));
            photo.CephOverlay = new CephOverlayState(result);
        }
        catch (Exception ex)
        {
            _cephTransientError = $"Overlay failed unexpectedly: {ex.Message}";
        }
        finally
        {
            _cephBusy = false;
            if (ReferenceEquals(photo, Active)) { SubscribeCephState(); FullRedraw(); }
            UpdateCephPanel();
        }
    }

    private void CephOpacity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CephOpacityText != null) CephOpacityText.Text = $"{e.NewValue:F0}%";
        if (_cephPanelUpdating || Active?.CephOverlay is not { } state) return;
        state.Opacity = e.NewValue / 100.0;
    }

    private void CephDisplay_Changed(object s, RoutedEventArgs e)
    {
        if (_cephPanelUpdating || Active?.CephOverlay is not { } state) return;
        state.IsVisible = CephShowXray.IsChecked == true;
        state.ShowProfiles = CephShowProfiles.IsChecked == true;
    }

    private void CephApply_Click(object s, RoutedEventArgs e)
    {
        var photo = Active;
        var result = photo?.CephOverlay?.Result;
        if (photo == null || result?.CanApplyScale != true) return;
        double newPx = result.Registration!.PhotoPixelsPerMm;
        var warnings = result.AllWarnings;
        bool changesExisting = photo.IsNormalized && photo.PixelsPerMm > 0 &&
                               Math.Abs(newPx / photo.PixelsPerMm - 1) > ScaleChangeTolerance;

        if (warnings.Count > 0 || changesExisting)
        {
            var lines = new List<string>();
            if (changesExisting)
                lines.Add($"This photo is already calibrated: {F(photo.PixelsPerMm, "F3")} px/mm -> {F(newPx, "F3")} px/mm " +
                          $"({F((newPx / photo.PixelsPerMm - 1) * 100, "+0.00;-0.00")}%).");
            if (warnings.Count > 0)
            {
                lines.Add("The overlay reported these warnings:");
                lines.AddRange(warnings.Select(w => "• " + w));
            }
            lines.Add("");
            lines.Add($"Apply {F(newPx, "F3")} px/mm to this photo?");
            if (!ConfirmApply(string.Join("\n", lines))) return;
        }

        photo.NormalizeScale(newPx, 1.0);
        _cephStatus = (photo, $"Scale set from radiograph overlay: {F(newPx, "F3")} px/mm");
        FullRedraw();
        UpdateCephPanel();
    }

    private void CephRemove_Click(object s, RoutedEventArgs e)
    {
        if (Active == null) return;
        _cephTransientError = null;
        Active.CephOverlay = null;   // an applied scale is NOT undone; the property change redraws and refreshes the panel
    }

    /// <summary>Small Apply / Cancel dialog (MessageBox cannot label its buttons).</summary>
    private bool ConfirmApply(string text)
    {
        var dlg = new Window
        {
            Title = "Apply scale from radiograph overlay",
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x19, 0x20)),
        };
        var apply = new Button { Content = "Apply", Width = 80, Padding = new Thickness(0, 5, 0, 5), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        apply.Click += (_, _) => dlg.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        var body = new StackPanel { Margin = new Thickness(16), MaxWidth = 520 };
        body.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD3, 0xDC)),
            Margin = new Thickness(0, 0, 0, 14),
        });
        body.Children.Add(buttons);
        dlg.Content = body;
        return dlg.ShowDialog() == true;
    }
}
