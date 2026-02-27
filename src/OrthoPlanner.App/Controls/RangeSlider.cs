using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrthoPlanner.App.Controls;

/// <summary>
/// A dual-thumb range slider built as a Canvas so it renders immediately
/// without needing a ControlTemplate in Generic.xaml.
/// </summary>
public class RangeSlider : Canvas
{
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register("Minimum", typeof(double), typeof(RangeSlider),
            new PropertyMetadata(-1024.0, OnChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register("Maximum", typeof(double), typeof(RangeSlider),
            new PropertyMetadata(3071.0, OnChanged));

    public static readonly DependencyProperty LowerValueProperty =
        DependencyProperty.Register("LowerValue", typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(200.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public static readonly DependencyProperty UpperValueProperty =
        DependencyProperty.Register("UpperValue", typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(3071.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => (double)GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => (double)GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }

    private readonly Rectangle _trackBg;
    private readonly Rectangle _trackFill;
    private readonly Ellipse _thumbL;
    private readonly Ellipse _thumbR;
    private bool _dragL, _dragR;

    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x8D, 0xAF));
    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x27, 0x30));
    private static readonly Brush DarkBrush = new SolidColorBrush(Color.FromRgb(0x0D, 0x11, 0x17));
    private const double Thumb = 14, Track = 4;

    public RangeSlider()
    {
        ClipToBounds = true;
        Height = 22;
        Background = Brushes.Transparent;

        _trackBg = new Rectangle { Height = Track, RadiusX = 2, RadiusY = 2, Fill = TrackBrush };
        _trackFill = new Rectangle { Height = Track, RadiusX = 2, RadiusY = 2, Fill = AccentBrush };
        _thumbL = MakeThumb();
        _thumbR = MakeThumb();

        Children.Add(_trackBg);
        Children.Add(_trackFill);
        Children.Add(_thumbL);
        Children.Add(_thumbR);

        SizeChanged += (_, _) => Layout();
        Loaded += (_, _) => Layout();
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseMove += OnMove;
    }

    private Ellipse MakeThumb() => new()
    {
        Width = Thumb, Height = Thumb,
        Fill = AccentBrush, Stroke = DarkBrush, StrokeThickness = 2,
        Cursor = Cursors.Hand
    };

    private void Layout()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double usable = w - Thumb;
        double range = Maximum - Minimum;
        if (range <= 0) return;

        double lf = Math.Clamp((LowerValue - Minimum) / range, 0, 1);
        double rf = Math.Clamp((UpperValue - Minimum) / range, 0, 1);
        double lx = lf * usable, rx = rf * usable;

        SetLeft(_trackBg, Thumb / 2); SetTop(_trackBg, (h - Track) / 2); _trackBg.Width = usable;
        SetLeft(_trackFill, lx + Thumb / 2); SetTop(_trackFill, (h - Track) / 2); _trackFill.Width = Math.Max(0, rx - lx);
        SetLeft(_thumbL, lx); SetTop(_thumbL, (h - Thumb) / 2);
        SetLeft(_thumbR, rx); SetTop(_thumbR, (h - Thumb) / 2);
    }

    private void OnDown(object s, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        double usable = ActualWidth - Thumb, range = Maximum - Minimum;
        double lx = (LowerValue - Minimum) / range * usable + Thumb / 2;
        double rx = (UpperValue - Minimum) / range * usable + Thumb / 2;

        if (Math.Abs(p.X - lx) <= Math.Abs(p.X - rx)) _dragL = true; else _dragR = true;
        CaptureMouse(); e.Handled = true;
    }

    private void OnUp(object s, MouseButtonEventArgs e)
    {
        _dragL = _dragR = false; ReleaseMouseCapture();
    }

    private void OnMove(object s, MouseEventArgs e)
    {
        if (!_dragL && !_dragR) return;
        double usable = ActualWidth - Thumb, range = Maximum - Minimum;
        double frac = Math.Clamp((e.GetPosition(this).X - Thumb / 2) / usable, 0, 1);
        double val = Math.Round(Minimum + frac * range);

        if (_dragL) { if (val > UpperValue) val = UpperValue; LowerValue = val; }
        else { if (val < LowerValue) val = LowerValue; UpperValue = val; }
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeSlider rs) rs.Layout();
    }
}
