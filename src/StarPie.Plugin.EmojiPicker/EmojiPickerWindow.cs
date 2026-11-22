using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.EmojiPicker;

internal sealed class EmojiPickerWindow : Window
{
    private const int GwlExStyle = -20;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaActivate = 1;
    private const int MaNoActivate = 3;

    private sealed record VisibleGridEntry(
        EmojiItemKind Kind,
        FrameworkElement Visual,
        int Columns,
        string DisplayName,
        string ModeTag,
        EmojiEntry? Emoji = null,
        CustomSticker? Sticker = null,
        KaomojiEntry? Kaomoji = null);

    private readonly IPluginContext _context;
    private readonly IHostInputPanelService _panels;
    private readonly EmojiLibraryStorage _storage;
    private readonly EmojiLibraryDocument _doc;
    private readonly EmojiImageCache _imageCache;
    private readonly GifAnimationController _gifController = new();
    private readonly EmojiInputDispatcher _dispatcher;
    private readonly Action<bool> _onSaveLibrary;
    private readonly Action _onOpenManager;
    private readonly EmojiUiTheme _theme;
    private readonly ActionContext _target;
    private readonly int _anchorX;
    private readonly int _anchorY;

    private readonly TextBox _searchBox;
    private readonly TextBlock _searchWatermark;
    private readonly Border _searchBorder;
    private readonly Button _clearSearchBtn;
    private readonly Button _pinBtn;
    private readonly Button _addBtn;
    private readonly Button _manageBtn;
    private readonly Button _closeBtn;
    private readonly Grid _dragArea;
    private readonly Border _footerBorder;
    private readonly Dictionary<EmojiPickerMode, Button> _modeButtons = new();
    private readonly Border _catRowHost;
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _sectionsPanel = new() { Margin = new Thickness(0, 2, 0, 6) };
    private readonly Dictionary<string, FrameworkElement> _sectionAnchors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _catTabButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Border _dropOverlay;
    private readonly TextBlock _footerPreviewGlyph;
    private readonly Image _footerPreviewImage;
    private readonly TextBlock _footerNameText;
    private readonly TextBlock _footerFormatText;
    private readonly TextBlock _footerKeysText;

    private readonly List<VisibleGridEntry> _visibleEntries = new();
    private IInputPanelSession? _session;
    private int _sessionGeneration;
    private nint _activeTargetHwnd;
    private HwndSource? _hwndSource;
    private EmojiPickerMode _mode;
    private string _activeCatId = "recent";
    private string _activeCustomGroupId = "__all__";
    private string _activeKaomojiCat = "__all__";
    private string _searchQuery = "";
    private bool _isPinned;
    private bool _isSearchActive;
    private bool _suppressSessionClose;
    private bool _suppressDeactivateClose;
    private int _modalOrMenuDepth;
    private int _focusedIndex = -1;
    private bool _closed;

    private bool _isDragging;
    private UIElement? _dragCaptureElement;
    private PointNative _dragStartCursor;
    private int _dragStartWinX;
    private int _dragStartWinY;

    internal bool IsPinned => _isPinned;
    internal bool IsClosedForTest => _closed;
    internal bool IsSearchActiveForTest => _isSearchActive;
    internal nint ActiveTargetHwndForTest => _activeTargetHwnd;

    internal static (int X, int Y) ComputeDragPosition(
        int startWinX, int startWinY, int startCurX, int startCurY, int curX, int curY) =>
        (startWinX + (curX - startCurX), startWinY + (curY - startCurY));

