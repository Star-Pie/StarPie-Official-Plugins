using System.IO;

namespace StarPie.Plugin.QuickSearch;

internal sealed record FolderIndexStatus(bool IsRefreshing, int SelectedRootCount, int UnavailableRootCount,
    int SkippedDirectoryCount, int UnwatchedRootCount, long Revision, int IndexedItemCount, bool HasFailed = false);

internal sealed class FolderSearchIndex : IDisposable
{
    private readonly object _gate = new();
    private readonly string _cacheDirectory;
    private readonly TimeSpan _pollInterval;
    private FolderIndexSession? _session;
    private bool _disposed;

    public FolderSearchIndex(string cacheDirectory) : this(cacheDirectory, TimeSpan.FromSeconds(3)) { }

    internal FolderSearchIndex(string cacheDirectory, TimeSpan pollInterval)
    {
        _cacheDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheDirectory));
        _pollInterval = pollInterval;
    }

    public FolderIndexStatus Status
    {
        get
        {
            lock (_gate) return _session?.Status ?? new FolderIndexStatus(false, 0, 0, 0, 0, 0, 0);
        }
    }

    public Task SetRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = new List<string>();
        foreach (var value in roots)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) continue;
            try { paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
        }

        var normalized = paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session == null || _session.Ready.IsFaulted || _session.Status.HasFailed ||
                !_session.Roots.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase))
            {
                _session?.Stop();
                _session = new FolderIndexSession(normalized, _cacheDirectory, _pollInterval);
                _session.Start();
            }
            // Callers cancel their wait. Reconfiguring the roots or unloading stops the shared worker.
            return _session.Ready.WaitAsync(cancellationToken);
        }
    }

    public Task<SearchPage> SearchAsync(string query, string category, int offset, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        cancellationToken.ThrowIfCancellationRequested();
        FolderIndexSession? session;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            session = _session;
        }
        if (session == null) return Task.FromResult(SearchPage.Empty);
        var snapshot = session.Snapshot;
        return Task.Run(() =>
        {
            var term = (query ?? "").Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            var page = Find(snapshot, term, category, offset, pageSize, cancellationToken, session.Token);
            lock (_gate)
                return !_disposed && _session == session ? page : SearchPage.Empty;
        }, cancellationToken);
    }

    private static SearchPage Find(FolderIndexView snapshot, string query,
        string category, int offset, int pageSize, CancellationToken token, CancellationToken sessionToken)
    {
        var items = new List<SearchHit>(Math.Min(pageSize, 100));
        char[]? validationBuffer = null;
        int boundary = query.LastIndexOf(Path.DirectorySeparatorChar) + 1;
        if (snapshot.Rows is { } rows)
        {
            foreach (var group in rows.Groups)
            {
                bool parentMatches = rows.Hits[group.Start].Prefix.Contains(query, StringComparison.OrdinalIgnoreCase);
                for (int i = group.Start; i < group.End; i++)
                {
                    if ((i & 127) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                    }
                    var hit = rows.Hits[i];
                    if (!hit.Matches(query, boundary, parentMatches)) continue;
                    if (Collect(hit)) return new SearchPage(items, true);
                }
            }
        }
        else
        {
            foreach (var hit in snapshot.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                if (hit.Matches(query, boundary, hit.Prefix.Contains(query, StringComparison.OrdinalIgnoreCase)) && Collect(hit))
                    return new SearchPage(items, true);
            }
        }
        return new SearchPage(items, false);

        bool Collect(FolderEntry hit)
        {
            if (!SearchCategories.Matches(category, hit) ||
                (snapshot.Provisional && !FolderIndexCache.StillPresent(hit, ref validationBuffer))) return false;
            if (offset > 0) { offset--; return false; }
            if (items.Count == pageSize) return true;
            items.Add(new SearchHit(hit.FullPath, hit.IsDirectory));
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _session?.Stop();
            _session = null;
        }
    }
}
