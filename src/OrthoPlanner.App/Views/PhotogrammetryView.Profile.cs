using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OrthoPlanner.App.ViewModels.Photogrammetry;
using OrthoPlanner.Core.Photogrammetry.ProfileMatching;

namespace OrthoPlanner.App.Views;

/// <summary>
/// "Profile analysis": automatic soft-tissue landmarks on the photo profile (editable, snapping to the contour) and
/// facial thirds as vertical distances in the NHP frame. Drawn by FullRedraw through I2S (z 6-11).
/// </summary>
public partial class PhotogrammetryView
{
    private const int ProfileLineZ = 6;
    private const int ThirdsZ = 7;
    private const int LandmarkZ = 10;
    private const double LandmarkHitPx = 9;
    private const double ProfileTailMm = 5;   // profile line continues a little below Me'

    private static readonly Color LmHigh = Colors.White;
    private static readonly Color LmLow = Color.FromRgb(0xFF, 0x9F, 0x43);
    private static readonly Color LmEdited = Color.FromRgb(0x1B, 0x98, 0xE0);
    private static readonly Color ProfileGold = Color.FromRgb(0xFF, 0xD7, 0x00);

    private bool _profileBusy;
    private bool _showThirds = true;
    private string? _profileError;
    private ProfileLandmarkId? _lmDragId;
    private bool _lmDragUndoSaved;

    // ── Panel ─────────────────────────────────────────────────────────────────────
    private void InitProfilePanel()
    {
        KeyDown += ProfileView_KeyDown;
        // Only one floating panel at a time.
        BtnCephOverlay.Checked += (_, _) => ProfilePanel.Visibility = Visibility.Collapsed;
    }

