using System;
using System.Collections.Generic;
using BatterySentinel.Hid;

namespace BatterySentinel.Core
{
    public enum AlertLevel
    {
        None = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>界面用的设备状态（快照）。</summary>
    public sealed class DeviceState
    {
        public string Key = "";
        public string Name = "";
        public string BaseName = "";
        public string VidPid = "";
        public string KindText = "无线外设";
        public string ConnectionText = "";
        public bool Present;
        public bool Supported = true;
        public int Level = -1;
        public BatteryStatus Status = BatteryStatus.Unknown;
        public int VoltageMv = -1;
        public string Source = "";
        public string Detail = "";
        public string ModelName = "";
        public string Firmware = "";
        public DateTime UpdatedAt = DateTime.MinValue;
        public List<int> History = new List<int>();
        public bool IsBluetooth;
        public string InterfacePath = "";
        public int SortOrder = 5;
        public string ErrorText = "";
        public string RawHex = "";
        public bool Stale;   // 本轮没读到，显示的是上一次的数据
        public bool Charging { get { return Status == BatteryStatus.Charging || Status == BatteryStatus.Full; } }

        public bool HasLevel { get { return Present && Level >= 0; } }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case BatteryStatus.Charging: return "充电中";
                    case BatteryStatus.Full: return "已充满";
                    case BatteryStatus.Discharging: return "放电中";
                    default: return "";
                }
            }
        }

        public string LevelText { get { return Level >= 0 ? Level + "%" : "--"; } }

        public string UpdatedText
        {
            get { return UpdatedAt == DateTime.MinValue ? "" : UpdatedAt.ToString("HH:mm:ss"); }
        }

        public DeviceState Clone()
        {
            var c = (DeviceState)MemberwiseClone();
            c.History = new List<int>(History);
            return c;
        }
    }

    /// <summary>一次低电量提醒请求。</summary>
    public sealed class AlertNotice
    {
        public DeviceState Device;
        public AlertLevel Level;
        public int Threshold;
        public string Title = "";
        public string Message = "";
    }
}
