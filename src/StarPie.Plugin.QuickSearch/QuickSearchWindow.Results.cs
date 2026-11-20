using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StarPie.Plugin.QuickSearch;

internal sealed partial class QuickSearchWindow
{
    // Shared frozen drawings never consult a file association or load shell thumbnails.
    private static readonly Dictionary<string, Geometry> Glyphs = new Dictionary<string, string>
    {
        ["search"] = "M14.8,14.8 L21,21 M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10",
        ["scope"] = "M3,7 L10,7 12,10 21,10 21,19 3,19 Z M4,5 L11,5 13,7 20,7",
        ["pin"] = "M9,3 L19,8 16,9 14,14 15,17 6,13 9,11 11,6 Z M10,15 L6,22",
        ["close"] = "M6,6 L18,18 M18,6 L6,18",
        ["all"] = "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z",
        ["apps"] = "M3,4 H21 V17 H3 Z M8,21 H16 M12,17 V21",
        ["cad"] = "M12,2 L21,7 21,17 12,22 3,17 3,7 Z M3,7 L12,12 21,7 M12,12 V22",
        ["docs"] = "M5,2 H14 L19,7 V22 H5 Z M14,2 V7 H19 M8,12 H16 M8,16 H15",
        ["video"] = "M3,5 H21 V19 H3 Z M10,9 L15,12 10,15 Z",
        ["folders"] = "M3,6 H9 L11,8 H21 V19 H3 Z",
        ["system"] = "M4,5 H20 V9 H4 Z M4,15 H20 V19 H4 Z M7,7 H8 M7,17 H8",
        ["file"] = "M5,2 H14 L19,7 V22 H5 Z M14,2 V7 H19",
        ["taskmgr"] = "M3,4 H21 V20 H3 Z M5,15 H8 L10,10 13,17 16,8 18,12 H20",
        ["control"] = "M3,7 H7 M11,7 H21 M3,17 H13 M17,17 H21 M7,4 H11 V10 H7 Z M13,14 H17 V20 H13 Z",
        ["calc"] = "M5,2 H19 V22 H5 Z M8,5 H16 V9 H8 Z M8,12 H10 M14,12 H16 M8,15 H10 M14,15 H16 M8,18 H10 M14,18 H16",
        ["open"] = "M13,4 H20 V11 M20,4 L10,14 M9,6 H4 V20 H18 V15",
        ["locate"] = "M3,6 H9 L11,8 H21 V19 H3 Z M11,11 L14,14 11,17",
        ["runAdmin"] = "M12,2 L20,5 V11 C20,16 17,19 12,22 C7,19 4,16 4,11 V5 Z M12,6 V17",
        ["copyPath"] = "M9,6 H20 V21 H9 Z M15,3 H5 V16",
        ["copyName"] = "M4,5 H20 V19 H4 Z M7,9 H17 M7,13 H13",
        ["previous"] = "M15,5 L8,12 15,19",
        ["next"] = "M9,5 L16,12 9,19"
    }.ToDictionary(pair => pair.Key, pair =>
    {
        var drawing = Geometry.Parse(pair.Value);
        drawing.Freeze();
        return drawing;
    });

    private static Viewbox DrawGlyph(string key, double size)
    {
        var path = new System.Windows.Shapes.Path { Data = Glyphs[key], StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding
        {
            RelativeSource = System.Windows.Data.RelativeSource.Self,
            Path = new PropertyPath(TextElement.ForegroundProperty)
        });
        var art = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = art, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center };
    }

    private static string ResultCategory(SearchRow row)
    {
        if (row.IsDirectory) return SearchCategories.Folders;
        if (!row.FromSelectedRoots) return SearchCategories.SystemTools;
        var entry = FolderEntry.Split(row.Path, false);
        if (SearchCategories.Matches(SearchCategories.SystemTools, entry)) return SearchCategories.SystemTools;
        foreach (string key in FilterKeys)
            if (key != SearchCategories.All && key != SearchCategories.SystemTools && SearchCategories.Matches(key, entry))
                return key;
        return "file";
    }

    private static string ResultGlyph(SearchRow row, string category)
    {
        if (category != SearchCategories.SystemTools) return category;
        return System.IO.Path.GetFileName(row.Path).ToLowerInvariant() switch
        {
            "taskmgr.exe" => "taskmgr",
            "control.exe" => "control",
            "calc.exe" => "calc",
            "explorer.exe" => "folders",
            _ => category
        };
    }

    private static TextBlock MatchText(string text, string query, double size)
    {
        var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = size };
        if (string.IsNullOrEmpty(query))
        {
            line.Text = text;
            return line;
        }
        int start = 0;
        int match;
        while ((match = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (match > start) line.Inlines.Add(new Run(text[start..match]));
            line.Inlines.Add(new Run(text.Substring(match, query.Length))
            {
                FontWeight = FontWeights.Bold,
                TextDecorations = TextDecorations.Underline
            });
            start = match + query.Length;
        }
        if (start < text.Length) line.Inlines.Add(new Run(text[start..]));
        return line;
    }

    private void AddResult(SearchRow row)
    {
        string category = ResultCategory(row);
        string glyph = ResultGlyph(row, category);
        var item = new ListBoxItem { Tag = row, ToolTip = row.Path, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, row.Name + " " + row.Path);
        var body = new DockPanel();
        var tile = new Border { Name = "ResultIcon", Width = 35, Height = 35, CornerRadius = new CornerRadius(9),
            Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Child = DrawGlyph(glyph, 21) };
        tile.SetResourceReference(Border.BackgroundProperty, "QuickSearch.Tile." + glyph);
        tile.SetResourceReference(TextElement.ForegroundProperty, "QuickSearch.Ink." + glyph);
        DockPanel.SetDock(tile, Dock.Left);
        body.Children.Add(tile);

        var open = new Button { Name = "RowOpen", Style = (Style)FindResource("QuickSearch.RowOpen"), Content = DrawGlyph("open", 15),
            Width = 29, Height = 29, VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = _context.Info.HasCapability(PluginCapability.Process) };
        LabelControl(open, T("open"));
        open.Click += (_, e) => { e.Handled = true; ExecuteResultCommand("open", row); };
        DockPanel.SetDock(open, Dock.Right);
        body.Children.Add(open);
        var badge = new Border { Name = "TypeBadge", CornerRadius = new CornerRadius(8),
            Margin = new Thickness(5, 0, 7, 0), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = T(category), FontSize = 11, FontWeight = FontWeights.Medium } };
        badge.SetResourceReference(Border.BackgroundProperty, "QuickSearch.Tile." + category);
        badge.SetResourceReference(TextElement.ForegroundProperty, "QuickSearch.Ink." + category);
        DockPanel.SetDock(badge, Dock.Right);
        body.Children.Add(badge);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = row.Path };
        string query = _input.Text.Trim();
        var name = MatchText(row.Name, query, 14);
        name.FontWeight = FontWeights.SemiBold;
        var path = MatchText(row.Path, query, 11);
        path.Margin = new Thickness(0, 2, 0, 0);
        path.Style = (Style)FindResource("QuickSearch.PathText");
        lines.Children.Add(name);
        lines.Children.Add(path);
        body.Children.Add(lines);
        item.Content = body;
        _list.Items.Add(item);
    }
}
