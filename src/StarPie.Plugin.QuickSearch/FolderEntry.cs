using System.IO;

namespace StarPie.Plugin.QuickSearch;

// Prefix includes its trailing separator. Siblings share that string; file rows
// carry just their basename until a page actually needs a full result path.
internal readonly record struct FolderEntry(string Prefix, string Name, bool IsDirectory, long Size = 0, long LastWriteUtcTicks = 0)
{
    internal string FullPath => string.Concat(Prefix, Name);

    internal static FolderEntry Split(string path, bool directory, long size = 0, long lastWriteUtcTicks = 0)
    {
        int split = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;
        return new FolderEntry(path[..split], path[split..], directory, size, lastWriteUtcTicks);
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

    internal bool MatchesExact(string query, int boundary)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (boundary > 0)
        {
            if (query.Length > Prefix.Length + Name.Length || query.Length <= Name.Length) return false;
            int prefixNeedLength = query.Length - Name.Length;
            ReadOnlySpan<char> querySpan = query.AsSpan();
            if (!querySpan[prefixNeedLength..].Equals(Name.AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            ReadOnlySpan<char> prefixSpan = Prefix.AsSpan();
            if (!prefixSpan[^prefixNeedLength..].Equals(querySpan[..prefixNeedLength], StringComparison.OrdinalIgnoreCase)) return false;
            int tailStart = prefixSpan.Length - prefixNeedLength;
            return tailStart == 0 || prefixSpan[tailStart - 1] == Path.DirectorySeparatorChar;
        }
        if (Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return true;
        return !IsDirectory && !query.Contains('.') &&
            Path.GetFileNameWithoutExtension(Name.AsSpan()).Equals(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    internal bool MatchesRelatedToken(string token, int boundary, bool prefixMatches)
    {
        if (Matches(token, boundary, prefixMatches)) return true;
        if (boundary > 0) return false;
        return MatchesCompactOrAcronym(Name.AsSpan(), token.AsSpan());
    }

    private static bool MatchesCompactOrAcronym(ReadOnlySpan<char> name, ReadOnlySpan<char> token)
    {
        if (name.IsEmpty || token.IsEmpty) return false;
        if (MatchesSeparatorInsensitive(name, token)) return true;
        return CountNormalizedLength(token) >= 2 && MatchesWordAcronym(name, token);
    }

    private static bool IsWordSeparator(char c) => c is '-' or '_' or '.' or ' ';

    private static int CountNormalizedLength(ReadOnlySpan<char> text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
            if (!IsWordSeparator(text[i])) count++;
        return count;
    }

    private static bool MatchesSeparatorInsensitive(ReadOnlySpan<char> name, ReadOnlySpan<char> token)
    {
        int firstToken = 0;
        while (firstToken < token.Length && IsWordSeparator(token[firstToken])) firstToken++;
        if (firstToken == token.Length) return false;

        for (int start = 0; start < name.Length; start++)
        {
            if (IsWordSeparator(name[start])) continue;
            int ni = start;
            int ti = firstToken;
            while (true)
            {
                while (ni < name.Length && IsWordSeparator(name[ni])) ni++;
                while (ti < token.Length && IsWordSeparator(token[ti])) ti++;
                if (ti == token.Length) return true;
                if (ni == name.Length) break;
                if (char.ToUpperInvariant(name[ni]) != char.ToUpperInvariant(token[ti])) break;
                ni++;
                ti++;
            }
        }
        return false;
    }

    private static bool MatchesWordAcronym(ReadOnlySpan<char> name, ReadOnlySpan<char> token)
    {
        int ti = 0;
        while (ti < token.Length && IsWordSeparator(token[ti])) ti++;
        if (ti == token.Length) return false;

        int lastMatchedIndex = -2;
        for (int ni = 0; ni < name.Length; ni++)
        {
            char c = name[ni];
            if (IsWordSeparator(c)) continue;
            if (char.ToUpperInvariant(c) != char.ToUpperInvariant(token[ti])) continue;
            bool isWordStart = ni == 0 || IsWordSeparator(name[ni - 1]) ||
                (char.IsUpper(c) && char.IsLower(name[ni - 1])) ||
                (char.IsDigit(c) != char.IsDigit(name[ni - 1]) && !IsWordSeparator(name[ni - 1]));
            if (!isWordStart && ni != lastMatchedIndex + 1) continue;
            lastMatchedIndex = ni;
            ti++;
            while (ti < token.Length && IsWordSeparator(token[ti])) ti++;
            if (ti == token.Length) return true;
        }
        return false;
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
