using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using BatterySentinel.Hid;

namespace BatterySentinel.HidHarness
{
    internal static class VxeDebug
    {
        private static string Hex(byte[] b)
        {
            if (b == null) return "(null)";
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(b[i].ToString("X2")); }
            return sb.ToString();
        }

        private static byte[] Frame(byte cmd, byte[] extra)
        {
            var f = new byte[17];
            f[0] = 0x08;
            f[1] = cmd;
            if (extra != null) Array.Copy(extra, 0, f, 2, Math.Min(extra.Length, 15));
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += f[i];
            f[16] = (byte)((0x55 - (sum & 0xFF)) & 0xFF);
            return f;
        }

        private static void Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            var all = HidBattery.Enumerate();
            HidInterface target = null;
            foreach (var it in all)
            {
                if (it.Vid == 0x3554 && it.UsagePage == 0xFF02) { target = it; break; }
            }
            if (target == null) { Console.WriteLine("no VXE iface"); return; }
            Console.WriteLine("iface " + target.Path);

            string err;
            var dev = HidDevice.Open(target.Path, true, out err);
            if (dev == null) { Console.WriteLine("open failed: " + err); return; }
            Console.WriteLine("writeAccess=" + dev.WriteAccess + " caps in=" + dev.GetCaps().InputReportByteLength + " out=" + dev.GetCaps().OutputReportByteLength);

            try
            {
                // 1) INFO with challenge
                var challenge = new byte[] { 0x11, 0x22, 0x33, 0x44 };
                var info = Frame(0x01, new byte[] { 0x00, 0x00, 0x00, 0x08, challenge[0], challenge[1], challenge[2], challenge[3] });
                Console.WriteLine("INFO req: " + Hex(info));
                Dump(dev, info, 700);

                var status = Frame(0x03, null);
                Console.WriteLine("STATUS req: " + Hex(status));
                Dump(dev, status, 700);

                var power = Frame(0x04, null);
                Console.WriteLine("POWER req: " + Hex(power));
                Dump(dev, power, 700);

                Console.WriteLine("POWER req again:");
                Dump(dev, power, 700);

                Console.WriteLine("HidApi.Read: ");
                var r = HidBattery.Read(target, false);
                Console.WriteLine("  ok=" + r.Ok + " level=" + r.Level + " volt=" + r.VoltageMv + " src=" + r.Source + " detail=" + r.Detail + " raw=" + Hex(r.Raw));
            }
            finally
            {
                dev.Dispose();
            }
        }

        private static void Dump(HidDevice dev, byte[] request, int waitMs)
        {
            dev.Drain();
            try { dev.WriteOutput(request); } catch (Exception ex) { Console.WriteLine("  write err " + ex.Message); return; }
            var sw = Stopwatch.StartNew();
            int n = 0;
            while (sw.ElapsedMilliseconds < waitMs)
            {
                byte[] r = dev.ReadInput(120, null);
                if (r == null) continue;
                n++;
                Console.WriteLine("  <- len=" + r.Length + " " + Hex(r));
            }
            Console.WriteLine("  replies=" + n + " elapsed=" + sw.ElapsedMilliseconds);
        }
    }
}
