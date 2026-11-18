// ---------------------------------------------------------------------------
//  BluetoothBattery.cs —— 蓝牙（HFP）电量
//
//  原理：Windows 蓝牙协议栈收到耳机通过 HFP 上报的电量后，会写到设备
//  Hands-Free / 免提 节点上的一个未公开 PnP 属性：
//      {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2   (DEVPROP_TYPE_BYTE)
//  值就是电量百分比（0-100）。
//
//  注意：
//   1. 属性挂在 Hands-Free AG 节点上，不在设备主节点上；
//   2. 耳机没连上 / 不支持 HFP 电量上报时，属性不存在或为 0。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BatterySentinel.Hid
{
    /// <summary>蓝牙设备电量读取（SetupAPI + 未公开 PnP 属性）。</summary>
    public static class BluetoothBattery
    {
        private sealed class EnumAttempt
        {
            public Guid? ClassGuid;
            public string Enumerator;
            public uint Flags;
        }

        /// <summary>按 PnP 属性的语义返回所有蓝牙节点（含没有电量的）。</summary>
        public static List<BtDevice> AllDevices()
        {
            var found = new Dictionary<string, BtDevice>(StringComparer.OrdinalIgnoreCase);

            var attempts = new List<EnumAttempt>();
            attempts.Add(new EnumAttempt { Flags = Win32.DIGCF_ALLCLASSES });
            attempts.Add(new EnumAttempt { Enumerator = "BTHENUM", Flags = 0 });
            attempts.Add(new EnumAttempt { ClassGuid = KnownGuids.SystemClass, Flags = 0 });
            attempts.Add(new EnumAttempt { ClassGuid = KnownGuids.BluetoothClass, Flags = 0 });

            foreach (EnumAttempt attempt in attempts)
            {
                try
                {
                    Collect(attempt, found);
                }
                catch
                {
                    // 某条枚举路径失败不影响其它路径
                }
            }

            return new List<BtDevice>(found.Values);
        }

        /// <summary>界面用：只保留“当前确实连接着”的蓝牙设备。</summary>
        public static List<BtDevice> Devices()
        {
            List<BluetoothRadio.ConnectedDevice> connected = BluetoothRadio.Connected();
            var result = new List<BtDevice>();
            foreach (BtDevice dev in AllDevices())
            {
                if (!dev.HasProperty) continue;
                if (!IsConnected(dev, connected)) continue;
                result.Add(dev);
            }

            result.Sort(delegate (BtDevice a, BtDevice b)
            {
                int ah = a.IsHandsFree ? 0 : 1;
                int bh = b.IsHandsFree ? 0 : 1;
                if (ah != bh) return ah.CompareTo(bh);
                return b.Battery.CompareTo(a.Battery);
            });
            return result;
        }

        /// <summary>
        /// PnP 里的 Hands-Free 节点在耳机断开后仍然存在（并保留上次的电量），
        /// 所以必须再和“当前连接集合”比对一次：优先用实例 ID 里的设备地址，其次用名称。
        /// </summary>
        private static bool IsConnected(BtDevice dev, List<BluetoothRadio.ConnectedDevice> connected)
        {
            if (connected == null || connected.Count == 0) return false;

            string address = BluetoothRadio.AddressFromInstanceId(dev.InstanceId);
            foreach (BluetoothRadio.ConnectedDevice c in connected)
            {
                if (!string.IsNullOrEmpty(address) &&
                    string.Equals(address, c.AddressHex, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (NameMatches(dev.Name, c.Name)) return true;
            }
            return false;
        }

        private static bool NameMatches(string pnpName, string radioName)
        {
            string a = CleanName(pnpName);
            string b = CleanName(radioName);
            if (a.Length < 2 || b.Length < 2) return false;
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                   b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string result = name.Trim();
            string[] suffixes = { " Hands-Free AG", " Hands-Free HF", " Hands-Free", "Hands-Free AG", "Hands-Free HF", "Hands-Free", " AG", " HF" };
            foreach (string suffix in suffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length).Trim();
                    break;
                }
            }
            return result;
        }

        private static void Collect(EnumAttempt attempt, Dictionary<string, BtDevice> found)
        {
            IntPtr hdev;
            if (attempt.ClassGuid.HasValue)
            {
                Guid guid = attempt.ClassGuid.Value;
                hdev = Native.SetupDiGetClassDevs(ref guid, attempt.Enumerator,
                    IntPtr.Zero, attempt.Flags | Win32.DIGCF_PRESENT);
            }
            else
            {
                hdev = Native.SetupDiGetClassDevsNoClass(IntPtr.Zero, attempt.Enumerator,
                    IntPtr.Zero, attempt.Flags | Win32.DIGCF_PRESENT);
            }

            if (hdev == IntPtr.Zero || hdev == Win32.INVALID_HANDLE_VALUE) return;

            try
            {
                uint index = 0;
                while (true)
                {
                    var info = new SP_DEVINFO_DATA();
                    info.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    if (!Native.SetupDiEnumDeviceInfo(hdev, index, ref info)) break;
                    index++;

                    string instanceId = GetInstanceId(hdev, ref info);
                    if (string.IsNullOrEmpty(instanceId)) continue;
                    if (!instanceId.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (found.ContainsKey(instanceId)) continue;

                    string name = GetRegistryProperty(hdev, ref info, Win32.SPDRP_FRIENDLYNAME);
                    if (string.IsNullOrEmpty(name))
                        name = GetRegistryProperty(hdev, ref info, Win32.SPDRP_DEVICEDESC);

                    var device = new BtDevice();
                    device.Name = string.IsNullOrEmpty(name) ? "(无名称)" : name;
                    device.InstanceId = instanceId;

                    int battery;
                    bool hasProperty;
                    string reason;
                    ReadBatteryProperty(hdev, ref info, out battery, out hasProperty, out reason);
                    device.Battery = battery;
                    device.HasProperty = hasProperty;
                    device.Reason = reason;

                    found[instanceId] = device;
                }
            }
            finally
            {
                Native.SetupDiDestroyDeviceInfoList(hdev);
            }
        }

        private static string GetInstanceId(IntPtr hdev, ref SP_DEVINFO_DATA info)
        {
            const int size = 1024;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                uint need;
                if (!Native.SetupDiGetDeviceInstanceId(hdev, ref info, buf, size, out need))
                    return "";
                return Marshal.PtrToStringUni(buf) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static string GetRegistryProperty(IntPtr hdev, ref SP_DEVINFO_DATA info,
            uint property)
        {
            const int size = 2048;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                uint type;
                uint need;
                if (!Native.SetupDiGetDeviceRegistryProperty(hdev, ref info, property,
                        out type, buf, size, out need))
                    return "";
                return (Marshal.PtrToStringUni(buf) ?? "").Trim();
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>读取 {104EA319-6EE2-4701-BD47-8DDBF425BBE5} pid=2（DEVPROP_TYPE_BYTE）。</summary>
        private static void ReadBatteryProperty(IntPtr hdev, ref SP_DEVINFO_DATA info,
            out int battery, out bool hasProperty, out string reason)
        {
            battery = -1;
            hasProperty = false;
            reason = "";

            var key = new DEVPROPKEY();
            key.fmtid = KnownGuids.BluetoothBatteryProperty;
            key.pid = KnownGuids.BluetoothBatteryPid;

            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(buf, 0);
                uint type;
                uint need;
                bool ok = Native.SetupDiGetDeviceProperty(hdev, ref info, ref key,
                    out type, buf, 1, out need, 0);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == Win32.ERROR_NOT_FOUND)
                    {
                        reason = "该设备没有电池属性（耳机可能未连接或不上报电量）";
                    }
                    else
                    {
                        reason = "读取蓝牙电量属性失败（Win32 错误码 " + err + "）";
                    }
                    return;
                }

                hasProperty = true;
                int value = Marshal.ReadByte(buf) & 0xFF;
                if (value == 0)
                {
                    battery = 0;
                    reason = "属性存在但值为 0（通常表示未连接或未上报）";
                    return;
                }
                battery = Math.Min(100, value);
                reason = "来自蓝牙 HFP 电量上报";
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }
}
