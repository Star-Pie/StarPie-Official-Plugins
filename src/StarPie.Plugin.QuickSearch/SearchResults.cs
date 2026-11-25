using System.IO;

namespace StarPie.Plugin.QuickSearch;

internal sealed record SearchRow(string Name, string Path, bool IsDirectory, bool FromSelectedRoots = false,
    long Size = 0, long LastWriteUtcTicks = 0);

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

    internal Task<(IReadOnlyList<SearchRow> Items, bool HasMore, string Source)> QueryAsync(
        string query, string category, int offset, int count, CancellationToken token) =>
        QueryAsync(query, category, offset, count, SearchQueryOptions.Default, token);

    internal async Task<(IReadOnlyList<SearchRow> Items, bool HasMore, string Source)> QueryAsync(
        string query, string category, int offset, int count, SearchQueryOptions options, CancellationToken token)
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
            var shortcuts = SystemShortcuts(query, options);
            return (shortcuts.Skip(offset).Take(count).ToArray(), shortcuts.Count - offset > count, "system");
        }
        if (!hasRoots || folders is null) return ([], false, "chooseRoots");
        var page = await folders.SearchAsync(query, category, offset, count, options, token);
        token.ThrowIfCancellationRequested();
        return (page.Items.Select(hit => new SearchRow(hit.Name.Length == 0 ? hit.FullPath : hit.Name, hit.FullPath,
            hit.IsDirectory, FromSelectedRoots: true, hit.Size, hit.LastWriteUtcTicks)).ToArray(), page.HasMore, "folderIndex");
    }

    private List<SearchRow> SystemShortcuts(string query, SearchQueryOptions options)
    {
        var rows = new List<SearchRow>();
        string system = Environment.SystemDirectory;
        Add("taskmgr", "Task Manager", Path.Combine(system, "Taskmgr.exe"));
        Add("control", "Control Panel", Path.Combine(system, "control.exe"));
        Add("calculator", "Calculator", Path.Combine(system, "calc.exe"));
        Add("explorer", "File Explorer", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows,
            Environment.SpecialFolderOption.DoNotVerify), "explorer.exe"));
        return SortShortcuts(rows, options);

        void Add(string key, string english, string path)
        {
            string name = context.I18n.T(key);
            if (!MatchesShortcut(name, english, path, query, options.MatchMode)) return;
            long size = 0;
            long ticks = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) { size = info.Length; ticks = info.LastWriteTimeUtc.Ticks; }
            }
            catch { }
            if (!MatchesShortcutFilters(path, size, ticks, options)) return;
            rows.Add(new SearchRow(name, path, false, Size: size, LastWriteUtcTicks: ticks));
        }
    }

    private static bool MatchesShortcut(string localizedName, string englishName, string path, string query, SearchMatchMode mode)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0) return true;
        string fileName = Path.GetFileName(path);
        var entry = FolderEntry.Split(path, false);
        if (mode == SearchMatchMode.Exact)
            return localizedName.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                   englishName.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                   entry.MatchesExact(q, q.LastIndexOf(Path.DirectorySeparatorChar) + 1);
        if (mode == SearchMatchMode.Related)
        {
            foreach (string token in q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int boundary = token.LastIndexOf(Path.DirectorySeparatorChar) + 1;
                bool matched = localizedName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    englishName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    entry.MatchesRelatedToken(token, boundary, boundary == 0 && entry.Prefix.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (!matched) return false;
            }
            return true;
        }
        return localizedName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               englishName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesShortcutFilters(string path, long size, long utcTicks, SearchQueryOptions options)
    {
        if (!SearchMetadataFilter.MatchesSize(options.SizeFilter, false, size)) return false;
        long minTicks = SearchMetadataFilter.GetMinUtcTicks(options.TimeFilter);
        if (!SearchMetadataFilter.MatchesTime(utcTicks, minTicks)) return false;
        if (string.IsNullOrWhiteSpace(options.PathFilter)) return true;
        foreach (string token in options.PathFilter.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!path.Contains(token, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static List<SearchRow> SortShortcuts(List<SearchRow> rows, SearchQueryOptions options)
    {
        IOrderedEnumerable<SearchRow> ordered = options.SortField switch
        {
            SearchSortField.Size => options.SortDescending
                ? rows.OrderByDescending(r => r.Size)
                : rows.OrderBy(r => r.Size),
            SearchSortField.ModifiedTime => options.SortDescending
                ? rows.OrderByDescending(r => r.LastWriteUtcTicks)
                : rows.OrderBy(r => r.LastWriteUtcTicks),
            _ => options.SortDescending
                ? rows.OrderByDescending(r => r.Path, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
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
