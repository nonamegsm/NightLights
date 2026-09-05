using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NightLights
{
    // Two cached icons, created once rather than allocating a native icon every poll.
    internal sealed class TrayModeIcons : IDisposable
    {
        private readonly Icon _day;
        private readonly Icon _night;
        private bool _disposed;

        public TrayModeIcons()
        {
            _day = Create(false);
            try { _night = Create(true); }
            catch { _day.Dispose(); throw; }
        }

        public Icon ForNight(bool isNight)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TrayModeIcons));
            return isNight ? _night : _day;
        }

        private static Icon Create(bool isNight)
        {
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (isNight) DrawMoon(graphics);
                    else DrawSun(graphics);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (var borrowed = Icon.FromHandle(handle))
                        return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        private static void DrawSun(Graphics graphics)
        {
            using (var outline = new Pen(Color.FromArgb(132, 81, 14), 3.6f))
            using (var rays = new Pen(Color.FromArgb(255, 192, 55), 2.2f))
            using (var fill = new SolidBrush(Color.FromArgb(255, 192, 55)))
            using (var edge = new Pen(Color.FromArgb(132, 81, 14), 0.9f))
            {
                outline.StartCap = outline.EndCap = LineCap.Round;
                rays.StartCap = rays.EndCap = LineCap.Round;
                for (int ray = 0; ray < 8; ray++)
                {
                    double angle = ray * Math.PI / 4;
                    var inner = new PointF(16 + (float)Math.Cos(angle) * 10, 16 + (float)Math.Sin(angle) * 10);
                    var outer = new PointF(16 + (float)Math.Cos(angle) * 13, 16 + (float)Math.Sin(angle) * 13);
                    graphics.DrawLine(outline, inner, outer);
                    graphics.DrawLine(rays, inner, outer);
                }
                graphics.FillEllipse(fill, 9, 9, 14, 14);
                graphics.DrawEllipse(edge, 9, 9, 14, 14);
            }
        }

        private static void DrawMoon(Graphics graphics)
        {
            using (var moon = new GraphicsPath())
            using (var fill = new SolidBrush(Color.FromArgb(172, 208, 255)))
            using (var edge = new Pen(Color.FromArgb(48, 83, 141), 0.9f))
            {
                moon.AddBezier(21, 3, 7, 0, -1, 15, 7, 25);
                moon.AddBezier(7, 25, 15, 34, 28, 27, 29, 18);
                moon.AddBezier(29, 18, 19, 26, 9, 12, 21, 3);
                moon.CloseFigure();
                graphics.FillPath(fill, moon);
                graphics.DrawPath(edge, moon);
                var star = new[] { new PointF(26, 3), new PointF(27, 6), new PointF(30, 7),
                    new PointF(27, 8), new PointF(26, 11), new PointF(25, 8), new PointF(22, 7), new PointF(25, 6) };
                graphics.FillPolygon(fill, star);
                graphics.DrawPolygon(edge, star);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _day.Dispose();
            _night.Dispose();
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
