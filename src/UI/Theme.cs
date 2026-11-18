using System;
using System.Windows;
using System.Windows.Media;

namespace BatterySentinel.UI
{
    /// <summary>配色与字体（深浅色两套，跟随系统或手动指定）。</summary>
    public static class Theme
    {
        public static bool IsDark { get; private set; }
        public static Color AccentColor = Color.FromRgb(0x4F, 0x8C, 0xFF);
        public static FontFamily Font;
        public static FontFamily IconFont;

        public static Brush Bg, BgTop, BgBottom, Card, CardHover, Stroke, StrokeSoft, Text, Text2, Text3,
            Accent, AccentHover, AccentSoft, Green, Yellow, Orange, Red, Hover, Selected, WindowBorder, Shadow, ScrollThumb, ScrollThumbHover;

        public static event Action Changed;

        static Theme()
        {
            try { Font = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"); }
            catch { Font = new FontFamily("Segoe UI, Microsoft YaHei UI"); }
            try { IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); }
            catch { IconFont = new FontFamily("Segoe MDL2 Assets"); }
            Init(false, Color.FromRgb(0x4F, 0x8C, 0xFF));
        }

        public static void Init(bool dark, Color accent)
        {
            IsDark = dark;
            AccentColor = accent;

            if (dark)
            {
                BgTop = New(0x1E, 0x1F, 0x24);
                BgBottom = New(0x15, 0x16, 0x1A);
                Card = New(0x23, 0x25, 0x2B);
                CardHover = New(0x2A, 0x2C, 0x33);
                Stroke = New(0x2E, 0x30, 0x38);
                StrokeSoft = New(0x24, 0x26, 0x2C);
                Text = New(0xF4, 0xF4, 0xF6);
                Text2 = New(0xA9, 0xAC, 0xB4);
                Text3 = New(0x74, 0x77, 0x7F);
                Hover = New(0x2C, 0x2E, 0x36);
                Selected = New(0x33, 0x36, 0x40);
                WindowBorder = New(0x3A, 0x3D, 0x46);
                Green = New(0x3D, 0xDC, 0x84);
                Yellow = New(0xF5, 0xC4, 0x51);
                Orange = New(0xFF, 0x9F, 0x45);
                Red = New(0xFF, 0x6B, 0x6B);
                ScrollThumb = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                ScrollThumbHover = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                BgTop = New(0xF7, 0xF8, 0xFA);
                BgBottom = New(0xEC, 0xEE, 0xF3);
                Card = New(0xFF, 0xFF, 0xFF);
                CardHover = New(0xFA, 0xFB, 0xFD);
                Stroke = New(0xE2, 0xE4, 0xE9);
                StrokeSoft = New(0xEC, 0xEE, 0xF2);
                Text = New(0x1B, 0x1C, 0x1F);
                Text2 = New(0x5B, 0x5F, 0x66);
                Text3 = New(0x8B, 0x8F, 0x96);
                Hover = New(0xF0, 0xF2, 0xF6);
                Selected = New(0xE8, 0xEC, 0xF4);
                WindowBorder = New(0xD8, 0xDB, 0xE1);
                Green = New(0x12, 0xA3, 0x4A);
                Yellow = New(0xC0, 0x86, 0x00);
                Orange = New(0xD9, 0x76, 0x0B);
                Red = New(0xDC, 0x26, 0x26);
                ScrollThumb = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
                ScrollThumbHover = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00));
            }

            Accent = New(accent.R, accent.G, accent.B);
            AccentHover = New((byte)Math.Min(255, accent.R + 22), (byte)Math.Min(255, accent.G + 22), (byte)Math.Min(255, accent.B + 22));
            AccentSoft = new SolidColorBrush(Color.FromArgb(46, accent.R, accent.G, accent.B));
            Bg = new LinearGradientBrush(((SolidColorBrush)BgTop).Color, ((SolidColorBrush)BgBottom).Color, 90);
            Shadow = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x66 : (byte)0x22, 0, 0, 0));

            Application app = Application.Current;
            if (app != null)
            {
                ResourceDictionary r = app.Resources;
                r["Bg"] = Bg;
                r["BgTop"] = BgTop;
                r["BgBottom"] = BgBottom;
                r["Card"] = Card;
                r["CardHover"] = CardHover;
                r["Stroke"] = Stroke;
                r["StrokeSoft"] = StrokeSoft;
                r["Text"] = Text;
                r["Text2"] = Text2;
                r["Text3"] = Text3;
                r["Accent"] = Accent;
                r["AccentHover"] = AccentHover;
                r["AccentSoft"] = AccentSoft;
                r["Green"] = Green;
                r["Yellow"] = Yellow;
                r["Red"] = Red;
                r["Hover"] = Hover;
                r["Selected"] = Selected;
                r["WindowBorder"] = WindowBorder;
                r["ScrollThumb"] = ScrollThumb;
                r["ScrollThumbHover"] = ScrollThumbHover;
            }

            Action c = Changed;
            if (c != null) c();
        }

        private static SolidColorBrush New(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public static SolidColorBrush LevelColor(int level)
        {
            if (level < 0) return (SolidColorBrush)Text3;
            if (level >= 60) return (SolidColorBrush)Green;
            if (level >= 30) return (SolidColorBrush)Yellow;
            if (level >= 15) return (SolidColorBrush)Orange;
            return (SolidColorBrush)Red;
        }

        public static string LevelLabel(int level)
        {
            if (level < 0) return "未读取";
            if (level >= 60) return "电量充足";
            if (level >= 30) return "电量中等";
            if (level >= 15) return "电量偏低";
            return "电量告急";
        }

        /// <summary>在给定背景上取更易读的前景色。</summary>
        public static Color WithAlpha(Color c, byte a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }
    }
}
