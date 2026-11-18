// ---------------------------------------------------------------------------
//  HidHarness.cs —— HID 层控制台自测程序（main 入口）
//
//  编译（.NET Framework 4.8 运行时 + dotnet SDK 自带 Roslyn）：
//    dotnet "...\sdk\3.0.101\Roslyn\bincore\csc.dll" /nologo /target:exe /langversion:8.0 ^
//      /out:"...\tests\HidHarness.exe" /nostdlib+ ^
//      /reference:"%FW%\mscorlib.dll" /reference:"%FW%\System.dll" /reference:"%FW%\System.Core.dll" ^
//      src\Hid\*.cs tests\HidHarness.cs
//
//  验收点：
//    1. 打印枚举到的 HID 接口（VID:PID / usage / 报告长度）
//    2. VID=0x3554 的 VXE / ATK 鼠标 → Read（期望 电量 30%、电压 ~3661 mV）
//    3. VID=0x1EA7 的键鼠接收器 → Read（期望 电量 56%）
//    4. 蓝牙设备列表
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BatterySentinel.Hid;

namespace BatterySentinel.HidHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            Console.WriteLine("=== BatterySentinel HID 自测（HidHarness）===");
            Console.WriteLine("系统时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine();

            // 先用 HidBattery 触发静态构造函数（它会把 HidApi.* 委托指向实现）
            var interfaces = HidBattery.Enumerate();
            Console.WriteLine("HidApi 委托绑定：Enumerate=" + DelegateState(HidApi.Enumerate) +
                              " Read=" + DelegateState(HidApi.Read) +
                              " ValueCaps=" + DelegateState(HidApi.ValueCaps) +
                              " BluetoothDevices=" + DelegateState(HidApi.BluetoothDevices));
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 1) 枚举
            // -----------------------------------------------------------------
            Console.WriteLine("--- 1) HID 接口枚举：共 " + interfaces.Count + " 个 ---");
            Console.WriteLine(Pad("序号", 5) + " " + Pad("VID:PID", 9) + " " + Pad("USAGE", 15) + " " +
                              Pad("IN", 4) + " " + Pad("OUT", 4) + " " + Pad("FEAT", 4) + " " +
                              Pad("独占", 4) + " 产品名 / 厂商");
            Console.WriteLine(new string('-', 110));
            for (int i = 0; i < interfaces.Count; i++)
            {
                HidInterface it = interfaces[i];
                string name = it.Product;
                if (string.IsNullOrEmpty(name)) name = "(无产品名)";
                if (!string.IsNullOrEmpty(it.Manufacturer)) name += "  [" + it.Manufacturer + "]";

                Console.WriteLine(Pad("[" + i + "]", 5) + " " +
                                  Pad(it.VidPid, 9) + " " +
                                  Pad(it.UsageText, 15) + " " +
                                  Pad(it.InputLen.ToString(), 4) + " " +
                                  Pad(it.OutputLen.ToString(), 4) + " " +
                                  Pad(it.FeatureLen.ToString(), 4) + " " +
                                  Pad(it.Reserved ? "是" : "否", 4) + " " + name);
            }
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 2) VXE / ATK（VID 0x3554）
            // -----------------------------------------------------------------
            bool vxePass = false;
            bool vxeLevelClose = false;
            HidInterface vxe = FindInterface(interfaces, 0x3554, 0xFF02);
            Console.WriteLine("--- 2) VXE / ATK 鼠标（VID 0x3554）---");
            if (vxe == null)
            {
                Console.WriteLine("未找到 VID=0x3554 且 usage page=0xFF02 的接口。");
            }
            else
            {
                Console.WriteLine("命中接口：" + vxe.VidPid + " " + vxe.UsageText +
                                  "  in=" + vxe.InputLen + " out=" + vxe.OutputLen +
                                  " feat=" + vxe.FeatureLen + "  \"" + vxe.DisplayName + "\"");

                var caps = HidApi.ValueCaps(vxe, 0);
                Console.WriteLine("ValueCaps(Input) = " + caps.Count + " 项" +
                                  (caps.Count > 0 ? "：" + DescribeCaps(caps) : ""));
                Console.WriteLine("DumpFeatureReports = " + HidBattery.DumpFeatureReports(vxe).Count +
                                  " 条（该接口没有 Feature Report）");

                // 诊断：dongle 自己的 INFO(0x01) / STATUS(0x03)，用来确认私有链路是否正常
                byte[] infoReq = BuildVxeInfoFrame();
                List<byte[]> infoReplies = HidBattery.SendRaw(vxe, infoReq, false, 500, 17);
                Console.WriteLine("诊断 INFO(0x01) → " + infoReplies.Count + " 条回复" +
                                  (infoReplies.Count > 0 ? "：" + Hex(infoReplies[0]) : ""));
                if (infoReplies.Count > 0 && infoReplies[0].Length >= 13)
                {
                    byte[] ir = infoReplies[0];
                    Console.WriteLine("    型号=0x" + ((ir[10] << 8) | ir[11]).ToString("X4") +
                                      " 连接类型=" + ir[12] + "（0=1K 1=4K 2=有线）");
                }
                byte[] statusReq = BuildVxeFrame(0x03);
                List<byte[]> statusReplies = HidBattery.SendRaw(vxe, statusReq, false, 500, 17);
                Console.WriteLine("诊断 STATUS(0x03) → " + statusReplies.Count + " 条回复" +
                                  (statusReplies.Count > 0 ? "：" + Hex(statusReplies[0]) : "") +
                                  (statusReplies.Count > 0 && statusReplies[0].Length > 6
                                      ? "（[6]=" + statusReplies[0][6] + "，1=设备在线）" : ""));

                var sw = Stopwatch.StartNew();
                BatteryReading r = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    r = HidApi.Read(vxe, true);
                    if (r.Ok) break;
                    if (attempt < 2)
                    {
                        Console.WriteLine("第 " + (attempt + 1) + " 次读取失败：" + r.Detail +
                                          "（鼠标休眠时这是正常现象，1 秒后重试）");
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                sw.Stop();

                PrintReading(r, sw.ElapsedMilliseconds);

                // 再按“不允许监听输入报告”的方式读一次（走纯厂商协议路径）
                var sw2 = Stopwatch.StartNew();
                BatteryReading r2 = HidApi.Read(vxe, false);
                sw2.Stop();
                Console.WriteLine("allowInputMonitor=false → Ok=" + r2.Ok +
                                  " Level=" + r2.Level + " Voltage=" + r2.VoltageMv +
                                  "（耗时 " + sw2.ElapsedMilliseconds + " ms）");

                // SendRaw 演示：同一条 0x04 电量查询命令
                byte[] frame = BuildVxePowerFrame();
                List<byte[]> replies = HidBattery.SendRaw(vxe, frame, false, 450, 17);
                Console.WriteLine("SendRaw(" + Hex(frame) + ") → " + replies.Count + " 条回复");
                foreach (byte[] reply in replies)
                    Console.WriteLine("    回复: " + Hex(reply));

                vxePass = r.Ok && r2.Ok && r.Level >= 0;
                vxeLevelClose = r.Ok && Math.Abs(r.Level - 30) <= 1;
                Console.WriteLine("判定: " + (r.Ok ? "读取成功" : "读取失败") +
                                  (vxeLevelClose ? "，电量 " + r.Level + "%（期望 30±1）√" : ""));
            }
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 3) 1EA7:0068 键鼠接收器
            // -----------------------------------------------------------------
            bool donglePass = false;
            bool dongleLevelClose = false;
            HidInterface dongle = FindInterface(interfaces, 0x1EA7, 0xFFB5);
            Console.WriteLine("--- 3) 键鼠接收器（VID 0x1EA7）---");
            if (dongle == null)
            {
                Console.WriteLine("未找到 VID=0x1EA7 且 usage page=0xFFB5 的接口。");
            }
            else
            {
                Console.WriteLine("命中接口：" + dongle.VidPid + " " + dongle.UsageText +
                                  "  in=" + dongle.InputLen + " out=" + dongle.OutputLen +
                                  " feat=" + dongle.FeatureLen + "  \"" + dongle.DisplayName + "\"");
                Console.WriteLine("ValueCaps(Input) = " + HidApi.ValueCaps(dongle, 0).Count +
                                  " 项；DumpFeatureReports = " +
                                  HidBattery.DumpFeatureReports(dongle).Count + " 条（无 Feature 接口）");

                var sw = Stopwatch.StartNew();
                BatteryReading r = HidApi.Read(dongle, true);
                sw.Stop();

                PrintReading(r, sw.ElapsedMilliseconds);

                // SendRaw 演示
                byte[] frame = new byte[] { 0xB5, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                List<byte[]> replies = HidBattery.SendRaw(dongle, frame, false, 500, 8);
                Console.WriteLine("SendRaw(" + Hex(frame) + ") → " + replies.Count + " 条回复");
                foreach (byte[] reply in replies)
                    Console.WriteLine("    回复: " + Hex(reply));

                donglePass = r.Ok && r.Level >= 0;
                dongleLevelClose = r.Ok && Math.Abs(r.Level - 56) <= 1;
                Console.WriteLine("判定: " + (r.Ok ? "读取成功" : "读取失败") +
                                  (dongleLevelClose ? "，电量 " + r.Level + "%（期望 56）√" : ""));
            }
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 4) 蓝牙
            // -----------------------------------------------------------------
            Console.WriteLine("--- 4) 蓝牙设备（HFP 电量属性）---");
            List<BtDevice> bt = HidApi.BluetoothDevices();
            Console.WriteLine("共 " + bt.Count + " 个蓝牙节点");
            foreach (BtDevice d in bt)
            {
                Console.WriteLine("  " + (d.HasProperty ? "[有属性]" : "[无属性]") +
                                  " " + d.Name + "  电量=" +
                                  (d.Battery >= 0 ? d.Battery.ToString() + "%" : "未知") +
                                  "  " + d.Reason + (d.IsHandsFree ? "（Hands-Free）" : ""));
                Console.WriteLine("     实例：" + d.InstanceId);
            }
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 5) 健壮性抽查：其它接口 / 独占接口调用 Read 不能抛异常
            // -----------------------------------------------------------------
            Console.WriteLine("--- 5) 健壮性抽查（Read 不抛异常，读不到时返回中文说明）---");
            int sampled = 0;
            foreach (HidInterface it in interfaces)
            {
                if (sampled >= 3) break;
                if (it.Vid == 0x3554 || it.Vid == 0x1EA7) continue;      // 上面已测
                if (!it.IsVendorPage && it.FeatureLen <= 0) continue;    // 跳过纯标准接口
                sampled++;
                BatteryReading rr = HidApi.Read(it, false);
                Console.WriteLine("  " + it.VidPid + " " + it.UsageText +
                                  (it.Reserved ? "（独占）" : "") +
                                  " → Ok=" + rr.Ok + " Level=" + rr.Level +
                                  " Source=" + rr.Source);
                Console.WriteLine("      Detail=" + rr.Detail);
            }
            Console.WriteLine();

            // -----------------------------------------------------------------
            // 汇总
            // -----------------------------------------------------------------
            bool pass = vxePass && donglePass && vxeLevelClose && dongleLevelClose;
            Console.WriteLine("=== 验收结论 ===");
            Console.WriteLine("VXE(0x3554) 读取: " + (vxePass ? "成功" : "失败") +
                              (vxeLevelClose ? "  电量 30±1 √" : "  电量不在 30±1"));
            Console.WriteLine("1EA7 读取:       " + (donglePass ? "成功" : "失败") +
                              (dongleLevelClose ? "  电量 56 √" : "  电量不是 56"));
            Console.WriteLine("总判定: " + (pass ? "PASS" : "FAIL"));
            return pass ? 0 : 1;
        }

        // -------------------------------------------------------------------
        //  输出辅助
        // -------------------------------------------------------------------

        private static void PrintReading(BatteryReading r, long elapsedMs)
        {
            Console.WriteLine("HidApi.Read 结果（耗时 " + elapsedMs + " ms）：");
            Console.WriteLine("  Ok      = " + r.Ok);
            Console.WriteLine("  Level   = " + (r.Level >= 0 ? r.Level + " %" : "未知(-1)"));
            Console.WriteLine("  RawLevel= " + r.RawLevel);
            Console.WriteLine("  Voltage = " + (r.VoltageMv > 0 ? r.VoltageMv + " mV" : "未知(-1)"));
            Console.WriteLine("  Status  = " + StatusText(r.Status));
            Console.WriteLine("  Source  = " + r.Source);
            if (!string.IsNullOrEmpty(r.ModelName)) Console.WriteLine("  Model   = " + r.ModelName);
            if (!string.IsNullOrEmpty(r.Firmware)) Console.WriteLine("  Firmware= " + r.Firmware);
            Console.WriteLine("  Detail  = " + r.Detail);
            Console.WriteLine("  Raw     = " + Hex(r.Raw));
        }

        private static string StatusText(BatteryStatus status)
        {
            switch (status)
            {
                case BatteryStatus.Discharging: return "放电中";
                case BatteryStatus.Charging: return "充电中";
                case BatteryStatus.Full: return "已充满";
                default: return "未知";
            }
        }

        private static HidInterface FindInterface(List<HidInterface> list, int vid, int usagePage)
        {
            foreach (HidInterface it in list)
            {
                if (it.Vid == vid && it.UsagePage == usagePage) return it;
            }
            foreach (HidInterface it in list)
            {
                if (it.Vid == vid) return it;
            }
            return null;
        }

        private static string DescribeCaps(List<ValueCap> caps)
        {
            var sb = new StringBuilder();
            foreach (ValueCap c in caps)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append("page=0x" + c.UsagePage.ToString("X4") +
                          " usage=0x" + c.Usage.ToString("X4") +
                          " rid=0x" + c.ReportId.ToString("X2") +
                          " bits=" + c.BitSize + " max=" + c.LogicalMax);
            }
            return sb.ToString();
        }

        /// <summary>VXE / ATK 的 17 字节帧（校验 = (0x55 - sum(buf[0..15])) &amp; 0xFF）。</summary>
        private static byte[] BuildVxeFrame(byte command)
        {
            var frame = new byte[17];
            frame[0] = 0x08;
            frame[1] = command;
            FixVxeChecksum(frame);
            return frame;
        }

        /// <summary>0x01 设备信息帧：[5]=0x08，[6..9]=4 字节挑战。</summary>
        private static byte[] BuildVxeInfoFrame()
        {
            var frame = new byte[17];
            frame[0] = 0x08;
            frame[1] = 0x01;
            frame[5] = 0x08;
            frame[6] = 0x11;
            frame[7] = 0x22;
            frame[8] = 0x33;
            frame[9] = 0x44;
            FixVxeChecksum(frame);
            return frame;
        }

        private static void FixVxeChecksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += frame[i];
            frame[16] = (byte)((0x55 - (sum & 0xFF)) & 0xFF);
        }

        /// <summary>0x04 电量查询帧。</summary>
        private static byte[] BuildVxePowerFrame()
        {
            return BuildVxeFrame(0x04);
        }

        private static string Hex(byte[] data)
        {
            if (data == null || data.Length == 0) return "(空)";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string DelegateState(Delegate d)
        {
            if (d == null) return "null";
            return d.Method.DeclaringType.Name + "." + d.Method.Name;
        }

        /// <summary>按“显示宽度”粗略补齐（中文算 2 格）。</summary>
        private static string Pad(string text, int width)
        {
            if (text == null) text = "";
            int actual = 0;
            foreach (char ch in text) actual += ch > 0x7F ? 2 : 1;
            if (actual >= width) return text;
            return text + new string(' ', width - actual);
        }
    }
}
