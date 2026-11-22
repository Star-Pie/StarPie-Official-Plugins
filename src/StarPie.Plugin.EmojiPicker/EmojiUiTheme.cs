using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using BorderControl = System.Windows.Controls.Border;

namespace StarPie.Plugin.EmojiPicker;

internal sealed class EmojiUiTheme
{
    internal bool IsDark { get; }
    internal SolidColorBrush WindowBg { get; }
    internal SolidColorBrush Surface { get; }
    internal SolidColorBrush SurfaceHover { get; }
    internal SolidColorBrush SurfaceActive { get; }
    internal SolidColorBrush InputBg { get; }
    internal SolidColorBrush Border { get; }
    internal SolidColorBrush TextPrimary { get; }
    internal SolidColorBrush TextSecondary { get; }
    internal SolidColorBrush TextMuted { get; }
    internal SolidColorBrush Accent { get; }
    internal SolidColorBrush AccentFg { get; }
    internal SolidColorBrush AccentSoft { get; }
    internal SolidColorBrush StickerOutline { get; }
    internal SolidColorBrush TagOverlayBg { get; }
    internal SolidColorBrush FavoriteDot { get; }
    internal SolidColorBrush Danger { get; }
    internal SolidColorBrush DangerSoft { get; }

    private EmojiUiTheme(InputPanelAppearance p)
    {
        Color bg = ParseColor(p.Background, Color.FromRgb(0x25, 0x25, 0x28));
        Color surf = ParseColor(p.Surface, Color.FromRgb(0x1C, 0x1C, 0x1F));
        Color fg = ParseColor(p.Foreground, Color.FromRgb(0xF2, 0xF2, 0xF5));
        Color brd = ParseColor(p.Border, Color.FromRgb(0x3C, 0x3C, 0x40));
        Color acc = ParseColor(p.Accent, Color.FromRgb(0x38, 0xBD, 0xF8));
        Color accFg = ParseColor(p.AccentForeground, Colors.White);

        double lum = (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255.0;
        IsDark = lum < 0.5;

        // If host dark theme sends near-black #111/#1A, tune card to sleek charcoal similar to modern Windows picker
        WindowBg = FreezeBrush(bg);
        InputBg = FreezeBrush(surf);
        Surface = FreezeBrush(IsDark ? Blend(surf, Colors.White, 0.04) : Blend(bg, Colors.Black, 0.03));
        SurfaceHover = FreezeBrush(IsDark ? Blend(bg, Colors.White, 0.11) : Blend(bg, Colors.Black, 0.08));
        SurfaceActive = FreezeBrush(IsDark ? Blend(bg, Colors.White, 0.17) : Blend(acc, bg, 0.85));
        Border = FreezeBrush(brd);
        TextPrimary = FreezeBrush(fg);
        TextSecondary = FreezeBrush(Blend(fg, bg, 0.28));
        TextMuted = FreezeBrush(Blend(fg, bg, 0.45));
        Accent = FreezeBrush(acc);
        AccentFg = FreezeBrush(accFg);
        AccentSoft = FreezeBrush(Color.FromArgb(IsDark ? (byte)46 : (byte)32, acc.R, acc.G, acc.B));
        StickerOutline = FreezeBrush(IsDark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(24, 15, 23, 42));
        TagOverlayBg = FreezeBrush(Color.FromArgb(165, 10, 10, 12));
        FavoriteDot = FreezeBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        Danger = FreezeBrush(IsDark ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0xDC, 0x26, 0x26));
        DangerSoft = FreezeBrush(IsDark ? Color.FromArgb(36, 248, 113, 113) : Color.FromArgb(26, 220, 38, 38));
    }

