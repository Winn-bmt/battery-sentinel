using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BatterySentinel.UI
{
    /// <summary>界面基础件：样式、文字、卡片、按钮、图标、弹出菜单。</summary>
    public static class Ui
    {
        public const string IconGear = "\uE713";
        public const string IconRefresh = "\uE72C";
        public const string IconClose = "\uE8BB";
        public const string IconMinimize = "\uE921";
        public const string IconChevronDown = "\uE70D";
        public const string IconChevronRight = "\uE76C";
        public const string IconWarning = "\uE7BA";
        public const string IconCheck = "\uE73E";
        public const string IconMore = "\uE712";
        public const string IconDelete = "\uE74D";
        public const string IconBluetooth = "\uE702";
        public const string IconSearch = "\uE721";
        public const string IconPlay = "\uE768";
        public const string IconBack = "\uE72B";
        public const string IconBug = "\uEBE8";
        public const string IconSave = "\uE74E";
        public const string IconInfo = "\uE946";
        public const string IconPower = "\uE7E8";
        public const string IconHourglass = "\uE823";

        public static void Init()
        {
            Application app = Application.Current;
            if (app == null) return;
            ResourceDictionary r = app.Resources;

            r["BtnPrimary"] = ButtonStyle(@"
                <Border x:Name='bd' CornerRadius='6' Background='{DynamicResource Accent}' Padding='14,7' SnapsToDevicePixels='True'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='White'/>
                </Border>",
                @"
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource AccentHover}'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.76'/></Trigger>
                <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>");

            r["BtnSecondary"] = ButtonStyle(@"
                <Border x:Name='bd' CornerRadius='6' Background='{DynamicResource Card}' BorderBrush='{DynamicResource Stroke}' BorderThickness='1' Padding='14,7' SnapsToDevicePixels='True'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource Text}'/>
                </Border>",
                @"
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource CardHover}'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.76'/></Trigger>
                <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>");

            r["BtnGhost"] = ButtonStyle(@"
                <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='12,7' SnapsToDevicePixels='True'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource Text2}'/>
                </Border>",
                @"
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/><Setter Property='TextElement.Foreground' Value='{DynamicResource Text}'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.7'/></Trigger>
                <Trigger Property='IsEnabled' Value='False'><Setter TargetName='bd' Property='Opacity' Value='0.4'/></Trigger>");

            r["BtnIcon"] = ButtonStyle(@"
                <Border x:Name='bd' CornerRadius='6' Background='Transparent' SnapsToDevicePixels='True'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>",
                @"
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.7'/></Trigger>");

            r["BtnClose"] = ButtonStyle(@"
                <Border x:Name='bd' CornerRadius='6' Background='Transparent' SnapsToDevicePixels='True'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>",
                @"
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#E81123'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.8'/></Trigger>");

            r["MenuPanel"] = MenuPanelStyle();
            r["CtxMenu"] = ContextMenuStyle();
            r["CtxItem"] = ContextMenuItemStyle(false);
            r["CtxItemDanger"] = ContextMenuItemStyle(true);
            r[typeof(ScrollBar)] = ThinScrollBarStyle();
        }

        /// <summary>细滚动条（深色/浅色都适配），避免默认样式在深色界面里冒出白色条。</summary>
        private static Style ThinScrollBarStyle()
        {
            string xml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/>
  <Setter Property='MinWidth' Value='10'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False' IsTabStop='False'/>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False' IsTabStop='False'/>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='thumb' Background='{DynamicResource ScrollThumb}' CornerRadius='3' Margin='2,2,2,2'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='thumb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)XamlReader.Parse(xml);
        }

        /// <summary>强调色小胶囊（2.4G / 蓝牙）。</summary>
        public static Border AccentChip(string text)
        {
            Color c = Theme.AccentColor;
            Color lighter = Color.FromRgb(
                (byte)Math.Min(255, c.R * 0.55 + 255 * 0.45),
                (byte)Math.Min(255, c.G * 0.55 + 255 * 0.45),
                (byte)Math.Min(255, c.B * 0.55 + 255 * 0.45));
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, c.R, c.G, c.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt(text, 10.5, new SolidColorBrush(lighter), semibold: true)
            };
        }

        private static Style ButtonStyle(string body, string triggers)
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate("Button", body, triggers)));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, Theme.Font));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            return s;
        }

        private static ControlTemplate ParseTemplate(string targetType, string body, string triggers)
        {
            string xml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='" + targetType + "'>" +
                body +
                (string.IsNullOrEmpty(triggers) ? "" : "<ControlTemplate.Triggers>" + triggers + "</ControlTemplate.Triggers>") +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xml);
        }

        private static Style MenuPanelStyle()
        {
            var s = new Style(typeof(Border));
            s.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Card));
            s.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(8)));
            s.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Stroke));
            s.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
            return s;
        }

        private static Style ContextMenuStyle()
        {
            var s = new Style(typeof(ContextMenu));
            s.Setters.Add(new Setter(ContextMenu.BackgroundProperty, Theme.Card));
            s.Setters.Add(new Setter(ContextMenu.ForegroundProperty, Theme.Text));
            s.Setters.Add(new Setter(ContextMenu.BorderBrushProperty, Theme.Stroke));
            s.Setters.Add(new Setter(ContextMenu.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(ContextMenu.PaddingProperty, new Thickness(4)));
            s.Setters.Add(new Setter(ContextMenu.FontFamilyProperty, Theme.Font));
            s.Setters.Add(new Setter(ContextMenu.FontSizeProperty, 12.5));
            return s;
        }

        private static Style ContextMenuItemStyle(bool danger)
        {
            var s = new Style(typeof(MenuItem));
            s.Setters.Add(new Setter(MenuItem.ForegroundProperty, danger ? Theme.Red : Theme.Text));
            s.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(10, 6, 14, 6)));
            s.Setters.Add(new Setter(MenuItem.FontFamilyProperty, Theme.Font));
            s.Setters.Add(new Setter(MenuItem.FontSizeProperty, 12.5));
            s.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
            var trigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            trigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, Theme.Hover));
            s.Triggers.Add(trigger);
            return s;
        }

        // ---------------- 文字 ----------------
        public static TextBlock Txt(string text, double size, Brush fg, bool bold = false, bool semibold = false)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg,
                FontFamily = Theme.Font,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (bold) t.FontWeight = FontWeights.Bold;
            else if (semibold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        public static TextBlock Muted(string text, double size = 11.5)
        {
            return Txt(text, size, Theme.Text3);
        }

        public static TextBlock Glyph(string glyph, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = glyph,
                FontSize = size,
                Foreground = fg,
                FontFamily = Theme.IconFont,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        // ---------------- 布局 ----------------
        public static StackPanel Col(double spacing, params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Vertical };
            foreach (UIElement c in children)
            {
                if (c == null) continue;
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(0, p.Children.Count == 0 ? 0 : spacing, 0, 0);
                p.Children.Add(c);
            }
            return p;
        }

        public static StackPanel Row(double spacing, params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (UIElement c in children)
            {
                if (c == null) continue;
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null) fe.Margin = new Thickness(p.Children.Count == 0 ? 0 : spacing, 0, 0, 0);
                p.Children.Add(c);
            }
            return p;
        }

        public static Grid Fill(UIElement child)
        {
            var g = new Grid();
            if (child != null) g.Children.Add(child);
            return g;
        }

        public static Border Card(double radius = 12)
        {
            return new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                SnapsToDevicePixels = true
            };
        }

        public static Border Divider()
        {
            return new Border { Height = 1, Background = Theme.StrokeSoft, Margin = new Thickness(0, 2, 0, 2) };
        }

        public static void Shadow(FrameworkElement el, double blur = 22, double opacity = 0.32, double depth = 6)
        {
            el.Effect = new DropShadowEffect
            {
                BlurRadius = blur,
                ShadowDepth = depth,
                Opacity = opacity,
                Color = Colors.Black,
                RenderingBias = RenderingBias.Quality
            };
        }

        // ---------------- 控件 ----------------
        public static Button Button(string text, bool accent, Action onClick)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.Resources[accent ? "BtnPrimary" : "BtnSecondary"] };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 32, double glyphSize = 14)
        {
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["BtnIcon"],
                Content = Glyph(glyph, glyphSize, Theme.Text2),
                Width = size,
                Height = size,
                ToolTip = tooltip
            };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static Button CloseButton(string glyph, Action onClick)
        {
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["BtnClose"],
                Content = Glyph(glyph, 12, Theme.Text),
                Width = 38,
                Height = 30
            };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static Border Chip(string text, Brush fg, Brush bg, double fontSize = 11)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Txt(text, fontSize, fg)
            };
        }

        public static Border Dot(Brush color, double size = 6)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = color,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>小电池图形（横向），用于标题/提示条/弹窗。</summary>
        public static Grid BatteryGlyph(double width, int level, Brush stroke, Brush fill)
        {
            double h = Math.Max(8, width * 0.48);
            var g = new Grid { Width = width, Height = h, VerticalAlignment = VerticalAlignment.Center };
            var body = new Border
            {
                BorderBrush = stroke,
                BorderThickness = new Thickness(Math.Max(1, width / 12)),
                CornerRadius = new CornerRadius(Math.Max(1.5, width / 12)),
                Margin = new Thickness(0, 0, Math.Max(2, width * 0.09), 0)
            };
            if (level >= 0)
            {
                double pct = Math.Max(0, Math.Min(1, level / 100.0));
                var inner = new Border
                {
                    Background = fill,
                    CornerRadius = new CornerRadius(Math.Max(1, width / 24)),
                    Margin = new Thickness(Math.Max(1.5, width / 16), Math.Max(1.5, width / 16),
                                           Math.Max(3, width * 0.09) + Math.Max(1.5, width / 16),
                                           Math.Max(1.5, width / 16)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(0.5, (width - Math.Max(3, width * 0.09) - Math.Max(1.5, width / 16) * 2) * pct)
                };
                body.Child = inner;
            }
            g.Children.Add(body);
            var tip = new Border
            {
                Width = Math.Max(2, width * 0.055),
                Height = h * 0.4,
                CornerRadius = new CornerRadius(1),
                Background = stroke,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(tip);
            return g;
        }

        // ---------------- 弹出菜单 ----------------
        public sealed class MenuItemModel
        {
            public string Text = "";
            public string Glyph = "";
            public Action Action;
            public bool Selected;
            public bool IsSeparator;
            public bool Danger;

            public static MenuItemModel Item(string text, Action action, string glyph = "", bool selected = false)
            {
                return new MenuItemModel { Text = text, Action = action, Glyph = glyph, Selected = selected };
            }

            public static MenuItemModel Sep()
            {
                return new MenuItemModel { IsSeparator = true };
            }
        }

        public static void ShowMenu(UIElement anchor, IEnumerable<MenuItemModel> items, double minWidth = 168)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (MenuItemModel it in items)
            {
                if (it.IsSeparator)
                {
                    panel.Children.Add(new Border { Height = 1, Background = Theme.StrokeSoft, Margin = new Thickness(6, 4, 6, 4) });
                    continue;
                }
                var row = new Grid { Height = 32, MinWidth = minWidth };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                if (!string.IsNullOrEmpty(it.Glyph))
                {
                    var ic = Glyph(it.Glyph, 12, Theme.Text2);
                    Grid.SetColumn(ic, 0);
                    row.Children.Add(ic);
                }
                var tx = Txt(it.Text, 12.5, it.Danger ? Theme.Red : Theme.Text);
                Grid.SetColumn(tx, 1);
                row.Children.Add(tx);
                if (it.Selected)
                {
                    var ck = Glyph(IconCheck, 11, Theme.Accent);
                    Grid.SetColumn(ck, 2);
                    row.Children.Add(ck);
                }
                var hover = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    Child = row,
                    Padding = new Thickness(2, 0, 2, 0),
                    Cursor = Cursors.Hand
                };
                MenuItemModel captured = it;
                hover.MouseEnter += (s, e) => hover.Background = Theme.Hover;
                hover.MouseLeave += (s, e) => hover.Background = Brushes.Transparent;
                hover.MouseLeftButtonUp += (s, e) =>
                {
                    if (captured.Action != null) captured.Action();
                };
                panel.Children.Add(hover);
            }

            var host = new Border
            {
                Background = Theme.Card,
                BorderBrush = Theme.Stroke,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = panel,
                MinWidth = minWidth + 8
            };
            Shadow(host, 26, Theme.IsDark ? 0.55 : 0.2, 6);

            var popup = new Popup
            {
                Child = host,
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = 4
            };
            // 统一关闭引用
            foreach (object o in panel.Children)
            {
                var b = o as Border;
                if (b != null) b.MouseLeftButtonUp += (s, e) => popup.IsOpen = false;
            }
            popup.IsOpen = true;
        }

        // ---------------- 动画 ----------------
        public static bool AnimationsEnabled = true;

        public static void FadeIn(UIElement el, double ms = 180)
        {
            if (!AnimationsEnabled) { el.Opacity = 1; return; }
            el.Opacity = 0;
            var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static void FadeOutAndRemove(UIElement el, Action done, double ms = 160)
        {
            var a = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (s, e) => { if (done != null) done(); };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static void SlideFadeIn(UIElement el, double fromX = 0, double fromY = 16, double ms = 260)
        {
            var tt = new TranslateTransform(fromX, fromY);
            el.RenderTransform = tt;
            el.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        }

        public static void HoverBorder(Border b, Brush normal, Brush hover)
        {
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = normal;
        }
    }
}
