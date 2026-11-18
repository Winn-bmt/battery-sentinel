using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatterySentinel.Core;
using BatterySentinel.Hid;

namespace BatterySentinel.UI
{
    /// <summary>离屏渲染自检：把主界面/设置页/弹窗渲染成 PNG，便于检查排版。</summary>
    public static class RenderTest
    {
        public static void Run(AppController controller, string dir)
        {
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "render");
            Directory.CreateDirectory(dir);

            controller.Settings.WarnPercent = 20;
            controller.Settings.CriticalPercent = 10;
            controller.Settings.AutoRefresh = true;
            controller.Settings.IntervalSeconds = 60;
            Ui.AnimationsEnabled = false;

            var win = new MainWindow(controller);
            var content = (UIElement)win.Content;
            win.Content = null;
            var host = new Border { Background = Theme.Bg, Child = content };

            List<DeviceState> normal = Fake();
            controller.LastSnapshot = normal;
            win.OnSnapshot(normal);
            RenderElement(host, 940, 690, Path.Combine(dir, "main.png"), false);

            List<DeviceState> low = FakeLow();
            controller.LastSnapshot = low;
            win.OnSnapshot(low);
            RenderElement(host, 940, 690, Path.Combine(dir, "main-low.png"), false);
            RenderElement(host, 720, 620, Path.Combine(dir, "main-narrow.png"), false);

            controller.AppDataWritable = false;
            win.OnSnapshot(normal);
            RenderElement(host, 940, 690, Path.Combine(dir, "main-restricted.png"), false);
            controller.AppDataWritable = true;
            win.OnSnapshot(normal);

            win.ShowPage(true);
            RenderElement(host, 940, 690, Path.Combine(dir, "settings.png"), false);
            RenderElement(host, 940, 1500, Path.Combine(dir, "settings-full.png"), false);

            RenderTraySheet(dir);

            // 浅色主题
            Theme.Init(false, Platform.EffectiveAccent(false));
            Ui.Init();
            var win2 = new MainWindow(controller);
            var content2 = (UIElement)win2.Content;
            win2.Content = null;
            var host2 = new Border { Background = Theme.Bg, Child = content2 };
            win2.OnSnapshot(normal);
            RenderElement(host2, 940, 690, Path.Combine(dir, "main-light.png"), false);
            win2.ShowPage(true);
            RenderElement(host2, 940, 690, Path.Combine(dir, "settings-light.png"), false);
            Theme.Init(true, Platform.EffectiveAccent(false));
            Ui.Init();

            var notice = new AlertNotice
            {
                Device = low[0],
                Level = AlertLevel.Critical,
                Threshold = 10,
                Title = low[0].Name + " 电量告急",
                Message = "当前电量 12%，已低于 10% 提醒阈值。请立即充电，以免突然断开。"
            };
            var toast = new ToastWindow(notice);
            RenderElement(Detach(toast), 392, 190, Path.Combine(dir, "toast.png"), true);

            var warnNotice = new AlertNotice
            {
                Device = low[0],
                Level = AlertLevel.Warning,
                Threshold = 20,
                Title = low[0].Name + " 电量不足",
                Message = "当前电量 18%，已低于 20% 提醒阈值。建议尽快充电或更换电池。"
            };
            var toast2 = new ToastWindow(warnNotice);
            RenderElement(Detach(toast2), 392, 190, Path.Combine(dir, "toast-warning.png"), true);

            Console.WriteLine("已输出：" + dir);
        }

        private static List<DeviceState> Fake()
        {
            var list = new List<DeviceState>();
            list.Add(new DeviceState
            {
                Key = "3554:F58E",
                Name = "VXE 无线鼠标",
                VidPid = "3554:F58E",
                KindText = "2.4G 鼠标",
                Level = 30,
                Status = BatteryStatus.Discharging,
                VoltageMv = 3661,
                Source = "VXE / ATK 私有协议（0x04 电量查询）",
                Detail = "回复 08 04 00 00 00 02 1E 00 0E 4D",
                Supported = true,
                Present = true,
                UpdatedAt = DateTime.Now,
                History = new List<int> { 96, 95, 93, 90, 88, 84, 80, 76, 71, 66, 60, 55, 49, 44, 39, 35, 30 },
                SortOrder = 0
            });
            list.Add(new DeviceState
            {
                Key = "1EA7:0068",
                Name = "无线键鼠接收器",
                VidPid = "1EA7:0068",
                KindText = "2.4G 键鼠",
                Level = 56,
                Status = BatteryStatus.Discharging,
                Source = "厂商私有协议（Report 0xB5 / 命令 0x01）",
                Detail = "回复 B5 01 01 38",
                Supported = true,
                Present = true,
                UpdatedAt = DateTime.Now,
                History = new List<int> { 78, 76, 74, 72, 70, 69, 66, 64, 62, 60, 58, 56 },
                SortOrder = 1
            });
            return list;
        }

        private static List<DeviceState> FakeLow()
        {
            var list = Fake();
            list[0].Level = 12;
            list[0].Status = BatteryStatus.Discharging;
            list[0].History = new List<int> { 30, 27, 25, 22, 20, 18, 16, 14, 12 };
            list[1].Level = 18;
            list[1].History = new List<int> { 56, 48, 40, 33, 27, 22, 18 };
            return list;
        }


        private static void RenderTraySheet(string dir)
        {
            int[] levels = { 100, 56, 30, 18, 8, -1 };
            int cell = 44;
            using (var bmp = new System.Drawing.Bitmap(levels.Length * cell + 8, 2 * cell + 8))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(255, 255, 255, 255));
                for (int row = 0; row < 2; row++)
                {
                    bool light = row == 1;
                    using (var bg = new System.Drawing.SolidBrush(light
                        ? System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3)
                        : System.Drawing.Color.FromArgb(0x20, 0x20, 0x20)))
                    {
                        g.FillRectangle(bg, 4, 4 + row * cell, levels.Length * cell, cell);
                    }
                    for (int i = 0; i < levels.Length; i++)
                    {
                        int lv = levels[i];
                        using (var ico = AppIcon.TrayIcon(lv, lv >= 0 && lv < 20, false, light))
                        using (var big = new System.Drawing.Bitmap(ico.ToBitmap(), 34, 34))
                        {
                            g.DrawImage(big, 4 + i * cell + 5, 4 + row * cell + 5);
                        }
                    }
                }
                bmp.Save(Path.Combine(dir, "tray-icons.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static FrameworkElement Detach(Window w)
        {
            var content = (UIElement)w.Content;
            w.Content = null;
            return new Border { Child = content };
        }

        private static void RenderElement(FrameworkElement el, double width, double height, string path, bool autoHeight)
        {
            if (el == null) return;
            el.Width = width;
            if (!autoHeight) el.Height = height;
            el.Measure(new Size(width, autoHeight ? double.PositiveInfinity : height));
            double h = autoHeight ? Math.Max(10, Math.Ceiling(el.DesiredSize.Height)) : height;
            el.Arrange(new Rect(0, 0, width, h));
            el.UpdateLayout();

            var rtb = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("  " + Path.GetFileName(path) + "  " + (int)width + "x" + (int)h);
        }
    }
}
