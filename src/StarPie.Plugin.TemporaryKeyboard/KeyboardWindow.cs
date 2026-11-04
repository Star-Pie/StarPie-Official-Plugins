using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class KeyboardWindow : Window
{
    private readonly IPluginContext _context;
    private readonly IHostInputPanelService _panels;
    private readonly KeySchemes _schemes;
    private readonly ModifierLatch _modifiers = new();
    private readonly Grid _keys = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly ComboBox _schemePicker = new() { DisplayMemberPath = nameof(KeyScheme.Name), MinWidth = 120, Focusable = false };
    private readonly InputPanelAppearance _palette;
    private readonly int _x, _y;
    private IInputPanelSession? _session;
    private KeyboardMode _mode;
    private bool _caps;
    private readonly bool _loadFailed;
    private HwndSource? _source;
    private readonly CancellationTokenSource _lifetime = new();
    private int _renderRevision;
    private bool _closed;

    internal KeyboardWindow(IPluginContext context, IHostInputPanelService panels, KeySchemes schemes,
        KeyboardMode mode, string? initialScheme, int x, int y, bool loadFailed = false)
    {
        _context = context; _panels = panels; _schemes = schemes; _mode = mode; _x = x; _y = y;
        _palette = panels.GetAppearance();
        _loadFailed = loadFailed;
        Title = KeyboardTexts.T(context, "title");
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Focusable = false;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = ModeWidth(mode); Height = 500; MinHeight = 280; MaxHeight = 750; MinWidth = 280;
        KeyboardUi.Apply(this, _palette);
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (KeyboardMode item in Enum.GetValues<KeyboardMode>())
        {
            var button = KeyboardUi.Button(KeyboardTexts.T(context, item.ToString()));
            button.Click += (_, _) => { if (_mode != item) { _mode = item; Width = ModeWidth(item); } _modifiers.Clear(); Render(); };
            modes.Children.Add(button);
        }
        root.Children.Add(modes);
        _schemePicker.ItemsSource = schemes.Items;
        _schemePicker.SelectedItem = schemes.Items.FirstOrDefault(item => item.Id.Equals(initialScheme, StringComparison.OrdinalIgnoreCase)
            || item.Name.Equals(initialScheme, StringComparison.OrdinalIgnoreCase)) ?? schemes.Items[0];
        _schemePicker.SelectionChanged += (_, _) => { if (_mode == KeyboardMode.Custom) Render(); };
        Grid.SetRow(_schemePicker, 1); root.Children.Add(_schemePicker);
        Grid.SetRow(_keys, 2); root.Children.Add(_keys);
        var modifiers = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (KeyboardModifiers modifier in new[] { KeyboardModifiers.Shift, KeyboardModifiers.Ctrl, KeyboardModifiers.Alt, KeyboardModifiers.Win })
        {
            var button = KeyboardUi.Button(modifier.ToString());
            button.Click += (_, _) => { _modifiers.Toggle(modifier); Render(); };
            modifiers.Children.Add(button);
        }
        var close = KeyboardUi.Button(KeyboardTexts.T(context, "close")); close.Click += (_, _) => Close();
        modifiers.Children.Add(close); Grid.SetRow(modifiers, 3); root.Children.Add(modifiers);
        Grid.SetRow(_status, 4); root.Children.Add(_status);
        Content = root;
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            nint style = GetWindowLongPtr(handle, -20);
            SetWindowLongPtr(handle, -20, style | 0x08000000 | 0x00000080);
            _source = HwndSource.FromHwnd(handle); _source?.AddHook(WindowMessage);
            SetWindowPos(handle, 0, _x, _y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        };
        ContentRendered += (_, _) => FitWorkArea();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel(); _lifetime.Dispose();
            _renderRevision++;
            _source?.RemoveHook(WindowMessage); _source = null;
            IInputPanelSession? session = _session; _session = null;
            _modifiers.Clear(); session?.Dispose();
            _schemePicker.ItemsSource = null; _keys.Children.Clear();
        };
        Render();
    }

    internal void AttachSession(IInputPanelSession session) { _session = session; Render(); }
    internal void PrepareWithoutActivation() => new WindowInteropHelper(this).EnsureHandle();

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }

    private void Render()
    {
        if (_closed) return;
        ++_renderRevision;
        _keys.Children.Clear();
        _keys.RowDefinitions.Clear();
        _schemePicker.Visibility = _mode == KeyboardMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        if (_mode == KeyboardMode.Custom)
        {
            var scheme = _schemePicker.SelectedItem as KeyScheme ?? _schemes.Items[0];
            var template = KeyboardGrid.Template(_caps ^ _modifiers.Current.HasFlag(KeyboardModifiers.Shift));
            var grid = KeyboardGrid.Custom(scheme, (slot, binding) =>
            {
                KeyboardKey? key = binding == null ? null : template.GetValueOrDefault(binding.KeyId);
                var button = KeyboardGrid.Key(binding?.Label is { Length: > 0 } label ? label : key?.Label ?? KeyboardTexts.T(_context, "empty"));
                button.IsEnabled = key != null;
                button.Click += (_, _) => { if (key != null) ClickKey(key); };
                return button;
            });
            _keys.Children.Add(grid);
        }
        else
        {
            bool upper = _caps ^ _modifiers.Current.HasFlag(KeyboardModifiers.Shift);
            foreach (var row in KeyboardLayouts.Create(_mode, upper))
            {
                var grid = new Grid(); int column = 0;
                foreach (KeyboardKey key in row)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(key.Width, GridUnitType.Star) });
                    var button = KeyboardGrid.Key(key.Label);
                    if (key.Kind == KeyboardKeyKind.Modifier && Enum.TryParse<KeyboardModifiers>(key.Value, out var mod) && _modifiers.Current.HasFlag(mod))
                    { button.Background = KeyboardUi.Brush(_palette.Accent); button.Foreground = KeyboardUi.Brush(_palette.AccentForeground); }
                    button.Click += (_, _) => ClickKey(key);
                    Grid.SetColumn(button, column++); grid.Children.Add(button);
                }
                Grid.SetRow(grid, _keys.RowDefinitions.Count); _keys.RowDefinitions.Add(new()); _keys.Children.Add(grid);
            }
        }
        _status.Text = (_loadFailed ? KeyboardTexts.T(_context, "loadFailed") : KeyboardTexts.T(_context, "hint"))
            + (_modifiers.Current == KeyboardModifiers.None ? "" : " · " + _modifiers.Current);
    }

    private static double ModeWidth(KeyboardMode mode) => mode switch
    { KeyboardMode.Numeric => 340, KeyboardMode.Functions => 520, KeyboardMode.Full => 900, _ => 650 };


    private void ClickKey(KeyboardKey key)
    {
        if (_session?.IsOpen != true) { Close(); return; }
        if (key.Kind == KeyboardKeyKind.Modifier)
        { if (Enum.TryParse<KeyboardModifiers>(key.Value, out var modifier)) _modifiers.Toggle(modifier); Render(); return; }
        if (key.Kind == KeyboardKeyKind.Caps)
        { if (_mode is KeyboardMode.Full or KeyboardMode.Custom) Report(_session.QueueHotkey("CapsLock")); else _caps = !_caps; Render(); return; }
        KeyboardModifiers modifiers = _modifiers.Consume();
        InputPanelQueueResult result;
        if (key.Kind == KeyboardKeyKind.Text && (modifiers & ~KeyboardModifiers.Shift) == KeyboardModifiers.None)
            result = _session.QueueText(modifiers.HasFlag(KeyboardModifiers.Shift) && key.Id.StartsWith("punct-", StringComparison.Ordinal)
                ? ShiftedPunctuation(key.Value) : key.Value);
        else
        {
            string value = key.ShortcutKey ?? key.Value;
            string prefix = "";
            if (modifiers.HasFlag(KeyboardModifiers.Ctrl)) prefix += "Ctrl+";
            if (modifiers.HasFlag(KeyboardModifiers.Alt)) prefix += "Alt+";
            if (modifiers.HasFlag(KeyboardModifiers.Shift)) prefix += "Shift+";
            if (modifiers.HasFlag(KeyboardModifiers.Win)) prefix += "Win+";
            result = _session.QueueHotkey(prefix + value);
        }
        Render(); Report(result);
    }

    private void Report(InputPanelQueueResult? result)
    {
        if (result == InputPanelQueueResult.Closed || result == null) { Close(); return; }
        if (result == InputPanelQueueResult.Busy) _status.Text = KeyboardTexts.T(_context, "busy");
        else if (result != InputPanelQueueResult.Accepted) _status.Text = KeyboardTexts.T(_context, "unavailable");
    }

    internal static string ShiftedPunctuation(string value) => value switch
    {
        "[" => "{", "]" => "}", "\\" => "|", ";" => ":", "'" => "\"",
        "," => "<", "." => ">", "/" => "?", "-" => "_", "=" => "+", _ => value,
    };

    private void FitWorkArea()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        nint monitor = MonitorFromPoint(new PointNative { X = _x, Y = _y }, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        MaxWidth = Math.Max(200, (info.Work.Right - info.Work.Left - 12) / scale);
        MaxHeight = Math.Max(180, (info.Work.Bottom - info.Work.Top - 12) / scale);
        UpdateLayout();
        if (!GetWindowRect(hwnd, out var rect)) return;
        int x = Math.Clamp(_x, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - (rect.Right - rect.Left)));
        int y = Math.Clamp(_y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - (rect.Bottom - rect.Top)));
        SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectNative Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(PointNative point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectNative rect);
}
