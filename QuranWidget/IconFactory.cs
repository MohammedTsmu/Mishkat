using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;

namespace QuranWidget;

public static class IconFactory
{
    public static SD.Icon CreateIcon(int size)
    {
        using var bmp = Render(size);
        return SD.Icon.FromHandle(bmp.GetHicon());
    }

    public static ImageSource CreateImageSource(int size)
    {
        using var bmp = Render(size);
        var hbm = bmp.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero,
                System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hbm);
        }
    }

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    static SD.Bitmap Render(int size)
    {
        int s = Math.Max(32, size);
        var bmp = new SD.Bitmap(s, s);
        using var g = SD.Graphics.FromImage(bmp);
        g.SmoothingMode = SD2.SmoothingMode.AntiAlias;

        var rect = new SD.RectangleF(0, 0, s, s);
        using var bg = new SD2.LinearGradientBrush(rect,
            SD.Color.FromArgb(255, 26, 42, 32),
            SD.Color.FromArgb(255, 13, 22, 17), 60f);

        using (var path = RoundedRect(1, 1, s - 2, s - 2, s * 0.28f))
            g.FillPath(bg, path);

        // gold crescent
        var cx = s * 0.46f;
        var cy = s * 0.54f;
        var rad = s * 0.27f;
        using (var gold = new SD.SolidBrush(SD.Color.FromArgb(255, 232, 200, 119)))
        {
            g.FillEllipse(gold, cx - rad, cy - rad, rad * 2, rad * 2);
            g.FillEllipse(bg, cx - rad * 0.84f + rad * 0.52f, cy - rad * 0.84f - rad * 0.30f, rad * 1.68f, rad * 1.68f);
        }

        // eight-point star
        using (var gold = new SD.SolidBrush(SD.Color.FromArgb(255, 240, 212, 150)))
            g.FillPolygon(gold, Star(s * 0.75f, s * 0.18f, s * 0.088f));

        return bmp;
    }

    static SD.PointF[] Star(float cx, float cy, float rr)
    {
        var pts = new SD.PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double ang = Math.PI / 4 * i - Math.PI / 2;
            float rad = i % 2 == 0 ? rr : rr * 0.42f;
            pts[i] = new SD.PointF(cx + (float)Math.Cos(ang) * rad, cy + (float)Math.Sin(ang) * rad);
        }
        return pts;
    }

    static SD2.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new SD2.GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
