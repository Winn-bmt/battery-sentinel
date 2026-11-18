// ---------------------------------------------------------------------------
//  BluetoothRadio.cs —— 查询“当前真正连接着”的蓝牙设备
//
//  背景：BTHENUM 下的 Hands-Free 节点即使耳机已断开也会留在设备树里，
//  并且保留上次连接时写入的 HFP 电量属性。只看 PnP 属性会把“配对过但现在没开”
//  的设备也当成在线设备显示出来。
//
//  这里用 Windows 自带的蓝牙 API（bthprops.cpl 的 BluetoothFindFirstRadio /
//  BluetoothFindFirstDevice，参数只要求 fReturnConnected）拿到当前连接集合，
//  再按设备地址（实例 ID 里的 12 位十六进制）或名称去匹配。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BatterySentinel.Hid
{
    internal static class BluetoothRadio
    {
        private const int BLUETOOTH_MAX_NAME_SIZE = 248;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_FIND_RADIO_PARAMS
        {
            public int dwSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public int fConnected;
            public int fRemembered;
            public int fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            public int fReturnAuthenticated;
            public int fReturnRemembered;
            public int fReturnUnknown;
            public int fReturnConnected;
            public int fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr hRadio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr hRadio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindRadioClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS p, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public sealed class ConnectedDevice
        {
            public string Name = "";
            public ulong Address;
            public string AddressHex { get { return Address.ToString("X12"); } }
        }

        /// <summary>当前处于连接状态的蓝牙设备（没有蓝牙硬件时返回空列表）。</summary>
        public static List<ConnectedDevice> Connected()
        {
            var result = new List<ConnectedDevice>();
            try
            {
                var radioParams = new BLUETOOTH_FIND_RADIO_PARAMS
                {
                    dwSize = Marshal.SizeOf(typeof(BLUETOOTH_FIND_RADIO_PARAMS))
                };
                IntPtr radio;
                IntPtr findRadio = BluetoothFindFirstRadio(ref radioParams, out radio);
                if (findRadio == IntPtr.Zero) return result;

                try
                {
                    bool moreRadio = true;
                    while (moreRadio)
                    {
                        try
                        {
                            if (radio != IntPtr.Zero) CollectDevices(radio, result);
                        }
                        finally
                        {
                            if (radio != IntPtr.Zero) CloseHandle(radio);
                        }
                        moreRadio = BluetoothFindNextRadio(findRadio, out radio);
                    }
                }
                finally
                {
                    BluetoothFindRadioClose(findRadio);
                }
            }
            catch
            {
                // 没有蓝牙无线电 / API 不可用时静默返回空列表
            }
            return result;
        }

        private static void CollectDevices(IntPtr radio, List<ConnectedDevice> result)
        {
            var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                fReturnAuthenticated = 0,
                fReturnRemembered = 0,
                fReturnUnknown = 0,
                fReturnConnected = 1,
                fIssueInquiry = 0,
                cTimeoutMultiplier = 1,
                hRadio = radio
            };
            var info = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO))
            };

            IntPtr find = BluetoothFindFirstDevice(ref search, ref info);
            if (find == IntPtr.Zero) return;
            try
            {
                do
                {
                    var item = new ConnectedDevice();
                    item.Name = (info.szName ?? "").Trim();
                    item.Address = info.Address;
                    result.Add(item);
                    info = new BLUETOOTH_DEVICE_INFO
                    {
                        dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO))
                    };
                }
                while (BluetoothFindNextDevice(find, ref info));
            }
            finally
            {
                BluetoothFindDeviceClose(find);
            }
        }

        /// <summary>从 BTHENUM 实例 ID 里提取设备地址（12 位十六进制）。</summary>
        public static string AddressFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            Match m = Regex.Match(instanceId, @"[&_]([0-9A-Fa-f]{12})(?:_|$)", RegexOptions.RightToLeft);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            // 兜底：取最后一段 12 位十六进制（避开 GUID 里的 00805F9B34FB）
            MatchCollection all = Regex.Matches(instanceId, "[0-9A-Fa-f]{12}");
            if (all.Count > 0) return all[all.Count - 1].Value.ToUpperInvariant();
            return "";
        }
    }
}
