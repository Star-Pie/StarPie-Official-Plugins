using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 插件持有的桌宠形象库管理窗口（纯代码构建，无 XAML，零外部依赖）。
/// <para>
/// 左侧展示已导入形象缩略图与当前使用标记；
/// 中间展示选中形象的大图预览与 12 状态手动切换；
/// 底部提供明确的「使用此形象」安全应用，具备 Busy 保护与失败保留旧状态机制。
/// </para>
/// </summary>
internal sealed class PetLibraryWindow : Window
{
    private readonly IPluginContext _context;
    private readonly PetController _controller;
    private readonly PetLibraryManager _libraryManager;
    private readonly CancellationTokenSource _cts = new();

    private readonly StackPanel _listPanel;
    private readonly Border _emptyListBorder;
    private readonly Grid _detailsPanel;
    private readonly Border _emptySelectionBorder;

    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _metaTextBlock;
    private readonly Image _previewImage;
    private readonly TextBlock _previewStateLabel;
    private readonly WrapPanel _statesPanel;
    private readonly Button _applyButton;
    private readonly TextBlock _statusTextBlock;
    private readonly Button _importButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
    private readonly Button _removeButton;
    private bool _editingLibrary;

    public const int MaxPageCards = 24;
    private int _currentPage = 0;
    private int _totalPages = 1;
    private readonly Border _paginationBorder;
    private readonly Button _prevPageButton;
    private readonly Button _nextPageButton;
    private readonly TextBlock _pageInfoTextBlock;

    public int CardCount => _cards.Count;
    public int CurrentPage => _currentPage;
    public int TotalPages => _totalPages;
    public static int ActiveThumbnailDecodes => 2 - _thumbnailThrottle.CurrentCount;

    private readonly List<LibraryCardView> _cards = new();
    private PetLibraryItem? _selectedItem;
    private string _currentFrameName = "idle.png";
    private bool _isWindowClosed;
    private int _previewGeneration;
    private CancellationTokenSource? _previewCts;
    private static readonly SemaphoreSlim _thumbnailThrottle = new(2, 2);

    // 12 个预览状态定义
    private static readonly (string FrameName, string LabelZh, string LabelEn)[] PreviewStates = new[]
    {
        ("idle.png", "常驻 (Idle)", "Idle"),
        ("blink.png", "眨眼 (Blink)", "Blink"),
        ("react.png", "点击反馈 (React)", "React"),
        ("drag.png", "拖动姿势 (Drag)", "Drag"),
        ("point_E.png", "指向东 (E)", "Point E"),
        ("point_SE.png", "指向东南 (SE)", "Point SE"),
        ("point_S.png", "指向南 (S)", "Point S"),
        ("point_SW.png", "指向西南 (SW)", "Point SW"),
        ("point_W.png", "指向西 (W)", "Point W"),
        ("point_NW.png", "指向西北 (NW)", "Point NW"),
        ("point_N.png", "指向北 (N)", "Point N"),
        ("point_NE.png", "指向东北 (NE)", "Point NE"),
    };

