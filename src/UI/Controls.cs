using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BatterySentinel.UI
{
    /// <summary>Windows 11 风格的开关。</summary>
    public sealed class ToggleSwitch : ToggleButton
    {
        public ToggleSwitch()
        {
            Width = 40;
            Height = 22;
            Cursor = Cursors.Hand;
            FocusVisualStyle = null;
            Template = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'>
  <Grid Width='40' Height='22' Background='Transparent'>
    <Border x:Name='track' CornerRadius='11' Background='{DynamicResource Stroke}'/>
    <Border x:Name='thumb' Width='14' Height='14' CornerRadius='7' Background='{DynamicResource Text2}'
            HorizontalAlignment='Left' Margin='4,0,0,0'/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='track' Property='Background' Value='{DynamicResource Accent}'/>
      <Setter TargetName='thumb' Property='Background' Value='White'/>
      <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
      <Setter TargetName='thumb' Property='Margin' Value='0,0,4,0'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='track' Property='Opacity' Value='0.85'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter Property='Opacity' Value='0.45'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
        }
    }

    /// <summary>分段选择器。</summary>
    public sealed class Segmented : Border
    {
        private readonly StackPanel _panel = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly string[] _labels;
        private int _index;
        public event Action<int> SelectionChanged;

        public Segmented(string[] labels, int index)
        {
            _labels = labels ?? new string[0];
            _index = Math.Max(0, Math.Min(index, _labels.Length - 1));
            Background = new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)0x2A : (byte)0x08, 0, 0, 0));
            BorderBrush = Theme.StrokeSoft;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(7);
            Padding = new Thickness(2);
            HorizontalAlignment = HorizontalAlignment.Left;
            Child = _panel;
            Rebuild();
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (value == _index || value < 0 || value >= _labels.Length) return;
                _index = value;
                Rebuild();
                Action<int> h = SelectionChanged;
                if (h != null) h(_index);
            }
        }

        private void Rebuild()
        {
            _panel.Children.Clear();
            for (int i = 0; i < _labels.Length; i++)
            {
                int idx = i;
                bool sel = i == _index;
                var tb = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(12, 5, 12, 5),
                    Background = sel ? Theme.Card : Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = Ui.Txt(_labels[i], 12, sel ? Theme.Text : Theme.Text2, semibold: sel)
                };
                if (sel) Ui.Shadow(tb, 8, Theme.IsDark ? 0.45 : 0.12, 1);
                if (!sel)
                {
                    tb.MouseEnter += (s, e) => tb.Background = Theme.Hover;
                    tb.MouseLeave += (s, e) => tb.Background = Brushes.Transparent;
                }
                tb.MouseLeftButtonUp += (s, e) => SelectedIndex = idx;
                _panel.Children.Add(tb);
            }
        }
    }

    /// <summary>环形电量指示（带数值动画）。</summary>
    public sealed class BatteryRing : FrameworkElement
    {
        private int _level = -1;
        private double _animated = -1;
        private System.Windows.Threading.DispatcherTimer _timer;
        private double _from;
        private double _to;
        private DateTime _animStart;
        private const double AnimMs = 520;

        public double Thickness = 9;
        public bool ShowCaption = true;

        public BatteryRing()
        {
            Width = 104;
            Height = 104;
            SnapsToDevicePixels = true;
        }

        public int Level
        {
            get { return _level; }
            set
            {
                if (value == _level) return;
                _level = value;
                double target = value < 0 ? -1 : value;
                if (_timer == null)
                {
                    _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    _timer.Tick += (s, e) => Tick();
                }
                _from = _animated;
                if (_from < 0) _from = 0;
                _to = target < 0 ? 0 : target;
                _animStart = DateTime.UtcNow;
                _timer.Start();
                InvalidateVisual();
            }
        }

        private void Tick()
        {
            double t = (DateTime.UtcNow - _animStart).TotalMilliseconds / AnimMs;
            if (t >= 1)
            {
                _animated = _to;
                _timer.Stop();
            }
            else
            {
                double e = 1 - Math.Pow(1 - t, 3);
                _animated = _from + (_to - _from) * e;
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            double radius = (size - Thickness) / 2 - 1;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double start = 135;
            double sweep = 270;

            var trackColor = Theme.IsDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(22, 0, 0, 0);
            var trackPen = new Pen(new SolidColorBrush(trackColor), Thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(null, trackPen, Arc(center, radius, start, sweep));

            if (_animated > 0)
            {
                Color c = Theme.LevelColor(_level).Color;
                var brush = new LinearGradientBrush(c, Color.FromArgb(255, (byte)Math.Min(255, c.R + 30), (byte)Math.Min(255, c.G + 30), (byte)Math.Min(255, c.B + 30)), new Point(0, 0), new Point(1, 1));
                var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                double valueSweep = sweep * Math.Max(0, Math.Min(1, _animated / 100.0));
                dc.DrawGeometry(null, pen, Arc(center, radius, start, Math.Max(0.01, valueSweep)));
            }

            // 中心文字
            string num = _level < 0 ? "--" : _level.ToString(CultureInfo.InvariantCulture);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var smallTypeface = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Brush fg = _level < 0 ? Theme.Text3 : Theme.LevelColor(_level);
            double numSize = size * 0.27;
            var ft = new FormattedText(num, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, numSize, fg, dpi);
            double pctSize = size * 0.115;
            var ftPct = new FormattedText("%", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, smallTypeface, pctSize, Theme.Text3, dpi);
            double total = ft.Width + 2 + ftPct.Width;
            double baseY = center.Y - ft.Height / 2 + (ShowCaption ? -6 : 0);
            dc.DrawText(ft, new Point(center.X - total / 2, baseY));
            dc.DrawText(ftPct, new Point(center.X - total / 2 + ft.Width + 2, baseY + ft.Height - ftPct.Height - 2));

            if (ShowCaption)
            {
                string cap = Theme.LevelLabel(_level);
                var ftCap = new FormattedText(cap, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, smallTypeface, size * 0.098, Theme.Text3, dpi);
                dc.DrawText(ftCap, new Point(center.X - ftCap.Width / 2, baseY + ft.Height + 2));
            }
        }

        private static Geometry Arc(Point center, double radius, double startAngle, double sweepAngle)
        {
            double a0 = startAngle * Math.PI / 180.0;
            double a1 = (startAngle + sweepAngle) * Math.PI / 180.0;
            var p0 = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
            var p1 = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));
            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(p0, false, false);
                ctx.ArcTo(p1, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            return geo;
        }
    }

    /// <summary>最近若干次电量的迷你折线图。</summary>
    public sealed class Sparkline : FrameworkElement
    {
        private List<int> _values = new List<int>();

        public Sparkline()
        {
            Height = 30;
            MinWidth = 80;
            IsHitTestVisible = false;
        }

        public void SetValues(IEnumerable<int> values, int currentLevel)
        {
            _values = values == null ? new List<int>() : new List<int>(values);
            _level = currentLevel;
            InvalidateVisual();
        }

        private int _level = -1;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 2 || h <= 2) return;

            var color = Theme.LevelColor(_level).Color;
            var baseline = new Pen(new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)60 : (byte)40, color.R, color.G, color.B)), 1)
            { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };

            if (_values.Count < 2)
            {
                dc.DrawLine(baseline, new Point(0, h - 6), new Point(w, h - 6));
                return;
            }

            // 自动缩放纵轴：让相邻读数的变化也能看出来（否则 30%→12% 只是一条平线）
            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in _values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            double lo = min - 4;
            double hi = max + 4;
            if (hi - lo < 12)
            {
                double mid = (min + max) / 2.0;
                lo = mid - 6;
                hi = mid + 6;
            }
            if (lo < -4) lo = -4;
            if (hi > 104) hi = 104;
            if (hi - lo < 8) hi = lo + 8;

            var pts = new List<Point>();
            for (int i = 0; i < _values.Count; i++)
            {
                double x = _values.Count == 1 ? w / 2 : w * i / (_values.Count - 1.0);
                double norm = (_values[i] - lo) / (hi - lo);
                double y = h - 3 - (h - 8) * Math.Max(0, Math.Min(1, norm));
                pts.Add(new Point(x, y));
            }

            var geo = new StreamGeometry();
            using (StreamGeometryContext ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, h), true, true);
                ctx.LineTo(pts[0], true, false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, false);
                ctx.LineTo(new Point(pts[pts.Count - 1].X, h), true, false);
            }
            geo.Freeze();
            var fill = new LinearGradientBrush(Color.FromArgb(64, color.R, color.G, color.B), Color.FromArgb(4, color.R, color.G, color.B), 90);
            dc.DrawGeometry(fill, null, geo);

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i], true, false);
            }
            line.Freeze();
            var pen = new Pen(new SolidColorBrush(color), 1.9) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, line);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(Theme.IsDark ? (byte)46 : (byte)30, color.R, color.G, color.B)), 1), new Point(0, h - 1), new Point(w, h - 1));
            dc.DrawEllipse(new SolidColorBrush(color), null, pts[pts.Count - 1], 2.2, 2.2);
        }
    }
}
