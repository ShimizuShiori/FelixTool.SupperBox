using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FelixTool.SupperBox;

public static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var outline = CreateRoundedRectangle(1.5f, 2.5f, 29, 27, 7);
        using var background = new LinearGradientBrush(
            new PointF(2, 3),
            new PointF(30, 29),
            Color.FromArgb(255, 79, 70, 229),
            Color.FromArgb(255, 8, 145, 178));
        graphics.FillPath(background, outline);

        using var border = new Pen(Color.FromArgb(190, 224, 242, 254), 1.2f);
        graphics.DrawPath(border, outline);

        using var promptPen = new Pen(Color.White, 2.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(promptPen, [new PointF(9, 10), new PointF(15, 16), new PointF(9, 22)]);

        using var cursorPen = new Pen(Color.FromArgb(255, 165, 243, 252), 2.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(cursorPen, 18, 22, 24, 22);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(
        float x,
        float y,
        float width,
        float height,
        float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
