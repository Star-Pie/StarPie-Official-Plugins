using System.IO;

namespace StarPie.Plugin.QuickSearch;

internal sealed record SearchHit(string FullPath, bool IsDirectory)
{
    public string Name => Path.GetFileName(FullPath);
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
