using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using BatterySentinel.Core;

namespace BatterySentinel.UI
{
    /// <summary>右下角轻提示（低电量弹窗）。</summary>
    public sealed class ToastWindow : Window
    {
        private readonly DispatcherTimer _timer;
        private bool _closing;
        public event Action<ToastWindow> Closed2;
        public event Action<string> SnoozeRequested;
        public event Action OpenRequested;

        private readonly string _deviceKey;

        public ToastWindow(AlertNotice notice)
        {
            _deviceKey = notice.Device != null ? notice.Device.Key : "";
            bool critical = notice.Level == AlertLevel.Critical;
            bool dark = Theme.IsDark;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.Height;
            Width = 392;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Theme.Font;

            var card = new Border
            {
                Background = Theme.Card,
                BorderBrush = critical ? new SolidColorBrush(Color.FromArgb(120, 255, 90, 90)) : Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 14, 14),
                Margin = new Thickness(10, 6, 10, 14)
            };
            Ui.Shadow(card, 30, dark ? 0.6 : 0.22, 8);

            var accentColor = critical ? Theme.Red : Theme.Yellow;
            var iconBox = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(38, ((SolidColorBrush)accentColor).Color.R, ((SolidColorBrush)accentColor).Color.G, ((SolidColorBrush)accentColor).Color.B)),
                VerticalAlignment = VerticalAlignment.Top,
                Child = Ui.Glyph(Ui.IconWarning, 18, accentColor)
            };

            var title = Ui.Txt(notice.Title, 14, Theme.Text, semibold: true);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var time = Ui.Txt(DateTime.Now.ToString("HH:mm"), 11, Theme.Text3);
            time.Margin = new Thickness(8, 0, 4, 0);
            var closeBtn = Ui.IconButton(Ui.IconClose, "关闭", Close2, 24, 9);
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(title);
            Grid.SetColumn(time, 1);
            head.Children.Add(time);
            Grid.SetColumn(closeBtn, 2);
            head.Children.Add(closeBtn);

            var msg = Ui.Txt(notice.Message, 12.2, Theme.Text2);
            msg.TextWrapping = TextWrapping.Wrap;
            msg.Margin = new Thickness(0, 5, 0, 0);
            msg.LineHeight = 19;

            var bar = new Grid { Height = 4, Margin = new Thickness(0, 12, 0, 12), VerticalAlignment = VerticalAlignment.Center };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)0x26 : (byte)0x14, 255, 255, 255)) };
            var fill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = accentColor,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 240 * Math.Max(0, Math.Min(100, notice.Device != null ? notice.Device.Level : 0)) / 100.0
            };
            var barHost = new Grid();
            barHost.Children.Add(track);
            barHost.Children.Add(fill);
            bar.Children.Add(barHost);

            var snooze = new Button { Content = "稍后提醒", Style = (Style)Application.Current.Resources["BtnGhost"], Height = 30, Padding = new Thickness(10, 0, 10, 0) };
            snooze.Click += (s, e) => { if (SnoozeRequested != null) SnoozeRequested(_deviceKey); Close2(); };
            var open = new Button { Content = "查看设备", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 30, Padding = new Thickness(12, 0, 12, 0) };
            open.Click += (s, e) => { if (OpenRequested != null) OpenRequested(); Close2(); };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            snooze.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(snooze);
            actions.Children.Add(open);

            var bottom = new Grid();
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottom.Children.Add(actions);

            var textCol = new StackPanel();
            textCol.Children.Add(head);
            textCol.Children.Add(msg);
            textCol.Children.Add(bar);
            textCol.Children.Add(bottom);
            textCol.Margin = new Thickness(12, 0, 0, 0);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(iconBox);
            Grid.SetColumn(textCol, 1);
            grid.Children.Add(textCol);

            var outer = new Grid();
            outer.Children.Add(grid);

            card.Child = outer;
            Content = card;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(critical ? 18 : 12) };
            _timer.Tick += (s, e) => Close2();
            Loaded += (s, e) =>
            {
                if (!Ui.AnimationsEnabled) { Opacity = 1; _timer.Start(); return; }
                _timer.Start();
                var tt = new TranslateTransform(46, 0);
                RenderTransform = tt;
                Opacity = 0;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(46, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
            };
            PreviewMouseLeftButtonDown += (s, e) => { if (OpenRequested != null) OpenRequested(); Close2(); };
        }

        public void Close2()
        {
            if (_closing) return;
            _closing = true;
            _timer.Stop();
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            fade.Completed += (s, e) =>
            {
                try { Close(); } catch { }
                if (Closed2 != null) Closed2(this);
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>弹窗堆叠管理：右上角依次往下排。</summary>
    public static class ToastManager
    {
        private static readonly List<ToastWindow> Open = new List<ToastWindow>();

        public static void Show(AlertNotice notice, Action<string> snooze, Action open)
        {
            var t = new ToastWindow(notice);
            t.SnoozeRequested += snooze;
            t.OpenRequested += open;
            t.Closed2 += w => { Open.Remove(w); Relayout(); };
            Open.Add(t);
            t.Show();
            Relayout();
        }

        public static void CloseAll()
        {
            foreach (ToastWindow t in new List<ToastWindow>(Open)) t.Close2();
        }

        private static void Relayout()
        {
            Rect wa = SystemParameters.WorkArea;
            double top = 14;
            foreach (ToastWindow t in Open)
            {
                t.Left = wa.Right - t.Width - 6;
                t.Top = top;
                t.UpdateLayout();
                top += t.ActualHeight > 10 ? t.ActualHeight : 170;
            }
        }
    }

    /// <summary>“对话框”式提醒（需要点确认）。</summary>
    public sealed class AlertDialog : Window
    {
        public AlertDialog(AlertNotice notice)
        {
            Title = "电量提醒";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.Height;
            Width = 430;
            ShowInTaskbar = true;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Font;

            var card = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 20, 20, 18),
                Margin = new Thickness(10)
            };
            Ui.Shadow(card, 32, 0.5, 8);

            var icon = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(40, ((SolidColorBrush)Theme.Red).Color.R, ((SolidColorBrush)Theme.Red).Color.G, ((SolidColorBrush)Theme.Red).Color.B)),
                Child = Ui.Glyph(Ui.IconWarning, 20, Theme.Red),
                VerticalAlignment = VerticalAlignment.Top
            };

            var title = Ui.Txt(notice.Title, 17, Theme.Text, semibold: true);
            var msg = Ui.Txt(notice.Message, 12.6, Theme.Text2);
            msg.TextWrapping = TextWrapping.Wrap;
            msg.LineHeight = 20;
            msg.Margin = new Thickness(0, 7, 0, 0);

            var detail = Ui.Txt("设备：" + (notice.Device != null ? notice.Device.Name : "") +
                                " · " + (notice.Device != null ? notice.Device.VidPid : ""), 11.5, Theme.Text3);
            detail.Margin = new Thickness(0, 10, 0, 0);

            var ok = new Button { Content = "知道了", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 32, MinWidth = 84 };
            ok.Click += (s, e) => Close();
            var open = new Button { Content = "查看设备", Style = (Style)Application.Current.Resources["BtnSecondary"], Height = 32, MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
            open.Click += (s, e) => { e.Handled = true; Close(); };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            actions.Children.Add(ok);
            actions.Children.Add(open);

            var textCol = new StackPanel();
            textCol.Children.Add(title);
            textCol.Children.Add(msg);
            textCol.Children.Add(detail);
            textCol.Children.Add(actions);
            textCol.Margin = new Thickness(14, 0, 0, 0);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(icon);
            Grid.SetColumn(textCol, 1);
            grid.Children.Add(textCol);
            card.Child = grid;
            Content = card;

            Loaded += (s, e) => { Ui.SlideFadeIn(card, 0, 14, 240); };
        }
    }
}
