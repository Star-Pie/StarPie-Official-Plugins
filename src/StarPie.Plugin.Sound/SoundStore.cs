using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarPie.Plugin;

namespace StarPie.Plugin.Sound;

internal sealed class SoundDocument
{
    public int SchemaVersion { get; set; } = 1;
    public bool LegacyImported { get; set; }
    public Dictionary<string, JsonElement>? LegacyFields { get; set; }
    public SoundPreferences Preferences { get; set; } = new();
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extras { get; set; }
}

internal sealed class SoundStore
{
    internal const int MaxDocumentBytes = 8 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new(SoundJsonContext.Default.Options)
    {
        TypeInfoResolver = SoundJsonContext.Default
    };
    internal static readonly (string Key, string Property, bool Default)[] Switches =
    {
        ("enabled", "EnableSoundEffects", true), ("onPopup", "SoundOnPopup", true),
        ("onHover", "SoundOnHover", true), ("onExpand", "SoundOnExpand", true),
        ("onExecute", "SoundOnExecute", true), ("onCancel", "SoundOnCancel", true)
    };
    internal static IEnumerable<string> SettingKeys => Switches.Select(s => s.Key).Concat(new[] { "volume", "theme", "profileId" });
    private readonly object _gate = new();
    private readonly IPluginSettings _settings;
    private readonly string _path;
    private readonly string _legacyPath;
    private SoundDocument _document;

    internal SoundStore(IPluginSettings settings, string dataDirectory, string? legacyPath = null)
    {
        _settings = settings;
        _path = Path.Combine(dataDirectory, "sound.json");
        string local = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _legacyPath = Path.GetFullPath(legacyPath ?? Path.Combine(local, "StarPie", "config.json"));
        if (File.Exists(_path))
        {
            _document = JsonSerializer.Deserialize<SoundDocument>(ReadBounded(_path), JsonOptions)
                ?? throw new InvalidDataException("Empty sound document.");
            if (_document.SchemaVersion != 1) throw new InvalidDataException("Unsupported sound schema.");
            if (_document.Preferences == null) throw new InvalidDataException("Missing sound preferences.");
            Normalize(_document.Preferences, strict: false);
        }
        else
        {
            _document = new();
            if (File.Exists(_legacyPath))
            {
                using var source = JsonDocument.Parse(ReadBounded(_legacyPath));
                if (source.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Legacy configuration must be an object.");
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var names = typeof(SoundPreferences).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var item in source.RootElement.EnumerateObject())
                    if (names.Contains(item.Name)) fields[item.Name] = item.Value.Clone();
                if (fields.Count != 0)
                {
                    _document.Preferences = JsonSerializer.Deserialize<SoundPreferences>(JsonSerializer.Serialize(fields, JsonOptions), JsonOptions) ?? new();
                    Normalize(_document.Preferences, strict: false);
                    _document.LegacyFields = fields; _document.LegacyImported = true;
                    WriteAtomic(_path, JsonSerializer.Serialize(_document, JsonOptions), overwrite: false);
                }
            }
        }
    }

    internal SoundPreferences ReadPreferences()
    {
        lock (_gate)
        {
            var result = Clone(_document.Preferences);
            result.EnableSoundEffects = _settings.GetBool("enabled", result.EnableSoundEffects);
            result.SoundOnPopup = _settings.GetBool("onPopup", result.SoundOnPopup);
            result.SoundOnHover = _settings.GetBool("onHover", result.SoundOnHover);
            result.SoundOnExpand = _settings.GetBool("onExpand", result.SoundOnExpand);
            result.SoundOnExecute = _settings.GetBool("onExecute", result.SoundOnExecute);
            result.SoundOnCancel = _settings.GetBool("onCancel", result.SoundOnCancel);
            result.SoundVolume = _settings.GetDouble("volume", result.SoundVolume);
            result.SoundTheme = _settings.Get("theme") ?? result.SoundTheme;
            result.ActiveCustomSoundProfileId = _settings.Get("profileId") ?? result.ActiveCustomSoundProfileId;
            Normalize(result, strict: false);
            return result;
        }
    }

