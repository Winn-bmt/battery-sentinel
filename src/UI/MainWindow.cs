using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using BatterySentinel.Core;
using BatterySentinel.Hid;

namespace BatterySentinel.UI
{
    /// <summary>主窗口：设备总览 + 设置页（Win11 风格、自绘标题栏）。</summary>
    public sealed class MainWindow : Window
    {
        private readonly AppController _app;
        private readonly Dictionary<string, DeviceCardView> _cards = new Dictionary<string, DeviceCardView>(StringComparer.OrdinalIgnoreCase);

        private Grid _root;
        private Border _titleBar;
        private Button _settingsBtn;
        private ContentControl _pageHost;
        private Grid _dashPage;
        private StackPanel _settingsPage;
        private WrapPanel _cardsPanel;
        private TextBlock _summaryLine;
        private TextBlock _emptyTitle;
        private TextBlock _emptyBody;
        private Border _emptyState;
        private Border _alertBanner;
        private Border _restrictedBanner;
        private TextBlock _alertBannerText;
        private TextBlock _footerLeft;
        private TextBlock _footerRight;
        private Button _intervalBtn;
        private DispatcherTimer _clock;
        private bool _settingsOpen;
        private bool _reallyClose;
        private bool _trayHintShown;

        public MainWindow(AppController app)
        {
            _app = app;
            Title = "电量哨兵";
            Width = 940;
            Height = 690;
            MinWidth = 720;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = Theme.Bg;
            FontFamily = Theme.Font;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Icon = AppIcon.WindowIcon();

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 46,
                ResizeBorderThickness = new Thickness(5),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            Build();
            SourceInitialized += (s, e) => Platform.ApplyWindowEffects(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Theme.Changed += OnThemeChanged;
            Closing += OnClosing;
            KeyDown += OnKeyDown;

            SizeChanged += (s, e) => LayoutCards();
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Maximized)
                {
                    MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
                    MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;
                }
                else
                {
                    MaxHeight = double.PositiveInfinity;
                    MaxWidth = double.PositiveInfinity;
                }
            };

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (s, e) => UpdateFooterClock();
            _clock.Start();

            Loaded += (s, e) =>
            {
                Ui.FadeIn(_root, 260);
                if (_app.LastSnapshot != null) OnSnapshot(_app.LastSnapshot);
                if (_app.Settings.StartMinimized) Hide();
            };
        }

