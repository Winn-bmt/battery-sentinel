using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BatterySentinel.Core;
using BatterySentinel.Hid;
using BatterySentinel.UI;
using CoreMonitor = BatterySentinel.Core.Monitor;

namespace BatterySentinel
{
    public static class Program
    {
        public const string Version = "1.0.3";

        private static Mutex _mutex;
        private static bool _crashShown;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                MainCore(args);
            }
            catch (Exception ex)
            {
                ReportCrash(ex);
            }
        }

        private static void MainCore(string[] args)
        {
            // 显式初始化 HID 层（HidBattery 的静态构造函数会把真正的实现挂到 HidApi 上）
            HidApi.Enumerate = HidBattery.Enumerate;
            HidApi.Read = HidBattery.Read;
            HidApi.ValueCaps = HidBattery.ValueCaps;
            HidApi.BluetoothDevices = HidBattery.BluetoothDevices;

            bool diagnose = Has(args, "--diagnose");
            bool once = Has(args, "--once");
            string outFile = Value(args, "--out");
            bool minimized = Has(args, "--minimized") || Has(args, "--tray");

            if (Has(args, "--version"))
            {
                ConsoleMode.Attach();
                try { Console.WriteLine("电量哨兵 " + Program.Version); } catch { }
                if (!string.IsNullOrEmpty(outFile))
                {
                    try { File.WriteAllText(outFile, "电量哨兵 " + Program.Version + Environment.NewLine, new UTF8Encoding(false)); } catch { }
                }
                ConsoleMode.Detach();
                return;
            }

            if (diagnose || once)
            {
                ConsoleMode.Run(diagnose, outFile);
                return;
            }

            string autostart = Value(args, "--autostart");
            if (!string.IsNullOrEmpty(autostart))
            {
                ConsoleMode.Attach();
                var sbAs = new StringBuilder();
                try
                {
                    if (autostart == "on")
                        sbAs.AppendLine(Platform.SetAutostart(true)
                            ? "已加入开机自启（" + Platform.AutostartMethod() + "）：" + Platform.StartupShortcutPath()
                            : "设置失败：" + Environment.NewLine + Platform.LastAutostartError + Environment.NewLine +
                              "若程序运行在受限环境（沙箱/聊天工具直接启动），请从资源管理器双击 exe 再试。");
                    else if (autostart == "off")
                        sbAs.AppendLine(Platform.SetAutostart(false) ? "已取消开机自启" : "取消失败");
                    else
                    {
                        sbAs.AppendLine("版本：" + Program.Version);
                        sbAs.AppendLine("程序：" + Platform.ExePath());
                        sbAs.AppendLine("数据目录可写：" + (Platform.CheckAppDataWritable() ? "是" : "否"));
                        sbAs.AppendLine("管理员身份：" + (Platform.IsElevated() ? "是" : "否"));
                        sbAs.AppendLine("开机自启：" + (Platform.IsAutostartEnabled() ? "已启用（" + Platform.AutostartMethod() + "）" : "未启用"));
                        sbAs.AppendLine("快捷方式：" + Platform.StartupShortcutPath());
                        sbAs.AppendLine("注册表项：HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\BatterySentinel");
                    }
                }
                catch (Exception ex) { sbAs.AppendLine("失败：" + ex.Message); }
                Console.WriteLine(sbAs.ToString());
                if (!string.IsNullOrEmpty(outFile))
                {
                    try { File.WriteAllText(outFile, sbAs.ToString(), new UTF8Encoding(false)); } catch { }
                }
                ConsoleMode.Detach();
                return;
            }

            if (Has(args, "--alert-test"))
            {
                AlertLogicTest(outFile);
                return;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                ReportCrash(e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);

            AppSettings settings = AppSettings.Load();
            AlertMemory memory = AlertMemory.Load();
            AppController.LoadUserProtocols();

            bool dark = settings.Theme == "dark" || (settings.Theme == "system" && !Platform.IsLightTheme());
            Theme.Init(dark, Platform.EffectiveAccent(settings.UseSystemAccent));
            Ui.Init();

            if (Has(args, "--smoke"))
            {
                SmokeTest(outFile, settings, memory);
                return;
            }

            if (Has(args, "--selftest"))
            {
                SelfTest(outFile, settings, memory);
                return;
            }

            var controller = new AppController(settings, memory, false);

            if (Has(args, "--render"))
            {
                string dir = Value(args, "--render");
                try { RenderTest.Run(controller, dir); }
                catch (Exception ex) { WriteConsoleLine("渲染失败：" + ex); }
                Environment.Exit(0);
                return;
            }

            // 单实例：正常启动才需要（测试/渲染模式互不干扰）
            bool created;
            _mutex = new Mutex(true, Platform.MutexName, out created);
            if (!created)
            {
                Platform.BroadcastShowWindow();
                MessageBox.Show(
                    "电量哨兵已经在运行。\n\n如果打开的主窗口标题栏里看不到 v" + Version + "，" +
                    "说明后台还是旧版本：请右键任务栏托盘的电量图标 → 退出，再重新打开这个版本。",
                    "电量哨兵", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            controller.Start(!minimized);
            app.Run();
            GC.KeepAlive(_mutex);
        }

        /// <summary>启动冒烟测试：创建托盘、主窗口、监控线程与提醒弹窗，但都不显示，用于验证启动路径。</summary>
        private static void SmokeTest(string outFile, AppSettings settings, AlertMemory memory)
        {
            var sb = new StringBuilder();
            var controller = new AppController(settings, memory, false);
            try
            {
                sb.AppendLine("smoke " + DateTime.Now.ToString("HH:mm:ss"));
                controller.Start(false);
                sb.AppendLine("托盘=" + (controller.Tray != null));
                sb.AppendLine("主窗口=" + (controller.Main != null));
                sb.AppendLine("监控=" + (controller.Monitor != null));

                var notice = new AlertNotice
                {
                    Device = new DeviceState { Key = "smoke", Name = "冒烟测试设备", VidPid = "0000:0000", Level = 9, Supported = true, Present = true },
                    Level = AlertLevel.Critical,
                    Threshold = 10,
                    Title = "冒烟测试设备 电量告急",
                    Message = "这是一条不显示的测试弹窗。"
                };
                var toast = new ToastWindow(notice);
                sb.AppendLine("弹窗内容=" + (toast.Content != null));
                toast.Close();

                System.Threading.Thread.Sleep(500);
                sb.AppendLine("结果=OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果=FAIL");
                sb.AppendLine(ex.ToString());
            }
            try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { controller.Quit(); } catch { }
            Environment.Exit(0);
        }

        /// <summary>低电量提醒逻辑的确定性测试（不弹窗、不读写配置）。</summary>
        private static void AlertLogicTest(string outFile)
        {
            var sb = new StringBuilder();
            var s = new AppSettings { WarnPercent = 20, CriticalPercent = 10, AlertCooldownMinutes = 30, AlertStyle = "toast" };
            var mem = new AlertMemory();
            var mon = new CoreMonitor(s, mem);
            var alerts = new List<string>();
            mon.AlertRaised += n => alerts.Add(n.Level + " @" + n.Device.Level + "%");

            DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            int fails = 0;

            Action<string, int, BatteryStatus, DateTime, int> step = (name, level, status, when, expect) =>
            {
                var d = new DeviceState { Key = "t", Name = "测试鼠标", Level = level, Status = status, Supported = true, Present = true };
                mon.EvaluateAlerts(new List<DeviceState> { d }, when);
                bool ok = alerts.Count == expect;
                if (!ok) fails++;
                sb.AppendLine((ok ? "PASS " : "FAIL ") + name + "：提醒累计 " + alerts.Count + "（期望 " + expect + "）");
            };

            step("25% 不提醒", 25, BatteryStatus.Discharging, t0, 0);
            step("18% 首次提醒", 18, BatteryStatus.Discharging, t0, 1);
            step("18% 冷却期内不重复", 18, BatteryStatus.Discharging, t0.AddMinutes(1), 1);
            step("8% 升级为严重提醒", 8, BatteryStatus.Discharging, t0.AddMinutes(2), 2);
            step("8% 冷却期内不重复", 8, BatteryStatus.Discharging, t0.AddMinutes(3), 2);
            step("充电中不提醒并复位", 18, BatteryStatus.Charging, t0.AddMinutes(4), 2);
            step("重新放电到 18% 再次提醒", 18, BatteryStatus.Discharging, t0.AddMinutes(5), 3);
            step("超过冷却期后重复提醒", 16, BatteryStatus.Discharging, t0.AddMinutes(45), 4);
            step("回到 25% 复位", 25, BatteryStatus.Discharging, t0.AddMinutes(46), 4);
            step("再次跌破阈值提醒", 18, BatteryStatus.Discharging, t0.AddMinutes(47), 5);

            sb.AppendLine("提醒序列：" + string.Join(" | ", alerts.ToArray()));
            sb.AppendLine(fails == 0 ? "结果=PASS" : "结果=FAIL(" + fails + ")");
            try { Console.WriteLine(sb.ToString()); } catch { }
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
            }
        }

        /// <summary>同步自检：启动托盘/主窗口/监控线程，等首次读取完成后写报告退出（不需要消息循环）。</summary>
        private static void SelfTest(string outFile, AppSettings settings, AlertMemory memory)
        {
            var sb = new StringBuilder();
            AppController controller = null;
            try
            {
                controller = new AppController(settings, memory, false);
                controller.Start(false);

                DateTime deadline = DateTime.UtcNow.AddSeconds(18);
                List<DeviceState> snap = new List<DeviceState>();
                while (DateTime.UtcNow < deadline)
                {
                    snap = controller.Monitor.Snapshot();
                    if (snap.Count > 0) break;
                    System.Threading.Thread.Sleep(250);
                }

                sb.AppendLine("电量哨兵自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("托盘=" + (controller.Tray != null) + "  主窗口=" + (controller.Main != null) + "  监控=" + (controller.Monitor != null));
                sb.AppendLine("上次刷新=" + (controller.Monitor.LastRun != DateTime.MinValue ? controller.Monitor.LastRun.ToString("HH:mm:ss") : "未完成"));
                sb.AppendLine("设备 " + snap.Count + " 台：");
                foreach (DeviceState d in snap)
                {
                    sb.AppendLine("  " + d.Name + "  " + d.VidPid + "  " +
                                  (d.HasLevel ? d.Level + "%" : "未读取") + "  " + d.StatusText +
                                  "  " + d.Source + (d.VoltageMv > 0 ? "  " + d.VoltageMv + "mV" : "") +
                                  (string.IsNullOrEmpty(d.Detail) ? "" : "  [" + d.Detail + "]"));
                }
                sb.AppendLine("结果=" + (snap.Count > 0 ? "OK" : "FAIL(没有设备)"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果=FAIL");
                sb.AppendLine(ex.ToString());
            }
            finally
            {
                try { if (controller != null) controller.Quit(); } catch { }
            }
            try { Console.WriteLine(sb.ToString()); } catch { }
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
            }
            Environment.Exit(0);
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Value(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return "";
        }

        private static void WriteConsoleLine(string text)
        {
            try { Console.WriteLine(text); } catch { }
        }

        private static void ReportCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(AppSettings.AppDir);
                File.WriteAllText(AppSettings.CrashPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex, Encoding.UTF8);
            }
            catch { }
            if (_crashShown) return;
            _crashShown = true;
            try
            {
                MessageBox.Show("电量哨兵遇到一个错误，已写入：\n" + AppSettings.CrashPath + "\n\n" + ex.Message,
                    "电量哨兵", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }
    }

    /// <summary>把设置、监控、托盘、窗口串起来。</summary>
    public sealed class AppController
    {
        public AppSettings Settings;
        public AlertMemory Memory;
        public CoreMonitor Monitor;
        public TrayIcon Tray;
        public MainWindow Main;
        public List<DeviceState> LastSnapshot;
        public bool Quitting;

        /// <summary>配置目录是否可写；不可写通常意味着程序运行在受限环境里。</summary>
        public bool AppDataWritable = true;

        /// <summary>最近一次成功刷新的时间（没有监控线程时回退到快照里的时间）。</summary>
        public DateTime LastRunAt
        {
            get
            {
                if (Monitor != null && Monitor.LastRun != DateTime.MinValue) return Monitor.LastRun;
                if (LastSnapshot != null && LastSnapshot.Count > 0) return LastSnapshot[0].UpdatedAt;
                return DateTime.MinValue;
            }
        }
        private readonly bool _headless;
        private HwndSource _hwnd;
        private DispatcherTimer _deviceDebounce;

        public AppController(AppSettings settings, AlertMemory memory, bool headless)
        {
            Settings = settings;
            Memory = memory;
            _headless = headless;
        }

        public void Start(bool showWindow)
        {
            TrySyncAutostart();
            AppDataWritable = Platform.CheckAppDataWritable();
            if (_headless) return;

            Monitor = new CoreMonitor(Settings, Memory);
            Monitor.SnapshotReady += OnSnapshot;
            Monitor.AlertRaised += OnAlert;
            Monitor.Start();

            Tray = new TrayIcon(this);
            Main = new MainWindow(this);
            if (showWindow) Main.Show();
            HookWindow();
        }

        private void TrySyncAutostart()
        {
            // 只同步“真实状态”，绝不替用户改动启动项
            try
            {
                bool real = Platform.IsAutostartEnabled();
                if (Settings.StartWithWindows != real)
                {
                    Settings.StartWithWindows = real;
                    Settings.Save();
                }
            }
            catch { }
        }

        private void HookWindow()
        {
            if (Main == null) return;
            try
            {
                var helper = new WindowInteropHelper(Main);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) hwnd = helper.EnsureHandle();   // 不显示窗口也先建句柄（开机自启/最小化启动）
                _hwnd = HwndSource.FromHwnd(hwnd);
                if (_hwnd != null) _hwnd.AddHook(WndProc);
            }
            catch
            {
                // 拿不到窗口句柄不影响核心功能，只是收不到设备插拔/系统主题通知
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x0219;
            const int WM_SETTINGCHANGE = 0x001A;
            if (msg == WM_DEVICECHANGE)
            {
                if (_deviceDebounce == null)
                {
                    _deviceDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    _deviceDebounce.Tick += (s, e) =>
                    {
                        _deviceDebounce.Stop();
                        RefreshNow();
                    };
                }
                _deviceDebounce.Stop();
                _deviceDebounce.Start();
            }
            else if (msg == WM_SETTINGCHANGE)
            {
                if (Settings.Theme == "system") ApplyTheme();
            }
            else if ((uint)msg == Platform.ShowWindowMessageId())
            {
                ShowMain();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnSnapshot(List<DeviceState> devices)
        {
            Application app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                LastSnapshot = devices;
                if (Main != null && Main.IsLoaded) Main.OnSnapshot(devices);
                if (Tray != null) Tray.UpdateDevices(devices);
            }));
        }

        private void OnAlert(AlertNotice notice)
        {
            Application app = Application.Current;
            if (app == null) return;
            app.Dispatcher.BeginInvoke(new Action(() => ShowAlert(notice)));
        }

        private void ShowAlert(AlertNotice notice)
        {
            if (Settings.AlertStyle == "off") return;
            if (Settings.IsMutedNow()) return;
            if (Settings.AlertSound) Platform.PlayAlertSound(notice.Level == AlertLevel.Critical);

            switch (Settings.AlertStyle)
            {
                case "dialog":
                    try { new AlertDialog(notice).ShowDialog(); } catch { }
                    break;
                case "balloon":
                    if (Tray != null) Tray.BalloonAlert(notice.Title, notice.Message, notice.Level == AlertLevel.Critical);
                    break;
                default:
                    ToastManager.Show(notice, Snooze, () => ShowMain());
                    break;
            }
        }

        private void Snooze(string deviceKey)
        {
            Settings.MuteAllUntil = DateTime.UtcNow.AddMinutes(30);
            Settings.Save();
            if (Main != null) Main.OnSnapshot(LastSnapshot ?? new List<DeviceState>());
        }

        public void TestAlert()
        {
            DeviceState d = null;
            if (LastSnapshot != null)
                d = LastSnapshot.Where(x => x.HasLevel).OrderBy(x => x.Level).FirstOrDefault();
            if (d == null)
            {
                d = new DeviceState
                {
                    Key = "demo",
                    Name = "演示设备",
                    VidPid = "DEMO:0001",
                    KindText = "2.4G 鼠标",
                    Level = Math.Max(3, Settings.WarnPercent - 3),
                    Status = BatteryStatus.Discharging,
                    Supported = true,
                    Present = true,
                    Source = "演示数据",
                    Detail = "这是一条测试提醒，不会保存任何状态。"
                };
            }
            else
            {
                d = d.Clone();
                d.Level = Math.Max(2, Math.Min(d.Level, Settings.WarnPercent - 3));
                d.Status = BatteryStatus.Discharging;
                d.History = new List<int>();
            }
            AlertLevel lvl = d.Level <= Settings.CriticalPercent ? AlertLevel.Critical : AlertLevel.Warning;
            var notice = new AlertNotice
            {
                Device = d.Clone(),
                Level = lvl,
                Threshold = lvl == AlertLevel.Critical ? Settings.CriticalPercent : Settings.WarnPercent,
                Title = d.Name + (lvl == AlertLevel.Critical ? " 电量告急" : " 电量不足")
            };
            notice.Message = "当前电量 " + d.Level + "%，已低于 " + notice.Threshold + "% 提醒阈值。" +
                             (lvl == AlertLevel.Critical ? "请立即充电，以免突然断开。" : "建议尽快充电或更换电池。") + "（测试）";
            ShowAlert(notice);
        }

        public void RefreshNow()
        {
            if (Monitor != null) Monitor.RefreshNow();
        }

        public void ShowMain(bool settings = false)
        {
            if (Main == null) return;
            if (!Main.IsVisible) Main.Show();
            if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
            Main.ShowPage(settings);
            Main.Activate();
            Main.Topmost = true;
            Main.Topmost = false;
            Main.Focus();
        }

        public void ApplyTheme()
        {
            bool dark = Settings.Theme == "dark" || (Settings.Theme == "system" && !Platform.IsLightTheme());
            Theme.Init(dark, Platform.EffectiveAccent(Settings.UseSystemAccent));
            Ui.Init();
            if (Tray != null) Tray.RefreshMenu();
        }

        public void SaveSettings()
        {
            Settings.Save();
            if (Monitor != null) Monitor.ApplySettings(Settings);
            if (Tray != null) Tray.RefreshMenu();
            if (Main != null) Main.OnSnapshot(LastSnapshot ?? new List<DeviceState>());
        }

        public void OpenDiagnostics()
        {
            var w = new DiagnosticsWindow(this);
            w.Show();
        }

        /// <summary>自检模式：启动完整的界面/托盘/监控，几秒后输出报告并退出。</summary>
        public void BeginSelfTest(string outFile, int seconds)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var sb = new StringBuilder();
                sb.AppendLine("电量哨兵自检 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("托盘=" + (Tray != null) + "  主窗口=" + (Main != null) + "  监控=" + (Monitor != null));
                sb.AppendLine("设置：间隔=" + Settings.IntervalSeconds + "s 自动=" + Settings.AutoRefresh +
                              " 阈值=" + Settings.WarnPercent + "/" + Settings.CriticalPercent +
                              " 提醒=" + Settings.AlertStyle + " 主题=" + Settings.Theme);
                sb.AppendLine("上次刷新=" + (Monitor != null && Monitor.LastRun != DateTime.MinValue ? Monitor.LastRun.ToString("HH:mm:ss") : "未刷新"));
                if (LastSnapshot != null)
                {
                    sb.AppendLine("设备 " + LastSnapshot.Count + " 台：");
                    foreach (DeviceState d in LastSnapshot)
                    {
                        sb.AppendLine("  " + d.Name + "  " + d.VidPid + "  " +
                                      (d.HasLevel ? d.Level + "%" : "未读取") +
                                      "  " + d.StatusText + "  " + d.Source +
                                      (d.VoltageMv > 0 ? "  " + d.VoltageMv + "mV" : ""));
                    }
                }
                else sb.AppendLine("设备：尚未完成首次读取");
                sb.AppendLine("设置文件=" + AppSettings.SettingsPath);
                sb.AppendLine("配置文件=" + AppSettings.ProtocolsPath);
                try { Directory.CreateDirectory(AppSettings.AppDir); File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
                Quit();
            };
            timer.Start();
        }

        public void Quit()
        {
            if (Quitting) return;
            Quitting = true;
            try { ToastManager.CloseAll(); } catch { }
            try { if (Monitor != null) Monitor.Stop(); } catch { }
            try { if (Memory != null) Memory.Save(); } catch { }
            try { Settings.Save(); } catch { }
            try { if (Tray != null) Tray.Dispose(); } catch { }
            try { if (Main != null) Main.RequestRealClose(); } catch { }
            try { Application.Current.Shutdown(); } catch { }
        }

        // ---------------- 用户协议表 ----------------
        public static void LoadUserProtocols()
        {
            var list = new List<ProtocolSpec>();
            try
            {
                if (File.Exists(AppSettings.ProtocolsPath))
                {
                    var arr = Json.AsArr(Json.Parse(File.ReadAllText(AppSettings.ProtocolsPath, Encoding.UTF8)));
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            var d = Json.AsObj(o);
                            if (d == null) continue;
                            var spec = new ProtocolSpec();
                            spec.Name = Json.Str(d, "name", "自定义协议");
                            spec.Vid = ParseHexInt(Json.Str(d, "vid", "0"));
                            spec.UsagePage = ParseHexInt(Json.Str(d, "usagePage", "0"));
                            spec.ReportId = ParseHexInt(Json.Str(d, "reportId", "0"));
                            var payload = new List<byte>();
                            foreach (object b in Json.Arr(d, "payload"))
                                payload.Add((byte)Math.Max(0, Math.Min(255, Convert.ToInt32(b))));
                            spec.Payload = payload.ToArray();
                            spec.Length = Json.Int(d, "length", 64);
                            spec.LevelOffset = Json.Int(d, "levelOffset", 0);
                            spec.WaitMs = Json.Int(d, "waitMs", 150);
                            spec.Scale = Json.Int(d, "scale", 1);
                            spec.LevelMax = Json.Int(d, "levelMax", -1);
                            spec.ChargingOffset = Json.Int(d, "chargingOffset", -1);
                            spec.ChargingNonZero = Json.Bool(d, "chargingNonZero", true);
                            spec.VoltageOffset = Json.Int(d, "voltageOffset", -1);
                            spec.Mode = Json.Str(d, "mode", "");
                            spec.Notes = Json.Str(d, "notes", "");
                            spec.Enabled = Json.Bool(d, "enabled", true);
                            list.Add(spec);
                        }
                    }
                }
            }
            catch { }
            HidApi.UserProtocols = list;
        }

        private static int ParseHexInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            int v;
            if (int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            if (int.TryParse(s, out v)) return v;
            return 0;
        }
    }

    /// <summary>命令行模式：--once / --diagnose（可选 --out 输出到文件）。</summary>
    public static class ConsoleMode
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        public static void Attach()
        {
            try { AttachConsole(-1); } catch { }
        }

        public static void Detach()
        {
            try { FreeConsole(); } catch { }
        }

        public static void Run(bool diagnose, string outFile)
        {
            Attach();
            var sb = new StringBuilder();
            sb.AppendLine("电量哨兵 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                List<HidInterface> ifaces = HidApi.Enumerate() ?? new List<HidInterface>();
                if (diagnose)
                {
                    sb.AppendLine("=== HID 接口（" + ifaces.Count + "）===");
                    foreach (HidInterface it in ifaces.OrderBy(i => i.VidPid))
                    {
                        sb.AppendLine("  " + it.VidPid + " " + it.UsageText + " in=" + it.InputLen + " out=" + it.OutputLen +
                                      " feat=" + it.FeatureLen + "  " + it.DisplayName);
                        sb.AppendLine("      " + it.Path);
                    }
                }

                sb.AppendLine("=== 设备读数 ===");
                var groups = new Dictionary<string, List<HidInterface>>(StringComparer.OrdinalIgnoreCase);
                foreach (HidInterface it in ifaces)
                {
                    string g = string.IsNullOrEmpty(it.Group) ? it.Path : it.Group;
                    if (!groups.ContainsKey(g)) groups[g] = new List<HidInterface>();
                    groups[g].Add(it);
                }
                int okCount = 0;
                foreach (var kv in groups)
                {
                    var list = kv.Value;
                    if (list.Count == 0) continue;
                    HidInterface primary = list.OrderByDescending(i => (i.IsVendorPage ? 100 : 0) + (i.OutputLen > 0 ? 30 : 0)).First();
                    BatteryReading reading = null;
                    foreach (HidInterface it in list.OrderByDescending(i => (i.IsVendorPage ? 100 : 0) + (i.OutputLen > 0 ? 30 : 0)))
                    {
                        try { reading = HidApi.Read(it, true); } catch { reading = null; }
                        if (reading != null && reading.Ok && reading.Level >= 0) break;
                    }
                    if (reading != null && reading.Ok && reading.Level >= 0)
                    {
                        okCount++;
                        sb.AppendLine("  [OK] " + primary.VidPid + "  " + primary.DisplayName + "  电量 " + reading.Level + "%" +
                                      (reading.VoltageMv > 0 ? "  电压 " + reading.VoltageMv + "mV" : "") +
                                      (string.IsNullOrEmpty(reading.Source) ? "" : "  (" + reading.Source + ")"));
                    }
                    else if (diagnose)
                    {
                        sb.AppendLine("  [--] " + primary.VidPid + "  " + primary.DisplayName +
                                      "  " + (reading != null ? reading.Detail : "没有可用的读取方式"));
                    }
                }

                var bts = HidApi.BluetoothDevices();
                if (bts != null && bts.Count > 0)
                {
                    sb.AppendLine("=== 蓝牙 ===");
                    foreach (BtDevice b in bts)
                        sb.AppendLine("  " + b.Name + "  电量 " + (b.Battery >= 0 ? b.Battery + "%" : "未知") + "  " + b.Reason);
                }
                sb.AppendLine("读取成功 " + okCount + " 台设备");
            }
            catch (Exception ex)
            {
                sb.AppendLine("失败：" + ex);
            }

            try { Console.WriteLine(sb.ToString()); } catch { }
            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { }
            }
            try { FreeConsole(); } catch { }
        }
    }
}
