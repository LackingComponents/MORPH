using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OrthoPlanner.App.Helpers;

/// <summary>Draws the same screen-space reference grid used in the main 3D viewport.</summary>
public static class ScreenGridRenderer
{
    public static void Draw(Canvas canvas, double width, double height, Point center)
    {
        canvas.Children.Clear();

        if (width < 10 || height < 10) return;

        double cx = center.X >= 0 ? center.X : width / 2.0;
        double cy = center.Y >= 0 ? center.Y : height / 2.0;
        const double spacing = 20.0;

        var thinBrush = Freeze(Color.FromArgb(30, 255, 255, 255));
        var midBrush = Freeze(Color.FromArgb(55, 255, 255, 255));
        var thickBrush = Freeze(Color.FromArgb(80, 255, 255, 255));
        var crossBrush = Freeze(Color.FromArgb(100, 100, 200, 255));

        for (double x = cx % spacing; x < width; x += spacing)
        {
            int idx = (int)Math.Round((x - cx) / spacing);
            var (brush, thick) = BrushForIndex(idx, thinBrush, midBrush, thickBrush, crossBrush);
            canvas.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = height,
                Stroke = brush, StrokeThickness = thick, IsHitTestVisible = false
            });
        }

        for (double y = cy % spacing; y < height; y += spacing)
        {
            int idx = (int)Math.Round((y - cy) / spacing);
            var (brush, thick) = BrushForIndex(idx, thinBrush, midBrush, thickBrush, crossBrush);
            canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = brush, StrokeThickness = thick, IsHitTestVisible = false
            });
        }
    }

    private static (Brush brush, double thick) BrushForIndex(
        int idx, Brush thin, Brush mid, Brush thick, Brush cross)
    {
        if (idx == 0) return (cross, 1.5);
        if (idx % 10 == 0) return (thick, 1.5);
        if (idx % 5 == 0) return (mid, 1.0);
        return (thin, 0.5);
    }

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
