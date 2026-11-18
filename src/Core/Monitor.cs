using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BatterySentinel.Hid;

namespace BatterySentinel.Core
{
    /// <summary>设备命名/分类的静态表。</summary>
    public static class DeviceCatalog
    {
        public static string KindFor(int vid, int pid, int usagePage, int usage)
        {
            switch (vid)
            {
                case 0x3654: return "2.4G 耳机";
                case 0x3554:
                case 0x373B:
                case 0x3367:
                case 0x3057:
                case 0x046D: return "2.4G 鼠标";
                case 0x1EA7: return "2.4G 键鼠";
                case 0x1915: return "2.4G 接收器";
            }
            if (usagePage == 0x0001 && (usage == 0x0002 || usage == 0x0001)) return "无线鼠标";
            if (usagePage == 0x0001 && (usage == 0x0006 || usage == 0x0007)) return "无线键盘";
            if (usagePage == 0x000D) return "无线笔/触控";
            if (usagePage >= 0xFF00) return "2.4G 接收器";
            return "无线外设";
        }

        public static string BaseNameFor(int vid, int pid, int usagePage, string product)
        {
            switch (vid)
            {
                case 0x3654: return "DARKRIM 无线耳机";
                case 0x3554: return "VXE 无线鼠标";
                case 0x373B: return "ATK 无线鼠标";
                case 0x3367: return "Endgame 无线鼠标";
                case 0x3057: return "VAXEE 无线鼠标";
                case 0x046D: return "罗技无线设备";
                case 0x1915: return "Nordic 无线接收器";
                case 0x1EA7: return "无线键鼠接收器";
            }
            if (!string.IsNullOrWhiteSpace(product) && !LooksGeneric(product))
                return product.Trim();
            return "无线外设";
        }

        private static bool LooksGeneric(string name)
        {
            string low = name.ToLowerInvariant();
            return low.Contains("hid-compliant") || low.Contains("hid 设备")
                || low.Contains("usb composite") || low.Contains("usb dongle")
                || low.Contains("virtual") || low.Contains("hid device");
        }

        public static int SortKeyFor(int vid)
        {
            switch (vid)
            {
                case 0x3554:
                case 0x373B: return 0;   // 鼠标
                case 0x1EA7: return 1;   // 键鼠
                case 0x3654: return 2;   // 耳机
                case 0x046D: return 3;
                case 0x3367:
                case 0x3057: return 4;
                case 0x1915: return 6;
                default: return 5;
            }
        }
    }

