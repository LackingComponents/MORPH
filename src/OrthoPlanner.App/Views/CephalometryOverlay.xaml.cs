using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OrthoPlanner.Core.Imaging;
using OrthoPlanner.Core.Imaging.Cephalometry;
using OrthoPlanner.App.Helpers;
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
    private readonly Dictionary<MeshGeometryModel3D, CephalometricLandmark> _landmarkSphereMap = new();
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

    // ── Generation ─────────────────────────────────────────────────────────────
    private CancellationTokenSource? _genCts;
    private bool _initialized;
    private string? _lateralDrrKey;
    private string? _paDrrKey;

    private readonly CephAnalysisPanelViewModel _analysisPanel = new();
    private bool _showCephGrid;
    private System.Windows.Point _cephGridCenter = new(-1, -1);
    private System.Windows.Point _cephGridNewCenter = new(-1, -1);
    private bool _isDraggingCephGrid;
    private System.Windows.Point _cephGridDragStart;
    private System.Windows.Point _cephGridDragInitialCenter;
    private MainViewModel? _subscribedVm;
    private bool _syncingGridCheckbox;

    /// <summary>View-model for the Steiner / Tweed / Ricketts analysis result tables.</summary>
    public CephAnalysisPanelViewModel AnalysisPanel => _analysisPanel;

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Constructor
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    public CephalometryOverlay()
    {
        InitializeComponent();
        AnalysisTabControl.DataContext = _analysisPanel;

        CmbProjection.Items.Add("Lateral");
        CmbProjection.Items.Add("PA");
        CmbProjection.SelectedIndex = 0;

        ViewportBorder.SizeChanged += (_, _) => UpdateImageTransform();

        ViewportGrid.SizeChanged += (_, _) => DrawCephGrid();

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
                Refresh3DMeasurements();
                MeasurementsChanged?.Invoke();
                e.Handled = true;
            }
        };

        // Default tool highlight
        UpdateToolButtonHighlights();

        // 3D click detection: in 3D mode the central overlay is always
        // mouse-transparent so HelixToolkit receives every button natively
        // (rotate/pan/zoom). Placement clicks are intercepted via Preview
        // handlers attached directly to the shared viewport (see
        // AttachViewportPlacementHandlers), which consume the left click only
        // when actually placing a landmark/measurement.
        ViewportGrid.MouseEnter += (_, _) => UpdateViewportGridHitTest();

        IsVisibleChanged += OnCephVisibilityChanged;

        // Apply initial hit-test state (default = 2D mode -> fully hit-testable).
        UpdateViewportGridHitTest();
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // Public API
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    // ════════════════════════════════════════════════════════════════════
    // Public API used by the Measurements tab tree (right panel)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fired whenever measurements are added, removed, or their visibility changes.
    /// The Measurements tab in MainWindow subscribes to this to rebuild its tree.
    /// </summary>
    public event Action? MeasurementsChanged;

    /// <summary>Returns the live measurement list (never null after initialization).</summary>
    public IReadOnlyList<CephMeasurement> GetMeasurements() =>
        _toolState.Measurements.AsReadOnly();

    /// <summary>
    /// Called by the Measurements tab tree when a visibility checkbox changes.
    /// Immediately repaints the DRR canvas.
    /// </summary>
    public void SetMeasurementVisible(CephMeasurement m, bool visible)
    {
        m.IsVisible = visible;
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        MeasurementsChanged?.Invoke();
    }

    public void RefreshMeasurementDisplayFromExternalChange()
    {
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        Refresh3DMeasurements();
    }

    /// <summary>
    /// Called by the Measurements tab tree to delete a measurement.
    /// </summary>
    public void DeleteMeasurementFromTree(CephMeasurement m)
    {
        _toolState.Measurements.Remove(m);
        if (_toolState.SelectedMeasurement == m)
            _toolState.SelectedMeasurement = null;
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        Refresh3DMeasurements();
        MeasurementsChanged?.Invoke();
    }

    public void SetVolume(VolumeData volume)
    {
        bool sameVolume = volume == _volume && _initialized;
        _volume = volume;
        if (!sameVolume)
        {
            _lateralDrr = null;
            _paDrr = null;
            _activeDrr = null;
            _lateralDrrKey = null;
            _paDrrKey = null;
            _landmarks = CephalometricLandmarkDefinitions.GetAll();

            // Restore any previously saved landmark positions from the project
            RestoreLandmarkData();
            BuildLandmarkSidebar();
        }
        else
        {
            InvalidateDrrCache();
        }

        _initialized = true;
        RefreshAnalysis();
        bool lateral = CmbProjection.SelectedIndex == 0;
        _ = GenerateDrrAsync(lateral, resetView: !sameVolume, reprojectGeometry: sameVolume);
    }

    private void InvalidateDrrCache()
    {
        _lateralDrr = null;
        _paDrr = null;
        _lateralDrrKey = null;
        _paDrrKey = null;
    }

    private static string BuildDrrCacheKey(DrrProjectionParams projection, bool lateral)
    {
        var inv = projection.InverseNhp;
        return $"{(lateral ? "L" : "P")}:{projection.MinX:R}:{projection.MaxX:R}:{projection.MinY:R}:{projection.MaxY:R}:{projection.MinZ:R}:{projection.MaxZ:R}:" +
               $"{inv.M11:R}:{inv.M12:R}:{inv.M13:R}:{inv.M21:R}:{inv.M22:R}:{inv.M23:R}:" +
               $"{inv.M31:R}:{inv.M32:R}:{inv.M33:R}:{inv.M41:R}:{inv.M42:R}:{inv.M43:R}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DRR Generation
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task GenerateDrrAsync(bool lateral, bool resetView = false, bool reprojectGeometry = false)
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
            var vm = GetMainVm();
            if (vm == null || !vm.TryGetDrrProjectionParams(out var projection))
                return;

            string cacheKey = BuildDrrCacheKey(projection, lateral);
            DrrResult drr;
            if (lateral)
            {
                if (_lateralDrr == null || _lateralDrrKey != cacheKey)
                {
                    _lateralDrr = await Task.Run(() => DrrGenerator.GenerateLateral(_volume, projection, ct), ct);
                    _lateralDrrKey = cacheKey;
                }
                drr = _lateralDrr;
            }
            else
            {
                if (_paDrr == null || _paDrrKey != cacheKey)
                {
                    _paDrr = await Task.Run(() => DrrGenerator.GeneratePA(_volume, projection, ct), ct);
                    _paDrrKey = cacheKey;
                }
                drr = _paDrr;
            }

            _activeDrr = drr;
            if (resetView)
            {
                _windowWidth = 1.0;
                _windowCenter = 0.5;
                _zoom = 1.0;
                _panX = 0;
                _panY = 0;
            }

            RenderDrr();
            if (resetView)
                FitToViewport();

            if (reprojectGeometry)
                ReprojectLandmarksAndMeasurements2D();

            StatusLeft.Text = "Ready — Left-click place landmark, right-drag W/L (Shift+right-click on dot to delete)";
            UpdateStatusInfo();
            RefreshAnalysis();
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
                    SyncLandmarksToVm();
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
            // Drag ended: persist the final position to the ViewModel
            SyncLandmarksToVm();
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
        if (e.OriginalSource is Ellipse dot && dot.Tag is CephalometricLandmark lm
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            lm.Position = null;
            lm.Position3D = null;
            RefreshLandmarkOverlay();
            if (_is3DMode) Refresh3DLandmarks();
            UpdateLandmarkSidebarItem(lm);
            SyncLandmarksToVm();
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

    // ═══════════════════════════════════════════════════════════════════════════
    // 3D Mode: Toggle
    // ═══════════════════════════════════════════════════════════════════════════

    private void ChkOrtho_Changed(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mw)
            mw.OnProjectionChanged(sender, e);
    }

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
                ? "3D — Left-click place/drag landmark (Shift+right-click near dot to delete)"
                : "3D — Click to measure (Esc to cancel)";
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
            StatusLeft.Text = "2D — Left-click place/drag landmark, right-drag W/L (Shift+right-click on dot to delete)";
        }

        UpdateViewportGridHitTest();
        DrawCephGrid();
    }

    private HelixToolkit.Wpf.SharpDX.Viewport3DX? SharedViewport3D =>
        (Application.Current.MainWindow as MainWindow)?.MainViewport;

    // ─── 3D viewport pass-through / placement routing ────────────────────────
    // In 3D mode the central ViewportGrid is never hit-testable: the real Helix
    // viewport receives ALL mouse input directly (rotate / pan / zoom for every
    // button). Placement is handled by Preview handlers attached straight to the
    // shared viewport, which consume the left click only when a tool/landmark is
    // actually being placed. No synthetic event forwarding (RaiseEvent) is used.
    // In 2D mode, the DRR canvas remains fully interactive.

    private void UpdateViewportGridHitTest()
    {
        // In 3D mode the grid overlay is normally mouse-transparent so the Helix
        // viewport gets native navigation. When "Move Grid" is active we let the
        // ViewportGrid capture input so the grid can be dragged over the 3D view too.
        bool gridDrag = BtnGridDrag?.IsChecked == true
                        && CephGridOverlay?.Visibility == Visibility.Visible;
        bool shouldCapture = !_is3DMode || gridDrag;

        ViewportGrid.IsHitTestVisible = shouldCapture;
        ViewportGrid.Background = shouldCapture ? Brushes.Transparent : null;
    }

    private HelixToolkit.Wpf.SharpDX.Viewport3DX? _hookedViewport;

    private void AttachViewportPlacementHandlers()
    {
        var vp = SharedViewport3D;
        if (vp == null || _hookedViewport == vp) return;
        DetachViewportPlacementHandlers();

        _hookedViewport = vp;
        vp.PreviewMouseLeftButtonDown += Viewport3D_PreviewMouseLeftButtonDown;
        vp.PreviewMouseLeftButtonUp   += OnViewport3DMouseLeftButtonUp;
        vp.PreviewMouseMove           += Viewport3D_PreviewMouseMove;
        vp.PreviewMouseRightButtonDown += Viewport3D_PreviewMouseRightButtonDown;
    }

    private void DetachViewportPlacementHandlers()
    {
        if (_hookedViewport == null) return;
        _hookedViewport.PreviewMouseLeftButtonDown -= Viewport3D_PreviewMouseLeftButtonDown;
        _hookedViewport.PreviewMouseLeftButtonUp   -= OnViewport3DMouseLeftButtonUp;
        _hookedViewport.PreviewMouseMove           -= Viewport3D_PreviewMouseMove;
        _hookedViewport.PreviewMouseRightButtonDown -= Viewport3D_PreviewMouseRightButtonDown;
        _hookedViewport = null;
    }

    // ── Cephalometry reference grid ───────────────────────────────────────────

    private MainViewModel? GetMainVm() =>
        (Application.Current.MainWindow as MainWindow)?.DataContext as MainViewModel;

    private void SubscribeMainVm()
    {
        var vm = GetMainVm();
        if (vm == null || vm == _subscribedVm) return;
        UnsubscribeMainVm();
        _subscribedVm = vm;
        _subscribedVm.PropertyChanged += OnMainVmPropertyChanged;
        _subscribedVm.NhpCommitted += OnNhpCommitted;
        _syncingGridCheckbox = true;
        ChkShowGrid.IsChecked = _showCephGrid;
        _syncingGridCheckbox = false;
        UpdateCephGrid();
    }

    private void UnsubscribeMainVm()
    {
        if (_subscribedVm == null) return;
        _subscribedVm.PropertyChanged -= OnMainVmPropertyChanged;
        _subscribedVm.NhpCommitted -= OnNhpCommitted;
        _subscribedVm = null;
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The cephalometry grid is intentionally local to this overlay. Do not
        // mirror MainViewModel.ShowGrid here, otherwise the main viewport grid
        // renders underneath and creates a double-grid layer.
        if (e.PropertyName == nameof(MainViewModel.ShowGrid) && IsVisible)
            Dispatcher.InvokeAsync(() => SetMainViewportGridLayerVisible(false));
        else if (e.PropertyName == nameof(MainViewModel.NhpPreviewTransform))
            Dispatcher.InvokeAsync(ApplyNhpToCephalometryVisuals);
    }

    private void OnNhpCommitted(Matrix3D delta)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // MainViewModel has already baked and saved landmark coordinates.
            RestoreLandmarkData();

            foreach (var measurement in _measurements3D)
            {
                for (int i = 0; i < measurement.Pts.Count; i++)
                    measurement.Pts[i] = TransformPoint(measurement.Pts[i], delta);
            }

            for (int i = 0; i < _pending3DPts.Count; i++)
                _pending3DPts[i] = TransformPoint(_pending3DPts[i], delta);

            foreach (var plane in _toolState.Measurements)
            {
                if (plane.PlaneOrigin3D is { } origin)
                    plane.PlaneOrigin3D = TransformPoint(origin, delta);
                if (plane.PlaneNormal3D is { } normal)
                    plane.PlaneNormal3D = TransformVector(normal, delta);
                if (plane.PlaneAxisU3D is { } axisU)
                    plane.PlaneAxisU3D = TransformVector(axisU, delta);
                if (plane.PlaneAxisV3D is { } axisV)
                    plane.PlaneAxisV3D = TransformVector(axisV, delta);
            }

            Refresh3DLandmarks();
            Refresh3DMeasurements();
            InvalidateDrrCache();
            EnsureGeometry3DFrom2D();
            bool lateral = CmbProjection.SelectedIndex == 0;
            _ = GenerateDrrAsync(lateral, resetView: false, reprojectGeometry: true);
        });
    }

    private void ChkShowGrid_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingGridCheckbox) return;
        _showCephGrid = ChkShowGrid.IsChecked == true;
        UpdateCephGrid();
    }

    private void UpdateCephGrid()
    {
        bool show = _showCephGrid;
        CephGridOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BtnGridDrag.IsEnabled = show;

        if (!show)
        {
            BtnGridDrag.IsChecked = false;
            CephGridOverlay.IsHitTestVisible = false;
            _isDraggingCephGrid = false;
        }
        else
        {
            CephGridOverlay.IsHitTestVisible = BtnGridDrag.IsChecked == true;
            DrawCephGrid();
        }
        UpdateViewportGridHitTest();
    }

    private void DrawCephGrid()
    {
        if (CephGridOverlay.Visibility != Visibility.Visible) return;
        ScreenGridRenderer.Draw(
            CephGridOverlay,
            ViewportGrid.ActualWidth,
            ViewportGrid.ActualHeight,
            _cephGridCenter);
    }

    // ── Grid drag (screen-space reposition, shared with main-viewport behaviour) ──

    private void BtnGridDrag_Changed(object sender, RoutedEventArgs e)
    {
        bool dragging = BtnGridDrag.IsChecked == true
                        && CephGridOverlay.Visibility == Visibility.Visible;
        CephGridOverlay.IsHitTestVisible = dragging;
        if (!dragging) _isDraggingCephGrid = false;
        UpdateViewportGridHitTest();

        StatusLeft.Text = dragging
            ? "Move Grid: drag to reposition. Right-click the button for center options."
            : "";
    }

    private System.Windows.Point CephGridResolvedCenter() =>
        _cephGridCenter.X >= 0
            ? _cephGridCenter
            : new System.Windows.Point(ViewportGrid.ActualWidth / 2.0, ViewportGrid.ActualHeight / 2.0);

    private void CephGridOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BtnGridDrag.IsChecked != true) return;
        _isDraggingCephGrid = true;
        _cephGridDragStart = e.GetPosition(CephGridOverlay);
        _cephGridDragInitialCenter = CephGridResolvedCenter();
        CephGridOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void CephGridOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingCephGrid) return;
        var pos = e.GetPosition(CephGridOverlay);
        _cephGridCenter = new System.Windows.Point(
            _cephGridDragInitialCenter.X + (pos.X - _cephGridDragStart.X),
            _cephGridDragInitialCenter.Y + (pos.Y - _cephGridDragStart.Y));
        DrawCephGrid();
        e.Handled = true;
    }

    private void CephGridOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingCephGrid) return;
        _isDraggingCephGrid = false;
        CephGridOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CephGrid_SetCenter_Click(object sender, RoutedEventArgs e) =>
        _cephGridNewCenter = CephGridResolvedCenter();

    private void CephGrid_Recentre_Click(object sender, RoutedEventArgs e)
    {
        _cephGridCenter = _cephGridNewCenter.X >= 0
            ? _cephGridNewCenter
            : new System.Windows.Point(-1, -1);
        DrawCephGrid();
    }

    private void CephGrid_Reset_Click(object sender, RoutedEventArgs e)
    {
        _cephGridCenter = new System.Windows.Point(-1, -1);
        _cephGridNewCenter = new System.Windows.Point(-1, -1);
        DrawCephGrid();
    }

    // ─── Ceph overlay visibility: refresh spheres on enter, hide on exit ────

    private void OnCephVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            SubscribeMainVm();
            SetMainViewportGridLayerVisible(false);
            AttachViewportPlacementHandlers();
            if (_initialized) Refresh3DLandmarks();
            UpdateViewportGridHitTest();
            DrawCephGrid();
        }
        else
        {
            UnsubscribeMainVm();
            DetachViewportPlacementHandlers();
            CephGridOverlay.Visibility = Visibility.Collapsed;
            var vm = GetMainVm();
            SetMainViewportGridLayerVisible(vm?.ShowGrid == true);
            bool keepVisible = vm?.ShowCephLandmarksIn3D ?? false;
            foreach (var s in _landmarkSpheres3D) s.IsRendering = keepVisible;
        }
    }

    private static void SetMainViewportGridLayerVisible(bool visible)
    {
        if (Application.Current.MainWindow is not MainWindow mainWindow) return;
        if (mainWindow.FindName("GridOverlay") is not UIElement gridOverlay) return;
        gridOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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

        var screenPos = e.GetPosition(vp);
        var hits = vp.FindHits(screenPos);
        if (hits == null || hits.Count == 0) return;

        if (_toolState.ActiveTool == CephTool.Select)
        {
            // Drag an existing landmark sphere
            foreach (var hit in hits)
            {
                if (hit.ModelHit is MeshGeometryModel3D marker
                    && _landmarkSphereMap.TryGetValue(marker, out var lm))
                {
                    _isDraggingLandmark = true;
                    _draggingLandmark = lm;
                    _activeLandmark = lm;
                    RebuildSidebarHighlights();
                    Refresh3DLandmarks();
                    e.Handled = true;
                    return;
                }
            }

            // Click on mesh surface → place the active sidebar landmark
            if (_activeLandmark != null)
            {
                foreach (var hit in hits)
                {
                    if (!hit.IsValid) continue;
                    if (hit.ModelHit is MeshGeometryModel3D m && _landmarkSphereMap.ContainsKey(m)) continue;

                    _pending3DHit = (Vector3)hit.PointHit;
                    _mouseDown3DScreenPos = e.GetPosition(ViewportGrid);
                    e.Handled = true;
                    return;
                }
            }

            return;
        }

        // Measurement tools: capture first valid mesh hit
        var meshHit = hits.FirstOrDefault(h => h.IsValid);
        if (meshHit != null)
        {
            _pending3DHit = (Vector3)meshHit.PointHit;
            _mouseDown3DScreenPos = e.GetPosition(ViewportGrid);
            e.Handled = true;
        }
    }

    private void Viewport3D_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_is3DMode || !_isDraggingLandmark || _draggingLandmark == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var vp = SharedViewport3D;
        if (vp == null) return;

        var hits = vp.FindHits(e.GetPosition(vp));
        if (hits == null) return;

        foreach (var hit in hits)
        {
            if (!hit.IsValid) continue;
            if (hit.ModelHit is MeshGeometryModel3D marker && _landmarkSphereMap.ContainsKey(marker)) continue;

            var baked = WorldToBaked((Vector3)hit.PointHit);
            _draggingLandmark.Position3D = (baked.X, baked.Y, baked.Z);
            var pos2D = Project3DTo2D(baked.X, baked.Y, baked.Z);
            if (pos2D.HasValue) _draggingLandmark.Position = pos2D.Value;

            Refresh3DLandmarks();
            RefreshLandmarkOverlay();
            UpdateLandmarkSidebarItem(_draggingLandmark);
            e.Handled = true;
            return;
        }
    }

    private void OnViewport3DMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_is3DMode && _isDraggingLandmark && _draggingLandmark != null)
        {
            _isDraggingLandmark = false;
            _draggingLandmark = null;
            SyncLandmarksToVm();
            e.Handled = true;
            return;
        }

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
        var baked = WorldToBaked(hit);
        _activeLandmark.Position3D = (baked.X, baked.Y, baked.Z);
        var pos2D = Project3DTo2D(baked.X, baked.Y, baked.Z);
        if (pos2D.HasValue) _activeLandmark.Position = pos2D.Value;
        Refresh3DLandmarks();
        RefreshLandmarkOverlay();
        UpdateLandmarkSidebarItem(_activeLandmark);
        AdvanceToNextLandmark();
        SyncLandmarksToVm();
    }

    /// <summary>
    /// Shift+right-click near a placed landmark sphere deletes it; plain right-click is left to
    /// HelixToolkit for camera pan/zoom.
    /// </summary>
    private void Viewport3D_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_is3DMode || (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            return;
        if (_landmarks == null) return;

        var vp = SharedViewport3D;
        if (vp == null) return;

        var hit = vp.FindHits(e.GetPosition(vp)).FirstOrDefault();
        if (hit == null || !hit.IsValid) return;

        var hitPt = (Vector3)hit.PointHit;
        CephalometricLandmark? nearest = null;
        double bestDist = 3.0; // mm — must click close to the sphere

        foreach (var lm in _landmarks)
        {
            if (!lm.IsPlaced3D) continue;
            var (px, py, pz) = lm.Position3D!.Value;
            var world = BakedToWorld(px, py, pz);
            double d = Vector3.Distance(hitPt, new Vector3((float)world.X, (float)world.Y, (float)world.Z));
            if (d < bestDist)
            {
                bestDist = d;
                nearest = lm;
            }
        }

        if (nearest == null) return;

        nearest.Position = null;
        nearest.Position3D = null;
        Refresh3DLandmarks();
        RefreshLandmarkOverlay();
        UpdateLandmarkSidebarItem(nearest);
        SyncLandmarksToVm();
        e.Handled = true;
    }

    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ
    // 3D -> 2D Projection
    // ÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉÔòÉ

    /// <summary>
    /// Projects a baked 3D physical-space point (mm) onto the 2D DRR image pixel coordinates.
    /// </summary>
    private (double X, double Y)? Project3DTo2D(double physX, double physY, double physZ)
    {
        if (_volume == null || _activeDrr == null) return null;

        bool isLateral = CmbProjection.SelectedIndex == 0;

        double drrCol, drrRow;
        if (isLateral)
        {
            drrCol = (_activeDrr.SpaceMaxY - physY) / _activeDrr.SpacingX;
            drrRow = (_activeDrr.SpaceMaxZ - physZ) / _activeDrr.SpacingY;
        }
        else
        {
            drrCol = (physX - _activeDrr.SpaceMinX) / _activeDrr.SpacingX;
            drrRow = (_activeDrr.SpaceMaxZ - physZ) / _activeDrr.SpacingY;
        }

        drrCol -= _activeDrr.CropOffsetX;
        drrRow -= _activeDrr.CropOffsetY;

        if (drrCol < 0 || drrCol >= _activeDrr.Width || drrRow < 0 || drrRow >= _activeDrr.Height)
            return null;

        return (drrCol, drrRow);
    }

    /// <summary>
    /// Projects a DRR pixel coordinate to a baked 3D physical-space point (mm).
    /// Uses the midplane of the projection along the ray axis.
    /// </summary>
    private Vector3? Project2DTo3D(double drrCol, double drrRow)
    {
        if (_volume == null || _activeDrr == null) return null;

        double col = drrCol + _activeDrr.CropOffsetX;
        double row = drrRow + _activeDrr.CropOffsetY;
        bool isLateral = CmbProjection.SelectedIndex == 0;

        if (isLateral)
        {
            double y = _activeDrr.SpaceMaxY - col * _activeDrr.SpacingX;
            double z = _activeDrr.SpaceMaxZ - row * _activeDrr.SpacingY;
            double x = (_activeDrr.SpaceMinX + _activeDrr.SpaceMaxX) / 2.0;
            return new Vector3((float)x, (float)y, (float)z);
        }

        double xPa = _activeDrr.SpaceMinX + col * _activeDrr.SpacingX;
        double zPa = _activeDrr.SpaceMaxZ - row * _activeDrr.SpacingY;
        double yPa = (_activeDrr.SpaceMinY + _activeDrr.SpaceMaxY) / 2.0;
        return new Vector3((float)xPa, (float)yPa, (float)zPa);
    }

    private void EnsureGeometry3DFrom2D()
    {
        if (_landmarks == null || _activeDrr == null) return;

        foreach (var lm in _landmarks)
        {
            if (lm.IsPlaced && !lm.IsPlaced3D && lm.Position is { } pos)
            {
                if (Project2DTo3D(pos.X, pos.Y) is Vector3 v)
                    lm.Position3D = (v.X, v.Y, v.Z);
            }
        }

        foreach (var m in _toolState.Measurements)
        {
            if (_measurements3D.Any(x => x.Label == m.Label) || m.Points.Count == 0)
                continue;

            var pts3d = new List<Vector3>();
            foreach (var pt in m.Points)
            {
                if (Project2DTo3D(pt.X, pt.Y) is Vector3 v)
                    pts3d.Add(v);
            }

            if (pts3d.Count == 0) continue;
            _measurements3D.Add(new Meas3D
            {
                Tool = m.ToolType,
                Label = m.Label,
                Pts = pts3d,
                Value = m.Value,
                Unit = m.Unit,
                Color = Color.FromRgb(m.ColorR, m.ColorG, m.ColorB),
            });
        }
    }

    private void ReprojectLandmarksAndMeasurements2D()
    {
        if (_landmarks != null)
        {
            foreach (var lm in _landmarks)
            {
                if (!lm.IsPlaced3D || lm.Position3D is not { } p3) continue;
                var p2d = Project3DTo2D(p3.X, p3.Y, p3.Z);
                if (p2d.HasValue)
                    lm.Position = p2d.Value;
            }
        }

        foreach (var m in _toolState.Measurements)
        {
            var m3 = _measurements3D.FirstOrDefault(x => x.Label == m.Label);
            if (m3 == null || m3.Pts.Count == 0) continue;

            m.Points.Clear();
            foreach (var p in m3.Pts)
            {
                var p2d = Project3DTo2D(p.X, p.Y, p.Z);
                if (p2d.HasValue)
                    m.Points.Add(new CephPoint(p2d.Value.X, p2d.Value.Y));
            }
        }

        RefreshLandmarkOverlay();
        RefreshMeasurementOverlay();
        SyncLandmarksToVm();
        MeasurementsChanged?.Invoke();
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
        _landmarkSphereMap.Clear();

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
                IsHitTestVisible = true,
            };

            _landmarkSpheres3D.Add(sphere);
            _landmarkSphereMap[sphere] = lm;
            SharedViewport3D?.Items.Add(sphere);
        }

        ApplyNhpToCephalometryVisuals();
    }

    /// <summary>
    /// Applies the current NHP preview transform to landmark spheres so they move with
    /// mesh segments during slider preview (same pattern as ApplyNhpToAllTrackedObjects).
    /// Stored landmark coordinates remain in baked volume space.
    /// </summary>
    private void ApplyNhpToCephalometryVisuals()
    {
        var transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity;
        foreach (var sphere in _landmarkSpheres3D)
            sphere.Transform = transform;
        foreach (var visual in _meas3DVisuals)
            visual.Transform = transform;
        foreach (var sphere in _pending3DSpheres)
            sphere.Transform = transform;
    }

    private Point3D BakedToWorld(double x, double y, double z)
    {
        var transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity;
        return transform.Transform(new Point3D(x, y, z));
    }

    private Point3D WorldToBaked(Vector3 world)
    {
        var transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity;
        if (transform.Value.IsIdentity)
            return new Point3D(world.X, world.Y, world.Z);

        var inverse = transform.Value;
        if (!inverse.HasInverse)
            return new Point3D(world.X, world.Y, world.Z);

        inverse.Invert();
        return inverse.Transform(new Point3D(world.X, world.Y, world.Z));
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
            UpdateViewportGridHitTest();
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
        MeasurementsChanged?.Invoke();
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
        {
            if (m.IsVisible) DrawMeasurement(m);
        }

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
        var alpha = (byte)Math.Round(Math.Clamp(m.Opacity, 0.0, 1.0) * 255.0);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, m.ColorR, m.ColorG, m.ColorB));
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

            case CephTool.InfinitePlane when m.Points.Count >= 2 || m.PlaneOrigin3D != null:
                if (m.PlaneKind != CephPlaneKind.Manual && TryProjectGeneratedPlaneLine(m, out var ga, out var gb))
                {
                    DrawInfiniteLine(ga, gb, brush, strokeW, m);
                    var gmid = new CephPoint((ga.X + gb.X) / 2, (ga.Y + gb.Y) / 2);
                    DrawMeasurementLabel(gmid, 0, -4, m.Label, brush, m);
                }
                else if (m.Points.Count >= 2)
                {
                    DrawInfiniteLine(m.Points[0], m.Points[1], brush, strokeW, m);
                    DrawMeasurementDot(m.Points[0], 1, brush, m);
                    DrawMeasurementDot(m.Points[1], 1, brush, m);
                    var pmid = new CephPoint((m.Points[0].X + m.Points[1].X) / 2,
                                             (m.Points[0].Y + m.Points[1].Y) / 2);
                    DrawMeasurementLabel(pmid, 0, -4, m.Label, brush, m);
                }
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

    private bool TryProjectGeneratedPlaneLine(
        CephMeasurement plane,
        out CephPoint a,
        out CephPoint b)
    {
        a = default;
        b = default;
        if (plane.PlaneOrigin3D == null) return false;

        var origin = ToVector3(plane.PlaneOrigin3D.Value);
        var axis = CmbProjection.SelectedIndex == 0
            ? plane.PlaneAxisV3D
            : plane.PlaneAxisU3D;
        if (axis == null) return false;

        var dir = ToVector3(axis.Value);
        if (dir.Length() < 1e-4f) return false;
        dir = Vector3.Normalize(dir);

        // DrawInfiniteLine extends this segment to the overlay bounds.
        var p1 = origin - dir * 80f;
        var p2 = origin + dir * 80f;
        var p1Proj = Project3DTo2D(p1.X, p1.Y, p1.Z);
        var p2Proj = Project3DTo2D(p2.X, p2.Y, p2.Z);
        if (!p1Proj.HasValue || !p2Proj.HasValue) return false;

        a = new CephPoint(p1Proj.Value.X, p1Proj.Value.Y);
        b = new CephPoint(p2Proj.Value.X, p2Proj.Value.Y);
        return true;
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
                Refresh3DMeasurements();
                MeasurementsChanged?.Invoke();
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
        InvalidateDrrCache();
        _ = GenerateDrrAsync(CmbProjection.SelectedIndex == 0, resetView: true);
    }

    private void ChkInvert_Changed(object sender, RoutedEventArgs e)
    {
        _inverted = ChkInvert.IsChecked == true;
        RenderDrr();
    }

    private void AddFrankfortPlane_Click(object sender, RoutedEventArgs e)
    {
        if (_landmarks == null)
        {
            StatusLeft.Text = "Load cephalometry landmarks before creating Frankfort plane.";
            return;
        }

        if (!CephPlaneBuilder.TryBuildFrankfortHorizontal(_landmarks, out var plane, out var error))
        {
            StatusLeft.Text = error;
            MessageBox.Show(error, "Frankfort Plane", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = _toolState.Measurements.FirstOrDefault(
            m => m.PlaneKind == CephPlaneKind.FrankfortHorizontal);
        if (existing != null)
            _toolState.Measurements.Remove(existing);

        _toolState.Measurements.Add(plane);
        _toolState.SelectedMeasurement = plane;
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        Refresh3DMeasurements();
        MeasurementsChanged?.Invoke();
        StatusLeft.Text = "Frankfort plane created from Porion (L/R) and Orbitale (L/R).";
    }

    private void EditNhp_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenNhpEditor();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var result = MessageBox.Show(
            owner,
            "This will clear all cephalometric landmarks, measurements, pending tools, and analysis values. Continue?",
            "Reset cephalometry?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
            ResetCephalometry();
    }

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

    private void ResetCephalometry()
    {
        _toolState.Reset();
        _rubberBandEnd = null;
        _activeLandmark = null;
        _draggingDot = null;
        _draggingLandmark = null;
        _isDraggingLandmark = false;
        _pending3DHit = null;
        _measurements3D.Clear();
        ClearPending3DSpheres();

        if (_landmarks != null)
        {
            foreach (var lm in _landmarks)
            {
                lm.Position = null;
                lm.Position3D = null;
            }
        }

        var vm = GetMainVm();
        if (vm != null)
            vm.SavedCephLandmarks = new List<CephLandmarkSave>();

        BuildLandmarkSidebar();
        RefreshLandmarkOverlay();
        RefreshMeasurementOverlay();
        RefreshMeasurementPanel();
        Refresh3DLandmarks();
        Refresh3DMeasurements();
        RefreshAnalysis();
        MeasurementsChanged?.Invoke();
        UpdateToolButtonHighlights();
        UpdateToolStatus();
        StatusLeft.Text = "Cephalometry reset.";
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
        var bakedPoint = WorldToBaked(hit);
        var baked = new Vector3((float)bakedPoint.X, (float)bakedPoint.Y, (float)bakedPoint.Z);
        _pending3DPts.Add(baked);

        // Show pending sphere at hit point
        var pendingSphere = Make3DSphere(baked, 1.5f, System.Windows.Media.Colors.White);
        pendingSphere.Transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity;
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

        foreach (var plane in _toolState.Measurements)
        {
            if (!plane.IsVisible || plane.PlaneOrigin3D == null || plane.PlaneNormal3D == null)
                continue;
            Add3DVisual(Make3DPlane(plane));
        }
    }

    private void Add3DVisual(Element3D el)
    {
        el.Transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity;
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

    private MeshGeometryModel3D Make3DPlane(CephMeasurement plane)
    {
        var origin = ToVector3(plane.PlaneOrigin3D!.Value);
        var axisU = plane.PlaneAxisU3D is { } u ? Vector3.Normalize(ToVector3(u)) : Vector3.UnitX;
        var axisV = plane.PlaneAxisV3D is { } v ? Vector3.Normalize(ToVector3(v)) : Vector3.UnitY;
        var size = EstimatePlaneDisplaySize();
        var halfU = axisU * (float)(size * 0.5);
        var halfV = axisV * (float)(size * 0.5);

        var a = origin - halfU - halfV;
        var b = origin + halfU - halfV;
        var c = origin + halfU + halfV;
        var d = origin - halfU + halfV;

        var builder = new HelixToolkit.Geometry.MeshBuilder();
        builder.AddTriangle(a, b, c);
        builder.AddTriangle(a, c, d);

        var alpha = (float)Math.Clamp(plane.Opacity, 0.05, 1.0);
        var color = new HelixToolkit.Maths.Color4(
            plane.ColorR / 255f, plane.ColorG / 255f, plane.ColorB / 255f, alpha);

        return new MeshGeometryModel3D
        {
            Geometry = HxGeom.Converter.ToMeshGeometry3D(builder.ToMesh()),
            Material = new PhongMaterial
            {
                DiffuseColor = color,
                EmissiveColor = new HelixToolkit.Maths.Color4(
                    plane.ColorR / 255f * 0.12f,
                    plane.ColorG / 255f * 0.12f,
                    plane.ColorB / 255f * 0.12f,
                    alpha)
            },
            Transform = GetMainVm()?.NhpPreviewTransform ?? Transform3D.Identity,
            CullMode = SharpDX.Direct3D11.CullMode.None,
            IsTransparent = alpha < 0.98f,
            IsHitTestVisible = false
        };
    }

    private double EstimatePlaneDisplaySize()
    {
        var bounds = GetMainVm()?.BoneOnlyBounds ?? Rect3D.Empty;
        if (!bounds.IsEmpty)
            return Math.Max(80.0, Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) * 1.2);
        return 180.0;
    }

    private static Vector3 ToVector3(CephPoint3D point) =>
        new((float)point.X, (float)point.Y, (float)point.Z);

    private static Vector3 TransformPoint(Vector3 point, Matrix3D matrix)
    {
        var transformed = matrix.Transform(new Point3D(point.X, point.Y, point.Z));
        return new Vector3((float)transformed.X, (float)transformed.Y, (float)transformed.Z);
    }

    private static CephPoint3D TransformPoint(CephPoint3D point, Matrix3D matrix)
    {
        var transformed = matrix.Transform(new Point3D(point.X, point.Y, point.Z));
        return new CephPoint3D(transformed.X, transformed.Y, transformed.Z);
    }

    private static CephPoint3D TransformVector(CephPoint3D vector, Matrix3D matrix)
    {
        var transformed = matrix.Transform(new Vector3D(vector.X, vector.Y, vector.Z));
        return new CephPoint3D(transformed.X, transformed.Y, transformed.Z);
    }

    private void ClearPending3DSpheres()
    {
        foreach (var s in _pending3DSpheres) SharedViewport3D?.Items.Remove(s);
        _pending3DSpheres.Clear();
        _pending3DPts.Clear();
    }

    // ════════════════════════════════════════════════════════════════════
    // Landmark persistence helpers (Issue 10 fix)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Writes the current in-memory landmark list into MainViewModel.SavedCephLandmarks
    /// so that ProjectViewModel can include it in the .orthoplan ZIP.
    /// Call this after every landmark place / move / delete.
    /// </summary>
    private void SyncLandmarksToVm()
    {
        var vm = (Application.Current.MainWindow as MainWindow)?.DataContext as ViewModels.MainViewModel;
        if (vm == null || _landmarks == null) return;

        vm.SavedCephLandmarks = _landmarks
            .Select(lm => new ViewModels.CephLandmarkSave(
                lm.Name,
                lm.Position?.X,   lm.Position?.Y,
                lm.Position3D?.X, lm.Position3D?.Y, lm.Position3D?.Z))
            .ToList();

        RefreshAnalysis();
    }

    /// <summary>
    /// Recomputes Steiner / Tweed / Ricketts from the current landmark set and DRR spacing.
    /// </summary>
    private void RefreshAnalysis()
    {
        if (_landmarks == null || _activeDrr == null)
            return;

        double sx = _activeDrr.SpacingX;
        double sy = _activeDrr.SpacingY;

        _analysisPanel.Update(
            CephAnalysisEngine.Compute(CephAnalysisType.Steiner, _landmarks, sx, sy),
            CephAnalysisEngine.Compute(CephAnalysisType.Tweed, _landmarks, sx, sy),
            CephAnalysisEngine.Compute(CephAnalysisType.Ricketts, _landmarks, sx, sy));
    }

    /// <summary>
    /// Reads MainViewModel.SavedCephLandmarks and restores positions onto the freshly
    /// created _landmarks list.  Call this at the end of SetVolume(), before BuildLandmarkSidebar().
    /// </summary>
    private void RestoreLandmarkData()
    {
        var vm = (Application.Current.MainWindow as MainWindow)?.DataContext as ViewModels.MainViewModel;
        if (vm == null || _landmarks == null || vm.SavedCephLandmarks.Count == 0) return;

        var byName = vm.SavedCephLandmarks.ToDictionary(s => s.Name);
        foreach (var lm in _landmarks)
        {
            if (!byName.TryGetValue(lm.Name, out var saved)) continue;
            if (saved.X2D.HasValue && saved.Y2D.HasValue)
                lm.Position = (saved.X2D.Value, saved.Y2D.Value);
            if (saved.X3D.HasValue && saved.Y3D.HasValue && saved.Z3D.HasValue)
                lm.Position3D = (saved.X3D.Value, saved.Y3D.Value, saved.Z3D.Value);
        }
    }
}
