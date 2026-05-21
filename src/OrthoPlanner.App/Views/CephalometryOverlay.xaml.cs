using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Infrastructure;
using OrthoPlanner.App.ViewModels;
using HelixToolkit.Wpf.SharpDX;
using HxGeom = HelixToolkit.SharpDX;

namespace OrthoPlanner.App.Views;

public partial class CephalometryOverlay : UserControl
{
    // ÔöÇÔöÇ Reusable Brushes ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private static readonly SolidColorBrush CyanBrush = new(Color.FromRgb(0x00, 0xE5, 0xFF));
    private static readonly SolidColorBrush ActiveGreenBrush = new(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly SolidColorBrush GrayDotBrush = new(Color.FromRgb(0x3E, 0x3E, 0x42));
    private static readonly SolidColorBrush ActiveRowBg = new(Color.FromRgb(0x28, 0x35, 0x45));
    private static readonly SolidColorBrush TextWhite = new(Color.FromRgb(0xD0, 0xD8, 0xE0));
    private static readonly SolidColorBrush TextActiveBlue = new(Color.FromRgb(0x6B, 0x8D, 0xAF));
    private static readonly SolidColorBrush SubduedText = new(Color.FromRgb(0x6E, 0x7F, 0x90));
    private static readonly SolidColorBrush StrokeCyan = new(Color.FromRgb(0x00, 0x80, 0x90));

    // ÔöÇÔöÇ Volume & DRR ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private VolumeData? _volume;
    private DrrResult? _lateralDrr;
    private DrrResult? _paDrr;
    private DrrResult? _activeDrr;

    // ÔöÇÔöÇ Window/Level ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private double _windowWidth  = 1.140;   // user-preferred default
    private double _windowCenter = 0.584;   // user-preferred default
    private bool _inverted = true;

    // ÔöÇÔöÇ Zoom / Pan ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private double _zoom = 1.0;
    private double _panX, _panY;
    private bool _isPanning;
    private System.Windows.Point _panDragOrigin;
    private double _panDragStartX, _panDragStartY;
    private bool _isWlDragging;
    private System.Windows.Point _wlDragOrigin;
    private double _wlDragStartWw, _wlDragStartWc;

    // ÔöÇÔöÇ Landmarks ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private List<CephalometricLandmark>? _landmarks;
    private CephalometricLandmark? _activeLandmark;
    private Ellipse? _draggingDot;
    private CephalometricLandmark? _draggingLandmark;
    private bool _isDraggingLandmark;

    // ── 3D Mode ───────────────────────────────────────────────────────────────
    private bool _is3DMode;
    private readonly List<MeshGeometryModel3D> _landmarkSpheres3D = new();
    private Vector3? _pending3DHit;
    private System.Windows.Point _mouseDown3DScreenPos;

    // ÔöÇÔöÇ 3D Measurements ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private sealed class Meas3D
    {
        public CephTool Tool;
        public string Label = "";
        public List<Vector3> Pts = new();
        public double Value;
        public string Unit = "";
        public System.Windows.Media.Color Color;
    }
    private readonly List<Meas3D> _measurements3D = new();
    private readonly List<Element3D> _meas3DVisuals = new();
    private readonly List<Vector3> _pending3DPts = new();
    private readonly List<MeshGeometryModel3D> _pending3DSpheres = new();

    // ÔöÇÔöÇ Measurement Tools ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private readonly CephToolState _toolState = new();
    private CephPoint? _rubberBandEnd;

    // ÔöÇÔöÇ Generation ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ
    private CancellationTokenSource? _genCts;
    private bool _initialized;

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Constructor
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    public CephalometryOverlay()
    {
        InitializeComponent();

        CmbProjection.Items.Add("Lateral");
        CmbProjection.Items.Add("PA");
        CmbProjection.SelectedIndex = 0;

        ViewportBorder.SizeChanged += (_, _) => UpdateImageTransform();

        // Keyboard shortcuts
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Btn3DToggle.IsChecked = !(Btn3DToggle.IsChecked == true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape &&
                     (_toolState.PendingPoints.Count > 0 || _pending3DPts.Count > 0))
            {
                _toolState.CancelPending();
                _rubberBandEnd = null;
                ClearPending3DSpheres();
                RefreshMeasurementOverlay();
                UpdateToolStatus();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _toolState.SelectedMeasurement != null)
            {
                var sel = _toolState.SelectedMeasurement;
                // also remove from 3D list if present
                var m3 = _measurements3D.FirstOrDefault(m => m.Label == sel.Label);
                if (m3 != null) { _measurements3D.Remove(m3); Refresh3DMeasurements(); }
                _toolState.Measurements.Remove(sel);
                _toolState.SelectedMeasurement = null;
                RefreshMeasurementOverlay();
                RefreshMeasurementPanel();
                e.Handled = true;
            }
        };

        // Default tool highlight
        UpdateToolButtonHighlights();

        // 3D click detection: ViewportGrid has Background="Transparent" so it is hit-test
        // visible. Clicks are tunnelled here and forwarded to the underlying shared
        // MainViewport via FindHits in the handlers below.
        ViewportGrid.PreviewMouseLeftButtonUp += OnViewport3DMouseLeftButtonUp;
        ViewportGrid.PreviewMouseLeftButtonDown += Viewport3D_PreviewMouseLeftButtonDown;
        IsVisibleChanged += OnCephVisibilityChanged;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Public API
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    public void SetVolume(VolumeData volume)
    {
        if (volume == _volume && _initialized) return;
        _volume = volume;
        _lateralDrr = null;
        _paDrr = null;
        _activeDrr = null;
        _landmarks = CephalometricLandmarkDefinitions.GetAll();
        _initialized = true;

        BuildLandmarkSidebar();
        _ = GenerateDrrAsync(lateral: true);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // DRR Generation
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private async Task GenerateDrrAsync(bool lateral)
    {
        if (_volume == null) return;

        _genCts?.Cancel();
        var oldCts = _genCts;
        _genCts = new CancellationTokenSource();
        var ct = _genCts.Token;
        oldCts?.Dispose();

        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingText.Text = lateral ? "Generating lateral DRR..." : "Generating PA DRR...";
        StatusLeft.Text = "Generating...";

        try
        {
            DrrResult drr;
            if (lateral)
            {
                _lateralDrr ??= await Task.Run(() => DrrGenerator.GenerateLateral(_volume, ct), ct);
                drr = _lateralDrr;
            }
            else
            {
                _paDrr ??= await Task.Run(() => DrrGenerator.GeneratePA(_volume, ct), ct);
                drr = _paDrr;
            }

            _activeDrr = drr;
            _windowWidth = 1.0;
            _windowCenter = 0.5;
            _zoom = 1.0;
            _panX = 0;
            _panY = 0;

            RenderDrr();
            FitToViewport();

            StatusLeft.Text = "Ready -- Left-click to place landmark, right-drag for W/L";
            UpdateStatusInfo();
        }
        catch (OperationCanceledException) { StatusLeft.Text = "Cancelled"; }
        catch (Exception ex)
        {
            StatusLeft.Text = "Generation failed";
            MessageBox.Show(ex.Message, "DRR Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // DRR Rendering
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void RenderDrr()
    {
        if (_activeDrr == null) return;

        int w = _activeDrr.Width;
        int h = _activeDrr.Height;
        float[] src = _activeDrr.Pixels;
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
                ? (byte)Math.Clamp(clamped, 0, 255)
                : (byte)Math.Clamp(255.0 - clamped, 0, 255);
        }

        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Gray8, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w, 0);
        DrrImageControl.Source = bmp;
        DrrImageControl.Width = w;
        DrrImageControl.Height = h;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Fit / Transform
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void FitToViewport()
    {
        if (_activeDrr == null) return;
        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        if (vpW <= 0 || vpH <= 0) return;

        double physW = _activeDrr.Width * _activeDrr.SpacingX;
        double physH = _activeDrr.Height * _activeDrr.SpacingY;

        _zoom = Math.Min(vpW / physW, vpH / physH) * 0.95;
        _panX = 0;
        _panY = 0;
        UpdateImageTransform();
    }

    private void UpdateImageTransform()
    {
        if (_activeDrr == null) return;
        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        if (vpW <= 0 || vpH <= 0) return;

        double scaleX = _zoom * _activeDrr.SpacingX;
        double scaleY = _zoom * _activeDrr.SpacingY;
        double dispW = _activeDrr.Width * scaleX;
        double dispH = _activeDrr.Height * scaleY;

        double left = (vpW - dispW) / 2.0 + _panX;
        double top = (vpH - dispH) / 2.0 + _panY;

        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(scaleX, scaleY));
        DrrImageControl.RenderTransform = tg;
        MeasurementsCanvas.RenderTransform = tg;
        LandmarkCanvas.RenderTransform = tg;

        Canvas.SetLeft(DrrImageControl, left);
        Canvas.SetTop(DrrImageControl, top);
        Canvas.SetLeft(MeasurementsCanvas, left);
        Canvas.SetTop(MeasurementsCanvas, top);
        Canvas.SetLeft(LandmarkCanvas, left);
        Canvas.SetTop(LandmarkCanvas, top);

        UpdateStatusInfo();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Mouse 2D: Left-click = place / drag landmark
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ImageCanvas);

        if (_toolState.ActiveTool == CephTool.Select)
        {
            // Select mode: landmark drag or placement
            if (e.OriginalSource is Ellipse dot && dot.Tag is CephalometricLandmark lm)
            {
                _isDraggingLandmark = true;
                _draggingDot = dot;
                _draggingLandmark = lm;
                ImageCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Check if clicking on a measurement overlay for selection
            if (e.OriginalSource is FrameworkElement fe && fe.Tag is CephMeasurement clickedM)
            {
                _toolState.SelectedMeasurement = clickedM;
                RefreshMeasurementOverlay();
                RefreshMeasurementPanel();
                e.Handled = true;
                return;
            }

            if (_activeLandmark != null && _activeDrr != null)
            {
                var imgPos = ScreenToImage(pos);
                if (imgPos.X >= 0 && imgPos.X < _activeDrr.Width &&
                    imgPos.Y >= 0 && imgPos.Y < _activeDrr.Height)
                {
                    _activeLandmark.Position = (imgPos.X, imgPos.Y);
                    // Project 2D click to 3D midplane so both views stay in sync
                    _activeLandmark.Position3D = Project2DTo3D(imgPos.X, imgPos.Y) is Vector3 v
                        ? ((double)v.X, (double)v.Y, (double)v.Z)
                        : null;
                    RefreshLandmarkOverlay();
                    if (_is3DMode) Refresh3DLandmarks();
                    UpdateLandmarkSidebarItem(_activeLandmark);
                    AdvanceToNextLandmark();
                    e.Handled = true;
                }
            }
        }
        else
        {
            // Measurement tool click
            if (_activeDrr == null) return;
            var imgPos = ScreenToImage(pos);
            if (imgPos.X >= 0 && imgPos.X < _activeDrr.Width &&
                imgPos.Y >= 0 && imgPos.Y < _activeDrr.Height)
            {
                HandleToolClick(new CephPoint(imgPos.X, imgPos.Y));
                e.Handled = true;
            }
        }
    }

    private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingLandmark && _draggingLandmark != null)
        {
            _isDraggingLandmark = false;
            _draggingDot = null;
            _draggingLandmark = null;
            ImageCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Mouse 2D: Middle-click = Pan
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ImageCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panDragOrigin = e.GetPosition(ImageCanvas);
            _panDragStartX = _panX;
            _panDragStartY = _panY;
            ImageCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ImageCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            ImageCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Mouse 2D: Right-click = W/L or delete landmark
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ImageCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Ellipse dot && dot.Tag is CephalometricLandmark lm)
        {
            lm.Position = null;
            lm.Position3D = null;
            RefreshLandmarkOverlay();
            if (_is3DMode) Refresh3DLandmarks();
            UpdateLandmarkSidebarItem(lm);
            e.Handled = true;
            return;
        }

        _isWlDragging = true;
        _wlDragOrigin = e.GetPosition(ImageCanvas);
        _wlDragStartWw = _windowWidth;
        _wlDragStartWc = _windowCenter;
        ImageCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ImageCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isWlDragging)
        {
            _isWlDragging = false;
            ImageCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingLandmark && _draggingLandmark != null)
        {
            var imgPos = ScreenToImage(e.GetPosition(ImageCanvas));
            _draggingLandmark.Position = (imgPos.X, imgPos.Y);
            // Keep 3D position in sync during drag
            _draggingLandmark.Position3D = Project2DTo3D(imgPos.X, imgPos.Y) is Vector3 v
                ? ((double)v.X, (double)v.Y, (double)v.Z)
                : null;
            RefreshLandmarkOverlay();
            if (_is3DMode) Refresh3DLandmarks();
            UpdateLandmarkSidebarItem(_draggingLandmark);
            return;
        }

        if (_isPanning)
        {
            var pos = e.GetPosition(ImageCanvas);
            _panX = _panDragStartX + (pos.X - _panDragOrigin.X);
            _panY = _panDragStartY + (pos.Y - _panDragOrigin.Y);
            UpdateImageTransform();
        }
        else if (_isWlDragging)
        {
            var pos = e.GetPosition(ImageCanvas);
            double dx = pos.X - _wlDragOrigin.X;
            double dy = pos.Y - _wlDragOrigin.Y;
            _windowWidth = Math.Clamp(_wlDragStartWw + dx * 0.003, 0.01, 2.0);
            _windowCenter = Math.Clamp(_wlDragStartWc - dy * 0.003, -0.5, 1.5);
            RenderDrr();
            UpdateStatusInfo();
        }

        // Rubber-band preview for multi-click tools
        if (_toolState.PendingPoints.Count > 0 && _toolState.ActiveTool is
            CephTool.Line or CephTool.InfinitePlane or CephTool.DistancePoints or
            CephTool.Angle3Points or CephTool.DistancePointPlane)
        {
            var imgPos = ScreenToImage(e.GetPosition(ImageCanvas));
            _rubberBandEnd = new CephPoint(imgPos.X, imgPos.Y);
            RefreshMeasurementOverlay();
        }

        // Live magnifier loupe
        UpdateLoupe(e.GetPosition(ImageCanvas));
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Mouse 2D: Zoom
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var cursorPos = e.GetPosition(ViewportBorder);
        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        double cx = cursorPos.X - vpW / 2.0;
        double cy = cursorPos.Y - vpH / 2.0;
        _panX = cx + factor * (_panX - cx);
        _panY = cy + factor * (_panY - cy);
        _zoom *= factor;
        UpdateImageTransform();
        e.Handled = true;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Coordinate conversion
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private System.Windows.Point ScreenToImage(System.Windows.Point screenPos)
    {
        if (_activeDrr == null) return new System.Windows.Point(-1, -1);
        double vpW = ViewportBorder.ActualWidth;
        double vpH = ViewportBorder.ActualHeight;
        double scaleX = _zoom * _activeDrr.SpacingX;
        double scaleY = _zoom * _activeDrr.SpacingY;
        double dispW = _activeDrr.Width * scaleX;
        double dispH = _activeDrr.Height * scaleY;
        double left = (vpW - dispW) / 2.0 + _panX;
        double top = (vpH - dispH) / 2.0 + _panY;
        double imgX = (screenPos.X - left) / scaleX;
        double imgY = (screenPos.Y - top) / scaleY;
        return new System.Windows.Point(imgX, imgY);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D Mode: Toggle
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Toggle3D_Changed(object sender, RoutedEventArgs e)
    {
        _is3DMode = Btn3DToggle.IsChecked == true;

        if (_is3DMode)
        {
            // Hide DRR overlay so the live HelixViewport3D in MainWindow shows through
            // the transparent ViewportGrid behind this overlay.
            ViewportBorder.Visibility = Visibility.Collapsed;

            // Backfill Position3D for any landmark placed in 2D without a 3D coord
            if (_landmarks != null)
            {
                foreach (var lm in _landmarks)
                    if (lm.IsPlaced && !lm.IsPlaced3D)
                        if (Project2DTo3D(lm.Position!.Value.X, lm.Position!.Value.Y) is Vector3 v)
                            lm.Position3D = ((double)v.X, (double)v.Y, (double)v.Z);
            }

            Refresh3DLandmarks();
            Refresh3DMeasurements();

            StatusLeft.Text = _toolState.ActiveTool == CephTool.Select
                ? "3D Mode — landmark spheres visible on model"
                : "3D Mode — Click to measure (Esc to cancel)";
        }
        else
        {
            // Remove pending/measurement 3D visuals; keep landmark spheres
            var vp = SharedViewport3D;
            if (vp != null)
            {
                foreach (var v in _meas3DVisuals) vp.Items.Remove(v);
                foreach (var s in _pending3DSpheres) vp.Items.Remove(s);
            }
            _meas3DVisuals.Clear();
            _pending3DSpheres.Clear();
            _pending3DPts.Clear();

            // Show DRR overlay again
            ViewportBorder.Visibility = Visibility.Visible;
            RefreshLandmarkOverlay();
            StatusLeft.Text = "2D Mode — Left-click to place landmark, right-drag for W/L";
        }
    }

    private HelixToolkit.Wpf.SharpDX.Viewport3DX? SharedViewport3D =>
        (Application.Current.MainWindow as MainWindow)?.MainViewport;

    // ─── Ceph overlay visibility: refresh spheres on enter, hide on exit ────

    private void OnCephVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            // Became visible: rebuild landmark spheres on the shared viewport
            if (_initialized) Refresh3DLandmarks();
        }
        else
        {
            // Became hidden: respect the global "show in 3D" toggle on MainWindow's
            // Measurements tab — landmark data stays in the ViewModel but visuals
            // disappear from the 3D viewport unless explicitly kept visible.
            var vm = (Application.Current.MainWindow as MainWindow)?.DataContext as ViewModels.MainViewModel;
            bool keepVisible = vm?.ShowCephLandmarksIn3D ?? false;
            foreach (var s in _landmarkSpheres3D) s.IsRendering = keepVisible;
        }
    }

