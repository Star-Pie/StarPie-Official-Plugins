using System.IO;

namespace StarPie.Plugin.QuickSearch;

// Prefix includes its trailing separator. Siblings share that string; file rows
// carry just their basename until a page actually needs a full result path.
internal readonly record struct FolderEntry(string Prefix, string Name, bool IsDirectory)
{
    internal string FullPath => string.Concat(Prefix, Name);

    internal static FolderEntry Split(string path, bool directory)
    {
        int split = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;
        return new FolderEntry(path[..split], path[split..], directory);
    }

    internal bool AtOrBelow(string root)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (root.Length <= Prefix.Length)
            return Prefix.StartsWith(root, comparison) &&
                (root.Length == Prefix.Length || Path.EndsInDirectorySeparator(root) || Prefix[root.Length] == Path.DirectorySeparatorChar);
        return root.StartsWith(Prefix, comparison) && Name.AsSpan().Equals(root.AsSpan(Prefix.Length), comparison);
    }

    internal bool Matches(string term, int boundary, bool prefixMatches)
    {
        if (prefixMatches || Name.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        return boundary > 0 && Prefix.AsSpan().EndsWith(term.AsSpan(0, boundary), StringComparison.OrdinalIgnoreCase) &&
            Name.AsSpan().StartsWith(term.AsSpan(boundary), StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class FolderPathComparer : IComparer<FolderEntry>, IEqualityComparer<FolderEntry>
{
    internal static readonly FolderPathComparer Instance = new();
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    public bool Equals(FolderEntry left, FolderEntry right) =>
        IgnoreCase.Equals(left.Prefix, right.Prefix) && IgnoreCase.Equals(left.Name, right.Name);

    public int GetHashCode(FolderEntry entry) =>
        HashCode.Combine(IgnoreCase.GetHashCode(entry.Prefix), IgnoreCase.GetHashCode(entry.Name));

    public int Compare(FolderEntry left, FolderEntry right)
    {
        if (ReferenceEquals(left.Prefix, right.Prefix)) return IgnoreCase.Compare(left.Name, right.Name);
        int common = Math.Min(left.Prefix.Length, right.Prefix.Length);
        int order = left.Prefix.AsSpan(0, common).CompareTo(right.Prefix.AsSpan(0, common), StringComparison.OrdinalIgnoreCase);
        if (order != 0) return order;
        if (left.Prefix.Length == right.Prefix.Length) return IgnoreCase.Compare(left.Name, right.Name);
        return left.Prefix.Length < right.Prefix.Length
            ? CompareTail(left.Name, right.Prefix.AsSpan(common), right.Name)
            : -CompareTail(right.Name, left.Prefix.AsSpan(common), left.Name);
    }

    // The shorter prefix ended at a separator. Compare the remaining pieces
    // directly, preserving ordinal full-path order across nested directories.
    private static int CompareTail(string name, ReadOnlySpan<char> otherPrefix, string otherName)
    {
        int length = Math.Min(name.Length, otherPrefix.Length);
        int order = name.AsSpan(0, length).CompareTo(otherPrefix[..length], StringComparison.OrdinalIgnoreCase);
        if (order != 0) return order;
        if (length < otherPrefix.Length) return -1;
        return name.AsSpan(length).CompareTo(otherName, StringComparison.OrdinalIgnoreCase);
    }
}
