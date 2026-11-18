using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using BatterySentinel.UI;

namespace BatterySentinel.Core
{
    /// <summary>与 Windows 系统打交道的零散功能：主题、强调色、开机自启、单实例、提示音。</summary>
    public static class Platform
    {
        public static bool IsLightTheme()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v is int) return (int)v != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool IsSystemLightForTray()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SystemUsesLightTheme");
                        if (v is int) return (int)v != 0;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>系统强调色（DWM AccentColor 是 ABGR）。</summary>
        public static Color AccentColor()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AccentColor");
                        if (v is int)
                        {
                            uint abgr = unchecked((uint)(int)v);
                            byte r = (byte)(abgr & 0xFF);
                            byte g = (byte)((abgr >> 8) & 0xFF);
                            byte b = (byte)((abgr >> 16) & 0xFF);
                            if (r + g + b > 60) return Color.FromRgb(r, g, b);
                        }
                    }
                }
            }
            catch { }
            return Color.FromRgb(0x4F, 0x8C, 0xFF);
        }

        /// <summary>界面强调色：默认使用固定的天蓝色（更符合电量语义），也可跟随系统强调色。</summary>
        public static Color EffectiveAccent(bool useSystem)
        {
            Color fallback = Color.FromRgb(0x5B, 0x9B, 0xFF);
            if (!useSystem) return fallback;
            Color c = AccentColor();
            double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            if (lum < 0.42) return fallback;   // 系统强调色太暗（深色主题常见）时退回天蓝
            return c;
        }

        public static string ExePath()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly();
                if (asm != null && !string.IsNullOrEmpty(asm.Location)) return asm.Location;
            }
            catch { }
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        // ---------------- 开机自启（启动文件夹快捷方式，失败时退回 HKCU Run） ----------------
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "BatterySentinel";

        public static string StartupShortcutPath()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(dir, "电量哨兵.lnk");
        }

        /// <summary>程序是否以管理员权限运行（提权时直接启动 explorer.exe 会 0xc0000142）。</summary>
        public static bool IsElevated()
        {
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(id);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool IsAutostartEnabled()
        {
            try
            {
                string exe = ExePath();
                string lnk = StartupShortcutPath();
                if (File.Exists(lnk))
                {
                    string target = ShellLink.TargetOf(lnk);
                    if (string.IsNullOrEmpty(target) || SamePath(target, exe)) return true;
                }
                string run = RunEntryValue();
                if (!string.IsNullOrEmpty(run) && run.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        /// <summary>当前自启方式（用于界面/命令行显示）。</summary>
        public static string AutostartMethod()
        {
            try
            {
                if (File.Exists(StartupShortcutPath())) return "启动文件夹快捷方式";
                if (!string.IsNullOrEmpty(RunEntryValue())) return "注册表启动项（HKCU Run）";
            }
            catch { }
            return "";
        }

        /// <summary>最近一次设置开机自启失败的详细原因（给界面/命令行显示）。</summary>
        public static string LastAutostartError = "";

        public static bool SetAutostart(bool enable)
        {
            string exe = ExePath();
            string lnk = StartupShortcutPath();
            LastAutostartError = "";

            if (!enable)
            {
                bool ok = true;
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch (Exception ex) { ok = false; LastAutostartError += "删除快捷方式失败：" + ex.Message + Environment.NewLine; }
                try { RemoveRunEntry(); } catch (Exception ex) { ok = false; LastAutostartError += "删除注册表项失败：" + ex.Message + Environment.NewLine; }
                return ok;
            }

            // 1) 启动文件夹快捷方式：进程内 COM（IShellLink），不再启动 powershell
            string error;
            if (ShellLink.Create(lnk, exe, "--minimized", Path.GetDirectoryName(exe), exe,
                    "电量哨兵 - 2.4G 外设电量监控", out error))
                return true;
            LastAutostartError = "① 启动文件夹快捷方式：" + (string.IsNullOrEmpty(error) ? "创建失败" : error) + Environment.NewLine;

            // 2) 兜底：写 HKCU\...\Run，任务管理器“启动”标签同样可见
            if (SetRunEntry(exe, out error)) return true;
            LastAutostartError += "② 注册表启动项：" + (string.IsNullOrEmpty(error) ? "写入失败" : error);
            return false;
        }

        /// <summary>配置目录是否可写（用来判断程序是否跑在受限/沙箱环境里）。</summary>
        public static bool CheckAppDataWritable()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.AppDir);
                string probe = Path.Combine(AppSettings.AppDir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string RunEntryValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (k == null) return "";
                    return Convert.ToString(k.GetValue(RunValueName)) ?? "";
                }
            }
            catch { return ""; }
        }

        private static bool SetRunEntry(string exe, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null)
                    {
                        error = "无法打开 HKCU\\" + RunKeyPath;
                        return false;
                    }
                    k.SetValue(RunValueName, "\"" + exe + "\" --minimized", RegistryValueKind.String);
                    object check = k.GetValue(RunValueName);
                    if (check == null)
                    {
                        error = "写入后读不到值";
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void RemoveRunEntry()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k != null) k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// 打开文件夹：优先用 Shell.Application（借已在运行的资源管理器打开，
        /// 不会自己启动 explorer.exe，也就不会遇到 0xc0000142）；失败再退回 ShellExecute。
        /// </summary>
        public static bool OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); } catch { }
            if (OpenFolderViaShellCom(path)) return true;
            if (IsElevated()) return false;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return true;
            }
            catch { }
            return false;
        }

        private static bool OpenFolderViaShellCom(string path)
        {
            object shell = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return false;
                shell = Activator.CreateInstance(t);
                t.InvokeMember("Open", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return true;
            }
            catch { return false; }
            finally
            {
                if (shell != null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
            }
        }

        public static void PlayAlertSound(bool critical)
        {
            try
            {
                if (critical)
                {
                    // 低电量：系统“关键停止”提示音，短促响亮但不吓人
                    System.Media.SystemSounds.Hand.Play();
                }
                else
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }
            }
            catch { }
        }

        // ---------------- 单实例 ----------------
        public const string MutexName = "Local\\BatterySentinel.SingleInstance.7C1F";
        public const string ShowWindowMessage = "BatterySentinel.ShowMainWindow.7C1F";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private static uint _showMsg;

        public static void BroadcastShowWindow()
        {
            try
            {
                if (_showMsg == 0) _showMsg = RegisterWindowMessage(ShowWindowMessage);
                PostMessage(new IntPtr(0xFFFF), _showMsg, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        public static uint ShowWindowMessageId()
        {
            if (_showMsg == 0) _showMsg = RegisterWindowMessage(ShowWindowMessage);
            return _showMsg;
        }

        // ---------------- Win11 窗口特效 ----------------
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        public static void ApplyWindowEffects(IntPtr hwnd)
        {
            try
            {
                int dark = Theme.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
                int corner = 2;                                            // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE
                int backdrop = 2;                                          // DWMSBT_MAINWINDOW (Mica)
                int hr = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
                if (hr != 0)
                {
                    // Win10/旧版 Win11 不支持 Mica：用扩展边框近似
                    var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                    DwmExtendFrameIntoClientArea(hwnd, ref m);
                }
            }
            catch { }
        }
    }
}
