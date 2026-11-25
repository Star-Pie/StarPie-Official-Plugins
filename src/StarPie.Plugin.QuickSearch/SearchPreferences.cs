using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StarPie.Plugin.QuickSearch;

internal sealed class SearchPreferences(IPluginSettings settings, PluginActionInput legacy)
{
    internal double Width => Dimension("QuickSearchWidth", 740, 560);
    internal double Height => Dimension("QuickSearchHeight", 530, 380);
    internal bool Pinned => settings.GetBool("QuickSearchPinned", legacy.Bool("QuickSearchPinned"));

    internal SearchMatchMode MatchMode => (settings.Get("QuickSearchMatchMode") ?? "").Trim().ToLowerInvariant() switch
    {
        "exact" => SearchMatchMode.Exact,
        "substring" => SearchMatchMode.Substring,
        _ => SearchMatchMode.Related
    };

    internal SearchSortField SortField => (settings.Get("QuickSearchSortField") ?? "").Trim().ToLowerInvariant() switch
    {
        "size" => SearchSortField.Size,
        "modified" => SearchSortField.ModifiedTime,
        _ => SearchSortField.Path
    };

    internal bool SortDescending => settings.GetBool("QuickSearchSortDescending", false);

    internal void SetMatchMode(SearchMatchMode mode)
    {
        string value = mode switch
        {
            SearchMatchMode.Exact => "exact",
            SearchMatchMode.Substring => "substring",
            _ => "related"
        };
        settings.Set("QuickSearchMatchMode", value);
        settings.Save();
    }

    internal void SetSort(SearchSortField field, bool descending)
    {
        string fieldValue = field switch
        {
            SearchSortField.Size => "size",
            SearchSortField.ModifiedTime => "modified",
            _ => "path"
        };
        settings.Set("QuickSearchSortField", fieldValue);
        settings.Set("QuickSearchSortDescending", descending.ToString());
        settings.Save();
    }

    private double Dimension(string key, double standard, double minimum)
    {
        double value = settings.GetDouble(key, legacy.Double(key, standard));
        return double.IsFinite(value) && value >= minimum ? Math.Min(value, 16384) : standard;
    }

    internal bool PendingScopeClear => settings.GetBool("pendingScopeClear");

    internal void ConfirmScopeCleared()
    {
        settings.Set("pendingScopeClear", null);
        settings.Save();
    }

    internal string[] Roots()
    {
        string? first = settings.Get("roots");
        if (string.IsNullOrEmpty(first)) return [];
        var json = new StringBuilder(first);
        for (int part = 1; settings.Get(RootKey(part)) is { } fragment; part++) json.Append(fragment);
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json.ToString()) ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException) { return []; }
    }

    internal void SetRoots(IEnumerable<string> roots)
    {
        // The SDK trims individual values at MaxParameterValueLength. Store long
        // root lists in parts and commit the complete set in one settings save.
        string[] paths = roots.ToArray();
        string json = JsonSerializer.Serialize(paths);
        int part = 0;
        for (int start = 0; start < json.Length; start += PluginApi.MaxParameterValueLength)
        {
            int length = Math.Min(json.Length - start, PluginApi.MaxParameterValueLength);
            settings.Set(RootKey(part++), json.Substring(start, length));
        }
        while (settings.Get(RootKey(part)) is not null) settings.Set(RootKey(part++), null);
        settings.Set("pendingScopeClear", paths.Length == 0 ? bool.TrueString : null);
        settings.Save();
    }

    private static string RootKey(int part) => part == 0 ? "roots" : "roots." + part.ToString(CultureInfo.InvariantCulture);

    internal void SetPinned(bool pinned)
    {
        settings.Set("QuickSearchPinned", pinned.ToString());
        settings.Save();
    }

    internal void SaveSize(double width, double height)
    {
        settings.Set("QuickSearchWidth", width.ToString("R", CultureInfo.InvariantCulture));
        settings.Set("QuickSearchHeight", height.ToString("R", CultureInfo.InvariantCulture));
        settings.Save();
    }
}