    internal static EmojiUiTheme From(InputPanelAppearance appearance) => new(appearance ?? new InputPanelAppearance());

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return fallback;
    }

    private static Color Blend(Color a, Color b, double bRatio)
    {
        double aRatio = 1.0 - bRatio;
        return Color.FromArgb(
            255,
            (byte)Math.Clamp((int)Math.Round(a.R * aRatio + b.R * bRatio), 0, 255),
            (byte)Math.Clamp((int)Math.Round(a.G * aRatio + b.G * bRatio), 0, 255),
            (byte)Math.Clamp((int)Math.Round(a.B * aRatio + b.B * bRatio), 0, 255));
    }

    private static SolidColorBrush FreezeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    internal void ApplyTo(Window window)
    {
        window.Background = WindowBg;
        window.Foreground = TextPrimary;
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        window.FontSize = 12;

        ApplyControlStyles(window.Resources);
    }

    internal void ApplyControlStyles(ResourceDictionary resources)
    {
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
        buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        buttonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        buttonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty, RoundedButtonTemplate(7)));
        resources[typeof(Button)] = buttonStyle;

        var tbStyle = new Style(typeof(TextBox));
        tbStyle.Setters.Add(new Setter(Control.BackgroundProperty, InputBg));
        tbStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
        tbStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, TextPrimary));
        tbStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        tbStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        tbStyle.Setters.Add(new Setter(Control.TemplateProperty, TextBoxTemplate(7)));
        resources[typeof(TextBox)] = tbStyle;

        var comboStyle = new Style(typeof(ComboBox));
        comboStyle.Setters.Add(new Setter(Control.BackgroundProperty, InputBg));
        comboStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
        comboStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Border));
        comboStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        comboStyle.Setters.Add(new Setter(Control.TemplateProperty, ComboBoxTemplate()));
        resources[typeof(ComboBox)] = comboStyle;

        var scrollStyle = new Style(typeof(ScrollBar));
        scrollStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        scrollStyle.Setters.Add(new Setter(Control.ForegroundProperty, Border));
        scrollStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        scrollStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 6.0));
        scrollStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 6.0));
        scrollStyle.Setters.Add(new Setter(Control.TemplateProperty, SlimScrollBarTemplate(vertical: true)));

        var horizTrigger = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
        horizTrigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
        horizTrigger.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
        horizTrigger.Setters.Add(new Setter(FrameworkElement.HeightProperty, 6.0));
        horizTrigger.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 6.0));
        horizTrigger.Setters.Add(new Setter(Control.TemplateProperty, SlimScrollBarTemplate(vertical: false)));
        scrollStyle.Triggers.Add(horizTrigger);

        var scrollHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        scrollHover.Setters.Add(new Setter(Control.ForegroundProperty, TextMuted));
        scrollStyle.Triggers.Add(scrollHover);

        resources[typeof(ScrollBar)] = scrollStyle;
    }

    private static ControlTemplate SlimScrollBarTemplate(bool vertical)
    {
        string dirRev = vertical ? "True" : "False";
        string decCmd = vertical ? "ScrollBar.PageUpCommand" : "ScrollBar.PageLeftCommand";
        string incCmd = vertical ? "ScrollBar.PageDownCommand" : "ScrollBar.PageRightCommand";
        string margin = vertical ? "1,2,1,2" : "2,1,2,1";
        string xaml = $@"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
            xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
            TargetType=""ScrollBar"">
            <Grid Background=""Transparent"" SnapsToDevicePixels=""True"">
                <Track x:Name=""PART_Track"" IsDirectionReversed=""{dirRev}"">
                    <Track.DecreaseRepeatButton>
                        <RepeatButton Command=""{decCmd}"" Focusable=""False"" Opacity=""0"" IsTabStop=""False""/>
                    </Track.DecreaseRepeatButton>
                    <Track.Thumb>
                        <Thumb Focusable=""False"">
                            <Thumb.Template>
                                <ControlTemplate TargetType=""Thumb"">
                                    <Border CornerRadius=""3"" Margin=""{margin}""
                                        Background=""{{Binding Foreground, RelativeSource={{RelativeSource AncestorType=ScrollBar}}}}""/>
                                </ControlTemplate>
                            </Thumb.Template>
                        </Thumb>
                    </Track.Thumb>
                    <Track.IncreaseRepeatButton>
                        <RepeatButton Command=""{incCmd}"" Focusable=""False"" Opacity=""0"" IsTabStop=""False""/>
                    </Track.IncreaseRepeatButton>
                </Track>
            </Grid>
        </ControlTemplate>";
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    internal ControlTemplate FlatTileTemplate(double cornerRadius, SolidColorBrush? hoverBrush = null)
    {
        var template = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(BorderControl), "bd");
        bd.SetValue(BorderControl.CornerRadiusProperty, new CornerRadius(cornerRadius));
        bd.SetBinding(BorderControl.BackgroundProperty, Parent("Background"));
        bd.SetBinding(BorderControl.BorderBrushProperty, Parent("BorderBrush"));
        bd.SetBinding(BorderControl.BorderThicknessProperty, Parent("BorderThickness"));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(FrameworkElement.MarginProperty, Parent("Padding"));
        presenter.SetBinding(FrameworkElement.HorizontalAlignmentProperty, Parent("HorizontalContentAlignment"));
        presenter.SetBinding(FrameworkElement.VerticalAlignmentProperty, Parent("VerticalContentAlignment"));
        bd.AppendChild(presenter);

        template.VisualTree = bd;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BorderControl.BackgroundProperty, hoverBrush ?? SurfaceHover, "bd"));
        template.Triggers.Add(hover);

        return template;
    }

    private ControlTemplate RoundedButtonTemplate(double radius)
    {
        var template = new ControlTemplate(typeof(Button));
        var frame = new FrameworkElementFactory(typeof(BorderControl), "frame");
        frame.SetValue(BorderControl.CornerRadiusProperty, new CornerRadius(radius));
        frame.SetBinding(BorderControl.BackgroundProperty, Parent("Background"));
        frame.SetBinding(BorderControl.BorderBrushProperty, Parent("BorderBrush"));
        frame.SetBinding(BorderControl.BorderThicknessProperty, Parent("BorderThickness"));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, Parent("Content"));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, Parent("ContentTemplate"));
        content.SetBinding(FrameworkElement.MarginProperty, Parent("Padding"));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, Parent("HorizontalContentAlignment"));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        template.VisualTree = frame;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BorderControl.BackgroundProperty, SurfaceHover, "frame"));
        hover.Setters.Add(new Setter(BorderControl.BorderBrushProperty, Accent, "frame"));
        template.Triggers.Add(hover);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
        template.Triggers.Add(disabled);

        return template;
    }

    private ControlTemplate TextBoxTemplate(double radius)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var frame = new FrameworkElementFactory(typeof(BorderControl), "frame");
        frame.SetValue(BorderControl.CornerRadiusProperty, new CornerRadius(radius));
        frame.SetBinding(BorderControl.BackgroundProperty, Parent("Background"));
        frame.SetBinding(BorderControl.BorderBrushProperty, Parent("BorderBrush"));
        frame.SetBinding(BorderControl.BorderThicknessProperty, Parent("BorderThickness"));

        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 4, 7, 4));
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(host);
        template.VisualTree = frame;

        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(BorderControl.BorderBrushProperty, Accent, "frame"));
        template.Triggers.Add(focus);

        return template;
    }

    private ControlTemplate ComboBoxTemplate()
    {
        var template = new ControlTemplate(typeof(ComboBox));
        var grid = new FrameworkElementFactory(typeof(Grid));

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetValue(ContentControl.ContentProperty, "▾");
        toggle.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right);
        toggle.SetValue(Control.PaddingProperty, new Thickness(8, 4, 8, 4));
        toggle.SetBinding(Control.BackgroundProperty, Parent("Background"));
        toggle.SetBinding(Control.ForegroundProperty, Parent("Foreground"));
        toggle.SetBinding(Control.BorderBrushProperty, Parent("BorderBrush"));
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(1));

        var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
        var tFrame = new FrameworkElementFactory(typeof(BorderControl), "frame");
        tFrame.SetValue(BorderControl.CornerRadiusProperty, new CornerRadius(7));
        tFrame.SetBinding(BorderControl.BackgroundProperty, Parent("Background"));
        tFrame.SetBinding(BorderControl.BorderBrushProperty, Parent("BorderBrush"));
        tFrame.SetBinding(BorderControl.BorderThicknessProperty, Parent("BorderThickness"));
        var tContent = new FrameworkElementFactory(typeof(ContentPresenter));
        tContent.SetBinding(FrameworkElement.MarginProperty, Parent("Padding"));
        tContent.SetBinding(FrameworkElement.HorizontalAlignmentProperty, Parent("HorizontalContentAlignment"));
        tContent.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        tFrame.AppendChild(tContent);
        toggleTemplate.VisualTree = tFrame;
        toggle.SetValue(Control.TemplateProperty, toggleTemplate);

        var openBind = Parent("IsDropDownOpen");
        openBind.Mode = BindingMode.TwoWay;
        toggle.SetBinding(ToggleButton.IsCheckedProperty, openBind);
        grid.AppendChild(toggle);

        var sel = new FrameworkElementFactory(typeof(ContentPresenter));
        sel.SetValue(UIElement.IsHitTestVisibleProperty, false);
        sel.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 4, 26, 4));
        sel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        sel.SetBinding(ContentPresenter.ContentProperty, Parent("SelectionBoxItem"));
        sel.SetBinding(ContentPresenter.ContentTemplateProperty, Parent("SelectionBoxItemTemplate"));
        grid.AppendChild(sel);

        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        var popOpen = Parent("IsDropDownOpen");
        popOpen.Mode = BindingMode.TwoWay;
        popup.SetBinding(Popup.IsOpenProperty, popOpen);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);

        var popBorder = new FrameworkElementFactory(typeof(BorderControl));
        popBorder.SetValue(BorderControl.BackgroundProperty, InputBg);
        popBorder.SetValue(BorderControl.BorderBrushProperty, Border);
        popBorder.SetValue(BorderControl.BorderThicknessProperty, new Thickness(1));
        popBorder.SetValue(BorderControl.CornerRadiusProperty, new CornerRadius(7));
        popBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
        popBorder.SetBinding(FrameworkElement.MinWidthProperty, Parent("ActualWidth"));

        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(FrameworkElement.MaxHeightProperty, 220.0);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        popBorder.AppendChild(scroll);
        popup.AppendChild(popBorder);
        grid.AppendChild(popup);

        template.VisualTree = grid;
        return template;
    }

    private static Binding Parent(string property) =>
        new(property) { RelativeSource = RelativeSource.TemplatedParent };

    private static readonly Dictionary<string, Geometry> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["search"] = FreezeGeom("M11,4 A7,7 0 1,1 4,11 A7,7 0 0,1 11,4 Z M16.2,16.2 L20.5,20.5"),
        ["pin"] = FreezeGeom("M12,17 L12,22 M8.5,11 L6,15 L18,15 L15.5,11 L15.5,5.5 L17,4 L7,4 L8.5,5.5 Z"),
        ["plus"] = FreezeGeom("M12,5 L12,19 M5,12 L19,12"),
        ["gear"] = FreezeGeom("M12,8.5 A3.5,3.5 0 1,1 8.5,12 A3.5,3.5 0 0,1 12,8.5 Z M12,2.5 L12,5 M12,19 L12,21.5 M2.5,12 L5,12 M19,12 L21.5,12 M5.2,5.2 L7,7 M17,17 L18.8,18.8 M18.8,5.2 L17,7 M7,17 L5.2,18.8"),
        ["close"] = FreezeGeom("M6,6 L18,18 M18,6 L6,18"),
        ["recent"] = FreezeGeom("M12,3 A9,9 0 1,1 3,12 A9,9 0 0,1 12,3 Z M12,7 L12,12 L15.5,14"),
        ["smileys"] = FreezeGeom("M12,3 A9,9 0 1,1 3,12 A9,9 0 0,1 12,3 Z M8.5,14.5 C9.5,16.2 14.5,16.2 15.5,14.5 M9,9.5 L9.01,9.5 M15,9.5 L15.01,9.5"),
        ["people"] = FreezeGeom("M12,3 L12,12 M8.5,5.5 L8.5,12 M15.5,5.5 L15.5,12 M5.5,9.5 L5.5,14 C5.5,18 8.5,20.5 12,20.5 C15.5,20.5 18.5,18 18.5,13.5 L18.5,8.5"),
        ["animals"] = FreezeGeom("M8,16.5 C8,14 9.8,12.2 12,12.2 C14.2,12.2 16,14 16,16.5 C16,18 14.5,19 12,19 C9.5,19 8,18 8,16.5 Z M7.5,8.5 A1.2,1.2 0 1,1 5.1,8.5 A1.2,1.2 0 0,1 7.5,8.5 Z M13.2,6.5 A1.2,1.2 0 1,1 10.8,6.5 A1.2,1.2 0 0,1 13.2,6.5 Z M18.9,8.5 A1.2,1.2 0 1,1 16.5,8.5 A1.2,1.2 0 0,1 18.9,8.5 Z"),
        ["food"] = FreezeGeom("M4,17.5 L20,17.5 M5.5,17.5 A6.5,6.5 0 0,1 18.5,17.5 M12,7.5 L12,11"),
        ["travel"] = FreezeGeom("M5,12 L7.2,7 L16.8,7 L19,12 M4,12 L20,12 L20,17 L4,17 Z M7.5,17.5 A1.2,1.2 0 1,1 7.4,17.5 M16.5,17.5 A1.2,1.2 0 1,1 16.4,17.5"),
        ["activities"] = FreezeGeom("M12,3 A9,9 0 1,1 3,12 A9,9 0 0,1 12,3 Z M12,8 L15.5,10.6 L14.2,14.7 L9.8,14.7 L8.5,10.6 Z"),
        ["objects"] = FreezeGeom("M9.5,18 L14.5,18 M10.2,20.8 L13.8,20.8 M12,3 A5.5,5.5 0 0,0 8.5,12.8 C9.2,13.4 9.5,14.2 9.5,15 L14.5,15 C14.5,14.2 14.8,13.4 15.5,12.8 A5.5,5.5 0 0,0 12,3 Z"),
        ["symbols"] = FreezeGeom("M12,19.5 C12,19.5 5,14.8 5,9.6 A3.6,3.6 0 0,1 12,7.8 A3.6,3.6 0 0,1 19,9.6 C19,14.8 12,19.5 12,19.5 Z"),
        ["customJump"] = FreezeGeom("M4,5 L20,5 L20,19 L4,19 Z M8.5,9.5 A1.2,1.2 0 1,1 8.4,9.5 M20,15 L15.5,10.5 L8.5,19")
    };

    private static Geometry FreezeGeom(string data)
    {
        Geometry g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    internal static System.Windows.Shapes.Path Icon(string key, Brush stroke, double size = 16, double strokeThickness = 1.6)
    {
        Geometry g = Icons.GetValueOrDefault(key, Icons["symbols"]);
        return new System.Windows.Shapes.Path
        {
            Data = g,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
    }

    internal static string ActionSvgData =>
        "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,20A8,8 0 1,1 20,12A8,8 0 0,1 12,20M9,10.5A1.25,1.25 0 1,1 7.75,9.25A1.25,1.25 0 0,1 9,10.5M16.25,10.5A1.25,1.25 0 1,1 15,9.25A1.25,1.25 0 0,1 16.25,10.5M12,17.2C14.3,17.2 16.2,15.8 17,13.8H7C7.8,15.8 9.7,17.2 12,17.2Z";
}
