using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarPie.Plugin.EmojiPicker;

public enum EmojiPickerMode
{
    All = 0,
    Emoji = 1,
    Custom = 2,
    Kaomoji = 3
}

public enum EmojiItemKind
{
    Emoji = 0,
    Sticker = 1,
    Kaomoji = 2
}

public sealed record EmojiEntry(
    string Glyph,
    string NameZh,
    string NameEn,
    string Keywords,
    string CategoryId);

public sealed class StickerGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsMounted { get; set; }
    public string SourcePath { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    public StickerGroup Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsMounted = IsMounted,
        SourcePath = SourcePath,
        Extras = Extras == null ? null : new Dictionary<string, JsonElement>(Extras)
    };
}

public sealed class CustomSticker
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Format { get; set; } = "PNG";
    public string GroupId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsMounted { get; set; }
    public bool IsFavorite { get; set; }
    public long FileSizeBytes { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    public CustomSticker Clone() => new()
    {
        Id = Id,
        Name = Name,
        Keywords = Keywords,
        Format = Format,
        GroupId = GroupId,
        FilePath = FilePath,
        IsMounted = IsMounted,
        IsFavorite = IsFavorite,
        FileSizeBytes = FileSizeBytes,
        Extras = Extras == null ? null : new Dictionary<string, JsonElement>(Extras)
    };
}

public sealed class KaomojiEntry
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Name { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsFavorite { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    public KaomojiEntry Clone() => new()
    {
        Id = Id,
        Text = Text,
        Name = Name,
        Keywords = Keywords,
        Category = Category,
        IsFavorite = IsFavorite,
        Extras = Extras == null ? null : new Dictionary<string, JsonElement>(Extras)
    };
}

public sealed class EmojiPreferences
{
    public string DefaultMode { get; set; } = nameof(EmojiPickerMode.All);
    public bool AutoCloseOnPick { get; set; } = true;
    public bool GifHoverPlay { get; set; } = true;
    public bool TrackRecent { get; set; } = true;
    public bool EmojiClipboardFallback { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    public EmojiPreferences Clone() => new()
    {
        DefaultMode = DefaultMode,
        AutoCloseOnPick = AutoCloseOnPick,
        GifHoverPlay = GifHoverPlay,
        TrackRecent = TrackRecent,
        EmojiClipboardFallback = EmojiClipboardFallback,
        Extras = Extras == null ? null : new Dictionary<string, JsonElement>(Extras)
    };
}

public sealed class EmojiLibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> PinnedRecommended { get; set; } = new();
    public List<string> RecentEmojis { get; set; } = new();
    public List<string> FavoriteEmojis { get; set; } = new();
    public List<StickerGroup> Groups { get; set; } = new();
    public List<CustomSticker> Stickers { get; set; } = new();
    public List<KaomojiEntry> Kaomojis { get; set; } = new();
    public EmojiPreferences Preferences { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    [JsonIgnore]
    public bool RequiresRecovery { get; set; }

    [JsonIgnore]
    public string? RecoveryBackupPath { get; set; }

    public EmojiLibraryDocument Clone()
    {
        var copy = new EmojiLibraryDocument
        {
            SchemaVersion = SchemaVersion,
            PinnedRecommended = new List<string>(PinnedRecommended),
            RecentEmojis = new List<string>(RecentEmojis),
            FavoriteEmojis = new List<string>(FavoriteEmojis),
            Groups = Groups.Select(g => g.Clone()).ToList(),
            Stickers = Stickers.Select(s => s.Clone()).ToList(),
            Kaomojis = Kaomojis.Select(k => k.Clone()).ToList(),
            Preferences = Preferences?.Clone() ?? new EmojiPreferences(),
            Extras = Extras == null ? null : new Dictionary<string, JsonElement>(Extras),
            RequiresRecovery = RequiresRecovery,
            RecoveryBackupPath = RecoveryBackupPath
        };
        return copy;
    }
}
