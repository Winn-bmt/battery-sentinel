using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatterySentinel.Core;
using Pen = System.Drawing.Pen;
using LinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;

namespace BatterySentinel.UI
{
    /// <summary>程序图标（窗口图标 + 托盘动态电量图标）。</summary>
    public static class AppIcon
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>窗口图标：圆角底 + 白色电池。</summary>
        public static ImageSource WindowIcon()
        {
            try
            {
                using (var bmp = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var path = RoundRect(new RectangleF(2, 2, 60, 60), 16))
                    using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(64, 64),
                        System.Drawing.Color.FromArgb(0x5B, 0x9B, 0xFF), System.Drawing.Color.FromArgb(0x3A, 0x6B, 0xD8)))
                    {
                        g.FillPath(brush, path);
                    }
                    DrawBattery(g, new RectangleF(16, 22, 28, 20), System.Drawing.Color.White, System.Drawing.Color.White, 100);
                    IntPtr h = bmp.GetHicon();
                    ImageSource src = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(h);
                    return src;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>托盘图标：电池形状，按电量着色；低电量时右上角红点。</summary>
        public static Icon TrayIcon(int level, bool critical, bool charging, bool lightTaskbar)
        {
            int size = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                System.Drawing.Color fg = lightTaskbar ? System.Drawing.Color.FromArgb(32, 32, 32) : System.Drawing.Color.FromArgb(242, 242, 244);
                System.Drawing.Color color = level < 0 ? fg
                    : level >= 60 ? System.Drawing.Color.FromArgb(0x3D, 0xDC, 0x84)
                    : level >= 30 ? System.Drawing.Color.FromArgb(0xF5, 0xC4, 0x51)
                    : level >= 15 ? System.Drawing.Color.FromArgb(0xFF, 0x9F, 0x45)
                    : System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x6B);

                float pad = size * 0.08f;
                float bodyW = size * 0.78f;
                float bodyH = size * 0.5f;
                var body = new RectangleF(pad, (size - bodyH) / 2 + size * 0.04f, bodyW, bodyH);
                float radius = Math.Max(1.5f, size * 0.11f);
                using (var pen = new Pen(fg, Math.Max(1.2f, size * 0.075f)))
                using (var path = RoundRect(body, radius))
                {
                    g.DrawPath(pen, path);
                }
                // 正极凸起
                using (var brush = new SolidBrush(fg))
                {
                    float tipW = Math.Max(1.5f, size * 0.07f);
                    float tipH = bodyH * 0.38f;
                    g.FillRectangle(brush, body.Right + size * 0.02f, body.Top + (bodyH - tipH) / 2, tipW, tipH);
                }
                if (level >= 0)
                {
                    float innerPad = Math.Max(1.5f, size * 0.1f);
                    float maxW = body.Width - innerPad * 2;
                    float w = Math.Max(1.2f, maxW * Math.Max(0, Math.Min(100, level)) / 100f);
                    using (var brush = new SolidBrush(color))
                    using (var inner = RoundRect(new RectangleF(body.X + innerPad, body.Y + innerPad, w, body.Height - innerPad * 2), Math.Max(1f, radius - 1)))
                    {
                        g.FillPath(brush, inner);
                    }
                }
                if (critical)
                {
                    float d = Math.Max(5f, size * 0.34f);
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0x4D, 0x4D)))
                    {
                        g.FillEllipse(brush, size - d - 0.5f, 0.5f, d, d);
                    }
                    using (var pen = new Pen(System.Drawing.Color.White, Math.Max(1f, size * 0.06f)))
                    {
                        float cx = size - d / 2 - 0.5f;
                        g.DrawLine(pen, cx, 0.5f + d * 0.28f, cx, 0.5f + d * 0.62f);
                        g.FillEllipse(System.Drawing.Brushes.White, cx - size * 0.035f, 0.5f + d * 0.72f, size * 0.07f, size * 0.07f);
                    }
                }
                if (charging)
                {
                    float d = Math.Max(6f, size * 0.42f);
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(0x3D, 0xDC, 0x84)))
                    {
                        g.FillEllipse(brush, size - d - 0.5f, size - d - 0.5f, d, d);
                    }
                    using (var pen = new Pen(System.Drawing.Color.FromArgb(20, 20, 20), Math.Max(1f, size * 0.055f)))
                    {
                        float cx = size - d / 2 - 0.5f, cy = size - d / 2 - 0.5f;
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx + d * 0.1f, cy - d * 0.22f),
                            new PointF(cx - d * 0.12f, cy + d * 0.02f),
                            new PointF(cx + d * 0.04f, cy + d * 0.02f),
                            new PointF(cx - d * 0.08f, cy + d * 0.24f)
                        });
                    }
                }
            }
            IntPtr hIcon = bmp.GetHicon();
            using (var tmp = Icon.FromHandle(hIcon))
            {
                var clone = (Icon)tmp.Clone();
                DestroyIcon(hIcon);
                bmp.Dispose();
                return clone;
            }
        }

        private static void DrawBattery(Graphics g, RectangleF body, System.Drawing.Color stroke, System.Drawing.Color fill, int level)
        {
            using (var pen = new Pen(stroke, 3f))
            using (var path = RoundRect(body, 5))
            {
                g.DrawPath(pen, path);
            }
            using (var brush = new SolidBrush(stroke))
            {
                g.FillRectangle(brush, body.Right + 2, body.Top + body.Height * 0.3f, 4, body.Height * 0.4f);
            }
            if (level > 0)
            {
                float w = (body.Width - 8) * level / 100f;
                using (var brush = new SolidBrush(fill))
                using (var inner = RoundRect(new RectangleF(body.X + 4, body.Y + 4, Math.Max(2, w), body.Height - 8), 2.5f))
                {
                    g.FillPath(brush, inner);
                }
            }
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
