using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using StarPie.Plugin.QuickSearch;

internal static class CompactQueryChecks
{
    private delegate SearchPage FindPage(FolderIndexView view, string query, string category,
        int offset, int size, SearchQueryOptions options, CancellationToken token, CancellationToken sessionToken);

    private static readonly FindPage Find = typeof(FolderSearchIndex)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(m => m.Name == "Find" && m.GetParameters().Length == 8)
        .CreateDelegate<FindPage>();

    internal static void Run(string fixture)
    {
        ComparePathsAndQueries(fixture);
        CheckMatchModesFilterAndSort(fixture);
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
                var page = Find(view, term, "all", offset, 3, SearchQueryOptions.LegacyDefault, default, default);
                Assert(page.Items.Select(hit => hit.FullPath).SequenceEqual(matches.Skip(offset).Take(3)) &&
                    page.HasMore == (matches.Length - offset > 3), "Substring, page or ordering mismatch for " + term);
            }
        }
        Console.WriteLine("Compact/flat path ordering, cross-separator matching and pagination agree on partial and optimized snapshots.");
    }

    private static void CheckMatchModesFilterAndSort(string fixture)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long threeDaysAgo = DateTime.UtcNow.AddDays(-3).Ticks;
        long fifteenDaysAgo = DateTime.UtcNow.AddDays(-15).Ticks;
        long fourHundredDaysAgo = DateTime.UtcNow.AddDays(-400).Ticks;

        string dirAlpha = Path.GetFullPath(Path.Combine(fixture, "proj", "sub")) + Path.DirectorySeparatorChar;
        string dirBeta = Path.GetFullPath(Path.Combine(fixture, "archive", "logs")) + Path.DirectorySeparatorChar;

        var rawEntries = new FolderEntry[]
        {
            new(dirAlpha, "Quick_Search.cs", false, 2048, nowTicks),                  // Tiny (<100KB), Today
            new(dirAlpha, "QuickSearchWindow.cs", false, 524_288, threeDaysAgo),      // Small (512KB), Last7Days
            new(dirAlpha, "test.pdf", false, 5_242_880, fifteenDaysAgo),              // Medium (5MB), Last30Days
            new(dirAlpha, "test.pdf.bak", false, 52_428_800, fourHundredDaysAgo),     // Large (50MB), >1yr
            new(dirBeta, "dump.iso", false, 209_715_200, threeDaysAgo),               // Huge (200MB), Last7Days
            new(dirBeta, "sub", true, 0, nowTicks)                                    // Directory
        };
        var entries = rawEntries.ToImmutableSortedSet(FolderPathComparer.Instance);
        var views = new[]
        {
            new FolderIndexView(entries, false),
            new FolderIndexView(entries, false, FolderSearchRows.Build(entries, default))
        };

        foreach (var view in views)
        {
            // Related mode: multi-token, separator-stripped, acronym
            var qs = Find(view, "quicksearch", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Related), default, default);
            Assert(qs.Items.Select(i => i.Name).SequenceEqual(["QuickSearchWindow.cs", "Quick_Search.cs"]),
                "Related separator-insensitive match");

            var multi = Find(view, "proj window", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Related), default, default);
            Assert(multi.Items.Single().Name == "QuickSearchWindow.cs", "Related multi-token path + name match");

            var acronym = Find(view, "qsw", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Related), default, default);
            Assert(acronym.Items.Single().Name == "QuickSearchWindow.cs", "Related CamelCase acronym match");

            // Exact mode: exact name, exact stem, exact path suffix, never substring
            var exactPdf = Find(view, "test.pdf", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Exact), default, default);
            Assert(exactPdf.Items.Single().Name == "test.pdf", "Exact filename avoids .bak suffix match");

            var exactStem = Find(view, "test", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Exact), default, default);
            Assert(exactStem.Items.Single().Name == "test.pdf", "Exact stem matches base name");

            var exactSub = Find(view, @"sub\test.pdf", "all", 0, 10, new SearchQueryOptions(SearchMatchMode.Exact), default, default);
            Assert(exactSub.Items.Single().Name == "test.pdf", "Exact path suffix matches at segment boundary");

            // Path, Size, Time filters
            var pathFiltered = Find(view, "", "all", 0, 10, new SearchQueryOptions(PathFilter: @"archive\logs"), default, default);
            Assert(pathFiltered.Items.Count == 2 && pathFiltered.Items.All(i => i.FullPath.Contains("archive")),
                "PathFilter restricts prefix");

            Assert(Find(view, "", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Tiny), default, default)
                .Items.Single().Name == "Quick_Search.cs", "SizeFilter.Tiny excludes directories and matches <100KB");
            Assert(Find(view, "", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Small), default, default)
                .Items.Single().Name == "QuickSearchWindow.cs", "SizeFilter.Small");
            Assert(Find(view, "", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Medium), default, default)
                .Items.Single().Name == "test.pdf", "SizeFilter.Medium");
            Assert(Find(view, "", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Large), default, default)
                .Items.Single().Name == "test.pdf.bak", "SizeFilter.Large");
            Assert(Find(view, "", "all", 0, 10, new SearchQueryOptions(SizeFilter: SearchSizeFilter.Huge), default, default)
                .Items.Single().Name == "dump.iso", "SizeFilter.Huge");

            var recentWeek = Find(view, "", "all", 0, 10, new SearchQueryOptions(TimeFilter: SearchTimeFilter.Last7Days), default, default);
            Assert(recentWeek.Items.Count == 4 && !recentWeek.Items.Any(i => i.Name is "test.pdf" or "test.pdf.bak"),
                "TimeFilter.Last7Days");

            // Sort by Size descending + pagination (files before directories)
            var sizePage1 = Find(view, "", "all", 0, 3, new SearchQueryOptions(SortField: SearchSortField.Size, SortDescending: true), default, default);
            Assert(sizePage1.HasMore && sizePage1.Items.Select(i => i.Name).SequenceEqual(["dump.iso", "test.pdf.bak", "test.pdf"]),
                "Size desc page 1");
            var sizePage2 = Find(view, "", "all", 3, 3, new SearchQueryOptions(SortField: SearchSortField.Size, SortDescending: true), default, default);
            Assert(!sizePage2.HasMore && sizePage2.Items.Select(i => i.Name).SequenceEqual(["QuickSearchWindow.cs", "Quick_Search.cs", "sub"]),
                "Size desc page 2 puts directories after files");

            // Sort by ModifiedTime descending
            var timeDesc = Find(view, "", "all", 0, 10, new SearchQueryOptions(SortField: SearchSortField.ModifiedTime, SortDescending: true), default, default);
            Assert(timeDesc.Items.Last().Name == "test.pdf.bak" &&
                timeDesc.Items.First().LastWriteUtcTicks == nowTicks, "ModifiedTime desc puts newest first and oldest last");

            // Sort by Path descending
            var pathDesc = Find(view, "", "all", 0, 10, new SearchQueryOptions(SortField: SearchSortField.Path, SortDescending: true), default, default);
            Assert(pathDesc.Items.Select(i => i.FullPath).SequenceEqual(entries.Select(e => e.FullPath).Reverse()),
                "Path desc streams exact reverse order");
        }

        // Verify bounded top-K heap matches full sort over 2500 entries
        var largeBuilder = ImmutableSortedSet.CreateBuilder<FolderEntry>(FolderPathComparer.Instance);
        for (int i = 0; i < 2500; i++)
        {
            long sz = ((i * 7919L) % 50000L) + 1;
            largeBuilder.Add(new FolderEntry(dirAlpha, $"item-{i:D4}.dat", false, sz, nowTicks - i * 1000L));
        }
        var largeView = new FolderIndexView(largeBuilder.ToImmutable(), false);
        var paged = Find(largeView, "", "all", 10, 25, new SearchQueryOptions(SortField: SearchSortField.Size, SortDescending: true), default, default);
        var expectedTop = largeBuilder.OrderByDescending(e => e.Size).ThenBy(e => e, FolderPathComparer.Instance).Skip(10).Take(25).Select(e => e.Name);
        Assert(paged.HasMore && paged.Items.Select(i => i.Name).SequenceEqual(expectedTop), "Top-K PriorityQueue sort matches full sort");

        Console.WriteLine("Match modes (Related/Exact/Substring), path/size/time filters, and Path/Size/ModifiedTime sorting checks passed.");
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
            var first = Find(view, "", "all", 0, 1, SearchQueryOptions.LegacyDefault, default, default);
            var last = Find(view, "", "all", 1, 1, SearchQueryOptions.LegacyDefault, default, default);
            var end = Find(view, "", "all", 2, 1, SearchQueryOptions.LegacyDefault, default, default);
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
        _ = Find(view, "part-", "cad", 9996, 3, SearchQueryOptions.LegacyDefault, default, default);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var page = Find(view, "part-", "cad", 9996, 3, SearchQueryOptions.LegacyDefault, default, default);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(page.Items.Count == 3 && page.HasMore && bytes < 65536, "Deep-offset allocations grow with skipped full paths");
        Console.WriteLine($"10,000 matching synthetic rows, offset 9,996, three results: {bytes} query-thread allocated bytes.");
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
