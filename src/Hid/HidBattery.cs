// ---------------------------------------------------------------------------
//  HidBattery.cs —— 对外门面
//
//  把 Native / HidDevice / Protocols / Darkrim / BluetoothBattery 聚合起来，
//  对外只暴露契约（Models.cs 里的 HidApi）要求的那几个方法，
//  并在静态构造函数里把 HidApi 的委托指向自己的实现 ——
//  这样界面层只调 HidApi.* 也能正常工作。
//
//  约定：
//    * Level = -1 表示未知；
//    * Source / Detail 全是中文，界面可以直接显示；
//    * 任何方法都不向外抛异常（设备休眠 / 无回复 → Ok=false + 中文说明）。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace BatterySentinel.Hid
{
    /// <summary>HID 电量读取门面。</summary>
    public static class HidBattery
    {
        static HidBattery()
        {
            // 让只认 HidApi 的界面代码也能用到真正的实现
            HidApi.Enumerate = Enumerate;
            HidApi.Read = Read;
            HidApi.ValueCaps = ValueCaps;
            HidApi.BluetoothDevices = BluetoothDevices;
        }

        // ===================================================================
        //  初始化
        // ===================================================================

        /// <summary>
        /// 确保 HidApi.* 的委托已经指向本类（空操作，仅用于触发静态构造函数）。
        /// 界面层如果只调 HidApi.*、从不触碰 HidBattery，请在启动时先调用一次此方法
        /// （或者调用一次 HidBattery.Enumerate()，效果一样）。
        /// </summary>
        public static void Initialize()
        {
            // 静态构造函数已经在访问类型时执行完毕，这里什么都不用做
        }

        // ===================================================================
        //  枚举
        // ===================================================================

        /// <summary>枚举系统中所有 HID 顶层集合。</summary>
        public static List<HidInterface> Enumerate()
        {
            try
            {
                return HidDevice.EnumerateAll();
            }
            catch
            {
                return new List<HidInterface>();
            }
        }

        // ===================================================================
        //  读取（多策略）
        // ===================================================================

        /// <summary>
        /// 按顺序尝试所有策略，返回最佳结果。
        /// allowInputMonitor=true 时最后会监听 ≤1.5 秒 Input Report 找电量字段。
        /// </summary>
        public static BatteryReading Read(HidInterface iface, bool allowInputMonitor)
        {
            if (iface == null)
                return BatteryReading.Fail("HID 电量", "接口为空（没有传入 HID 接口）");

            var steps = new List<KeyValuePair<string, Func<BatteryReading>>>();
            steps.Add(new KeyValuePair<string, Func<BatteryReading>>(
                "标准 HID 电量字段", delegate { return Protocols.ReadStandardUsage(iface); }));
            steps.Add(new KeyValuePair<string, Func<BatteryReading>>(
                "DARKRIM 私有协议", delegate { return Protocols.ReadDarkrim(iface); }));
            steps.Add(new KeyValuePair<string, Func<BatteryReading>>(
                "厂商协议", delegate { return Protocols.TryVendor(iface); }));
            steps.Add(new KeyValuePair<string, Func<BatteryReading>>(
                "罗技 HID++", delegate { return Protocols.ReadLogitech(iface); }));
            if (allowInputMonitor)
            {
                steps.Add(new KeyValuePair<string, Func<BatteryReading>>(
                    "输入报告监听", delegate { return Protocols.MonitorInput(iface, 1500); }));
            }

            BatteryReading firstFailure = null;

            foreach (KeyValuePair<string, Func<BatteryReading>> step in steps)
            {
                BatteryReading result;
                try
                {
                    result = step.Value();
                }
                catch (Exception ex)
                {
                    // 单个策略异常不影响其它策略
                    result = BatteryReading.Fail(step.Key, "异常：" + ex.Message);
                }

                if (result == null) continue;          // 该策略不适用于这个接口
                if (result.Ok) return result;
                if (firstFailure == null && !string.IsNullOrEmpty(result.Detail))
                    firstFailure = result;
            }

            if (firstFailure != null) return firstFailure;

            return BatteryReading.Fail("HID 电量",
                "该接口没有可用的电量读取方式（它不是外设的数据接口，或者设备没有声明电量字段）");
        }

        // ===================================================================
        //  能力表
        // ===================================================================

        /// <summary>0=Input，1=Output，2=Feature。</summary>
        public static List<ValueCap> ValueCaps(HidInterface iface, int reportType)
        {
            try
            {
                return Protocols.ValueCaps(iface, reportType);
            }
            catch
            {
                return new List<ValueCap>();
            }
        }

        // ===================================================================
        //  蓝牙
        // ===================================================================

        /// <summary>蓝牙设备电量列表（只返回有电量属性或 Hands-Free 的节点）。</summary>
        public static List<BtDevice> BluetoothDevices()
        {
            try
            {
                return BluetoothBattery.Devices();
            }
            catch
            {
                return new List<BtDevice>();
            }
        }

        // ===================================================================
        //  诊断面板
        // ===================================================================

        /// <summary>每个 report id 读一条 Feature Report（失败的跳过）。</summary>
        public static List<byte[]> DumpFeatureReports(HidInterface iface)
        {
            try
            {
                return Protocols.DumpFeatureReports(iface);
            }
            catch
            {
                return new List<byte[]>();
            }
        }

        /// <summary>原样发一条报文并收集回复（诊断 / 手动标定用）。</summary>
        public static List<byte[]> SendRaw(HidInterface iface, byte[] data, bool feature,
            int waitMs, int length)
        {
            try
            {
                return Protocols.SendRaw(iface, data, feature, waitMs, length);
            }
            catch
            {
                return new List<byte[]>();
            }
        }

        /// <summary>试出该接口接受哪些 Output Report ID。</summary>
        public static List<int> ProbeReportIds(HidInterface iface)
        {
            try
            {
                return Protocols.ProbeReportIds(iface);
            }
            catch
            {
                return new List<int>();
            }
        }

        // ===================================================================
        //  附加便利方法（界面分组用，不属于契约）
        // ===================================================================

        /// <summary>把枚举结果按物理设备分组。</summary>
        public static Dictionary<string, List<HidInterface>> Group(IList<HidInterface> interfaces)
        {
            var groups = new Dictionary<string, List<HidInterface>>(StringComparer.OrdinalIgnoreCase);
            if (interfaces == null) return groups;
            foreach (HidInterface item in interfaces)
            {
                if (item == null) continue;
                string key = string.IsNullOrEmpty(item.Group) ? item.Path : item.Group;
                List<HidInterface> bucket;
                if (!groups.TryGetValue(key, out bucket))
                {
                    bucket = new List<HidInterface>();
                    groups[key] = bucket;
                }
                bucket.Add(item);
            }
            return groups;
        }

        /// <summary>一个物理设备组的显示名。</summary>
        public static string DeviceName(IList<HidInterface> groupInterfaces)
        {
            return HidDevice.PickDeviceName(groupInterfaces);
        }
    }
}
