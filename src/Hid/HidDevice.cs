// ---------------------------------------------------------------------------
//  HidDevice.cs —— HID 接口枚举 + 句柄封装
//
//  设计要点（都是踩过的坑）：
//   * 鼠标 / 键盘 / 笔 / 触摸这类“系统独占”的顶层集合，用普通读写权限
//     CreateFile 会直接返回拒绝访问；改用 dwDesiredAccess = 0 打开，
//     依然可以读属性、能力表和 Feature Report。
//   * 绝对不要在同一个句柄上「一边阻塞 ReadFile、一边 WriteFile」——
//     HID 驱动层可能直接把进程打崩。所有 Input Report 读取都走一个
//     独立的、带 FILE_FLAG_OVERLAPPED 的读句柄 + 事件 + 超时 + CancelIoEx。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BatterySentinel.Hid
{
    /// <summary>HID 层异常（只在 HidDevice 内部使用；对外由 HidBattery 吞掉并转成中文说明）。</summary>
    public sealed class HidException : Exception
    {
        public int Win32Error { get; private set; }

        public HidException(string message) : base(message) { Win32Error = 0; }

        public HidException(string message, int win32Error) : base(message)
        {
            Win32Error = win32Error;
        }
    }

    /// <summary>一个已打开的 HID 接口句柄。</summary>
    public sealed class HidDevice : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private IntPtr _preparsed = IntPtr.Zero;
        private bool _writeAccess;
        private IntPtr _readHandle = IntPtr.Zero;   // 独立的 overlapped 读句柄
        private IntPtr _readEvent = IntPtr.Zero;
        private HidCaps _caps;
        private bool _disposed;

        /// <summary>设备接口路径（\\?\hid#vid_xxxx&pid_xxxx...）。</summary>
        public string Path { get; private set; }

        /// <summary>当前句柄是否真的有写权限。</summary>
        public bool WriteAccess { get { return _writeAccess; } }

        /// <summary>句柄是否有效。</summary>
        public bool IsOpen
        {
            get
            {
                return !_disposed && _handle != IntPtr.Zero &&
                       _handle != Win32.INVALID_HANDLE_VALUE;
            }
        }

        private HidDevice() { }

        // ===================================================================
        //  打开 / 关闭
        // ===================================================================

        /// <summary>
        /// 打开一个 HID 接口。
        /// needWrite=true 时先尝试「读写」，失败自动退化为「0 权限只读」；
        /// 返回 null 时 error 里是中文说明。
        /// </summary>
        public static HidDevice Open(string path, bool needWrite, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(path))
            {
                error = "设备路径为空";
                return null;
            }

            IntPtr h = IntPtr.Zero;
            bool write = false;
            int lastError = 0;

            if (needWrite)
            {
                h = Native.CreateFile(path,
                    Win32.GENERIC_READ | Win32.GENERIC_WRITE,
                    Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE,
                    IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero);
                if (IsValidHandle(h))
                {
                    write = true;
                }
                else
                {
                    lastError = Marshal.GetLastWin32Error();
                }
            }

            if (!IsValidHandle(h))
            {
                h = Native.CreateFile(path, 0,
                    Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE,
                    IntPtr.Zero, Win32.OPEN_EXISTING, 0, IntPtr.Zero);
                if (!IsValidHandle(h))
                {
                    lastError = Marshal.GetLastWin32Error();
                    error = "打不开设备接口（Win32 错误码 " + lastError + "）";
                    return null;
                }
                write = false;
            }

            var dev = new HidDevice();
            dev.Path = path;
            dev._handle = h;
            dev._writeAccess = write;

            IntPtr prep;
            if (Native.HidD_GetPreparsedData(h, out prep)) dev._preparsed = prep;
            return dev;
        }

        /// <summary>只读方式打开（0 访问权限，系统独占设备也能打开）。</summary>
        public static HidDevice Open(string path, out string error)
        {
            return Open(path, false, out error);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_readHandle != IntPtr.Zero)
            {
                try { Native.CancelIoEx(_readHandle, IntPtr.Zero); }
                catch { }
                Native.CloseHandle(_readHandle);
                _readHandle = IntPtr.Zero;
            }
            if (_readEvent != IntPtr.Zero)
            {
                Native.CloseHandle(_readEvent);
                _readEvent = IntPtr.Zero;
            }
            if (_preparsed != IntPtr.Zero)
            {
                Native.HidD_FreePreparsedData(_preparsed);
                _preparsed = IntPtr.Zero;
            }
            if (_handle != IntPtr.Zero && _handle != Win32.INVALID_HANDLE_VALUE)
            {
                Native.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private static bool IsValidHandle(IntPtr h)
        {
            return h != IntPtr.Zero && h != Win32.INVALID_HANDLE_VALUE;
        }

        // ===================================================================
        //  能力表
        // ===================================================================

        /// <summary>报告长度 / usage 等能力信息（首次访问时向驱动查询并缓存）。</summary>
        public HidCaps GetCaps()
        {
            if (_caps != null) return _caps;
            if (_preparsed == IntPtr.Zero)
                throw new HidException("没有 preparsed data，无法读取能力表");

            HIDP_CAPS caps;
            int status = Native.HidP_GetCaps(_preparsed, out caps);
            if (status != Win32.HIDP_STATUS_SUCCESS)
                throw new HidException("HidP_GetCaps 失败（status=0x" + status.ToString("X8") + "）");

            var result = new HidCaps();
            result.Usage = caps.Usage;
            result.UsagePage = caps.UsagePage;
            result.InputReportByteLength = caps.InputReportByteLength;
            result.OutputReportByteLength = caps.OutputReportByteLength;
            result.FeatureReportByteLength = caps.FeatureReportByteLength;
            result.NumberFeatureValueCaps = caps.NumberFeatureValueCaps;
            result.NumberInputValueCaps = caps.NumberInputValueCaps;
            _caps = result;
            return result;
        }

        /// <summary>指定报告类型的原始 HIDP_VALUE_CAPS 数组。</summary>
        public HIDP_VALUE_CAPS[] HidValueCaps(int reportType)
        {
            if (_preparsed == IntPtr.Zero) return new HIDP_VALUE_CAPS[0];

            ushort length = 0;
            Native.HidP_GetValueCaps(reportType, IntPtr.Zero, ref length, _preparsed);
            if (length == 0) return new HIDP_VALUE_CAPS[0];

            int size = Marshal.SizeOf(typeof(HIDP_VALUE_CAPS));
            IntPtr buf = Marshal.AllocHGlobal(size * length);
            try
            {
                ushort got = length;
                int status = Native.HidP_GetValueCaps(reportType, buf, ref got, _preparsed);
                if (status != Win32.HIDP_STATUS_SUCCESS) return new HIDP_VALUE_CAPS[0];

                var arr = new HIDP_VALUE_CAPS[got];
                for (int i = 0; i < got; i++)
                {
                    IntPtr p = IntPtr.Add(buf, i * size);
                    arr[i] = (HIDP_VALUE_CAPS)Marshal.PtrToStructure(p, typeof(HIDP_VALUE_CAPS));
                }
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>指定报告类型的值能力表（转成契约里的 ValueCap）。</summary>
        public List<ValueCap> ValueCaps(int reportType)
        {
            var list = new List<ValueCap>();
            foreach (var cap in HidValueCaps(reportType))
            {
                var vc = new ValueCap();
                vc.UsagePage = cap.UsagePage;
                vc.Usage = cap.UsageMin;
                vc.ReportId = cap.ReportID;
                vc.BitSize = cap.BitSize;
                vc.ReportCount = cap.ReportCount;
                vc.LogicalMin = cap.LogicalMin;
                vc.LogicalMax = cap.LogicalMax;
                vc.LinkCollection = cap.LinkCollection;
                vc.IsRange = cap.IsRange != 0;
                list.Add(vc);
            }
            return list;
        }

        /// <summary>
        /// 按能力表定义，从原始报文里取出某个字段的数值。
        /// report 必须包含第 0 字节的 Report ID。取不到返回 null。
        /// </summary>
        public int? UsageValue(int reportType, byte[] report, HIDP_VALUE_CAPS cap)
        {
            if (_preparsed == IntPtr.Zero) return null;
            if (report == null || report.Length == 0) return null;

            IntPtr buf = Marshal.AllocHGlobal(report.Length);
            try
            {
                Marshal.Copy(report, 0, buf, report.Length);
                uint value;
                int status = Native.HidP_GetUsageValue(reportType, cap.UsagePage,
                    cap.LinkCollection, cap.UsageMin, out value, _preparsed,
                    buf, (uint)report.Length);
                if (status != Win32.HIDP_STATUS_SUCCESS) return null;
                return unchecked((int)value);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        // ===================================================================
        //  报文读写
        // ===================================================================

        /// <summary>读一条 Feature Report（返回的 buf[0] 是 report id）。</summary>
        public byte[] GetFeatureReport(int reportId, int length)
        {
            if (!IsOpen) throw new HidException("设备句柄已关闭");
            if (length < 2) length = 2;

            IntPtr buf = Marshal.AllocHGlobal(length);
            try
            {
                for (int i = 0; i < length; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.WriteByte(buf, 0, (byte)(reportId & 0xFF));

                if (!Native.HidD_GetFeature(_handle, buf, (uint)length))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new HidException("读 Feature Report 0x" +
                        reportId.ToString("X2") + " 失败（Win32 错误码 " + err + "）", err);
                }
                return ReadManagedBytes(buf, length);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>写一条 Feature Report（data[0] 必须是 report id）。</summary>
        public void SetFeatureReport(byte[] data)
        {
            if (!IsOpen) throw new HidException("设备句柄已关闭");
            if (!_writeAccess) throw new HidException("当前句柄没有写权限，无法发送 Feature Report");
            if (data == null || data.Length == 0) throw new HidException("要发送的报文为空");

            IntPtr buf = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buf, data.Length);
                if (!Native.HidD_SetFeature(_handle, buf, (uint)data.Length))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new HidException("写 Feature Report 失败（Win32 错误码 " + err + "）", err);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>
        /// 发送 Output Report（data[0] 必须是 report id）。
        /// 先试 HidD_SetOutputReport，失败再退到 WriteFile。
        /// </summary>
        public void WriteOutput(byte[] data)
        {
            if (!IsOpen) throw new HidException("设备句柄已关闭");
            if (!_writeAccess) throw new HidException("当前句柄没有写权限，无法发送 Output Report");
            if (data == null || data.Length == 0) throw new HidException("要发送的报文为空");

            IntPtr buf = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buf, data.Length);

                if (Native.HidD_SetOutputReport(_handle, buf, (uint)data.Length)) return;

                uint written;
                if (!Native.WriteFile(_handle, buf, (uint)data.Length, out written, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new HidException("WriteFile 失败（Win32 错误码 " + err + "）", err);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>
        /// 读一条 Input Report，超时或取消返回 null。
        /// 内部使用独立的 overlapped 读句柄，绝不会和写操作抢同一个句柄。
        /// </summary>
        public byte[] ReadInput(int timeoutMs, Func<bool> stopFlag)
        {
            if (!IsOpen) return null;
            if (!EnsureReaderHandle()) return null;

            int length;
            try { length = GetCaps().InputReportByteLength; }
            catch { return null; }
            if (length <= 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal(length);
            int ovSize = Marshal.SizeOf(typeof(OVERLAPPED));
            IntPtr ovPtr = Marshal.AllocHGlobal(ovSize);
            try
            {
                var ov = new OVERLAPPED();
                ov.hEvent = _readEvent;
                Marshal.StructureToPtr(ov, ovPtr, false);
                Native.ResetEvent(_readEvent);

                if (Native.ReadFile(_readHandle, buffer, (uint)length, IntPtr.Zero, ovPtr))
                {
                    uint done;
                    if (!Native.GetOverlappedResult(_readHandle, ovPtr, out done, false))
                        return null;
                    return ReadManagedBytes(buffer, (int)Math.Min((uint)length, done));
                }

                int err = Marshal.GetLastWin32Error();
                if (err != Win32.ERROR_IO_PENDING) return null;

                var sw = Stopwatch.StartNew();
                if (timeoutMs <= 0) timeoutMs = 1;
                while (true)
                {
                    if (stopFlag != null && stopFlag())
                    {
                        CancelPendingRead(ovPtr);
                        return null;
                    }

                    long remain = timeoutMs - sw.ElapsedMilliseconds;
                    if (remain <= 0)
                    {
                        CancelPendingRead(ovPtr);
                        return null;
                    }

                    uint waited = Native.WaitForSingleObject(_readEvent,
                        (uint)Math.Min(remain, 200L));
                    if (waited == Win32.WAIT_OBJECT_0)
                    {
                        uint done2;
                        if (Native.GetOverlappedResult(_readHandle, ovPtr, out done2, false))
                            return ReadManagedBytes(buffer, (int)Math.Min((uint)length, done2));
                        return null;
                    }
                    if (waited == Win32.WAIT_FAILED)
                    {
                        CancelPendingRead(ovPtr);
                        return null;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ovPtr);
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>丢弃驱动里已经堆积的旧报文（发请求前调用）。</summary>
        public void Drain()
        {
            try
            {
                int length = GetCaps().InputReportByteLength;
                if (length <= 0) return;
                for (int i = 0; i < 64; i++)
                {
                    if (ReadInput(8, null) == null) return;
                }
            }
            catch
            {
                // 能力表读不到就没什么可丢的
            }
        }

        // ===================================================================
        //  内部：异步读句柄
        // ===================================================================

        private bool EnsureReaderHandle()
        {
            if (_readHandle != IntPtr.Zero) return true;

            IntPtr h = Native.CreateFile(Path, Win32.GENERIC_READ,
                Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, IntPtr.Zero,
                Win32.OPEN_EXISTING, Win32.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (!IsValidHandle(h)) return false;

            IntPtr ev = Native.CreateEvent(IntPtr.Zero, true, false, null);
            if (ev == IntPtr.Zero)
            {
                Native.CloseHandle(h);
                return false;
            }

            _readHandle = h;
            _readEvent = ev;
            return true;
        }

        /// <summary>取消挂起的异步读，并把句柄状态清干净（避免残留的 OVERLAPPED）。</summary>
        private void CancelPendingRead(IntPtr ovPtr)
        {
            try
            {
                Native.CancelIoEx(_readHandle, ovPtr);
                Native.WaitForSingleObject(_readEvent, 200);
                uint done;
                Native.GetOverlappedResult(_readHandle, ovPtr, out done, false);
                Native.ResetEvent(_readEvent);
            }
            catch
            {
            }
        }

        private static byte[] ReadManagedBytes(IntPtr buffer, int count)
        {
            if (count <= 0) return new byte[0];
            var result = new byte[count];
            Marshal.Copy(buffer, result, 0, count);
            return result;
        }

        // ===================================================================
        //  枚举
        // ===================================================================

        /// <summary>枚举系统里所有 HID 顶层集合（每个 setupapi 设备接口一条）。</summary>
        public static List<HidInterface> EnumerateAll()
        {
            var result = new List<HidInterface>();
            Guid guid = KnownGuids.HidDeviceInterface;

            IntPtr hdev = Native.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero,
                Win32.DIGCF_PRESENT | Win32.DIGCF_DEVICEINTERFACE);
            if (!IsValidHandle(hdev)) return result;

            try
            {
                uint index = 0;
                while (true)
                {
                    var iface = new SP_DEVICE_INTERFACE_DATA();
                    iface.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                    Guid g = guid;
                    if (!Native.SetupDiEnumDeviceInterfaces(hdev, IntPtr.Zero, ref g,
                            index, ref iface))
                        break;
                    index++;

                    uint need;
                    Native.SetupDiGetDeviceInterfaceDetail(hdev, ref iface, IntPtr.Zero,
                        0, out need, IntPtr.Zero);
                    if (need == 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal((int)need);
                    try
                    {
                        // cbSize：x64 = 8，x86 = 6（DWORD + WCHAR，字节对齐）
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        uint got;
                        if (!Native.SetupDiGetDeviceInterfaceDetail(hdev, ref iface, buf,
                                need, out got, IntPtr.Zero))
                            continue;

                        string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        HidInterface item = ProbeInterface(path);
                        if (item == null) continue;
                        item.Group = PhysicalGroupKey(path);
                        result.Add(item);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(hdev);
            }
            return result;
        }

        /// <summary>打开一个接口读属性 / 能力表（用 0 访问权限，独占设备也能读）。</summary>
        private static HidInterface ProbeInterface(string path)
        {
            var item = new HidInterface();
            item.Path = path;

            int vid, pid;
            ParseVidPid(path, out vid, out pid);
            item.Vid = vid;
            item.Pid = pid;

            IntPtr h = Native.CreateFile(path, 0,
                Win32.FILE_SHARE_READ | Win32.FILE_SHARE_WRITE, IntPtr.Zero,
                Win32.OPEN_EXISTING, 0, IntPtr.Zero);
            if (!IsValidHandle(h))
            {
                // 少数设备即使 0 权限也打不开：至少保留 vid:pid，界面上还能看到
                return item;
            }

            try
            {
                var attrs = new HIDD_ATTRIBUTES();
                attrs.Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (Native.HidD_GetAttributes(h, out attrs))
                {
                    item.Vid = attrs.VendorID;
                    item.Pid = attrs.ProductID;
                    item.Version = attrs.VersionNumber;
                }

                item.Product = ReadHidString(h, HidStringKind.Product);
                item.Manufacturer = ReadHidString(h, HidStringKind.Manufacturer);
                item.Serial = ReadHidString(h, HidStringKind.SerialNumber);

                IntPtr prep;
                if (Native.HidD_GetPreparsedData(h, out prep) && prep != IntPtr.Zero)
                {
                    try
                    {
                        HIDP_CAPS caps;
                        if (Native.HidP_GetCaps(prep, out caps) == Win32.HIDP_STATUS_SUCCESS)
                        {
                            item.UsagePage = caps.UsagePage;
                            item.Usage = caps.Usage;
                            item.InputLen = caps.InputReportByteLength;
                            item.OutputLen = caps.OutputReportByteLength;
                            item.FeatureLen = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        Native.HidD_FreePreparsedData(prep);
                    }
                }
            }
            finally
            {
                Native.CloseHandle(h);
            }

            item.Reserved = IsSystemReserved(item.UsagePage, item.Usage);
            return item;
        }

        private enum HidStringKind
        {
            Product,
            Manufacturer,
            SerialNumber
        }

        private static string ReadHidString(IntPtr handle, HidStringKind kind)
        {
            const int size = 512;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);

                bool ok;
                switch (kind)
                {
                    case HidStringKind.Product:
                        ok = Native.HidD_GetProductString(handle, buf, size);
                        break;
                    case HidStringKind.Manufacturer:
                        ok = Native.HidD_GetManufacturerString(handle, buf, size);
                        break;
                    default:
                        ok = Native.HidD_GetSerialNumberString(handle, buf, size);
                        break;
                }
                if (!ok) return "";
                string s = Marshal.PtrToStringUni(buf);
                return (s ?? "").Trim();
            }
            catch
            {
                return "";
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        // ===================================================================
        //  工具
        // ===================================================================

        /// <summary>从设备接口路径里解析 VID / PID。</summary>
        public static void ParseVidPid(string path, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (string.IsNullOrEmpty(path)) return;
            string lower = path.ToLowerInvariant();

            int i = lower.IndexOf("vid_", StringComparison.Ordinal);
            if (i >= 0 && lower.Length >= i + 8)
            {
                int v;
                if (int.TryParse(lower.Substring(i + 4, 4),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    vid = v;
            }
            i = lower.IndexOf("pid_", StringComparison.Ordinal);
            if (i >= 0 && lower.Length >= i + 8)
            {
                int v;
                if (int.TryParse(lower.Substring(i + 4, 4),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    pid = v;
            }
        }

        /// <summary>
        /// 同一个物理设备的多个接口归到一组：
        /// 路径形如 \\?\hid#vid_373b&pid_1216&mi_02&col01#7&70aa155&0&0000#{guid}，
        /// 用第 2 段去掉 mi_/col_ 后作为分组键。
        /// </summary>
        public static string PhysicalGroupKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string[] parts = path.Split('#');
            if (parts.Length < 2) return path;

            var keep = new List<string>();
            foreach (string raw in parts[1].Split('&'))
            {
                string low = raw.ToLowerInvariant();
                if (low.StartsWith("col", StringComparison.Ordinal) ||
                    low.StartsWith("mi_", StringComparison.Ordinal))
                    continue;
                keep.Add(raw);
            }
            return keep.Count > 0 ? string.Join("&", keep.ToArray()) : parts[1];
        }

        /// <summary>Windows 独占的顶层集合（只能用 0 访问权限打开，且发不了 Output Report）。</summary>
        public static bool IsSystemReserved(int usagePage, int usage)
        {
            if (usagePage == 0x0001)
                return usage == 0x0001 || usage == 0x0002 || usage == 0x0006 || usage == 0x0007;
            if (usagePage == 0x000D)
                return usage == 0x0001 || usage == 0x0002 || usage == 0x0004 || usage == 0x0005;
            return false;
        }

        private static readonly string[] GenericNames =
        {
            "HID 设备", "HID-compliant", "USB Composite Device", "HIDI2C Device"
        };

        /// <summary>在一组接口里挑一个最有信息量的名字作为设备名。</summary>
        public static string PickDeviceName(IList<HidInterface> items)
        {
            if (items == null || items.Count == 0) return "HID 设备";

            string best = "";
            int bestScore = int.MinValue;
            foreach (HidInterface it in items)
            {
                string name = (it.Product ?? "").Trim();
                if (name.Length == 0) continue;

                int score = name.Length;
                foreach (string g in GenericNames)
                {
                    if (name.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score -= 20;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(it.Manufacturer)) score += 5;
                if (it.IsVendorPage) score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = name;
                }
            }
            if (best.Length > 0) return best;
            return items[0].DisplayName;
        }
    }
}
