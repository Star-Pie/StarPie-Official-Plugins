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

    public Task<SearchPage> SearchAsync(string query, string category, int offset, int pageSize, CancellationToken cancellationToken) =>
        SearchAsync(query, category, offset, pageSize, SearchQueryOptions.LegacyDefault, cancellationToken);

    public Task<SearchPage> SearchAsync(string query, string category, int offset, int pageSize,
        SearchQueryOptions options, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        ArgumentNullException.ThrowIfNull(options);
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
            var page = Find(snapshot, term, category, offset, pageSize, options, cancellationToken, session.Token);
            lock (_gate)
                return !_disposed && _session == session ? page : SearchPage.Empty;
        }, cancellationToken);
    }

    private static SearchPage Find(FolderIndexView snapshot, string query,
        string category, int offset, int pageSize, CancellationToken token, CancellationToken sessionToken) =>
        Find(snapshot, query, category, offset, pageSize, SearchQueryOptions.LegacyDefault, token, sessionToken);

    private static SearchPage Find(FolderIndexView snapshot, string query,
        string category, int offset, int pageSize, SearchQueryOptions options,
        CancellationToken token, CancellationToken sessionToken)
    {
        int boundary = query.LastIndexOf(Path.DirectorySeparatorChar) + 1;
        string[] pathTokens = SplitFilterTokens(options.PathFilter);
        long minUtcTicks = SearchMetadataFilter.GetMinUtcTicks(options.TimeFilter);
        SearchSizeFilter sizeFilter = options.SizeFilter;
        bool hasMetadataFilter = sizeFilter != SearchSizeFilter.Any || minUtcTicks > 0L || pathTokens.Length > 0;
        SearchMatchMode mode = options.MatchMode;
        (string Term, int Boundary)[] queryTokens = mode == SearchMatchMode.Related && query.Length > 0
            ? SplitRelatedTerms(query)
            : [];
        bool[]? groupTokenPrefixHits = queryTokens.Length > 0 ? new bool[queryTokens.Length] : null;
        bool[]? pathGroupHits = pathTokens.Length > 0 ? new bool[pathTokens.Length] : null;
        char[]? validationBuffer = null;

        if (options.SortField == SearchSortField.Path)
        {
            var items = new List<SearchHit>(Math.Min(pageSize, 100));
            int remainingOffset = offset;

            bool CollectStreaming(FolderEntry hit, bool[]? activePathHits)
            {
                if (!MatchesExtraAndCategory(hit, category, pathTokens, activePathHits, sizeFilter, minUtcTicks) ||
                    (snapshot.Provisional && !FolderIndexCache.StillPresent(hit, ref validationBuffer)))
                    return false;
                if (remainingOffset > 0) { remainingOffset--; return false; }
                if (items.Count == pageSize) return true;
                items.Add(ToHit(hit));
                return false;
            }

            if (snapshot.Rows is { } rows)
            {
                var groups = rows.Groups;
                if (!options.SortDescending)
                {
                    foreach (var group in groups)
                    {
                        var prefix = rows.Hits[group.Start].Prefix;
                        PreparePathGroupHits(prefix, pathTokens, pathGroupHits);
                        bool parentMatches = mode == SearchMatchMode.Substring && prefix.Contains(query, StringComparison.OrdinalIgnoreCase);
                        PrepareTokenGroupHits(prefix, queryTokens, groupTokenPrefixHits);
                        for (int i = group.Start; i < group.End; i++)
                        {
                            if ((i & 127) == 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                            }
                            var hit = rows.Hits[i];
                            if (!MatchesTerm(hit, query, boundary, parentMatches, mode, queryTokens, groupTokenPrefixHits)) continue;
                            if (CollectStreaming(hit, pathGroupHits)) return new SearchPage(items, true);
                        }
                    }
                }
                else
                {
                    for (int g = groups.Length - 1; g >= 0; g--)
                    {
                        var group = groups[g];
                        var prefix = rows.Hits[group.Start].Prefix;
                        PreparePathGroupHits(prefix, pathTokens, pathGroupHits);
                        bool parentMatches = mode == SearchMatchMode.Substring && prefix.Contains(query, StringComparison.OrdinalIgnoreCase);
                        PrepareTokenGroupHits(prefix, queryTokens, groupTokenPrefixHits);
                        for (int i = group.End - 1; i >= group.Start; i--)
                        {
                            if ((i & 127) == 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                            }
                            var hit = rows.Hits[i];
                            if (!MatchesTerm(hit, query, boundary, parentMatches, mode, queryTokens, groupTokenPrefixHits)) continue;
                            if (CollectStreaming(hit, pathGroupHits)) return new SearchPage(items, true);
                        }
                    }
                }
            }
            else
            {
                IEnumerable<FolderEntry> source = options.SortDescending ? snapshot.Entries.Reverse() : snapshot.Entries;
                int index = 0;
                foreach (var hit in source)
                {
                    if ((index++ & 127) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                    }
                    if (!MatchesTermDirect(hit, query, boundary, mode, queryTokens)) continue;
                    if (CollectStreaming(hit, null)) return new SearchPage(items, true);
                }
            }
            return new SearchPage(items, false);
        }

        // Size / ModifiedTime sorting: use a bounded max-heap for typical pages (offset + pageSize + 1 <= 2048).
        var comparer = new FolderMetadataComparer(options.SortField, options.SortDescending);
        long needLong = (long)offset + pageSize + 1;
        PriorityQueue<FolderEntry, FolderEntry>? topHeap = needLong <= 2048
            ? new PriorityQueue<FolderEntry, FolderEntry>((int)needLong, new ReverseEntryComparer(comparer))
            : null;
        List<FolderEntry>? allMatches = topHeap == null ? new List<FolderEntry>() : null;
        int topCap = (int)Math.Min(needLong, 2048);

        void AcceptSortedMatch(FolderEntry hit, bool[]? activePathHits)
        {
            if (!MatchesExtraAndCategory(hit, category, pathTokens, activePathHits, sizeFilter, minUtcTicks) ||
                (snapshot.Provisional && !FolderIndexCache.StillPresent(hit, ref validationBuffer)))
                return;
            if (topHeap != null)
            {
                if (topHeap.Count < topCap) topHeap.Enqueue(hit, hit);
                else if (comparer.Compare(hit, topHeap.Peek()) < 0) topHeap.DequeueEnqueue(hit, hit);
            }
            else allMatches!.Add(hit);
        }

        if (snapshot.Rows is { } sortedRows)
        {
            foreach (var group in sortedRows.Groups)
            {
                var prefix = sortedRows.Hits[group.Start].Prefix;
                PreparePathGroupHits(prefix, pathTokens, pathGroupHits);
                bool parentMatches = mode == SearchMatchMode.Substring && prefix.Contains(query, StringComparison.OrdinalIgnoreCase);
                PrepareTokenGroupHits(prefix, queryTokens, groupTokenPrefixHits);
                for (int i = group.Start; i < group.End; i++)
                {
                    if ((i & 127) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                    }
                    var hit = sortedRows.Hits[i];
                    if (!MatchesTerm(hit, query, boundary, parentMatches, mode, queryTokens, groupTokenPrefixHits)) continue;
                    AcceptSortedMatch(hit, pathGroupHits);
                }
            }
        }
        else
        {
            int index = 0;
            foreach (var hit in snapshot.Entries)
            {
                if ((index++ & 127) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (sessionToken.IsCancellationRequested) return SearchPage.Empty;
                }
                if (!MatchesTermDirect(hit, query, boundary, mode, queryTokens)) continue;
                AcceptSortedMatch(hit, null);
            }
        }

        if (topHeap != null)
        {
            int count = topHeap.Count;
            if (count <= offset) return SearchPage.Empty;
            var sorted = new List<FolderEntry>(count);
            while (topHeap.Count > 0) sorted.Add(topHeap.Dequeue());
            sorted.Sort(comparer);
            int take = Math.Min(pageSize, sorted.Count - offset);
            var pageItems = new List<SearchHit>(take);
            for (int i = 0; i < take; i++) pageItems.Add(ToHit(sorted[offset + i]));
            return new SearchPage(pageItems, sorted.Count > offset + pageSize);
        }
        else
        {
            if (allMatches!.Count <= offset) return SearchPage.Empty;
            allMatches.Sort(comparer);
            int take = Math.Min(pageSize, allMatches.Count - offset);
            var pageItems = new List<SearchHit>(take);
            for (int i = 0; i < take; i++) pageItems.Add(ToHit(allMatches[offset + i]));
            return new SearchPage(pageItems, allMatches.Count > offset + pageSize);
        }
    }

    private static SearchHit ToHit(FolderEntry hit) =>
        new(hit.FullPath, hit.IsDirectory, hit.Size, hit.LastWriteUtcTicks);

    private static string[] SplitFilterTokens(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? []
            : filter.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static (string Term, int Boundary)[] SplitRelatedTerms(string query)
    {
        var raw = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new (string Term, int Boundary)[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            result[i] = (raw[i], raw[i].LastIndexOf(Path.DirectorySeparatorChar) + 1);
        return result;
    }

    private static bool PreparePathGroupHits(string prefix, string[] pathTokens, bool[]? pathGroupHits)
    {
        if (pathGroupHits == null) return true;
        bool all = true;
        for (int i = 0; i < pathTokens.Length; i++)
        {
            bool hit = prefix.Contains(pathTokens[i], StringComparison.OrdinalIgnoreCase);
            pathGroupHits[i] = hit;
            if (!hit) all = false;
        }
        return all;
    }

    private static void PrepareTokenGroupHits(string prefix, (string Term, int Boundary)[] queryTokens, bool[]? groupTokenPrefixHits)
    {
        if (groupTokenPrefixHits == null) return;
        for (int i = 0; i < queryTokens.Length; i++)
        {
            groupTokenPrefixHits[i] = queryTokens[i].Boundary == 0 &&
                prefix.Contains(queryTokens[i].Term, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool MatchesTerm(FolderEntry hit, string query, int boundary, bool parentMatches,
        SearchMatchMode mode, (string Term, int Boundary)[] queryTokens, bool[]? groupTokenPrefixHits)
    {
        switch (mode)
        {
            case SearchMatchMode.Exact:
                return hit.MatchesExact(query, boundary);
            case SearchMatchMode.Related:
                if (queryTokens.Length == 0) return true;
                for (int i = 0; i < queryTokens.Length; i++)
                {
                    if (groupTokenPrefixHits![i]) continue;
                    if (!hit.MatchesRelatedToken(queryTokens[i].Term, queryTokens[i].Boundary, false))
                        return false;
                }
                return true;
            default:
                return hit.Matches(query, boundary, parentMatches);
        }
    }

    private static bool MatchesTermDirect(FolderEntry hit, string query, int boundary,
        SearchMatchMode mode, (string Term, int Boundary)[] queryTokens)
    {
        switch (mode)
        {
            case SearchMatchMode.Exact:
                return hit.MatchesExact(query, boundary);
            case SearchMatchMode.Related:
                for (int i = 0; i < queryTokens.Length; i++)
                {
                    var (term, tokBoundary) = queryTokens[i];
                    bool prefixHit = tokBoundary == 0 && hit.Prefix.Contains(term, StringComparison.OrdinalIgnoreCase);
                    if (!hit.MatchesRelatedToken(term, tokBoundary, prefixHit)) return false;
                }
                return true;
            default:
                return hit.Matches(query, boundary, hit.Prefix.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool MatchesExtraAndCategory(FolderEntry hit, string category, string[] pathTokens, bool[]? pathGroupHits,
        SearchSizeFilter sizeFilter, long minUtcTicks)
    {
        if (!SearchCategories.Matches(category, hit)) return false;
        if (!SearchMetadataFilter.MatchesSize(sizeFilter, hit.IsDirectory, hit.Size)) return false;
        if (!SearchMetadataFilter.MatchesTime(hit.LastWriteUtcTicks, minUtcTicks)) return false;
        if (pathTokens.Length == 0) return true;
        for (int i = 0; i < pathTokens.Length; i++)
        {
            if (pathGroupHits != null && pathGroupHits[i]) continue;
            string tok = pathTokens[i];
            if (pathGroupHits == null && hit.Prefix.Contains(tok, StringComparison.OrdinalIgnoreCase)) continue;
            int tokBoundary = tok.LastIndexOf(Path.DirectorySeparatorChar) + 1;
            if (!hit.Matches(tok, tokBoundary, false)) return false;
        }
        return true;
    }

    private sealed class FolderMetadataComparer(SearchSortField field, bool descending) : IComparer<FolderEntry>
    {
        public int Compare(FolderEntry left, FolderEntry right)
        {
            int primary = field switch
            {
                SearchSortField.Size => CompareSize(left, right),
                SearchSortField.ModifiedTime => CompareModified(left, right),
                _ => 0
            };
            if (primary != 0) return primary;
            int tie = FolderPathComparer.Instance.Compare(left, right);
            return descending ? -tie : tie;
        }

        private int CompareSize(FolderEntry left, FolderEntry right)
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? 1 : -1;
            if (left.IsDirectory) return 0;
            return descending ? right.Size.CompareTo(left.Size) : left.Size.CompareTo(right.Size);
        }

        private int CompareModified(FolderEntry left, FolderEntry right)
        {
            bool leftHas = left.LastWriteUtcTicks > 0;
            bool rightHas = right.LastWriteUtcTicks > 0;
            if (leftHas != rightHas) return leftHas ? -1 : 1;
            return descending
                ? right.LastWriteUtcTicks.CompareTo(left.LastWriteUtcTicks)
                : left.LastWriteUtcTicks.CompareTo(right.LastWriteUtcTicks);
        }
    }

    private sealed class ReverseEntryComparer(IComparer<FolderEntry> inner) : IComparer<FolderEntry>
    {
        public int Compare(FolderEntry x, FolderEntry y) => inner.Compare(y, x);
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
