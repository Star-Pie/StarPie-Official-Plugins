using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace StarPie.Plugin.QuickSearch;

internal sealed partial class QuickSearchWindow : Window
{
    private readonly IPluginContext _context;
    private readonly SearchPreferences _preferences;
    private readonly SearchResults _search;
    private readonly List<string> _roots;
    private CancellationTokenSource? _query;
    private CancellationTokenSource? _scopeWait;
    private HwndSource? _source;
    private string _category = SearchCategories.All;
    private bool _pinned, _closed, _choosingFolder, _ready, _resizing;
    private int _page;
    private long _searchRevision = -1;
    private readonly DispatcherTimer _healthTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _statusLine = "";
    private const int PageSize = 40;

    internal QuickSearchWindow(IPluginContext context, PluginActionInput input, SearchResults search)
    {
        _context = context;
        _preferences = new SearchPreferences(context.Settings, input);
        _roots = _preferences.Roots().ToList();
        _search = search;
        _pinned = _preferences.Pinned;
        _healthTimer.Tick += RefreshHealth;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        UseLayoutRounding = true;
        BuildLayout();
        RefreshLanguage();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(HandleSizeMove);
        };
        Deactivated += (_, _) =>
        {
            if (_ready && !_closed && !_pinned && !_choosingFolder && _menu?.IsOpen != true) Close();
        };
        PreviewKeyDown += OnKey;
    }

    private nint HandleSizeMove(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0231) _resizing = false; // WM_ENTERSIZEMOVE
        if (message == 0x0214) _resizing = true;  // WM_SIZING
        if (message == 0x0232 && _resizing)       // WM_EXITSIZEMOVE
        {
            _resizing = false;
            TryUserAction(() => { _preferences.SaveSize(ActualWidth, ActualHeight); return true; });
        }
        return 0;
    }

    internal void Present(int x, int y)
    {
        _ready = false;
        _category = SearchCategories.All;
        foreach (var (key, button) in _categoryButtons) MarkCategory(key, button);
        _input.Clear();
        _scope.Visibility = _roots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchWindowPlacement.AtPointer(this, x, y, _preferences.Width, _preferences.Height);
        Show();
        Activate();
        _input.Focus();
        Keyboard.Focus(_input);
        _ready = true;
        ConfigureScope();
    }

    internal void RefreshLanguage()
    {
        Title = T("title");
        RefreshPresentation();
        if (_menu is not null)
            foreach (var item in _menu.Items.OfType<MenuItem>()) item.Header = T((string)item.Tag);
        if (_ready) RequestPage(0);
    }

    private string T(string key) => _context.I18n.T(key);

    private async void ConfigureScope()
    {
        _scopeWait?.Cancel();
        var wait = new CancellationTokenSource();
        var waitToken = wait.Token;
        _scopeWait = wait;
        bool clear = _roots.Count == 0 && _preferences.PendingScopeClear;
        try
        {
            var ready = _search.Configure(_roots.ToArray(), clear);
            if (_roots.Count != 0) _healthTimer.Start();
            else _healthTimer.Stop();
            RequestPage(0);
            // Searching does not wait for preparation. A persisted or partial page can
            // be presented while the worker is still enumerating the selected roots.
            await ready.WaitAsync(waitToken);
            if (_closed || waitToken.IsCancellationRequested) return;
            if (clear) _preferences.ConfirmScopeCleared();
            RefreshHealth(null, EventArgs.Empty);
            // The first page can predate the worker's first publication. Replace even
            // an in-flight older query here; later batches are coalesced by the timer.
            if (_roots.Count > 0 && _search.FolderStatus is { } status) RefreshPublishedPage(status.Revision);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed && !waitToken.IsCancellationRequested)
            {
                _context.Log.Error("Quick Search index preparation failed.", ex);
                SetStatus(T(clear ? "clearFailed" : "indexFailed"));
            }
        }
        finally
        {
            if (ReferenceEquals(_scopeWait, wait)) _scopeWait = null;
            wait.Dispose();
        }
    }

    private async void RequestPage(int page, bool debounce = false, string? retainSelection = null)
    {
        if (!_ready || _closed) return;
        _query?.Cancel();
        var request = new CancellationTokenSource();
        _query = request;
        var token = request.Token;
        _page = page;
        _list.Items.Clear();
        _previous.IsEnabled = page > 0;
        _next.IsEnabled = false;
        SetStatus(T("searching"));
        try
        {
            if (debounce) await Task.Delay(180, token);
            _searchRevision = _search.FolderStatus?.Revision ?? -1;
            var result = await _search.QueryAsync(_input.Text.Trim(), _category, checked(page * PageSize), PageSize,
                token);
            if (token.IsCancellationRequested || _closed) return;
            foreach (var row in result.Items) AddResult(row);
            if (_list.Items.Count != 0)
            {
                _list.SelectedItem = retainSelection == null ? _list.Items[0] :
                    _list.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag is SearchRow row &&
                        string.Equals(row.Path, retainSelection, StringComparison.OrdinalIgnoreCase)) ?? _list.Items[0];
                if (retainSelection != null) _list.ScrollIntoView(_list.SelectedItem);
            }
            _next.IsEnabled = result.HasMore;
            string source = T(result.Source);
            SetStatus(result.Items.Count == 0 && result.Source != "chooseRoots" && _search.FolderStatus?.IsRefreshing != true
                ? $"{source} · {T("empty")}" : source);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closed)
            {
                _context.Log.Error("Quick Search query failed.", ex);
                SetStatus(T("failure"));
            }
        }
        finally
        {
            if (ReferenceEquals(_query, request)) _query = null;
            request.Dispose();
        }
    }

    private void SetStatus(string text)
    {
        _statusLine = text;
        RefreshHealth(null, EventArgs.Empty);
    }

    private void RefreshHealth(object? sender, EventArgs e)
    {
        var status = _search.FolderStatus;
        if (status is null)
        {
            _status.Text = _statusLine;
            return;
        }
        var details = new List<string> { _statusLine };
        if (_roots.Count != 0) details.Add(string.Format(T("indexedCount"), status.IndexedItemCount));
        if (_search.HasError)
        {
            string issue = T(_roots.Count == 0 ? "clearFailed" : "indexFailed");
            if (issue != _statusLine) details.Add(issue);
        }
        if (status.IsRefreshing) details.Add(T("refreshing"));
        if (status.UnavailableRootCount > 0) details.Add($"{T("unavailableRoots")} {status.UnavailableRootCount}");
        if (status.SkippedDirectoryCount > 0) details.Add($"{T("skippedDirs")} {status.SkippedDirectoryCount}");
        if (status.UnwatchedRootCount > 0) details.Add($"{T("unwatchedRoots")} {status.UnwatchedRootCount}");
        string value = string.Join(" · ", details);
        if (_status.Text != value) _status.Text = value;
        if (sender is DispatcherTimer && _query is null) RefreshPublishedPage(status.Revision);
    }

    private void RefreshPublishedPage(long revision)
    {
        if (!_ready || _closed || _menu?.IsOpen == true || revision == _searchRevision) return;
        string? selected = ((_list.SelectedItem as ListBoxItem)?.Tag as SearchRow)?.Path;
        RequestPage(_page, retainSelection: selected);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_menu?.IsOpen == true) return;
        if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) &&
            (_input.IsKeyboardFocusWithin || _list.IsKeyboardFocusWithin))
        {
            e.Handled = true;
            OpenSelectedMenu();
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        if (!_input.IsKeyboardFocusWithin && !_list.IsKeyboardFocusWithin) return;
        if (e.Key is Key.Up or Key.Down && _input.IsKeyboardFocusWithin)
        {
            int count = _list.Items.Count;
            if (count > 0)
            {
                _list.SelectedIndex = (_list.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1) + count) % count;
                _list.ScrollIntoView(_list.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !IsRowButton(e.OriginalSource))
        {
            if (_query is not null) RequestPage(0);
            else ExecuteRow(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "locate" : "open");
            e.Handled = true;
        }
    }

    private void ExecuteRow(string command)
    {
        if (_list.SelectedItem is ListBoxItem { Tag: SearchRow row }) ExecuteResultCommand(command, row);
    }

    private void ExecuteResultCommand(string command, SearchRow row)
    {
        if (_closed) return;
        bool copy = command is "copyPath" or "copyName";
        if (!_context.Info.HasCapability(copy ? PluginCapability.Clipboard : PluginCapability.Process))
        {
            SetStatus(T("permission"));
            return;
        }

        bool ok = TryUserAction(() =>
        {
            if (copy)
            {
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(row.Path));
                return _context.Host.SetClipboardText(command == "copyPath" || string.IsNullOrEmpty(name) ? row.Path : name);
            }
            // Recheck the selected path after refreshes and removable-drive changes.
            if (!Path.IsPathFullyQualified(row.Path) || !_search.IsCurrentTarget(row) ||
                !(row.IsDirectory ? Directory.Exists(row.Path) : File.Exists(row.Path))) return false;
            if (command == "runAdmin")
                return SupportsAdministrator(row) && _context.Host.LaunchWithMode(row.Path, ProcessLaunchMode.Administrator);
            // Host launch expands environment variables in its executable field. Shell paths
            // containing literal percent-delimited names must travel as a quoted argument.
            if (command == "locate" || Environment.ExpandEnvironmentVariables(row.Path) != row.Path)
            {
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                string argument = (command == "locate" ? "/select," : "") + "\"" + row.Path + "\"";
                return _context.Host.LaunchWithMode(explorer, ProcessLaunchMode.Default, argument);
            }
            return _context.Host.LaunchWithMode(row.Path, ProcessLaunchMode.Default);
        });
        if (!ok || _closed) return;
        if (copy) SetStatus(T("copied"));
        else Close();
    }

    private bool TryUserAction(Func<bool> action)
    {
        try
        {
            if (action()) return true;
        }
        catch (Exception ex) { _context.Log.Error("Quick Search operation failed.", ex); }
        SetStatus(T("failure"));
        return false;
    }

    private void ChooseFolders()
    {
        _choosingFolder = true;
        try
        {
            var picker = new OpenFolderDialog { Multiselect = true, Title = T("add") };
            if (picker.ShowDialog(this) == true && !_closed) AddRoots(picker.FolderNames);
        }
        finally { _choosingFolder = false; }
    }

    private async void AddLocalDrives()
    {
        var button = _labels["localDrives"];
        button.IsEnabled = false;
        try
        {
            string[] drives = await Task.Run(() => DriveInfo.GetDrives().Where(IsReadyFixedDrive)
                .Select(drive => drive.RootDirectory.FullName).ToArray());
            if (_closed) return;
            if (drives.Length == 0) SetStatus(T("noDrives"));
            else AddRoots(drives);
        }
        catch (Exception ex)
        {
            if (!_closed) { _context.Log.Error("Could not list local drives.", ex); SetStatus(T("failure")); }
        }
        finally { button.IsEnabled = true; }
    }

    private static bool IsReadyFixedDrive(DriveInfo drive)
    {
        try { return drive.DriveType == DriveType.Fixed && drive.IsReady; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private void AddStartMenu() => AddRoots(new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
    });

    private void AddRoots(IEnumerable<string> folders)
    {
        var next = _roots.Concat(folders.Where(path => !string.IsNullOrWhiteSpace(path)))
            .Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (next.Length != _roots.Count) UpdateRoots(next);
    }

    private void RemoveRoot()
    {
        if (_rootList.SelectedItem is string selected)
            UpdateRoots(_roots.Where(path => !string.Equals(path, selected, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    private void UpdateRoots(string[] roots)
    {
        if (!TryUserAction(() => { _preferences.SetRoots(roots); return true; })) return;
        _query?.Cancel();
        _roots.Clear();
        _roots.AddRange(roots);
        _rootList.ItemsSource = roots;
        ConfigureScope();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _ready = false;
        _scopeWait?.Cancel();
        _query?.Cancel();
        _healthTimer.Stop();
        _healthTimer.Tick -= RefreshHealth;
        _source?.RemoveHook(HandleSizeMove);
        _source = null;
        if (_menu is not null) _menu.IsOpen = false;
        _list.Items.Clear();
        base.OnClosed(e);
    }
}
