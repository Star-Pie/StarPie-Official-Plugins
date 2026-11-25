using System.IO;

namespace StarPie.Plugin.QuickSearch;

internal sealed record SearchHit(string FullPath, bool IsDirectory, long Size = 0, long LastWriteUtcTicks = 0)
{
    public string Name => Path.GetFileName(FullPath);
}

internal enum SearchMatchMode
{
    Substring = 0,
    Related = 1,
    Exact = 2
}

internal enum SearchSortField
{
    Path = 0,
    Size = 1,
    ModifiedTime = 2
}

internal enum SearchSizeFilter
{
    Any = 0,
    Tiny = 1,
    Small = 2,
    Medium = 3,
    Large = 4,
    Huge = 5
}

internal enum SearchTimeFilter
{
    Any = 0,
    Today = 1,
    Last7Days = 2,
    Last30Days = 3,
    LastYear = 4
}

internal sealed record SearchQueryOptions(
    SearchMatchMode MatchMode = SearchMatchMode.Related,
    SearchSortField SortField = SearchSortField.Path,
    bool SortDescending = false,
    string PathFilter = "",
    SearchSizeFilter SizeFilter = SearchSizeFilter.Any,
    SearchTimeFilter TimeFilter = SearchTimeFilter.Any)
{
    public static SearchQueryOptions Default { get; } = new();
    public static SearchQueryOptions LegacyDefault { get; } = new(SearchMatchMode.Substring, SearchSortField.Path, false);
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(PathFilter) || SizeFilter != SearchSizeFilter.Any || TimeFilter != SearchTimeFilter.Any;
    public int ActiveFilterCount =>
        (!string.IsNullOrWhiteSpace(PathFilter) ? 1 : 0) +
        (SizeFilter != SearchSizeFilter.Any ? 1 : 0) +
        (TimeFilter != SearchTimeFilter.Any ? 1 : 0);
}

internal static class SearchMetadataFilter
{
    public static bool MatchesSize(SearchSizeFilter filter, bool isDirectory, long size) => filter switch
    {
        SearchSizeFilter.Any => true,
        SearchSizeFilter.Tiny => !isDirectory && size < 102_400L,
        SearchSizeFilter.Small => !isDirectory && size is >= 102_400L and < 1_048_576L,
        SearchSizeFilter.Medium => !isDirectory && size is >= 1_048_576L and < 10_485_760L,
        SearchSizeFilter.Large => !isDirectory && size is >= 10_485_760L and < 104_857_600L,
        SearchSizeFilter.Huge => !isDirectory && size >= 104_857_600L,
        _ => true
    };

    public static long GetMinUtcTicks(SearchTimeFilter filter) => filter switch
    {
        SearchTimeFilter.Today => DateTime.Today.ToUniversalTime().Ticks,
        SearchTimeFilter.Last7Days => DateTime.UtcNow.AddDays(-7).Ticks,
        SearchTimeFilter.Last30Days => DateTime.UtcNow.AddDays(-30).Ticks,
        SearchTimeFilter.LastYear => DateTime.UtcNow.AddYears(-1).Ticks,
        _ => 0L
    };

    public static bool MatchesTime(long lastWriteUtcTicks, long minUtcTicks) =>
        minUtcTicks <= 0L || (lastWriteUtcTicks > 0L && lastWriteUtcTicks >= minUtcTicks);
}

internal sealed record SearchPage(IReadOnlyList<SearchHit> Items, bool HasMore)
{
    public static SearchPage Empty { get; } = new(Array.Empty<SearchHit>(), false);
}

internal static class SearchCategories
{
    public const string All = "all";
    public const string Apps = "apps";
    public const string Cad = "cad";
    public const string Docs = "docs";
    public const string Video = "video";
    public const string Folders = "folders";
    public const string SystemTools = "system";

    private static readonly Dictionary<string, string[]> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Apps] = ["exe", "bat", "cmd", "ps1", "lnk"],
        [Cad] = ["sldprt", "sldasm", "slddrw", "step", "stp", "iges", "igs", "dwg", "dxf", "prt", "asm", "catpart", "x_t", "x_b"],
        [Docs] = ["md", "txt", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf", "py", "cs", "cpp", "c", "h", "json", "xml", "csv"],
        [Video] = ["mp4", "mkv", "avi", "mov", "flv", "wmv", "rmvb", "webm", "ts", "m4v", "3gp", "f4v"]
    };
    private static readonly string SystemDirectory = Environment.SystemDirectory;
    private static readonly string WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static bool Matches(string category, FolderEntry hit)
    {
        if (string.Equals(category, SystemTools, StringComparison.OrdinalIgnoreCase)) return IsSystemTool(hit);
        if (string.Equals(category, Folders, StringComparison.OrdinalIgnoreCase)) return hit.IsDirectory;
        if (!FileExtensions.TryGetValue(category ?? All, out var extensions)) return true;
        if (hit.IsDirectory) return false;
        var extension = Path.GetExtension(hit.Name.AsSpan());
        return !extension.IsEmpty && MatchesExtension(extension[1..], extensions);
    }

    private static bool IsSystemTool(FolderEntry hit)
    {
        if (hit.IsDirectory) return false;
        var name = hit.Name.AsSpan();
        var extension = Path.GetExtension(name);
        if (extension.Equals(".msc", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cpl", StringComparison.OrdinalIgnoreCase)) return true;
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var directory = Path.TrimEndingDirectorySeparator(hit.Prefix.AsSpan());
        if (directory.Equals(WindowsDirectory, StringComparison.OrdinalIgnoreCase))
            return name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
        return directory.Equals(SystemDirectory, StringComparison.OrdinalIgnoreCase) &&
            (name.Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("control.exe", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("calc.exe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesExtension(ReadOnlySpan<char> extension, string[] candidates)
    {
        foreach (var candidate in candidates)
            if (extension.Equals(candidate.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