    private void ProfileToggle_Click(object s, RoutedEventArgs e)
    {
        bool open = ProfilePanel.Visibility != Visibility.Visible;
        ProfilePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) BtnCephOverlay.IsChecked = false;
        UpdateProfilePanel();
    }

    private void ProfilePanelClose_Click(object s, RoutedEventArgs e)
    {
        ProfilePanel.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void ProfileView_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ProfilePanel.Visibility != Visibility.Visible) return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        ProfilePanel.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    /// <summary>Why detection cannot run on this photo, or null.</summary>
    private static string? ProfileBlockReason(PhotoViewModel? photo) =>
        photo?.ImageSource == null ? "No photo."
        : !photo.IsNormalized || !photo.IsNhpOriented ? "Set the scale and press NHP first."
        : null;

    private void UpdateProfilePanel()
    {
        var photo = Active;
        BtnProfile.IsEnabled = photo?.ImageSource != null;
        string? block = ProfileBlockReason(photo);
        ProfileReasonText.Text = block ?? "";
        ProfileReasonText.Visibility = block == null ? Visibility.Collapsed : Visibility.Visible;
        ProfileDetectButton.IsEnabled = block == null && !_profileBusy;
        ProfileDetectButton.Content = _profileBusy ? "Detecting…" : "Detect landmarks";
        ProfileErrorText.Text = _profileError ?? "";
        ProfileErrorText.Visibility = _profileError == null ? Visibility.Collapsed : Visibility.Visible;

        var lm = photo?.ProfileLandmarks;
        ProfileResultBlock.Visibility = lm == null ? Visibility.Collapsed : Visibility.Visible;
        if (lm == null || photo == null) return;
        ProfileThirdsList.ItemsSource = lm.Thirds(photo.RotationAngle, photo.PixelsPerMm);
        ProfileLandmarkList.ItemsSource = Enum.GetValues<ProfileLandmarkId>().Select(id =>
            lm.Points.TryGetValue(id, out var p)
                ? $"{p.Label}  {(p.Edited ? "edited" : p.Confidence.ToString())} — {p.Reason}"
                : $"{ProfileLandmarkDetector.Label(id)}  missing").ToList();
        ProfileLandmarkList.ToolTip = string.Join("\n", lm.Diagnostics);
        ProfileNhpWarning.Visibility = photo.IsNhpOriented ? Visibility.Collapsed : Visibility.Visible;
        ProfileShowThirds.IsChecked = _showThirds;
    }

    private void ProfileShowThirds_Changed(object s, RoutedEventArgs e)
    {
        _showThirds = ProfileShowThirds.IsChecked == true;
        FullRedraw();
    }

    // ── Detection ─────────────────────────────────────────────────────────────────
    private async void ProfileDetect_Click(object s, RoutedEventArgs e)
    {
        var photo = Active;
        if (_profileBusy || photo?.ImageSource is not BitmapSource image || ProfileBlockReason(photo) != null) return;
        if (!image.IsFrozen) image = (BitmapSource)image.GetCurrentValueAsFrozen();
        var facing = SelectedFacing;
        double rotation = photo.RotationAngle, ppm = photo.PixelsPerMm;
        var overlay = photo.CephOverlay?.Result;
        var assist = overlay is { Reference: OverlayReference.SkinMesh, HasMatrix: true, Skin.Contour: { } skinContour }
            ? new ProfileSkinAssist(skinContour, overlay.Registration!.XrayToPhotoMatrix) : null;

        _profileBusy = true;
        _profileError = null;
        UpdateProfilePanel();
        try
        {
            var (result, contour) = await Task.Run(() => DetectProfile(image, facing, rotation, ppm, assist));
            if (!result.Success || contour == null)
            {
                _profileError = $"Detection failed: {result.FailureReason}";
                return;
            }
            Vm?.SaveUndo("Detect landmarks");
            photo.ProfileLandmarks = ProfileLandmarksViewModel.FromDetection(
                result, contour.Points.Select(p => new Point(p.X, p.Y)).ToList(), contour.FacingRight);
        }
        catch (Exception ex)
        {
            _profileError = $"Detection failed unexpectedly: {ex.Message}";
        }
        finally
        {
            _profileBusy = false;
            if (ReferenceEquals(photo, Active)) FullRedraw();
            UpdateProfilePanel();
        }
    }

    private static (ProfileLandmarkResult Result, ProfileContour? Contour) DetectProfile(BitmapSource image, FacingOverride facing,
        double rotation, double ppm, ProfileSkinAssist? assist)
    {
        var conv = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 3;
        var rgb = new byte[stride * h];
        conv.CopyPixels(rgb, stride, 0);
        var extraction = ProfileExtractor.ExtractFromPhoto(rgb, w, h, stride, facing);
        if (!extraction.Success)
            return (new ProfileLandmarkResult(false, $"photo profile: {extraction.FailureReason}", Array.Empty<DetectedProfileLandmark>(),
                                              Array.Empty<ProfileLandmarkId>(), extraction.Diagnostics), null);
        var result = ProfileLandmarkDetector.Detect(extraction.Contour!, rotation, rgb, w, h, stride, ppm, assist);
        return (result, extraction.Contour);
    }

    // ── Drawing (called by FullRedraw) ────────────────────────────────────────────
    private void DrawProfileLayer(PhotoViewModel photo)
    {
        if (photo.ProfileLandmarks is not { } lm) return;
        DrawProfileLine(lm, photo.PixelsPerMm);
        if (_showThirds) DrawThirds(lm, photo);
        foreach (var p in lm.Points.Values) DrawLandmark(p, lm.FacingRight);
    }

    // Photo profile, 1 px gold, from G' to a little below Me' (no hair, headband or neck).
    private void DrawProfileLine(ProfileLandmarksViewModel lm, double ppm)
    {
        if (!lm.Points.TryGetValue(ProfileLandmarkId.G, out var g) || !lm.Points.TryGetValue(ProfileLandmarkId.Me, out var me)) return;
        int a = Math.Min(g.ContourIndex, me.ContourIndex), end = Math.Max(g.ContourIndex, me.ContourIndex);
        double tail = 0;
        while (end < lm.Contour.Count - 1 && tail < ProfileTailMm * ppm)
        {
            tail += (lm.Contour[end + 1] - lm.Contour[end]).Length;
            end++;
        }
        if (end - a < 1) return;
        var line = new Polyline
        {
            Points = new PointCollection(Enumerable.Range(a, end - a + 1).Select(i => I2S(lm.Contour[i]))),
            Stroke = new SolidColorBrush(ProfileGold), StrokeThickness = 1.0, IsHitTestVisible = false,
        };
        Panel.SetZIndex(line, ProfileLineZ);
        MasterCanvas.Children.Add(line);
    }

    private void DrawThirds(ProfileLandmarksViewModel lm, PhotoViewModel photo)
    {
        var ids = new[] { ProfileLandmarkId.Tr, ProfileLandmarkId.G, ProfileLandmarkId.N, ProfileLandmarkId.Sn,
                          ProfileLandmarkId.Stm, ProfileLandmarkId.Me };
        var screen = ids.Where(lm.Points.ContainsKey).ToDictionary(id => id, id => I2S(lm.Points[id].Position));
        if (screen.Count == 0) return;
        double front = lm.FacingRight ? lm.Points.Values.Max(p => I2S(p.Position).X) : lm.Points.Values.Min(p => I2S(p.Position).X);
        double dir = lm.FacingRight ? 1 : -1;
        double x1 = front + dir * 40, x2 = front + dir * 110;
        var dash = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255));
        foreach (var (id, p) in screen)
            AddProfileShape(new Line { X1 = p.X - dir * 60, Y1 = p.Y, X2 = x2 + dir * 10, Y2 = p.Y, Stroke = dash,
                                       StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 4, 3 } }, ThirdsZ);

        var rows = lm.Thirds(photo.RotationAngle, photo.PixelsPerMm);
        void Bracket(ProfileLandmarkId a, ProfileLandmarkId b, double x, string label)
        {
            if (!screen.TryGetValue(a, out var pa) || !screen.TryGetValue(b, out var pb)) return;
            var white = new SolidColorBrush(Colors.White);
            AddProfileShape(new Line { X1 = x, Y1 = pa.Y, X2 = x, Y2 = pb.Y, Stroke = white, StrokeThickness = 1 }, ThirdsZ);
            AddProfileShape(new Line { X1 = x - dir * 6, Y1 = pa.Y, X2 = x, Y2 = pa.Y, Stroke = white, StrokeThickness = 1 }, ThirdsZ);
            AddProfileShape(new Line { X1 = x - dir * 6, Y1 = pb.Y, X2 = x, Y2 = pb.Y, Stroke = white, StrokeThickness = 1 }, ThirdsZ);
            var row = rows.FirstOrDefault(r => r.Label.EndsWith(label, StringComparison.Ordinal));
            var tb = new TextBlock
            {
                Text = row?.Mm is double mm ? $"{mm:F1} mm" + (row.Warning.Length > 0 ? " ⚠" : "") : "",
                Foreground = white, FontSize = 11, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(150, 8, 12, 18)), Padding = new Thickness(3, 0, 3, 0),
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, lm.FacingRight ? x + 4 : x - 4 - tb.DesiredSize.Width);
            Canvas.SetTop(tb, (pa.Y + pb.Y) / 2 - tb.DesiredSize.Height / 2);
            Panel.SetZIndex(tb, ThirdsZ);
            MasterCanvas.Children.Add(tb);
        }
        Bracket(ProfileLandmarkId.Tr, ProfileLandmarkId.G, x1, "Tr–G'");
        Bracket(ProfileLandmarkId.G, ProfileLandmarkId.Sn, x1, "G'–Sn");
        Bracket(ProfileLandmarkId.Sn, ProfileLandmarkId.Me, x1, "Sn–Me'");
        Bracket(ProfileLandmarkId.Sn, ProfileLandmarkId.Stm, x2, "Sn–Stm");
        Bracket(ProfileLandmarkId.Stm, ProfileLandmarkId.Me, x2, "Stm–Me'");
    }

    private void DrawLandmark(ProfileLandmarkPoint p, bool facingRight)
    {
        var c = p.Edited ? LmEdited : p.Confidence == LandmarkConfidence.High ? LmHigh : LmLow;
        var s = I2S(p.Position);
        var dot = new Ellipse { Width = 8, Height = 8, Stroke = new SolidColorBrush(c), StrokeThickness = 1.5,
                                Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), IsHitTestVisible = false };
        Canvas.SetLeft(dot, s.X - 4);
        Canvas.SetTop(dot, s.Y - 4);
        Panel.SetZIndex(dot, LandmarkZ);
        MasterCanvas.Children.Add(dot);
        var tb = new TextBlock { Text = p.Label, Foreground = new SolidColorBrush(c), FontSize = 10, FontWeight = FontWeights.SemiBold,
                                 IsHitTestVisible = false };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, facingRight ? s.X - 8 - tb.DesiredSize.Width : s.X + 8);   // label on the face side
        Canvas.SetTop(tb, s.Y - tb.DesiredSize.Height / 2);
        Panel.SetZIndex(tb, LandmarkZ + 1);
        MasterCanvas.Children.Add(tb);
    }

    private void AddProfileShape(Shape shape, int z)
    {
        shape.IsHitTestVisible = false;
        Panel.SetZIndex(shape, z);
        MasterCanvas.Children.Add(shape);
    }

    // ── Editing: drag with snapping, Alt = free, right click = reset ───────────────
    private ProfileLandmarkId? LandmarkAt(Point screen)
    {
        if (Active?.ProfileLandmarks is not { } lm) return null;
        ProfileLandmarkId? best = null;
        double bd = LandmarkHitPx;
        foreach (var p in lm.Points.Values)
        {
            double d = (I2S(p.Position) - screen).Length;
            if (d <= bd) { bd = d; best = p.Id; }
        }
        return best;
    }

    /// <summary>Starts dragging the landmark under the mouse. True when a drag started.</summary>
    private bool TryStartLandmarkDrag(Point screen)
    {
        if (LandmarkAt(screen) is not { } id) return false;
        _lmDragId = id;
        _lmDragUndoSaved = false;
        ViewportHost.CaptureMouse();
        ViewportHost.Cursor = Cursors.Hand;
        return true;
    }

    private bool DragLandmark(Point screen)
    {
        if (_lmDragId is not { } id || Active is not { ProfileLandmarks: { } lm } photo) return false;
        var img = S2I(screen);
        int idx = lm.NearestContourIndex(img);
        var pos = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? img : lm.Contour[idx];   // snap unless Alt
        if (!_lmDragUndoSaved)
        {
            Vm?.SaveUndo($"Move {ProfileLandmarkDetector.Label(id)}");   // once per drag, only when it really moves
            _lmDragUndoSaved = true;
        }
        photo.ProfileLandmarks = lm.WithMoved(id, pos, idx);
        FullRedraw();
        UpdateProfilePanel();
        return true;
    }

    private bool EndLandmarkDrag()
    {
        if (_lmDragId == null) return false;
        _lmDragId = null;
        ViewportHost.ReleaseMouseCapture();
        ViewportHost.Cursor = GridMoveMode ? Cursors.SizeAll : null;
        return true;
    }

    private void Viewport_RightUp(object s, MouseButtonEventArgs e)
    {
        if (Active is not { ProfileLandmarks: { } lm } photo || LandmarkAt(e.GetPosition(MasterCanvas)) is not { } id) return;
        var item = new MenuItem { Header = "Reset to automatic", IsEnabled = lm.Points[id].Edited && lm.Automatic.ContainsKey(id) };
        item.Click += (_, _) =>
        {
            if (photo.ProfileLandmarks is not { } current) return;
            Vm?.SaveUndo($"Reset {ProfileLandmarkDetector.Label(id)}");
            photo.ProfileLandmarks = current.WithReset(id);
            FullRedraw();
            UpdateProfilePanel();
        };
        var menu = new ContextMenu { PlacementTarget = ViewportHost };
        menu.Items.Add(item);
        menu.IsOpen = true;
        e.Handled = true;
    }
}
