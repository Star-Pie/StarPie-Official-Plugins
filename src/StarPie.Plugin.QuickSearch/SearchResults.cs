using System.IO;

namespace StarPie.Plugin.QuickSearch;

internal sealed record SearchRow(string Name, string Path, bool IsDirectory, bool FromSelectedRoots = false);

internal sealed class SearchResults(IPluginContext context) : IDisposable
{
    private readonly object _gate = new();
    private FolderSearchIndex? _folders;
    private string[] _roots = [];
    private Task _preparation = Task.CompletedTask;
    private bool _stopped;

    internal FolderIndexStatus? FolderStatus { get { lock (_gate) return _folders?.Status; } }
    internal bool HasError { get { lock (_gate) return _preparation.IsFaulted || _folders?.Status.HasFailed == true; } }

    // A session belongs to the enabled plugin. The popup only cancels its own queries.
    // Keep first use idle until the user picks a scope, including after cancelling a picker.
    internal Task Configure(string[] roots, bool clearEmpty = false)
    {
        lock (_gate)
        {
            if (_stopped) return Task.CompletedTask;
            if (roots.Length == 0 && !clearEmpty && _folders == null) return Task.CompletedTask;
            _roots = roots;
            _folders ??= new FolderSearchIndex(Path.Combine(context.DataDirectory, "index"));
            var preparing = _folders.SetRootsAsync(roots, CancellationToken.None);
            if (!ReferenceEquals(_preparation, preparing))
            {
                _preparation = preparing;
                _ = preparing.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            return _preparation;
        }
    }

    internal bool IsCurrentTarget(SearchRow row) => !row.FromSelectedRoots ||
        (_roots.Any(root => FolderIndexSession.AtOrBelow(row.Path, root)) && FolderIndexSession.IsReachable(row.Path));

    internal async Task<(IReadOnlyList<SearchRow> Items, bool HasMore, string Source)> QueryAsync(
        string query, string category, int offset, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        FolderSearchIndex? folders;
        bool hasRoots;
        lock (_gate)
        {
            if (_stopped) return ([], false, "chooseRoots");
            folders = _folders;
            hasRoots = _roots.Length != 0;
        }
        // Fixed, user-invoked shell actions remain available before choosing an index scope.
        if (!hasRoots && (category == SearchCategories.SystemTools ||
            (query.Length == 0 && category == SearchCategories.All)))
        {
            var shortcuts = SystemShortcuts(query);
            return (shortcuts.Skip(offset).Take(count).ToArray(), shortcuts.Count - offset > count, "system");
        }
        if (!hasRoots || folders is null) return ([], false, "chooseRoots");
        var page = await folders.SearchAsync(query, category, offset, count, token);
        token.ThrowIfCancellationRequested();
        return (page.Items.Select(hit => new SearchRow(hit.Name.Length == 0 ? hit.FullPath : hit.Name, hit.FullPath,
            hit.IsDirectory, FromSelectedRoots: true)).ToArray(), page.HasMore, "folderIndex");
    }

    private List<SearchRow> SystemShortcuts(string query)
    {
        var rows = new List<SearchRow>();
        string system = Environment.SystemDirectory;
        Add("taskmgr", "Task Manager", Path.Combine(system, "Taskmgr.exe"));
        Add("control", "Control Panel", Path.Combine(system, "control.exe"));
        Add("calculator", "Calculator", Path.Combine(system, "calc.exe"));
        Add("explorer", "File Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows,
            Environment.SpecialFolderOption.DoNotVerify), "explorer.exe"));
        return rows;

        void Add(string key, string english, string path)
        {
            string name = context.I18n.T(key);
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                english.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase))
                rows.Add(new SearchRow(name, path, false));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _folders?.Dispose();
            _folders = null;
        }
    }
}
