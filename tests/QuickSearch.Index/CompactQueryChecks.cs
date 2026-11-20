using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using StarPie.Plugin.QuickSearch;

internal static class CompactQueryChecks
{
    private delegate SearchPage FindPage(FolderIndexView view, string query, string category,
        int offset, int size, CancellationToken token, CancellationToken sessionToken);

    private static readonly FindPage Find = typeof(FolderSearchIndex).GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<FindPage>();

    internal static void Run(string fixture)
    {
        ComparePathsAndQueries(fixture);
        CheckProvisionalPaging(fixture);
        CheckAllocations(fixture);
    }

    private static void ComparePathsAndQueries(string fixture)
    {
        string[] directories = ["", "A", "a-", "a.txt", "a_", "A/子", "A/子-1", "A/子/二", "图纸", "😀"];
        string[] names = ["x", "轴.step", "A", "a.b", "q_1.txt", "Z9", "😀.SCAD"];
        var all = directories.SelectMany(directory => names.Select(name =>
            Path.GetFullPath(Path.Combine(fixture, directory, name)))).ToList();
        // Pure path values exercise drive and UNC root order without querying either location.
        all.AddRange([@"H:\", @"\\fileserver\share\", @"\\fileserver\share\one", @"\\fileserver\share\one.ext", @"\\fileserver\share\one\leaf"]);
        var expected = all.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = all.Select(path => FolderEntry.Split(path, false)).ToImmutableSortedSet(FolderPathComparer.Instance);
        Assert(entries.Select(e => e.FullPath).SequenceEqual(expected), "Compact comparator differs from full-path ordinal order");
        var views = new[] { new FolderIndexView(entries, false), new FolderIndexView(entries, false, FolderSearchRows.Build(entries, default)) };
        foreach (var view in views)
        foreach (string term in new[] { "", "x", @"A\", @"子\二\", @"二\轴", @"子-1\q", @"share\one", "😀", "txt", @"\Z9", "MISSING" })
        {
            var matches = expected.Where(path => path.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
            for (int offset = 0; offset <= matches.Length + 1; offset++)
            {
                var page = Find(view, term, "all", offset, 3, default, default);
                Assert(page.Items.Select(hit => hit.FullPath).SequenceEqual(matches.Skip(offset).Take(3)) &&
                    page.HasMore == (matches.Length - offset > 3), "Substring, page or ordering mismatch for " + term);
            }
        }
        Console.WriteLine("Compact/flat path ordering, cross-separator matching and pagination agree on partial and optimized snapshots.");
    }

    private static void CheckProvisionalPaging(string fixture)
    {
        string directory = Directory.CreateDirectory(Path.Combine(fixture, "validation", new string('长', 96), new string('深', 96))).FullName;
        string[] names = ["0-gone.txt", "1-live.txt", "2-file-now-directory.txt", "3-目录仍在.txt", "4-gone.txt", "5-wrong-type"];
        string[] paths = names.Select(name => Path.Combine(directory, name)).ToArray();
        File.WriteAllText(paths[1], "");
        Directory.CreateDirectory(paths[2]);
        File.WriteAllText(paths[3], "");
        File.WriteAllText(paths[5], "");
        var entries = paths.Select((path, i) => FolderEntry.Split(path, i == 5)).ToImmutableSortedSet(FolderPathComparer.Instance);
        foreach (var rows in new FolderSearchRows?[] { null, FolderSearchRows.Build(entries, default) })
        {
            var view = new FolderIndexView(entries, true, rows);
            var first = Find(view, "", "all", 0, 1, default, default);
            var last = Find(view, "", "all", 1, 1, default, default);
            var end = Find(view, "", "all", 2, 1, default, default);
            Assert(first.Items.Single().FullPath == paths[1] && first.HasMore, "Stale first page");
            Assert(last.Items.Single().FullPath == paths[3] && !last.HasMore, "Stale or changed-type items counted toward offset/HasMore");
            Assert(end.Items.Count == 0 && !end.HasMore, "Stale results remained beyond final live page");
        }
        Console.WriteLine("Provisional long-path lookup excludes missing and changed-type rows before offset and HasMore.");
    }

    private static void CheckAllocations(string fixture)
    {
        string prefix = fixture + Path.DirectorySeparatorChar;
        var entries = Enumerable.Range(0, 10000).Select(i => new FolderEntry(prefix, $"part-{i:D5}.step", false))
            .ToImmutableSortedSet(FolderPathComparer.Instance);
        var view = new FolderIndexView(entries, false, FolderSearchRows.Build(entries, default));
        _ = Find(view, "part-", "cad", 9996, 3, default, default);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var page = Find(view, "part-", "cad", 9996, 3, default, default);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(page.Items.Count == 3 && page.HasMore && bytes < 65536, "Deep-offset allocations grow with skipped full paths");
        Console.WriteLine($"10,000 matching synthetic rows, offset 9,996, three results: {bytes} query-thread allocated bytes.");
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
