using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Security;
using System.Threading.Channels;

namespace StarPie.Plugin.QuickSearch;

internal sealed record FolderIndexView(ImmutableSortedSet<FolderEntry> Entries, bool Provisional, FolderSearchRows? Rows = null);

internal sealed class FolderIndexSession
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly ImmutableSortedSet<FolderEntry> Empty = ImmutableSortedSet.Create<FolderEntry>(FolderPathComparer.Instance);
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false
    };
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stopGate = new();
    // Serializes atomic replacements and scope clears, entirely on background workers.
    private static readonly object CacheCommitGate = new();
    private static readonly ConcurrentDictionary<string, FolderIndexSession> CacheOwners = new(PathComparer);
    private readonly TimeSpan _pollInterval;
    private readonly string _cacheDirectory;
    private readonly Channel<string> _changes = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(PathComparer);
    private HashSet<string> _online = new(PathComparer);
    private readonly HashSet<string> _skippedDirectories = new(PathComparer);
    // Watch notifications also share their existing directory prefix. A removed
    // directory retires its prefixes; file creation cannot grow this table.
    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);
    private FolderIndexView _snapshot = new(Empty, false);
    private FolderIndexStatus _status;
    private int _needsRebuild;
    private long _revision;
    private bool _stopped;

    public FolderIndexSession(string[] roots, string cacheDirectory, TimeSpan pollInterval)
    {
        Roots = roots;
        _cacheDirectory = cacheDirectory;
        _pollInterval = pollInterval;
        Token = _lifetime.Token;
        _status = new FolderIndexStatus(roots.Length > 0, roots.Length, 0, 0, 0, 0, 0);
    }

    public string[] Roots { get; }
    public Task Ready => _ready.Task;
    public FolderIndexView Snapshot => Volatile.Read(ref _snapshot);
    public CancellationToken Token { get; }
    public FolderIndexStatus Status => Volatile.Read(ref _status);
    public void Start()
    {
        CacheOwners[_cacheDirectory] = this;
        _ = Task.Run(RunAsync);
    }

    private bool OwnsCache => CacheOwners.TryGetValue(_cacheDirectory, out var current) && current == this;

    public void Stop()
    {
        lock (_stopGate)
        {
            if (_stopped) return;
            _stopped = true;
            _lifetime.Cancel();
        }
    }

    private async Task RunAsync()
    {
        var token = _lifetime.Token;
        using var timer = new Timer(_ => Signal(string.Empty), null, _pollInterval, _pollInterval);
        try
        {
            if (Roots.Length == 0)
            {
                lock (CacheCommitGate)
                {
                    // An explicitly cleared scope must survive immediate window close.
                    // A newer scope at the same location takes ownership before scheduling its worker.
                    if (OwnsCache) FolderIndexCache.Remove(_cacheDirectory);
                }
                _ready.TrySetResult();
                return;
            }
            token.ThrowIfCancellationRequested();
            UpdateWatchers(false, token);
            PublishStatus(true);
            var restored = FolderIndexCache.Read(_cacheDirectory, Roots, _online, token, OfferRestored);
            if (restored != null) OfferRestored(restored);
            else if (Snapshot.Provisional) SetSnapshot(Empty, token);
            var index = Snapshot.Entries.ToBuilder();
            Reconcile(index, Roots, restored == null, token);
            SetSnapshot(index.ToImmutable(), token, optimize: true);
            Save(index, token);
            PublishStatus(false);
            _ready.TrySetResult();
            long lastFallback = Stopwatch.GetTimestamp(), lastSave = lastFallback, lastChange = lastFallback;
            bool dirty = false;
            while (await _changes.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                // Batch bursts from directory moves and extraction without delaying reads of the current snapshot.
                await Task.Delay(100, token).ConfigureAwait(false);
                var paths = new HashSet<string>(StringComparer.Ordinal);
                bool timerFired = false;
                // A continuously busy producer must not starve publication or cancellation.
                for (int i = 0; i < 4096 && _changes.Reader.TryRead(out var path); i++)
                {
                    if (path.Length == 0) timerFired = true;
                    else paths.Add(path);
                }
                bool overflow = Interlocked.Exchange(ref _needsRebuild, 0) != 0;
                var reconcile = timerFired || overflow ? UpdateWatchers(overflow, token) : new HashSet<string>(PathComparer);
                if (overflow) reconcile.UnionWith(Roots);
                if (_watchers.Count < _online.Count && Stopwatch.GetElapsedTime(lastFallback) >= TimeSpan.FromSeconds(30))
                {
                    reconcile.UnionWith(_online.Where(root => !_watchers.ContainsKey(root)));
                    lastFallback = Stopwatch.GetTimestamp();
                }
                long previousRevision = _revision;
                if (reconcile.Count != 0)
                {
                    PublishStatus(true);
                    Reconcile(index, reconcile, false, token);
                }
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    if (!reconcile.Any(root => AtOrBelow(path, root))) Refresh(index, path, token);
                }
                SetSnapshot(index.ToImmutable(), token);
                if (_revision != previousRevision)
                {
                    dirty = true;
                    lastChange = Stopwatch.GetTimestamp();
                }
                else if (timerFired && Stopwatch.GetElapsedTime(lastChange) >= TimeSpan.FromSeconds(2))
                    SetSnapshot(index.ToImmutable(), token, optimize: true);
                PublishStatus(false);
                // Larger catalogues take longer to serialize. A busy drive gets one
                // coalesced checkpoint, with a bounded delay even if it never becomes idle.
                var checkpoint = TimeSpan.FromSeconds(Math.Clamp(index.Count / 1000, 15, 900));
                var sinceSave = Stopwatch.GetElapsedTime(lastSave);
                if (dirty && sinceSave >= checkpoint && (Stopwatch.GetElapsedTime(lastChange) >= TimeSpan.FromSeconds(2) || sinceSave >= checkpoint * 2))
                {
                    Save(index, token);
                    lastSave = Stopwatch.GetTimestamp();
                    dirty = false;
                }
            }

            void OfferRestored(ImmutableSortedSet<FolderEntry> entries)
            {
                SetSnapshot(entries, token, provisional: true);
                PublishStatus(true);
                _ready.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _ready.TrySetCanceled(token); }
        catch (Exception ex)
        {
            Volatile.Write(ref _status, Status with { IsRefreshing = false, HasFailed = true });
            _ready.TrySetException(ex);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, FolderIndexSession>>)CacheOwners).Remove(new(_cacheDirectory, this));
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _changes.Writer.TryComplete();
            lock (_stopGate)
            {
                _stopped = true;
                _lifetime.Dispose();
            }
        }
    }

    private void Save(ImmutableSortedSet<FolderEntry>.Builder index, CancellationToken token) =>
        FolderIndexCache.Write(_cacheDirectory, Roots, index, token, (temporary, destination) =>
        {
            lock (CacheCommitGate)
            {
                token.ThrowIfCancellationRequested();
                if (OwnsCache) File.Move(temporary, destination, true);
            }
        });

    private void PublishStatus(bool refreshing)
    {
        int failedRoots = _online.Count(root => _skippedDirectories.Contains(root));
        Volatile.Write(ref _status, new FolderIndexStatus(refreshing, Roots.Length,
            Roots.Length - _online.Count + failedRoots, _skippedDirectories.Count - failedRoots,
            _online.Count - _watchers.Count, _revision, Snapshot.Entries.Count));
    }

    private void SetSnapshot(ImmutableSortedSet<FolderEntry> next, CancellationToken token, bool provisional = false, bool optimize = false)
    {
        token.ThrowIfCancellationRequested();
        var previous = Snapshot;
        bool needsRows = optimize && next.Count >= 512 && previous.Rows == null;
        if (ReferenceEquals(previous.Entries, next) && previous.Provisional == provisional && !needsRows) return;
        bool changed = previous.Entries.Count != next.Count;
        if (!changed && !ReferenceEquals(previous.Entries, next))
        {
            using var items = previous.Entries.GetEnumerator();
            foreach (var entry in next)
            {
                token.ThrowIfCancellationRequested();
                if (!items.MoveNext() || items.Current != entry) { changed = true; break; }
            }
        }
        if (changed) _revision++;
        var rows = changed ? null : previous.Rows;
        if (rows == null && optimize && next.Count >= 512) rows = FolderSearchRows.Build(next, token);
        Volatile.Write(ref _snapshot, new FolderIndexView(next, provisional, rows));
    }

    private HashSet<string> UpdateWatchers(bool reconnect, CancellationToken token)
    {
        var online = new HashSet<string>(PathComparer);
        foreach (var root in Roots)
        {
            token.ThrowIfCancellationRequested();
            if (!Excluded(root) && Directory.Exists(root) && IsReachable(root)) online.Add(root);
        }
        var changed = new HashSet<string>(_online, PathComparer);
        changed.SymmetricExceptWith(online);
        _online = online;
        foreach (var root in _watchers.Keys.Where(root => reconnect || !online.Contains(root)).ToArray())
        {
            _watchers[root].Dispose();
            _watchers.Remove(root);
        }
        foreach (string root in online)
        {
            token.ThrowIfCancellationRequested();
            if (_watchers.ContainsKey(root)) continue;
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Security,
                    InternalBufferSize = 32 * 1024
                };
                watcher.Created += (_, e) => Signal(e.FullPath);
                watcher.Deleted += (_, e) => Signal(e.FullPath);
                watcher.Changed += (_, e) => Signal(e.FullPath);
                watcher.Renamed += (_, e) => { Signal(e.OldFullPath); Signal(e.FullPath); };
                watcher.Error += (_, _) => { Interlocked.Exchange(ref _needsRebuild, 1); Signal(string.Empty); };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(root, watcher);
                changed.Add(root);
            }
            catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException or PlatformNotSupportedException or Win32Exception)
            {
                watcher?.Dispose();
            }
        }
        return changed;
    }

    private void Signal(string path)
    {
        if (path.Length > 0 && Excluded(path)) return;
        if (!_changes.Writer.TryWrite(path)) Interlocked.Exchange(ref _needsRebuild, 1);
    }

    private void Reconcile(ImmutableSortedSet<FolderEntry>.Builder index, IReadOnlyCollection<string> roots,
        bool publishPartial, CancellationToken token)
    {
        bool all = ReferenceEquals(roots, Roots) || Roots.All(roots.Contains);
        if (all) _skippedDirectories.Clear();
        else _skippedDirectories.RemoveWhere(path => roots.Any(root => AtOrBelow(path, root)));
        // Reuse paths and immutable nodes from the preceding snapshot. Only the
        // references needed to detect removals are retained through reconciliation.
        HashSet<FolderEntry>? missing = null;
        if (index.Count != 0)
        {
            missing = new HashSet<FolderEntry>(all ? index.Count : 0, FolderPathComparer.Instance);
            foreach (var entry in index)
            {
                token.ThrowIfCancellationRequested();
                if (all || roots.Any(entry.AtOrBelow)) missing.Add(entry);
            }
        }
        long lastPublish = Stopwatch.GetTimestamp();
        int publishedCount = 0;
        foreach (var root in roots.OrderBy(path => path.Length))
        {
            token.ThrowIfCancellationRequested();
            var rootEntry = FolderEntry.Split(root, true);
            if (_online.Contains(root) && (!index.Contains(rootEntry) || missing?.Contains(rootEntry) == true))
                Scan(index, root, token, missing, publishPartial ? Progress : null);
        }
        if (missing != null)
        {
            foreach (var entry in missing) { token.ThrowIfCancellationRequested(); index.Remove(entry); }
            PruneRemovedPrefixes(index, token);
        }

        void Progress()
        {
            int added = index.Count - publishedCount;
            if (added <= 0) return;
            var elapsed = Stopwatch.GetElapsedTime(lastPublish);
            if (publishedCount == 0 ? added < 128 && elapsed.TotalMilliseconds < 35 : elapsed.TotalMilliseconds < 120) return;
            SetSnapshot(index.ToImmutable(), token);
            PublishStatus(true);
            _ready.TrySetResult();
            publishedCount = index.Count;
            lastPublish = Stopwatch.GetTimestamp();
        }
    }

    private void Refresh(ImmutableSortedSet<FolderEntry>.Builder index, string path, CancellationToken token)
    {
        if (Excluded(path) || !_online.Any(root => AtOrBelow(path, root))) return;
        _skippedDirectories.RemoveWhere(key => AtOrBelow(key, path));
        var target = FolderEntry.Split(path, false);
        if (index.TryGetValue(target, out var previous) && previous.IsDirectory)
        {
            var removed = new List<FolderEntry>();
            foreach (var entry in index)
            {
                token.ThrowIfCancellationRequested();
                if (entry.AtOrBelow(path)) removed.Add(entry);
            }
            foreach (var entry in removed) index.Remove(entry);
            foreach (var key in _prefixes.Keys.Where(prefix => AtOrBelow(prefix, path)).ToArray()) _prefixes.Remove(key);
        }
        else index.Remove(target);
        if (IsReachable(path)) Scan(index, path, token);
    }

    private void Scan(ImmutableSortedSet<FolderEntry>.Builder index, string path, CancellationToken token,
        HashSet<FolderEntry>? missing = null, Action? progress = null)
    {
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || Excluded(current)) continue;
                bool directory = (attributes & FileAttributes.Directory) != 0;
                if (!directory)
                {
                    var info = new FileInfo(current);
                    var file = FolderEntry.Split(current, false, info.Length, info.LastWriteTimeUtc.Ticks);
                    Record(file with { Prefix = SharePrefix(file.Prefix) });
                    continue;
                }
                if (!IsReachable(current))
                {
                    _skippedDirectories.Add(current);
                    continue;
                }
                var folder = FolderEntry.Split(current, true, 0L, Directory.GetLastWriteTimeUtc(current).Ticks);
                Record(folder with { Prefix = SharePrefix(folder.Prefix) });
                string prefix = SharePrefix(Path.EndsInDirectorySeparator(current) ? current : current + Path.DirectorySeparatorChar);
                var children = new FileSystemEnumerable<FolderEntry>(current,
                    (ref FileSystemEntry entry) => new FolderEntry(prefix, entry.FileName.ToString(), entry.IsDirectory,
                        entry.IsDirectory ? 0L : entry.Length, entry.LastWriteTimeUtc.UtcTicks), Options);
                int visited = 0;
                foreach (var entry in children)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.AtOrBelow(_cacheDirectory)) continue;
                    Record(entry);
                    if (entry.IsDirectory) stack.Push(entry.FullPath);
                    if ((++visited & 63) == 0) progress?.Invoke();
                }
                progress?.Invoke();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (IsFileError(ex)) { _skippedDirectories.Add(current); }
        }

        void Record(FolderEntry hit)
        {
            missing?.Remove(hit);
            if (index.Add(hit)) return;
            if (index.TryGetValue(hit, out var known) && known != hit)
            {
                index.Remove(known);
                index.Add(hit);
            }
        }
    }

    private string SharePrefix(string prefix)
    {
        if (_prefixes.TryGetValue(prefix, out var shared)) return shared;
        _prefixes.Add(prefix, prefix);
        return prefix;
    }

    private void PruneRemovedPrefixes(ImmutableSortedSet<FolderEntry>.Builder index, CancellationToken token)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in index)
        {
            token.ThrowIfCancellationRequested();
            used.Add(entry.Prefix);
        }
        foreach (string prefix in _prefixes.Keys.Where(key => !used.Contains(key)).ToArray()) _prefixes.Remove(prefix);
    }

    private bool Excluded(string path) => AtOrBelow(path, _cacheDirectory);

    // Check ancestors too: a queued event or a configured root may sit below a junction.
    internal static bool IsReachable(string path)
    {
        try
        {
            for (string? item = path; item != null; item = Path.GetDirectoryName(item))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception ex) when (IsFileError(ex)) { return false; }
    }

    internal static bool AtOrBelow(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
        (path.Length == root.Length || Path.EndsInDirectorySeparator(root) || path[root.Length] == Path.DirectorySeparatorChar);

    internal static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;
}