    internal static SoundPreferences Clone(SoundPreferences value) =>
        JsonSerializer.Deserialize<SoundPreferences>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    internal static void Normalize(SoundPreferences preferences, bool strict)
    {
        preferences.SoundVolume = double.IsFinite(preferences.SoundVolume) ? Math.Clamp(preferences.SoundVolume, 0, 1) : 0.6;
        string[] themes = { "Mechanical", "Crisp", "Bubble", "Minimalist", "Custom" };
        preferences.SoundTheme = themes.FirstOrDefault(t => t.Equals(preferences.SoundTheme, StringComparison.OrdinalIgnoreCase)) ?? "Mechanical";
        if (preferences.CustomSoundProfiles == null || preferences.CustomSoundProfiles.Count == 0)
            preferences.CustomSoundProfiles = CustomSoundProfile.CreateDefaultDemoProfiles();
        foreach (var profile in preferences.CustomSoundProfiles)
        {
            if (profile == null || profile.Events == null || (strict && profile.Events.Count == 0)) throw new InvalidDataException("Invalid sound profile.");
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            var seen = new HashSet<SoundType>();
            foreach (var entry in profile.Events)
            {
                if (entry == null || !Enum.IsDefined(entry.EventType) || !Enum.IsDefined(entry.SourceType) || !seen.Add(entry.EventType))
                    throw new InvalidDataException("Invalid or duplicate event in sound profile.");
                if (entry.SourceType == SoundSourceType.BuiltInPreset && string.Equals(entry.BuiltInTheme, "Custom", StringComparison.OrdinalIgnoreCase))
                {
                    if (strict) throw new InvalidDataException("Built-in preset cannot reference Custom.");
                    entry.BuiltInTheme = "Mechanical";
                }
                entry.PitchSemitones = Math.Clamp(entry.PitchSemitones, -24, 24);
                entry.DurationMs = Math.Clamp(entry.DurationMs, 5, 300);
                entry.RelativeVolume = double.IsFinite(entry.RelativeVolume) ? Math.Clamp(entry.RelativeVolume, 0, 1) : 0.8;
            }
        }
        string selected = preferences.ActiveCustomSoundProfileId ?? "";
        var active = preferences.CustomSoundProfiles.FirstOrDefault(p => p.Id.Equals(selected, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
        preferences.ActiveCustomSoundProfileId = (active ?? preferences.CustomSoundProfiles[0]).Id;
    }

    internal string Import(string filePath, bool activate, bool replace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(filePath);
        using var json = JsonDocument.Parse(ReadBounded(full));
        List<CustomSoundProfile> incoming;
        if (json.RootElement.ValueKind == JsonValueKind.Array)
            incoming = JsonSerializer.Deserialize<List<CustomSoundProfile>>(json.RootElement.GetRawText(), JsonOptions)!;
        else if (HasProperty(json.RootElement, "Preferences"))
            incoming = JsonSerializer.Deserialize<SoundDocument>(json.RootElement.GetRawText(), JsonOptions)?.Preferences.CustomSoundProfiles!;
        else if (HasProperty(json.RootElement, "CustomSoundProfiles"))
            incoming = JsonSerializer.Deserialize<SoundPreferences>(json.RootElement.GetRawText(), JsonOptions)?.CustomSoundProfiles!;
        else incoming = new() { JsonSerializer.Deserialize<CustomSoundProfile>(json.RootElement.GetRawText(), JsonOptions)! };
        if (incoming == null || incoming.Count == 0) throw new InvalidDataException("No profiles in file.");
        var checkedInput = new SoundPreferences { CustomSoundProfiles = incoming };
        Normalize(checkedInput, strict: true);
        var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in incoming)
            if (!incomingIds.Add(profile.Id)) throw new InvalidDataException("Duplicate profile IDs in import batch.");
        foreach (var profile in incoming)
            foreach (var entry in profile.Events)
                if (entry.SourceType == SoundSourceType.CustomFile && !string.IsNullOrWhiteSpace(entry.CustomFilePath) && !Path.IsPathRooted(entry.CustomFilePath))
                    entry.CustomFilePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, entry.CustomFilePath));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = JsonSerializer.Deserialize<SoundDocument>(JsonSerializer.Serialize(_document, JsonOptions), JsonOptions)!;
            foreach (var profile in incoming)
            {
                int existing = next.Preferences.CustomSoundProfiles.FindIndex(p => p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0 && !replace) { profile.Id = Guid.NewGuid().ToString("N"); existing = -1; }
                if (existing >= 0) next.Preferences.CustomSoundProfiles[existing] = profile;
                else next.Preferences.CustomSoundProfiles.Add(profile);
            }
            if (activate) next.Preferences.ActiveCustomSoundProfileId = incoming[0].Id;
            Normalize(next.Preferences, strict: true);
            WriteAtomic(_path, JsonSerializer.Serialize(next, JsonOptions), overwrite: true, cancellationToken);
            _document = next;
            if (activate) { _settings.Set("profileId", incoming[0].Id); _settings.Save(); }
            return incoming[0].Id;
        }
    }

    internal void Export(string filePath, string? profileId, bool bundle, bool overwrite, CancellationToken cancellationToken)
    {
        string full = Path.GetFullPath(filePath);
        if (full.Equals(_legacyPath, StringComparison.OrdinalIgnoreCase) || full.Equals(Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Export cannot replace host or plugin configuration.");
        var preferences = ReadPreferences();
        object payload = bundle ? preferences : preferences.CustomSoundProfiles.FirstOrDefault(p =>
            p.Id.Equals(string.IsNullOrWhiteSpace(profileId) ? preferences.ActiveCustomSoundProfileId : profileId, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, profileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Unknown profile.");
        cancellationToken.ThrowIfCancellationRequested();
        WriteAtomic(full, JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions), overwrite, cancellationToken);
    }

    private static bool HasProperty(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static string ReadBounded(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxDocumentBytes) throw new InvalidDataException("Sound metadata exceeds 8 MiB.");
        return File.ReadAllText(path);
    }
    internal static void WriteAtomic(string path, string json, bool overwrite, CancellationToken cancellationToken = default)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes) throw new InvalidDataException("Sound document exceeds 8 MiB.");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false));
            // 原子移动是提交点：取消发生于此之前不得更新文件/内存/活动方案。
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
