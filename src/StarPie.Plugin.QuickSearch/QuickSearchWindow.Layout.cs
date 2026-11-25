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
    private readonly TextBox _pathFilterInput = new() { Name = "PathFilterInput" };
    private readonly TextBlock _pathFilterPlaceholder = new() { Name = "PathFilterPlaceholder", FontSize = 11.5, IsHitTestVisible = false,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 4, 0) };
    private readonly ListBox _list = new() { Name = "ResultsList", HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _rootList = new() { MaxHeight = 55 };
    private readonly StackPanel _scope = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(14, 2, 14, 8) };
    private readonly Border _filterBar = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(9),
        BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(13, 0, 13, 6) };
    private readonly ComboBox _sortFieldCombo = new() { Name = "SortFieldCombo", MinWidth = 96, Margin = new Thickness(0, 2, 4, 2) };
    private readonly ComboBox _sizeFilterCombo = new() { Name = "SizeFilterCombo", MinWidth = 108, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _timeFilterCombo = new() { Name = "TimeFilterCombo", MinWidth = 96, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _help = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock _keys = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _placeholder = new() { Name = "SearchPlaceholder", FontSize = 16, IsHitTestVisible = false,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
    private readonly Dictionary<string, ContentControl> _labels = new();
    private readonly Dictionary<string, TextBlock> _captions = new();
    private readonly Dictionary<string, Button> _categoryButtons = new();
    private Button _pin = null!, _previous = null!, _next = null!;
    private Button _matchRelatedButton = null!, _matchExactButton = null!, _sortDirectionButton = null!, _filterToggleButton = null!, _resetFilterButton = null!;
    private bool _suppressControlEvents;
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

        var filters = new WrapPanel { Name = "FilterBar", Margin = new Thickness(13, 0, 9, 4) };
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

        BuildControlBar(outer);
        BuildAdvancedFilterBar(outer);

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
        RefreshMatchModeControls();
        RefreshSortControls();
        RefreshFilterControls();
        _help.Text = T("scopeHelp");
        _keys.Text = T("shortcuts");
        _placeholder.Text = T("placeholder");
        _pathFilterPlaceholder.Text = T("filterPathPlaceholder");
        AutomationProperties.SetName(_input, T("search"));
        AutomationProperties.SetHelpText(_input, _placeholder.Text);
        AutomationProperties.SetName(_pathFilterInput, _pathFilterPlaceholder.Text);
        AutomationProperties.SetName(_sortFieldCombo, T("sortLabel"));
        AutomationProperties.SetName(_sizeFilterCombo, T("filterSizeAny"));
        AutomationProperties.SetName(_timeFilterCombo, T("filterTimeAny"));
        AutomationProperties.SetName(_list, T("results"));
        AutomationProperties.SetName(_rootList, T("scope"));
    }

    private void BuildControlBar(DockPanel outer)
    {
        var bar = new WrapPanel { Name = "ControlBar", Margin = new Thickness(13, 0, 9, 5) };
        DockPanel.SetDock(bar, Dock.Top);
        outer.Children.Add(bar);

        _matchRelatedButton = MakeButton("matchRelated", () => SetMatchMode(
            _matchMode == SearchMatchMode.Related ? SearchMatchMode.Substring : SearchMatchMode.Related), "matchRelated");
        _matchRelatedButton.Name = "MatchRelatedButton";
        _matchExactButton = MakeButton("matchExact", () => SetMatchMode(
            _matchMode == SearchMatchMode.Exact ? SearchMatchMode.Substring : SearchMatchMode.Exact), "matchExact");
        _matchExactButton.Name = "MatchExactButton";
        bar.Children.Add(_matchRelatedButton);
        bar.Children.Add(_matchExactButton);

        var divider = new Border { Width = 1, Height = 16, Margin = new Thickness(4, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
        divider.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        bar.Children.Add(divider);

        _sortFieldCombo.Style = (Style)FindResource("QuickSearch.Combo");
        _sortFieldCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressControlEvents || _sortFieldCombo.SelectedItem is not ComboBoxItem { Tag: SearchSortField field }) return;
            ChangeSortField(field);
        };
        bar.Children.Add(_sortFieldCombo);

        _sortDirectionButton = new Button
        {
            Name = "SortDirectionButton",
            Style = (Style)FindResource("QuickSearch.Button"),
            Width = 28,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 2, 6, 2),
            Content = DrawGlyph("sortAsc", 14)
        };
        _sortDirectionButton.Click += (_, _) => ToggleSortDirection();
        bar.Children.Add(_sortDirectionButton);

        _filterToggleButton = MakeButton("filterToggle", ToggleFilterBar, "filter");
        _filterToggleButton.Name = "FilterToggleButton";
        bar.Children.Add(_filterToggleButton);
    }

    private void BuildAdvancedFilterBar(DockPanel outer)
    {
        _filterBar.Name = "AdvancedFilterBar";
        _filterBar.SetResourceReference(Border.BackgroundProperty, "ButtonDefaultBgBrush");
        _filterBar.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        DockPanel.SetDock(_filterBar, Dock.Top);
        outer.Children.Add(_filterBar);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBox = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Height = 27 };
        pathBox.SetResourceReference(Border.BackgroundProperty, "InputBackgroundBrush");
        pathBox.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
        var pathGrid = new Grid();
        _pathFilterInput.Style = (Style)FindResource("QuickSearch.FilterInput");
        _pathFilterInput.TextChanged += (_, _) =>
        {
            _pathFilterPlaceholder.Visibility = _pathFilterInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_suppressControlEvents) return;
            _pathFilter = _pathFilterInput.Text.Trim();
            RefreshFilterToggleCaption();
            RequestPage(0, debounce: true);
        };
        _pathFilterPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        pathGrid.Children.Add(_pathFilterInput);
        pathGrid.Children.Add(_pathFilterPlaceholder);
        pathBox.Child = pathGrid;
        row.Children.Add(pathBox);

        _sizeFilterCombo.Style = (Style)FindResource("QuickSearch.Combo");
        _sizeFilterCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressControlEvents || _sizeFilterCombo.SelectedItem is not ComboBoxItem { Tag: SearchSizeFilter filter }) return;
            _sizeFilter = filter;
            RefreshFilterToggleCaption();
            RequestPage(0);
        };
        Grid.SetColumn(_sizeFilterCombo, 1);
        row.Children.Add(_sizeFilterCombo);

        _timeFilterCombo.Style = (Style)FindResource("QuickSearch.Combo");
        _timeFilterCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressControlEvents || _timeFilterCombo.SelectedItem is not ComboBoxItem { Tag: SearchTimeFilter filter }) return;
            _timeFilter = filter;
            RefreshFilterToggleCaption();
            RequestPage(0);
        };
        Grid.SetColumn(_timeFilterCombo, 2);
        row.Children.Add(_timeFilterCombo);

        _resetFilterButton = MakeButton("filterReset", ResetFilters);
        _resetFilterButton.Name = "ResetFilterButton";
        _resetFilterButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_resetFilterButton, 3);
        row.Children.Add(_resetFilterButton);

        _filterBar.Child = row;
    }

    private void RefreshMatchModeControls()
    {
        if (_matchRelatedButton is null || _matchExactButton is null) return;
        Selector.SetIsSelected(_matchRelatedButton, _matchMode == SearchMatchMode.Related);
        Selector.SetIsSelected(_matchExactButton, _matchMode == SearchMatchMode.Exact);
        LabelControl(_matchRelatedButton, T("matchRelatedTip"));
        LabelControl(_matchExactButton, T("matchExactTip"));
    }

    private void RefreshSortControls()
    {
        if (_sortFieldCombo is null || _sortDirectionButton is null) return;
        _suppressControlEvents = true;
        try
        {
            _sortFieldCombo.Items.Clear();
            _sortFieldCombo.Items.Add(new ComboBoxItem { Tag = SearchSortField.Path, Content = T("sortPath") });
            _sortFieldCombo.Items.Add(new ComboBoxItem { Tag = SearchSortField.Size, Content = T("sortSize") });
            _sortFieldCombo.Items.Add(new ComboBoxItem { Tag = SearchSortField.ModifiedTime, Content = T("sortModified") });
            _sortFieldCombo.SelectedItem = _sortFieldCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _sortField));
        }
        finally { _suppressControlEvents = false; }

        _sortDirectionButton.Content = DrawGlyph(_sortDescending ? "sortDesc" : "sortAsc", 14);
        LabelControl(_sortDirectionButton, T(_sortDescending ? "sortDesc" : "sortAsc"));
    }

    private void RefreshFilterControls()
    {
        if (_sizeFilterCombo is null || _timeFilterCombo is null) return;
        _suppressControlEvents = true;
        try
        {
            _sizeFilterCombo.Items.Clear();
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Any, Content = T("filterSizeAny") });
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Tiny, Content = T("filterSizeTiny") });
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Small, Content = T("filterSizeSmall") });
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Medium, Content = T("filterSizeMedium") });
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Large, Content = T("filterSizeLarge") });
            _sizeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchSizeFilter.Huge, Content = T("filterSizeHuge") });
            _sizeFilterCombo.SelectedItem = _sizeFilterCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _sizeFilter));

            _timeFilterCombo.Items.Clear();
            _timeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchTimeFilter.Any, Content = T("filterTimeAny") });
            _timeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchTimeFilter.Today, Content = T("filterTimeToday") });
            _timeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchTimeFilter.Last7Days, Content = T("filterTimeWeek") });
            _timeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchTimeFilter.Last30Days, Content = T("filterTimeMonth") });
            _timeFilterCombo.Items.Add(new ComboBoxItem { Tag = SearchTimeFilter.LastYear, Content = T("filterTimeYear") });
            _timeFilterCombo.SelectedItem = _timeFilterCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, _timeFilter));
        }
        finally { _suppressControlEvents = false; }

        RefreshFilterToggleCaption();
    }

    private void RefreshFilterToggleCaption()
    {
        if (_filterToggleButton is null || !_captions.TryGetValue("filterToggle", out var caption)) return;
        int active = CurrentQueryOptions.ActiveFilterCount;
        string baseLabel = T("filterToggle");
        caption.Text = active > 0 ? $"{baseLabel} ({active})" : baseLabel;
        Selector.SetIsSelected(_filterToggleButton, _filterBar.Visibility == Visibility.Visible || active > 0);
        if (_resetFilterButton != null) _resetFilterButton.IsEnabled = active > 0;
    }
}
