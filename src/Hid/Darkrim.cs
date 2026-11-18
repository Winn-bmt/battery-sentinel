// ---------------------------------------------------------------------------
//  Darkrim.cs —— DARKRIM（杰理 Jieli 方案）2.4G 耳机 dongle 的私有 TLV 协议
//
//  协议来源：逆向厂商软件 + 抓包 + 爆破 CRC，已在真机验证。
//
//  传输：HID 厂商接口，UsagePage=0xFF02 Usage=0x0002，报告长度 65 字节（第 0 字节 0x00）。
//  帧格式（65 字节）：
//      0     0x00 固定
//      1     0xFE 帧头
//      2     序号（每次请求 +1）
//      3     0x10 = 本机侧(dongle) / 0x11 = 耳机侧
//      4     0x00
//      5     0x02 / 0x03
//      6     分片序号
//      7     0x02 = 还有后续分片
//      8-9   载荷长度（小端）
//      10..  载荷（TLV：type(1) + len(1) + value(len)）
//      ...   紧接载荷之后 2 字节 CRC16-CCITT-FALSE（大端），覆盖帧[1 .. 10+len)
//
//  TLV：0x01 状态/电量、0x02 设备名、0x03 EQ、0x04 dongle 固件、0x05 耳机固件、
//       0x06 状态码、0x09 基础信息、0x0B 地址、0xFF 校验信息。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace BatterySentinel.Hid
{
    /// <summary>一帧 DARKRIM 报文。</summary>
    public sealed class DarkrimFrame
    {
        public int Seq;
        public int Channel;
        public int Sub;
        public int Fragment;
        public int More;
        public byte[] Payload = new byte[0];
        public bool CrcOk;
        public byte[] Raw = new byte[0];

        /// <summary>把载荷拆成 (type, value) 列表。</summary>
        public List<DarkrimTlv> Tlvs()
        {
            var list = new List<DarkrimTlv>();
            byte[] p = Payload;
            int i = 0;
            while (i + 2 <= p.Length)
            {
                int type = p[i];
                int len = p[i + 1];
                int start = i + 2;
                int take = Math.Min(len, Math.Max(0, p.Length - start));
                var value = new byte[take];
                Array.Copy(p, start, value, 0, take);
                var tlv = new DarkrimTlv();
                tlv.Type = type;
                tlv.Value = value;
                list.Add(tlv);
                i = start + len;
            }
            return list;
        }
    }

    /// <summary>一个 TLV 项。</summary>
    public sealed class DarkrimTlv
    {
        public int Type;
        public byte[] Value = new byte[0];

        public string ValueText
        {
            get { return Value != null && Value.Length > 0 ? HidText.Hex(Value) : "-"; }
        }

        public string TypeName
        {
            get { return Darkrim.TlvTypeName(Type); }
        }
    }

    /// <summary>DARKRIM dongle + 耳机信息。</summary>
    public sealed class DarkrimInfo
    {
        public bool Online;
        public int Battery = -1;
        public int BatteryRaw = -1;
        public string Name = "";
        public string DongleFirmware = "";
        public string HeadsetFirmware = "";
        public string Address = "";
        public int StatusCode = -1;
        public List<DarkrimTlv> RawTlvs = new List<DarkrimTlv>();
        public string Detail = "";
    }

    /// <summary>DARKRIM TLV 协议实现。</summary>
    public static class Darkrim
    {
        public const int Vid = 0x3654;
        public const int UsagePage = 0xFF02;
        public const int Usage = 0x0002;
        public const int ReportLength = 65;

        public const byte ChannelLocal = 0x10;
        public const byte ChannelHeadset = 0x11;

        public const int TlvBattery = 0x01;
        public const int TlvName = 0x02;
        public const int TlvFwDongle = 0x04;
        public const int TlvFwHeadset = 0x05;
        public const int TlvStatus = 0x06;
        public const int TlvAddress = 0x0B;

        /// <summary>该接口是不是 DARKRIM 的命令接口。</summary>
        public static bool Matches(HidInterface iface)
        {
            if (iface == null) return false;
            return iface.Vid == Vid && iface.UsagePage == UsagePage &&
                   iface.OutputLen == ReportLength;
        }

        public static string TlvTypeName(int type)
        {
            switch (type)
            {
                case 0x01: return "状态/电量";
                case 0x02: return "设备名";
                case 0x03: return "EQ 数据";
                case 0x04: return "dongle 固件版本";
                case 0x05: return "耳机固件版本";
                case 0x06: return "状态码";
                case 0x09: return "基础信息";
                case 0x0B: return "地址/ID";
                case 0xFF: return "校验信息";
                default: return "未知 TLV";
            }
        }

        /// <summary>CRC16-CCITT-FALSE：poly 0x1021，init 0xFFFF，不反转。</summary>
        public static ushort Crc16Ccitt(byte[] data, int offset, int count)
        {
            int crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= (data[offset + i] << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x8000) != 0) crc = ((crc << 1) ^ 0x1021) & 0xFFFF;
                    else crc = (crc << 1) & 0xFFFF;
                }
            }
            return (ushort)crc;
        }

        /// <summary>拼一帧 65 字节的请求。</summary>
        public static byte[] BuildFrame(int seq, byte[] payload, byte channel,
            byte sub, byte fragment, byte more)
        {
            if (payload == null) payload = new byte[0];
            if (payload.Length > ReportLength - 12)
                throw new ArgumentException("TLV 载荷太长，超过 65 字节帧容量");

            var frame = new byte[ReportLength];
            frame[0] = 0x00;
            frame[1] = 0xFE;
            frame[2] = (byte)(seq & 0xFF);
            frame[3] = channel;
            frame[4] = 0x00;
            frame[5] = sub;
            frame[6] = fragment;
            frame[7] = more;
            frame[8] = (byte)(payload.Length & 0xFF);
            frame[9] = (byte)((payload.Length >> 8) & 0xFF);
            Array.Copy(payload, 0, frame, 10, payload.Length);

            ushort crc = Crc16Ccitt(frame, 1, 9 + payload.Length);
            frame[10 + payload.Length] = (byte)((crc >> 8) & 0xFF);
            frame[11 + payload.Length] = (byte)(crc & 0xFF);
            return frame;
        }

        /// <summary>解析一帧回复（不是合法帧返回 null）。</summary>
        public static DarkrimFrame ParseFrame(byte[] report)
        {
            if (report == null || report.Length < 12) return null;
            if (report[1] != 0xFE) return null;

            int len = report[8] | (report[9] << 8);
            if (len < 0 || 10 + len + 2 > report.Length) return null;

            var frame = new DarkrimFrame();
            frame.Seq = report[2];
            frame.Channel = report[3];
            frame.Sub = report[5];
            frame.Fragment = report[6];
            frame.More = report[7];
            frame.Payload = new byte[len];
            Array.Copy(report, 10, frame.Payload, 0, len);

            int crc = (report[10 + len] << 8) | report[11 + len];
            frame.CrcOk = crc == Crc16Ccitt(report, 1, 9 + len);
            frame.Raw = report;
            return frame;
        }

        /// <summary>发一条 TLV 查询（len=0），把这一轮同序号的回复都收回来。</summary>
        public static List<DarkrimFrame> Query(HidDevice dev, int seq, int tlvType,
            byte channel, int timeoutMs)
        {
            var frames = new List<DarkrimFrame>();
            dev.Drain();

            byte[] payload = new byte[] { (byte)tlvType, 0x00 };
            dev.WriteOutput(BuildFrame(seq, payload, channel, 0x02, 0x00, 0x00));

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int left = (int)(timeoutMs - sw.ElapsedMilliseconds);
                byte[] report = dev.ReadInput(Math.Max(60, Math.Min(120, left)), null);
                if (report == null) continue;

                DarkrimFrame frame = ParseFrame(report);
                if (frame == null) continue;
                if ((frame.Seq & 0xFF) != (seq & 0xFF)) continue;
                frames.Add(frame);
            }
            return frames;
        }

        /// <summary>读取 dongle + 耳机的信息与电量（约 0.35~2 秒）。</summary>
        public static DarkrimInfo ReadInfo(HidInterface iface, int timeoutMs)
        {
            var info = new DarkrimInfo();
            if (iface == null)
            {
                info.Detail = "没有传入 DARKRIM 接口";
                return info;
            }

            string err;
            HidDevice dev = HidDevice.Open(iface.Path, true, out err);
            if (dev == null)
            {
                info.Detail = "无法打开 DARKRIM 接口：" + err;
                return info;
            }

            try
            {
                if (!dev.WriteAccess)
                {
                    info.Detail = "DARKRIM 接口没有写权限（可能被厂商软件占用）";
                    return info;
                }

                int seq = 0;
                List<DarkrimFrame> frames = Query(dev, seq, TlvBattery, ChannelLocal, timeoutMs);
                seq = (seq + 1) & 0xFF;

                foreach (DarkrimFrame frame in frames)
                {
                    foreach (DarkrimTlv tlv in frame.Tlvs())
                    {
                        if (tlv.Type == TlvBattery && tlv.Value.Length >= 1)
                        {
                            info.BatteryRaw = tlv.Value[0];
                            if (tlv.Value[0] <= 100) info.Battery = tlv.Value[0];
                            info.Online = true;
                        }
                        info.RawTlvs.Add(tlv);
                    }
                }

                int[] others = new int[]
                {
                    TlvName, TlvFwDongle, TlvFwHeadset, TlvAddress, TlvStatus, 0x09
                };
                foreach (int type in others)
                {
                    List<DarkrimFrame> more = Query(dev, seq, type, ChannelLocal, timeoutMs);
                    seq = (seq + 1) & 0xFF;
                    foreach (DarkrimFrame frame in more)
                    {
                        foreach (DarkrimTlv tlv in frame.Tlvs())
                        {
                            switch (tlv.Type)
                            {
                                case TlvName:
                                    info.Name = AsciiFromTlv(tlv.Value);
                                    break;
                                case TlvFwDongle:
                                    info.DongleFirmware = AsciiFromTlv(tlv.Value);
                                    break;
                                case TlvFwHeadset:
                                    info.HeadsetFirmware = AsciiFromTlv(tlv.Value);
                                    break;
                                case TlvAddress:
                                    info.Address = AddressText(tlv.Value);
                                    break;
                                case TlvStatus:
                                    if (tlv.Value.Length >= 1) info.StatusCode = tlv.Value[0];
                                    break;
                            }
                            info.Online = true;
                            if (tlv.Type != 0x03 && tlv.Type != 0x14) info.RawTlvs.Add(tlv);
                        }
                    }
                }

                info.Detail = "设备=" + (info.Name.Length > 0 ? info.Name : "?") +
                              " 固件(dongle/耳机)=" +
                              (info.DongleFirmware.Length > 0 ? info.DongleFirmware : "?") + "/" +
                              (info.HeadsetFirmware.Length > 0 ? info.HeadsetFirmware : "?") +
                              " 状态码=" + (info.StatusCode >= 0 ? info.StatusCode.ToString() : "?");
            }
            catch (Exception ex)
            {
                info.Detail = "读取失败：" + ex.Message;
            }
            finally
            {
                dev.Dispose();
            }
            return info;
        }

        /// <summary>轻量读取：只问一次电量（TLV 0x01）。返回 -1 表示没读到。</summary>
        public static int ReadBatteryOnly(HidInterface iface, int timeoutMs, out string detail)
        {
            detail = "";
            if (iface == null)
            {
                detail = "没有传入 DARKRIM 接口";
                return -1;
            }

            string err;
            HidDevice dev = HidDevice.Open(iface.Path, true, out err);
            if (dev == null)
            {
                detail = "无法打开 DARKRIM 接口：" + err;
                return -1;
            }

            try
            {
                foreach (DarkrimFrame frame in Query(dev, 0, TlvBattery, ChannelLocal, timeoutMs))
                {
                    foreach (DarkrimTlv tlv in frame.Tlvs())
                    {
                        if (tlv.Type == TlvBattery && tlv.Value.Length >= 1 && tlv.Value[0] <= 100)
                            return tlv.Value[0];
                    }
                }
                detail = "耳机没有上报电量（确认耳机已开机并连上）";
                return -1;
            }
            catch (Exception ex)
            {
                detail = "读取失败：" + ex.Message;
                return -1;
            }
            finally
            {
                dev.Dispose();
            }
        }

        private static string AsciiFromTlv(byte[] value)
        {
            if (value == null) return "";
            var sb = new StringBuilder();
            foreach (byte b in value)
            {
                if (b >= 32 && b < 127) sb.Append((char)b);
            }
            return sb.ToString().Trim();
        }

        private static string AddressText(byte[] value)
        {
            if (value == null) return "";
            var parts = new string[value.Length];
            for (int i = 0; i < value.Length; i++) parts[i] = value[i].ToString("X2");
            return string.Join("-", parts);
        }
    }
}
