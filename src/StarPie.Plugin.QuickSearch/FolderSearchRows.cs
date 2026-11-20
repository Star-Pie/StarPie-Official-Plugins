using System.Collections.Immutable;

namespace StarPie.Plugin.QuickSearch;

// Adjacent sorted paths often share the same containing directory. A term
// without a separator only needs to search that prefix once for the whole run.
internal sealed record FolderSearchRows(FolderEntry[] Hits, FolderSearchRows.Group[] Groups)
{
    internal readonly record struct Group(int Start, int End);

    internal static FolderSearchRows Build(ImmutableSortedSet<FolderEntry> entries, CancellationToken token)
    {
        var hits = new FolderEntry[entries.Count];
        var groups = new List<Group>();
        int i = 0, start = 0;
        foreach (var hit in entries)
        {
            if ((i & 127) == 0) token.ThrowIfCancellationRequested();
            if (i == 0 || !string.Equals(hits[start].Prefix, hit.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (i != 0) groups.Add(new Group(start, i));
                start = i;
            }
            hits[i++] = hit;
        }
        if (i != 0) groups.Add(new Group(start, i));
        return new FolderSearchRows(hits, groups.ToArray());
    }
}