    internal EmojiPickerWindow(
        IPluginContext context,
        IHostInputPanelService panels,
        EmojiLibraryStorage storage,
        EmojiLibraryDocument doc,
        EmojiImageCache imageCache,
        EmojiInputDispatcher dispatcher,
        ActionContext target,
        EmojiPickerMode initialMode,
        Action<bool> onSaveLibrary,
        Action onOpenManager)
    {
        _context = context;
        _panels = panels;
        _storage = storage;
        _doc = doc;
        _imageCache = imageCache;
        _dispatcher = dispatcher;
        _target = target;
        _activeTargetHwnd = (nint)target.ForegroundWindowHandle;
        _anchorX = target.CursorX;
        _anchorY = target.CursorY;
        _mode = initialMode;
        _onSaveLibrary = onSaveLibrary;
        _onOpenManager = onOpenManager;
        _theme = EmojiUiTheme.From(panels.GetAppearance());

        Title = EmojiTexts.T(context, "pluginTitle");
        Width = 376;
        Height = 500;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        _theme.ApplyControlStyles(Resources);

        var cardBorder = new Border
        {
            Background = _theme.WindowBg,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(8),
            AllowDrop = true
        };
        var shadow = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = _theme.IsDark ? 0.5 : 0.22, Color = Colors.Black };
        shadow.Freeze();
        cardBorder.Effect = shadow;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) }); // Drag handle
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Search + tools
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Mode switcher
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Category strip
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); // Footer

        // Row 0: subtle non-activating drag handle
        _dragArea = new Grid { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = EmojiTexts.T(context, "dragHandleTip") };
        _dragArea.Children.Add(new Border
        {
            Width = 36,
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = _theme.Border,
            Opacity = 0.75,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        AttachNonActivatingDrag(_dragArea);
        rootGrid.Children.Add(_dragArea);

        // Row 1: Search bar + Pin + QuickAdd + Manage + Close
        var topRow = new Grid { Margin = new Thickness(10, 0, 10, 7) };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _searchBorder = new Border
        {
            Background = _theme.InputBg,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Height = 32,
            Padding = new Thickness(8, 0, 6, 0)
        };
        var searchInner = new Grid();
        searchInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchInner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchInner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchIcon = EmojiUiTheme.Icon("search", _theme.TextMuted, 14, 1.8);
        searchIcon.Margin = new Thickness(0, 0, 6, 0);
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        searchInner.Children.Add(searchIcon);

        _searchWatermark = new TextBlock
        {
            Text = EmojiTexts.T(context, "searchPlaceholder"),
            Foreground = _theme.TextMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _searchBox = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = _theme.TextPrimary,
            CaretBrush = _theme.TextPrimary,
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Template = TransparentTextBoxTemplate()
        };
        _searchBox.TextChanged += (_, _) => OnSearchTextChanged();
        _searchBox.GotKeyboardFocus += (_, _) => _searchBorder.BorderBrush = _theme.Accent;
        _searchBox.LostKeyboardFocus += (_, _) => _searchBorder.BorderBrush = _theme.Border;

        var boxHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
        boxHost.Children.Add(_searchWatermark);
        boxHost.Children.Add(_searchBox);
        Grid.SetColumn(boxHost, 1);
        searchInner.Children.Add(boxHost);

        _clearSearchBtn = new Button
        {
            Content = "✕",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            FontSize = 10,
            Foreground = _theme.TextMuted,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
            Focusable = false,
            Template = _theme.FlatTileTemplate(9),
            ToolTip = EmojiTexts.T(context, "clearSearch")
        };
        _clearSearchBtn.Click += (_, _) =>
        {
            _searchBox.Text = "";
            if (_isSearchActive) _searchBox.Focus();
        };
        Grid.SetColumn(_clearSearchBtn, 2);
        searchInner.Children.Add(_clearSearchBtn);
        _searchBorder.Child = searchInner;
        topRow.Children.Add(_searchBorder);

        _pinBtn = MakeToolIconButton("pin", EmojiTexts.T(context, "pinTip"));
        _pinBtn.Margin = new Thickness(4, 0, 0, 0);
        _pinBtn.Click += (_, _) => TogglePinned();
        Grid.SetColumn(_pinBtn, 1);
        topRow.Children.Add(_pinBtn);

        _addBtn = MakeToolIconButton("plus", EmojiTexts.T(context, "addTip"));
        _addBtn.Margin = new Thickness(3, 0, 0, 0);
        _addBtn.Click += (_, _) => OpenQuickAddMenu();
        Grid.SetColumn(_addBtn, 2);
        topRow.Children.Add(_addBtn);

        _manageBtn = MakeToolIconButton("gear", EmojiTexts.T(context, "manageTip"));
        _manageBtn.Margin = new Thickness(3, 0, 0, 0);
        _manageBtn.Click += (_, _) =>
        {
            _suppressDeactivateClose = true;
            _onOpenManager();
            _suppressDeactivateClose = false;
        };
        Grid.SetColumn(_manageBtn, 3);
        topRow.Children.Add(_manageBtn);

        _closeBtn = MakeToolIconButton("close", EmojiTexts.T(context, "close"));
        _closeBtn.Margin = new Thickness(3, 0, 0, 0);
        _closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(_closeBtn, 4);
        topRow.Children.Add(_closeBtn);

        Grid.SetRow(topRow, 1);
        rootGrid.Children.Add(topRow);

        // Row 2: Mode Switcher (4 pills)
        var modeContainer = new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Margin = new Thickness(10, 0, 10, 6)
        };
        var modeGrid = new Grid();
        (EmojiPickerMode Mode, string Key)[] modes =
        [
            (EmojiPickerMode.All, "modeAll"),
            (EmojiPickerMode.Emoji, "modeEmoji"),
            (EmojiPickerMode.Custom, "modeCustom"),
            (EmojiPickerMode.Kaomoji, "modeKaomoji")
        ];
        for (int i = 0; i < modes.Length; i++)
        {
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var m = modes[i].Mode;
            var btn = new Button
            {
                Content = EmojiTexts.T(context, modes[i].Key),
                Height = 24,
                Padding = new Thickness(4, 0, 4, 0),
                FontSize = 11.5,
                Focusable = false,
                BorderThickness = new Thickness(0),
                Template = _theme.FlatTileTemplate(6)
            };
            btn.Click += (_, _) =>
            {
                _mode = m;
                _focusedIndex = -1;
                RenderAll();
            };
            Grid.SetColumn(btn, i);
            modeGrid.Children.Add(btn);
            _modeButtons[m] = btn;
        }
        modeContainer.Child = modeGrid;
        Grid.SetRow(modeContainer, 2);
        rootGrid.Children.Add(modeContainer);

        // Row 3: Category / Group strip
        _catRowHost = new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 0, 8, 6)
        };
        Grid.SetRow(_catRowHost, 3);
        rootGrid.Children.Add(_catRowHost);

        // Row 4: Scrollable Body + Drop Overlay
        var bodyHost = new Grid();
        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8, 6, 8, 8),
            Focusable = false,
            Content = _sectionsPanel
        };
        _scrollViewer.ScrollChanged += OnScrollChanged;
        bodyHost.Children.Add(_scrollViewer);

        _dropOverlay = new Border
        {
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(10),
            BorderBrush = _theme.Accent,
            BorderThickness = new Thickness(2),
            Background = _theme.IsDark
                ? new SolidColorBrush(Color.FromArgb(228, 18, 22, 30))
                : new SolidColorBrush(Color.FromArgb(235, 241, 245, 249)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var dropStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        dropStack.Children.Add(new TextBlock { Text = "📥", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        dropStack.Children.Add(new TextBlock { Text = EmojiTexts.T(context, "dropOverlayTitle"), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = _theme.TextPrimary, HorizontalAlignment = HorizontalAlignment.Center });
        dropStack.Children.Add(new TextBlock { Text = EmojiTexts.T(context, "dropOverlaySub"), FontSize = 11, Foreground = _theme.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        _dropOverlay.Child = dropStack;
        bodyHost.Children.Add(_dropOverlay);

        Grid.SetRow(bodyHost, 4);
        rootGrid.Children.Add(bodyHost);

        // Row 5: Footer micro bar (also supports non-activating window drag)
        _footerBorder = new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 13, 13),
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.SizeAll
        };
        AttachNonActivatingDrag(_footerBorder);
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previewLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _footerPreviewGlyph = new TextBlock { Text = "😊", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        _footerPreviewImage = new Image { Width = 16, Height = 16, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 5, 0) };
        _footerNameText = new TextBlock
        {
            Text = EmojiTexts.T(context, "footerDefault"),
            Foreground = _theme.TextMuted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 148,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _footerFormatText = new TextBlock
        {
            Text = "",
            Foreground = _theme.TextSecondary,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        previewLeft.Children.Add(_footerPreviewGlyph);
        previewLeft.Children.Add(_footerPreviewImage);
        previewLeft.Children.Add(_footerNameText);
        previewLeft.Children.Add(_footerFormatText);
        footerGrid.Children.Add(previewLeft);

        _footerKeysText = new TextBlock
        {
            Text = EmojiTexts.T(context, "footerKeys"),
            Foreground = _theme.TextMuted,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(_footerKeysText, 1);
        footerGrid.Children.Add(_footerKeysText);
        _footerBorder.Child = footerGrid;
        Grid.SetRow(_footerBorder, 5);
        rootGrid.Children.Add(_footerBorder);

        cardBorder.DragEnter += OnDragEnterOver;
        cardBorder.DragOver += OnDragEnterOver;
        cardBorder.DragLeave += (_, _) => _dropOverlay.Visibility = Visibility.Collapsed;
        cardBorder.Drop += OnDropFiles;

        cardBorder.Child = rootGrid;
        Content = cardBorder;

        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            nint exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
            SetWindowLongPtr(hwnd, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
            SetWindowPos(hwnd, 0, _anchorX, _anchorY, 0, 0, 0x0001 | 0x0004 | 0x0010);
        };
        ContentRendered += (_, _) => FitWorkArea();
        PreviewKeyDown += OnPickerPreviewKeyDown;
        Deactivated += (_, _) => HandleDeactivated();
        Closed += (_, _) =>
        {
            _closed = true;
            EndNonActivatingDrag();
            _gifController.Dispose();
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            DetachSessionQuietly();
        };

        RenderAll();
    }

    internal void PrepareWithoutActivation() => new WindowInteropHelper(this).EnsureHandle();

    internal void AttachSession(IInputPanelSession session)
    {
        _sessionGeneration++;
        _session = session;
        if (_doc.RequiresRecovery)
        {
            _footerNameText.Text = EmojiTexts.T(_context, "statusLoadFailed");
            _footerNameText.Foreground = _theme.Danger;
        }
    }

    internal void EnterSearchModeForTest() => _isSearchActive = true;

    internal void SetModalOrMenuDepthForTest(int depth) => _modalOrMenuDepth = Math.Max(0, depth);

    internal void OnSessionClosed(InputPanelCloseReason reason) => OnSessionClosed(reason, expectedGeneration: null);

    internal void OnSessionClosed(InputPanelCloseReason reason, int? expectedGeneration)
    {
        if (_closed || _suppressSessionClose || reason == InputPanelCloseReason.Disposed) return;
        if (expectedGeneration.HasValue && expectedGeneration.Value != _sessionGeneration) return;
        if (_session == null && reason is InputPanelCloseReason.TargetChanged or InputPanelCloseReason.Escape) return;

        if (_isPinned)
        {
            if (reason is InputPanelCloseReason.PluginStopped or InputPanelCloseReason.Replaced)
                Close();
            else
            {
                _session = null;
                _sessionGeneration++;
            }
            return;
        }

        if (_modalOrMenuDepth > 0 && reason == InputPanelCloseReason.TargetChanged)
        {
            _session = null;
            _sessionGeneration++;
            return;
        }

        Close();
    }

    internal void RefreshFromLibrary()
    {
        if (_closed) return;
        RenderAll();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            if (IsCursorOverElement(_searchBorder))
            {
                EnterSearchMode(hwnd);
                handled = true;
                return MaActivate;
            }
            if (IsCursorOverElement(_dragArea) || IsCursorOverElement(_footerBorder))
            {
                handled = true;
                return MaNoActivate;
            }
            if (_isSearchActive)
            {
                handled = true;
                return MaActivate;
            }
            handled = true;
            return MaNoActivate;
        }
        return 0;
    }

    private static bool IsCursorOverElement(FrameworkElement element)
    {
        try
        {
            if (!GetCursorPos(out PointNative pt)) return false;
            Point local = element.PointFromScreen(new Point(pt.X, pt.Y));
            return local.X >= 0 && local.X <= element.ActualWidth &&
                   local.Y >= 0 && local.Y <= element.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void DetachSessionQuietly()
    {
        _sessionGeneration++;
        _suppressSessionClose = true;
        try { _session?.Dispose(); } catch { }
        _session = null;
        _suppressSessionClose = false;
    }

    private void CaptureLiveExternalTarget()
    {
        nint fg = _dispatcher.GetForegroundWindow();
        if (_dispatcher.IsExternalTargetWindow(fg))
            _activeTargetHwnd = fg;
    }

    private void TryReopenSessionForActiveTarget()
    {
        if (_closed || _isPinned || _isSearchActive || _session is { IsOpen: true })
            return;
        if (!_dispatcher.IsExternalTargetWindow(_activeTargetHwnd))
            return;

        try
        {
            var ctx = new ActionContext
            {
                ForegroundProcessName = _target.ForegroundProcessName,
                ForegroundWindowTitle = _target.ForegroundWindowTitle,
                ForegroundWindowHandle = _activeTargetHwnd,
                CursorX = _target.CursorX,
                CursorY = _target.CursorY,
                IsElevated = _target.IsElevated,
                LanguageCode = _target.LanguageCode
            };
            int gen = ++_sessionGeneration;
            IInputPanelSession reopened = _panels.Open(ctx, reason => OnSessionClosed(reason, gen));
            if (reopened.IsOpen && !_closed && !_isPinned && !_isSearchActive)
                _session = reopened;
            else
                reopened.Dispose();
        }
        catch { }
    }

    private void EnterSearchMode(nint hwnd)
    {
        if (_isSearchActive) return;
        _isSearchActive = true;
        CaptureLiveExternalTarget();
        DetachSessionQuietly();

        nint style = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, (style & ~WsExNoActivate) | WsExToolWindow);
        Dispatcher.BeginInvoke(() =>
        {
            if (!_closed)
            {
                _searchBox.Focus();
                Keyboard.Focus(_searchBox);
            }
        });
    }

    internal void HandleDeactivated()
    {
        if (_closed || _suppressDeactivateClose || _modalOrMenuDepth > 0 || !_isSearchActive) return;
        if (!_isPinned) Close();
        else ExitSearchPassive();
    }

    internal void ExitSearchPassive()
    {
        if (!_isSearchActive) return;
        _isSearchActive = false;
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
        {
            nint style = GetWindowLongPtr(hwnd, GwlExStyle);
            SetWindowLongPtr(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        }
        Keyboard.ClearFocus();
        CaptureLiveExternalTarget();
    }

    private async Task<bool> ExitSearchToNonActivatingAsync()
    {
        if (!_isSearchActive) return true;
        _isSearchActive = false;
        _suppressDeactivateClose = true;
        try
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0)
            {
                nint style = GetWindowLongPtr(hwnd, GwlExStyle);
                SetWindowLongPtr(hwnd, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
            }
            Keyboard.ClearFocus();
            bool restored = await _dispatcher.RestoreForegroundAsync(_activeTargetHwnd, 150).ConfigureAwait(true);
            if (_closed) return false;
            if (restored && !_isPinned)
                TryReopenSessionForActiveTarget();
            return restored;
        }
        finally
        {
            _suppressDeactivateClose = false;
        }
    }

    private void AttachNonActivatingDrag(UIElement el)
    {
        el.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            nint hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == 0 || !GetCursorPos(out _dragStartCursor) || !GetWindowRect(hwnd, out var rect))
                return;
            _isDragging = true;
            _dragCaptureElement = el;
            _dragStartWinX = rect.Left;
            _dragStartWinY = rect.Top;
            el.CaptureMouse();
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (!_isDragging || !ReferenceEquals(_dragCaptureElement, el)) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndNonActivatingDrag();
                return;
            }
            nint hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != 0 && GetCursorPos(out var cur))
            {
                (int nx, int ny) = ComputeDragPosition(_dragStartWinX, _dragStartWinY, _dragStartCursor.X, _dragStartCursor.Y, cur.X, cur.Y);
                SetWindowPos(hwnd, 0, nx, ny, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
        };
        el.MouseLeftButtonUp += (_, e) =>
        {
            if (ReferenceEquals(_dragCaptureElement, el))
            {
                EndNonActivatingDrag();
                e.Handled = true;
            }
        };
        el.LostMouseCapture += (_, _) =>
        {
            if (ReferenceEquals(_dragCaptureElement, el))
                EndNonActivatingDrag();
        };
    }

    private void EndNonActivatingDrag()
    {
        _isDragging = false;
        UIElement? cap = _dragCaptureElement;
        _dragCaptureElement = null;
        if (cap is { IsMouseCaptured: true })
        {
            try { cap.ReleaseMouseCapture(); } catch { }
        }
    }

    private void RenderAll()
    {
        foreach (var pair in _modeButtons)
        {
            bool active = pair.Key == _mode;
            pair.Value.Background = active ? _theme.WindowBg : Brushes.Transparent;
            pair.Value.Foreground = active ? _theme.TextPrimary : _theme.TextMuted;
            pair.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        _pinBtn.Background = _isPinned ? _theme.AccentSoft : Brushes.Transparent;
        _pinBtn.BorderBrush = _isPinned ? _theme.Accent : Brushes.Transparent;
        _pinBtn.BorderThickness = new Thickness(_isPinned ? 1 : 0);
        _footerKeysText.Text = EmojiTexts.T(_context, _isPinned ? "footerPinned" : "footerKeys");

        RenderCategoryStrip();
        RenderBodySections();
    }

    private void RenderCategoryStrip()
    {
        _catTabButtons.Clear();
        if (_mode is EmojiPickerMode.All or EmojiPickerMode.Emoji)
        {
            var tabs = _mode == EmojiPickerMode.Emoji
                ? EmojiCatalog.CategoryTabs.Take(9)
                : EmojiCatalog.CategoryTabs;

            var grid = new Grid();
            int col = 0;
            foreach (EmojiCategoryMeta cat in tabs)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bool isActive = string.Equals(_activeCatId, cat.Id, StringComparison.OrdinalIgnoreCase);
                var btn = new Button
                {
                    Width = 30,
                    Height = 30,
                    Padding = new Thickness(0),
                    Focusable = false,
                    Background = isActive ? _theme.SurfaceActive : Brushes.Transparent,
                    BorderBrush = isActive ? _theme.Border : Brushes.Transparent,
                    BorderThickness = new Thickness(isActive ? 1 : 0),
                    Template = _theme.FlatTileTemplate(15),
                    ToolTip = EmojiTexts.T(_context, cat.TooltipKey),
                    Content = EmojiUiTheme.Icon(cat.Id, isActive ? _theme.TextPrimary : _theme.TextMuted, 16, 1.55)
                };
                string capturedId = cat.Id;
                btn.Click += (_, _) => JumpToCategory(capturedId);
                Grid.SetColumn(btn, col++);
                grid.Children.Add(btn);
                _catTabButtons[cat.Id] = btn;
            }
            _catRowHost.Child = grid;
        }
        else if (_mode == EmojiPickerMode.Custom)
        {
            var pills = new WrapPanel { Orientation = Orientation.Horizontal };
            pills.Children.Add(MakeGroupPill("__all__", EmojiTexts.T(_context, "grpAllCustom"),
                string.Equals(_activeCustomGroupId, "__all__", StringComparison.OrdinalIgnoreCase),
                () => { _activeCustomGroupId = "__all__"; RenderAll(); }));
            pills.Children.Add(MakeGroupPill("__fav__", EmojiTexts.T(_context, "grpFavorites"),
                string.Equals(_activeCustomGroupId, "__fav__", StringComparison.OrdinalIgnoreCase),
                () => { _activeCustomGroupId = "__fav__"; RenderAll(); }));

            foreach (StickerGroup g in _doc.Groups)
            {
                string label = g.IsMounted ? $"🔗 {g.Name}" : g.Name;
                string gid = g.Id;
                pills.Children.Add(MakeGroupPill(gid, label,
                    string.Equals(_activeCustomGroupId, gid, StringComparison.OrdinalIgnoreCase),
                    () => { _activeCustomGroupId = gid; RenderAll(); }));
            }

            var newGrpBtn = MakeGroupPill("__new__", EmojiTexts.T(_context, "grpNew"), false, CreateNewGroupInteractive);
            pills.Children.Add(newGrpBtn);

            _catRowHost.Child = pills;
        }
        else
        {
            var pills = new WrapPanel { Orientation = Orientation.Horizontal };
            pills.Children.Add(MakeGroupPill("__all__", EmojiTexts.T(_context, "kaoAll"),
                string.Equals(_activeKaomojiCat, "__all__", StringComparison.OrdinalIgnoreCase),
                () => { _activeKaomojiCat = "__all__"; RenderAll(); }));
            foreach (string cat in EmojiCatalog.KaomojiCategories)
            {
                string c = cat;
                pills.Children.Add(MakeGroupPill(c, EmojiTexts.T(_context, c),
                    string.Equals(_activeKaomojiCat, c, StringComparison.OrdinalIgnoreCase),
                    () => { _activeKaomojiCat = c; RenderAll(); }));
            }
            _catRowHost.Child = pills;
        }
    }

    private Button MakeGroupPill(string id, string label, bool active, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 25,
            Padding = new Thickness(9, 0, 9, 0),
            Margin = new Thickness(0, 2, 5, 2),
            FontSize = 11.5,
            Focusable = false,
            Background = active ? _theme.AccentSoft : _theme.Surface,
            Foreground = active ? _theme.Accent : _theme.TextSecondary,
            BorderBrush = active ? _theme.Accent : _theme.Border,
            BorderThickness = new Thickness(1),
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Template = _theme.FlatTileTemplate(12)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void JumpToCategory(string categoryId)
    {
        _activeCatId = categoryId;
        if (!string.IsNullOrEmpty(_searchQuery))
        {
            _searchBox.Text = "";
            return;
        }
        RenderCategoryStrip();
        if (_sectionAnchors.TryGetValue(categoryId, out FrameworkElement? anchor))
        {
            anchor.BringIntoView();
        }
    }

    private void OnSearchTextChanged()
    {
        _searchQuery = _searchBox.Text.Trim();
        _searchWatermark.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _clearSearchBtn.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        _focusedIndex = -1;
        RenderBodySections();
    }

    private void RenderBodySections()
    {
        _gifController.Stop();
        _sectionsPanel.Children.Clear();
        _sectionAnchors.Clear();
        _visibleEntries.Clear();

        string lang = _context.Info.LanguageCode;

        if (!string.IsNullOrEmpty(_searchQuery))
        {
            if (_mode is EmojiPickerMode.All or EmojiPickerMode.Emoji)
            {
                var matchedEmojis = EmojiSearchEngine.SearchEmojis(_searchQuery);
                if (matchedEmojis.Count > 0)
                    AddEmojiSection("search_emoji", EmojiTexts.Format(_context, "secEmojiMatch", matchedEmojis.Count), matchedEmojis, lang);
            }
            if (_mode is EmojiPickerMode.All or EmojiPickerMode.Custom)
            {
                var matchedStickers = EmojiSearchEngine.SearchStickers(_doc, _searchQuery);
                if (matchedStickers.Count > 0)
                    AddStickerSection("search_custom", EmojiTexts.Format(_context, "secCustomMatch", matchedStickers.Count), matchedStickers);
            }
            if (_mode is EmojiPickerMode.All or EmojiPickerMode.Kaomoji)
            {
                var matchedKao = EmojiSearchEngine.SearchKaomojis(_doc, _searchQuery);
                if (matchedKao.Count > 0)
                    AddKaomojiSection("search_kao", EmojiTexts.Format(_context, "secKaomojiMatch", matchedKao.Count), matchedKao);
            }

            if (_sectionsPanel.Children.Count == 0)
            {
                var empty = new StackPanel { Margin = new Thickness(12, 34, 12, 16), HorizontalAlignment = HorizontalAlignment.Center };
                empty.Children.Add(new TextBlock { Text = "🔍", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
                empty.Children.Add(new TextBlock
                {
                    Text = EmojiTexts.Format(_context, "emptySearchTitle", _searchQuery),
                    Foreground = _theme.TextPrimary,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                empty.Children.Add(new TextBlock
                {
                    Text = EmojiTexts.T(_context, "emptySearchHint"),
                    Foreground = _theme.TextMuted,
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                _sectionsPanel.Children.Add(empty);
            }
            return;
        }

        if (_mode is EmojiPickerMode.All or EmojiPickerMode.Emoji)
        {
            var rec = EmojiSearchEngine.GetRecommended(_doc);
            AddEmojiSection("recent", EmojiTexts.T(_context, "secRecommended"), rec, lang);

            foreach (var cat in EmojiCatalog.CategoryTabs.Where(c => c.Id != "recent" && c.Id != "customJump"))
            {
                var items = EmojiCatalog.BuiltInEmojis.Where(e => e.CategoryId == cat.Id).ToList();
                if (items.Count > 0)
                    AddEmojiSection(cat.Id, EmojiTexts.T(_context, cat.TitleKey), items, lang);
            }
        }

        if (_mode is EmojiPickerMode.All or EmojiPickerMode.Custom)
        {
            string? grpFilter = _mode == EmojiPickerMode.Custom ? _activeCustomGroupId : "__all__";
            var stickers = EmojiSearchEngine.SearchStickers(_doc, "", grpFilter);
            string title = EmojiTexts.T(_context, "secCustom");
            if (_mode == EmojiPickerMode.Custom && grpFilter == "__fav__")
                title = EmojiTexts.T(_context, "grpFavorites");
            else if (_mode == EmojiPickerMode.Custom && grpFilter != "__all__")
            {
                StickerGroup? grp = _doc.Groups.FirstOrDefault(g => string.Equals(g.Id, grpFilter, StringComparison.OrdinalIgnoreCase));
                if (grp != null) title = grp.IsMounted ? $"🔗 {grp.Name}" : grp.Name;
            }
            AddStickerSection("customJump", title, stickers, showEmptyCta: true);
        }

        if (_mode is EmojiPickerMode.All or EmojiPickerMode.Kaomoji)
        {
            string? catFilter = _mode == EmojiPickerMode.Kaomoji ? _activeKaomojiCat : "__all__";
            var kaomojis = EmojiSearchEngine.SearchKaomojis(_doc, "", catFilter);
            AddKaomojiSection(_mode == EmojiPickerMode.Kaomoji ? "kaomojiOnly" : "customJumpKaomoji",
                EmojiTexts.T(_context, "secKaomoji"), kaomojis);
        }
    }

    private void AddEmojiSection(string secId, string title, IReadOnlyList<EmojiEntry> items, string lang)
    {
        var header = MakeSectionHeader(title, null);
        _sectionAnchors[secId] = header;
        _sectionsPanel.Children.Add(header);

        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 0, 0, 8) };
        foreach (EmojiEntry e in items)
        {
            int idx = _visibleEntries.Count;
            string name = EmojiCatalog.GetEmojiDisplayName(e, lang);
            bool isFav = _doc.FavoriteEmojis.Contains(e.Glyph);

            var cell = new Grid();
            cell.Children.Add(new TextBlock
            {
                Text = e.Glyph,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Microsoft YaHei UI"),
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (isFav) cell.Children.Add(MakeFavoriteDot());

            var btn = new Button
            {
                Height = 38,
                Padding = new Thickness(0),
                Focusable = false,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1.5),
                Template = _theme.FlatTileTemplate(8),
                ToolTip = name,
                Content = cell
            };

            var entry = new VisibleGridEntry(EmojiItemKind.Emoji, btn, 8, name,
                EmojiTexts.T(_context, "modeTagUnicode"), Emoji: e);
            _visibleEntries.Add(entry);
            WireItemEvents(btn, entry, idx);
            grid.Children.Add(btn);
        }
        _sectionsPanel.Children.Add(grid);
    }

    private void AddStickerSection(string secId, string title, IReadOnlyList<CustomSticker> stickers, bool showEmptyCta = false)
    {
        string badge = EmojiTexts.Format(_context, "customBadge", stickers.Count);
        var header = MakeSectionHeader(title, badge);
        _sectionAnchors[secId] = header;
        _sectionsPanel.Children.Add(header);

        if (stickers.Count == 0 && showEmptyCta)
        {
            var emptyBox = new Border
            {
                Background = _theme.Surface,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 16, 12, 16),
                Margin = new Thickness(2, 2, 2, 10)
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = EmojiTexts.T(_context, "emptyCustom"),
                Foreground = _theme.TextMuted,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            var importBtn = new Button
            {
                Content = EmojiTexts.T(_context, "mgrImportFilesBtn"),
                Margin = new Thickness(0, 10, 0, 0),
                Background = _theme.Accent,
                Foreground = _theme.AccentFg,
                BorderBrush = _theme.Accent,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            importBtn.Click += (_, _) => ImportFilesInteractive();
            stack.Children.Add(importBtn);
            emptyBox.Child = stack;
            _sectionsPanel.Children.Add(emptyBox);
            return;
        }

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
        foreach (CustomSticker s in stickers)
        {
            int idx = _visibleEntries.Count;
            string fullPath = _storage.ResolveStickerPath(s);
            BitmapSource? thumb = _imageCache.GetThumbnail(fullPath, 96);

            var cardGrid = new Grid();
            var imgCtrl = new Image
            {
                Stretch = Stretch.Uniform,
                Source = thumb,
                Margin = new Thickness(4)
            };
            if (thumb == null)
            {
                cardGrid.Children.Add(new TextBlock
                {
                    Text = "🖼️",
                    FontSize = 22,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                cardGrid.Children.Add(imgCtrl);
            }

            if (s.IsMounted)
            {
                cardGrid.Children.Add(new Border
                {
                    Background = _theme.AccentSoft,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(3),
                    Child = new TextBlock { Text = "🔗", FontSize = 9, Foreground = _theme.Accent }
                });
            }

            if (s.IsFavorite) cardGrid.Children.Add(MakeFavoriteDot());

            cardGrid.Children.Add(new Border
            {
                Background = _theme.TagOverlayBg,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(3),
                Child = new TextBlock { Text = s.Format, FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            });

            var btn = new Button
            {
                Height = 76,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Focusable = false,
                Background = _theme.Surface,
                BorderBrush = _theme.StickerOutline,
                BorderThickness = new Thickness(1),
                Template = _theme.FlatTileTemplate(9),
                ToolTip = s.Name,
                Content = cardGrid
            };

            var entry = new VisibleGridEntry(EmojiItemKind.Sticker, btn, 4, s.Name,
                EmojiTexts.Format(_context, "modeTagSticker", s.Format), Sticker: s);
            _visibleEntries.Add(entry);
            WireItemEvents(btn, entry, idx, imgCtrl, fullPath, thumb);
            grid.Children.Add(btn);
        }
        _sectionsPanel.Children.Add(grid);
    }

    private void AddKaomojiSection(string secId, string title, IReadOnlyList<KaomojiEntry> items)
    {
        if (items.Count == 0) return;
        var header = MakeSectionHeader(title, null);
        _sectionAnchors[secId] = header;
        _sectionsPanel.Children.Add(header);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        foreach (KaomojiEntry k in items)
        {
            int idx = _visibleEntries.Count;
            var outer = new Grid();
            var stack = new StackPanel { Margin = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = k.Text,
                Foreground = _theme.TextPrimary,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{k.Name} · {EmojiTexts.T(_context, k.Category)}",
                Foreground = _theme.TextMuted,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            outer.Children.Add(stack);
            if (k.IsFavorite) outer.Children.Add(MakeFavoriteDot());

            var btn = new Button
            {
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Focusable = false,
                Background = _theme.Surface,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                Template = _theme.FlatTileTemplate(8),
                ToolTip = k.Text,
                Content = outer
            };

            var entry = new VisibleGridEntry(EmojiItemKind.Kaomoji, btn, 2, k.Name,
                EmojiTexts.T(_context, "modeTagText"), Kaomoji: k);
            _visibleEntries.Add(entry);
            WireItemEvents(btn, entry, idx);
            grid.Children.Add(btn);
        }
        _sectionsPanel.Children.Add(grid);
    }

    private FrameworkElement MakeSectionHeader(string title, string? badge)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = _theme.TextMuted,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrEmpty(badge))
        {
            panel.Children.Add(new Border
            {
                Background = _theme.Surface,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = badge, Foreground = _theme.TextMuted, FontSize = 9.5 }
            });
        }
        return panel;
    }

    private Border MakeFavoriteDot() => new()
    {
        Width = 5,
        Height = 5,
        CornerRadius = new CornerRadius(2.5),
        Background = _theme.FavoriteDot,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(3),
        IsHitTestVisible = false
    };

    private void WireItemEvents(
        Button btn,
        VisibleGridEntry entry,
        int idx,
        Image? stickerImg = null,
        string? stickerFullPath = null,
        BitmapSource? stickerStill = null)
    {
        btn.MouseEnter += (_, _) =>
        {
            UpdateFooterPreview(entry);
            if (entry.Sticker != null &&
                string.Equals(entry.Sticker.Format, "GIF", StringComparison.OrdinalIgnoreCase) &&
                stickerImg != null && !string.IsNullOrEmpty(stickerFullPath))
            {
                _gifController.Start(stickerImg, stickerFullPath, stickerStill);
            }
        };
        btn.MouseLeave += (_, _) =>
        {
            if (stickerImg != null)
                _gifController.Stop(stickerImg);
        };
        btn.Click += async (_, _) =>
        {
            bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            await ActivateEntryAsync(entry, shiftHeld).ConfigureAwait(true);
        };
        btn.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowItemContextMenu(btn, entry);
        };
    }

    private void UpdateFooterPreview(VisibleGridEntry entry)
    {
        _footerNameText.Foreground = _theme.TextPrimary;
        _footerNameText.Text = entry.DisplayName;
        _footerFormatText.Text = entry.ModeTag;
        _footerFormatText.Visibility = Visibility.Visible;

        if (entry.Emoji != null)
        {
            _footerPreviewImage.Visibility = Visibility.Collapsed;
            _footerPreviewGlyph.Visibility = Visibility.Visible;
            _footerPreviewGlyph.Text = entry.Emoji.Glyph;
        }
        else if (entry.Sticker != null)
        {
            BitmapSource? thumb = _imageCache.GetThumbnail(_storage.ResolveStickerPath(entry.Sticker), 64);
            if (thumb != null)
            {
                _footerPreviewGlyph.Visibility = Visibility.Collapsed;
                _footerPreviewImage.Visibility = Visibility.Visible;
                _footerPreviewImage.Source = thumb;
            }
            else
            {
                _footerPreviewImage.Visibility = Visibility.Collapsed;
                _footerPreviewGlyph.Visibility = Visibility.Visible;
                _footerPreviewGlyph.Text = "🖼️";
            }
        }
        else
        {
            _footerPreviewImage.Visibility = Visibility.Collapsed;
            _footerPreviewGlyph.Visibility = Visibility.Visible;
            _footerPreviewGlyph.Text = "✨";
        }
    }

    internal Task ActivateEntryForTestAsync(
        EmojiEntry? emoji = null,
        CustomSticker? sticker = null,
        KaomojiEntry? kaomoji = null,
        bool shiftHeld = false)
    {
        EmojiItemKind kind = sticker != null ? EmojiItemKind.Sticker
            : kaomoji != null ? EmojiItemKind.Kaomoji
            : EmojiItemKind.Emoji;
        var entry = new VisibleGridEntry(kind, _pinBtn, 8, "test", "test", emoji, sticker, kaomoji);
        return ActivateEntryAsync(entry, shiftHeld);
    }

    private async Task ActivateEntryAsync(VisibleGridEntry entry, bool shiftHeld)
    {
        if (_closed) return;

        if (_isSearchActive)
        {
            bool ok = await ExitSearchToNonActivatingAsync().ConfigureAwait(true);
            if (!ok || _closed)
            {
                _footerNameText.Text = EmojiTexts.T(_context, "targetUnavailable");
                _footerNameText.Foreground = _theme.Danger;
                return;
            }
        }
        else
        {
            CaptureLiveExternalTarget();
        }

        nint targetHwnd = _activeTargetHwnd;
        IInputPanelSession? activeSession = _session;
        if (_isPinned)
        {
            nint liveFg = _dispatcher.GetForegroundWindow();
            targetHwnd = _dispatcher.IsExternalTargetWindow(liveFg) ? liveFg : 0;
            if (targetHwnd != 0) _activeTargetHwnd = targetHwnd;
            activeSession = null;
        }

        DispatchOutcome outcome;
        if (entry.Emoji != null)
        {
            outcome = _dispatcher.SendText(entry.Emoji.Glyph, activeSession, _context.Host,
                targetHwnd, _doc.Preferences.EmojiClipboardFallback);
            if (outcome == DispatchOutcome.Sent && _doc.Preferences.TrackRecent)
            {
                _doc.RecentEmojis.RemoveAll(g => g == entry.Emoji.Glyph);
                _doc.RecentEmojis.Insert(0, entry.Emoji.Glyph);
                if (_doc.RecentEmojis.Count > 16) _doc.RecentEmojis.RemoveRange(16, _doc.RecentEmojis.Count - 16);
                _onSaveLibrary(false);
            }
        }
        else if (entry.Kaomoji != null)
        {
            outcome = _dispatcher.SendText(entry.Kaomoji.Text, activeSession, _context.Host,
                targetHwnd, _doc.Preferences.EmojiClipboardFallback);
        }
        else if (entry.Sticker != null)
        {
            string fullPath = _storage.ResolveStickerPath(entry.Sticker);
            outcome = _dispatcher.SendSticker(fullPath, entry.Sticker.Format, activeSession, _context.Host,
                targetHwnd);
        }
        else
        {
            return;
        }

        if (outcome == DispatchOutcome.Busy)
        {
            _footerNameText.Text = EmojiTexts.T(_context, "busy");
            _footerNameText.Foreground = _theme.Danger;
            return;
        }
        if (outcome is DispatchOutcome.TargetUnavailable or DispatchOutcome.Closed)
        {
            if (_isPinned)
            {
                _footerNameText.Text = EmojiTexts.T(_context, "targetUnavailable");
                _footerNameText.Foreground = _theme.Danger;
                return;
            }
            Close();
            return;
        }

        bool keepOpen = _isPinned || shiftHeld || !_doc.Preferences.AutoCloseOnPick;
        if (!keepOpen)
        {
            Close();
        }
        else if (_doc.Preferences.TrackRecent && entry.Emoji != null && string.IsNullOrEmpty(_searchQuery))
        {
            RenderBodySections();
        }
    }

    private void OnPickerPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_isPinned)
            {
                if (!string.IsNullOrEmpty(_searchBox.Text))
                    _searchBox.Text = "";
                else if (_isSearchActive)
                    _ = ExitSearchToNonActivatingAsync();
            }
            else
            {
                Close();
            }
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            _mode = (EmojiPickerMode)(((int)_mode + 1) % 4);
            _focusedIndex = -1;
            RenderAll();
            return;
        }

        if (_visibleEntries.Count == 0) return;

        bool isArrow = e.Key is Key.Right or Key.Left or Key.Down or Key.Up;
        if (isArrow)
        {
            if (_searchBox.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right && !string.IsNullOrEmpty(_searchBox.Text))
                return;

            e.Handled = true;
            int cur = _focusedIndex;
            int cols = cur >= 0 ? _visibleEntries[cur].Columns : _visibleEntries[0].Columns;
            int next = cur;
            if (cur < 0) next = 0;
            else if (e.Key == Key.Right) next = Math.Min(_visibleEntries.Count - 1, cur + 1);
            else if (e.Key == Key.Left) next = Math.Max(0, cur - 1);
            else if (e.Key == Key.Down) next = Math.Min(_visibleEntries.Count - 1, cur + cols);
            else if (e.Key == Key.Up) next = Math.Max(0, cur - cols);

            ApplyKeyboardFocusHighlight(next);
            return;
        }

        if (e.Key == Key.Enter && _focusedIndex >= 0 && _focusedIndex < _visibleEntries.Count)
        {
            e.Handled = true;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            _ = ActivateEntryAsync(_visibleEntries[_focusedIndex], shift);
        }
    }

    private void ApplyKeyboardFocusHighlight(int nextIdx)
    {
        if (_focusedIndex >= 0 && _focusedIndex < _visibleEntries.Count)
        {
            var prev = _visibleEntries[_focusedIndex];
            if (prev.Visual is Button pb)
                pb.BorderBrush = prev.Kind == EmojiItemKind.Emoji ? Brushes.Transparent
                    : prev.Kind == EmojiItemKind.Sticker ? _theme.StickerOutline : _theme.Border;
        }

        _focusedIndex = nextIdx;
        if (_focusedIndex >= 0 && _focusedIndex < _visibleEntries.Count)
        {
            var cur = _visibleEntries[_focusedIndex];
            if (cur.Visual is Button cb)
            {
                cb.BorderBrush = _theme.Accent;
                cb.BringIntoView();
            }
            UpdateFooterPreview(cur);
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_searchQuery) || _mode is not (EmojiPickerMode.All or EmojiPickerMode.Emoji))
            return;

        string active = "recent";
        foreach (var cat in EmojiCatalog.CategoryTabs)
        {
            if (!_sectionAnchors.TryGetValue(cat.Id, out FrameworkElement? anchor)) continue;
            try
            {
                Point rel = anchor.TranslatePoint(new Point(0, 0), _scrollViewer);
                if (rel.Y <= 28) active = cat.Id;
            }
            catch { }
        }

        if (!string.Equals(_activeCatId, active, StringComparison.OrdinalIgnoreCase))
        {
            _activeCatId = active;
            foreach (var pair in _catTabButtons)
            {
                bool on = pair.Key == active;
                pair.Value.Background = on ? _theme.SurfaceActive : Brushes.Transparent;
                pair.Value.BorderBrush = on ? _theme.Border : Brushes.Transparent;
                pair.Value.BorderThickness = new Thickness(on ? 1 : 0);
                pair.Value.Content = EmojiUiTheme.Icon(pair.Key, on ? _theme.TextPrimary : _theme.TextMuted, 16, 1.55);
            }
        }
    }

    internal void TogglePinned()
    {
        _isPinned = !_isPinned;
        _pinBtn.Background = _isPinned ? _theme.AccentSoft : Brushes.Transparent;
        _pinBtn.BorderBrush = _isPinned ? _theme.Accent : Brushes.Transparent;
        _pinBtn.BorderThickness = new Thickness(_isPinned ? 1 : 0);
        _footerKeysText.Text = EmojiTexts.T(_context, _isPinned ? "footerPinned" : "footerKeys");

        if (_isPinned)
        {
            CaptureLiveExternalTarget();
            DetachSessionQuietly();
        }
        else if (!_isSearchActive)
        {
            CaptureLiveExternalTarget();
            TryReopenSessionForActiveTarget();
        }
    }

    private void OpenQuickAddMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "addPickFiles"), ImportFilesInteractive));
        menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "addPickFolder"), ImportFolderInteractive));
        menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "addClipboard"), SaveClipboardImageInteractive));
        menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "addKaomoji"), AddKaomojiInteractive));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "addMountFolder"), MountFolderInteractive));
        ShowTrackedContextMenu(_addBtn, menu, PlacementMode.Bottom);
    }

    private void ShowItemContextMenu(Button target, VisibleGridEntry entry)
    {
        var menu = new ContextMenu();
        if (entry.Emoji != null)
        {
            EmojiEntry em = entry.Emoji;
            bool fav = _doc.FavoriteEmojis.Contains(em.Glyph);
            bool pinRec = _doc.PinnedRecommended.Contains(em.Glyph);
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, fav ? "ctxUnfavorite" : "ctxFavorite"), () =>
            {
                if (fav) _doc.FavoriteEmojis.Remove(em.Glyph);
                else _doc.FavoriteEmojis.Add(em.Glyph);
                _onSaveLibrary(false);
                RenderBodySections();
            }));
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, pinRec ? "ctxUnpinRec" : "ctxPinRec"), () =>
            {
                if (pinRec) _doc.PinnedRecommended.Remove(em.Glyph);
                else _doc.PinnedRecommended.Insert(0, em.Glyph);
                _onSaveLibrary(false);
                RenderBodySections();
            }));
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxCopyOnly"), () =>
            {
                if (_dispatcher.CopyTextOnly(em.Glyph)) ShowStatusNote(EmojiTexts.T(_context, "statusCopied"));
            }));
        }
        else if (entry.Sticker != null)
        {
            CustomSticker st = entry.Sticker;
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, st.IsFavorite ? "ctxUnfavorite" : "ctxFavorite"), () =>
            {
                st.IsFavorite = !st.IsFavorite;
                _onSaveLibrary(true);
                RenderBodySections();
            }));
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxCopyOnly"), () =>
            {
                if (_dispatcher.CopyStickerOnly(_storage.ResolveStickerPath(st), st.Format))
                    ShowStatusNote(EmojiTexts.T(_context, "statusCopied"));
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxEdit"), () =>
            {
                var updated = RunTrackedModal(() => EmojiDialogs.EditItemProperties(_context, _theme, this, st.Name, st.Keywords));
                if (updated != null)
                {
                    st.Name = updated.Value.Name;
                    st.Keywords = updated.Value.Keywords;
                    _onSaveLibrary(true);
                    RenderBodySections();
                }
            }));
            if (st.IsMounted)
            {
                menu.Items.Add(new MenuItem { Header = EmojiTexts.T(_context, "ctxMountedReadonly"), IsEnabled = false });
            }
            else
            {
                menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxDelete"), () =>
                {
                    _storage.DeleteSticker(_doc, st);
                    _onSaveLibrary(true);
                    RenderBodySections();
                }));
            }
        }
        else if (entry.Kaomoji != null)
        {
            KaomojiEntry km = entry.Kaomoji;
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, km.IsFavorite ? "ctxUnfavorite" : "ctxFavorite"), () =>
            {
                km.IsFavorite = !km.IsFavorite;
                _onSaveLibrary(true);
                RenderBodySections();
            }));
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxCopyOnly"), () =>
            {
                if (_dispatcher.CopyTextOnly(km.Text)) ShowStatusNote(EmojiTexts.T(_context, "statusCopied"));
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxEdit"), () =>
            {
                var updated = RunTrackedModal(() => EmojiDialogs.EditItemProperties(_context, _theme, this, km.Name, km.Keywords));
                if (updated != null)
                {
                    km.Name = updated.Value.Name;
                    km.Keywords = updated.Value.Keywords;
                    _onSaveLibrary(true);
                    RenderBodySections();
                }
            }));
            menu.Items.Add(MakeMenuItem(EmojiTexts.T(_context, "ctxDelete"), () =>
            {
                _doc.Kaomojis.Remove(km);
                _onSaveLibrary(true);
                RenderBodySections();
            }));
        }

        ShowTrackedContextMenu(target, menu, PlacementMode.MousePoint);
    }

    private void ShowTrackedContextMenu(FrameworkElement anchor, ContextMenu menu, PlacementMode placement)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = placement;
        _modalOrMenuDepth++;
        menu.Closed += (_, _) => _modalOrMenuDepth = Math.Max(0, _modalOrMenuDepth - 1);
        menu.IsOpen = true;
    }

    private MenuItem MakeMenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private T RunTrackedModal<T>(Func<T> func)
    {
        _modalOrMenuDepth++;
        _suppressDeactivateClose = true;
        try { return func(); }
        finally
        {
            _suppressDeactivateClose = false;
            _modalOrMenuDepth = Math.Max(0, _modalOrMenuDepth - 1);
        }
    }

    private string TargetManagedGroupId()
    {
        if (!string.IsNullOrEmpty(_activeCustomGroupId) &&
            !_activeCustomGroupId.StartsWith("__", StringComparison.Ordinal))
        {
            var g = _doc.Groups.FirstOrDefault(x => string.Equals(x.Id, _activeCustomGroupId, StringComparison.OrdinalIgnoreCase) && !x.IsMounted);
            if (g != null) return g.Id;
        }
        return _storage.EnsureManagedGroup(_doc, EmojiTexts.T(_context, "defaultGroupCats")).Id;
    }

    private void ImportFilesInteractive()
    {
        string[]? files = RunTrackedModal(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif"
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        });
        if (files == null || files.Length == 0) return;

        string grpId = TargetManagedGroupId();
        ImportSummary summary = _storage.ImportFiles(_doc, files, grpId);
        if (summary.ImportedCount > 0)
        {
            _mode = EmojiPickerMode.Custom;
            _activeCustomGroupId = grpId;
            _onSaveLibrary(true);
            RenderAll();
        }
        ReportImportNote(summary);
    }

    private void ImportFolderInteractive()
    {
        string? folder = RunTrackedModal(() =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog();
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        });
        if (string.IsNullOrEmpty(folder)) return;

        var (grp, summary) = _storage.ImportFolder(_doc, folder);
        if (grp != null && summary.ImportedCount > 0)
        {
            _mode = EmojiPickerMode.Custom;
            _activeCustomGroupId = grp.Id;
            _onSaveLibrary(true);
            RenderAll();
        }
        ReportImportNote(summary);
    }

    private void SaveClipboardImageInteractive()
    {
        ClipboardImageReadResult read = _dispatcher.ReadImageFromClipboard();
        List<StickerGroup> managed = _doc.Groups.Where(g => !g.IsMounted).ToList();
        var confirmed = RunTrackedModal(() =>
            EmojiDialogs.ShowClipboardFavoriteDialog(_context, _theme, this, read, managed, TargetManagedGroupId()));
        if (confirmed == null || read.Bytes == null) return;

        CustomSticker? added = _storage.ImportFromBytes(_doc, read.Bytes, confirmed.Value.Name, confirmed.Value.Keywords, confirmed.Value.GroupId);
        if (added != null)
        {
            _mode = EmojiPickerMode.Custom;
            _activeCustomGroupId = confirmed.Value.GroupId;
            _onSaveLibrary(true);
            RenderAll();
            ShowStatusNote(EmojiTexts.Format(_context, "statusImportedCount", 1));
        }
    }

    private void AddKaomojiInteractive()
    {
        var added = RunTrackedModal(() => EmojiDialogs.ShowAddKaomojiDialog(_context, _theme, this,
            _activeKaomojiCat == "__all__" ? "kaoHappy" : _activeKaomojiCat));
        if (added == null) return;

        _doc.Kaomojis.Insert(0, new KaomojiEntry
        {
            Id = "k_" + Guid.NewGuid().ToString("N")[..8],
            Text = added.Value.Text,
            Name = added.Value.Name,
            Keywords = string.IsNullOrWhiteSpace(added.Value.Keywords) ? added.Value.Name : added.Value.Keywords,
            Category = added.Value.Category
        });
        _mode = EmojiPickerMode.Kaomoji;
        _activeKaomojiCat = added.Value.Category;
        _onSaveLibrary(true);
        RenderAll();
    }

    private void MountFolderInteractive()
    {
        var res = RunTrackedModal(() => EmojiDialogs.ShowMountFolderDialog(_context, _theme, this));
        if (res == null) return;

        StickerGroup? grp = _storage.MountFolder(_doc, res.Value.Path, res.Value.DisplayName);
        if (grp != null)
        {
            _mode = EmojiPickerMode.Custom;
            _activeCustomGroupId = grp.Id;
            _onSaveLibrary(true);
            RenderAll();
            int count = _doc.Stickers.Count(s => s.GroupId == grp.Id);
            ShowStatusNote(EmojiTexts.Format(_context, "statusMountRefreshed", count));
        }
    }

    private void CreateNewGroupInteractive()
    {
        string? name = RunTrackedModal(() => EmojiDialogs.PromptGroupName(_context, _theme, this, EmojiTexts.T(_context, "grpNew"), ""));
        if (string.IsNullOrEmpty(name)) return;
        StickerGroup grp = _storage.CreateManagedGroup(_doc, name);
        _activeCustomGroupId = grp.Id;
        _onSaveLibrary(true);
        RenderAll();
    }

    private void OnDragEnterOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            _dropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        _dropOverlay.Visibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        var fileList = new List<string>();
        foreach (string p in paths)
        {
            if (File.Exists(p)) fileList.Add(p);
        }
        if (fileList.Count == 0) return;

        string grpId = TargetManagedGroupId();
        ImportSummary summary = _storage.ImportFiles(_doc, fileList, grpId);
        if (summary.ImportedCount > 0)
        {
            _mode = EmojiPickerMode.Custom;
            _activeCustomGroupId = grpId;
            _onSaveLibrary(true);
            RenderAll();
        }
        ReportImportNote(summary);
        e.Handled = true;
    }

    private void ReportImportNote(ImportSummary summary)
    {
        if (summary.SkippedCount > 0)
            ShowStatusNote(EmojiTexts.Format(_context, "statusImportedSkipped", summary.ImportedCount, summary.SkippedCount));
        else if (summary.ImportedCount > 0)
            ShowStatusNote(EmojiTexts.Format(_context, "statusImportedCount", summary.ImportedCount));
    }

    private void ShowStatusNote(string text)
    {
        _footerPreviewImage.Visibility = Visibility.Collapsed;
        _footerPreviewGlyph.Visibility = Visibility.Visible;
        _footerPreviewGlyph.Text = "✓";
        _footerFormatText.Visibility = Visibility.Collapsed;
        _footerNameText.Foreground = _theme.Accent;
        _footerNameText.Text = text;
    }

    private Button MakeToolIconButton(string iconKey, string toolTip) => new()
    {
        Width = 28,
        Height = 28,
        Padding = new Thickness(0),
        Focusable = false,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Template = _theme.FlatTileTemplate(7),
        ToolTip = toolTip,
        Content = EmojiUiTheme.Icon(iconKey, _theme.TextSecondary, 14, 1.7)
    };

    private static ControlTemplate TransparentTextBoxTemplate()
    {
        var template = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = host;
        return template;
    }

    private void FitWorkArea()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        nint monitor = MonitorFromPoint(new PointNative { X = _anchorX, Y = _anchorY }, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        UpdateLayout();
        if (!GetWindowRect(hwnd, out var rect)) return;

        int w = rect.Right - rect.Left;
        int h = rect.Bottom - rect.Top;
        int offset = (int)Math.Round(12 * scale);
        int prefX = _anchorX + offset;
        int prefY = _anchorY - h / 2;
        int x = Math.Clamp(prefX, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - w));
        int y = Math.Clamp(prefY, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - h));
        SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectNative Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint hwnd, out RectNative rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out PointNative pt);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(PointNative point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}
