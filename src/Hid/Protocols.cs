// ---------------------------------------------------------------------------
//  Protocols.cs —— 内置协议表 + 多策略电量读取
//
//  策略（按顺序尝试，第一条成功的就是结论）：
//    A. 标准 HID 电量字段（Feature Report + HidP_GetUsageValue）
//    A2. DARKRIM（杰理方案）2.4G 耳机 dongle 的私有 TLV 协议（见 Darkrim.cs）
//    B. 内置厂商协议表（VXE/ATK、1EA7 接收器、Endgame Gear、VAXEE、Nordic ...）
//       + HidApi.UserProtocols 里的用户自定义协议（VID + UsagePage 相同才参与）
//    C. 罗技 HID++ 2.0（Feature 0x1000 / 0x1004 / 0x1001）
//    E. 监听 Input Report 里上报的电量字段（只在 allowInputMonitor=true 时做，≤1.5 秒）
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BatterySentinel.Hid
{
    /// <summary>格式化小工具。</summary>
    public static class HidText
    {
        /// <summary>把字节数组格式化成 "08 04 00 ... CE"。</summary>
        public static string Hex(byte[] data)
        {
            if (data == null || data.Length == 0) return "(空)";
            var sb = new System.Text.StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>截断到最多 maxBytes 字节再格式化，避免界面被长报文撑爆。</summary>
        public static string HexShort(byte[] data, int maxBytes)
        {
            if (data == null) return "(空)";
            if (maxBytes <= 0 || data.Length <= maxBytes) return Hex(data);
            var part = new byte[maxBytes];
            Array.Copy(data, part, maxBytes);
            return Hex(part) + " …(共 " + data.Length + " 字节)";
        }

        /// <summary>电量状态的默认中文描述（Detail 里用）。</summary>
        public static string StatusText(BatteryStatus status)
        {
            switch (status)
            {
                case BatteryStatus.Discharging: return "放电中";
                case BatteryStatus.Charging: return "充电中";
                case BatteryStatus.Full: return "已充满";
                default: return "未知";
            }
        }

        public static int ClampPercent(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }
    }

    /// <summary>
    /// 一条厂商 / 用户协议。
    /// 比契约里的 ProtocolSpec 多出「回复校验（expect）」「重试次数」「特殊模式」。
    /// </summary>
    internal sealed class VendorProtocol
    {
        public string Name = "自定义协议";
        public int Vid;
        public int UsagePage;
        public int ReportId;
        public byte[] Payload = new byte[0];
        public int Length = 64;          // 报文总长度（含 Report ID）
        public int LevelOffset;          // 电量字节下标（从报文第 0 字节算）
        public int WaitMs = 120;
        public int Scale = 1;
        public int LevelMax = -1;        // >0 时超过即视为无效
        public int ChargingOffset = -1;
        public bool ChargingNonZero = true;
        public int VoltageOffset = -1;   // 小端 16 位
        public string Mode = "";         // "feature" / "output" / ""=自动 / "vxe-55" / "1ea7-b5"
        public int Retries = 2;
        public int[] ExpectOffsets = new int[0];
        public byte[] ExpectValues = new byte[0];
        public string Notes = "";

        public bool HasExpect
        {
            get { return ExpectOffsets != null && ExpectOffsets.Length > 0; }
        }

        public string EffectiveMode(HidInterface iface)
        {
            if (Mode.Length > 0) return Mode;
            return iface != null && iface.FeatureLen > 0 ? "feature" : "output";
        }

        public static VendorProtocol FromSpec(ProtocolSpec spec)
        {
            var vp = new VendorProtocol();
            vp.Name = string.IsNullOrEmpty(spec.Name) ? "自定义协议" : spec.Name;
            vp.Vid = spec.Vid;
            vp.UsagePage = spec.UsagePage;
            vp.ReportId = spec.ReportId;
            vp.Payload = spec.Payload ?? new byte[0];
            vp.Length = spec.Length > 0 ? spec.Length : 64;
            vp.LevelOffset = spec.LevelOffset;
            vp.WaitMs = spec.WaitMs > 0 ? spec.WaitMs : 150;
            vp.Scale = spec.Scale > 0 ? spec.Scale : 1;
            vp.LevelMax = spec.LevelMax;
            vp.ChargingOffset = spec.ChargingOffset;
            vp.ChargingNonZero = spec.ChargingNonZero;
            vp.VoltageOffset = spec.VoltageOffset;
            vp.Mode = spec.Mode ?? "";
            vp.Retries = 2;
            vp.Notes = spec.Notes ?? "";
            return vp;
        }
    }

    /// <summary>多策略电量读取。</summary>
    internal static class Protocols
    {
        // ---- 标准 HID 电量字段用到的 usage（UsagePage, Usage）----
        private static readonly int[][] BatteryUsages = new int[][]
        {
            new int[] { 0x0006, 0x0020 },   // Battery Strength（通用设备控制页）
            new int[] { 0x000D, 0x003B },   // Battery Strength（数字化仪页，触控笔常见）
            new int[] { 0x0085, 0x0001 },   // Absolute State of Charge（电池系统页）
            new int[] { 0x0085, 0x0066 },   // Relative State of Charge
            new int[] { 0x0085, 0x0065 },   // Battery Level
        };

        private static string BatteryUsageName(int usagePage, int usage)
        {
            if (usagePage == 0x0006 && usage == 0x0020) return "Battery Strength（通用设备控制页）";
            if (usagePage == 0x000D && usage == 0x003B) return "Battery Strength（数字化仪页）";
            if (usagePage == 0x0085 && usage == 0x0001) return "Absolute State of Charge（电池系统页）";
            if (usagePage == 0x0085 && usage == 0x0066) return "Relative State of Charge（电池系统页）";
            if (usagePage == 0x0085 && usage == 0x0065) return "Battery Level（电池系统页）";
            return null;
        }

        private static bool IsBatteryUsage(int usagePage, int usage)
        {
            foreach (int[] pair in BatteryUsages)
            {
                if (pair[0] == usagePage && pair[1] == usage) return true;
            }
            return false;
        }

        // ===================================================================
        //  内置协议表
        // ===================================================================

        public static readonly List<VendorProtocol> Builtin = BuildBuiltin();

        private static VendorProtocol VxeSpec(int vid, string name)
        {
            var vp = new VendorProtocol();
            vp.Name = name;
            vp.Vid = vid;
            vp.UsagePage = 0xFF02;      // usage page 0xFF02 / usage 0x0002 / 17 字节
            vp.ReportId = 0x08;
            vp.Payload = new byte[0];
            vp.Length = 17;
            vp.LevelOffset = 6;
            vp.WaitMs = 350;
            vp.Mode = "vxe-55";
            vp.Retries = 3;
            vp.Notes = "17 字节报文：[0]=0x08 命令，[16]=(0x55-sum(buf[0..15]))&0xFF；" +
                       "命令 0x04 回复 [6]=电量% [7]=1 充电/有线 [8..9]=电压 mV 大端";
            return vp;
        }

        private static List<VendorProtocol> BuildBuiltin()
        {
            var list = new List<VendorProtocol>();

            // ---- 1) VXE / ATK / Pulsar 鼠标（Nordic 方案，17 字节，校验 0x55）----
            list.Add(VxeSpec(0x3554, "VXE / ATK 无线鼠标（0xFF02 私有协议）"));
            list.Add(VxeSpec(0x373B, "ATK / VXE / VGN 无线鼠标（0xFF02 私有协议）"));

            // ---- 2) 1EA7:0068 键鼠接收器（8 字节，Report ID 0xB5）----
            var dongle = new VendorProtocol();
            dongle.Name = "无线键鼠接收器（1EA7 私有协议）";
            dongle.Vid = 0x1EA7;
            dongle.UsagePage = 0xFFB5;  // usage 0x0001 / 8 字节
            dongle.ReportId = 0xB5;
            dongle.Payload = new byte[] { 0x01 };
            dongle.Length = 8;
            dongle.LevelOffset = 3;
            dongle.WaitMs = 300;
            dongle.Mode = "1ea7-b5";
            dongle.Retries = 2;
            dongle.Notes = "写 B5 01 00 00 00 00 00 00；回复 B5 01 01 38 00 00 00 00 → " +
                           "[3]=电量%，[2]=1 表示设备在线";
            list.Add(dongle);

            // ---- 3) ATK / VXE 协议 2：64 字节 Output，命令 0x72 ----
            var atk2 = new VendorProtocol();
            atk2.Name = "ATK / VXE 无线鼠标（协议 2）";
            atk2.Vid = 0x373B;
            atk2.UsagePage = 0xFF05;
            atk2.ReportId = 0x08;
            atk2.Payload = new byte[] { 0x7D, 0x72, 0x02, 0x00, 0x01, 0x07, 0x01 };
            atk2.Length = 64;
            atk2.LevelOffset = 7;
            atk2.WaitMs = 100;
            atk2.LevelMax = 100;
            atk2.Mode = "output";
            atk2.Retries = 3;
            atk2.ExpectOffsets = new int[] { 1, 5 };
            atk2.ExpectValues = new byte[] { 0x72, 0x07 };
            atk2.Notes = "回复 [1]=0x72 且 [5]=0x07 为有效标志，电量在 [7]";
            list.Add(atk2);

            // ---- 4) Endgame Gear（XM2w / OP1W / 无线接收器）----
            var endgame = new VendorProtocol();
            endgame.Name = "Endgame Gear（XM2w / OP1W / 无线接收器）";
            endgame.Vid = 0x3367;
            endgame.UsagePage = 0xFF01;
            endgame.ReportId = 0xA1;
            endgame.Payload = new byte[] { 0xB4 };
            endgame.Length = 64;
            endgame.LevelOffset = 16;
            endgame.VoltageOffset = 17;
            endgame.WaitMs = 350;
            endgame.Mode = "feature";
            endgame.Retries = 3;
            endgame.ExpectOffsets = new int[] { 1 };
            endgame.ExpectValues = new byte[] { 0x01 };
            endgame.Notes = "[1] 状态：0x01 有效 / 0x03 忙 / 0x07 不支持 / 0x08 睡眠；" +
                            "第一次读常只是唤醒，需连读两次";
            list.Add(endgame);

            // ---- 5) VAXEE / ZYGEN（含 4K 接收器）----
            var vaxee = new VendorProtocol();
            vaxee.Name = "VAXEE / ZYGEN（含 4K 接收器）";
            vaxee.Vid = 0x3057;
            vaxee.UsagePage = 0xFF05;
            vaxee.ReportId = 0x0E;
            vaxee.Payload = new byte[] { 0xA5, 0x0B, 0x01, 0x01 };
            vaxee.Length = 64;
            vaxee.LevelOffset = 5;
            vaxee.WaitMs = 120;
            vaxee.Scale = 5;
            vaxee.LevelMax = 20;
            vaxee.Mode = "feature";
            vaxee.Notes = "以 5% 为步长上报（原始值 0..20）";
            list.Add(vaxee);

            // ---- 6) Nordic nRF 方案 2.4G 接收器 ----
            var nordic = new VendorProtocol();
            nordic.Name = "Nordic nRF 方案 2.4G 接收器";
            nordic.Vid = 0x1915;
            nordic.UsagePage = 0xFFA0;
            nordic.ReportId = 0x05;
            nordic.Payload = new byte[] { 0x15, 0x00, 0x00, 0x01 };
            nordic.Length = 32;
            nordic.LevelOffset = 9;
            nordic.WaitMs = 90;
            nordic.Mode = "feature";
            nordic.Notes = "请求命令 0x15 读取电量";
            list.Add(nordic);

            return list;
        }

        // ===================================================================
        //  策略 A：标准 HID 电量字段
        // ===================================================================

        /// <summary>没有声明电量字段时返回 null（表示“不适用”）。</summary>
        public static BatteryReading ReadStandardUsage(HidInterface iface)
        {
            if (iface == null || iface.FeatureLen <= 0) return null;

            const string source = "标准 HID 电量字段";
            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, false, out err);
                if (dev == null)
                    return BatteryReading.Fail(source, "无法打开接口：" + err);

                HIDP_VALUE_CAPS[] caps = dev.HidValueCaps(Win32.HidP_Feature);
                string firstError = null;

                foreach (HIDP_VALUE_CAPS cap in caps)
                {
                    string usageName = BatteryUsageName(cap.UsagePage, cap.UsageMin);
                    if (usageName == null) continue;

                    byte[] report;
                    try
                    {
                        report = dev.GetFeatureReport(cap.ReportID,
                            Math.Max(iface.FeatureLen, 2));
                    }
                    catch (HidException ex)
                    {
                        if (firstError == null) firstError = ex.Message;
                        continue;
                    }

                    int? value = dev.UsageValue(Win32.HidP_Feature, report, cap);
                    if (value == null) continue;

                    int level = ScaleToPercent(value.Value, cap.LogicalMax);
                    var reading = new BatteryReading();
                    reading.Ok = true;
                    reading.Level = level;
                    reading.RawLevel = value.Value;
                    reading.Status = BatteryStatus.Unknown;
                    reading.Source = source + "（" + usageName + "）";
                    reading.Detail = string.Format(
                        "UsagePage=0x{0:X4} Usage=0x{1:X4} ReportID=0x{2:X2} 原始值={3} 上限={4}",
                        cap.UsagePage, cap.UsageMin, cap.ReportID, value.Value, cap.LogicalMax);
                    reading.Raw = report;
                    return reading;
                }

                if (firstError != null)
                {
                    return BatteryReading.Fail(source,
                        "接口声明了电量字段，但读取 Feature Report 失败：" + firstError);
                }
                return null;
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail(source, "读取失败：" + ex.Message);
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        // ===================================================================
        //  策略 A2：DARKRIM（杰理方案）私有 TLV 协议
        // ===================================================================

        public static BatteryReading ReadDarkrim(HidInterface iface)
        {
            if (!Darkrim.Matches(iface)) return null;

            const string source = "DARKRIM 私有协议（TLV over HID）";
            DarkrimInfo info = Darkrim.ReadInfo(iface, 400);
            if (info.Battery < 0)
            {
                string detail = info.Detail;
                if (string.IsNullOrEmpty(detail)) detail = "没有取到电量字段";
                return BatteryReading.Fail("DARKRIM 私有协议（TLV）", detail);
            }

            var reading = new BatteryReading();
            reading.Ok = true;
            reading.Level = HidText.ClampPercent(info.Battery);
            reading.RawLevel = info.BatteryRaw;
            reading.Status = info.Battery >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging;
            reading.Source = source;
            reading.ModelName = info.Name;
            reading.Firmware = info.HeadsetFirmware;
            reading.Detail = info.Detail + "；电量 TLV(0x01) 原始值 0x" +
                             info.BatteryRaw.ToString("X2");
            return reading;
        }

        // ===================================================================
        //  策略 B：厂商协议（内置 + 用户自定义）
        // ===================================================================

        /// <summary>没有匹配到任何协议时返回 null。</summary>
        public static BatteryReading TryVendor(HidInterface iface)
        {
            if (iface == null) return null;

            var specs = new List<VendorProtocol>();
            foreach (VendorProtocol builtin in Builtin)
            {
                if (builtin.Vid == iface.Vid && builtin.UsagePage == iface.UsagePage)
                    specs.Add(builtin);
            }

            List<ProtocolSpec> user = HidApi.UserProtocols;
            if (user != null)
            {
                foreach (ProtocolSpec spec in user)
                {
                    if (spec == null || !spec.Enabled) continue;
                    if (spec.Vid != iface.Vid || spec.UsagePage != iface.UsagePage) continue;
                    specs.Add(VendorProtocol.FromSpec(spec));
                }
            }

            if (specs.Count == 0) return null;

            BatteryReading last = null;
            foreach (VendorProtocol spec in specs)
            {
                BatteryReading result;
                if (spec.Mode == "vxe-55") result = ReadVxeNordic(iface, spec);
                else if (spec.Mode == "1ea7-b5") result = ReadDongle1EA7(iface, spec);
                else result = RunGeneric(iface, spec);

                if (result != null && result.Ok) return result;
                if (last == null && result != null) last = result;
            }
            return last;
        }

        private static BatteryReading RunGeneric(HidInterface iface, VendorProtocol spec)
        {
            string kind = spec.EffectiveMode(iface);
            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, true, out err);
                if (dev == null)
                    return BatteryReading.Fail(spec.Name, "无法打开接口：" + err);

                string requestError;
                byte[] report = RequestAndReply(dev, spec, kind, out requestError);

                if (report == null || report.Length == 0)
                {
                    if (!string.IsNullOrEmpty(requestError))
                        return BatteryReading.Fail(spec.Name, "收发失败：" + requestError);
                    return BatteryReading.Fail(spec.Name,
                        "设备没有回复（可能正在休眠，动一下鼠标再试）");
                }
                return ParseSpecResponse(spec, report);
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail(spec.Name,
                    "收发失败：" + ex.Message + "（可能是权限被占用，请关闭厂商驱动软件后重试）");
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        /// <summary>
        /// 按协议发请求、等回复、再读回报文。
        /// feature：写 Feature Report 后直接读回同 ID 的 Feature Report。
        /// output ：写 Output Report 后等 Input Report（有 expect 时只接受匹配的回复）。
        /// </summary>
        private static byte[] RequestAndReply(HidDevice dev, VendorProtocol spec,
            string kind, out string error)
        {
            error = "";
            int total = spec.Length > 0 ? spec.Length : 64;

            byte[] body = new byte[total];
            body[0] = (byte)(spec.ReportId & 0xFF);
            if (spec.Payload != null && spec.Payload.Length > 0)
            {
                int copy = Math.Min(spec.Payload.Length, Math.Max(0, total - 1));
                Array.Copy(spec.Payload, 0, body, 1, copy);
            }

            byte[] last = null;
            int retries = Math.Max(1, spec.Retries);
            for (int attempt = 0; attempt < retries; attempt++)
            {
                try
                {
                    if (kind == "feature")
                    {
                        int capLen = 0;
                        try { capLen = dev.GetCaps().FeatureReportByteLength; }
                        catch { }

                        int length = total;
                        if (capLen > length) length = capLen;

                        var request = new byte[length];
                        Array.Copy(body, request, Math.Min(body.Length, request.Length));
                        dev.SetFeatureReport(request);
                        Thread.Sleep(spec.WaitMs);

                        last = dev.GetFeatureReport(spec.ReportId, length);
                        if (!spec.HasExpect || Matches(last, spec)) return last;
                    }
                    else
                    {
                        dev.Drain();
                        dev.WriteOutput(body);
                        Thread.Sleep(spec.WaitMs);

                        var sw = Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 650)
                        {
                            byte[] reply = dev.ReadInput(150, null);
                            if (reply == null || reply.Length == 0) continue;
                            last = reply;
                            if (!spec.HasExpect || Matches(reply, spec)) return reply;
                        }
                    }
                }
                catch (HidException ex)
                {
                    error = ex.Message;
                }
            }
            return last;
        }

        private static bool Matches(byte[] report, VendorProtocol spec)
        {
            if (report == null || report.Length == 0) return false;
            for (int i = 0; i < spec.ExpectOffsets.Length; i++)
            {
                int offset = spec.ExpectOffsets[i];
                byte value = i < spec.ExpectValues.Length ? spec.ExpectValues[i] : (byte)0;
                if (report.Length <= offset || report[offset] != value) return false;
            }
            return true;
        }

        private static BatteryReading ParseSpecResponse(VendorProtocol spec, byte[] report)
        {
            if (report.Length <= spec.LevelOffset)
            {
                return BatteryReading.Fail(spec.Name,
                    "回复长度不足（只有 " + report.Length + " 字节）");
            }

            int raw = report[spec.LevelOffset];
            if (spec.LevelMax > 0 && raw > spec.LevelMax)
            {
                return BatteryReading.Fail(spec.Name,
                    "电量字节 0x" + raw.ToString("X2") + " 超出预期范围（上限 " + spec.LevelMax + "）");
            }

            int level = HidText.ClampPercent(raw * Math.Max(1, spec.Scale));

            var status = BatteryStatus.Unknown;
            if (spec.ChargingOffset >= 0 && report.Length > spec.ChargingOffset)
            {
                bool flag = report[spec.ChargingOffset] != 0;
                if (spec.ChargingNonZero)
                    status = flag ? BatteryStatus.Charging : BatteryStatus.Discharging;
                else
                    status = flag ? BatteryStatus.Discharging : BatteryStatus.Charging;
            }

            int voltage = -1;
            if (spec.VoltageOffset >= 0 && report.Length > spec.VoltageOffset + 1)
            {
                int mv = report[spec.VoltageOffset] |
                         (report[spec.VoltageOffset + 1] << 8);
                if (mv >= 2500 && mv <= 5000) voltage = mv;
            }

            var reading = new BatteryReading();
            reading.Ok = true;
            reading.Level = level;
            reading.RawLevel = raw;
            reading.Status = status;
            reading.VoltageMv = voltage;
            reading.Source = spec.Name;
            reading.Raw = report;

            string detail = string.Format(
                "ReportID=0x{0:X2} 电量字节@{1}=0x{2:X2} ×{3}",
                spec.ReportId, spec.LevelOffset, raw, Math.Max(1, spec.Scale));
            if (voltage > 0) detail += " 电压=" + voltage + " mV";
            if (status != BatteryStatus.Unknown) detail += " 状态=" + HidText.StatusText(status);
            reading.Detail = detail;
            return reading;
        }

        // ===================================================================
        //  内置特例 1：VXE / ATK / Pulsar 鼠标（Nordic 方案，17 字节，校验 0x55）
        // ===================================================================

        private const byte VxeChecksumMagic = 0x55;

        private static byte VxeChecksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < 16 && i < frame.Length; i++) sum += frame[i];
            return (byte)((VxeChecksumMagic - (sum & 0xFF)) & 0xFF);
        }

        private static bool VxeChecksumValid(byte[] frame)
        {
            if (frame == null || frame.Length < 17) return false;
            return frame[16] == VxeChecksum(frame);
        }

        private static byte[] BuildVxeFrame(int command, Action<byte[]> fill)
        {
            var frame = new byte[17];
            frame[0] = 0x08;                    // Report ID
            frame[1] = (byte)(command & 0xFF);
            if (fill != null) fill(frame);
            frame[16] = VxeChecksum(frame);
            return frame;
        }

        private static byte[] VxeExchange(HidDevice dev, byte[] frame, int command, int waitMs)
        {
            dev.Drain();
            dev.WriteOutput(frame);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < waitMs)
            {
                int left = (int)(waitMs - sw.ElapsedMilliseconds);
                int timeout = Math.Max(60, Math.Min(200, left));
                byte[] reply = dev.ReadInput(timeout, null);
                if (reply == null) continue;
                if (reply.Length < 17) continue;
                if (reply[1] != (command & 0xFF)) continue;
                if (!VxeChecksumValid(reply)) continue;   // 校验不过直接丢弃
                return reply;
            }
            return null;
        }

        private static string VxeModelName(int modelId)
        {
            switch (modelId)
            {
                case 0x0220: return "ATK VXE R1 SE+";
                case 0x021F: return "ATK VXE R1 SE+";   // 本机实测的 model_id
                default: return "VXE / ATK 无线鼠标";
            }
        }

        private static string VxeConnectionText(int connection)
        {
            switch (connection)
            {
                case 0: return "1kHz";
                case 1: return "4kHz";
                case 2: return "有线";
                default: return "未知(" + connection + ")";
            }
        }

        private static BatteryReading ReadVxeNordic(HidInterface iface, VendorProtocol spec)
        {
            string source = spec.Name;
            if (iface.InputLen < 17 || iface.OutputLen < 17)
            {
                return BatteryReading.Fail(source,
                    "接口报告长度不是 17 字节，不符合 VXE / ATK 私有协议");
            }

            string err;
            HidDevice dev = HidDevice.Open(iface.Path, true, out err);
            if (dev == null)
                return BatteryReading.Fail(source, "无法打开接口：" + err);

            try
            {
                if (!dev.WriteAccess)
                {
                    return BatteryReading.Fail(source,
                        "接口被系统或厂商软件独占（没有写权限），无法发送查询命令");
                }

                // ---- 命令 0x01：设备信息（型号 / 连接类型）----
                string modelName = "";
                string connection = "";
                try
                {
                    var challenge = new byte[4];
                    using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                    {
                        rng.GetBytes(challenge);
                    }

                    byte[] info = VxeExchange(dev, BuildVxeFrame(0x01, delegate (byte[] f)
                    {
                        f[5] = 0x08;                        // 固定 8
                        Array.Copy(challenge, 0, f, 6, 4);  // [6..9] 4 字节挑战
                    }), 0x01, 450);

                    if (info != null)
                    {
                        int modelId = (info[10] << 8) | info[11];
                        modelName = VxeModelName(modelId);
                        connection = VxeConnectionText(info[12]);
                    }
                }
                catch
                {
                    // 型号读不到不影响电量
                }

                // ---- 命令 0x04：电量（第一次常被当成唤醒，重试几次）----
                byte[] power = null;
                int retries = Math.Max(1, spec.Retries);
                for (int attempt = 0; attempt < retries && power == null; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(60);
                    power = VxeExchange(dev, BuildVxeFrame(0x04, null), 0x04, 450);
                }

                if (power == null)
                {
                    // 用 STATUS(0x03) 区分“接收器没反应”和“接收器正常但鼠标在休眠”，
                    // 好给界面一句能指导用户操作的提示。
                    string failDetail = "设备没有回复（可能正在休眠，动一下鼠标再试）";
                    try
                    {
                        byte[] status = VxeExchange(dev, BuildVxeFrame(0x03, null), 0x03, 300);
                        if (status != null)
                        {
                            if (status[6] == 0)
                            {
                                failDetail = "接收器正常，但鼠标处于休眠或未连接状态" +
                                             "（动一下鼠标或按一下按键再试）";
                            }
                            else
                            {
                                failDetail = "鼠标在线（状态=1），但电量查询没有回复，请再试一次";
                            }
                        }
                    }
                    catch
                    {
                    }
                    return BatteryReading.Fail(source, failDetail);
                }

                int raw = power[6];
                int chargeFlag = power[7];
                int voltage = (power[8] << 8) | power[9];

                if (raw > 100)
                {
                    return BatteryReading.Fail(source,
                        "电量字节 0x" + raw.ToString("X2") + " 超出 0-100 范围（回复=" +
                        HidText.Hex(power) + "）");
                }

                var reading = new BatteryReading();
                reading.Ok = true;
                reading.Level = raw;
                reading.RawLevel = raw;
                reading.VoltageMv = (voltage >= 2500 && voltage <= 5000) ? voltage : -1;
                if (chargeFlag == 1) reading.Status = BatteryStatus.Charging;
                else if (raw >= 100) reading.Status = BatteryStatus.Full;
                else reading.Status = BatteryStatus.Discharging;
                reading.Source = source;
                reading.ModelName = modelName;
                reading.Raw = power;

                string detail = "命令 0x04：电量 " + raw + "%";
                if (reading.VoltageMv > 0) detail += "，电压 " + reading.VoltageMv + " mV";
                if (connection.Length > 0) detail += "，连接 " + connection;
                if (chargeFlag == 1) detail += "，正在充电 / 有线连接";
                if (modelName.Length > 0) detail += "，型号 " + modelName;
                reading.Detail = detail;
                return reading;
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail(source, "读取失败：" + ex.Message);
            }
            finally
            {
                dev.Dispose();
            }
        }

        // ===================================================================
        //  内置特例 2：1EA7:0068 键鼠接收器（8 字节，Report ID 0xB5）
        // ===================================================================

        private static BatteryReading ReadDongle1EA7(HidInterface iface, VendorProtocol spec)
        {
            string source = spec.Name;
            if (iface.InputLen < 4 || iface.OutputLen < 8)
            {
                return BatteryReading.Fail(source,
                    "接口报告长度不符（需要 8 字节 Input / Output）");
            }

            string err;
            HidDevice dev = HidDevice.Open(iface.Path, true, out err);
            if (dev == null)
                return BatteryReading.Fail(source, "无法打开接口：" + err);

            try
            {
                if (!dev.WriteAccess)
                {
                    return BatteryReading.Fail(source,
                        "接口被系统或厂商软件独占（没有写权限），无法发送查询命令");
                }

                var request = new byte[8];
                request[0] = 0xB5;
                request[1] = 0x01;

                byte[] reply = null;
                int retries = Math.Max(1, spec.Retries);
                for (int attempt = 0; attempt < retries && reply == null; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(80);
                    dev.Drain();
                    dev.WriteOutput(request);
                    Thread.Sleep(spec.WaitMs);   // 实测要等 ~300ms 才有回复

                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 500)
                    {
                        byte[] r = dev.ReadInput(150, null);
                        if (r == null) continue;
                        if (r.Length >= 4 && r[0] == 0xB5 && r[1] == 0x01)
                        {
                            reply = r;
                            break;
                        }
                    }
                }

                if (reply == null)
                {
                    return BatteryReading.Fail(source,
                        "接收器没有回复（可能正在休眠，敲一下键盘或动一下鼠标再试）");
                }

                int raw = reply[3];
                bool online = reply[2] == 1;
                if (!online)
                {
                    return BatteryReading.Fail(source,
                        "接收器回复了，但设备不在线（键盘 / 鼠标可能已关机或休眠）");
                }
                if (raw > 100)
                {
                    return BatteryReading.Fail(source,
                        "电量字节 0x" + raw.ToString("X2") + " 超出 0-100 范围（回复=" +
                        HidText.Hex(reply) + "）");
                }

                var reading = new BatteryReading();
                reading.Ok = true;
                reading.Level = raw;
                reading.RawLevel = raw;
                reading.Status = raw >= 100 ? BatteryStatus.Full : BatteryStatus.Discharging;
                reading.Source = source;
                reading.ModelName = "无线键鼠接收器（1EA7）";
                reading.Raw = reply;
                reading.Detail = "命令 0xB5 0x01：电量 " + raw + "%，设备在线";
                return reading;
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail(source, "读取失败：" + ex.Message);
            }
            finally
            {
                dev.Dispose();
            }
        }

        // ===================================================================
        //  策略 C：罗技 HID++ 2.0
        // ===================================================================

        private const int HidppBatteryStatusFeature = 0x1000;
        private const int HidppBatteryVoltageFeature = 0x1001;
        private const int HidppUnifiedBatteryFeature = 0x1004;

        private static readonly int[] HidppDeviceIndices = new int[] { 0xFF, 1, 2, 3, 4, 5, 6 };

        public static BatteryReading ReadLogitech(HidInterface iface)
        {
            if (iface == null || iface.Vid != 0x046D || iface.OutputLen <= 0) return null;

            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, true, out err);
                if (dev == null)
                {
                    return BatteryReading.Fail("罗技 HID++",
                        "无法打开厂商接口：" + err);
                }
                if (!dev.WriteAccess) return null;

                foreach (int rid in new int[] { 0x11, 0x10 })
                {
                    int need = rid == 0x11 ? 20 : 7;
                    if (iface.OutputLen < need) continue;

                    foreach (int index in HidppDeviceIndices)
                    {
                        BatteryReading result = ProbeLogitechIndex(dev, rid, index);
                        if (result != null && result.Ok) return result;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail("罗技 HID++", "读取失败：" + ex.Message);
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        private static int? HidppError(byte[] report)
        {
            if (report == null || report.Length < 4) return null;
            if ((report[3] & 0x80) != 0) return report.Length > 4 ? report[4] : 0xFF;
            return null;
        }

        private static byte[] HidppCall(HidDevice dev, int rid, int index, int featureIndex,
            int function, byte[] parameters, int timeoutMs)
        {
            int length = rid == 0x11 ? 20 : 7;
            var body = new byte[length];
            body[0] = (byte)rid;
            body[1] = (byte)index;
            body[2] = (byte)featureIndex;
            body[3] = (byte)(((function & 0x0F) << 4) | 0x01);
            if (parameters != null && parameters.Length > 0)
            {
                int copy = Math.Min(parameters.Length, length - 4);
                Array.Copy(parameters, 0, body, 4, copy);
            }

            try { dev.WriteOutput(body); }
            catch (HidException) { return null; }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int left = (int)(timeoutMs - sw.ElapsedMilliseconds);
                int timeout = Math.Max(20, Math.Min(150, left));
                byte[] report = dev.ReadInput(timeout, null);
                if (report == null || report.Length < 4) break;
                if (report[0] != (byte)rid || report[1] != (byte)index ||
                    report[2] != (byte)featureIndex)
                    continue;
                if (((report[3] >> 4) & 0x0F) != function) continue;
                return report;
            }
            return null;
        }

        private static int HidppFeatureIndex(HidDevice dev, int rid, int index, int featureId)
        {
            byte[] parameters = new byte[] { (byte)((featureId >> 8) & 0xFF), (byte)(featureId & 0xFF) };
            byte[] reply = HidppCall(dev, rid, index, 0x00, 0x00, parameters, 260);
            if (reply == null) return -1;
            if (HidppError(reply) != null) return -1;

            // 短报文 / 长报文返回位置可能不同，逐个试探
            foreach (int pos in new int[] { 6, 4, 5 })
            {
                if (reply.Length > pos && reply[pos] != 0) return reply[pos];
            }
            return -1;
        }

        private static BatteryStatus HidppStatus(int code, out string text)
        {
            switch (code)
            {
                case 0: text = "放电中"; return BatteryStatus.Discharging;
                case 1: text = "充电中"; return BatteryStatus.Charging;
                case 2: text = "充电中（末段）"; return BatteryStatus.Charging;
                case 3: text = "已充满"; return BatteryStatus.Full;
                case 4: text = "充电中（慢速）"; return BatteryStatus.Charging;
                case 5: text = "电池类型无效"; return BatteryStatus.Unknown;
                case 6: text = "温度异常"; return BatteryStatus.Unknown;
                case 7: text = "充电异常"; return BatteryStatus.Unknown;
                default: text = "状态 " + code; return BatteryStatus.Unknown;
            }
        }

        private static BatteryReading ProbeLogitechIndex(HidDevice dev, int rid, int index)
        {
            // ---- 0x1000：电池状态 ----
            int fidx = HidppFeatureIndex(dev, rid, index, HidppBatteryStatusFeature);
            if (fidx > 0)
            {
                byte[] reply = HidppCall(dev, rid, index, fidx, 0x00, null, 260);
                if (reply != null && HidppError(reply) == null && reply.Length >= 7)
                {
                    int raw = reply[4];
                    int statusCode = reply[6];
                    string text;
                    BatteryStatus status = HidppStatus(statusCode, out text);

                    int level = raw;
                    // 充电时设备常常报 0，给个合理估计
                    if (level == 0 && status != BatteryStatus.Discharging)
                    {
                        if (status == BatteryStatus.Charging) level = 80;
                        else if (status == BatteryStatus.Full) level = 100;
                    }

                    var reading = new BatteryReading();
                    reading.Ok = true;
                    reading.Level = HidText.ClampPercent(level);
                    reading.RawLevel = raw;
                    reading.Status = status;
                    reading.Source = "罗技 HID++ 2.0（0x1000 电池状态）";
                    reading.Detail = "设备索引 " + index + "：电量=" + raw + "% 状态=" + text;
                    reading.Raw = reply;
                    return reading;
                }
            }

            // ---- 0x1004：统一电池 ----
            fidx = HidppFeatureIndex(dev, rid, index, HidppUnifiedBatteryFeature);
            if (fidx > 0)
            {
                byte[] reply = HidppCall(dev, rid, index, fidx, 0x00, null, 260);
                if (reply != null && HidppError(reply) == null && reply.Length >= 6)
                {
                    int raw = reply[4];
                    int statusCode = reply[5];
                    string text;
                    BatteryStatus status = HidppStatus(statusCode, out text);

                    var reading = new BatteryReading();
                    reading.Ok = true;
                    reading.Level = HidText.ClampPercent(raw);
                    reading.RawLevel = raw;
                    reading.Status = status;
                    reading.Source = "罗技 HID++ 2.0（0x1004 统一电池）";
                    reading.Detail = "设备索引 " + index + "：电量=" + raw + "% 状态=" + text;
                    reading.Raw = reply;
                    return reading;
                }
            }

            // ---- 0x1001：电池电压（只有 mV，没有百分比）----
            fidx = HidppFeatureIndex(dev, rid, index, HidppBatteryVoltageFeature);
            if (fidx > 0)
            {
                byte[] reply = HidppCall(dev, rid, index, fidx, 0x00, null, 260);
                if (reply != null && HidppError(reply) == null && reply.Length >= 6)
                {
                    int mv = reply[4] | (reply[5] << 8);
                    if (mv >= 2000 && mv <= 5000)
                    {
                        var reading = new BatteryReading();
                        reading.Ok = true;
                        reading.Level = -1;
                        reading.Status = BatteryStatus.Unknown;
                        reading.VoltageMv = mv;
                        reading.Source = "罗技 HID++ 2.0（0x1001 电池电压）";
                        reading.Detail = "设备索引 " + index + "：电压 " + mv +
                                         " mV（该机型不直接上报百分比）";
                        reading.Raw = reply;
                        return reading;
                    }
                }
            }
            return null;
        }

        // ===================================================================
        //  策略 E：监听 Input Report 里的电量字段
        // ===================================================================

        public static BatteryReading MonitorInput(HidInterface iface, int maxMs)
        {
            if (iface == null || iface.InputLen <= 0) return null;

            const string source = "输入报告监听";
            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, false, out err);
                if (dev == null)
                    return BatteryReading.Fail(source, "无法打开接口：" + err);

                var batteryCaps = new List<HIDP_VALUE_CAPS>();
                foreach (HIDP_VALUE_CAPS cap in dev.HidValueCaps(Win32.HidP_Input))
                {
                    if (IsBatteryUsage(cap.UsagePage, cap.UsageMin)) batteryCaps.Add(cap);
                }

                if (batteryCaps.Count == 0)
                {
                    return BatteryReading.Fail(source,
                        "该接口的输入报告没有声明电量字段，无法通过监听得到电量");
                }

                if (maxMs <= 0) maxMs = 1500;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < maxMs)
                {
                    byte[] report = dev.ReadInput(200, null);
                    if (report == null) continue;

                    foreach (HIDP_VALUE_CAPS cap in batteryCaps)
                    {
                        int? value = dev.UsageValue(Win32.HidP_Input, report, cap);
                        if (value == null) continue;

                        var reading = new BatteryReading();
                        reading.Ok = true;
                        reading.Level = ScaleToPercent(value.Value, cap.LogicalMax);
                        reading.RawLevel = value.Value;
                        reading.Status = BatteryStatus.Unknown;
                        reading.Source = string.Format(
                            "输入报告电量字段（UsagePage=0x{0:X4} Usage=0x{1:X4}）",
                            cap.UsagePage, cap.UsageMin);
                        reading.Detail = "监听 Input Report 得到 原始值=" + value.Value +
                                         " 上限=" + cap.LogicalMax;
                        reading.Raw = report;
                        return reading;
                    }
                }

                return BatteryReading.Fail(source,
                    "监听 " + (maxMs / 1000.0).ToString("0.0") +
                    " 秒没有收到带电量字段的输入报告（设备可能只在事件发生时上报）");
            }
            catch (Exception ex)
            {
                return BatteryReading.Fail(source, "监听失败：" + ex.Message);
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        // ===================================================================
        //  诊断用：Feature 转储 / 原始发送 / Report ID 探测 / 能力表
        // ===================================================================

        /// <summary>每个 report id 读一条 Feature Report（失败跳过）。</summary>
        public static List<byte[]> DumpFeatureReports(HidInterface iface)
        {
            var result = new List<byte[]>();
            if (iface == null || iface.FeatureLen <= 0) return result;

            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, false, out err);
                if (dev == null) return result;

                var ids = new List<int>();
                foreach (HIDP_VALUE_CAPS cap in dev.HidValueCaps(Win32.HidP_Feature))
                {
                    if (!ids.Contains(cap.ReportID)) ids.Add(cap.ReportID);
                }
                for (int rid = 0x00; rid <= 0x20; rid++)
                {
                    if (!ids.Contains(rid)) ids.Add(rid);
                }

                int length = Math.Max(iface.FeatureLen, 2);
                foreach (int rid in ids)
                {
                    try
                    {
                        byte[] data = dev.GetFeatureReport(rid, length);
                        if (data != null && data.Length > 0) result.Add(data);
                    }
                    catch
                    {
                        // 这个 report id 不支持，跳过
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
            return result;
        }

        /// <summary>
        /// 原样发一条报文并收集回复（诊断面板“手动标定”用）。
        /// data[0] 必须已经是 Report ID；length 传 0 表示用接口能力表里的长度。
        /// </summary>
        public static List<byte[]> SendRaw(HidInterface iface, byte[] data, bool feature,
            int waitMs, int length)
        {
            var replies = new List<byte[]>();
            if (iface == null || data == null || data.Length == 0) return replies;

            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, true, out err);
                if (dev == null) return replies;

                int size = length;
                if (size <= 0)
                    size = feature
                        ? Math.Max(iface.FeatureLen, data.Length)
                        : Math.Max(iface.OutputLen, data.Length);
                size = Math.Max(size, data.Length);
                if (waitMs <= 0) waitMs = 150;

                var body = new byte[size];
                Array.Copy(data, body, data.Length);

                if (feature)
                {
                    if (!dev.WriteAccess) return replies;
                    try { dev.SetFeatureReport(body); }
                    catch (HidException) { return replies; }

                    Thread.Sleep(waitMs);
                    try
                    {
                        byte[] reply = dev.GetFeatureReport(body[0], size);
                        if (reply != null && reply.Length > 0) replies.Add(reply);
                    }
                    catch
                    {
                    }
                }
                else
                {
                    if (!dev.WriteAccess) return replies;
                    try
                    {
                        dev.Drain();
                        dev.WriteOutput(body);
                    }
                    catch (HidException)
                    {
                        return replies;
                    }

                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < waitMs)
                    {
                        byte[] reply = dev.ReadInput(100, null);
                        if (reply != null && reply.Length > 0) replies.Add(reply);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
            return replies;
        }

        /// <summary>
        /// 试出该接口接受哪些 Output Report 的 Report ID。
        /// Windows 对 Report ID 不匹配的写请求返回 87（参数错误），
        /// 因此用全 0 报文逐个试就能把合法 ID 列出来。
        /// </summary>
        public static List<int> ProbeReportIds(HidInterface iface)
        {
            var okIds = new List<int>();
            if (iface == null || iface.OutputLen <= 0) return okIds;

            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, true, out err);
                if (dev == null || !dev.WriteAccess) return okIds;

                int length = iface.OutputLen;
                try
                {
                    int capLen = dev.GetCaps().OutputReportByteLength;
                    if (capLen > 0) length = capLen;
                }
                catch
                {
                }
                if (length < 1) return okIds;

                for (int rid = 0x00; rid <= 0x20; rid++)
                {
                    var body = new byte[length];
                    body[0] = (byte)rid;
                    try
                    {
                        dev.WriteOutput(body);
                        okIds.Add(rid);
                    }
                    catch
                    {
                        // 这个 Report ID 不被接受，继续
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
            return okIds;
        }

        /// <summary>枚举一个接口的 Input / Output / Feature 值能力表。</summary>
        public static List<ValueCap> ValueCaps(HidInterface iface, int reportType)
        {
            var empty = new List<ValueCap>();
            if (iface == null) return empty;

            HidDevice dev = null;
            string err;
            try
            {
                dev = HidDevice.Open(iface.Path, false, out err);
                if (dev == null) return empty;
                return dev.ValueCaps(reportType);
            }
            catch
            {
                return empty;
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }
        }

        // ===================================================================
        //  工具
        // ===================================================================

        /// <summary>把原始数值按逻辑上限换算成 0-100。</summary>
        public static int ScaleToPercent(int value, int logicalMax)
        {
            if (value < 0) value = 0;
            if (logicalMax > 0 && logicalMax != 100)
            {
                if (logicalMax == 0xFF && value > 100)
                    return HidText.ClampPercent((int)Math.Round(value * 100.0 / 255.0));
                return HidText.ClampPercent((int)Math.Round(value * 100.0 / logicalMax));
            }
            if (value > 100)
                return HidText.ClampPercent((int)Math.Round(value * 100.0 / 255.0));
            return HidText.ClampPercent(value);
        }
    }
}
