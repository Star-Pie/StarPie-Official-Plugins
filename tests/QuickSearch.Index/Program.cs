using System.IO;
using System.Diagnostics;
using StarPie.Plugin.QuickSearch;

if (args is ["--scale-create", var scaleParent])
{
    ScaleFixture.Create(scaleParent);
    return;
}
if (args is ["--scale", var scaleDirectory])
{
    await ScaleFixture.Measure(scaleDirectory);
    return;
}

if (args is ["--prime-windows-cache", var windowsFixture])
{
    string selected = Path.Combine(windowsFixture, "selected");
    using var writer = new FolderSearchIndex(Path.Combine(windowsFixture, "cache"));
    await writer.SetRootsAsync([selected, Path.Combine(selected, "external-junction", "private")], CancellationToken.None);
    await Eventually(() => Task.FromResult(!writer.Status.IsRefreshing), "Seed cache complete");
    Check((await Query(writer, "secret-entry")).Items.Count == 1 &&
        (await Query(writer, "denied-entry")).Items.Count == 1, "Offline directory and ACL fixture starts readable");
    return;
}

// The caller supplies an isolated, writable fixture parent. No user locations are discovered.
if (args.Length is < 1 or > 2 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Supply an absolute test fixture parent.");
string parent = Path.GetFullPath(args[0]);
string fixture = Path.Combine(parent, "quick-search-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    CompactQueryChecks.Run(fixture);
    await Run(fixture);
    if (args.Length == 2) await RunWindowsFixture(args[1]);
}
finally
{
    if (!string.Equals(Path.GetDirectoryName(fixture), parent, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Fixture escaped its dedicated parent.");
    Directory.Delete(fixture, true);
}

static async Task Run(string fixture)
{
    string root = Directory.CreateDirectory(Path.Combine(fixture, "selected")).FullName;
    string cache = Path.Combine(root, "index-cache");
    string other = Directory.CreateDirectory(Path.Combine(fixture, "selected-sibling")).FullName;
    string outside = Path.Combine(other, "outside-only.sldprt");
    File.WriteAllText(outside, "");
    string deep = root;
    for (int i = 0; i < 10; i++) deep = Path.Combine(deep, $"deep-{i}");
    Directory.CreateDirectory(deep);
    const int folders = 260;
    const int perFolder = 12;
    var expectedCad = new List<string>();
    for (int i = 0; i < folders; i++)
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, $"batch-{i:D3}")).FullName;
        for (int j = 0; j < perFolder; j++)
        {
            string file = Path.Combine(directory, $"corpus-{j:D2}" + (j % 3 == 0 ? ".SLDPRT" : ".txt"));
            File.WriteAllText(file, "");
            if (j % 3 == 0) expectedCad.Add(file);
        }
    }
    string target = Path.Combine(deep, "最终件.STEP");
    File.WriteAllText(target, "");
    double cold;
    var token = CancellationToken.None;
    using (var index = new FolderSearchIndex(cache, TimeSpan.FromMilliseconds(150)))
    {
        Check((await Query(index, "outside-only")).Items.Count == 0, "Unconfigured index is empty");
        var timer = Stopwatch.StartNew();
        await index.SetRootsAsync([root, deep, root], token);
        await Eventually(() => Task.FromResult(!index.Status.IsRefreshing), "Cold index complete");
        cold = timer.Elapsed.TotalMilliseconds;
        Check(index.Status is { IsRefreshing: false, SelectedRootCount: 2, UnavailableRootCount: 0, SkippedDirectoryCount: 0 }, "Cold coverage");
        long revision = index.Status.Revision;
        Check((await Query(index, "最终件", "CAD")).Items.Single().FullPath == target, "Deep CAD");
        Check((await Query(index, "deep-9" + Path.DirectorySeparatorChar, "cad")).Items.Single().FullPath == target, "Parent path search");
        Check((await Query(index, "batch-259", "Folders")).Items.Single().IsDirectory, "Wide directory coverage");
        Check((await Query(index, "outside-only")).Items.Count == 0, "Sibling outside scope");
        Check((await Query(index, "folder-index-")).Items.Count == 0, "Cache exclusion");
        Check((await Query(index, "corpus-", "System")).Items.Count == 0, "Ordinary documents stay out of tools");
        string tool = Path.Combine(root, "indexed-tool.msc");
        File.WriteAllText(tool, "");
        await Eventually(async () => (await Query(index, "", "System")).Items.SingleOrDefault()?.FullPath == tool,
            "Blank system category lists only actual indexed tools");
        revision = index.Status.Revision;
        var all = new List<string>();
        bool more;
        do
        {
            var page = await index.SearchAsync("CORPUS-", "CAD", all.Count, 37, token);
            all.AddRange(page.Items.Select(item => item.FullPath));
            more = page.HasMore;
        } while (more);
        Check(all.SequenceEqual(expectedCad.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase), "Complete stable filtered pagination");
        Check(!(await index.SearchAsync("corpus-", "cad", expectedCad.Count - 1, 1, token)).HasMore, "Exact page boundary");
        Check((await index.SearchAsync("corpus-", "cad", int.MaxValue, 10, token)).Items.Count == 0, "Offset beyond end");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await index.SearchAsync("corpus-", "all", 0, 50, cancelled.Token); throw new Exception("Cancelled query ran"); }
            catch (OperationCanceledException) { }
        }
        // Time the same negative query against the warm snapshot and a complete on-demand directory walk.
        var times = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            timer.Restart();
            Check((await Query(index, "synthetic-miss-940137")).Items.Count == 0, "Negative query");
            times.Add(timer.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var positive = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            string term = (i % 3) switch { 0 => "corpus-", 1 => "batch-25", _ => "最终件" };
            timer.Restart();
            var page = await index.SearchAsync(term, "cad", 0, 40, token);
            positive.Add(timer.Elapsed.TotalMilliseconds);
            Check(page.Items.Count > 0, "Positive CAD first-page query");
        }
        positive.Sort();
        var diskTimes = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            timer.Restart();
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            Check(!Directory.EnumerateFileSystemEntries(root, "*", options)
                .Any(path => path.Contains("synthetic-miss-940137", StringComparison.OrdinalIgnoreCase)), "Walk comparison");
            diskTimes.Add(timer.Elapsed.TotalMilliseconds);
        }
        diskTimes.Sort();
        Console.WriteLine($"Synthetic corpus {folders * perFolder + 1} files, {folders} batch directories, depth 10. Cold scan+cache {cold:F1} ms; " +
            $"40 negative warm queries median {times[20]:F2} ms / p95 {times[37]:F2} ms; " +
            $"40 positive CAD first pages (broad, fanout, deepest) median {positive[20]:F2} ms / p95 {positive[37]:F2} ms; " +
            $"8 direct walks median {diskTimes[4]:F2} ms.");
        await index.SetRootsAsync([deep, root], token);
        await Task.Delay(400);
        Check(index.Status.Revision == revision, "Polling an unchanged scope leaves its revision stable");
        var drift = Directory.CreateDirectory(Path.Combine(root, "incoming")).FullName;
        string watchFile = Path.Combine(drift, "watch-entry.sldasm");
        File.WriteAllBytes(watchFile, new byte[4096]);
        await Eventually(async () =>
            (await index.SearchAsync("watch-entry", "all", 0, 10, new SearchQueryOptions(SortField: SearchSortField.Size, SortDescending: true), token))
            .Items.FirstOrDefault() is { Size: 4096, LastWriteUtcTicks: > 0 } &&
            index.Status.Revision > revision, "create advances visible revision and captures metadata");
        var moved = Path.Combine(root, "renamed-folder");
        Directory.Move(drift, moved);
        await Eventually(async () => (await Query(index, "watch-entry")).Items.SingleOrDefault()?.FullPath ==
            Path.Combine(moved, "watch-entry.sldasm"), "directory rename and descendant path");
        File.Delete(Path.Combine(moved, "watch-entry.sldasm"));
        await Eventually(async () => (await Query(index, "watch-entry")).Items.Count == 0, "delete");
        Check(File.Exists(Path.Combine(cache, "folder-index-v1.bin")), "Persistent index written");
    }
    File.Delete(target);
    string newTarget = Path.Combine(deep, "changed-while-closed.dwg");
    File.WriteAllBytes(newTarget, new byte[150_000]);
    using (var reopened = new FolderSearchIndex(cache, TimeSpan.FromMilliseconds(150)))
    {
        var timer = Stopwatch.StartNew();
        await reopened.SetRootsAsync([root, deep], token);
        Console.WriteLine($"Restart ready with persisted index: {timer.Elapsed.TotalMilliseconds:F1} ms.");
        await Eventually(async () =>
            (await reopened.SearchAsync("changed-while-closed", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Small), token))
            .Items.SingleOrDefault()?.Size == 150_000 &&
            (await Query(reopened, "最终件")).Items.Count == 0, "offline reconciliation updates size and filters");
        await reopened.SetRootsAsync([other], token);
        Check((await Query(reopened, "outside-only")).Items.Single().FullPath == outside, "New scope");
        Check((await Query(reopened, "corpus-")).Items.Count == 0, "Old scope removed");
        var detached = Path.Combine(fixture, "temporarily-offline");
        Directory.Move(other, detached);
        await Eventually(async () => (await Query(reopened, "outside-only")).Items.Count == 0 &&
            reopened.Status.UnavailableRootCount == 1, "detached root disappears and reports unavailable");
        Directory.Move(detached, other);
        await Eventually(async () => (await Query(reopened, "outside-only")).Items.Count == 1 &&
            reopened.Status.UnavailableRootCount == 0, "reattached root returns");
        await reopened.SetRootsAsync([], token);
        Check(!File.Exists(Path.Combine(cache, "folder-index-v1.bin")), "Clearing the scope clears stored paths");
    }
    File.WriteAllBytes(Path.Combine(cache, "folder-index-v1.bin"), [1, 0, 0, 0, 3]);
    using (var corrupt = new FolderSearchIndex(cache))
    {
        await corrupt.SetRootsAsync([other], token);
        Check((await Query(corrupt, "outside-only")).Items.Count == 1, "Corrupt cache recovery");
    }
    // A read-only/unavailable cache must not disable in-memory search.
    using (var unavailableCache = new FolderSearchIndex(Path.Combine(outside, "not-a-directory")))
    {
        await unavailableCache.SetRootsAsync([other], token);
        Check((await Query(unavailableCache, "outside-only")).Items.Count == 1, "Unavailable cache");
    }
    using (var switched = new FolderSearchIndex(cache))
    {
        var replaced = switched.SetRootsAsync([root], token);
        var current = switched.SetRootsAsync([other], token);
        try { await replaced; } catch (OperationCanceledException) { }
        await current;
        Check((await Query(switched, "corpus-")).Items.Count == 0 &&
            (await Query(switched, "outside-only")).Items.Count == 1, "Latest selected roots win while scans overlap");
    }
    var clearing = new FolderSearchIndex(cache);
    string catalogue = Path.Combine(cache, "folder-index-v1.bin");
    using (File.Open(catalogue, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { await clearing.SetRootsAsync([], token); throw new Exception("Locked catalogue deletion reported success"); }
        catch (IOException) { }
    }
    var cleared = clearing.SetRootsAsync([], token);
    clearing.Dispose();
    await cleared;
    Check(!File.Exists(Path.Combine(cache, "folder-index-v1.bin")), "Immediate close preserves last-root removal");
    Console.WriteLine("Filesystem, cache, cancellation, scope and pagination checks passed.");
}