    /// <summary>
    /// Called by MainWindow when the ShowCephLandmarksIn3D toggle changes.
    /// Updates landmark sphere IsRendering so they show/hide in the main 3D
    /// viewport when the ceph panel is NOT open.
    /// </summary>
    public void UpdateLandmarkSphereVisibility(bool show)
    {
        // If ceph is currently visible, spheres are always on
        if (IsVisible) return;
        foreach (var s in _landmarkSpheres3D) s.IsRendering = show;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D Mode: Center Camera
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Viewport3D_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_is3DMode) return;
        var vp = SharedViewport3D;
        if (vp == null) return;
        var hit = vp.FindHits(e.GetPosition(vp)).FirstOrDefault();
        if (hit != null && hit.IsValid)
        {
            _pending3DHit = (Vector3)hit.PointHit;
            _mouseDown3DScreenPos = e.GetPosition(ViewportGrid);
        }
    }

    private void OnViewport3DMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_is3DMode || !_pending3DHit.HasValue)
        {
            _pending3DHit = null;
            return;
        }

        var upPos = e.GetPosition(ViewportGrid);
        var delta = upPos - _mouseDown3DScreenPos;
        if (delta.Length > 5) { _pending3DHit = null; return; }

        var hit = _pending3DHit.Value;
        _pending3DHit = null;

        if (_toolState.ActiveTool != CephTool.Select)
        {
            // Measurement tool click in 3D
            Handle3DToolClick(hit);
            return;
        }

        // Landmark placement (Select mode)
        if (_activeLandmark == null) return;
        _activeLandmark.Position3D = (hit.X, hit.Y, hit.Z);
        var pos2D = Project3DTo2D(hit.X, hit.Y, hit.Z);
        if (pos2D.HasValue) _activeLandmark.Position = pos2D.Value;
        Refresh3DLandmarks();
        RefreshLandmarkOverlay();
        UpdateLandmarkSidebarItem(_activeLandmark);
        AdvanceToNextLandmark();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D -> 2D Projection
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    /// <summary>
    /// Projects a 3D physical-space point (mm) onto the 2D DRR image pixel coordinates.
    /// MarchingCubes vertices are in physical space: voxel_index * spacing.
    /// </summary>
    private (double X, double Y)? Project3DTo2D(double physX, double physY, double physZ)
    {
        if (_volume == null || _activeDrr == null) return null;

        // Convert physical (mm) back to voxel indices
        double voxelX = physX / _volume.Spacing[0];
        double voxelY = physY / _volume.Spacing[1];
        double voxelZ = physZ / _volume.Spacing[2];

        bool isLateral = CmbProjection.SelectedIndex == 0;

        double drrCol, drrRow;
        if (isLateral)
        {
            // Lateral DRR projects along X axis:
            // col = volH - 1 - y  (flipped anteroposterior)
            // row = volD - 1 - z  (flipped superoinferior, superior at top)
            drrCol = (_volume.Height - 1) - voxelY;
            drrRow = (_volume.Depth - 1) - voxelZ;
        }
        else
        {
            // PA DRR projects along Y axis:
            // col = x  (left-right)
            // row = volD - 1 - z  (flipped superoinferior)
            drrCol = voxelX;
            drrRow = (_volume.Depth - 1) - voxelZ;
        }

        // Adjust for auto-crop offset applied during DRR generation
        drrCol -= _activeDrr.CropOffsetX;
        drrRow -= _activeDrr.CropOffsetY;

        // Bounds check
        if (drrCol < 0 || drrCol >= _activeDrr.Width || drrRow < 0 || drrRow >= _activeDrr.Height)
            return null;

        return (drrCol, drrRow);
    }

    /// <summary>
    /// Projects a DRR pixel coordinate to a 3D physical-space point (mm).
    /// Uses the midplane of the volume along the projection axis since the DRR is a
    /// sum projection ÔÇö there is no depth information in a single 2D click.
    /// </summary>
    private Vector3? Project2DTo3D(double drrCol, double drrRow)
    {
        if (_volume == null || _activeDrr == null) return null;

        // Reverse the auto-crop offset that was applied during DRR generation
        double col = drrCol + _activeDrr.CropOffsetX;
        double row = drrRow + _activeDrr.CropOffsetY;

        bool isLateral = CmbProjection.SelectedIndex == 0;

        double voxelX, voxelY, voxelZ;
        if (isLateral)
        {
            // Lateral DRR projects along X:
            //   col = (Height - 1) - voxelY  ÔåÆ  voxelY = (Height - 1) - col
            //   row = (Depth  - 1) - voxelZ  ÔåÆ  voxelZ = (Depth  - 1) - row
            //   voxelX = midplane along projection axis
            voxelY = (_volume.Height - 1) - col;
            voxelZ = (_volume.Depth  - 1) - row;
            voxelX = _volume.Width / 2.0;
        }
        else
        {
            // PA DRR projects along Y:
            //   col = voxelX
            //   row = (Depth - 1) - voxelZ  ÔåÆ  voxelZ = (Depth - 1) - row
            //   voxelY = midplane along projection axis
            voxelX = col;
            voxelZ = (_volume.Depth - 1) - row;
            voxelY = _volume.Height / 2.0;
        }

        return new Vector3(
            (float)(voxelX * _volume.Spacing[0]),
            (float)(voxelY * _volume.Spacing[1]),
            (float)(voxelZ * _volume.Spacing[2]));
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D Mode: Render landmark spheres
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Refresh3DLandmarks()
    {
        // Remove old spheres
        foreach (var sphere in _landmarkSpheres3D)
            SharedViewport3D?.Items.Remove(sphere);
        _landmarkSpheres3D.Clear();

        if (_landmarks == null) return;

        foreach (var lm in _landmarks)
        {
            if (!lm.IsPlaced3D) continue;
            var (px, py, pz) = lm.Position3D!.Value;

            var builder = new HelixToolkit.Geometry.MeshBuilder();
            builder.AddSphere(new Vector3((float)px, (float)py, (float)pz), 1.5f);
            var geom = HxGeom.Converter.ToMeshGeometry3D(builder.ToMesh());

            bool isActive = lm == _activeLandmark;
            var color = isActive
                ? new HelixToolkit.Maths.Color4(0f, 1f, 0.53f, 1f)  // #00FF88
                : new HelixToolkit.Maths.Color4(0f, 0.9f, 1f, 1f);  // #00E5FF

            var sphere = new MeshGeometryModel3D
            {
                Geometry = geom,
                Material = new PhongMaterial
                {
                    DiffuseColor = color,
                    SpecularColor = new HelixToolkit.Maths.Color4(0.3f, 0.3f, 0.3f, 1f),
                    SpecularShininess = 10f,
                },
                IsHitTestVisible = false,
            };

            _landmarkSpheres3D.Add(sphere);
            SharedViewport3D?.Items.Add(sphere);
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 2D Landmark overlay rendering
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void RefreshLandmarkOverlay()
    {
        LandmarkCanvas.Children.Clear();
        if (_landmarks == null) return;

        foreach (var lm in _landmarks)
        {
            if (!lm.IsPlaced) continue;
            var (px, py) = lm.Position!.Value;

            double dotRadius = 1;
            double hitRadius = 6;
            bool isActive = lm == _activeLandmark;

            // Transparent hit area for easier clicking/dragging
            var hitArea = new Ellipse
            {
                Width = hitRadius * 2,
                Height = hitRadius * 2,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = lm,
                IsHitTestVisible = true,
            };
            Canvas.SetLeft(hitArea, px - hitRadius);
            Canvas.SetTop(hitArea, py - hitRadius);

            // Visible dot
            var dot = new Ellipse
            {
                Width = dotRadius * 2,
                Height = dotRadius * 2,
                Fill = isActive ? ActiveGreenBrush : CyanBrush,
                Stroke = StrokeCyan,
                StrokeThickness = 0.4,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, px - dotRadius);
            Canvas.SetTop(dot, py - dotRadius);

            var label = new TextBlock
            {
                Text = lm.Abbreviation,
                Foreground = isActive ? ActiveGreenBrush : CyanBrush,
                FontSize = 3.75,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, px + dotRadius + 1);
            Canvas.SetTop(label, py - dotRadius);

            LandmarkCanvas.Children.Add(hitArea);
            LandmarkCanvas.Children.Add(dot);
            LandmarkCanvas.Children.Add(label);
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Landmark sidebar
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void BuildLandmarkSidebar()
    {
        LandmarkListPanel.Children.Clear();
        if (_landmarks == null) return;
        string? lastCategory = null;

        foreach (var lm in _landmarks)
        {
            string catName = lm.Category.ToString();
            if (catName != lastCategory)
            {
                lastCategory = catName;
                LandmarkListPanel.Children.Add(new TextBlock
                {
                    Text = catName.ToUpperInvariant(),
                    Foreground = SubduedText,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(4, 8, 0, 4),
                });
            }

            var row = CreateLandmarkRow(lm);
            LandmarkListPanel.Children.Add(row);
        }

        if (_landmarks.Count > 0)
        {
            _activeLandmark = _landmarks[0];
            UpdateLandmarkSidebarItem(_activeLandmark);
        }
    }

    private Border CreateLandmarkRow(CephalometricLandmark lm)
    {
        bool isActive = lm == _activeLandmark;

        var indicator = new Ellipse
        {
            Width = 8, Height = 8,
            Margin = new Thickness(4, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ApplyIndicatorStyle(indicator, lm);

        var nameText = new TextBlock
        {
            Text = $"{lm.Abbreviation}  {lm.Name}",
            Foreground = isActive ? TextActiveBlue : TextWhite,
            FontSize = 11,
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(indicator);
        stack.Children.Add(nameText);

        var border = new Border
        {
            Background = isActive ? ActiveRowBg : Brushes.Transparent,
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(0, 1, 0, 0),
            Cursor = Cursors.Hand,
            Tag = lm,
            ToolTip = lm.Description,
            Child = stack,
        };

        border.MouseLeftButtonDown += (_, _) =>
        {
            _activeLandmark = lm;
            RebuildSidebarHighlights();
            RefreshLandmarkOverlay();
            if (_is3DMode) Refresh3DLandmarks();
            StatusLeft.Text = $"Active: {lm.Abbreviation} -- {lm.Name}";
        };

        return border;
    }

    /// <summary>
    /// Sets the indicator dot style based on placement state:
    /// - 3D placed: filled cyan
    /// - 2D-only placed: cyan outline (stroke only)
    /// - Not placed: gray filled
    /// </summary>
    private static void ApplyIndicatorStyle(Ellipse indicator, CephalometricLandmark lm)
    {
        if (lm.IsPlaced3D)
        {
            indicator.Fill = CyanBrush;
            indicator.Stroke = null;
            indicator.StrokeThickness = 0;
        }
        else if (lm.IsPlaced)
        {
            indicator.Fill = Brushes.Transparent;
            indicator.Stroke = CyanBrush;
            indicator.StrokeThickness = 1.5;
        }
        else
        {
            indicator.Fill = GrayDotBrush;
            indicator.Stroke = null;
            indicator.StrokeThickness = 0;
        }
    }

    private void RebuildSidebarHighlights()
    {
        foreach (var child in LandmarkListPanel.Children)
        {
            if (child is Border b && b.Tag is CephalometricLandmark lm)
            {
                bool isActive = lm == _activeLandmark;
                b.Background = isActive ? ActiveRowBg : Brushes.Transparent;

                if (b.Child is StackPanel sp && sp.Children.Count >= 2)
                {
                    if (sp.Children[0] is Ellipse ind)
                        ApplyIndicatorStyle(ind, lm);
                    if (sp.Children[1] is TextBlock tb)
                    {
                        tb.Foreground = isActive ? TextActiveBlue : TextWhite;
                        tb.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                }
            }
        }
    }

    private void UpdateLandmarkSidebarItem(CephalometricLandmark lm)
    {
        RebuildSidebarHighlights();
    }

    private void AdvanceToNextLandmark()
    {
        if (_landmarks == null || _activeLandmark == null) return;
        int idx = _landmarks.IndexOf(_activeLandmark);
        for (int i = 1; i <= _landmarks.Count; i++)
        {
            var next = _landmarks[(idx + i) % _landmarks.Count];
            if (!next.IsPlaced)
            {
                _activeLandmark = next;
                RebuildSidebarHighlights();
                StatusLeft.Text = $"Active: {next.Abbreviation} -- {next.Name}";
                return;
            }
        }
        StatusLeft.Text = "All landmarks placed";
    }

    private void DeleteSelectedLandmark()
    {
        if (_activeLandmark == null || !_activeLandmark.IsPlaced) return;
        _activeLandmark.Position = null;
        _activeLandmark.Position3D = null;
        RefreshLandmarkOverlay();
        if (_is3DMode) Refresh3DLandmarks();
        UpdateLandmarkSidebarItem(_activeLandmark);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Measurement Tools: button handler + dispatch
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ToolBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName &&
            Enum.TryParse<CephTool>(toolName, out var tool))
        {
            _toolState.SetTool(tool);
            _rubberBandEnd = null;
            UpdateToolButtonHighlights();
            UpdateToolStatus();
            RefreshMeasurementOverlay();
        }
    }

    private void UpdateToolButtonHighlights()
    {
        foreach (var child in ToolButtonPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tagName &&
                Enum.TryParse<CephTool>(tagName, out var tool))
            {
                bool active = _toolState.ActiveTool == tool;
                btn.Background = active
                    ? new SolidColorBrush(Color.FromRgb(0x2A, 0x4A, 0x6B))
                    : new SolidColorBrush(Color.FromRgb(0x1E, 0x2D, 0x3D));
                btn.Foreground = active
                    ? new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF))
                    : TextWhite;
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x5A, 0x7A));
                btn.BorderThickness = new Thickness(1);
            }
        }
    }

    private void UpdateToolStatus()
    {
        var pending = _toolState.PendingPoints.Count;
        StatusLeft.Text = _toolState.ActiveTool switch
        {
            CephTool.Select => "Select mode -- Left-click to place landmark or select measurement",
            CephTool.CustomPoint => "Click to place a point",
            CephTool.Line => pending == 0
                ? "Click first point of line"
                : "Click second point -- Esc to cancel",
            CephTool.InfinitePlane => pending == 0
                ? "Click first point of plane"
                : "Click second point -- Esc to cancel",
            CephTool.Angle3Points => pending switch
            {
                0 => "Click point A",
                1 => "Click vertex point -- Esc to cancel",
                _ => "Click point B -- Esc to cancel"
            },
            CephTool.DistancePoints => pending == 0
                ? "Click first point for distance"
                : "Click second point -- Esc to cancel",
            CephTool.DistancePointPlane => pending == 0
                ? "Click the point"
                : "Click on a line/plane -- Esc to cancel",
            CephTool.AnglePlanes => "Click on first line or plane",
            _ => "Ready"
        };
    }

    private void HandleToolClick(CephPoint imgPt)
    {
        if (_activeDrr == null) return;
        double sx = _activeDrr.SpacingX;
        double sy = _activeDrr.SpacingY;

        switch (_toolState.ActiveTool)
        {
            case CephTool.CustomPoint:
                HandleCustomPointClick(imgPt);
                break;

            case CephTool.Line:
            case CephTool.InfinitePlane:
                HandleLineClick(imgPt, _toolState.ActiveTool);
                break;

            case CephTool.DistancePoints:
                HandleDistancePointsClick(imgPt, sx, sy);
                break;

            case CephTool.Angle3Points:
                HandleAngle3PointsClick(imgPt, sx, sy);
                break;

            case CephTool.AnglePlanes:
                // Requires selecting existing lines ÔÇö handled via measurement selection
                break;

            case CephTool.DistancePointPlane:
                HandleDistancePointPlaneClick(imgPt, sx, sy);
                break;
        }

        _rubberBandEnd = null;
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        UpdateToolStatus();
    }

    // ÔöÇÔöÇ Tool: CustomPoint ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void HandleCustomPointClick(CephPoint pt)
    {
        var m = new CephMeasurement
        {
            Label = _toolState.NextLabel(CephTool.CustomPoint),
            ToolType = CephTool.CustomPoint,
            Points = { pt },
            ColorR = 255, ColorG = 255, ColorB = 0, // yellow
        };
        _toolState.Measurements.Add(m);
    }

    // ÔöÇÔöÇ Tool: Line / InfinitePlane ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void HandleLineClick(CephPoint pt, CephTool toolType)
    {
        _toolState.PendingPoints.Add(pt);
        if (_toolState.PendingPoints.Count < 2) return;

        var p1 = _toolState.PendingPoints[0];
        var p2 = _toolState.PendingPoints[1];
        double length = _activeDrr != null
            ? CephToolEngine.DistanceMm(p1, p2, _activeDrr.SpacingX, _activeDrr.SpacingY)
            : 0;

        var m = new CephMeasurement
        {
            Label = _toolState.NextLabel(toolType),
            ToolType = toolType,
            Points = { p1, p2 },
            Value = length,
            Unit = "mm",
            ColorR = toolType == CephTool.Line ? (byte)255 : (byte)0,
            ColorG = toolType == CephTool.Line ? (byte)255 : (byte)229,
            ColorB = 255, // white for Line, cyan for Plane
        };
        _toolState.Measurements.Add(m);
        _toolState.PendingPoints.Clear();
    }

    // ÔöÇÔöÇ Tool: DistancePoints ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void HandleDistancePointsClick(CephPoint pt, double sx, double sy)
    {
        _toolState.PendingPoints.Add(pt);
        if (_toolState.PendingPoints.Count < 2) return;

        var p1 = _toolState.PendingPoints[0];
        var p2 = _toolState.PendingPoints[1];
        double dist = CephToolEngine.DistanceMm(p1, p2, sx, sy);

        var m = new CephMeasurement
        {
            Label = _toolState.NextLabel(CephTool.DistancePoints),
            ToolType = CephTool.DistancePoints,
            Points = { p1, p2 },
            Value = dist,
            Unit = "mm",
            ColorR = 0, ColorG = 220, ColorB = 100, // green
        };
        _toolState.Measurements.Add(m);
        _toolState.PendingPoints.Clear();
    }

    // ÔöÇÔöÇ Tool: Angle3Points ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void HandleAngle3PointsClick(CephPoint pt, double sx, double sy)
    {
        _toolState.PendingPoints.Add(pt);
        if (_toolState.PendingPoints.Count < 3) return;

        var a = _toolState.PendingPoints[0];
        var vertex = _toolState.PendingPoints[1];
        var b = _toolState.PendingPoints[2];
        double angle = CephToolEngine.Angle3Pts(a, vertex, b, sx, sy);

        var m = new CephMeasurement
        {
            Label = _toolState.NextLabel(CephTool.Angle3Points),
            ToolType = CephTool.Angle3Points,
            Points = { a, vertex, b },
            Value = angle,
            Unit = "\u00B0",
            ColorR = 255, ColorG = 180, ColorB = 0, // amber
        };
        _toolState.Measurements.Add(m);
        _toolState.PendingPoints.Clear();
    }

    // ÔöÇÔöÇ Tool: DistancePointPlane ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private void HandleDistancePointPlaneClick(CephPoint pt, double sx, double sy)
    {
        if (_toolState.PendingPoints.Count == 0)
        {
            _toolState.PendingPoints.Add(pt);
            return;
        }

        // Find the closest line/plane measurement to the second click
        CephMeasurement? closestLine = null;
        double bestDist = double.MaxValue;
        foreach (var m in _toolState.Measurements)
        {
            if (m.ToolType is not (CephTool.Line or CephTool.InfinitePlane) || m.Points.Count < 2)
                continue;
            var (_, d) = CephToolEngine.PerpendicularToLine(pt, m.Points[0], m.Points[1], sx, sy);
            if (d < bestDist) { bestDist = d; closestLine = m; }
        }

        if (closestLine == null)
        {
            StatusLeft.Text = "No line/plane found near click -- try again";
            return;
        }

        var point = _toolState.PendingPoints[0];
        var (foot, dist) = CephToolEngine.PerpendicularToLine(
            point, closestLine.Points[0], closestLine.Points[1], sx, sy);

        var measurement = new CephMeasurement
        {
            Label = _toolState.NextLabel(CephTool.DistancePointPlane),
            ToolType = CephTool.DistancePointPlane,
            Points = { point, foot },
            Value = dist,
            Unit = "mm",
            RefMeasurementId1 = closestLine.Id,
            ColorR = 0, ColorG = 200, ColorB = 120, // green-ish
        };
        _toolState.Measurements.Add(measurement);
        _toolState.PendingPoints.Clear();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Measurement Overlay Rendering
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void RefreshMeasurementOverlay()
    {
        MeasurementsCanvas.Children.Clear();

        // Draw completed measurements
        foreach (var m in _toolState.Measurements)
            DrawMeasurement(m);

        // Draw pending points
        foreach (var pt in _toolState.PendingPoints)
            DrawPendingDot(pt);

        // Rubber-band preview
        if (_toolState.PendingPoints.Count > 0 && _rubberBandEnd.HasValue)
        {
            var last = _toolState.PendingPoints[^1];
            var end = _rubberBandEnd.Value;
            DrawPreviewLine(last, end);
        }
    }

    private void DrawMeasurement(CephMeasurement m)
    {
        var brush = new SolidColorBrush(Color.FromRgb(m.ColorR, m.ColorG, m.ColorB));
        bool selected = m == _toolState.SelectedMeasurement;
        double strokeW = selected ? 1.0 : 0.5;

        switch (m.ToolType)
        {
            case CephTool.CustomPoint when m.Points.Count >= 1:
                DrawMeasurementDot(m.Points[0], 2, brush, m);
                DrawMeasurementLabel(m.Points[0], 3, 0, $"{m.Label}", brush, m);
                break;

            case CephTool.Line when m.Points.Count >= 2:
                DrawMeasurementLine(m.Points[0], m.Points[1], brush, strokeW, false, m);
                DrawMeasurementDot(m.Points[0], 1, brush, m);
                DrawMeasurementDot(m.Points[1], 1, brush, m);
                var mid = new CephPoint((m.Points[0].X + m.Points[1].X) / 2,
                                        (m.Points[0].Y + m.Points[1].Y) / 2);
                DrawMeasurementLabel(mid, 0, -4, $"{m.Label}: {m.Value:F1} mm", brush, m);
                break;

            case CephTool.InfinitePlane when m.Points.Count >= 2:
                DrawInfiniteLine(m.Points[0], m.Points[1], brush, strokeW, m);
                DrawMeasurementDot(m.Points[0], 1, brush, m);
                DrawMeasurementDot(m.Points[1], 1, brush, m);
                var pmid = new CephPoint((m.Points[0].X + m.Points[1].X) / 2,
                                         (m.Points[0].Y + m.Points[1].Y) / 2);
                DrawMeasurementLabel(pmid, 0, -4, m.Label, brush, m);
                break;

            case CephTool.DistancePoints when m.Points.Count >= 2:
                DrawMeasurementLine(m.Points[0], m.Points[1], brush, strokeW, false, m);
                DrawMeasurementDot(m.Points[0], 1, brush, m);
                DrawMeasurementDot(m.Points[1], 1, brush, m);
                var dmid = new CephPoint((m.Points[0].X + m.Points[1].X) / 2,
                                         (m.Points[0].Y + m.Points[1].Y) / 2);
                DrawMeasurementLabel(dmid, 0, -4, $"{m.Value:F1} mm", brush, m);
                break;

            case CephTool.Angle3Points when m.Points.Count >= 3:
                var amberBrush = new SolidColorBrush(Color.FromRgb(255, 180, 0));
                DrawMeasurementLine(m.Points[0], m.Points[1], brush, strokeW, false, m);
                DrawMeasurementLine(m.Points[2], m.Points[1], brush, strokeW, false, m);
                DrawMeasurementDot(m.Points[0], 1, brush, m);
                DrawMeasurementDot(m.Points[1], 1.5, amberBrush, m);
                DrawMeasurementDot(m.Points[2], 1, brush, m);
                DrawMeasurementLabel(m.Points[1], 3, -4, $"{m.Value:F1}\u00B0", amberBrush, m);
                break;

            case CephTool.DistancePointPlane when m.Points.Count >= 2:
                DrawMeasurementLine(m.Points[0], m.Points[1], brush, strokeW, true, m);
                DrawMeasurementDot(m.Points[0], 1, brush, m);
                DrawMeasurementDot(m.Points[1], 1, brush, m);
                var fmid = new CephPoint((m.Points[0].X + m.Points[1].X) / 2,
                                         (m.Points[0].Y + m.Points[1].Y) / 2);
                DrawMeasurementLabel(fmid, 0, -4, $"{m.Value:F1} mm", brush, m);
                break;
        }
    }

    private void DrawMeasurementDot(CephPoint pt, double radius, Brush fill, CephMeasurement tag)
    {
        var dot = new Ellipse
        {
            Width = radius * 2, Height = radius * 2,
            Fill = fill,
            Tag = tag,
            IsHitTestVisible = true,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(dot, pt.X - radius);
        Canvas.SetTop(dot, pt.Y - radius);
        MeasurementsCanvas.Children.Add(dot);
    }

    private void DrawMeasurementLine(CephPoint a, CephPoint b, Brush stroke, double thickness,
                                     bool dashed, CephMeasurement tag)
    {
        var line = new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            Stroke = stroke, StrokeThickness = thickness,
            Tag = tag, IsHitTestVisible = true, Cursor = Cursors.Hand,
        };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 4, 4 };
        MeasurementsCanvas.Children.Add(line);
    }

    private void DrawInfiniteLine(CephPoint a, CephPoint b, Brush stroke, double thickness,
                                  CephMeasurement tag)
    {
        // Extend line to large bounds
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        double scale = 5000.0 / len;
        var extA = new CephPoint(a.X - dx * scale, a.Y - dy * scale);
        var extB = new CephPoint(b.X + dx * scale, b.Y + dy * scale);

        var line = new Line
        {
            X1 = extA.X, Y1 = extA.Y, X2 = extB.X, Y2 = extB.Y,
            Stroke = stroke, StrokeThickness = thickness,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            Tag = tag, IsHitTestVisible = true, Cursor = Cursors.Hand,
        };
        MeasurementsCanvas.Children.Add(line);
    }

    private void DrawMeasurementLabel(CephPoint pos, double offsetX, double offsetY,
                                      string text, Brush foreground, CephMeasurement tag)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = foreground,
            FontSize = 3.5, IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, pos.X + offsetX);
        Canvas.SetTop(tb, pos.Y + offsetY);
        MeasurementsCanvas.Children.Add(tb);
    }

    private void DrawPendingDot(CephPoint pt)
    {
        var dot = new Ellipse
        {
            Width = 3, Height = 3,
            Fill = Brushes.White,
            Stroke = Brushes.Gray, StrokeThickness = 0.3,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, pt.X - 1.5);
        Canvas.SetTop(dot, pt.Y - 1.5);
        MeasurementsCanvas.Children.Add(dot);
    }

    private void DrawPreviewLine(CephPoint from, CephPoint to)
    {
        var line = new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)),
            StrokeThickness = 0.4,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false,
        };
        MeasurementsCanvas.Children.Add(line);
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Measurements Sidebar Panel
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void RefreshMeasurementPanel()
    {
        MeasurementListPanel.Children.Clear();

        foreach (var m in _toolState.Measurements)
        {
            bool selected = m == _toolState.SelectedMeasurement;
            var brush = new SolidColorBrush(Color.FromRgb(m.ColorR, m.ColorG, m.ColorB));

            // Color swatch
            var swatch = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = brush,
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Label
            var label = new TextBlock
            {
                Text = string.IsNullOrEmpty(m.Unit)
                    ? m.Label
                    : $"{m.Label}: {m.Value:F1} {m.Unit}",
                Foreground = selected ? TextActiveBlue : TextWhite,
                FontSize = 10,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 170,
            };

            // Delete button
            var delBtn = new Button
            {
                Content = "\u2715", FontSize = 9,
                Padding = new Thickness(3, 0, 3, 0),
                Margin = new Thickness(4, 0, 2, 0),
                Background = Brushes.Transparent,
                Foreground = SubduedText,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = m,
            };
            delBtn.Click += (_, _) =>
            {
                _toolState.Measurements.Remove(m);
                if (_toolState.SelectedMeasurement == m)
                    _toolState.SelectedMeasurement = null;
                RefreshMeasurementOverlay();
                RefreshMeasurementPanel();
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(swatch);
            stack.Children.Add(label);
            stack.Children.Add(delBtn);

            var row = new Border
            {
                Background = selected ? ActiveRowBg : Brushes.Transparent,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 1, 0, 0),
                Cursor = Cursors.Hand,
                Tag = m,
                Child = stack,
            };

            row.MouseLeftButtonDown += (_, _) =>
            {
                _toolState.SelectedMeasurement = m;
                RefreshMeasurementOverlay();
                RefreshMeasurementPanel();
            };

            MeasurementListPanel.Children.Add(row);
        }
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Toolbar handlers
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void CmbProjection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _volume == null) return;
        _ = GenerateDrrAsync(CmbProjection.SelectedIndex == 0);
    }

    private void ChkInvert_Changed(object sender, RoutedEventArgs e)
    {
        _inverted = ChkInvert.IsChecked == true;
        RenderDrr();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetView();

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Helpers
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void ResetView()
    {
        _windowWidth  = 1.140;  // user-preferred default
        _windowCenter = 0.584;
        _inverted = true;
        ChkInvert.IsChecked = true;
        FitToViewport();
        RenderDrr();
        UpdateStatusInfo();
    }

    private void UpdateStatusInfo()
    {
        if (_activeDrr == null) return;
        StatusCenter.Text = $"{_activeDrr.Width}x{_activeDrr.Height}  |  " +
                            $"{_activeDrr.SpacingX:F2}x{_activeDrr.SpacingY:F2} mm/px  |  " +
                            $"{_zoom * 100:F0}%";
        StatusRight.Text = $"W: {_windowWidth:F2}  L: {_windowCenter:F2}";
        WlHudW.Text = $"W  {_windowWidth:F3}";
        WlHudL.Text = $"L  {_windowCenter:F3}";
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Magnifier Loupe
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void UpdateLoupe(System.Windows.Point canvasPos)
    {
        if (DrrImageControl.Source == null)
        {
            LoupePanel.Visibility = Visibility.Collapsed;
            return;
        }

        LoupePanel.Visibility = Visibility.Visible;

        // Loupe shows a 2x magnified crop of the Canvas around the cursor
        double loupeW = LoupePanel.Width;   // 190
        double loupeH = LoupePanel.Height;  // 150
        double scale  = 2.0;
        double regionW = loupeW / scale;
        double regionH = loupeH / scale;

        // Clamp the viewbox so it doesn't exceed the Canvas bounds
        double canvasW = ImageCanvas.ActualWidth;
        double canvasH = ImageCanvas.ActualHeight;
        double vbX = Math.Max(0, Math.Min(canvasPos.X - regionW / 2, canvasW - regionW));
        double vbY = Math.Max(0, Math.Min(canvasPos.Y - regionH / 2, canvasH - regionH));

        LoupeRect.Fill = new VisualBrush(ImageCanvas)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox      = new Rect(vbX, vbY, regionW, regionH),
            Stretch      = Stretch.Fill
        };
    }

    // 3D Scroll-Wheel Pan (scroll = pan, Ctrl+scroll = zoom)
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Viewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SharedViewport3D?.Camera is not HelixToolkit.Wpf.SharpDX.PerspectiveCamera cam) return;

        double steps = e.Delta / 120.0;

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Scroll = zoom (move along look direction)
            var look = cam.LookDirection;
            double len = Math.Sqrt(look.X * look.X + look.Y * look.Y + look.Z * look.Z);
            if (len < 0.001) return;
            double f = steps * len * 0.08;
            cam.Position = new System.Windows.Media.Media3D.Point3D(
                cam.Position.X + look.X / len * f,
                cam.Position.Y + look.Y / len * f,
                cam.Position.Z + look.Z / len * f);
        }
        else
        {
            // Scroll = vertical pan (along camera up direction)
            var up = cam.UpDirection;
            double ulen = Math.Sqrt(up.X * up.X + up.Y * up.Y + up.Z * up.Z);
            if (ulen < 0.001) return;
            double panAmount = steps * 5.0; // mm per notch
            cam.Position = new System.Windows.Media.Media3D.Point3D(
                cam.Position.X + up.X / ulen * panAmount,
                cam.Position.Y + up.Y / ulen * panAmount,
                cam.Position.Z + up.Z / ulen * panAmount);
        }
        e.Handled = true;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D Measurements: click dispatch + completion
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Handle3DToolClick(Vector3 hit)
    {
        _pending3DPts.Add(hit);

        // Show pending sphere at hit point
        var pendingSphere = Make3DSphere(hit, 1.5f, System.Windows.Media.Colors.White);
        _pending3DSpheres.Add(pendingSphere);
        SharedViewport3D?.Items.Add(pendingSphere);

        switch (_toolState.ActiveTool)
        {
            case CephTool.CustomPoint:
                Finish3DCustomPoint();
                break;

            case CephTool.DistancePoints:
            case CephTool.Line:
                if (_pending3DPts.Count >= 2) Finish3DLine();
                break;

            case CephTool.Angle3Points:
                if (_pending3DPts.Count >= 3) Finish3DAngle();
                break;
        }

        UpdateToolStatus();
    }

    private void Finish3DCustomPoint()
    {
        var p = _pending3DPts[0];
        string label = _toolState.NextLabel(CephTool.CustomPoint);
        _measurements3D.Add(new Meas3D
        {
            Tool = CephTool.CustomPoint, Label = label,
            Pts = new List<Vector3> { p }, Value = 0, Unit = "",
            Color = System.Windows.Media.Colors.Yellow
        });
        _toolState.Measurements.Add(new CephMeasurement
        {
            Label = label, ToolType = CephTool.CustomPoint,
            ColorR = 255, ColorG = 215, ColorB = 0
        });
        _pending3DPts.Clear();
        ClearPending3DSpheres();
        Refresh3DMeasurements();
        RefreshMeasurementPanel();
    }

    private void Finish3DLine()
    {
        var p1 = _pending3DPts[0]; var p2 = _pending3DPts[1];
        double dist = (p2 - p1).Length();
        bool isLine = _toolState.ActiveTool == CephTool.Line;
        string label = _toolState.NextLabel(_toolState.ActiveTool);
        var col = isLine
            ? System.Windows.Media.Color.FromRgb(0, 229, 255)
            : System.Windows.Media.Color.FromRgb(0, 220, 100);
        _measurements3D.Add(new Meas3D
        {
            Tool = _toolState.ActiveTool, Label = label,
            Pts = new List<Vector3> { p1, p2 }, Value = dist, Unit = "mm", Color = col
        });
        _toolState.Measurements.Add(new CephMeasurement
        {
            Label = label, ToolType = _toolState.ActiveTool, Value = dist, Unit = "mm",
            ColorR = col.R, ColorG = col.G, ColorB = col.B
        });
        _pending3DPts.Clear();
        ClearPending3DSpheres();
        Refresh3DMeasurements();
        RefreshMeasurementPanel();
    }

    private void Finish3DAngle()
    {
        var a = _pending3DPts[0]; var vtx = _pending3DPts[1]; var b = _pending3DPts[2];
        var v1 = Vector3.Normalize(a - vtx);
        var v2 = Vector3.Normalize(b - vtx);
        double dot = Math.Clamp(Vector3.Dot(v1, v2), -1.0, 1.0);
        double angle = Math.Acos(dot) * 180.0 / Math.PI;
        string label = _toolState.NextLabel(CephTool.Angle3Points);
        var col = System.Windows.Media.Color.FromRgb(255, 180, 0);
        _measurements3D.Add(new Meas3D
        {
            Tool = CephTool.Angle3Points, Label = label,
            Pts = new List<Vector3> { a, vtx, b }, Value = angle, Unit = "\u00B0", Color = col
        });
        _toolState.Measurements.Add(new CephMeasurement
        {
            Label = label, ToolType = CephTool.Angle3Points, Value = angle, Unit = "\u00B0",
            ColorR = 255, ColorG = 180, ColorB = 0
        });
        _pending3DPts.Clear();
        ClearPending3DSpheres();
        Refresh3DMeasurements();
        RefreshMeasurementPanel();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D Measurement Rendering
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    private void Refresh3DMeasurements()
    {
        foreach (var v in _meas3DVisuals) SharedViewport3D?.Items.Remove(v);
        _meas3DVisuals.Clear();

        foreach (var m in _measurements3D)
        {
            var wc = m.Color;

            switch (m.Tool)
            {
                case CephTool.CustomPoint when m.Pts.Count >= 1:
                    Add3DVisual(Make3DSphere(m.Pts[0], 2f, wc));
                    break;

                case CephTool.Line:
                case CephTool.DistancePoints when m.Pts.Count >= 2:
                    Add3DVisual(Make3DSphere(m.Pts[0], 1.2f, wc));
                    Add3DVisual(Make3DSphere(m.Pts[1], 1.2f, wc));
                    Add3DVisual(Make3DLine(m.Pts[0], m.Pts[1], wc));
                    break;

                case CephTool.Angle3Points when m.Pts.Count >= 3:
                    Add3DVisual(Make3DSphere(m.Pts[0], 1f, wc));
                    Add3DVisual(Make3DSphere(m.Pts[1], 1.5f, wc));  // vertex bigger
                    Add3DVisual(Make3DSphere(m.Pts[2], 1f, wc));
                    Add3DVisual(Make3DLine(m.Pts[0], m.Pts[1], wc));
                    Add3DVisual(Make3DLine(m.Pts[2], m.Pts[1], wc));
                    break;
            }
        }
    }

    private void Add3DVisual(Element3D el)
    {
        _meas3DVisuals.Add(el);
        SharedViewport3D?.Items.Add(el);
    }

    private MeshGeometryModel3D Make3DSphere(Vector3 center, float radius, System.Windows.Media.Color col)
    {
        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddSphere(center, radius);
        return new MeshGeometryModel3D
        {
            Geometry = HxGeom.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material = new PhongMaterial
            {
                DiffuseColor = new HelixToolkit.Maths.Color4(
                    col.R / 255f, col.G / 255f, col.B / 255f, 1f)
            },
            IsHitTestVisible = false
        };
    }

    private LineGeometryModel3D Make3DLine(Vector3 p1, Vector3 p2, System.Windows.Media.Color col)
    {
        var lb = new HelixToolkit.SharpDX.LineBuilder();
        lb.AddLine(p1, p2);
        return new LineGeometryModel3D
        {
            Geometry = lb.ToLineGeometry3D(),
            Color = col,
            Thickness = 1.5,
            IsHitTestVisible = false
        };
    }

    private void ClearPending3DSpheres()
    {
        foreach (var s in _pending3DSpheres) SharedViewport3D?.Items.Remove(s);
        _pending3DSpheres.Clear();
        _pending3DPts.Clear();
    }
}
