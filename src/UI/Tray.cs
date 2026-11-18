using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using BatterySentinel.Core;

namespace BatterySentinel.UI
{
    /// <summary>托盘图标 + 右键菜单（图标内容跟随电量变化）。</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly AppController _app;
        private readonly NotifyIcon _nic;
        private readonly ContextMenuStrip _menu;
        private Icon _currentIcon;
        private bool _disposed;

        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _refreshItem;
        private readonly ToolStripMenuItem _autoItem;
        private readonly ToolStripMenuItem _alertItem;
        private readonly ToolStripMenuItem _bootItem;
        private readonly ToolStripMenuItem _settingsItem;
        private readonly ToolStripMenuItem _quitItem;

        public TrayIcon(AppController app)
        {
            _app = app;
            _menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Microsoft YaHei UI", 9f), Padding = new Padding(4) };
            _menu.Renderer = new TrayRenderer(Theme.IsDark);

            _openItem = new ToolStripMenuItem("打开电量哨兵");
            _openItem.Font = new Font(_menu.Font, System.Drawing.FontStyle.Bold);
            _openItem.Click += (s, e) => _app.ShowMain();
            _menu.Items.Add(_openItem);

            _refreshItem = new ToolStripMenuItem("立即刷新");
            _refreshItem.Click += (s, e) => _app.RefreshNow();
            _menu.Items.Add(_refreshItem);
            _menu.Items.Add(new ToolStripSeparator());

            _autoItem = new ToolStripMenuItem("自动刷新") { CheckOnClick = false };
            _autoItem.Click += (s, e) => { _app.Settings.AutoRefresh = !_app.Settings.AutoRefresh; _app.SaveSettings(); RefreshMenu(); };
            _menu.Items.Add(_autoItem);

            _alertItem = new ToolStripMenuItem("低电量提醒") { CheckOnClick = false };
            _alertItem.Click += (s, e) =>
            {
                _app.Settings.AlertStyle = _app.Settings.AlertStyle == "off" ? "toast" : "off";
                _app.SaveSettings();
                RefreshMenu();
            };
            _menu.Items.Add(_alertItem);
            _menu.Items.Add(new ToolStripSeparator());

            _bootItem = new ToolStripMenuItem("开机自启") { CheckOnClick = false };
            _bootItem.Click += (s, e) =>
            {
                bool want = !Platform.IsAutostartEnabled();
                if (Platform.SetAutostart(want))
                {
                    _app.Settings.StartWithWindows = want;
                    _app.Settings.Save();
                }
                RefreshMenu();
            };
            _menu.Items.Add(_bootItem);

            _settingsItem = new ToolStripMenuItem("设置…");
            _settingsItem.Click += (s, e) => _app.ShowMain(true);
            _menu.Items.Add(_settingsItem);
            _menu.Items.Add(new ToolStripSeparator());

            _quitItem = new ToolStripMenuItem("退出");
            _quitItem.Click += (s, e) => _app.Quit();
            _menu.Items.Add(_quitItem);

            _nic = new NotifyIcon
            {
                Visible = true,
                Icon = AppIcon.TrayIcon(-1, false, false, Platform.IsSystemLightForTray()),
                ContextMenuStrip = _menu,
                Text = "电量哨兵"
            };
            _nic.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) _app.ShowMain();
            };
            _nic.BalloonTipClicked += (s, e) => _app.ShowMain();
            RefreshMenu();
        }

        public void RefreshMenu()
        {
            try
            {
                _autoItem.Checked = _app.Settings.AutoRefresh;
                _alertItem.Checked = _app.Settings.AlertStyle != "off";
                _bootItem.Checked = Platform.IsAutostartEnabled();
            }
            catch { }
        }

        public void UpdateDevices(List<DeviceState> devices)
        {
            if (_disposed) return;
            DeviceState pick = null;
            if (devices != null)
            {
                pick = devices.Where(d => d.HasLevel).OrderBy(d => d.Level).FirstOrDefault();
            }
            int level = pick != null ? pick.Level : -1;
            bool critical = level >= 0 && level <= _app.Settings.WarnPercent;
            bool charging = pick != null && pick.Status == Hid.BatteryStatus.Charging;

            Icon next = AppIcon.TrayIcon(level, critical, charging, Platform.IsSystemLightForTray());
            _nic.Icon = next;
            if (_currentIcon != null && !ReferenceEquals(_currentIcon, next)) _currentIcon.Dispose();
            _currentIcon = next;

            string tip = BuildTooltip(devices);
            try { _nic.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip; } catch { }
            RefreshMenu();
        }

        private static string BuildTooltip(List<DeviceState> devices)
        {
            if (devices == null || devices.Count == 0) return "电量哨兵 · 未发现外设";
            var parts = new List<string>();
            foreach (DeviceState d in devices.Where(x => x.HasLevel).OrderBy(x => x.SortOrder).Take(3))
            {
                parts.Add(ShortName(d) + " " + d.Level + "%");
            }
            if (parts.Count == 0) return "电量哨兵 · 未读取到电量";
            return string.Join(" · ", parts);
        }

        private static string ShortName(DeviceState d)
        {
            if (string.IsNullOrEmpty(d.Name)) return "设备";
            return d.Name.Length > 8 ? d.Name.Substring(0, 7) + "…" : d.Name;
        }

        public void ShowBalloon(string title, string text)
        {
            try { _nic.ShowBalloonTip(4000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void BalloonAlert(string title, string text, bool critical)
        {
            try { _nic.ShowBalloonTip(6000, title, text, critical ? ToolTipIcon.Warning : ToolTipIcon.Info); } catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _nic.Visible = false; _nic.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            if (_currentIcon != null) { try { _currentIcon.Dispose(); } catch { } }
        }

        /// <summary>让 WinForms 菜单跟随深浅色。</summary>
        private sealed class TrayRenderer : ToolStripProfessionalRenderer
        {
            private readonly bool _dark;
            public TrayRenderer(bool dark) : base(new TrayColorTable(dark)) { _dark = dark; RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = _dark ? Color.FromArgb(0xF0, 0xF0, 0xF3) : Color.FromArgb(0x20, 0x20, 0x24);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(_dark ? Color.FromArgb(0x3A, 0x3D, 0x46) : Color.FromArgb(0xD8, 0xDB, 0xE1)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                }
            }
        }

        private sealed class TrayColorTable : ProfessionalColorTable
        {
            private readonly bool _dark;
            public TrayColorTable(bool dark) { _dark = dark; }

            public override Color ToolStripDropDownBackground { get { return _dark ? Color.FromArgb(0x23, 0x25, 0x2B) : Color.White; } }
            public override Color MenuBorder { get { return _dark ? Color.FromArgb(0x3A, 0x3D, 0x46) : Color.FromArgb(0xD8, 0xDB, 0xE1); } }
            public override Color MenuItemSelected { get { return _dark ? Color.FromArgb(0x33, 0x36, 0x40) : Color.FromArgb(0xEC, 0xEF, 0xF5); } }
            public override Color MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
            public override Color MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
            public override Color SeparatorDark { get { return _dark ? Color.FromArgb(0x3A, 0x3D, 0x46) : Color.FromArgb(0xE2, 0xE4, 0xE9); } }
            public override Color SeparatorLight { get { return SeparatorDark; } }
        }
    }
}
