using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StarPie.Plugin.EmojiPicker;

public sealed record ImportSummary(int ImportedCount, int SkippedCount, IReadOnlyList<CustomSticker> Added);

internal sealed class EmojiLibraryStorage
{
    internal const int MaxStickerBytes = 4 * 1024 * 1024;
    internal const int MaxLibraryFileBytes = 4 * 1024 * 1024;
    internal const int MaxManagedStickers = 1000;
    internal const int MaxMountedFolders = 16;
    internal const int MaxStickersPerMount = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;
    private readonly string _libraryPath;
    private readonly string _stickersDir;

    internal EmojiLibraryStorage(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _libraryPath = Path.Combine(_dataDirectory, "emoji-library.json");
        _stickersDir = Path.Combine(_dataDirectory, "stickers");
    }

    internal string DataDirectory => _dataDirectory;
    internal string LibraryPath => _libraryPath;

    internal EmojiLibraryDocument Load(string defaultGroupName)
    {
        if (!File.Exists(_libraryPath))
            return CreateDefault(defaultGroupName);

        try
        {
            var info = new FileInfo(_libraryPath);
            if (info.Length > MaxLibraryFileBytes)
                return CreateRecoveryDocument(defaultGroupName);

            string raw = File.ReadAllText(_libraryPath);
            EmojiLibraryDocument? doc = JsonSerializer.Deserialize<EmojiLibraryDocument>(raw, JsonOptions);
            if (doc == null || doc.SchemaVersion <= 0)
                return CreateRecoveryDocument(defaultGroupName);

            NormalizeDocument(doc, defaultGroupName);
            return doc;
        }
        catch
        {
            return CreateRecoveryDocument(defaultGroupName);
        }
    }

    internal void Save(EmojiLibraryDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.RequiresRecovery)
            throw new InvalidOperationException("Cannot overwrite a damaged emoji library without explicit rebuild.");

