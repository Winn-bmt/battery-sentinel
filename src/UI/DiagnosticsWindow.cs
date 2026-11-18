using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BatterySentinel.Core;
using BatterySentinel.Hid;

namespace BatterySentinel.UI
{
    /// <summary>设备诊断：列出所有 HID 接口，单独读取并显示原始报文。</summary>
    public sealed class DiagnosticsWindow : Window
    {
        private readonly AppController _app;
        private readonly ListBox _list = new ListBox();
        private readonly StackPanel _detail = new StackPanel();
        private readonly TextBlock _status;
        private List<HidInterface> _ifaces = new List<HidInterface>();
        private HidInterface _selected;

        public DiagnosticsWindow(AppController app)
        {
            _app = app;
            Title = "设备诊断";
            Width = 920;
            Height = 640;
            MinWidth = 760;
            MinHeight = 500;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = Theme.Bg;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = app.Main;
            Icon = AppIcon.WindowIcon();
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 46,
                ResizeBorderThickness = new Thickness(5),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });

            var bar = new Border { BorderBrush = Theme.StrokeSoft, BorderThickness = new Thickness(0, 0, 0, 1) };
            var barGrid = new Grid { Margin = new Thickness(16, 0, 8, 0) };
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Ui.Glyph(Ui.IconBug, 15, Theme.Text2));
            var t = Ui.Txt("设备诊断", 13.5, Theme.Text, semibold: true);
            t.Margin = new Thickness(8, 0, 0, 0);
            title.Children.Add(t);
            barGrid.Children.Add(title);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button close = Ui.CloseButton(Ui.IconClose, Close);
            close.Margin = new Thickness(6, 0, 0, 0);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
            btns.Children.Add(close);
            Grid.SetColumn(btns, 1);
            barGrid.Children.Add(btns);
            bar.Child = barGrid;
            root.Children.Add(bar);

            var body = new Grid { Margin = new Thickness(16, 12, 16, 8) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6)
            };
            _list.Background = Brushes.Transparent;
            _list.BorderThickness = new Thickness(0);
            _list.Foreground = Theme.Text;
            _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 1, 0, 1)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Text));
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Control.BackgroundProperty, Theme.Selected));
            itemStyle.Triggers.Add(sel);
            _list.ItemContainerStyle = itemStyle;
            _list.SelectionChanged += (s, e) => ShowSelected();
            left.Child = _list;
            body.Children.Add(left);

            var rightScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(14, 0, 0, 0) };
            var rightCard = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Child = _detail
            };
            rightScroll.Content = rightCard;
            Grid.SetColumn(rightScroll, 1);
            body.Children.Add(rightScroll);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var foot = new Grid { Margin = new Thickness(16, 0, 16, 8) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _status = Ui.Txt("", 11.5, Theme.Text3);
            foot.Children.Add(_status);
            var footBtns = new StackPanel { Orientation = Orientation.Horizontal };
            var reload = new Button { Content = "刷新列表", Style = (Style)Application.Current.Resources["BtnSecondary"], Height = 30, Padding = new Thickness(12, 0, 12, 0) };
            reload.Click += (s, e) => Reload();
            var readBtn = new Button { Content = "读取选中接口", Style = (Style)Application.Current.Resources["BtnPrimary"], Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            readBtn.Click += (s, e) => ReadSelected();
            var copy = new Button { Content = "复制报告", Style = (Style)Application.Current.Resources["BtnGhost"], Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0) };
            copy.Click += (s, e) => CopyReport();
            footBtns.Children.Add(reload);
            footBtns.Children.Add(readBtn);
            footBtns.Children.Add(copy);
            Grid.SetColumn(footBtns, 1);
            foot.Children.Add(footBtns);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            Content = root;
            SourceInitialized += (s, e) => Platform.ApplyWindowEffects(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Loaded += (s, e) => { Reload(); Ui.FadeIn(root, 200); };
        }

        private void Reload()
        {
            _ifaces = HidApi.Enumerate() ?? new List<HidInterface>();
            _list.Items.Clear();
            foreach (HidInterface it in _ifaces.OrderBy(i => i.VidPid).ThenBy(i => i.UsagePage))
            {
                var row = new StackPanel();
                row.Children.Add(Ui.Txt(it.VidPid + "   " + it.UsageText, 12, Theme.Text));
                string sub = (string.IsNullOrEmpty(it.Product) ? it.DisplayName : it.Product) +
                             "  ·  in " + it.InputLen + " / out " + it.OutputLen + " / feat " + it.FeatureLen;
                row.Children.Add(Ui.Txt(sub, 10.8, Theme.Text3));
                _list.Items.Add(row);
            }
            _status.Text = "共 " + _ifaces.Count + " 个 HID 接口 · 命令行 --diagnose 可导出完整报告";
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else _detail.Children.Clear();
        }

        private void ShowSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _ifaces.Count) return;
            _selected = _ifaces.OrderBy(x => x.VidPid).ThenBy(x => x.UsagePage).ElementAt(i);
            _detail.Children.Clear();
            _detail.Children.Add(Ui.Txt(_selected.VidPid + "  " + _selected.DisplayName, 15, Theme.Text, semibold: true));
            _detail.Children.Add(KeyValue("Usage", _selected.UsageText + (_selected.IsVendorPage ? "（厂商页）" : "")));
            _detail.Children.Add(KeyValue("报告长度", "输入 " + _selected.InputLen + " / 输出 " + _selected.OutputLen + " / Feature " + _selected.FeatureLen));
            _detail.Children.Add(KeyValue("厂商", string.IsNullOrEmpty(_selected.Manufacturer) ? "—" : _selected.Manufacturer));
            _detail.Children.Add(KeyValue("序列号", string.IsNullOrEmpty(_selected.Serial) ? "—" : _selected.Serial));
            _detail.Children.Add(KeyValue("分组", _selected.Group));
            _detail.Children.Add(KeyValue("接口路径", _selected.Path, true));
            var hint = Ui.Txt("提示：点“读取选中接口”会依次尝试标准电量字段、厂商协议、罗技 HID++ 与输入报告监听。", 11, Theme.Text3);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 12, 0, 0);
            _detail.Children.Add(hint);
        }

        private void ReadSelected()
        {
            if (_selected == null) return;
            _status.Text = "正在读取 " + _selected.VidPid + " …";
            HidInterface captured = _selected;
            Task.Run(() =>
            {
                BatteryReading r;
                try { r = HidApi.Read(captured, true); }
                catch (Exception ex) { r = BatteryReading.Fail("异常", ex.Message); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _detail.Children.Clear();
                    _detail.Children.Add(Ui.Txt(captured.VidPid + "  " + captured.DisplayName, 15, Theme.Text, semibold: true));
                    var chip = new Border
                    {
                        Margin = new Thickness(0, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = Ui.Chip(r.Ok ? "读取成功 · " + r.Level + "%" : "未读取到电量",
                            r.Ok ? Theme.LevelColor(r.Level) : Theme.Text2,
                            r.Ok ? Theme.AccentSoft : Theme.StrokeSoft, 11.5)
                    };
                    _detail.Children.Add(chip);
                    _detail.Children.Add(KeyValue("读取方式", string.IsNullOrEmpty(r.Source) ? "—" : r.Source));
                    _detail.Children.Add(KeyValue("说明", string.IsNullOrEmpty(r.Detail) ? "—" : r.Detail, true));
                    if (r.VoltageMv > 0) _detail.Children.Add(KeyValue("电压", r.VoltageMv + " mV"));
                    if (!string.IsNullOrEmpty(r.Firmware)) _detail.Children.Add(KeyValue("固件", r.Firmware));
                    if (r.Raw != null && r.Raw.Length > 0)
                    {
                        var mono = Ui.Txt(Hex(r.Raw), 11, Theme.Text2);
                        mono.FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI");
                        mono.TextWrapping = TextWrapping.Wrap;
                        var box = new Border
                        {
                            Background = Theme.StrokeSoft,
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(10),
                            Margin = new Thickness(0, 6, 0, 0),
                            Child = mono
                        };
                        _detail.Children.Add(box);
                    }
                    _status.Text = "读取完成：" + captured.VidPid;
                }));
            });
        }

        private void CopyReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("电量哨兵 · 设备诊断报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine("设备快照：");
            if (_app.LastSnapshot != null)
            {
                foreach (DeviceState d in _app.LastSnapshot)
                {
                    sb.AppendLine("  " + d.Name + " " + d.VidPid + " 电量=" + (d.HasLevel ? d.Level + "%" : "未知") +
                                  " 方式=" + d.Source + " 说明=" + d.Detail);
                }
            }
            sb.AppendLine("HID 接口：");
            foreach (HidInterface it in _ifaces)
            {
                sb.AppendLine("  " + it.VidPid + " " + it.UsageText + " in=" + it.InputLen + " out=" + it.OutputLen +
                              " feat=" + it.FeatureLen + " " + it.DisplayName);
            }
            try { Clipboard.SetText(sb.ToString()); _status.Text = "报告已复制到剪贴板"; } catch { }
        }

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static Grid KeyValue(string key, string value, bool mono = false)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Ui.Txt(key, 11.5, Theme.Text3));
            var v = Ui.Txt(value, mono ? 10.8 : 12, Theme.Text2);
            v.TextWrapping = TextWrapping.Wrap;
            v.LineHeight = 17;
            v.FontFamily = mono ? new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI") : Theme.Font;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }
    }
}
