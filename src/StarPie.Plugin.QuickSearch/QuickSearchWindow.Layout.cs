using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;

namespace StarPie.Plugin.QuickSearch;

internal sealed partial class QuickSearchWindow
{
    private readonly TextBox _input = new() { Name = "SearchInput", FontSize = 18, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _list = new() { Name = "ResultsList", HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _rootList = new() { MaxHeight = 55 };
    private readonly StackPanel _scope = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(14, 2, 14, 8) };
    private readonly TextBlock _help = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _keys = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _placeholder = new() { Name = "SearchPlaceholder", FontSize = 16, IsHitTestVisible = false,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
    private readonly Dictionary<string, ContentControl> _labels = new();
    private readonly Dictionary<string, TextBlock> _captions = new();
    private readonly Dictionary<string, Button> _categoryButtons = new();
    private Button _pin = null!, _previous = null!, _next = null!;
    private ContextMenu? _menu;
    private static readonly string[] FilterKeys = [SearchCategories.All, SearchCategories.Apps, SearchCategories.Cad,
        SearchCategories.Docs, SearchCategories.Video, SearchCategories.Folders, SearchCategories.SystemTools];

    private void BuildLayout()
    {
        InstallStyles();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 53, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(14),
            GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false
        });
        SetResourceReference(BackgroundProperty, "CardBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        var frame = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), SnapsToDevicePixels = true };
        frame.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Content = frame;
        var outer = new DockPanel();
        frame.Child = outer;

        var header = new DockPanel { Name = "Header", Height = 55, Margin = new Thickness(14, 0, 10, 0) };
        DockPanel.SetDock(header, Dock.Top);
        outer.Children.Add(header);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        tools.Children.Add(ToolButton("search", "SearchButton", () => { _input.Focus(); RequestPage(0); }));
        tools.Children.Add(ToolButton("scope", "ScopeButton", () =>
            _scope.Visibility = _scope.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        _pin = ToolButton("pin", "PinButton", TogglePin);
        tools.Children.Add(_pin);
        tools.Children.Add(ToolButton("close", "CloseButton", Close));

        // The margin between the entry and the tools belongs to the native draggable caption.
        var entry = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Height = 38, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        entry.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
        entry.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        WindowChrome.SetIsHitTestVisibleInChrome(entry, true);
        var field = new Grid { Margin = new Thickness(3, 0, 3, 0) };
        _input.Style = (Style)FindResource("QuickSearch.Input");
        _input.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RequestPage(0, debounce: _input.Text.Length > 0);
        };
        field.Children.Add(_input);
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        field.Children.Add(_placeholder);
        entry.Child = field;
        header.Children.Add(entry);

        var filters = new WrapPanel { Name = "FilterBar", Margin = new Thickness(13, 0, 9, 6) };
        DockPanel.SetDock(filters, Dock.Top);
        outer.Children.Add(filters);
        foreach (string category in FilterKeys)
        {
            var button = MakeButton(category, () =>
            {
                _category = category;
                foreach (var (key, control) in _categoryButtons) MarkCategory(key, control);
                _input.Focus();
                RequestPage(0);
            }, category);
            _categoryButtons.Add(category, button);
            filters.Children.Add(button);
        }
        BuildScope();
        var scopeScroll = new ScrollViewer { Content = _scope, MaxHeight = 154,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scopeScroll.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = _scope });
        DockPanel.SetDock(scopeScroll, Dock.Top);
        outer.Children.Add(scopeScroll);
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        outer.Children.Add(footer);

        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.Margin = new Thickness(6, 0, 6, 0);
        _list.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _list.ItemContainerStyle = (Style)FindResource("QuickSearch.Row");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        outer.Children.Add(_list);
        WireResultCommands();
    }

    private void BuildScope()
    {
        _scope.Children.Add(_help);
        _help.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _rootList.ItemsSource = _roots.ToArray();
        _rootList.SetResourceReference(BackgroundProperty, "InputBackgroundBrush");
        _rootList.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _rootList.SetResourceReference(BorderBrushProperty, "InputBorderBrush");
        _scope.Children.Add(_rootList);
        var actions = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        actions.Children.Add(MakeButton("localDrives", AddLocalDrives));
        actions.Children.Add(MakeButton("add", ChooseFolders));
        actions.Children.Add(MakeButton("appsRoot", AddStartMenu));
        var remove = MakeButton("remove", RemoveRoot);
        remove.IsEnabled = false;
        _rootList.SelectionChanged += (_, _) => remove.IsEnabled = _rootList.SelectedItem != null;
        actions.Children.Add(remove);
        actions.Children.Add(MakeButton("retry", ConfigureScope));
        _scope.Children.Add(actions);
    }

    private Border BuildFooter()
    {
        var footer = new Border { Padding = new Thickness(14, 4, 8, 7), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var body = new DockPanel();
        var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _previous = ToolButton("previous", "PreviousPage", () => RequestPage(Math.Max(0, _page - 1)));
        _next = ToolButton("next", "NextPage", () => RequestPage(_page + 1));
        nav.Children.Add(_previous);
        nav.Children.Add(_next);
        DockPanel.SetDock(nav, Dock.Right);
        body.Children.Add(nav);
        var messages = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _keys.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        messages.Children.Add(_status);
        messages.Children.Add(_keys);
        body.Children.Add(messages);
        footer.Child = body;
        return footer;
    }

    private Button MakeButton(string key, Action activate, string? icon = null)
    {
        var button = new Button { Style = (Style)FindResource("QuickSearch.Button"), Margin = new Thickness(0, 2, 4, 2) };
        _labels[key] = button;
        var caption = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _captions[key] = caption;
        if (icon == null) button.Content = caption;
        else
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(DrawGlyph(icon, 14));
            caption.Margin = new Thickness(5, 0, 0, 0);
            stack.Children.Add(caption);
            button.Content = stack;
        }
        button.Click += (_, _) => activate();
        return button;
    }

    private Button ToolButton(string key, string name, Action activate)
    {
        var button = new Button { Name = name, Style = (Style)FindResource("QuickSearch.Tool"), Content = DrawGlyph(key, 17) };
        _labels[key] = button;
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => activate();
        return button;
    }

    private void TogglePin()
    {
        bool next = !_pinned;
        if (!TryUserAction(() => { _preferences.SetPinned(next); return true; })) return;
        _pinned = next;
        RefreshPin();
    }

    private void RefreshPin()
    {
        LabelControl(_pin, T(_pinned ? "unpin" : "pin"));
        Selector.SetIsSelected(_pin, _pinned);
    }

    private void MarkCategory(string key, Button button) => Selector.SetIsSelected(button, key == _category);

    private static void LabelControl(FrameworkElement control, string label)
    {
        control.ToolTip = label;
        AutomationProperties.SetName(control, label);
    }

    private void RefreshPresentation()
    {
        foreach (var (key, control) in _labels)
        {
            string label = T(key);
            LabelControl(control, label);
            if (_captions.TryGetValue(key, out var caption)) caption.Text = label;
        }
        foreach (var (key, control) in _categoryButtons) MarkCategory(key, control);
        RefreshPin();
        _help.Text = T("scopeHelp");
        _keys.Text = T("shortcuts");
        _placeholder.Text = T("placeholder");
        AutomationProperties.SetName(_input, T("search"));
        AutomationProperties.SetHelpText(_input, _placeholder.Text);
        AutomationProperties.SetName(_list, T("results"));
        AutomationProperties.SetName(_rootList, T("scope"));
    }
}