        Directory.CreateDirectory(_dataDirectory);
        string json = JsonSerializer.Serialize(doc, JsonOptions);
        string tmp = _libraryPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _libraryPath, overwrite: true);
    }

    internal EmojiLibraryDocument RebuildAfterRecovery(string defaultGroupName)
    {
        EmojiLibraryDocument fresh = CreateDefault(defaultGroupName);
        fresh.RequiresRecovery = false;
        Save(fresh);
        return fresh;
    }

    internal static EmojiLibraryDocument CreateDefault(string defaultGroupName)
    {
        return new EmojiLibraryDocument
        {
            SchemaVersion = 1,
            PinnedRecommended = new List<string>(),
            RecentEmojis = new List<string>(EmojiCatalog.DefaultRecommendedGlyphs),
            FavoriteEmojis = new List<string> { "😄", "👍", "🚀" },
            Groups = new List<StickerGroup>
            {
                new() { Id = "grp_default", Name = string.IsNullOrWhiteSpace(defaultGroupName) ? "My Stickers" : defaultGroupName }
            },
            Stickers = new List<CustomSticker>(),
            Kaomojis = EmojiCatalog.CreateDefaultKaomojis(),
            Preferences = new EmojiPreferences()
        };
    }

    private static void NormalizeDocument(EmojiLibraryDocument doc, string defaultGroupName)
    {
        doc.PinnedRecommended ??= new List<string>();
        doc.RecentEmojis ??= new List<string>();
        doc.FavoriteEmojis ??= new List<string>();
        doc.Groups ??= new List<StickerGroup>();
        doc.Stickers ??= new List<CustomSticker>();
        doc.Kaomojis ??= new List<KaomojiEntry>();
        doc.Preferences ??= new EmojiPreferences();

        if (!doc.Groups.Any(g => !g.IsMounted))
        {
            doc.Groups.Insert(0, new StickerGroup
            {
                Id = "grp_default",
                Name = string.IsNullOrWhiteSpace(defaultGroupName) ? "My Stickers" : defaultGroupName
            });
        }

        if (doc.Kaomojis.Count == 0 && doc.RecentEmojis.Count == 0 && doc.Stickers.Count == 0)
            doc.Kaomojis = EmojiCatalog.CreateDefaultKaomojis();
    }

    private EmojiLibraryDocument CreateRecoveryDocument(string defaultGroupName)
    {
        string? backupPath = null;
        try
        {
            if (File.Exists(_libraryPath))
            {
                backupPath = $"{_libraryPath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Copy(_libraryPath, backupPath, overwrite: true);
            }
        }
        catch
        {
            backupPath = null;
        }

        EmojiLibraryDocument doc = CreateDefault(defaultGroupName);
        doc.RequiresRecovery = true;
        doc.RecoveryBackupPath = backupPath;
        return doc;
    }

    internal string ResolveStickerPath(CustomSticker sticker)
    {
        if (string.IsNullOrWhiteSpace(sticker.FilePath)) return "";
        if (sticker.IsMounted || Path.IsPathRooted(sticker.FilePath))
            return Path.GetFullPath(sticker.FilePath);
        return Path.GetFullPath(Path.Combine(_dataDirectory, sticker.FilePath));
    }

    internal StickerGroup EnsureManagedGroup(EmojiLibraryDocument doc, string groupIdOrName)
    {
        StickerGroup? existing = doc.Groups.FirstOrDefault(g =>
            !g.IsMounted && string.Equals(g.Id, groupIdOrName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        StickerGroup? fallback = doc.Groups.FirstOrDefault(g => !g.IsMounted);
        if (fallback != null) return fallback;

        var created = new StickerGroup
        {
            Id = "grp_" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(groupIdOrName) ? "My Stickers" : groupIdOrName.Trim()
        };
        doc.Groups.Add(created);
        return created;
    }

    internal StickerGroup CreateManagedGroup(EmojiLibraryDocument doc, string name)
    {
        string clean = string.IsNullOrWhiteSpace(name) ? "New Group" : name.Trim();
        var group = new StickerGroup
        {
            Id = "grp_" + Guid.NewGuid().ToString("N")[..8],
            Name = clean,
            IsMounted = false,
            SourcePath = ""
        };
        doc.Groups.Add(group);
        return group;
    }

    internal ImportSummary ImportFiles(EmojiLibraryDocument doc, IEnumerable<string> filePaths, string targetGroupId)
    {
        StickerGroup group = EnsureManagedGroup(doc, targetGroupId);
        Directory.CreateDirectory(_stickersDir);

        var added = new List<CustomSticker>();
        int skipped = 0;

        foreach (string rawPath in filePaths)
        {
            if (doc.Stickers.Count(s => !s.IsMounted) >= MaxManagedStickers)
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawPath) || !File.Exists(rawPath))
            {
                skipped++;
                continue;
            }

            var fileInfo = new FileInfo(rawPath);
            if (IsSymbolicLinkOrJunction(fileInfo) ||
                fileInfo.Length <= 0 || fileInfo.Length > MaxStickerBytes)
            {
                skipped++;
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fileInfo.FullName);
            }
            catch
            {
                skipped++;
                continue;
            }

            string? format = DetectImageFormat(bytes, fileInfo.Extension);
            if (format == null)
            {
                skipped++;
                continue;
            }

            string ext = FormatToExtension(format);
            string id = "s_" + Guid.NewGuid().ToString("N")[..10];
            string relPath = Path.Combine("stickers", id + ext);
            string destPath = Path.Combine(_dataDirectory, relPath);
            File.WriteAllBytes(destPath, bytes);

            string title = Path.GetFileNameWithoutExtension(fileInfo.Name);
            if (string.IsNullOrWhiteSpace(title)) title = id;

            var sticker = new CustomSticker
            {
                Id = id,
                Name = title,
                Keywords = $"{title} {format.ToLowerInvariant()}",
                Format = format,
                GroupId = group.Id,
                FilePath = relPath,
                IsMounted = false,
                IsFavorite = false,
                FileSizeBytes = bytes.Length
            };
            doc.Stickers.Insert(0, sticker);
            added.Add(sticker);
        }

        return new ImportSummary(added.Count, skipped, added);
    }

    internal (StickerGroup? Group, ImportSummary Summary) ImportFolder(EmojiLibraryDocument doc, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return (null, new ImportSummary(0, 0, Array.Empty<CustomSticker>()));

        var dirInfo = new DirectoryInfo(folderPath);
        if (IsSymbolicLinkOrJunction(dirInfo))
            return (null, new ImportSummary(0, 1, Array.Empty<CustomSticker>()));

        string groupName = string.IsNullOrWhiteSpace(dirInfo.Name) ? "Imported" : dirInfo.Name;
        StickerGroup? existing = doc.Groups.FirstOrDefault(g =>
            !g.IsMounted && string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        StickerGroup group = existing ?? CreateManagedGroup(doc, groupName);

        string[] candidates = EnumerateSupportedImagePaths(dirInfo.FullName, MaxManagedStickers).ToArray();
        ImportSummary summary = ImportFiles(doc, candidates, group.Id);
        return (group, summary);
    }

    internal CustomSticker? ImportFromBytes(
        EmojiLibraryDocument doc,
        byte[] bytes,
        string name,
        string keywords,
        string targetGroupId,
        bool favorite = true)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > MaxStickerBytes)
            return null;
        if (doc.Stickers.Count(s => !s.IsMounted) >= MaxManagedStickers)
            return null;

        string? format = DetectImageFormat(bytes, null);
        if (format == null) return null;

        StickerGroup group = EnsureManagedGroup(doc, targetGroupId);
        Directory.CreateDirectory(_stickersDir);

        string id = "sc_" + Guid.NewGuid().ToString("N")[..10];
        string ext = FormatToExtension(format);
        string relPath = Path.Combine("stickers", id + ext);
        File.WriteAllBytes(Path.Combine(_dataDirectory, relPath), bytes);

        string cleanName = string.IsNullOrWhiteSpace(name) ? "Clipboard Sticker" : name.Trim();
        var sticker = new CustomSticker
        {
            Id = id,
            Name = cleanName,
            Keywords = string.IsNullOrWhiteSpace(keywords) ? cleanName : keywords.Trim(),
            Format = format,
            GroupId = group.Id,
            FilePath = relPath,
            IsMounted = false,
            IsFavorite = favorite,
            FileSizeBytes = bytes.Length
        };
        doc.Stickers.Insert(0, sticker);
        return sticker;
    }

    internal StickerGroup? MountFolder(EmojiLibraryDocument doc, string folderPath, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return null;

        var dirInfo = new DirectoryInfo(folderPath);
        if (IsSymbolicLinkOrJunction(dirInfo))
            return null;

        string normalized = Path.GetFullPath(dirInfo.FullName);
        StickerGroup? existing = doc.Groups.FirstOrDefault(g =>
            g.IsMounted && string.Equals(g.SourcePath, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                existing.Name = displayName.Trim();
            ScanMountedFolder(doc, existing);
            return existing;
        }

        if (doc.Groups.Count(g => g.IsMounted) >= MaxMountedFolders)
            return null;

        string name = string.IsNullOrWhiteSpace(displayName)
            ? (string.IsNullOrWhiteSpace(dirInfo.Name) ? "Mounted" : dirInfo.Name)
            : displayName.Trim();

        var group = new StickerGroup
        {
            Id = "mnt_" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            IsMounted = true,
            SourcePath = normalized
        };
        doc.Groups.Add(group);
        ScanMountedFolder(doc, group);
        return group;
    }

    internal int ScanMountedFolder(EmojiLibraryDocument doc, StickerGroup group)
    {
        if (!group.IsMounted || string.IsNullOrWhiteSpace(group.SourcePath) || !Directory.Exists(group.SourcePath))
        {
            doc.Stickers.RemoveAll(s => string.Equals(s.GroupId, group.Id, StringComparison.OrdinalIgnoreCase));
            return 0;
        }

        var prevByPath = doc.Stickers
            .Where(s => string.Equals(s.GroupId, group.Id, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.FilePath, s => s, StringComparer.OrdinalIgnoreCase);

        doc.Stickers.RemoveAll(s => string.Equals(s.GroupId, group.Id, StringComparison.OrdinalIgnoreCase));

        int added = 0;
        foreach (string path in EnumerateSupportedImagePaths(group.SourcePath, MaxStickersPerMount))
        {
            var fi = new FileInfo(path);
            if (IsSymbolicLinkOrJunction(fi) || fi.Length <= 0 || fi.Length > MaxStickerBytes)
                continue;

            string? format = DetectImageFormatFromFileHeader(fi.FullName, fi.Extension);
            if (format == null) continue;

            if (prevByPath.TryGetValue(fi.FullName, out CustomSticker? existing))
            {
                existing.Format = format;
                existing.FileSizeBytes = fi.Length;
                existing.IsMounted = true;
                doc.Stickers.Add(existing);
            }
            else
            {
                string title = Path.GetFileNameWithoutExtension(fi.Name);
                doc.Stickers.Add(new CustomSticker
                {
                    Id = "sm_" + Guid.NewGuid().ToString("N")[..10],
                    Name = title,
                    Keywords = $"{title} {group.Name} {format.ToLowerInvariant()}",
                    Format = format,
                    GroupId = group.Id,
                    FilePath = fi.FullName,
                    IsMounted = true,
                    IsFavorite = false,
                    FileSizeBytes = fi.Length
                });
            }
            added++;
        }
        return added;
    }

    internal void UnmountFolder(EmojiLibraryDocument doc, string groupId)
    {
        doc.Groups.RemoveAll(g => g.IsMounted && string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
        doc.Stickers.RemoveAll(s => s.IsMounted && string.Equals(s.GroupId, groupId, StringComparison.OrdinalIgnoreCase));
    }

    internal bool DeleteManagedGroup(EmojiLibraryDocument doc, string groupId)
    {
        List<StickerGroup> managed = doc.Groups.Where(g => !g.IsMounted).ToList();
        if (managed.Count <= 1) return false;

        StickerGroup? target = managed.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase));
        if (target == null) return false;

        StickerGroup fallback = managed.First(g => g.Id != target.Id);
        foreach (CustomSticker s in doc.Stickers)
        {
            if (string.Equals(s.GroupId, target.Id, StringComparison.OrdinalIgnoreCase))
                s.GroupId = fallback.Id;
        }
        doc.Groups.Remove(target);
        return true;
    }

    internal bool DeleteSticker(EmojiLibraryDocument doc, CustomSticker sticker)
    {
        bool removed = doc.Stickers.Remove(sticker);
        if (removed && !sticker.IsMounted && !string.IsNullOrWhiteSpace(sticker.FilePath))
        {
            string full = ResolveStickerPath(sticker);
            string stickersPrefix = Path.GetFullPath(_stickersDir) + Path.DirectorySeparatorChar;
            if (full.StartsWith(stickersPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            {
                try { File.Delete(full); } catch { }
            }
        }
        return removed;
    }

    internal static bool IsSymbolicLinkOrJunction(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            return false;
        try
        {
            return info.LinkTarget != null;
        }
        catch
        {
            return true;
        }
    }

    internal static string? DetectImageFormat(ReadOnlySpan<byte> bytes, string? extension)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "PNG";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "JPG";

        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
            bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
        {
            return "GIF";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return "WEBP";
        }

        _ = extension;
        return null;
    }

    private static string? DetectImageFormatFromFileHeader(string path, string? extension)
    {
        try
        {
            Span<byte> header = stackalloc byte[16];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int read = fs.Read(header);
            return DetectImageFormat(header[..read], extension);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatToExtension(string format) => format switch
    {
        "JPG" => ".jpg",
        "GIF" => ".gif",
        "WEBP" => ".webp",
        _ => ".png"
    };

    private static IEnumerable<string> EnumerateSupportedImagePaths(string directory, int limit)
    {
        int count = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file);
            if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            yield return file;
            if (++count >= limit) yield break;
        }
    }
}
