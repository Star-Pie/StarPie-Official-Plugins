using System.Collections.Generic;
using System.Linq;

namespace StarPie.Plugin.EmojiPicker;

internal static class EmojiSearchEngine
{
    internal static IReadOnlyList<EmojiEntry> GetRecommended(EmojiLibraryDocument doc, int maxCount = 8)
    {
        var ordered = new List<EmojiEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string glyph)
        {
            if (ordered.Count >= maxCount || string.IsNullOrEmpty(glyph) || !seen.Add(glyph)) return;
            EmojiEntry? meta = EmojiCatalog.FindEmoji(glyph);
            ordered.Add(meta ?? new EmojiEntry(glyph, glyph, glyph, "", "recent"));
        }

        foreach (string p in doc.PinnedRecommended) TryAdd(p);
        foreach (string r in doc.RecentEmojis) TryAdd(r);
        foreach (string d in EmojiCatalog.DefaultRecommendedGlyphs) TryAdd(d);

        return ordered;
    }

    internal static IReadOnlyList<EmojiEntry> SearchEmojis(string query)
    {
        string[] tokens = SplitTokens(query);
        var result = new List<EmojiEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (EmojiEntry e in EmojiCatalog.BuiltInEmojis)
        {
            if (!seen.Add(e.Glyph)) continue;
            if (MatchesTokens($"{e.NameZh} {e.NameEn} {e.Keywords} {e.Glyph}", tokens))
                result.Add(e);
        }
        return result;
    }

    internal static IReadOnlyList<CustomSticker> SearchStickers(EmojiLibraryDocument doc, string query, string? groupFilter = null)
    {
        string[] tokens = SplitTokens(query);
        var groupNames = doc.Groups.ToDictionary(g => g.Id, g => g.Name, StringComparer.OrdinalIgnoreCase);

        IEnumerable<CustomSticker> source = doc.Stickers;
        if (string.Equals(groupFilter, "__fav__", StringComparison.OrdinalIgnoreCase))
            source = source.Where(s => s.IsFavorite);
        else if (!string.IsNullOrEmpty(groupFilter) && !string.Equals(groupFilter, "__all__", StringComparison.OrdinalIgnoreCase))
            source = source.Where(s => string.Equals(s.GroupId, groupFilter, StringComparison.OrdinalIgnoreCase));

        return source.Where(s =>
        {
            string grpName = groupNames.GetValueOrDefault(s.GroupId, "");
            return MatchesTokens($"{s.Name} {s.Keywords} {s.Format} {grpName}", tokens);
        }).ToList();
    }

    internal static IReadOnlyList<KaomojiEntry> SearchKaomojis(EmojiLibraryDocument doc, string query, string? categoryFilter = null)
    {
        string[] tokens = SplitTokens(query);
        IEnumerable<KaomojiEntry> source = doc.Kaomojis;
        if (!string.IsNullOrEmpty(categoryFilter) && !string.Equals(categoryFilter, "__all__", StringComparison.OrdinalIgnoreCase))
            source = source.Where(k => string.Equals(k.Category, categoryFilter, StringComparison.OrdinalIgnoreCase));

        return source.Where(k => MatchesTokens($"{k.Name} {k.Keywords} {k.Text} {k.Category}", tokens)).ToList();
    }

    private static string[] SplitTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool MatchesTokens(string haystack, string[] tokens)
    {
        if (tokens.Length == 0) return true;
        foreach (string token in tokens)
        {
            if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
