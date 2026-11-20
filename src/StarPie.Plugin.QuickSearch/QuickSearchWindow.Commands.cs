using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace StarPie.Plugin.QuickSearch;

internal sealed partial class QuickSearchWindow
{
    private void WireResultCommands()
    {
        _menu = new ContextMenu();
        foreach (string command in new[] { "open", "locate", "runAdmin", "copyPath", "copyName" })
        {
            if (command == "copyPath") _menu.Items.Add(new Separator());
            var item = new MenuItem
            {
                Tag = command,
                Header = T(command),
                InputGestureText = command switch { "open" => "Enter", "locate" => "Shift+Enter", _ => "" }
            };
            // A live index refresh can replace the selection while the popup is open.
            // The menu command belongs to the row on which the popup was opened.
            item.Click += (_, _) =>
            {
                if (item.CommandParameter is SearchRow target) ExecuteResultCommand(command, target);
            };
            _menu.Items.Add(item);
        }
        StyleMenu(_menu);
        _list.ContextMenu = _menu;
        _list.ContextMenuOpening += (_, e) =>
        {
            var item = e.CursorLeft < 0 ? _list.SelectedItem as ListBoxItem : ResultAt(e.OriginalSource);
            if (!PrepareMenu(item)) e.Handled = true;
            else
            {
                _menu.PlacementTarget = item;
                _menu.Placement = e.CursorLeft < 0 ? PlacementMode.Bottom : PlacementMode.MousePoint;
            }
        };
        _list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (ResultAt(e.OriginalSource) is { } item) item.IsSelected = true;
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && ResultAt(e.OriginalSource) is { Tag: SearchRow row } &&
                !e.Handled && !IsRowButton(e.OriginalSource))
            {
                e.Handled = true;
                ExecuteResultCommand("open", row);
            }
        };
    }

    private ListBoxItem? ResultAt(object source) => source is DependencyObject element
        ? ItemsControl.ContainerFromElement(_list, element) as ListBoxItem : null;

    private static bool IsRowButton(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null && element is not ListBoxItem)
        {
            if (element is ButtonBase) return true;
            element = element is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private bool PrepareMenu(ListBoxItem? item)
    {
        if (_menu == null || item?.Tag is not SearchRow row || !_list.Items.Contains(item)) return false;
        item.IsSelected = true;
        foreach (var command in _menu.Items.OfType<MenuItem>())
        {
            var key = (string)command.Tag;
            command.CommandParameter = row;
            command.IsEnabled = _context.Info.HasCapability(key is "copyPath" or "copyName"
                ? PluginCapability.Clipboard : PluginCapability.Process) && (key != "runAdmin" || SupportsAdministrator(row));
        }
        return true;
    }

    private void OpenSelectedMenu()
    {
        if (_list.SelectedItem is not ListBoxItem item || !PrepareMenu(item)) return;
        _list.ScrollIntoView(item);
        _menu!.PlacementTarget = item;
        _menu.Placement = PlacementMode.Bottom;
        _menu.IsOpen = true;
    }

    private static bool SupportsAdministrator(SearchRow row)
    {
        if (row.IsDirectory || !Path.IsPathFullyQualified(row.Path) || Environment.ExpandEnvironmentVariables(row.Path) != row.Path)
            return false;
        var extension = Path.GetExtension(row.Path);
        return extension.ToUpperInvariant() is ".EXE" or ".COM" or ".CMD" or ".BAT" or ".LNK" or ".MSC";
    }
}
