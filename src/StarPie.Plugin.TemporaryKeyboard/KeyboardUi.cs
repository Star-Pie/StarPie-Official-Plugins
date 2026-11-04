using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace StarPie.Plugin.TemporaryKeyboard;

internal static class KeyboardUi
{
    internal static SolidColorBrush Brush(string color)
    { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }

    private static Binding Parent(string property) => new(property) { RelativeSource = RelativeSource.TemplatedParent };
    private static FrameworkElementFactory Frame()
    {
        var frame = new FrameworkElementFactory(typeof(Border), "frame");
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        frame.SetBinding(Border.BackgroundProperty, Parent("Background"));
        frame.SetBinding(Border.BorderBrushProperty, Parent("BorderBrush"));
        frame.SetBinding(Border.BorderThicknessProperty, Parent("BorderThickness"));
        return frame;
    }
    private static ControlTemplate ButtonTemplate(Type type, InputPanelAppearance palette)
    {
        var template = new ControlTemplate(type); var frame = Frame();
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, Parent("Content"));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, Parent("ContentTemplate"));
        content.SetBinding(FrameworkElement.MarginProperty, Parent("Padding"));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, Parent("HorizontalContentAlignment"));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content); template.VisualTree = frame;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(palette.Accent), "frame")); template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(palette.Accent), "frame"));
        focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "frame")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5)); template.Triggers.Add(disabled);
        return template;
    }
    internal static void Apply(Window window, InputPanelAppearance palette)
    {
        window.Background = Brush(palette.Background); window.Foreground = Brush(palette.Foreground);
        window.FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); window.FontSize = 12;
        foreach (Type type in new[] { typeof(Button), typeof(ComboBox), typeof(TextBox), typeof(ListBox) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(palette.Surface)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(palette.Foreground)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(palette.Border)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            if (type == typeof(Button)) style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(type, palette)));
            if (type == typeof(TextBox))
            {
                var template = new ControlTemplate(type); var frame = Frame();
                var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
                host.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));
                frame.AppendChild(host); template.VisualTree = frame;
                var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
                focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(palette.Accent), "frame")); template.Triggers.Add(focus);
                style.Setters.Add(new Setter(Control.TemplateProperty, template));
            }
            if (type == typeof(ComboBox)) style.Setters.Add(new Setter(Control.TemplateProperty, ComboTemplate(palette)));
            window.Resources[type] = style;
        }
    }
    private static ControlTemplate ComboTemplate(InputPanelAppearance palette)
    {
        var template = new ControlTemplate(typeof(ComboBox));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetValue(ContentControl.ContentProperty, "▾");
        toggle.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right);
        toggle.SetValue(Control.PaddingProperty, new Thickness(8));
        toggle.SetBinding(Control.BackgroundProperty, Parent("Background"));
        toggle.SetBinding(Control.ForegroundProperty, Parent("Foreground"));
        toggle.SetBinding(Control.BorderBrushProperty, Parent("BorderBrush"));
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(1));
        toggle.SetValue(Control.TemplateProperty, ButtonTemplate(typeof(ToggleButton), palette));
        var open = Parent("IsDropDownOpen"); open.Mode = BindingMode.TwoWay;
        toggle.SetBinding(ToggleButton.IsCheckedProperty, open); grid.AppendChild(toggle);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(UIElement.IsHitTestVisibleProperty, false);
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 4, 30, 4));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetBinding(ContentPresenter.ContentProperty, Parent("SelectionBoxItem"));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, Parent("SelectionBoxItemTemplate")); grid.AppendChild(content);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        var popupOpen = Parent("IsDropDownOpen"); popupOpen.Mode = BindingMode.TwoWay;
        popup.SetBinding(Popup.IsOpenProperty, popupOpen);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom); popup.SetValue(Popup.AllowsTransparencyProperty, true);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brush(palette.Surface)); border.SetValue(Border.BorderBrushProperty, Brush(palette.Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0)); border.SetBinding(FrameworkElement.MinWidthProperty, Parent("ActualWidth"));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer)); scroll.SetValue(FrameworkElement.MaxHeightProperty, 250.0);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); border.AppendChild(scroll); popup.AppendChild(border); grid.AppendChild(popup);
        template.VisualTree = grid; return template;
    }
    internal static Button Button(string label) => new()
    { Content = label, Focusable = false, MinHeight = 32, HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(2) };
}