static async Task RunWindowsFixture(string fixture)
{
    string root = Path.Combine(fixture, "selected");
    string junction = Path.Combine(root, "external-junction");
    Check((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0, "External junction fixture required");
    Check((File.GetAttributes(Path.Combine(root, "cycle")) & FileAttributes.ReparsePoint) != 0, "Cycle fixture required");
    string[] roots = [root, Path.Combine(junction, "private")];
    var restored = FolderIndexCache.Read(Path.Combine(fixture, "cache"), roots, new HashSet<string>([root], StringComparer.OrdinalIgnoreCase),
        CancellationToken.None) ?? throw new Exception("Expected the prior readable-scope index.");
    Check(restored.Contains(FolderEntry.Split(Path.Combine(root, "public-entry.txt"), false)) &&
        !restored.Any(entry => entry.Name.Contains("secret-entry") || entry.Name.Contains("denied-entry")), "Warm rows exclude newly denied or redirected subtrees before reconciliation");
    using var index = new FolderSearchIndex(Path.Combine(fixture, "cache"), TimeSpan.FromMilliseconds(150));
    await index.SetRootsAsync(roots, CancellationToken.None);
    Check((await Query(index, "public-entry")).Items.Count == 1, "Cycle does not duplicate ordinary files");
    Check((await Query(index, "secret-entry")).Items.Count == 0, "External junction and root under junction are excluded");
    Check((await Query(index, "denied-entry")).Items.Count == 0, "Denied directory excluded");
    await Eventually(() => Task.FromResult(index.Status is { IsRefreshing: false, UnavailableRootCount: 1, SkippedDirectoryCount: 1 }),
        "Denied subtree and root coverage counters");
    Console.WriteLine("NTFS junction escape, link cycle, selected root below junction and ACL-denied subtree checks passed.");
}

static Task<SearchPage> Query(FolderSearchIndex index, string text, string category = SearchCategories.All) =>
    index.SearchAsync(text, category, 0, 100, CancellationToken.None);

static async Task Eventually(Func<Task<bool>> predicate, string label)
{
    var deadline = Stopwatch.StartNew();
    while (!await predicate())
    {
        if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Timed out: " + label);
        await Task.Delay(50);
    }
}

static void Check(bool ok, string label)
{
    if (!ok) throw new Exception(label);
}