    /// <summary>
    /// 设备监控引擎：后台线程枚举 HID、读取电量、维护历史、判定低电量。
    /// 所有读取都在本线程完成，UI 只拿快照。
    /// </summary>
    public sealed class Monitor : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<DeviceState> _devices = new List<DeviceState>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);
        private AppSettings _settings;
        private readonly AlertMemory _memory;
        private Thread _thread;
        private volatile bool _running;
        private bool _firstScan = true;
        private DateTime _lastRun = DateTime.MinValue;
        private string _lastError = "";

        /// <summary>每轮读取完成后触发（在后台线程）。</summary>
        public event Action<List<DeviceState>> SnapshotReady;

        /// <summary>低电量提醒（在后台线程触发）。</summary>
        public event Action<AlertNotice> AlertRaised;

        /// <summary>读取时发生的可展示错误（例如发现未支持设备）。</summary>
        public event Action<string> Notice;

        public DateTime LastRun { get { lock (_gate) return _lastRun; } }

        public Monitor(AppSettings settings, AlertMemory memory)
        {
            _settings = settings;
            _memory = memory ?? new AlertMemory();
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            _wake.Set();
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "BatteryMonitor";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _stopEvent.Set();
            _wake.Set();
            Thread t = _thread;
            if (t != null && t.IsAlive && t != Thread.CurrentThread)
            {
                try { t.Join(2500); } catch { }
            }
        }

        public void Dispose() { Stop(); }

        /// <summary>立刻刷新一次。</summary>
        public void RefreshNow() { _wake.Set(); }

        public List<DeviceState> Snapshot()
        {
            lock (_gate)
            {
                return _devices.Select(d => d.Clone()).ToList();
            }
        }

        public string LastError { get { lock (_gate) return _lastError; } }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    RunOnce();
                }
                catch (Exception ex)
                {
                    lock (_gate) { _lastError = ex.Message; }
                }

                int ms = _settings.AutoRefresh ? Math.Max(10, _settings.IntervalSeconds) * 1000 : Timeout.Infinite;
                int idx = WaitHandle.WaitAny(new WaitHandle[] { _wake, _stopEvent }, ms);
                if (!_running) break;
                if (idx == 1) break;
            }
        }

        private void RunOnce()
        {
            AppSettings settings = _settings;
            var found = new List<DeviceState>();
            var previous = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                foreach (DeviceState d in _devices) previous[d.Key] = d;
            }

            List<HidInterface> ifaces;
            try
            {
                ifaces = HidApi.Enumerate() ?? new List<HidInterface>();
            }
            catch (Exception ex)
            {
                lock (_gate) { _lastError = "枚举 HID 设备失败：" + ex.Message; }
                return;
            }

            // 按物理设备分组
            var groups = new Dictionary<string, List<HidInterface>>(StringComparer.OrdinalIgnoreCase);
            foreach (HidInterface it in ifaces)
            {
                string g = string.IsNullOrEmpty(it.Group) ? it.Path : it.Group;
                List<HidInterface> list;
                if (!groups.TryGetValue(g, out list))
                {
                    list = new List<HidInterface>();
                    groups[g] = list;
                }
                list.Add(it);
            }

            var keyCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int unsupported = 0;

            foreach (var kv in groups)
            {
                List<HidInterface> list = kv.Value;
                if (list.Count == 0) continue;

                HidInterface primary = PickPrimary(list);
                if (primary == null) continue;

                // 跳过明显不是外设的组（无厂商页、无电量字段、无输出报告）
                if (!LooksLikePeripheral(list)) continue;

                int vid = primary.Vid;
                string baseKey = vid.ToString("X4") + ":" + primary.Pid.ToString("X4");
                int n;
                keyCount.TryGetValue(baseKey, out n);
                keyCount[baseKey] = n + 1;
                string key = n == 0 ? baseKey : baseKey + "#" + (n + 1);

                bool monitorInput = _firstScan && !previous.ContainsKey(key);
                BatteryReading reading = TryReadGroup(list, monitorInput);

                DeviceState prev = null;
                previous.TryGetValue(key, out prev);

                var st = new DeviceState();
                st.Key = key;
                st.VidPid = vid.ToString("X4") + ":" + primary.Pid.ToString("X4");
                st.BaseName = DeviceCatalog.BaseNameFor(vid, primary.Pid, primary.UsagePage, primary.Product);
                st.KindText = DeviceCatalog.KindFor(vid, primary.Pid, primary.UsagePage, primary.Usage);
                st.SortOrder = DeviceCatalog.SortKeyFor(vid);
                st.Present = true;
                st.InterfacePath = primary.Path;
                st.Name = CustomName(settings, key, st.BaseName);

                if (reading != null && reading.Ok && reading.Level >= 0)
                {
                    st.Level = Math.Max(0, Math.Min(100, reading.Level));
                    st.Supported = true;
                    st.Status = reading.Status;
                    st.VoltageMv = reading.VoltageMv;
                    st.Source = reading.Source;
                    st.Detail = reading.Detail;
                    st.ModelName = reading.ModelName;
                    st.Firmware = reading.Firmware;
                    st.RawHex = Hex(reading.Raw);
                    st.UpdatedAt = DateTime.Now;
                    st.ConnectionText = BuildConnection(primary, st);
                    st.History = new List<int>(prev != null ? prev.History : new List<int>());
                    AppendHistory(st, st.Level, prev);
                }
                else
                {
                    st.Supported = false;
                    st.Level = prev != null ? prev.Level : -1;
                    st.Status = prev != null ? prev.Status : BatteryStatus.Unknown;
                    st.History = new List<int>(prev != null ? prev.History : new List<int>());
                    st.UpdatedAt = prev != null ? prev.UpdatedAt : DateTime.MinValue;
                    st.Stale = prev != null && prev.HasLevel;
                    st.ErrorText = reading != null && !string.IsNullOrEmpty(reading.Detail)
                        ? reading.Detail
                        : "未找到可用的电量读取方式";
                    st.ConnectionText = BuildConnection(primary, st);
                    st.Source = reading != null ? reading.Source : "";
                    unsupported++;
                }

                if (!st.HasLevel && !settings.ShowUnsupported && IsInternalNoise(primary, list)) continue;
                found.Add(st);
            }

            // 蓝牙设备
            if (settings.ShowBluetooth)
            {
                try
                {
                    List<BtDevice> bts = HidApi.BluetoothDevices() ?? new List<BtDevice>();
                    foreach (BtDevice b in bts)
                    {
                        if (b.Battery < 0) continue;
                        string key = "bt:" + b.InstanceId;
                        DeviceState prev;
                        previous.TryGetValue(key, out prev);
                        var st = new DeviceState();
                        st.Key = key;
                        st.IsBluetooth = true;
                        st.VidPid = "蓝牙";
                        st.BaseName = CleanBluetoothName(b.Name);
                        st.Name = CustomName(settings, key, st.BaseName);
                        st.KindText = b.IsHandsFree ? "蓝牙耳机" : "蓝牙设备";
                        st.Level = Math.Max(0, Math.Min(100, b.Battery));
                        st.Status = BatteryStatus.Unknown;
                        st.Source = "蓝牙 HFP 电量属性";
                        st.Detail = b.Reason;
                        st.Present = true;
                        st.Supported = true;
                        st.UpdatedAt = DateTime.Now;
                        st.ConnectionText = "蓝牙 · 已连接";
                        st.SortOrder = 10;
                        st.History = new List<int>(prev != null ? prev.History : new List<int>());
                        AppendHistory(st, st.Level, prev);
                        found.Add(st);
                    }
                }
                catch { }
            }

            found = found.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToList();

            lock (_gate)
            {
                _devices.Clear();
                _devices.AddRange(found);
                _lastRun = DateTime.Now;
                _lastError = "";
            }

            EvaluateAlerts(found, DateTime.UtcNow);

            Action<List<DeviceState>> cb = SnapshotReady;
            if (cb != null)
            {
                try { cb(found.Select(d => d.Clone()).ToList()); } catch { }
            }

            if (unsupported > 0 && _firstScan)
            {
                Action<string> n = Notice;
                if (n != null) { try { n("发现 " + unsupported + " 个暂不支持读取电量的外设"); } catch { } }
            }
            _firstScan = false;
        }

        private static string Hex(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string CustomName(AppSettings s, string key, string fallback)
        {
            string custom;
            if (s.Names.TryGetValue(key, out custom) && !string.IsNullOrWhiteSpace(custom)) return custom;
            return fallback;
        }

        private static void AppendHistory(DeviceState st, int level, DeviceState prev)
        {
            List<int> h = st.History;
            if (h == null) { h = new List<int>(); st.History = h; }
            if (h.Count == 0 || h[h.Count - 1] != level || prev == null || (DateTime.Now - prev.UpdatedAt).TotalMinutes >= 5)
                h.Add(level);
            while (h.Count > 60) h.RemoveAt(0);
        }

        private static string CleanBluetoothName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "蓝牙设备";
            string[] suffixes = { " Hands-Free AG", " Hands-Free HF", " Hands-Free", "Hands-Free AG", "Hands-Free HF", "Hands-Free", " AG", " HF" };
            string result = name.Trim();
            foreach (string suffix in suffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length).Trim();
                    break;
                }
            }
            return string.IsNullOrWhiteSpace(result) ? name.Trim() : result;
        }

        private static string BuildConnection(HidInterface p, DeviceState st)
        {
            string bus = (st.IsBluetooth ? "蓝牙" : "USB");
            return bus + " " + st.VidPid;
        }

        /// <summary>笔记本内置的触摸板 / ITE 控制器 / 虚拟设备，不是 2.4G 外设，默认不显示。</summary>
        private static bool IsInternalNoise(HidInterface primary, List<HidInterface> list)
        {
            string s = ((primary.Product ?? "") + " " + (primary.Path ?? "")).ToLowerInvariant();
            if (s.Contains("hidi2c") || s.Contains("hid over i2c")) return true;
            if (s.Contains("virtual")) return true;
            if (s.Contains("ite device")) return true;
            bool anyOut = list.Any(i => i.OutputLen > 0 || i.FeatureLen > 0);
            if (!anyOut && !primary.IsVendorPage) return true;
            return false;
        }

        private static bool LooksLikePeripheral(List<HidInterface> list)
        {
            foreach (HidInterface it in list)
            {
                if (it.IsVendorPage) return true;
                if (it.UsagePage == 0x0001 || it.UsagePage == 0x000C || it.UsagePage == 0x000D) return true;
                if (it.UsagePage == 0x0085 || (it.UsagePage == 0x0006)) return true;
            }
            return false;
        }

        private static HidInterface PickPrimary(List<HidInterface> list)
        {
            HidInterface best = null;
            int bestScore = int.MinValue;
            foreach (HidInterface it in list)
            {
                int score = 0;
                if (it.IsVendorPage) score += 50;
                if (it.OutputLen > 0) score += 20;
                if (it.FeatureLen > 0) score += 10;
                if (!string.IsNullOrEmpty(it.Product) && !it.Product.ToLowerInvariant().Contains("hid-compliant")) score += 5;
                if (it.UsagePage == 0x0001 && it.Usage == 0x0002) score += 3;   // 鼠标
                if (it.UsagePage == 0x0001 && it.Usage == 0x0006) score += 2;   // 键盘
                if (score > bestScore) { bestScore = score; best = it; }
            }
            return best;
        }

        private static BatteryReading TryReadGroup(List<HidInterface> list, bool monitorInput)
        {
            // 先试厂商页接口（更可能命中私有协议），再试标准接口
            var ordered = list
                .OrderByDescending(i => (i.IsVendorPage ? 100 : 0) + (i.OutputLen > 0 ? 30 : 0) + (i.FeatureLen > 0 ? 10 : 0))
                .ToList();

            BatteryReading bestFail = null;
            foreach (HidInterface it in ordered)
            {
                BatteryReading r;
                try { r = HidApi.Read(it, monitorInput); }
                catch (Exception ex) { r = BatteryReading.Fail("读取异常", ex.Message); }
                if (r != null && r.Ok && r.Level >= 0) return r;
                if (bestFail == null && r != null && !string.IsNullOrEmpty(r.Detail)) bestFail = r;
            }
            return bestFail;
        }

        internal void EvaluateAlerts(List<DeviceState> devs, DateTime now)
        {
            AppSettings s = _settings;
            if (s.AlertStyle == "off") return;
            if (s.IsMutedNow()) return;

            foreach (DeviceState d in devs)
            {
                if (!d.HasLevel) continue;
                if (s.Muted.Contains(d.Key)) continue;

                AlertEntry e = _memory.For(d.Key);

                if (d.Charging || d.Level >= s.WarnPercent + 5)
                {
                    e.Warned = false;
                    e.Critical = false;
                    e.LastSeverity = 0;
                }
                if (d.Charging) { e.LastLevel = d.Level; continue; }
                if (d.Level > s.WarnPercent) { e.LastLevel = d.Level; continue; }

                int severity = d.Level <= s.CriticalPercent ? 2 : 1;
                bool escalate = severity > e.LastSeverity;
                bool firstInBand = !e.Warned;
                bool cooling = e.LastAlertUtc != DateTime.MinValue &&
                               (now - e.LastAlertUtc).TotalMinutes < Math.Max(1, s.AlertCooldownMinutes);
                bool repeat = e.Warned && !cooling;

                if (!escalate && !firstInBand && !repeat) { e.LastLevel = d.Level; continue; }
                if (cooling && !escalate) { e.LastLevel = d.Level; continue; }

                AlertLevel lvl = severity >= 2 ? AlertLevel.Critical : AlertLevel.Warning;
                e.Warned = true;
                if (severity >= 2) e.Critical = true;
                e.LastSeverity = Math.Max(e.LastSeverity, severity);
                e.LastAlertUtc = now;
                e.LastLevel = d.Level;
                _memory.Save();

                var notice = new AlertNotice();
                notice.Device = d.Clone();
                notice.Level = lvl;
                notice.Threshold = lvl == AlertLevel.Critical ? s.CriticalPercent : s.WarnPercent;
                notice.Title = d.Name + (lvl == AlertLevel.Critical ? " 电量告急" : " 电量不足");
                notice.Message = "当前电量 " + d.Level + "%，已低于 " + notice.Threshold + "% 提醒阈值。" +
                                 (lvl == AlertLevel.Critical ? "请立即充电，以免突然断开。" : "建议尽快充电或更换电池。");
                Action<AlertNotice> cb = AlertRaised;
                if (cb != null) { try { cb(notice); } catch { } }
            }
        }

        /// <summary>供界面显示的一行摘要。</summary>
        public static string BuildSummary(List<DeviceState> devices)
        {
            if (devices == null || devices.Count == 0) return "未发现 2.4G 外设";
            int online = 0, low = 0;
            foreach (DeviceState d in devices)
            {
                if (d.HasLevel) online++;
                if (d.HasLevel && d.Level <= 20 && !d.Charging) low++;
            }
            var sb = new StringBuilder();
            sb.Append("在线 ").Append(online).Append(" 台");
            if (low > 0) sb.Append(" · 低电量 ").Append(low).Append(" 台");
            return sb.ToString();
        }
    }
}
