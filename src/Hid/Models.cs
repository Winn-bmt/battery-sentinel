using System;
using System.Collections.Generic;

namespace BatterySentinel.Hid
{
    /// <summary>电量状态。</summary>
    public enum BatteryStatus
    {
        Unknown = 0,
        Discharging = 1,
        Charging = 2,
        Full = 3
    }

    /// <summary>一次电量读取的结果。</summary>
    public sealed class BatteryReading
    {
        public bool Ok;
        public int Level = -1;          // 0..100，-1 表示未知
        public int RawLevel = -1;
        public BatteryStatus Status = BatteryStatus.Unknown;
        public int VoltageMv = -1;
        public string Source = "";      // 命中的策略（中文，直接显示）
        public string Detail = "";      // 说明文字
        public string ModelName = "";   // 从设备里读到的型号（可选）
        public string Firmware = "";    // 固件版本（可选）
        public byte[] Raw;

        public static BatteryReading Fail(string source, string detail)
        {
            return new BatteryReading { Ok = false, Source = source ?? "", Detail = detail ?? "" };
        }
    }

    /// <summary>一个 HID 顶层集合（setupapi 中的一个设备接口）。</summary>
    public sealed class HidInterface
    {
        public string Path = "";
        public string Group = "";       // 同一物理设备的接口归到一组
        public string Product = "";
        public string Manufacturer = "";
        public string Serial = "";
        public int Vid;
        public int Pid;
        public int Version;
        public int UsagePage;
        public int Usage;
        public int InputLen;
        public int OutputLen;
        public int FeatureLen;

        public bool Reserved;           // 系统独占的顶层集合（鼠标/键盘/笔/触摸）
        public bool IsVendorPage { get { return UsagePage >= 0xFF00; } }
        public string VidPid { get { return Vid.ToString("X4") + ":" + Pid.ToString("X4"); } }
        public string UsageText { get { return "0x" + UsagePage.ToString("X4") + ":0x" + Usage.ToString("X4"); } }
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Product)) return Product;
                if (!string.IsNullOrEmpty(Manufacturer)) return Manufacturer + " HID 设备";
                return "HID 设备";
            }
        }
    }

    /// <summary>HID 能力表。</summary>
    public sealed class HidCaps
    {
        public int Usage;
        public int UsagePage;
        public int InputReportByteLength;
        public int OutputReportByteLength;
        public int FeatureReportByteLength;
        public int NumberFeatureValueCaps;
        public int NumberInputValueCaps;
    }

    /// <summary>某个 report 字段的能力（对应 HIDP_VALUE_CAPS，只保留用得到的字段）。</summary>
    public sealed class ValueCap
    {
        public int UsagePage;
        public int Usage;
        public int ReportId;
        public int BitSize;
        public int ReportCount;
        public int LogicalMin;
        public int LogicalMax;
        public int LinkCollection;
        public bool IsRange;
    }

    /// <summary>可持久化的自定义协议（界面里的“手动标定”/用户协议表）。</summary>
    public sealed class ProtocolSpec
    {
        public string Name = "自定义协议";
        public int Vid;
        public int UsagePage;
        public int ReportId;
        public byte[] Payload = new byte[0];
        public int Length = 64;
        public int LevelOffset;
        public int WaitMs = 150;
        public int Scale = 1;
        public int LevelMax = -1;
        public int ChargingOffset = -1;
        public bool ChargingNonZero = true;
        public int VoltageOffset = -1;
        public string Mode = "";        // "feature" / "output" / 空=自动
        public string Notes = "";
        public bool Enabled = true;
    }

    /// <summary>蓝牙（HFP 电量属性）设备。</summary>
    public sealed class BtDevice
    {
        public string Name = "";
        public string InstanceId = "";
        public int Battery = -1;
        public bool HasProperty;
        public string Reason = "";
        public bool IsHandsFree
        {
            get
            {
                string low = (Name ?? "").ToLowerInvariant();
                return low.Contains("hands-free") || Name.Contains("免提") || low.Contains("handsfree");
            }
        }
    }

    /// <summary>HID 底层公开接口（由 HidBattery.cs 实现）。</summary>
    public static class HidApi
    {
        /// <summary>枚举系统中所有 HID 顶层集合。</summary>
        public static Func<List<HidInterface>> Enumerate = DefaultEnumerate;

        /// <summary>按内置 + 用户协议读取一个接口的电量。</summary>
        public static Func<HidInterface, bool, BatteryReading> Read = DefaultRead;

        /// <summary>输入 / Feature 报告的字段能力表（reportType: 0=Input, 1=Output, 2=Feature）。</summary>
        public static Func<HidInterface, int, List<ValueCap>> ValueCaps = DefaultValueCaps;

        /// <summary>用户自定义协议表（由应用启动时从 JSON 载入）。</summary>
        public static List<ProtocolSpec> UserProtocols = new List<ProtocolSpec>();

        /// <summary>蓝牙设备电量列表。</summary>
        public static Func<List<BtDevice>> BluetoothDevices = DefaultBt;

        // 下面三个仅作占位，真正实现由 HidBattery 类提供（见 HidBattery.cs）。
        private static List<HidInterface> DefaultEnumerate() { return new List<HidInterface>(); }
        private static BatteryReading DefaultRead(HidInterface i, bool monitor) { return BatteryReading.Fail("", "HID 层未初始化"); }
        private static List<ValueCap> DefaultValueCaps(HidInterface i, int rt) { return new List<ValueCap>(); }
        private static List<BtDevice> DefaultBt() { return new List<BtDevice>(); }
    }
}