        public void RequestRealClose()
        {
            _reallyClose = true;
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_reallyClose)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown && _app.Tray != null)
                {
                    _trayHintShown = true;
                    _app.Tray.ShowBalloon("电量哨兵仍在后台运行", "关闭窗口不会退出，双击托盘图标可再次打开。");
                }
            }
            else
            {
                _clock.Stop();
                Theme.Changed -= OnThemeChanged;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5) { _app.RefreshNow(); e.Handled = true; }
            else if (e.Key == Key.Escape && _settingsOpen) { ShowPage(false); e.Handled = true; }
            else if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { ShowPage(true); e.Handled = true; }
        }

        private void OnThemeChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool keepSettings = _settingsOpen;
                double offset = _settingsScroll != null ? _settingsScroll.VerticalOffset : 0;
                Background = Theme.Bg;
                Build();
                if (keepSettings)
                {
                    ShowPage(true);
                    if (_settingsScroll != null) _settingsScroll.ScrollToVerticalOffset(offset);
                }
                if (_app.LastSnapshot != null) OnSnapshot(_app.LastSnapshot);
                Platform.ApplyWindowEffects(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            }));
        }

        // ==================================================================
        // 骨架
        // ==================================================================
        private void Build()
        {
            _cards.Clear();
            _settingsOpen = false;
            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

            _titleBar = BuildTitleBar();
            _root.Children.Add(_titleBar);

            _pageHost = new ContentControl();
            Grid.SetRow(_pageHost, 1);
            _root.Children.Add(_pageHost);

            Border footer = BuildFooter();
            Grid.SetRow(footer, 2);
            _root.Children.Add(footer);

            Content = _root;
            BuildDashboard();
            BuildSettings();
            ShowPage(false);
        }

        private Border BuildTitleBar()
        {
            var bar = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Theme.StrokeSoft,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var grid = new Grid { Margin = new Thickness(16, 0, 8, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = new LinearGradientBrush(
                    Color.FromArgb(255, Theme.AccentColor.R, Theme.AccentColor.G, Theme.AccentColor.B),
                    Color.FromArgb(255, (byte)(Theme.AccentColor.R * 0.72), (byte)(Theme.AccentColor.G * 0.72), (byte)(Theme.AccentColor.B * 0.86)), 45),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Grid()
            };
            var markGlyph = Ui.Glyph("\uE83F", 14, Brushes.White);
            ((Grid)mark.Child).Children.Add(markGlyph);

            var title = Ui.Txt("电量哨兵", 13.5, Theme.Text, semibold: true);
            title.Margin = new Thickness(10, 0, 0, 0);
            var dot = Ui.Dot(Theme.Text3, 3);
            dot.Margin = new Thickness(10, 0, 10, 0);
            var sub = Ui.Txt("2.4G 外设电量监控", 11.5, Theme.Text3);
            var ver = Ui.Txt("v" + Program.Version, 10.5, Theme.Text3);
            ver.Margin = new Thickness(8, 0, 0, 0);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(mark);
            left.Children.Add(title);
            left.Children.Add(dot);
            left.Children.Add(sub);
            left.Children.Add(ver);
            grid.Children.Add(left);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _settingsBtn = Ui.IconButton(Ui.IconGear, "设置 (Ctrl+,)", () => ShowPage(!_settingsOpen), 34, 14);
            WindowChrome.SetIsHitTestVisibleInChrome(_settingsBtn, true);
            actions.Children.Add(_settingsBtn);

            Button min = Ui.IconButton(Ui.IconMinimize, "最小化", () => WindowState = WindowState.Minimized, 34, 12);
            min.Width = 38; min.Height = 30; min.Margin = new Thickness(4, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(min, true);
            actions.Children.Add(min);

            Button close = Ui.CloseButton(Ui.IconClose, Close);
            close.Margin = new Thickness(4, 0, 0, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            actions.Children.Add(close);

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
            bar.Child = grid;
            return bar;
        }

        private Border BuildFooter()
        {
            var footer = new Border
            {
                Background = new SolidColorBrush(Theme.IsDark ? Color.FromArgb(0x1A, 255, 255, 255) : Color.FromArgb(0x06, 0, 0, 0)),
                BorderBrush = Theme.StrokeSoft,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var grid = new Grid { Margin = new Thickness(20, 0, 16, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _footerLeft = Ui.Txt("", 11.5, Theme.Text2);
            grid.Children.Add(_footerLeft);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _footerRight = Ui.Txt("", 11.5, Theme.Text3);
            right.Children.Add(_footerRight);

            var test = new Button
            {
                Content = "测试提醒",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 11.5
            };
            test.Click += (s, e) => _app.TestAlert();
            right.Children.Add(test);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            footer.Child = grid;
            return footer;
        }

        // ==================================================================
        // 总览页
        // ==================================================================
        private void BuildDashboard()
        {
            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- 顶部标题行 ----
            var head = new Grid { Margin = new Thickness(22, 18, 22, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel();
            var h1 = Ui.Txt("设备电量", 22, Theme.Text, semibold: true);
            _summaryLine = Ui.Txt("正在扫描 2.4G 外设…", 12, Theme.Text3);
            _summaryLine.Margin = new Thickness(0, 4, 0, 0);
            titles.Children.Add(h1);
            titles.Children.Add(_summaryLine);
            head.Children.Add(titles);

            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            _intervalBtn = new Button
            {
                Style = (Style)Application.Current.Resources["BtnSecondary"],
                Height = 34,
                Padding = new Thickness(12, 0, 10, 0),
                Content = BuildIntervalContent()
            };
            _intervalBtn.Click += (s, e) => ShowIntervalMenu();
            tools.Children.Add(_intervalBtn);

            var refresh = new Button
            {
                Style = (Style)Application.Current.Resources["BtnPrimary"],
                Height = 34,
                Padding = new Thickness(14, 0, 16, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Content = BuildRefreshContent()
            };
            refresh.Click += (s, e) => _app.RefreshNow();
            tools.Children.Add(refresh);
            Grid.SetColumn(tools, 1);
            head.Children.Add(tools);
            page.Children.Add(head);

            // ---- 滚动内容 ----
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 0, 22, 16),
                Focusable = false
            };
            var body = new StackPanel();

            // 受限环境提示（配置目录不可写 = 沙箱/受限令牌）
            _restrictedBanner = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)0x2E : (byte)0x14,
                    ((SolidColorBrush)Theme.Yellow).Color.R, ((SolidColorBrush)Theme.Yellow).Color.G, ((SolidColorBrush)Theme.Yellow).Color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110,
                    ((SolidColorBrush)Theme.Yellow).Color.R, ((SolidColorBrush)Theme.Yellow).Color.G, ((SolidColorBrush)Theme.Yellow).Color.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Visibility = _app.AppDataWritable ? Visibility.Collapsed : Visibility.Visible
            };
            var rbGrid = new Grid();
            rbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rbGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var rbIcon = Ui.Glyph(Ui.IconWarning, 15, Theme.Yellow);
            rbIcon.Margin = new Thickness(0, 0, 10, 0);
            rbGrid.Children.Add(rbIcon);
            var rbText = Ui.Txt("当前运行在受限环境中：设置无法保存、开机自启不可用。请把程序复制到本地文件夹，从资源管理器双击运行。", 12.2, Theme.Text);
            rbText.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(rbText, 1);
            rbGrid.Children.Add(rbText);
            var rbCopy = new Button
            {
                Content = "复制程序路径",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Height = 26,
                FontSize = 11.5,
                Padding = new Thickness(10, 0, 10, 0)
            };
            rbCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(Platform.ExePath()); } catch { }
                rbCopy.Content = "已复制";
            };
            Grid.SetColumn(rbCopy, 2);
            rbGrid.Children.Add(rbCopy);
            _restrictedBanner.Child = rbGrid;
            body.Children.Add(_restrictedBanner);

            _alertBanner = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)0x28 : (byte)0x12, ((SolidColorBrush)Theme.Red).Color.R, ((SolidColorBrush)Theme.Red).Color.G, ((SolidColorBrush)Theme.Red).Color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, ((SolidColorBrush)Theme.Red).Color.R, ((SolidColorBrush)Theme.Red).Color.G, ((SolidColorBrush)Theme.Red).Color.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Visibility = Visibility.Collapsed
            };
            var banGrid = new Grid();
            banGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            banGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            banGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var warn = Ui.Glyph(Ui.IconWarning, 15, Theme.Red);
            warn.Margin = new Thickness(0, 0, 10, 0);
            banGrid.Children.Add(warn);
            _alertBannerText = Ui.Txt("", 12.2, Theme.Text);
            Grid.SetColumn(_alertBannerText, 1);
            banGrid.Children.Add(_alertBannerText);
            var bannerAction = new Button
            {
                Content = "查看",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11.5
            };
            bannerAction.Click += (s, e) => ScrollToFirstLow();
            Grid.SetColumn(bannerAction, 2);
            banGrid.Children.Add(bannerAction);
            _alertBanner.Child = banGrid;
            body.Children.Add(_alertBanner);

            _cardsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            _cardsPanel.SizeChanged += (s, e) => LayoutCards();
            body.Children.Add(_cardsPanel);

            // 空状态
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 40) };
            var emptyIcon = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(18),
                Background = Theme.StrokeSoft,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = Ui.Glyph("\uE83F", 28, Theme.Text3)
            };
            empty.Children.Add(emptyIcon);
            _emptyTitle = Ui.Txt("没有检测到 2.4G 外设", 15, Theme.Text, semibold: true);
            _emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyTitle.Margin = new Thickness(0, 14, 0, 0);
            empty.Children.Add(_emptyTitle);
            _emptyBody = Ui.Txt("把无线接收器插到 USB 口，或打开蓝牙设备后点“立即刷新”。", 12, Theme.Text3);
            _emptyBody.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyBody.Margin = new Thickness(0, 6, 0, 0);
            empty.Children.Add(_emptyBody);
            var emptyBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            var refresh2 = new Button { Content = "重新扫描", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 32, Padding = new Thickness(14, 0, 14, 0) };
            refresh2.Click += (s, e) => _app.RefreshNow();
            var diag = new Button { Content = "设备诊断", Style = (Style)Application.Current.Resources["BtnGhost"], Height = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            diag.Click += (s, e) => _app.OpenDiagnostics();
            emptyBtns.Children.Add(refresh2);
            emptyBtns.Children.Add(diag);
            empty.Children.Add(emptyBtns);
            _emptyState = new Border { Child = empty, Visibility = Visibility.Collapsed };
            body.Children.Add(_emptyState);

            scroll.Content = body;
            Grid.SetRow(scroll, 1);
            page.Children.Add(scroll);

            _dashPage = page;
        }

        private UIElement BuildRefreshContent()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(Ui.Glyph(Ui.IconRefresh, 13, Brushes.White));
            var t = Ui.Txt("立即刷新", 12.5, Brushes.White);
            t.Margin = new Thickness(7, 0, 0, 0);
            panel.Children.Add(t);
            return panel;
        }

        private UIElement BuildIntervalContent()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(Ui.Glyph(Ui.IconHourglass, 12, Theme.Text2));
            var t = Ui.Txt("每 " + _app.Settings.IntervalText(), 12, Theme.Text);
            t.Margin = new Thickness(6, 0, 4, 0);
            t.Tag = "intervalText";
            panel.Children.Add(t);
            panel.Children.Add(Ui.Glyph(Ui.IconChevronDown, 9, Theme.Text3));
            return panel;
        }

        private void RefreshIntervalButton()
        {
            if (_intervalBtn == null) return;
            _intervalBtn.Content = BuildIntervalContent();
        }

        private void ShowIntervalMenu()
        {
            var items = new List<Ui.MenuItemModel>();
            foreach (int sec in AppSettings.IntervalChoices)
            {
                int captured = sec;
                string label = sec < 60 ? sec + " 秒" : (sec / 60) + " 分钟";
                items.Add(Ui.MenuItemModel.Item(label, () =>
                {
                    _app.Settings.IntervalSeconds = captured;
                    _app.SaveSettings();
                    RefreshIntervalButton();
                    _app.RefreshNow();
                }, "", _app.Settings.IntervalSeconds == sec));
            }
            items.Add(Ui.MenuItemModel.Sep());
            items.Add(Ui.MenuItemModel.Item(_app.Settings.AutoRefresh ? "暂停自动刷新" : "恢复自动刷新", () =>
            {
                _app.Settings.AutoRefresh = !_app.Settings.AutoRefresh;
                _app.SaveSettings();
                UpdateFooterClock();
            }, _app.Settings.AutoRefresh ? "\uE769" : Ui.IconPlay));
            Ui.ShowMenu(_intervalBtn, items, 150);
        }

        private void ScrollToFirstLow()
        {
            DeviceState low = _app.LastSnapshot == null ? null : _app.LastSnapshot.FirstOrDefault(d => d.HasLevel && d.Level <= _app.Settings.WarnPercent && !d.Charging);
            if (low == null) return;
            DeviceCardView v;
            if (_cards.TryGetValue(low.Key, out v)) v.Root.BringIntoView();
        }

        // ==================================================================
        // 设置页
        // ==================================================================
        private void BuildSettings()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 18, 22, 22)
            };
            var root = new StackPanel();
            var headRow = new Grid();
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var back = Ui.IconButton(Ui.IconBack, "返回", () => ShowPage(false), 32, 12);
            headRow.Children.Add(back);
            var titles = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            titles.Children.Add(Ui.Txt("设置", 22, Theme.Text, semibold: true));
            titles.Children.Add(Ui.Txt("更改会立即生效并自动保存", 11.5, Theme.Text3));
            Grid.SetColumn(titles, 1);
            headRow.Children.Add(titles);
            root.Children.Add(headRow);

            // 刷新
            var sec1 = Section("刷新");
            sec1.Children.Add(SettingRow("自动刷新", "在后台按间隔读取外设电量；关闭后只能手动刷新。",
                MakeToggle(_app.Settings.AutoRefresh, v => { _app.Settings.AutoRefresh = v; _app.SaveSettings(); UpdateFooterClock(); })));
            var intervalSeg = new Segmented(new[] { "15 秒", "30 秒", "1 分", "2 分", "5 分", "10 分", "30 分" }, IntervalIndex(_app.Settings.IntervalSeconds));
            intervalSeg.SelectionChanged += i =>
            {
                _app.Settings.IntervalSeconds = AppSettings.IntervalChoices[i];
                _app.SaveSettings();
                RefreshIntervalButton();
                _app.RefreshNow();
            };
            sec1.Children.Add(SettingRow("刷新间隔", "鼠标/耳机休眠时可能读不到，多刷几次即可。", intervalSeg));
            root.Children.Add(sec1);

            // 提醒
            var sec2 = Section("低电量提醒");
            var styles = new[] { "轻提示", "对话框", "托盘气泡", "关闭" };
            var styleKeys = new[] { "toast", "dialog", "balloon", "off" };
            int styleIdx = Math.Max(0, Array.IndexOf(styleKeys, _app.Settings.AlertStyle));
            var styleSeg = new Segmented(styles, styleIdx);
            styleSeg.SelectionChanged += i => { _app.Settings.AlertStyle = styleKeys[i]; _app.SaveSettings(); };
            sec2.Children.Add(SettingRow("提醒方式", "轻提示在右上角自动消失；对话框需要点“知道了”。", styleSeg));

            sec2.Children.Add(SettingRow("电量偏低阈值", "低于此值弹出第一次提醒。",
                MakeStepper(_app.Settings.WarnPercent, 5, 80, 5, v =>
                {
                    _app.Settings.WarnPercent = v;
                    if (_app.Settings.CriticalPercent >= v) _app.Settings.CriticalPercent = Math.Max(1, v - 5);
                    _app.Settings.Save();
                    BuildSettingsPageRefresh();
                }, "%")));
            sec2.Children.Add(SettingRow("严重阈值", "低于此值时换用更醒目的红色提醒。",
                MakeStepper(_app.Settings.CriticalPercent, 1, 60, 1, v =>
                {
                    _app.Settings.CriticalPercent = Math.Min(v, _app.Settings.WarnPercent - 1);
                    _app.Settings.Save();
                    BuildSettingsPageRefresh();
                }, "%")));
            sec2.Children.Add(SettingRow("提示音", "提醒时播放系统提示音。",
                MakeToggle(_app.Settings.AlertSound, v => { _app.Settings.AlertSound = v; _app.SaveSettings(); })));
            sec2.Children.Add(SettingRow("重复提醒间隔", "同一设备持续低电量时，隔多久再提醒一次。",
                MakeCooldownSeg()));
            var muteBtn = new Button
            {
                Style = (Style)Application.Current.Resources["BtnSecondary"],
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Content = _app.Settings.IsMutedNow() ? "恢复提醒" : "静音 1 小时"
            };
            muteBtn.Click += (s, e) =>
            {
                _app.Settings.MuteAllUntil = _app.Settings.IsMutedNow() ? DateTime.MinValue : DateTime.UtcNow.AddHours(1);
                _app.Settings.Save();
                muteBtn.Content = _app.Settings.IsMutedNow() ? "恢复提醒" : "静音 1 小时";
                UpdateFooterClock();
            };
            var testBtn = new Button { Style = (Style)Application.Current.Resources["BtnGhost"], Height = 30, Padding = new Thickness(10, 0, 10, 0), Content = "测试提醒", Margin = new Thickness(8, 0, 0, 0) };
            testBtn.Click += (s, e) => _app.TestAlert();
            sec2.Children.Add(SettingRow("静音与测试", "临时关闭所有提醒，或预览提醒效果。", Ui.Row(0, muteBtn, testBtn)));
            root.Children.Add(sec2);

            // 窗口与启动
            var sec3 = Section("窗口与启动");
            sec3.Children.Add(SettingRow("开机自启", "在“启动”文件夹创建快捷方式，任务管理器里可管理。", BuildAutostartToggle()));
            sec3.Children.Add(SettingRow("启动时最小化", "登录后只显示托盘图标，不弹出窗口。",
                MakeToggle(_app.Settings.StartMinimized, v => { _app.Settings.StartMinimized = v; _app.SaveSettings(); })));
            var themeKeys = new[] { "system", "dark", "light" };
            int themeIdx = Math.Max(0, Array.IndexOf(themeKeys, _app.Settings.Theme));
            var themeSeg = new Segmented(new[] { "跟随系统", "深色", "浅色" }, themeIdx);
            themeSeg.SelectionChanged += i => { _app.Settings.Theme = themeKeys[i]; _app.SaveSettings(); _app.ApplyTheme(); };
            sec3.Children.Add(SettingRow("主题", "深色/浅色界面；跟随系统时会随 Windows 设置切换。", themeSeg));
            sec3.Children.Add(SettingRow("使用系统强调色", "关闭时使用固定的天蓝色；深色主题的系统强调色往往偏暗，会自动回退。", MakeToggle(_app.Settings.UseSystemAccent, v => { _app.Settings.UseSystemAccent = v; _app.SaveSettings(); _app.ApplyTheme(); })));
            root.Children.Add(sec3);

            // 设备
            var sec4 = Section("设备");
            sec4.Children.Add(SettingRow("显示蓝牙设备", "读取蓝牙耳机的 HFP 电量（需要耳机支持上报）。",
                MakeToggle(_app.Settings.ShowBluetooth, v => { _app.Settings.ShowBluetooth = v; _app.SaveSettings(); _app.RefreshNow(); })));
            sec4.Children.Add(SettingRow("显示不支持的设备", "把检测到但读不到电量的外设也列出来，便于排查。",
                MakeToggle(_app.Settings.ShowUnsupported, v => { _app.Settings.ShowUnsupported = v; _app.SaveSettings(); _app.RefreshNow(); })));

            var deviceList = new StackPanel();
            if (_app.LastSnapshot != null && _app.LastSnapshot.Count > 0)
            {
                foreach (DeviceState d in _app.LastSnapshot)
                {
                    deviceList.Children.Add(BuildDeviceSetting(d));
                }
            }
            else
            {
                deviceList.Children.Add(Ui.Muted("尚未发现设备。", 12));
            }
            var devBox = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 6, 14, 6),
                Child = deviceList
            };
            sec4.Children.Add(devBox);

            var diagRow = new StackPanel { Orientation = Orientation.Horizontal };
            var diagBtn = new Button { Style = (Style)Application.Current.Resources["BtnSecondary"], Height = 30, Padding = new Thickness(12, 0, 12, 0), Content = "设备诊断" };
            diagBtn.Click += (s, e) => _app.OpenDiagnostics();
            var dirBtn = new Button { Style = (Style)Application.Current.Resources["BtnGhost"], Height = 30, Padding = new Thickness(12, 0, 12, 0), Content = "打开数据目录", Margin = new Thickness(8, 0, 0, 0) };
            dirBtn.Click += (s, e) =>
            {
                if (!Platform.OpenFolder(AppSettings.AppDir))
                {
                    Dialogs.Message(this, "无法自动打开文件夹",
                        "可以直接把下面的路径粘贴到资源管理器地址栏：\n" + AppSettings.AppDir,
                        true, AppSettings.AppDir);
                }
            };
            diagRow.Children.Add(diagBtn);
            diagRow.Children.Add(dirBtn);
            sec4.Children.Add(SettingRow("诊断", "查看所有 HID 接口、原始报文，排查读不到电量的设备。", diagRow));
            root.Children.Add(sec4);

            // 关于
            var sec5 = Section("关于");
            var about = new StackPanel();
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(Ui.BatteryGlyph(26, 76, Theme.Text2, Theme.Green));
            var appName = Ui.Txt("电量哨兵  v" + Program.Version, 13, Theme.Text, semibold: true);
            appName.Margin = new Thickness(8, 0, 0, 0);
            nameRow.Children.Add(appName);
            about.Children.Add(nameRow);
            var desc = Ui.Txt("Windows 11 桌面小工具：监控 2.4G 无线鼠标、键盘、耳机的电量，低电量时弹窗提醒。" +
                              "完全本地运行，不联网、不上传、不写日志。", 12, Theme.Text2);
            desc.TextWrapping = TextWrapping.Wrap;
            desc.LineHeight = 19;
            desc.Margin = new Thickness(0, 8, 0, 0);
            about.Children.Add(desc);
            var credit = Ui.Txt("协议参考：yszyszysz233/2.4G-battery-viewer（MIT）、Linux hid-pulsar 驱动", 11, Theme.Text3);
            credit.Margin = new Thickness(0, 8, 0, 0);
            about.Children.Add(credit);

            about.Children.Add(AboutRow("程序", Platform.ExePath()));
            about.Children.Add(AboutRow("数据目录", AppSettings.AppDir + (_app.AppDataWritable ? "（可写）" : "（不可写 · 受限环境）")));
            about.Children.Add(AboutRow("运行身份", Platform.IsElevated() ? "管理员" : "普通用户"));

            var exitBtn = new Button
            {
                Content = "退出程序",
                Style = (Style)Application.Current.Resources["BtnSecondary"],
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0)
            };
            exitBtn.Click += (s, e) => _app.Quit();

            var copyDiagBtn = new Button
            {
                Content = "复制诊断信息",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 12, 0, 0)
            };
            copyDiagBtn.Click += (s, e) =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("电量哨兵 " + Program.Version);
                sb.AppendLine("程序：" + Platform.ExePath());
                sb.AppendLine("数据目录：" + AppSettings.AppDir + "  可写=" + _app.AppDataWritable);
                sb.AppendLine("管理员身份：" + Platform.IsElevated());
                sb.AppendLine("开机自启：" + (Platform.IsAutostartEnabled() ? "已启用（" + Platform.AutostartMethod() + "）" : "未启用"));
                sb.AppendLine("快捷方式：" + Platform.StartupShortcutPath() + "  存在=" + System.IO.File.Exists(Platform.StartupShortcutPath()));
                sb.AppendLine("注册表项：HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\BatterySentinel");
                sb.AppendLine("最近自启错误：" + Platform.LastAutostartError);
                if (_app.LastSnapshot != null)
                {
                    foreach (DeviceState d in _app.LastSnapshot)
                        sb.AppendLine("设备：" + d.Name + " " + d.VidPid + " " + (d.HasLevel ? d.Level + "%" : "未读取") + " · " + d.Source);
                }
                try { Clipboard.SetText(sb.ToString()); } catch { }
                copyDiagBtn.Content = "已复制";
            };

            var aboutButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            aboutButtons.Children.Add(exitBtn);
            aboutButtons.Children.Add(copyDiagBtn);
            about.Children.Add(aboutButtons);

            sec5.Children.Add(about);
            root.Children.Add(sec5);

            scroll.Content = root;
            _settingsPage = root;
            _settingsScroll = scroll;
        }

        private ScrollViewer _settingsScroll;

        private void BuildSettingsPageRefresh()
        {
            int y = 0;
            if (_settingsScroll != null) y = (int)_settingsScroll.VerticalOffset;
            BuildSettings();
            _settingsScroll.ScrollToVerticalOffset(y);
        }

        private static Grid AboutRow(string key, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Ui.Txt(key, 11, Theme.Text3));
            var v = Ui.Txt(value, 11, Theme.Text3);
            v.TextWrapping = TextWrapping.Wrap;
            v.FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        private static StackPanel Section(string title)
        {
            var p = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            var t = Ui.Txt(title, 13.5, Theme.Text, semibold: true);
            t.Margin = new Thickness(2, 0, 0, 8);
            p.Children.Add(t);
            return p;
        }

        private static Grid SettingRow(string title, string desc, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(2, 8, 2, 8), MinHeight = 40 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            text.Children.Add(Ui.Txt(title, 12.8, Theme.Text));
            if (!string.IsNullOrEmpty(desc))
            {
                var d = Ui.Txt(desc, 11.2, Theme.Text3);
                d.TextWrapping = TextWrapping.Wrap;
                d.HorizontalAlignment = HorizontalAlignment.Left;
                d.TextAlignment = TextAlignment.Left;
                d.Margin = new Thickness(0, 2, 0, 0);
                d.MaxWidth = 520;
                text.Children.Add(d);
            }
            g.Children.Add(text);
            if (control != null)
            {
                (control as FrameworkElement).VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            return g;
        }

        private UIElement BuildAutostartToggle()
        {
            var t = new ToggleSwitch { IsChecked = Platform.IsAutostartEnabled(), VerticalAlignment = VerticalAlignment.Center };
            t.Checked += (s, e) =>
            {
                if (!Platform.SetAutostart(true))
                {
                    t.IsChecked = false;
                    string diag = "版本 " + Program.Version +
                                  "\n程序：" + Platform.ExePath() +
                                  "\n数据目录可写：" + (_app.AppDataWritable ? "是" : "否") +
                                  "\n管理员身份：" + (Platform.IsElevated() ? "是" : "否") +
                                  "\n\n" + Platform.LastAutostartError;
                    string hint = _app.AppDataWritable
                        ? "\n\n可以手动在“启动”文件夹里新建快捷方式，目标填：\n" + Platform.ExePath() + " --minimized"
                        : "\n\n数据目录不可写，说明程序运行在受限环境中（例如从聊天工具或压缩包预览窗口直接打开）。" +
                          "请把 BatterySentinel.exe 复制到本地文件夹，从资源管理器双击运行后再设置。";
                    Dialogs.Message(this, "无法设置开机自启", "两种方式都没有成功：\n\n" + diag + hint, true, diag);
                    return;
                }
                _app.Settings.StartWithWindows = true;
                _app.SaveSettings();
            };
            t.Unchecked += (s, e) =>
            {
                Platform.SetAutostart(false);
                _app.Settings.StartWithWindows = false;
                _app.SaveSettings();
            };
            return t;
        }

        private ToggleSwitch MakeToggle(bool value, Action<bool> onChange)
        {
            var t = new ToggleSwitch { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            t.Checked += (s, e) => onChange(true);
            t.Unchecked += (s, e) => onChange(false);
            return t;
        }

        private int IntervalIndex(int seconds)
        {
            int i = Array.IndexOf(AppSettings.IntervalChoices, seconds);
            return i < 0 ? 2 : i;
        }

        private Segmented MakeCooldownSeg()
        {
            int[] values = { 10, 30, 60, 120 };
            string[] labels = { "10 分", "30 分", "1 小时", "2 小时" };
            int idx = Array.IndexOf(values, _app.Settings.AlertCooldownMinutes);
            if (idx < 0) idx = 1;
            var seg = new Segmented(labels, idx);
            seg.SelectionChanged += i => { _app.Settings.AlertCooldownMinutes = values[i]; _app.SaveSettings(); };
            return seg;
        }

        private UIElement MakeStepper(int value, int min, int max, int step, Action<int> onChange, string suffix)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var minus = Ui.IconButton("\uE738", "减少", null, 26, 10);
            var plus = Ui.IconButton("\uE710", "增加", null, 26, 10);
            var val = Ui.Txt(value + suffix, 12.5, Theme.Text, semibold: true);
            val.MinWidth = 44;
            val.TextAlignment = TextAlignment.Center;
            minus.Click += (s, e) =>
            {
                int nv = Math.Max(min, value - step);
                value = nv;
                val.Text = nv + suffix;
                onChange(nv);
            };
            plus.Click += (s, e) =>
            {
                int nv = Math.Min(max, value + step);
                value = nv;
                val.Text = nv + suffix;
                onChange(nv);
            };
            panel.Children.Add(minus);
            panel.Children.Add(val);
            panel.Children.Add(plus);
            return new Border
            {
                Background = Theme.StrokeSoft,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(2),
                Child = panel
            };
        }

        private UIElement BuildDeviceSetting(DeviceState d)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = Ui.Dot(d.HasLevel ? Theme.LevelColor(d.Level) : Theme.Text3, 7);
            dot.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(dot);
            left.Children.Add(Ui.Txt(d.Name, 12.5, Theme.Text));
            var meta = Ui.Txt("  " + d.VidPid + " · " + d.KindText, 11, Theme.Text3);
            left.Children.Add(meta);
            g.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var rename = new Button { Style = (Style)Application.Current.Resources["BtnGhost"], Height = 26, FontSize = 11.5, Padding = new Thickness(8, 0, 8, 0), Content = "重命名" };
            rename.Click += (s, e) =>
            {
                string nv = Dialogs.Prompt(this, "重命名设备", "给这台设备起个名字：", d.Name);
                if (nv != null)
                {
                    _app.Settings.Names[d.Key] = nv.Trim();
                    _app.SaveSettings();
                    _app.RefreshNow();
                }
            };
            var mute = new Button
            {
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Height = 26,
                FontSize = 11.5,
                Padding = new Thickness(8, 0, 8, 0),
                Content = _app.Settings.Muted.Contains(d.Key) ? "恢复提醒" : "静音此设备"
            };
            mute.Click += (s, e) =>
            {
                if (_app.Settings.Muted.Contains(d.Key)) _app.Settings.Muted.Remove(d.Key);
                else _app.Settings.Muted.Add(d.Key);
                _app.SaveSettings();
                mute.Content = _app.Settings.Muted.Contains(d.Key) ? "恢复提醒" : "静音此设备";
            };
            right.Children.Add(rename);
            right.Children.Add(mute);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        // ==================================================================
        // 页面切换与数据刷新
        // ==================================================================
        public void ShowPage(bool settings)
        {
            _settingsOpen = settings;
            if (settings)
            {
                BuildSettings();
                _pageHost.Content = _settingsScroll;
                _settingsBtn.Content = Ui.Glyph(Ui.IconClose, 13, Theme.Text2);
            }
            else
            {
                _pageHost.Content = _dashPage;
                _settingsBtn.Content = Ui.Glyph(Ui.IconGear, 14, Theme.Text2);
            }
            if (_pageHost.Content is FrameworkElement fe) Ui.FadeIn(fe, 200);
        }

        private void LayoutCards()
        {
            if (_cardsPanel == null) return;
            double avail = _cardsPanel.ActualWidth;
            if (avail < 10)
            {
                double w0 = ActualWidth > 100 ? ActualWidth : Width;
                avail = Math.Max(320, w0 - 44);
            }
            double half = (avail - 14) / 2.0;
            bool two = half >= 340;
            double w = two ? Math.Min(430, half) : Math.Max(300, avail - 2);
            foreach (DeviceCardView v in _cards.Values)
            {
                v.Root.Width = w;
                v.Root.Margin = new Thickness(0, 0, two ? 14 : 0, 14);
            }
        }

        public void OnSnapshot(List<DeviceState> devices)
        {
            if (_settingsOpen) return;
            if (devices == null) devices = new List<DeviceState>();

            bool showUnsupported = _app.Settings.ShowUnsupported;
            List<DeviceState> visible = devices.Where(d => d.Supported || d.HasLevel || showUnsupported).ToList();

            // 删除已消失的卡片
            var keys = new HashSet<string>(visible.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
            foreach (string k in _cards.Keys.ToList())
            {
                if (!keys.Contains(k))
                {
                    _cardsPanel.Children.Remove(_cards[k].Root);
                    _cards.Remove(k);
                }
            }

            // 更新/添加
            int idx = 0;
            foreach (DeviceState d in visible)
            {
                DeviceCardView v;
                if (!_cards.TryGetValue(d.Key, out v))
                {
                    v = new DeviceCardView(this, _app, d.Key);
                    _cards[d.Key] = v;
                    _cardsPanel.Children.Insert(Math.Min(idx, _cardsPanel.Children.Count), v.Root);
                }
                v.Update(d);
                idx++;
            }

            _emptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (visible.Count == 0)
            {
                _emptyTitle.Text = devices.Count == 0 ? "没有检测到 2.4G 外设" : "检测到设备，但读不到电量";
                _emptyBody.Text = devices.Count == 0
                    ? "把无线接收器插到 USB 口，或打开蓝牙设备后点“立即刷新”。"
                    : "点“设备诊断”查看接口与报文，或在设置里打开“显示不支持的设备”。";
            }

            LayoutCards();
            if (_restrictedBanner != null)
                _restrictedBanner.Visibility = _app.AppDataWritable ? Visibility.Collapsed : Visibility.Visible;
            UpdateAlertBanner(visible);
            UpdateSummary(devices);
            UpdateFooterClock();
        }

        private void UpdateAlertBanner(List<DeviceState> devices)
        {
            DeviceState low = devices.FirstOrDefault(d => d.HasLevel && d.Level <= _app.Settings.WarnPercent && !d.Charging);
            if (low == null)
            {
                _alertBanner.Visibility = Visibility.Collapsed;
                return;
            }
            _alertBanner.Visibility = Visibility.Visible;
            _alertBannerText.Text = low.Name + " 电量 " + low.Level + "%，已低于 " + _app.Settings.WarnPercent + "% 提醒阈值";
        }

        private void UpdateSummary(List<DeviceState> devices)
        {
            int online = devices.Count(d => d.HasLevel);
            int low = devices.Count(d => d.HasLevel && d.Level <= _app.Settings.WarnPercent && !d.Charging);
            string text = "共 " + devices.Count + " 台设备 · 在线 " + online + " 台";
            if (low > 0) text += " · " + low + " 台电量偏低";
            _summaryLine.Text = text;
        }

        private void UpdateFooterClock()
        {
            if (_footerLeft == null) return;
            if (!_app.Settings.AutoRefresh)
                _footerLeft.Text = "自动刷新已暂停";
            else if (_app.Settings.IsMutedNow())
                _footerLeft.Text = "提醒已静音至 " + _app.Settings.MuteAllUntil.ToLocalTime().ToString("HH:mm");
            else
                _footerLeft.Text = "自动刷新 · 每 " + _app.Settings.IntervalText();

            DateTime last = _app.LastRunAt;
            _footerRight.Text = last == DateTime.MinValue ? "尚未刷新" : "上次更新 " + last.ToString("HH:mm:ss");
        }

        // ==================================================================
        // 设备卡片
        // ==================================================================
        private sealed class DeviceCardView
        {
            public Border Root;
            private readonly BatteryRing _ring;
            private readonly TextBlock _name;
            private readonly TextBlock _sub;
            private readonly TextBlock _status;
            private readonly TextBlock _voltage;
            private readonly TextBlock _source;
            private readonly TextBlock _time;
            private readonly Sparkline _spark;
            private readonly Border _chipHost;
            private readonly Button _more;
            private readonly Border _card;
            private DeviceState _state;
            private readonly AppController _app;

            public DeviceCardView(MainWindow owner, AppController app, string key)
            {
                _app = app;
                _card = Ui.Card(14);
                _card.Width = 428;
                _card.Margin = new Thickness(0, 0, 14, 14);
                _card.Padding = new Thickness(16, 15, 14, 13);
                _card.Cursor = Cursors.Hand;
                Root = _card;

                var main = new Grid();
                main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _ring = new BatteryRing { Width = 96, Height = 96, Thickness = 8, ShowCaption = false, VerticalAlignment = VerticalAlignment.Top };
                _ring.Margin = new Thickness(0, 8, 16, 0);
                main.Children.Add(_ring);

                var right = new StackPanel();
                var titleRow = new Grid();
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
                _name = Ui.Txt("设备", 15, Theme.Text, semibold: true);
                nameRow.Children.Add(_name);
                _chipHost = new Border { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                nameRow.Children.Add(_chipHost);
                titleRow.Children.Add(nameRow);

                _more = Ui.IconButton(Ui.IconMore, "更多", null, 28, 12);
                _more.HorizontalAlignment = HorizontalAlignment.Right;
                _more.Click += (s, e) => ShowMoreMenu();
                Grid.SetColumn(_more, 1);
                titleRow.Children.Add(_more);
                right.Children.Add(titleRow);

                _sub = Ui.Txt("", 11.2, Theme.Text3);
                _sub.Margin = new Thickness(0, 3, 0, 0);
                right.Children.Add(_sub);

                var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
                _status = Ui.Txt("", 12, Theme.Text2);
                _voltage = Ui.Txt("", 11, Theme.Text3);
                _voltage.Margin = new Thickness(10, 0, 0, 0);
                statusRow.Children.Add(_status);
                statusRow.Children.Add(_voltage);
                right.Children.Add(statusRow);

                _spark = new Sparkline { Height = 28, Margin = new Thickness(0, 8, 0, 0) };
                right.Children.Add(_spark);

                var metaRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _source = Ui.Txt("", 10.5, Theme.Text3);
                _source.TextTrimming = TextTrimming.CharacterEllipsis;
                metaRow.Children.Add(_source);
                _time = Ui.Txt("", 10.5, Theme.Text3);
                Grid.SetColumn(_time, 1);
                metaRow.Children.Add(_time);
                right.Children.Add(metaRow);

                Grid.SetColumn(right, 1);
                main.Children.Add(right);
                _card.Child = main;

                _card.MouseEnter += (s, e) => { _card.Background = Theme.CardHover; _card.BorderBrush = Theme.WindowBorder; };
                _card.MouseLeave += (s, e) => { _card.Background = Theme.Card; ApplyLevelBorder(); };
                _card.MouseLeftButtonUp += (s, e) => Dialogs.ShowDeviceDetails(owner, _app, _state);
            }

            private void ShowMoreMenu()
            {
                var items = new List<Ui.MenuItemModel>();
                items.Add(Ui.MenuItemModel.Item("立即读取", () => _app.RefreshNow(), Ui.IconRefresh));
                items.Add(Ui.MenuItemModel.Item("复制设备信息", () =>
                {
                    try { Clipboard.SetText(InfoText(_state)); } catch { }
                }, "\uE8C8"));
                items.Add(Ui.MenuItemModel.Sep());
                items.Add(Ui.MenuItemModel.Item("显示详情", () => Dialogs.ShowDeviceDetails(Window.GetWindow(Root), _app, _state), Ui.IconInfo));
                items.Add(Ui.MenuItemModel.Item(_state != null && _app.Settings.Muted.Contains(_state.Key) ? "恢复此设备提醒" : "静音此设备提醒", () =>
                {
                    if (_state == null) return;
                    if (_app.Settings.Muted.Contains(_state.Key)) _app.Settings.Muted.Remove(_state.Key);
                    else _app.Settings.Muted.Add(_state.Key);
                    _app.SaveSettings();
                }, "\uE7ED"));
                Ui.ShowMenu(_more, items, 168);
            }

            private static string InfoText(DeviceState d)
            {
                if (d == null) return "";
                var sb = new StringBuilder();
                sb.AppendLine(d.Name + "  " + d.VidPid);
                sb.AppendLine("类型：" + d.KindText);
                sb.AppendLine("电量：" + (d.Level >= 0 ? d.Level + "%" : "未知") + (d.StatusText != "" ? "（" + d.StatusText + "）" : ""));
                if (d.VoltageMv > 0) sb.AppendLine("电压：" + d.VoltageMv + " mV");
                sb.AppendLine("读取方式：" + d.Source);
                sb.AppendLine("说明：" + d.Detail);
                sb.AppendLine("接口：" + d.InterfacePath);
                return sb.ToString();
            }

            private void ApplyLevelBorder()
            {
                if (_state != null && _state.HasLevel && _state.Level <= 15 && !_state.Charging)
                {
                    var c = Theme.LevelColor(_state.Level).Color;
                    _card.BorderBrush = new SolidColorBrush(Color.FromArgb(110, c.R, c.G, c.B));
                }
                else
                {
                    _card.BorderBrush = Theme.Stroke;
                }
            }

            public void Update(DeviceState d)
            {
                _state = d;
                _name.Text = d.Name;
                _sub.Text = d.IsBluetooth
                    ? d.KindText + " · 已连接" + (d.VoltageMv > 0 ? " · " + d.VoltageMv + " mV" : "")
                    : d.VidPid + " · " + d.KindText + (d.VoltageMv > 0 ? " · " + d.VoltageMv + " mV" : "");
                _ring.Level = d.HasLevel ? d.Level : -1;

                _chipHost.Child = d.IsBluetooth ? Ui.AccentChip("蓝牙") : Ui.AccentChip("2.4G");

                if (d.HasLevel)
                {
                    string st = d.StatusText;
                    _status.Text = st == "" ? Theme.LevelLabel(d.Level) : st;
                    _status.Foreground = d.Status == BatteryStatus.Charging || d.Status == BatteryStatus.Full
                        ? Theme.Green
                        : d.Level <= _app.Settings.WarnPercent ? Theme.LevelColor(d.Level) : Theme.Text2;
                    _voltage.Text = d.Stale ? "上次读数 · 设备无响应" : "";
                    _source.Text = d.Source;
                    _time.Text = d.UpdatedText;
                    _spark.SetValues(d.History, d.Level);
                    _spark.Opacity = d.Stale ? 0.45 : 1;
                }
                else
                {
                    _status.Text = string.IsNullOrEmpty(d.ErrorText) ? "未读取到电量" : ShortError(d.ErrorText);
                    _status.Foreground = Theme.Text3;
                    _voltage.Text = "";
                    _source.Text = d.Supported ? d.Source : "未找到可用的电量读取方式";
                    _time.Text = "";
                    _spark.SetValues(null, -1);
                    _spark.Opacity = 0.35;
                }
                ApplyLevelBorder();
            }

            private static string ShortError(string text)
            {
                if (string.IsNullOrEmpty(text)) return "未读取到电量";
                if (text.Length > 46) return text.Substring(0, 44) + "…";
                return text;
            }
        }
    }
}
