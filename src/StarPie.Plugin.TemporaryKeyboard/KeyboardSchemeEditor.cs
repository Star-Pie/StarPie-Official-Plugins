using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class KeyboardSchemeEditor : Window
{
    private const string DragFormat = "StarPie.Keyboard.KeyTemplate";
    private sealed record Payload(Guid Owner, string Scheme, long Revision, int Slot, KeyBinding? Binding);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly IPluginContext _context;
    private readonly KeyEditingState _state;
    private readonly KeySchemeDocument _document;
    private readonly InputPanelAppearance _palette;
    private readonly CancellationTokenSource _lifetime;
    private readonly ListBox _schemes = new() { DisplayMemberPath = nameof(KeyScheme.Name), Height = 140 };
    private readonly Grid _canvas = new();
    private readonly TextBox _name = new() { MaxLength = 64 };
    private readonly TextBox _label = new() { Name = "SelectedKeyLabel", MaxLength = 128 };
    private readonly TextBox _rows = new() { Width = 45 };
    private readonly TextBox _columns = new() { Width = 45 };
    private readonly TextBlock _selected = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button _undo, _redo, _save;
    private KeyBinding? _clipboard;
    private Point _dragStart;
    private bool _refreshing, _closed, _recover;
    internal event Action<KeySchemes, bool>? Saved;
    private KeyScheme Current => _state.CurrentScheme ?? throw new InvalidOperationException("A keyboard scheme is required.");

    internal KeyboardSchemeEditor(IPluginContext context, IHostInputPanelService panels, KeySchemeDocument document, CancellationToken lifetime)
    {
        _context = context; _document = document; _state = new(document.Schemes);
        _palette = panels.GetAppearance(); _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Title = T("configure"); Width = 1180; Height = 820; MinWidth = 900; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; KeyboardUi.Apply(this, _palette);
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = T("configure"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = T("keyEditorHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) }); root.Children.Add(heading);
        var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(175) }); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new GridLength(210) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; body.Children.Add(left);
        left.Children.Add(Heading(T("schemes"))); left.Children.Add(_schemes);
        left.Children.Add(Command(T("addShort"), () => Change(() => _state.AddScheme(UniqueName(T("newScheme"))))));
        left.Children.Add(Command(T("duplicate"), () => Change(() => _state.DuplicateScheme(UniqueName(Current.Name)))));
        left.Children.Add(Command(T("remove"), () => Change(_state.RemoveScheme)));
        left.Children.Add(new TextBlock { Text = T("legacyRetained"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        var center = new Grid { Margin = new Thickness(0, 0, 14, 0) }; Grid.SetColumn(center, 1); body.Children.Add(center);
        center.RowDefinitions.Add(new() { Height = GridLength.Auto }); center.RowDefinitions.Add(new());
        center.RowDefinitions.Add(new() { Height = GridLength.Auto }); center.RowDefinitions.Add(new() { Height = new GridLength(220) });
        var toolbar = new DockPanel(); var history = new StackPanel { Orientation = Orientation.Horizontal };
        _undo = Command(T("undo"), () => Change(_state.Undo)); _redo = Command(T("redo"), () => Change(_state.Redo));
        history.Children.Add(_undo); history.Children.Add(_redo); DockPanel.SetDock(history, Dock.Right); toolbar.Children.Add(history);
        toolbar.Children.Add(Heading(T("preview"))); center.Children.Add(toolbar); Grid.SetRow(_canvas, 1); center.Children.Add(_canvas);
        var hint = new TextBlock { Text = T("templateHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) };
        Grid.SetRow(hint, 2); center.Children.Add(hint); var template = BuildTemplate(); Grid.SetRow(template, 3); center.Children.Add(template);
        var inspector = new StackPanel(); var scroll = new ScrollViewer { Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 2); body.Children.Add(scroll); inspector.Children.Add(Heading(T("name"))); inspector.Children.Add(_name);
        inspector.Children.Add(new TextBlock { Text = T("dimensions"), Margin = new Thickness(0, 14, 0, 6) });
        var size = new WrapPanel(); size.Children.Add(_rows); size.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center }); size.Children.Add(_columns);
        inspector.Children.Add(size); inspector.Children.Add(Command(T("applySize"), ResizeCanvas));
        inspector.Children.Add(_selected); inspector.Children.Add(new TextBlock { Text = T("keyLabel") }); inspector.Children.Add(_label);
        inspector.Children.Add(Command(T("copyKey"), () => { if (CommitEditors()) _clipboard = Clone(Current.Keys[_state.SelectedSlot]); }));
        inspector.Children.Add(Command(T("pasteKey"), () => { if (_clipboard != null) ReuseKey(_clipboard, _state.SelectedSlot); }));
        inspector.Children.Add(Command(T("clearKey"), () => Change(() => _state.SetKey(_state.SelectedSlot, null))));
        inspector.Children.Add(new TextBlock { Text = T("pureKeyHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        Grid.SetRow(_status, 2); root.Children.Add(_status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _save = Command(T("save"), SaveChanges); _save.Name = "SaveKeyboardSchemes";
        _save.Background = KeyboardUi.Brush(_palette.Accent); _save.Foreground = KeyboardUi.Brush(_palette.AccentForeground);
        if (document.RequiresRecovery)
        { _status.Text = T("loadFailed"); footer.Children.Add(Command(T("rebuild"), () => { _recover = true; _save.IsEnabled = true; _status.Text = T("backupHint"); })); }
        footer.Children.Add(_save); footer.Children.Add(Command(T("cancel"), Close)); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        _schemes.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _schemes.SelectedItem is not KeyScheme scheme) return;
            if (!CommitEditors()) { RefreshAll(); return; }
            _state.SelectScheme(scheme.Id); _state.SelectSlot(0); RefreshAll();
        };
        _name.LostKeyboardFocus += (_, _) => CommitEditors(); _label.LostKeyboardFocus += (_, _) => { if (CommitEditors()) RenderCanvas(); };
        PreviewKeyDown += EditorKeyDown;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); Saved = null; _canvas.Children.Clear(); _schemes.ItemsSource = null; };
        RefreshAll();
    }
    private string T(string key) => KeyboardTexts.T(_context, key);
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
    private Button Command(string text, Action action) { var button = KeyboardUi.Button(text); button.Focusable = true; button.Click += (_, _) => action(); return button; }
    private string UniqueName(string stem)
    { if (stem.Length > 54) stem = stem[..54]; for (int i = 1; ; i++) { string name = stem + " " + i; if (!_state.Schemes.Items.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name; } }
    private bool CommitEditors()
    {
        if (_closed || _refreshing) return true;
        if (_name.Text.Trim() != Current.Name && !_state.RenameScheme(_name.Text.Trim())) { _status.Text = T("invalid"); return false; }
        var binding = Current.Keys[_state.SelectedSlot];
        if (binding != null && _label.Text != binding.Label && !_state.SetLabel(_state.SelectedSlot, _label.Text)) { _status.Text = T("invalid"); return false; }
        return true;
    }
    private void Change(Func<bool> change)
    { if (!_closed && CommitEditors() && change()) { _status.Text = T("unsaved"); RefreshAll(); } }
    private void ResizeCanvas()
    {
        if (!CommitEditors()) return;
        if (!int.TryParse(_rows.Text, out int rows) || !int.TryParse(_columns.Text, out int columns) ||
            (!_state.Resize(rows, columns) && (rows != Current.Rows || columns != Current.Columns))) { _status.Text = T("resizeBlocked"); return; }
        _status.Text = T("unsaved"); RefreshAll();
    }
    private void RefreshAll()
    {
        if (_closed) return; _refreshing = true;
        _schemes.ItemsSource = null; _schemes.ItemsSource = _state.Schemes.Items; _schemes.SelectedItem = Current;
        _name.Text = Current.Name; _rows.Text = Current.Rows.ToString(CultureInfo.InvariantCulture); _columns.Text = Current.Columns.ToString(CultureInfo.InvariantCulture);
        _undo.IsEnabled = _state.CanUndo; _redo.IsEnabled = _state.CanRedo; _save.IsEnabled = !_document.RequiresRecovery || _recover;
        _refreshing = false; RenderCanvas();
    }
    private void RenderCanvas()
    {
        if (_closed) return;
        _canvas.Children.Clear(); var template = KeyboardGrid.Template();
        _canvas.Children.Add(KeyboardGrid.Custom(Current, (slot, binding) =>
        {
            string label = binding?.Label is { Length: > 0 } text ? text : binding != null ? template[binding.KeyId].Label : T("empty");
            var button = KeyboardGrid.Key(label, true); button.Name = "CustomKey" + slot;
            if (slot == _state.SelectedSlot) { button.BorderBrush = KeyboardUi.Brush(_palette.Accent); button.BorderThickness = new Thickness(2); }
            button.Click += (_, _) => { if (CommitEditors()) { _state.SelectSlot(slot); RenderCanvas(); } };
            button.AllowDrop = true; InstallDrag(button, slot, null);
            button.DragOver += (_, e) => { e.Effects = Valid(e.Data) ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            button.Drop += (_, e) =>
            {
                if (Valid(e.Data) && e.Data.GetData(DragFormat) is Payload value)
                { if (value.Slot >= 0) MoveKey(value.Slot, slot); else if(value.Binding != null) ReuseKey(value.Binding, slot); }
                e.Handled = true;
            };
            return button;
        }));
        _refreshing = true; _selected.Text = string.Format(T("selectedKey"), _state.SelectedSlot + 1);
        _label.Text = Current.Keys[_state.SelectedSlot]?.Label ?? ""; _label.IsEnabled = Current.Keys[_state.SelectedSlot] != null; _refreshing = false;
    }
    private Grid BuildTemplate()
    {
        var template = new Grid { Name = "FullKeyboardTemplate" }; int rowIndex = 0;
        foreach (var row in KeyboardLayouts.Create(KeyboardMode.Full))
        {
            var grid = new Grid(); int column = 0;
            foreach (var key in row)
            {
                grid.ColumnDefinitions.Add(new() { Width = new GridLength(key.Width, GridUnitType.Star) });
                var binding = new KeyBinding { KeyId = key.Id, Label = key.Label };
                var button = KeyboardGrid.Key(key.Label, true); button.Tag = binding; button.ToolTip = T("templateKeyHint") + " " + key.Label;
                button.Click += (_, _) => { _clipboard = Clone(binding); _status.Text = T("templateCopied"); };
                button.MouseDoubleClick += (_, _) => ReuseKey(binding, _state.SelectedSlot);
                InstallDrag(button, -1, binding); Grid.SetColumn(button, column++); grid.Children.Add(button);
            }
            template.RowDefinitions.Add(new()); Grid.SetRow(grid, rowIndex++); template.Children.Add(grid);
        }
        return template;
    }
    private void InstallDrag(Button button, int slot, KeyBinding? binding)
    {
        button.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        button.PreviewMouseMove += (_, e) =>
        {
            Point point = e.GetPosition(this);
            if (e.LeftButton != MouseButtonState.Pressed || (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) || !CommitEditors()) return;
            DragDrop.DoDragDrop(button, new DataObject(DragFormat, new Payload(_owner, _state.SelectedSchemeId, _state.Revision, slot, Clone(binding))), slot < 0 ? DragDropEffects.Copy : DragDropEffects.Move);
            e.Handled = true;
        };
    }
    private bool Valid(IDataObject data) => !_closed && data.GetData(DragFormat) is Payload value && value.Owner == _owner && value.Scheme == _state.SelectedSchemeId && value.Revision == _state.Revision;
    internal bool MoveKey(int from, int to)
    { if (!CommitEditors() || !_state.SwapSlots(from, to)) return false; _state.SelectSlot(to); _status.Text = T("unsaved"); RefreshAll(); return true; }
    internal bool ReuseKey(KeyBinding binding, int target)
    { if (!CommitEditors() || !_state.SetKey(target, binding)) return false; _state.SelectSlot(target); _status.Text = T("unsaved"); RefreshAll(); return true; }
    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.C) { if (CommitEditors()) _clipboard = Clone(Keyboard.FocusedElement is Button { Tag: KeyBinding binding } ? binding : Current.Keys[_state.SelectedSlot]); e.Handled = true; }
            else if (e.Key == Key.V) { if (_clipboard != null) ReuseKey(_clipboard, _state.SelectedSlot); e.Handled = true; }
            else if (e.Key is Key.Z or Key.Y) { Change(e.Key == Key.Z ? _state.Undo : _state.Redo); e.Handled = true; }
        }
        else if (e.Key == Key.Delete) { Change(() => _state.SetKey(_state.SelectedSlot, null)); e.Handled = true; }
    }
    private void SaveChanges()
    {
        if (_closed || _lifetime.IsCancellationRequested || !CommitEditors() || (_document.RequiresRecovery && !_recover)) return;
        try { var snapshot = _state.Snapshot(); snapshot.Serialize(); Saved?.Invoke(snapshot, _recover); Close(); }
        catch (Exception ex) { _status.Text = T("invalid"); _context.Log.Error("Saving keyboard key schemes failed", ex); }
    }
    private static KeyBinding? Clone(KeyBinding? item) => item == null ? null : new()
    { KeyId = item.KeyId, Label = item.Label, ExtensionData = KeySchemes.CloneExtensionData(item.ExtensionData) };
}
