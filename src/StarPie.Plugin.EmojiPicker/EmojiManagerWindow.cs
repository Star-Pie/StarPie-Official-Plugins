using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.EmojiPicker;

internal sealed class EmojiManagerWindow : Window
{
    private readonly IPluginContext _context;
    private readonly IHostInputPanelService _panels;
    private readonly EmojiLibraryStorage _storage;
    private EmojiLibraryDocument _doc;
    private readonly EmojiImageCache _imageCache;
    private readonly EmojiInputDispatcher _dispatcher;
    private readonly Action<EmojiLibraryDocument> _onDocumentUpdated;
    private readonly EmojiUiTheme _theme;

    private readonly StackPanel _navPanel = new();
    private readonly Border _recoveryBanner = new();
    private readonly TextBlock _recoveryText = new();
    private readonly Grid _contentHost = new();
    private readonly TextBox _filterBox;
    private string _section = "all"; // all | fav | kaomoji | prefs | grp:<id>
    private string _filter = "";
    private bool _closed;

    internal EmojiManagerWindow(
        IPluginContext context,
        IHostInputPanelService panels,
        EmojiLibraryStorage storage,
        EmojiLibraryDocument doc,
        EmojiImageCache imageCache,
        EmojiInputDispatcher dispatcher,
        Action<EmojiLibraryDocument> onDocumentUpdated)
    {
        _context = context;
        _panels = panels;
        _storage = storage;
        _doc = doc;
        _imageCache = imageCache;
        _dispatcher = dispatcher;
        _onDocumentUpdated = onDocumentUpdated;
        _theme = EmojiUiTheme.From(panels.GetAppearance());

        Title = EmojiTexts.T(context, "mgrTitle");
        Width = 840;
        Height = 560;
        MinWidth = 680;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _theme.ApplyTo(this);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var navBorder = new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(10, 12, 10, 12),
                Content = _navPanel
            }
        };
        root.Children.Add(navBorder);

        var rightGrid = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // recovery banner
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _recoveryBanner.Background = _theme.DangerSoft;
        _recoveryBanner.BorderBrush = _theme.Danger;
        _recoveryBanner.BorderThickness = new Thickness(1);
        _recoveryBanner.CornerRadius = new CornerRadius(8);
        _recoveryBanner.Padding = new Thickness(12, 8, 12, 8);
        _recoveryBanner.Margin = new Thickness(0, 0, 0, 10);
        _recoveryBanner.Visibility = _doc.RequiresRecovery ? Visibility.Visible : Visibility.Collapsed;

        var recRow = new DockPanel();
        var rebuildBtn = new Button
        {
            Content = EmojiTexts.T(context, "rebuildBackup"),
            Name = "RebuildLibraryButton",
            Margin = new Thickness(10, 0, 0, 0)
        };
        DockPanel.SetDock(rebuildBtn, Dock.Right);
        rebuildBtn.Click += (_, _) =>
        {
            _doc = _storage.RebuildAfterRecovery(EmojiTexts.T(_context, "defaultGroupCats"));
            _recoveryBanner.Visibility = Visibility.Collapsed;
            _onDocumentUpdated(_doc);
            RenderNav();
            RenderContent();
        };
        _recoveryText.Text = EmojiTexts.T(context, "statusLoadFailed");
        _recoveryText.Foreground = _theme.Danger;
        _recoveryText.TextWrapping = TextWrapping.Wrap;
        _recoveryText.VerticalAlignment = VerticalAlignment.Center;
        recRow.Children.Add(rebuildBtn);
        recRow.Children.Add(_recoveryText);
        _recoveryBanner.Child = recRow;

        rightGrid.Children.Add(_recoveryBanner);
        Grid.SetRow(_contentHost, 1);
        rightGrid.Children.Add(_contentHost);

        _filterBox = new TextBox { Name = "ManagerFilterBox", Height = 30, Width = 152 };
        _filterBox.TextChanged += (_, _) =>
        {
            _filter = _filterBox.Text.Trim();
            RenderListBodyOnly();
        };

        Grid.SetColumn(rightGrid, 1);
        root.Children.Add(rightGrid);

        AllowDrop = true;
        Drop += OnManagerDrop;
        Closed += (_, _) => _closed = true;

        Content = root;
        RenderNav();
        RenderContent();
    }

    internal void ReloadDocument(EmojiLibraryDocument doc)
    {
        if (_closed) return;
        _doc = doc;
        _recoveryBanner.Visibility = _doc.RequiresRecovery ? Visibility.Visible : Visibility.Collapsed;
        RenderNav();
        RenderContent();
    }

    private void CommitChange(bool rebuildIfHealthy = true)
    {
        if (_doc.RequiresRecovery) return;
        if (rebuildIfHealthy)
        {
            try { _storage.Save(_doc); } catch { }
        }
        _onDocumentUpdated(_doc);
        RenderNav();
    }

    private void RenderNav()
    {
        _navPanel.Children.Clear();
        int favCount = _doc.Stickers.Count(s => s.IsFavorite) + _doc.Kaomojis.Count(k => k.IsFavorite);
        List<StickerGroup> managed = _doc.Groups.Where(g => !g.IsMounted).ToList();
        List<StickerGroup> mounted = _doc.Groups.Where(g => g.IsMounted).ToList();

        _navPanel.Children.Add(MakeNavButton("all", "🖼️", EmojiTexts.T(_context, "mgrNavAll"), _doc.Stickers.Count));
        _navPanel.Children.Add(MakeNavButton("fav", "⭐", EmojiTexts.T(_context, "mgrNavFav"), favCount));
        _navPanel.Children.Add(MakeNavButton("kaomoji", "✨", EmojiTexts.T(_context, "mgrNavKaomoji"), _doc.Kaomojis.Count));

        // Managed Groups section
        _navPanel.Children.Add(MakeSectionHead(EmojiTexts.T(_context, "mgrNavGroups"), EmojiTexts.T(_context, "mgrNewBtn"), () =>
        {
            string? name = EmojiDialogs.PromptGroupName(_context, _theme, this, EmojiTexts.T(_context, "grpNew"), "");
            if (string.IsNullOrEmpty(name)) return;
            StickerGroup grp = _storage.CreateManagedGroup(_doc, name);
            _section = "grp:" + grp.Id;
            CommitChange();
            RenderContent();
        }));

        foreach (StickerGroup g in managed)
        {
            int cnt = _doc.Stickers.Count(s => string.Equals(s.GroupId, g.Id, StringComparison.OrdinalIgnoreCase));
            string secKey = "grp:" + g.Id;
            bool active = _section == secKey;
            var row = new Border
            {
                Background = active ? _theme.AccentSoft : Brushes.Transparent,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(0, 1, 0, 1)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = $"📁 {g.Name}",
                    Foreground = active ? _theme.Accent : _theme.TextSecondary,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Template = _theme.FlatTileTemplate(5)
            };
            leftBtn.Click += (_, _) => { _section = secKey; RenderNav(); RenderContent(); };
            grid.Children.Add(leftBtn);

            var rightOps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            StickerGroup captured = g;
            rightOps.Children.Add(MakeMiniIconLink("✏️", EmojiTexts.T(_context, "rename"), () =>
            {
                string? next = EmojiDialogs.PromptGroupName(_context, _theme, this, EmojiTexts.T(_context, "rename"), captured.Name);
                if (string.IsNullOrEmpty(next)) return;
                captured.Name = next;
                CommitChange();
                RenderContent();
            }));
            if (managed.Count > 1)
            {
                rightOps.Children.Add(MakeMiniIconLink("🗑️", EmojiTexts.T(_context, "delete"), () =>
                {
                    if (_storage.DeleteManagedGroup(_doc, captured.Id))
                    {
                        if (_section == secKey) _section = "all";
                        CommitChange();
                        RenderContent();
                    }
                }, _theme.Danger));
            }
            rightOps.Children.Add(new TextBlock
            {
                Text = cnt.ToString(),
                Foreground = _theme.TextMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            });
            Grid.SetColumn(rightOps, 1);
            grid.Children.Add(rightOps);

            row.Child = grid;
            _navPanel.Children.Add(row);
        }

        // Mounted External Folders section
        _navPanel.Children.Add(MakeSectionHead(EmojiTexts.T(_context, "mgrNavMounts"), EmojiTexts.T(_context, "mgrMountBtn"), MountFolderAction));

        foreach (StickerGroup m in mounted)
        {
            int cnt = _doc.Stickers.Count(s => string.Equals(s.GroupId, m.Id, StringComparison.OrdinalIgnoreCase));
            string secKey = "grp:" + m.Id;
            bool active = _section == secKey;
            var card = new Border
            {
                Background = active ? _theme.AccentSoft : _theme.WindowBg,
                BorderBrush = active ? _theme.Accent : _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 4)
            };
            var stack = new StackPanel();
            var top = new DockPanel();
            var countTb = new TextBlock { Text = cnt.ToString(), Foreground = _theme.TextMuted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(countTb, Dock.Right);
            var titleBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock
                {
                    Text = $"🔗 {m.Name}",
                    Foreground = active ? _theme.Accent : _theme.TextPrimary,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Template = _theme.FlatTileTemplate(4)
            };
            titleBtn.Click += (_, _) => { _section = secKey; RenderNav(); RenderContent(); };
            top.Children.Add(countTb);
            top.Children.Add(titleBtn);
            stack.Children.Add(top);

            stack.Children.Add(new TextBlock
            {
                Text = m.SourcePath,
                ToolTip = m.SourcePath,
                Foreground = _theme.TextMuted,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 4)
            });

            var actions = new DockPanel();
            var pill = new Border
            {
                Background = _theme.Surface,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock { Text = EmojiTexts.T(_context, "mgrNoCopyBadge"), Foreground = _theme.TextMuted, FontSize = 9.5 }
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            StickerGroup captured = m;
            btns.Children.Add(MakeMiniIconLink(EmojiTexts.T(_context, "mgrRefresh"), "", () =>
            {
                _storage.ScanMountedFolder(_doc, captured);
                CommitChange();
                RenderContent();
            }, _theme.Accent));
            btns.Children.Add(MakeMiniIconLink(EmojiTexts.T(_context, "mgrUnmount"), "", () =>
            {
                _storage.UnmountFolder(_doc, captured.Id);
                if (_section == secKey) _section = "all";
                CommitChange();
                RenderContent();
            }, _theme.Danger));
            DockPanel.SetDock(btns, Dock.Right);
            actions.Children.Add(btns);
            actions.Children.Add(pill);
            stack.Children.Add(actions);

            card.Child = stack;
            _navPanel.Children.Add(card);
        }

        _navPanel.Children.Add(new Border
        {
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 6)
        });
        _navPanel.Children.Add(MakeNavButton("prefs", "⚙️", EmojiTexts.T(_context, "mgrNavPrefs"), null));
    }

    private Button MakeNavButton(string secKey, string icon, string label, int? count)
    {
        bool active = _section == secKey;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = $"{icon} {label}",
            Foreground = active ? _theme.Accent : _theme.TextSecondary,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (count.HasValue)
        {
            var cnt = new TextBlock
            {
                Text = count.Value.ToString(),
                Foreground = _theme.TextMuted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(cnt, 1);
            grid.Children.Add(cnt);
        }

        var btn = new Button
        {
            Background = active ? _theme.AccentSoft : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 7, 8, 7),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 1, 0, 1),
            Content = grid,
            Template = _theme.FlatTileTemplate(7)
        };
        btn.Click += (_, _) => { _section = secKey; RenderNav(); RenderContent(); };
        return btn;
    }

    private UIElement MakeSectionHead(string title, string actionLabel, Action onClick)
    {
        var dp = new DockPanel { Margin = new Thickness(4, 12, 2, 4) };
        var link = new Button
        {
            Content = actionLabel,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = _theme.Accent,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(4, 1, 4, 1),
            Template = _theme.FlatTileTemplate(4)
        };
        link.Click += (_, _) => onClick();
        DockPanel.SetDock(link, Dock.Right);
        dp.Children.Add(link);
        dp.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = _theme.TextMuted,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return dp;
    }

    private Button MakeMiniIconLink(string text, string tip, Action onClick, SolidColorBrush? fg = null)
    {
        var btn = new Button
        {
            Content = text,
            ToolTip = string.IsNullOrEmpty(tip) ? null : tip,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = fg ?? _theme.TextMuted,
            FontSize = 11,
            Padding = new Thickness(4, 1, 4, 1),
            Template = _theme.FlatTileTemplate(4)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private ScrollViewer? _listScroller;
    private StackPanel? _listStack;

    private void RenderContent()
    {
        _contentHost.Children.Clear();
        _contentHost.RowDefinitions.Clear();

        if (_section == "prefs")
        {
            RenderPreferencesPane();
            return;
        }

        _contentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _contentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _filterBox.Margin = new Thickness(0, 0, 8, 4);
        _filterBox.ToolTip = EmojiTexts.T(_context, "mgrFilterHint");
        if (_filterBox.Parent is Panel oldParent) oldParent.Children.Remove(_filterBox);
        toolbar.Children.Add(_filterBox);

        toolbar.Children.Add(MakeActionButton(EmojiTexts.T(_context, "mgrImportFilesBtn"), true, ImportFilesAction));
        toolbar.Children.Add(MakeActionButton(EmojiTexts.T(_context, "mgrImportFolderBtn"), false, ImportFolderAction));
        toolbar.Children.Add(MakeActionButton(EmojiTexts.T(_context, "mgrSaveClipBtn"), false, SaveClipboardAction));
        toolbar.Children.Add(MakeActionButton(EmojiTexts.T(_context, "mgrAddKaoBtn"), false, AddKaomojiAction));

        _contentHost.Children.Add(toolbar);

        _listStack = new StackPanel();
        _listScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _listStack
        };
        Grid.SetRow(_listScroller, 1);
        _contentHost.Children.Add(_listScroller);

        RenderListBodyOnly();
    }

    private void RenderListBodyOnly()
    {
        if (_listStack == null) return;
        _listStack.Children.Clear();

        if (_section == "kaomoji")
        {
            var items = EmojiSearchEngine.SearchKaomojis(_doc, _filter);
            if (items.Count == 0)
            {
                _listStack.Children.Add(MakeEmptyNote());
                return;
            }
            foreach (KaomojiEntry k in items)
                _listStack.Children.Add(BuildKaomojiRow(k));
            return;
        }

        string? groupFilter = _section == "fav" ? "__fav__" : (_section.StartsWith("grp:", StringComparison.Ordinal) ? _section[4..] : "__all__");
        var stickers = EmojiSearchEngine.SearchStickers(_doc, _filter, groupFilter);
        if (stickers.Count == 0)
        {
            _listStack.Children.Add(MakeEmptyNote());
            return;
        }
        foreach (CustomSticker s in stickers)
            _listStack.Children.Add(BuildStickerRow(s));
    }

    private Border BuildStickerRow(CustomSticker s)
    {
        var row = new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = 50,
            Height = 50,
            CornerRadius = new CornerRadius(7),
            Background = _theme.WindowBg,
            BorderBrush = _theme.StickerOutline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3)
        };
        var thumbGrid = new Grid();
        BitmapSource? bmp = _imageCache.GetThumbnail(_storage.ResolveStickerPath(s), 96);
        if (bmp != null)
            thumbGrid.Children.Add(new Image { Source = bmp, Stretch = Stretch.Uniform });
        else
            thumbGrid.Children.Add(new TextBlock { Text = "🖼️", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        thumbGrid.Children.Add(new Border
        {
            Background = _theme.TagOverlayBg,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 0, 3, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = s.Format, FontSize = 8, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
        });
        thumb.Child = thumbGrid;
        grid.Children.Add(thumb);

        var nameBox = new TextBox { Text = s.Name, Height = 28 };
        nameBox.LostFocus += (_, _) => { s.Name = nameBox.Text.Trim(); CommitChange(); };
        var nameCol = MakeFieldCol(EmojiTexts.T(_context, "colName"), nameBox);
        Grid.SetColumn(nameCol, 1);
        grid.Children.Add(nameCol);

        var kwBox = new TextBox { Text = s.Keywords, Height = 28 };
        kwBox.LostFocus += (_, _) => { s.Keywords = kwBox.Text.Trim(); CommitChange(); };
        var kwCol = MakeFieldCol(EmojiTexts.T(_context, "colKeywords"), kwBox);
        Grid.SetColumn(kwCol, 2);
        grid.Children.Add(kwCol);

        FrameworkElement grpControl;
        if (s.IsMounted)
        {
            grpControl = new TextBox { Text = EmojiTexts.T(_context, "mountTag"), IsReadOnly = true, Height = 28, Opacity = 0.7 };
        }
        else
        {
            var cb = new ComboBox { Height = 28 };
            List<StickerGroup> managed = _doc.Groups.Where(g => !g.IsMounted).ToList();
            int sel = 0;
            for (int i = 0; i < managed.Count; i++)
            {
                cb.Items.Add(new ComboBoxItem { Tag = managed[i].Id, Content = managed[i].Name });
                if (string.Equals(managed[i].Id, s.GroupId, StringComparison.OrdinalIgnoreCase)) sel = i;
            }
            if (cb.Items.Count > 0) cb.SelectedIndex = sel;
            cb.SelectionChanged += (_, _) =>
            {
                if (cb.SelectedItem is ComboBoxItem ci && ci.Tag is string gid)
                {
                    s.GroupId = gid;
                    CommitChange();
                }
            };
            grpControl = cb;
        }
        var grpCol = MakeFieldCol(s.IsMounted ? EmojiTexts.T(_context, "colSource") : EmojiTexts.T(_context, "colGroup"), grpControl);
        Grid.SetColumn(grpCol, 3);
        grid.Children.Add(grpCol);

        var ops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0) };
        ops.Children.Add(MakeOpBtn("↑", EmojiTexts.T(_context, "moveUp"), () => MoveSticker(s, -1)));
        ops.Children.Add(MakeOpBtn("↓", EmojiTexts.T(_context, "moveDown"), () => MoveSticker(s, +1)));
        ops.Children.Add(MakeOpBtn("★", EmojiTexts.T(_context, "ctxFavorite"), () =>
        {
            s.IsFavorite = !s.IsFavorite;
            CommitChange();
            RenderListBodyOnly();
        }, s.IsFavorite ? _theme.FavoriteDot : null));
        if (!s.IsMounted)
        {
            ops.Children.Add(MakeOpBtn("🗑️", EmojiTexts.T(_context, "delete"), () =>
            {
                _storage.DeleteSticker(_doc, s);
                CommitChange();
                RenderListBodyOnly();
            }, _theme.Danger));
        }
        Grid.SetColumn(ops, 4);
        grid.Children.Add(ops);

        row.Child = grid;
        return row;
    }

    private Border BuildKaomojiRow(KaomojiEntry k)
    {
        var row = new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtBox = new TextBox { Text = k.Text, Height = 28 };
        txtBox.LostFocus += (_, _) => { k.Text = txtBox.Text; CommitChange(); };
        var col0 = MakeFieldCol(EmojiTexts.T(_context, "colText"), txtBox);
        grid.Children.Add(col0);

        var nameBox = new TextBox { Text = k.Name, Height = 28 };
        nameBox.LostFocus += (_, _) => { k.Name = nameBox.Text.Trim(); CommitChange(); };
        var col1 = MakeFieldCol(EmojiTexts.T(_context, "colName"), nameBox);
        Grid.SetColumn(col1, 1);
        grid.Children.Add(col1);

        var kwBox = new TextBox { Text = k.Keywords, Height = 28 };
        kwBox.LostFocus += (_, _) => { k.Keywords = kwBox.Text.Trim(); CommitChange(); };
        var col2 = MakeFieldCol(EmojiTexts.T(_context, "colKeywords"), kwBox);
        Grid.SetColumn(col2, 2);
        grid.Children.Add(col2);

        var ops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0) };
        ops.Children.Add(MakeOpBtn("★", EmojiTexts.T(_context, "ctxFavorite"), () =>
        {
            k.IsFavorite = !k.IsFavorite;
            CommitChange();
            RenderListBodyOnly();
        }, k.IsFavorite ? _theme.FavoriteDot : null));
        ops.Children.Add(MakeOpBtn("🗑️", EmojiTexts.T(_context, "delete"), () =>
        {
            _doc.Kaomojis.Remove(k);
            CommitChange();
            RenderListBodyOnly();
        }, _theme.Danger));
        Grid.SetColumn(ops, 3);
        grid.Children.Add(ops);

        row.Child = grid;
        return row;
    }

    private void RenderPreferencesPane()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = EmojiTexts.T(_context, "mgrNavPrefs"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = _theme.TextPrimary,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var modeCombo = new ComboBox { Height = 30, Width = 160 };
        foreach (EmojiPickerMode m in Enum.GetValues<EmojiPickerMode>())
        {
            string key = m switch { EmojiPickerMode.Emoji => "modeEmoji", EmojiPickerMode.Custom => "modeCustom", EmojiPickerMode.Kaomoji => "modeKaomoji", _ => "modeAll" };
            modeCombo.Items.Add(new ComboBoxItem { Tag = m.ToString(), Content = EmojiTexts.T(_context, key) });
        }
        modeCombo.SelectedIndex = Enum.TryParse<EmojiPickerMode>(_doc.Preferences.DefaultMode, true, out var curMode) ? (int)curMode : 0;
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (modeCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
            {
                _doc.Preferences.DefaultMode = tag;
                SyncPreferenceToHostSettings("defaultMode", tag);
                CommitChange();
            }
        };
        stack.Children.Add(MakePrefCard(EmojiTexts.T(_context, "fieldDefaultMode"), modeCombo));

        stack.Children.Add(MakeBoolPrefCard(EmojiTexts.T(_context, "fieldAutoClose"), _doc.Preferences.AutoCloseOnPick, v =>
        {
            _doc.Preferences.AutoCloseOnPick = v;
            SyncPreferenceToHostSettings("autoCloseOnPick", v ? "true" : "false");
            CommitChange();
        }));
        stack.Children.Add(MakeBoolPrefCard(EmojiTexts.T(_context, "fieldGifHover"), _doc.Preferences.GifHoverPlay, v =>
        {
            _doc.Preferences.GifHoverPlay = v;
            SyncPreferenceToHostSettings("gifHoverPlay", v ? "true" : "false");
            CommitChange();
        }));
        stack.Children.Add(MakeBoolPrefCard(EmojiTexts.T(_context, "fieldTrackRecent"), _doc.Preferences.TrackRecent, v =>
        {
            _doc.Preferences.TrackRecent = v;
            SyncPreferenceToHostSettings("trackRecent", v ? "true" : "false");
            CommitChange();
        }));
        stack.Children.Add(MakeBoolPrefCard(EmojiTexts.T(_context, "fieldEmojiClipFallback"), _doc.Preferences.EmojiClipboardFallback, v =>
        {
            _doc.Preferences.EmojiClipboardFallback = v;
            SyncPreferenceToHostSettings("emojiClipboardFallback", v ? "true" : "false");
            CommitChange();
        }));

        var openFolderBtn = new Button { Content = EmojiTexts.T(_context, "mgrOpenDataDir"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        openFolderBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(_storage.DataDirectory);
            _context.Host.OpenFolder(_storage.DataDirectory);
        };
        stack.Children.Add(openFolderBtn);

        _contentHost.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack
        });
    }

    private void SyncPreferenceToHostSettings(string key, string value)
    {
        try
        {
            _context.Settings.Set(key, value);
            _context.Settings.Save();
        }
        catch { }
    }

    private Border MakeBoolPrefCard(string title, bool current, Action<bool> onChanged)
    {
        var cb = new CheckBox { IsChecked = current, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => onChanged(true);
        cb.Unchecked += (_, _) => onChanged(false);
        return MakePrefCard(title, cb);
    }

    private Border MakePrefCard(string title, UIElement control)
    {
        var dp = new DockPanel();
        DockPanel.SetDock(control, Dock.Right);
        dp.Children.Add(control);
        dp.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = _theme.TextPrimary,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = _theme.Surface,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = dp
        };
    }

    private StackPanel MakeFieldCol(string label, UIElement input)
    {
        var sp = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = _theme.TextMuted, FontSize = 10, Margin = new Thickness(0, 0, 0, 2) });
        sp.Children.Add(input);
        return sp;
    }

    private Button MakeActionButton(string label, bool primary, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 30,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 4),
            Background = primary ? _theme.Accent : _theme.Surface,
            Foreground = primary ? _theme.AccentFg : _theme.TextPrimary,
            BorderBrush = primary ? _theme.Accent : _theme.Border
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Button MakeOpBtn(string symbol, string tip, Action onClick, SolidColorBrush? fg = null)
    {
        var btn = new Button
        {
            Content = symbol,
            ToolTip = tip,
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Foreground = fg ?? _theme.TextSecondary
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private TextBlock MakeEmptyNote() => new()
    {
        Text = EmojiTexts.T(_context, "mgrEmptyList"),
        Foreground = _theme.TextMuted,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 36, 0, 0)
    };

    private string TargetManagedGroupId()
    {
        if (_section.StartsWith("grp:", StringComparison.Ordinal))
        {
            string gid = _section[4..];
            StickerGroup? g = _doc.Groups.FirstOrDefault(x => string.Equals(x.Id, gid, StringComparison.OrdinalIgnoreCase) && !x.IsMounted);
            if (g != null) return g.Id;
        }
        return _storage.EnsureManagedGroup(_doc, EmojiTexts.T(_context, "defaultGroupCats")).Id;
    }

    private void MoveSticker(CustomSticker s, int delta)
    {
        int idx = _doc.Stickers.IndexOf(s);
        int next = idx + delta;
        if (idx < 0 || next < 0 || next >= _doc.Stickers.Count) return;
        (_doc.Stickers[idx], _doc.Stickers[next]) = (_doc.Stickers[next], _doc.Stickers[idx]);
        CommitChange();
        RenderListBodyOnly();
    }

    private void ImportFilesAction()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif"
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
        string gid = TargetManagedGroupId();
        var summary = _storage.ImportFiles(_doc, dlg.FileNames, gid);
        if (summary.ImportedCount > 0)
        {
            _section = "grp:" + gid;
            CommitChange();
            RenderContent();
        }
    }

    private void ImportFolderAction()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() != true) return;
        var (grp, summary) = _storage.ImportFolder(_doc, dlg.FolderName);
        if (grp != null && summary.ImportedCount > 0)
        {
            _section = "grp:" + grp.Id;
            CommitChange();
            RenderContent();
        }
    }

    private void SaveClipboardAction()
    {
        ClipboardImageReadResult read = _dispatcher.ReadImageFromClipboard();
        List<StickerGroup> managed = _doc.Groups.Where(g => !g.IsMounted).ToList();
        var res = EmojiDialogs.ShowClipboardFavoriteDialog(_context, _theme, this, read, managed, TargetManagedGroupId());
        if (res == null || read.Bytes == null) return;
        if (_storage.ImportFromBytes(_doc, read.Bytes, res.Value.Name, res.Value.Keywords, res.Value.GroupId) != null)
        {
            _section = "grp:" + res.Value.GroupId;
            CommitChange();
            RenderContent();
        }
    }

    private void AddKaomojiAction()
    {
        var added = EmojiDialogs.ShowAddKaomojiDialog(_context, _theme, this);
        if (added == null) return;
        _doc.Kaomojis.Insert(0, new KaomojiEntry
        {
            Id = "k_" + Guid.NewGuid().ToString("N")[..8],
            Text = added.Value.Text,
            Name = added.Value.Name,
            Keywords = string.IsNullOrWhiteSpace(added.Value.Keywords) ? added.Value.Name : added.Value.Keywords,
            Category = added.Value.Category
        });
        _section = "kaomoji";
        CommitChange();
        RenderContent();
    }

    private void MountFolderAction()
    {
        var res = EmojiDialogs.ShowMountFolderDialog(_context, _theme, this);
        if (res == null) return;
        StickerGroup? grp = _storage.MountFolder(_doc, res.Value.Path, res.Value.DisplayName);
        if (grp != null)
        {
            _section = "grp:" + grp.Id;
            CommitChange();
            RenderContent();
        }
    }

    private void OnManagerDrop(object sender, DragEventArgs e)
    {
        if (_section == "prefs" || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        string gid = TargetManagedGroupId();
        var summary = _storage.ImportFiles(_doc, paths.Where(File.Exists), gid);
        if (summary.ImportedCount > 0)
        {
            _section = "grp:" + gid;
            CommitChange();
            RenderContent();
        }
    }
}