    public PetLibraryWindow(
        IPluginContext context,
        PetController controller,
        PetLibraryManager libraryManager)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));

        Title = _context.I18n.T("window.library.title", "桌宠形象库");
        Width = 860;
        Height = 640;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

        // 主网格：顶部标题栏 + 底部主内容
        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 1. 顶部工具栏
        var toolbarBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12, 16, 12),
        };

        var toolbarGrid = new Grid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        var headerTitle = new TextBlock
        {
            Text = _context.I18n.T("window.library.header", "桌宠形象管理"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerDesc = new TextBlock
        {
            Text = _context.I18n.T("window.library.desc", " · 本地 PNG 文件夹导入、12 状态预览与形象切换"),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        headerStack.Children.Add(headerTitle);
        headerStack.Children.Add(headerDesc);
        Grid.SetColumn(headerStack, 0);
        toolbarGrid.Children.Add(headerStack);

        var buttonStack = new StackPanel { Orientation = Orientation.Horizontal };
        _importButton = CreateButton(
            _context.I18n.T("window.library.btn.import", "导入本地文件夹..."),
            isPrimary: true,
            onClick: OnImportFolderClicked);
        _importButton.Margin = new Thickness(0, 0, 8, 0);

        var refreshButton = CreateButton(
            _context.I18n.T("window.library.btn.refresh", "刷新"),
            isPrimary: false,
            onClick: () => RefreshLibraryList());

        buttonStack.Children.Add(_importButton);
        buttonStack.Children.Add(refreshButton);
        Grid.SetColumn(buttonStack, 1);
        toolbarGrid.Children.Add(buttonStack);

        toolbarBorder.Child = toolbarGrid;
        Grid.SetRow(toolbarBorder, 0);
        mainGrid.Children.Add(toolbarBorder);

        // 2. 主体左右分栏
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧列表容器
        var leftBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };

        var leftGrid = new Grid();
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var listHeader = new TextBlock
        {
            Text = _context.I18n.T("window.library.list.title", "形象列表"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(16, 14, 16, 8),
        };
        Grid.SetRow(listHeader, 0);
        leftGrid.Children.Add(listHeader);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _listPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 12) };
        scrollViewer.Content = _listPanel;
        Grid.SetRow(scrollViewer, 1);
        leftGrid.Children.Add(scrollViewer);

        _emptyListBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16),
            Visibility = Visibility.Collapsed,
        };
        var emptyListText = new TextBlock
        {
            Text = _context.I18n.T("window.library.empty", "形象库为空。\n点击上方「导入本地文件夹...」添加素材。"),
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            FontSize = 13,
            LineHeight = 20,
        };
        _emptyListBorder.Child = emptyListText;
        Grid.SetRow(_emptyListBorder, 1);
        leftGrid.Children.Add(_emptyListBorder);

        _paginationBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.White,
            Visibility = Visibility.Collapsed,
        };

        var pageGrid = new Grid();
        pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _prevPageButton = CreateButton(_context.I18n.T("window.library.btn.prev", "上一页"), false, () => GoToPage(_currentPage - 1));
        _prevPageButton.Height = 26;
        _prevPageButton.Padding = new Thickness(8, 0, 8, 0);
        _prevPageButton.FontSize = 11;
        Grid.SetColumn(_prevPageButton, 0);
        pageGrid.Children.Add(_prevPageButton);

        _pageInfoTextBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
        };
        Grid.SetColumn(_pageInfoTextBlock, 1);
        pageGrid.Children.Add(_pageInfoTextBlock);

        _nextPageButton = CreateButton(_context.I18n.T("window.library.btn.next", "下一页"), false, () => GoToPage(_currentPage + 1));
        _nextPageButton.Height = 26;
        _nextPageButton.Padding = new Thickness(8, 0, 8, 0);
        _nextPageButton.FontSize = 11;
        Grid.SetColumn(_nextPageButton, 2);
        pageGrid.Children.Add(_nextPageButton);

        _paginationBorder.Child = pageGrid;
        Grid.SetRow(_paginationBorder, 3);
        leftGrid.Children.Add(_paginationBorder);

        var libraryTools = new WrapPanel { Margin = new Thickness(12, 8, 12, 12) };
        _moveUpButton = CreateButton(_context.I18n.T("library.btn.up"), false, () => EditSelectedItem(-1));
        _moveDownButton = CreateButton(_context.I18n.T("library.btn.down"), false, () => EditSelectedItem(1));
        _removeButton = CreateButton(_context.I18n.T("library.btn.remove"), false, () => EditSelectedItem(0));
        foreach (var button in new[] { _moveUpButton, _moveDownButton, _removeButton })
        {
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Padding = new Thickness(8, 0, 8, 0);
            button.IsEnabled = false;
            libraryTools.Children.Add(button);
        }
        _removeButton.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        Grid.SetRow(libraryTools, 2);
        leftGrid.Children.Add(libraryTools);

        leftBorder.Child = leftGrid;
        Grid.SetColumn(leftBorder, 0);
        bodyGrid.Children.Add(leftBorder);

        // 右侧详情与预览
        var rightContainer = new Border
        {
            Padding = new Thickness(20),
        };

        _emptySelectionBorder = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var emptySelectionText = new TextBlock
        {
            Text = _context.I18n.T("window.library.select.prompt", "请在左侧选择一个桌宠形象以预览和使用。"),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            FontSize = 14,
        };
        _emptySelectionBorder.Child = emptySelectionText;

        // 选中详情面板
        _detailsPanel = new Grid { Visibility = Visibility.Collapsed };
        _detailsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        _detailsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview + States

        // 选中形象头部
        var itemHeaderStack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        _titleTextBlock = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _metaTextBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 4, 0, 0),
        };
        itemHeaderStack.Children.Add(_titleTextBlock);
        itemHeaderStack.Children.Add(_metaTextBlock);
        Grid.SetRow(itemHeaderStack, 0);
        _detailsPanel.Children.Add(itemHeaderStack);

        // 预览主体网格（图片 + 12 状态切换）
        var previewAreaGrid = new Grid();
        previewAreaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewAreaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var previewCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 260,
            Height = 260,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var previewStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _previewImage = new Image
        {
            Width = 200,
            Height = 200,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.HighQuality);

        _previewStateLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        previewStack.Children.Add(_previewImage);
        previewStack.Children.Add(_previewStateLabel);
        previewCard.Child = previewStack;
        Grid.SetColumn(previewCard, 0);
        previewAreaGrid.Children.Add(previewCard);

        // 12 状态按钮集合
        var statesContainer = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var statesTitle = new TextBlock
        {
            Text = _context.I18n.T("window.library.states.title", "动作与指向状态预览（12 状态）："),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 0, 0, 8),
        };
        statesContainer.Children.Add(statesTitle);

        _statesPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(-3),
        };
        statesContainer.Children.Add(_statesPanel);
        Grid.SetRow(statesContainer, 1);
        previewAreaGrid.Children.Add(statesContainer);

        var previewScroll = new ScrollViewer { Content = previewAreaGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(previewScroll, 1);
        _detailsPanel.Children.Add(previewScroll);

        // 底部应用栏
        var footerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 16, 0, 0),
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _statusTextBlock = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(_statusTextBlock, 0);
        footerGrid.Children.Add(_statusTextBlock);

        _applyButton = CreateButton(
            _context.I18n.T("window.library.btn.apply", "使用此形象"),
            isPrimary: true,
            onClick: OnApplyButtonClicked);
        _applyButton.Height = 36;
        _applyButton.Padding = new Thickness(20, 0, 20, 0);
        _applyButton.IsEnabled = false;
        Grid.SetColumn(_applyButton, 1);
        footerGrid.Children.Add(_applyButton);

        footerBorder.Child = footerGrid;
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.Children.Add(_emptySelectionBorder);
        rightGrid.Children.Add(_detailsPanel);
        Grid.SetRow(footerBorder, 1);
        rightGrid.Children.Add(footerBorder);
        rightContainer.Child = rightGrid;
        Grid.SetColumn(rightContainer, 1);
        bodyGrid.Children.Add(rightContainer);

        Grid.SetRow(bodyGrid, 1);
        mainGrid.Children.Add(bodyGrid);

        Content = mainGrid;

        Closed += OnWindowClosed;

        // 初始化加载列表
        RefreshLibraryList();
    }

    private void GoToPage(int page)
    {
        if (_isWindowClosed) return;
        int clamped = Math.Clamp(page, 0, _totalPages - 1);
        if (clamped == _currentPage) return;
        _currentPage = clamped;
        RefreshLibraryList();
    }

    /// <summary>
    /// 刷新形象库列表（支持分页，每页上限 24 个卡片）。
    /// </summary>
    public void RefreshLibraryList(string? selectItemId = null)
    {
        if (_isWindowClosed) return;

        var items = _libraryManager.GetItems();
        foreach (var oldCard in _cards)
        {
            oldCard.Dispose();
        }
        _cards.Clear();
        _listPanel.Children.Clear();

        if (items.Count == 0)
        {
            _emptyListBorder.Visibility = Visibility.Visible;
            _detailsPanel.Visibility = Visibility.Collapsed;
            _emptySelectionBorder.Visibility = Visibility.Visible;
            _paginationBorder.Visibility = Visibility.Collapsed;
            _selectedItem = null;
            _previewCts?.Cancel();
            _previewImage.Source = null;
            _statesPanel.Children.Clear();
            _applyButton.IsEnabled = false;
            UpdateLibraryTools();
            return;
        }

        _emptyListBorder.Visibility = Visibility.Collapsed;

        // 计算分页信息
        int totalCount = items.Count;
        _totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / MaxPageCards));

        if (!string.IsNullOrWhiteSpace(selectItemId))
        {
            int targetIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].Id, selectItemId, StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    break;
                }
            }
            if (targetIndex >= 0)
            {
                _currentPage = targetIndex / MaxPageCards;
            }
        }
        _currentPage = Math.Clamp(_currentPage, 0, _totalPages - 1);

        // 更新分页栏显示
        _paginationBorder.Visibility = totalCount > MaxPageCards ? Visibility.Visible : Visibility.Collapsed;
        _prevPageButton.IsEnabled = _currentPage > 0;
        _nextPageButton.IsEnabled = _currentPage < _totalPages - 1;
        string pageFmt = _context.I18n.T("window.library.page.format", "{0} / {1} 页");
        _pageInfoTextBlock.Text = string.Format(pageFmt, _currentPage + 1, _totalPages);

        string currentActiveDir = _controller.Assets.CurrentDirectory
            ?? PetPreference.AssetDirectory(_context);
        var activeItem = _libraryManager.FindItemByDirectory(currentActiveDir);
        string activeId = activeItem?.Id ?? PetPreference.ActiveLibraryId(_context);

        PetLibraryItem? itemToSelect = null;
        if (!string.IsNullOrWhiteSpace(selectItemId))
        {
            itemToSelect = items.FirstOrDefault(i => string.Equals(i.Id, selectItemId, StringComparison.OrdinalIgnoreCase));
        }
        itemToSelect ??= _selectedItem != null ? items.FirstOrDefault(i => string.Equals(i.Id, _selectedItem.Id, StringComparison.OrdinalIgnoreCase)) : null;
        itemToSelect ??= activeItem ?? items.FirstOrDefault();

        // 仅截取当前页最多 24 个条目构建卡片
        var pageItems = items.Skip(_currentPage * MaxPageCards).Take(MaxPageCards).ToList();

        foreach (var item in pageItems)
        {
            bool isActive = string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase);
            var card = new LibraryCardView(item, isActive, _libraryManager, _context, () => _isWindowClosed, () => SelectItem(item));
            _cards.Add(card);
            _listPanel.Children.Add(card.Visual);
        }

        if (itemToSelect != null)
        {
            SelectItem(itemToSelect);
        }
    }

    /// <summary>
    /// 选中特定形象并展示预览。
    /// </summary>
    public void SelectItem(PetLibraryItem item)
    {
        if (_isWindowClosed) return;
        _selectedItem = item;

        foreach (var card in _cards)
        {
            card.SetSelected(string.Equals(card.Item.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        }

        _emptySelectionBorder.Visibility = Visibility.Collapsed;
        _detailsPanel.Visibility = Visibility.Visible;

        _titleTextBlock.Text = item.Name;
        _titleTextBlock.ToolTip = item.Name;
        string metaFmt = _context.I18n.T("window.library.meta.format", "画布 {0}×{1} · {2}/12 个状态");
        _metaTextBlock.Text = string.Format(metaFmt, item.CanvasWidth, item.CanvasHeight, item.AvailableFrames.Count);

        // 更新状态按钮集合
        _statesPanel.Children.Clear();
        string missingSuffix = _context.I18n.T("window.library.missing.suffix", " (缺)");
        foreach (var (frameName, labelZh, labelEn) in PreviewStates)
        {
            bool exists = item.AvailableFrames.Contains(frameName, StringComparer.OrdinalIgnoreCase);
            string label = _context.I18n.T("frame." + Path.GetFileNameWithoutExtension(frameName), labelEn);
            if (!exists)
            {
                label += missingSuffix;
            }

            var btn = new Button
            {
                Content = label,
                Tag = frameName,
                Margin = new Thickness(3),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12,
                Background = string.Equals(_currentFrameName, frameName, StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(224, 242, 254))
                    : Brushes.White,
                BorderBrush = string.Equals(_currentFrameName, frameName, StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                    : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Foreground = exists
                    ? new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    : new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            string capturedFrame = frameName;
            btn.Click += (s, e) => SwitchPreviewFrame(capturedFrame);
            _statesPanel.Children.Add(btn);
        }

        // 默认预览 idle
        SwitchPreviewFrame("idle.png");

        UpdateApplyButtonState();
        UpdateLibraryTools();
    }

    private void UpdateLibraryTools()
    {
        var items = _libraryManager.GetItems();
        int position = _selectedItem == null ? -1 : items.ToList().FindIndex(item => item.Id == _selectedItem.Id);
        bool enabled = !_editingLibrary && !_isWindowClosed && position >= 0;
        _moveUpButton.IsEnabled = enabled && position > 0;
        _moveDownButton.IsEnabled = enabled && position < items.Count - 1;
        bool active = _selectedItem != null && _controller.IsLibraryItemProtected(_selectedItem);
        _removeButton.IsEnabled = enabled && !active;
        _removeButton.ToolTip = _context.I18n.T(active ? "library.edit.active" : "library.remove.help");
    }

    private async void EditSelectedItem(int offset)
    {
        if (_selectedItem == null || _editingLibrary || _isWindowClosed) return;
        string id = _selectedItem.Id;
        if (offset == 0 && MessageBox.Show(this, _context.I18n.T("library.remove.confirm"), _context.I18n.T("library.btn.remove"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _editingLibrary = true;
        _applyButton.IsEnabled = false;
        UpdateLibraryTools();
        try
        {
            var result = offset == 0 ? await _controller.RemoveLibraryItemAsync(id, _cts.Token) : await _libraryManager.MoveItemAsync(id, offset, _cts.Token);
            if (_isWindowClosed) return;
            if (result.Success) RefreshLibraryList(offset == 0 ? null : id);
            _statusTextBlock.Text = result.FormatMessage(_context);
            _statusTextBlock.Foreground = new SolidColorBrush(result.Success ? Color.FromRgb(22, 101, 52) : Color.FromRgb(185, 28, 28));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_isWindowClosed) _statusTextBlock.Text = string.Format(_context.I18n.T("library.edit.failed"), ex.Message);
        }
        finally
        {
            _editingLibrary = false;
            if (!_isWindowClosed) { UpdateLibraryTools(); UpdateApplyButtonState(); }
        }
    }

    private void SwitchPreviewFrame(string frameName)
    {
        _currentFrameName = frameName;
        if (_selectedItem == null || _isWindowClosed) return;

        var targetItem = _selectedItem;
        int currentGen = unchecked(++_previewGeneration);

        try
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
        }
        catch { }
        _previewCts = new CancellationTokenSource();
        var previewToken = _previewCts.Token;

        // 更新按钮选中样式
        foreach (UIElement child in _statesPanel.Children)
        {
            if (child is Button btn && btn.Tag is string fn)
            {
                bool isCur = string.Equals(fn, frameName, StringComparison.OrdinalIgnoreCase);
                btn.Background = isCur ? new SolidColorBrush(Color.FromRgb(224, 242, 254)) : Brushes.White;
                btn.BorderBrush = isCur ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
        }

        // 后台异步解码与冻结，支持取消，避免阻塞 UI 线程
        _ = Task.Run(() =>
        {
            try
            {
                if (previewToken.IsCancellationRequested || _isWindowClosed) return null;
                var bmp = _libraryManager.LoadPreviewFrame(targetItem, frameName);
                if (previewToken.IsCancellationRequested || _isWindowClosed) return null;
                if (bmp != null && !bmp.IsFrozen && bmp.CanFreeze)
                {
                    bmp.Freeze();
                }
                return bmp;
            }
            catch
            {
                return null;
            }
        }, previewToken).ContinueWith(t =>
        {
            if (_isWindowClosed || previewToken.IsCancellationRequested) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isWindowClosed || previewToken.IsCancellationRequested || _previewGeneration != currentGen || _selectedItem != targetItem)
                {
                    return; // 丢弃过期、已取消或窗口已关闭的预览回调
                }

                _previewImage.Source = t.Result;

                bool hasFrame = targetItem.AvailableFrames.Contains(frameName, StringComparer.OrdinalIgnoreCase);
                string stateLabel = _context.I18n.T("frame." + Path.GetFileNameWithoutExtension(frameName), frameName);

                string fallbackSuffix = _context.I18n.T("window.library.fallback.idle", "（未包含，回退至常驻）");
                _previewStateLabel.Text = hasFrame
                    ? stateLabel
                    : $"{stateLabel} {fallbackSuffix}";
            });
        }, TaskScheduler.Default);
    }

    private void UpdateApplyButtonState()
    {
        if (_selectedItem == null || _isWindowClosed) return;
        if (_editingLibrary) { _applyButton.IsEnabled = false; return; }

        string currentActiveDir = _controller.Assets.CurrentDirectory
            ?? PetPreference.AssetDirectory(_context);
        var activeItem = _libraryManager.FindItemByDirectory(currentActiveDir);
        string activeId = activeItem?.Id ?? PetPreference.ActiveLibraryId(_context);

        bool isCurrentlyActive = string.Equals(_selectedItem.Id, activeId, StringComparison.OrdinalIgnoreCase);

        if (isCurrentlyActive)
        {
            _applyButton.IsEnabled = false;
            _applyButton.Content = _context.I18n.T("window.library.btn.current", "当前正在使用");
            _statusTextBlock.Text = _context.I18n.T("window.library.status.active", "当前桌宠正在使用此形象。");
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        }
        else
        {
            _applyButton.IsEnabled = true;
            _applyButton.Content = _context.I18n.T("window.library.btn.apply", "使用此形象");
            _statusTextBlock.Text = _context.I18n.T("window.library.status.ready", "点击右侧按钮将当前桌宠切换至该形象。");
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }
    }

    private async void OnApplyButtonClicked()
    {
        if (_selectedItem == null || _isWindowClosed) return;

        var targetItem = _selectedItem;
        _applyButton.IsEnabled = false;
        _statusTextBlock.Text = _context.I18n.T("window.library.status.switching", "正在切换形象...");
        _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));

        try
        {
            var result = await _controller.ApplyLibraryItemAsync(targetItem, _cts.Token);

            if (_isWindowClosed) return;

            if (result.IsSuccess)
            {
                _statusTextBlock.Text = result.FormatMessage(_context);
                _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                RefreshLibraryList(targetItem.Id);
            }
            else if (result.IsBusy)
            {
                _applyButton.IsEnabled = true;
                _statusTextBlock.Text = result.FormatMessage(_context);
                _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14)); // Amber / Warning
            }
            else
            {
                _applyButton.IsEnabled = true;
                _statusTextBlock.Text = result.FormatMessage(_context);
                _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red / Danger
            }
        }
        catch (OperationCanceledException)
        {
            // 窗体关闭或主动取消时正常退出，不让异常逃逸至 WPF Dispatcher
            if (_isWindowClosed) return;
            _statusTextBlock.Text = _context.I18n.T("window.library.status.canceled", "已取消切换。");
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            _applyButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (_isWindowClosed) return;
            string errFmt = _context.I18n.T("window.library.error.apply", "切换异常：{0}");
            _statusTextBlock.Text = string.Format(errFmt, ex.Message);
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            _applyButton.IsEnabled = true;
        }
        finally
        {
            if (!_isWindowClosed && _selectedItem != null)
            {
                UpdateApplyButtonState();
            }
        }
    }

    private async void OnImportFolderClicked()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _context.I18n.T("window.library.dialog.title", "选择包含桌宠 PNG 关键帧的本地文件夹"),
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            string folder = dialog.FolderName;
            _importButton.IsEnabled = false;
            _statusTextBlock.Text = _context.I18n.T("window.library.status.importing", "正在校验与导入素材...");
            _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));

            try
            {
                var result = await _libraryManager.ImportFolderAsync(folder, _cts.Token);
                if (_isWindowClosed) return;

                if (result.Success)
                {
                    RefreshLibraryList(result.Item?.Id);
                    _statusTextBlock.Text = result.FormatMessage(_context);
                    _statusTextBlock.Foreground = result.IsDuplicate
                        ? new SolidColorBrush(Color.FromRgb(146, 64, 14))
                        : new SolidColorBrush(Color.FromRgb(22, 101, 52));
                }
                else
                {
                    _statusTextBlock.Text = result.FormatMessage(_context);
                    _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
            }
            catch (Exception ex)
            {
                if (_isWindowClosed) return;
                string exFmt = _context.I18n.T("window.library.error.exception", "导入异常：{0}");
                _statusTextBlock.Text = string.Format(exFmt, ex.Message);
                _statusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
            finally
            {
                if (!_isWindowClosed)
                {
                    _importButton.IsEnabled = true;
                }
            }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _isWindowClosed = true;
        try { _previewCts?.Cancel(); } catch { }
        try { _previewCts?.Dispose(); } catch { }
        _previewCts = null;

        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }

        // 释放预览位图与所有卡片资源
        _previewImage.Source = null;
        foreach (var card in _cards)
        {
            card.Dispose();
        }
        _cards.Clear();
        _listPanel.Children.Clear();
        _statesPanel.Children.Clear();
    }

    private static Button CreateButton(string text, bool isPrimary, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            Height = 32,
            Padding = new Thickness(14, 0, 14, 0),
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = isPrimary
                ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
                : Brushes.White,
            Foreground = isPrimary
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderBrush = isPrimary
                ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
                : new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
        };
        btn.Click += (s, e) => onClick();
        return btn;
    }

    private sealed class LibraryCardView : IDisposable
    {
        public readonly PetLibraryItem Item;
        public readonly Border Visual;
        private readonly Image _thumbnail;
        private readonly Border _badge;
        private readonly CancellationTokenSource _cardCts = new();
        private bool _isSelected;
        private bool _isDisposed;

        public LibraryCardView(
            PetLibraryItem item,
            bool isActive,
            PetLibraryManager manager,
            IPluginContext context,
            Func<bool> windowClosedCheck,
            Action onSelected)
        {
            Item = item;

            Visual = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 3, 0, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _thumbnail = new Image
            {
                Width = 44,
                Height = 44,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(_thumbnail, BitmapScalingMode.LowQuality);

            var token = _cardCts.Token;
            // 异步后台加载微缩缩略图，通过信号量限制最大并发解码数为 2，支持取消、冻结与关闭检查
            _ = Task.Run(async () =>
            {
                if (token.IsCancellationRequested || windowClosedCheck()) return null;
                bool acquired = false;
                try
                {
                    await _thumbnailThrottle.WaitAsync(token);
                    acquired = true;
                    if (token.IsCancellationRequested || windowClosedCheck()) return null;
                    var bmp = manager.LoadThumbnail(item, 88);
                    if (bmp != null && !bmp.IsFrozen && bmp.CanFreeze)
                    {
                        bmp.Freeze();
                    }
                    return bmp;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    if (acquired)
                    {
                        _thumbnailThrottle.Release();
                    }
                }
            }, token).ContinueWith(t =>
            {
                if (_isDisposed || token.IsCancellationRequested || windowClosedCheck()) return;
                if (t.IsCompletedSuccessfully && t.Result != null)
                {
                    Visual.Dispatcher.InvokeAsync(() =>
                    {
                        if (!_isDisposed && !token.IsCancellationRequested && !windowClosedCheck())
                        {
                            _thumbnail.Source = t.Result;
                        }
                    });
                }
            }, TaskScheduler.Default);

            Grid.SetColumn(_thumbnail, 0);
            grid.Children.Add(_thumbnail);

            var infoStack = new StackPanel
            {
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var nameBlock = new TextBlock
            {
                Text = item.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            infoStack.Children.Add(nameBlock);

            var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };

            _badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(220, 252, 231)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = isActive ? Visibility.Visible : Visibility.Collapsed,
            };
            var badgeText = new TextBlock
            {
                Text = context.I18n.T("window.library.badge.active", "当前使用"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61)),
            };
            _badge.Child = badgeText;
            bottomRow.Children.Add(_badge);

            string statesFmt = context.I18n.T("window.library.card.states", "{0} 状态");
            var frameCountBlock = new TextBlock
            {
                Text = string.Format(statesFmt, item.AvailableFrames.Count),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            bottomRow.Children.Add(frameCountBlock);

            infoStack.Children.Add(bottomRow);

            Grid.SetColumn(infoStack, 1);
            grid.Children.Add(infoStack);

            Visual.Child = grid;

            Visual.MouseLeftButtonUp += (s, e) => onSelected();
            Visual.Focusable = true;
            System.Windows.Automation.AutomationProperties.SetName(Visual, item.Name);
            Visual.ToolTip = item.Name;
            Visual.KeyDown += (s, e) =>
            {
                if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { onSelected(); e.Handled = true; }
            };
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            Visual.Background = selected
                ? new SolidColorBrush(Color.FromRgb(241, 245, 249))
                : Brushes.White;
            Visual.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                : new SolidColorBrush(Color.FromRgb(226, 232, 240));
        }

        public void Dispose()
        {
            _isDisposed = true;
            try { _cardCts.Cancel(); } catch { }
            try { _cardCts.Dispose(); } catch { }
            _thumbnail.Source = null;
        }
    }
}
