using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BatterySentinel.Core;

namespace BatterySentinel.UI
{
    /// <summary>小对话框：输入框、设备详情。</summary>
    public static class Dialogs
    {
        private static Window Shell(string title, double width, UIElement content, Window owner)
        {
            var w = new Window
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                FontFamily = Theme.Font,
                Owner = owner
            };
            var card = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18, 20, 18),
                Margin = new Thickness(10)
            };
            Ui.Shadow(card, 30, Theme.IsDark ? 0.55 : 0.22, 7);
            card.Child = content;
            w.Content = card;
            card.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) w.DragMove(); };
            return w;
        }

        public static string Prompt(Window owner, string title, string label, string initial)
        {
            string result = null;
            var stack = new StackPanel();
            stack.Children.Add(Ui.Txt(title, 15, Theme.Text, semibold: true));
            var lab = Ui.Txt(label, 12, Theme.Text3);
            lab.Margin = new Thickness(0, 6, 0, 10);
            stack.Children.Add(lab);

            var box = new TextBox
            {
                Text = initial ?? "",
                FontSize = 13,
                Foreground = Theme.Text,
                Background = Theme.StrokeSoft,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                CaretBrush = Theme.Accent,
                SelectionBrush = Theme.Accent,
                FontFamily = Theme.Font,
                MaxLength = 40
            };
            stack.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "取消", Style = (Style)Application.Current.Resources["BtnGhost"], Height = 32, MinWidth = 76 };
            var ok = new Button { Content = "确定", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 32, MinWidth = 76, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            stack.Children.Add(buttons);

            Window w = Shell(title, 420, stack, owner);
            cancel.Click += (s, e) => w.Close();
            ok.Click += (s, e) => { result = box.Text; w.Close(); };
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            w.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) w.Close();
                else if (e.Key == Key.Enter) { result = box.Text; w.Close(); }
            };
            w.ShowDialog();
            return result;
        }

        public static void Message(Window owner, string title, string text, bool error = false, string copyText = null)
        {
            var stack = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Glyph(error ? Ui.IconWarning : Ui.IconInfo, 18, error ? Theme.Red : Theme.Accent));
            var t = Ui.Txt(title, 15, Theme.Text, semibold: true);
            t.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(t);
            stack.Children.Add(row);
            var body = Ui.Txt(text, 12.2, Theme.Text2);
            body.TextWrapping = TextWrapping.Wrap;
            body.LineHeight = 19;
            body.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "知道了", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 32, MinWidth = 84 };
            if (!string.IsNullOrEmpty(copyText))
            {
                var copy = new Button { Content = "复制路径", Style = (Style)Application.Current.Resources["BtnSecondary"], Height = 32, MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
                copy.Click += (s, e) =>
                {
                    try { Clipboard.SetText(copyText); } catch { }
                    copy.Content = "已复制";
                    copy.IsEnabled = false;
                };
                buttons.Children.Add(copy);
            }
            buttons.Children.Add(ok);
            stack.Children.Add(buttons);

            Window w = Shell(title, 440, stack, owner);
            ok.Click += (s, e) => w.Close();
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape || e.Key == Key.Enter) w.Close(); };
            w.ShowDialog();
        }

        public static void ShowDeviceDetails(Window owner, AppController app, DeviceState d)
        {
            if (d == null) return;
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new BatteryRing { Width = 72, Height = 72, Thickness = 6, Level = d.Level, ShowCaption = false });
            var headText = new StackPanel { Margin = new Thickness(14, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            headText.Children.Add(Ui.Txt(d.Name, 16, Theme.Text, semibold: true));
            headText.Children.Add(Ui.Txt(d.VidPid + " · " + d.KindText + (d.IsBluetooth ? " · 蓝牙" : " · 2.4G"), 11.5, Theme.Text3));
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            chips.Children.Add(Ui.Chip(d.StatusText == "" ? Theme.LevelLabel(d.Level) : d.StatusText, Theme.LevelColor(d.Level), Theme.AccentSoft));
            if (d.VoltageMv > 0) chips.Children.Add(new Border { Child = Ui.Chip(d.VoltageMv + " mV", Theme.Text2, Theme.StrokeSoft), Margin = new Thickness(6, 0, 0, 0) });
            headText.Children.Add(chips);
            head.Children.Add(headText);
            stack.Children.Add(head);

            stack.Children.Add(new Border { Height = 1, Background = Theme.StrokeSoft, Margin = new Thickness(0, 16, 0, 14) });

            stack.Children.Add(Row("读取方式", d.Source == "" ? "—" : d.Source));
            stack.Children.Add(Row("说明", d.Detail == "" ? "—" : d.Detail, true));
            stack.Children.Add(Row("接口", string.IsNullOrEmpty(d.InterfacePath) ? "—" : d.InterfacePath, true));
            if (!string.IsNullOrEmpty(d.RawHex)) stack.Children.Add(Row("原始报文", d.RawHex, true, true));
            stack.Children.Add(Row("更新时间", d.UpdatedAt == DateTime.MinValue ? "—" : d.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var copy = new Button { Content = "复制信息", Style = (Style)Application.Current.Resources["BtnGhost"], Height = 32, MinWidth = 80 };
            var reread = new Button { Content = "重新读取", Style = (Style)Application.Current.Resources["BtnSecondary"], Height = 32, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            var close = new Button { Content = "关闭", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 32, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(copy);
            buttons.Children.Add(reread);
            buttons.Children.Add(close);
            stack.Children.Add(buttons);

            Window w = Shell("设备详情", 520, stack, owner);
            copy.Click += (s, e) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("设备：" + d.Name);
                sb.AppendLine("ID：" + d.VidPid);
                sb.AppendLine("类型：" + d.KindText);
                sb.AppendLine("电量：" + (d.HasLevel ? d.Level + "%" : "未知"));
                sb.AppendLine("读取方式：" + d.Source);
                sb.AppendLine("说明：" + d.Detail);
                sb.AppendLine("接口：" + d.InterfacePath);
                sb.AppendLine("原始报文：" + d.RawHex);
                try { Clipboard.SetText(sb.ToString()); } catch { }
            };
            reread.Click += (s, e) => { app.RefreshNow(); w.Close(); };
            close.Click += (s, e) => w.Close();
            w.KeyDown += (s, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.ShowDialog();
        }

        private static Grid Row(string label, string value, bool wrap = false, bool mono = false)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Ui.Txt(label, 11.5, Theme.Text3);
            l.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(l);
            var v = Ui.Txt(value, mono ? 11 : 12, Theme.Text2);
            if (wrap) { v.TextWrapping = TextWrapping.Wrap; v.LineHeight = 17; }
            if (mono) { v.FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI"); }
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }
    }
}
