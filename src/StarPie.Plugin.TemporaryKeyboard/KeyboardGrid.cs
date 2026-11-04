using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace StarPie.Plugin.TemporaryKeyboard;

internal static class KeyboardGrid
{
    internal static Button Key(string label, bool editor = false)
    {
        var button = KeyboardUi.Button("");
        button.Focusable = editor;
        button.MinWidth = 0; button.MinHeight = 0;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.ToolTip = label;
        return button;
    }
    internal static UniformGrid Custom(KeyScheme scheme, Func<int, KeyBinding?, Button> create)
    {
        var grid = new UniformGrid { Rows = scheme.Rows, Columns = scheme.Columns };
        for (int slot = 0; slot < scheme.Keys.Count; slot++) grid.Children.Add(create(slot, scheme.Keys[slot]));
        return grid;
    }
    internal static IReadOnlyDictionary<string, KeyboardKey> Template(bool uppercase = false) =>
        KeyboardLayouts.Create(KeyboardMode.Full, uppercase).SelectMany(row => row).ToDictionary(key => key.Id, StringComparer.Ordinal);
}
