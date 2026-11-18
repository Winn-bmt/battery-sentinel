using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BatterySentinel.Core
{
    /// <summary>应用设置（settings.json）。</summary>
    public sealed class AppSettings
    {
        public int IntervalSeconds = 60;
        public bool AutoRefresh = true;
        public int WarnPercent = 20;
        public int CriticalPercent = 10;
        public string AlertStyle = "toast";       // toast | dialog | balloon | off
        public bool AlertSound = true;
        public int AlertCooldownMinutes = 30;
        public bool StartMinimized = false;
        public string Theme = "system";           // system | dark | light
        public bool ShowBluetooth = true;
        public bool StartWithWindows = false;
        public bool ShowUnsupported = false;
        public bool UseSystemAccent = false;
        public Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Muted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public DateTime MuteAllUntil = DateTime.MinValue;

        public static readonly int[] IntervalChoices = { 15, 30, 60, 120, 300, 600, 1800 };

        public static string AppDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatterySentinel"); } }
        public static string SettingsPath { get { return Path.Combine(AppDir, "settings.json"); } }
        public static string StatePath { get { return Path.Combine(AppDir, "state.json"); } }
        public static string ProtocolsPath { get { return Path.Combine(AppDir, "protocols.json"); } }
        public static string CrashPath { get { return Path.Combine(AppDir, "last-error.log"); } }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return s;
                var root = Json.AsObj(Json.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8)));
                if (root == null) return s;
                s.IntervalSeconds = Clamp(Json.Int(root, "interval", 60), 10, 3600);
                s.AutoRefresh = Json.Bool(root, "autoRefresh", true);
                s.WarnPercent = Clamp(Json.Int(root, "warn", 20), 5, 90);
                s.CriticalPercent = Clamp(Json.Int(root, "critical", 10), 1, 80);
                s.AlertStyle = Json.Str(root, "alertStyle", "toast");
                s.AlertSound = Json.Bool(root, "alertSound", true);
                s.AlertCooldownMinutes = Clamp(Json.Int(root, "cooldown", 30), 1, 720);
                s.StartMinimized = Json.Bool(root, "startMinimized", false);
                s.Theme = Json.Str(root, "theme", "system");
                s.ShowBluetooth = Json.Bool(root, "showBluetooth", true);
                s.StartWithWindows = Json.Bool(root, "startWithWindows", false);
                s.ShowUnsupported = Json.Bool(root, "showUnsupported", false);
                s.UseSystemAccent = Json.Bool(root, "useSystemAccent", false);
                foreach (object o in Json.Arr(root, "names"))
                {
                    var pair = Json.AsObj(o);
                    if (pair == null) continue;
                    string k = Json.Str(pair, "key");
                    string v = Json.Str(pair, "value");
                    if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v)) s.Names[k] = v;
                }
                foreach (object o in Json.Arr(root, "muted"))
                {
                    string k = Convert.ToString(o, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(k)) s.Muted.Add(k);
                }
                string muteUntil = Json.Str(root, "muteAllUntil", "");
                DateTime mu;
                if (DateTime.TryParse(muteUntil, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out mu)) s.MuteAllUntil = mu.ToUniversalTime();
                if (s.CriticalPercent >= s.WarnPercent) s.CriticalPercent = Math.Max(1, s.WarnPercent - 5);
            }
            catch
            {
                // 配置损坏时回退默认值，不影响启动
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var d = new Dictionary<string, object>();
                d["version"] = 1;
                d["interval"] = IntervalSeconds;
                d["autoRefresh"] = AutoRefresh;
                d["warn"] = WarnPercent;
                d["critical"] = CriticalPercent;
                d["alertStyle"] = AlertStyle;
                d["alertSound"] = AlertSound;
                d["cooldown"] = AlertCooldownMinutes;
                d["startMinimized"] = StartMinimized;
                d["theme"] = Theme;
                d["showBluetooth"] = ShowBluetooth;
                d["startWithWindows"] = StartWithWindows;
                d["showUnsupported"] = ShowUnsupported;
                d["useSystemAccent"] = UseSystemAccent;
                var names = new List<object>();
                foreach (var kv in Names)
                {
                    names.Add(new Dictionary<string, object> { { "key", kv.Key }, { "value", kv.Value } });
                }
                d["names"] = names;
                var muted = new List<object>();
                foreach (string k in Muted) muted.Add(k);
                d["muted"] = muted;
                d["muteAllUntil"] = MuteAllUntil == DateTime.MinValue ? "" : MuteAllUntil.ToString("o", CultureInfo.InvariantCulture);
                AtomicWrite(SettingsPath, Json.Stringify(d));
            }
            catch { }
        }

        public static void AtomicWrite(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch { }
        }

        public static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public string IntervalText()
        {
            int s = IntervalSeconds;
            if (s < 60) return s + " 秒";
            if (s % 60 == 0) return (s / 60) + " 分钟";
            return s + " 秒";
        }

        public bool IsMutedNow()
        {
            if (MuteAllUntil == DateTime.MinValue) return false;
            return DateTime.UtcNow < MuteAllUntil;
        }
    }

    /// <summary>低电量提醒的“已提醒”状态，避免重启后重复弹窗。</summary>
    public sealed class AlertEntry
    {
        public bool Warned;
        public bool Critical;
        public int LastSeverity;      // 0=正常 1=偏低 2=严重
        public int LastLevel = -1;
        public DateTime LastAlertUtc = DateTime.MinValue;
    }

    public sealed class AlertMemory
    {
        public Dictionary<string, AlertEntry> Entries = new Dictionary<string, AlertEntry>(StringComparer.OrdinalIgnoreCase);

        public AlertEntry For(string key)
        {
            AlertEntry e;
            if (!Entries.TryGetValue(key, out e))
            {
                e = new AlertEntry();
                Entries[key] = e;
            }
            return e;
        }

        public static AlertMemory Load()
        {
            var m = new AlertMemory();
            try
            {
                if (!File.Exists(AppSettings.StatePath)) return m;
                var root = Json.AsObj(Json.Parse(File.ReadAllText(AppSettings.StatePath, Encoding.UTF8)));
                if (root == null) return m;
                foreach (object o in Json.Arr(root, "alerts"))
                {
                    var d = Json.AsObj(o);
                    if (d == null) continue;
                    string key = Json.Str(d, "key");
                    if (string.IsNullOrEmpty(key)) continue;
                    var e = new AlertEntry();
                    e.Warned = Json.Bool(d, "warned");
                    e.Critical = Json.Bool(d, "critical");
                    e.LastSeverity = Json.Int(d, "sev", 0);
                    e.LastLevel = Json.Int(d, "level", -1);
                    DateTime t;
                    if (DateTime.TryParse(Json.Str(d, "time"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t))
                        e.LastAlertUtc = t.ToUniversalTime();
                    m.Entries[key] = e;
                }
            }
            catch { }
            return m;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.AppDir);
                var list = new List<object>();
                foreach (var kv in Entries)
                {
                    var d = new Dictionary<string, object>();
                    d["key"] = kv.Key;
                    d["warned"] = kv.Value.Warned;
                    d["critical"] = kv.Value.Critical;
                    d["sev"] = kv.Value.LastSeverity;
                    d["level"] = kv.Value.LastLevel;
                    d["time"] = kv.Value.LastAlertUtc == DateTime.MinValue ? "" : kv.Value.LastAlertUtc.ToString("o", CultureInfo.InvariantCulture);
                    list.Add(d);
                }
                var root = new Dictionary<string, object> { { "alerts", list } };
                AppSettings.AtomicWrite(AppSettings.StatePath, Json.Stringify(root));
            }
            catch { }
        }
    }
}
