using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrthoPlanner.App.Helpers;

public enum ViewportRotationKind { Yaw, Roll, Pitch }

/// <summary>Custom mouse cursors for viewport corner rotation zones.</summary>
public static class RotationCursors
{
    private static Cursor? _yaw, _roll, _pitch;

    public static Cursor Get(ViewportRotationKind kind) => kind switch
    {
        ViewportRotationKind.Yaw   => _yaw   ??= CreateYaw(),
        ViewportRotationKind.Roll  => _roll  ??= CreateRoll(),
        ViewportRotationKind.Pitch => _pitch ??= CreatePitch(),
        _ => Cursors.Arrow
    };

    private static Cursor CreateYaw() =>
        CreateFromDrawer(dc =>
        {
            var pen = MakePen();
            // Horizontal orbit arc (rotation around vertical / Z axis)
            dc.DrawArc(pen, new Point(8, 18), new Point(24, 18), new Size(8, 6), SweepDirection.Clockwise);
            DrawArrowHead(dc, pen, new Point(24, 18), new Vector(1, 0));
            DrawArrowHead(dc, pen, new Point(8, 18), new Vector(-1, 0));
            dc.DrawLine(pen, new Point(16, 6), new Point(16, 10));
            dc.DrawLine(pen, new Point(14, 8), new Point(16, 6));
            dc.DrawLine(pen, new Point(18, 8), new Point(16, 6));
        });

    private static Cursor CreateRoll() =>
        CreateFromDrawer(dc =>
        {
            var pen = MakePen();
            // Tilted orbit arc (roll / banking)
            dc.DrawArc(pen, new Point(7, 14), new Point(25, 20), new Size(9, 7), SweepDirection.Clockwise);
            DrawArrowHead(dc, pen, new Point(25, 20), new Vector(0.9, 0.35));
            dc.DrawLine(pen, new Point(16, 8), new Point(20, 12));
            dc.DrawLine(pen, new Point(16, 8), new Point(12, 12));
        });

    private static Cursor CreatePitch() =>
        CreateFromDrawer(dc =>
        {
            var pen = MakePen();
            // Vertical orbit arc (nod pitch)
            dc.DrawArc(pen, new Point(18, 8), new Point(18, 24), new Size(6, 8), SweepDirection.Clockwise);
            DrawArrowHead(dc, pen, new Point(18, 24), new Vector(0, 1));
            DrawArrowHead(dc, pen, new Point(18, 8), new Vector(0, -1));
            dc.DrawLine(pen, new Point(10, 16), new Point(14, 16));
            dc.DrawLine(pen, new Point(12, 14), new Point(10, 16));
            dc.DrawLine(pen, new Point(12, 18), new Point(10, 16));
        });

    private static Pen MakePen()
    {
        var pen = new Pen(Brushes.White, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static void DrawArrowHead(DrawingContext dc, Pen pen, Point tip, Vector dir)
    {
        dir.Normalize();
        var ortho = new Vector(-dir.Y, dir.X);
        var basePt = tip - dir * 5;
        dc.DrawLine(pen, tip, basePt + ortho * 3);
        dc.DrawLine(pen, tip, basePt - ortho * 3);
    }

    private static Cursor CreateFromDrawer(Action<DrawingContext> draw)
    {
        const int size = 32;
        const int hot = 16;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            draw(dc);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return CreateCursorFromBitmap(rtb, hot, hot);
    }

    private static Cursor CreateCursorFromBitmap(RenderTargetBitmap bitmap, int hotX, int hotY)
    {
        int w = bitmap.PixelWidth;
        int h = bitmap.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(pixels, stride, 0);

        int andRowBytes = ((w + 31) / 32) * 4;
        int xorSize = 40 + stride * h;
        int andSize = andRowBytes * h;
        int totalImageSize = xorSize + andSize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((ushort)0);
        bw.Write((ushort)2);
        bw.Write((ushort)1);

        bw.Write((byte)w);
        bw.Write((byte)h);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)hotX);
        bw.Write((ushort)hotY);
        bw.Write(totalImageSize);
        bw.Write(22);

        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        for (int row = h - 1; row >= 0; row--)
            bw.Write(pixels, row * stride, stride);

        for (int i = 0; i < andSize; i++)
            bw.Write((byte)0);

        ms.Position = 0;
        return new Cursor(ms);
    }
}

internal static class DrawingContextArcExtensions
{
    public static void DrawArc(this DrawingContext dc, Pen pen, Point start, Point end, Size radius, SweepDirection sweep)
    {
        var fig = new PathFigure(start, new[] { new ArcSegment(end, radius, 0, false, sweep, true) }, false);
        var geom = new PathGeometry(new[] { fig });
        geom.Freeze();
        dc.DrawGeometry(null, pen, geom);
    }
}
