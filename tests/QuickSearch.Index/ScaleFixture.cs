using System.Diagnostics;
using System.IO;
using StarPie.Plugin.QuickSearch;

internal static class ScaleFixture
{
    private const int DirectoryCount = 400;
    private const int FilesPerDirectory = 250;
    private const string Marker = "QuickSearch.Index 400x250 v1";

    internal static void Create(string parent)
    {
        if (!Path.IsPathFullyQualified(parent)) throw new ArgumentException("Supply an absolute isolated fixture parent.");
        var fixture = Path.Combine(parent, $"scale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        string selected = Directory.CreateDirectory(Path.Combine(fixture, "selected")).FullName;
        for (int d = 0; d < DirectoryCount; d++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(selected, $"project-{d:D3}")).FullName;
            for (int i = 0; i < FilesPerDirectory; i++)
            {
                string extension = (i % 4) switch { 0 => ".sldasm", 1 => ".cs", 2 => ".sldprt", _ => ".txt" };
                using var file = File.Create(Path.Combine(directory, $"assembly-{i:D3}{extension}"));
            }
        }
        File.WriteAllText(Path.Combine(fixture, "fixture.txt"), Marker);
        Console.WriteLine("Reusable synthetic fixture: " + fixture);
    }

    internal static async Task Measure(string fixture)
    {
        if (!Path.IsPathFullyQualified(fixture) || !Path.GetFileName(fixture).StartsWith("scale-", StringComparison.Ordinal) ||
            File.ReadAllText(Path.Combine(fixture, "fixture.txt")) != Marker) throw new ArgumentException("Expected the dedicated scale fixture.");
        var selected = Path.Combine(fixture, "selected");
        var cache = Path.Combine(fixture, "cache-" + Guid.NewGuid().ToString("N"));
        try { await Measure(indexRoot: selected, cache: cache); }
        finally
        {
            if (Path.GetDirectoryName(cache) != fixture) throw new InvalidOperationException("Unexpected cache path.");
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }
    }

    private static async Task Measure(string indexRoot, string cache)
    {
        using var process = Process.GetCurrentProcess();
        long baseHeap = GC.GetTotalMemory(true);
        process.Refresh();
        long baseWorking = process.WorkingSet64, peakHeap = baseHeap, peakWorking = baseWorking;
        double first, firstPage, complete;
        var timer = Stopwatch.StartNew();
        using (var index = new FolderSearchIndex(cache))
        {
            await index.SetRootsAsync([indexRoot], CancellationToken.None);
            first = timer.Elapsed.TotalMilliseconds;
            var partial = index.Status;
            var early = await index.SearchAsync("", "all", 0, 40, CancellationToken.None);
            firstPage = timer.Elapsed.TotalMilliseconds;
            if (!partial.IsRefreshing || partial.IndexedItemCount == 0 || early.Items.Count == 0)
                throw new Exception("Expected usable partial results before full index completion.");
            while (index.Status.IsRefreshing)
            {
                if (timer.Elapsed.TotalMinutes > 5) throw new TimeoutException("Scale fixture failed to settle.");
                process.Refresh();
                peakWorking = Math.Max(peakWorking, process.WorkingSet64);
                peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
                await Task.Delay(15);
            }
            complete = timer.Elapsed.TotalMilliseconds;
            int expected = DirectoryCount * (FilesPerDirectory + 1) + 1;
            if (index.Status.HasFailed || index.Status.IndexedItemCount != expected) throw new Exception("Incomplete scale scope.");
            process.Refresh();
            peakWorking = Math.Max(peakWorking, process.WorkingSet64);
            peakHeap = Math.Max(peakHeap, GC.GetTotalMemory(false));
            long retained = GC.GetTotalMemory(true) - baseHeap;
            var positives = new List<double>();
            var negatives = new List<double>();
            for (int i = 0; i < 48; i++)
            {
                string positive = (i % 3) switch { 0 => "assembly-", 1 => $"project-{i * 7:D3}", _ => "project-399\\assembly-248" };
                timer.Restart();
                var page = await index.SearchAsync(positive, "cad", 0, 40, CancellationToken.None);
                positives.Add(timer.Elapsed.TotalMilliseconds);
                if (page.Items.Count == 0) throw new Exception("Missing positive CAD query.");
                timer.Restart();
                page = await index.SearchAsync($"absent-{i:D3}", "all", 0, 40, CancellationToken.None);
                negatives.Add(timer.Elapsed.TotalMilliseconds);
                if (page.Items.Count != 0) throw new Exception("Unexpected negative result.");
            }
            timer.Restart();
            await index.SetRootsAsync([indexRoot], CancellationToken.None);
            var tail = await index.SearchAsync("assembly-", "cad", 49999, 40, CancellationToken.None);
            double tailTime = timer.Elapsed.TotalMilliseconds;
            if (tail.Items.Count != 1 || tail.HasMore) throw new Exception("Expected final indexed CAD item past 49,999 hits.");
            Console.WriteLine($"Real synthetic NTFS corpus: 100000 zero-byte files, 400 project directories, 100401 indexed entries. " +
                $"Cold first batch {first:F1} ms ({partial.IndexedItemCount} entries); first 40-row page {firstPage:F1} ms; settled + persisted {complete:F1} ms.");
            Console.WriteLine($"48 positive first pages p50/p95 {Percentile(positives, .5):F2}/{Percentile(positives, .95):F2} ms; " +
                $"48 negatives {Percentile(negatives, .5):F2}/{Percentile(negatives, .95):F2} ms; repeated scope + page at offset 49999 {tailTime:F2} ms.");
            Console.WriteLine($"Managed heap retained delta after GC {MiB(retained):F1} MiB, sampled managed peak delta {MiB(peakHeap - baseHeap):F1} MiB, " +
                $"sampled process working-set peak delta {MiB(peakWorking - baseWorking):F1} MiB, baseline working set {MiB(baseWorking):F1} MiB, " +
                $"binary cache {MiB(new FileInfo(Path.Combine(cache, "folder-index-v1.bin")).Length):F1} MiB.");
        }
        using var restored = new FolderSearchIndex(cache);
        timer.Restart();
        await restored.SetRootsAsync([indexRoot], CancellationToken.None);
        var firstRestored = await restored.SearchAsync("assembly-", "cad", 0, 40, CancellationToken.None);
        if (firstRestored.Items.Count == 0) throw new Exception("Persisted catalogue missing rows.");
        Console.WriteLine($"Process-local new index instance (validated partial disk cache + first available CAD page of {firstRestored.Items.Count}) {timer.Elapsed.TotalMilliseconds:F1} ms.");
        await restored.SetRootsAsync([], CancellationToken.None);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        values.Sort();
        return values[(int)Math.Ceiling(values.Count * percentile) - 1];
    }

    private static double MiB(long bytes) => bytes / 1048576d;
}
