using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class KeyBinding
{
    public string KeyId { get; set; } = "";

    public string Label { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class KeyScheme
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int Rows { get; set; } = 4;

    public int Columns { get; set; } = 3;

    public List<KeyBinding?> Keys { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    public override string ToString() => Name ?? "";
}

internal sealed class KeySchemes
{
    internal const int DefaultRows = 4;
    internal const int DefaultColumns = 3;
    internal const int MaxRows = 10;
    internal const int MaxColumns = 16;
    internal const int MaxKeys = 160;
    internal const int MaxSchemeCount = 32;
    internal const int MaxSchemeNameLength = 64;
    internal const int MaxKeyLabelLength = 128;
    internal const int MaxJsonBytes = 1024 * 1024;

    internal const string ErrorEmptyJson = "EMPTY_JSON";
    internal const string ErrorJsonTooLarge = "JSON_TOO_LARGE";
    internal const string ErrorInvalidJson = "INVALID_JSON";
    internal const string ErrorSchemesNull = "SCHEMES_NULL";
    internal const string ErrorSchemeNull = "SCHEME_NULL";
    internal const string ErrorTooManySchemes = "TOO_MANY_SCHEMES";
    internal const string ErrorEmptySchemeId = "EMPTY_SCHEME_ID";
    internal const string ErrorDuplicateSchemeId = "DUPLICATE_SCHEME_ID";
    internal const string ErrorEmptySchemeName = "EMPTY_SCHEME_NAME";
    internal const string ErrorDuplicateSchemeName = "DUPLICATE_SCHEME_NAME";
    internal const string ErrorSchemeNameTooLong = "SCHEME_NAME_TOO_LONG";
    internal const string ErrorRows = "ROWS";
    internal const string ErrorColumns = "COLUMNS";
    internal const string ErrorTotalSlots = "TOTAL_SLOTS";
    internal const string ErrorKeyCount = "KEY_COUNT";
    internal const string ErrorEmptyKeyId = "EMPTY_KEY_ID";
    internal const string ErrorUnknownKeyId = "UNKNOWN_KEY_ID";
    internal const string ErrorKeyLabelTooLong = "KEY_LABEL_TOO_LONG";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        MaxDepth = 32,
    };

    private static readonly HashSet<string> AllowedKeyIds =
        KeyboardLayouts.Create(KeyboardMode.Full)
            .SelectMany(row => row)
            .Select(key => key.Id)
            .ToHashSet(StringComparer.Ordinal);

    public List<KeyScheme> Items { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);

    public static KeySchemes CreateDefault(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A scheme name is required.", nameof(name));
        }

        if (name.Length > MaxSchemeNameLength)
        {
            throw new ArgumentException(
                $"A scheme name cannot exceed {MaxSchemeNameLength} characters.",
                nameof(name));
        }

        return new KeySchemes
        {
            Items =
            [
                new KeyScheme
                {
                    Id = "scheme-" + Guid.NewGuid().ToString("N"),
                    Name = name,
                    Rows = DefaultRows,
                    Columns = DefaultColumns,
                    Keys = Enumerable.Repeat<KeyBinding?>(null, DefaultRows * DefaultColumns).ToList(),
                },
            ],
        };
    }

    public KeySchemes DeepClone()
    {
        var clone = new KeySchemes
        {
            ExtensionData = CloneExtensionData(ExtensionData),
        };
        if (Items == null)
        {
            return clone;
        }

        foreach (KeyScheme? scheme in Items)
        {
            if (scheme == null)
            {
                continue;
            }

            var schemeCopy = new KeyScheme
            {
                Id = scheme.Id ?? "",
                Name = scheme.Name ?? "",
                Rows = scheme.Rows,
                Columns = scheme.Columns,
                Keys = new List<KeyBinding?>(),
                ExtensionData = CloneExtensionData(scheme.ExtensionData),
            };

            if (scheme.Keys != null)
            {
                foreach (KeyBinding? binding in scheme.Keys)
                {
                    schemeCopy.Keys.Add(CloneBinding(binding));
                }
            }

            clone.Items.Add(schemeCopy);
        }

        return clone;
    }

    public bool TryValidate(out string? error)
    {
        if (Items == null)
        {
            error = ErrorSchemesNull;
            return false;
        }

        if (Items.Count > MaxSchemeCount)
        {
            error = ErrorTooManySchemes;
            return false;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyScheme? scheme in Items)
        {
            if (scheme == null)
            {
                error = ErrorSchemeNull;
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheme.Id))
            {
                error = ErrorEmptySchemeId;
                return false;
            }

            if (!ids.Add(scheme.Id))
            {
                error = ErrorDuplicateSchemeId;
                return false;
            }

            if (string.IsNullOrWhiteSpace(scheme.Name))
            {
                error = ErrorEmptySchemeName;
                return false;
            }

            if (!names.Add(scheme.Name.Trim()))
            {
                error = ErrorDuplicateSchemeName;
                return false;
            }

            if (scheme.Name.Length > MaxSchemeNameLength)
            {
                error = ErrorSchemeNameTooLong;
                return false;
            }

            if (scheme.Rows < 1 || scheme.Rows > MaxRows)
            {
                error = ErrorRows;
                return false;
            }

            if (scheme.Columns < 1 || scheme.Columns > MaxColumns)
            {
                error = ErrorColumns;
                return false;
            }

            int totalSlots = scheme.Rows * scheme.Columns;
            if (totalSlots > MaxKeys)
            {
                error = ErrorTotalSlots;
                return false;
            }

            if (scheme.Keys == null || scheme.Keys.Count != totalSlots)
            {
                error = ErrorKeyCount;
                return false;
            }

            foreach (KeyBinding? binding in scheme.Keys)
            {
                if (binding == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.KeyId))
                {
                    error = ErrorEmptyKeyId;
                    return false;
                }

                if (!AllowedKeyIds.Contains(binding.KeyId))
                {
                    error = ErrorUnknownKeyId;
                    return false;
                }

                if ((binding.Label ?? "").Length > MaxKeyLabelLength)
                {
                    error = ErrorKeyLabelTooLong;
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    public string Serialize()
    {
        if (!TryValidate(out string? error))
        {
            throw new InvalidOperationException(error);
        }

        string json = JsonSerializer.Serialize(this, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            throw new InvalidOperationException(ErrorJsonTooLarge);
        }

        return json;
    }

    public static bool TryDeserialize(
        string? json,
        out KeySchemes? schemes,
        out string? error)
    {
        schemes = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = ErrorEmptyJson;
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            error = ErrorJsonTooLarge;
            return false;
        }

        try
        {
            schemes = JsonSerializer.Deserialize<KeySchemes>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = ErrorInvalidJson;
            return false;
        }
        catch (NotSupportedException)
        {
            error = ErrorInvalidJson;
            return false;
        }

        if (schemes == null)
        {
            error = ErrorInvalidJson;
            return false;
        }

        schemes.NormalizeAfterDeserialize();
        if (!schemes.TryValidate(out error))
        {
            schemes = null;
            return false;
        }

        return true;
    }

    private static KeyBinding? CloneBinding(KeyBinding? binding)
    {
        if (binding == null)
        {
            return null;
        }

        return new KeyBinding
        {
            KeyId = binding.KeyId ?? "",
            Label = binding.Label ?? "",
            ExtensionData = CloneExtensionData(binding.ExtensionData),
        };
    }

    internal static Dictionary<string, JsonElement> CloneExtensionData(
        Dictionary<string, JsonElement>? source)
    {
        var clone = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (source == null)
        {
            return clone;
        }

        foreach (KeyValuePair<string, JsonElement> item in source)
        {
            clone[item.Key] = item.Value.Clone();
        }

        return clone;
    }

    private void NormalizeAfterDeserialize()
    {
        ExtensionData = CloneExtensionData(ExtensionData);
        if (Items == null)
        {
            return;
        }

        foreach (KeyScheme? scheme in Items)
        {
            if (scheme == null)
            {
                continue;
            }

            scheme.Id ??= "";
            scheme.Name ??= "";
            scheme.ExtensionData = CloneExtensionData(scheme.ExtensionData);
            if (scheme.Keys == null)
            {
                continue;
            }

            foreach (KeyBinding? binding in scheme.Keys)
            {
                if (binding == null)
                {
                    continue;
                }

                binding.KeyId ??= "";
                binding.Label ??= "";
                binding.ExtensionData = CloneExtensionData(binding.ExtensionData);
            }
        }
    }

    internal static bool TryDeserialize(
        JsonElement root,
        out KeySchemes? schemes,
        out string? error)
    {
        schemes = null;
        error = null;

        string json = root.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            error = ErrorJsonTooLarge;
            return false;
        }

        try
        {
            schemes = JsonSerializer.Deserialize<KeySchemes>(root, JsonOptions);
        }
        catch (JsonException)
        {
            error = ErrorInvalidJson;
            return false;
        }
        catch (NotSupportedException)
        {
            error = ErrorInvalidJson;
            return false;
        }

        if (schemes == null)
        {
            error = ErrorInvalidJson;
            return false;
        }

        schemes.NormalizeAfterDeserialize();
        if (!schemes.TryValidate(out error))
        {
            schemes = null;
            return false;
        }

        return true;
    }
}

internal sealed class KeyEditingState
{
    private const int MaxHistoryEntries = 32;
    private const long MaxHistoryBytes = 4L * 1024 * 1024;

    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    internal KeyEditingState(KeySchemes source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!TryValidateCandidate(source))
        {
            throw new ArgumentException("Keyboard key schemes are invalid.", nameof(source));
        }

        KeySchemes clone = source.DeepClone();
        if (!TryValidateCandidate(clone))
        {
            throw new ArgumentException("Keyboard key schemes are invalid.", nameof(source));
        }

        Schemes = clone;
        SelectedSchemeId = clone.Items.FirstOrDefault()?.Id ?? "";
        SelectedSlot = 0;
    }

    internal KeySchemes Schemes { get; private set; }

    internal KeyScheme? CurrentScheme => FindScheme(Schemes, SelectedSchemeId);

    internal string SelectedSchemeId { get; private set; }

    internal int SelectedSlot { get; private set; }

    internal long Revision { get; private set; }

    internal bool CanUndo => _undo.Count > 0;

    internal bool CanRedo => _redo.Count > 0;

    internal void SelectScheme(string id)
    {
        KeyScheme? scheme = FindScheme(Schemes, id);
        if (scheme == null)
        {
            return;
        }

        SelectedSchemeId = scheme.Id;
        SelectedSlot = Math.Min(SelectedSlot, scheme.Keys.Count - 1);
    }

    internal void SelectSlot(int index)
    {
        if (IsSlot(index))
        {
            SelectedSlot = index;
        }
    }

    internal bool RenameScheme(string name)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null || !TryNormalizeName(name, out string normalized))
        {
            return false;
        }

        if (string.Equals(current.Name, normalized, StringComparison.Ordinal) ||
            NameExists(Schemes, normalized, current.Id))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? target = FindScheme(candidate, current.Id);
        if (target == null)
        {
            return false;
        }

        target.Name = normalized;
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool AddScheme(string name)
    {
        if (!TryNormalizeName(name, out string normalized) ||
            NameExists(Schemes, normalized, null))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme addition = KeySchemes.CreateDefault(normalized).Items[0];
        candidate.Items.Add(addition);
        return TryCommit(candidate, addition.Id, 0);
    }

    internal bool DuplicateScheme(string name)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null ||
            !TryNormalizeName(name, out string normalized) ||
            NameExists(Schemes, normalized, null))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? source = FindScheme(candidate, current.Id);
        if (source == null)
        {
            return false;
        }

        KeyScheme duplicate = CloneScheme(source, NewSchemeId(candidate), normalized);
        candidate.Items.Add(duplicate);
        return TryCommit(candidate, duplicate.Id, 0);
    }

    internal bool RemoveScheme()
    {
        KeyScheme? current = CurrentScheme;
        if (current == null || Schemes.Items.Count <= 1)
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        int index = candidate.Items.FindIndex(
            item => string.Equals(item.Id, current.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        candidate.Items.RemoveAt(index);
        int nextIndex = Math.Min(index, candidate.Items.Count - 1);
        string nextId = candidate.Items[nextIndex].Id;
        return TryCommit(candidate, nextId, 0);
    }

    internal bool SetKey(int slot, KeyBinding? binding)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null || !IsSlot(slot) ||
            BindingsEqual(current.Keys[slot], binding))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? target = FindScheme(candidate, SelectedSchemeId);
        if (target == null)
        {
            return false;
        }

        target.Keys[slot] = CloneBinding(binding);
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool SetLabel(int slot, string label)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null || !IsSlot(slot))
        {
            return false;
        }

        KeyBinding? binding = current.Keys[slot];
        if (binding == null)
        {
            return false;
        }

        string normalized = label ?? "";
        if (normalized.Length > KeySchemes.MaxKeyLabelLength ||
            string.Equals(binding.Label ?? "", normalized, StringComparison.Ordinal))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? target = FindScheme(candidate, SelectedSchemeId);
        if (target?.Keys[slot] == null)
        {
            return false;
        }

        target.Keys[slot]!.Label = normalized;
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool SwapSlots(int source, int target)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null ||
            !IsSlot(source) ||
            !IsSlot(target) ||
            source == target ||
            BindingsEqual(current.Keys[source], current.Keys[target]))
        {
            return false;
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? scheme = FindScheme(candidate, SelectedSchemeId);
        if (scheme == null)
        {
            return false;
        }

        (scheme.Keys[source], scheme.Keys[target]) =
            (scheme.Keys[target], scheme.Keys[source]);
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool Resize(int rows, int columns)
    {
        KeyScheme? current = CurrentScheme;
        if (current == null ||
            rows < 1 || rows > KeySchemes.MaxRows ||
            columns < 1 || columns > KeySchemes.MaxColumns ||
            (rows == current.Rows && columns == current.Columns))
        {
            return false;
        }

        int totalSlots = rows * columns;
        if (totalSlots > KeySchemes.MaxKeys)
        {
            return false;
        }

        if (totalSlots < current.Keys.Count)
        {
            for (int index = totalSlots; index < current.Keys.Count; index++)
            {
                if (current.Keys[index] != null)
                {
                    return false;
                }
            }
        }

        KeySchemes candidate = Schemes.DeepClone();
        KeyScheme? target = FindScheme(candidate, SelectedSchemeId);
        if (target == null)
        {
            return false;
        }

        var keys = new List<KeyBinding?>(totalSlots);
        for (int index = 0; index < totalSlots; index++)
        {
            keys.Add(index < target.Keys.Count ? CloneBinding(target.Keys[index]) : null);
        }

        target.Rows = rows;
        target.Columns = columns;
        target.Keys = keys;
        int selectedSlot = Math.Min(SelectedSlot, totalSlots - 1);
        return TryCommit(candidate, SelectedSchemeId, selectedSlot);
    }

    internal bool Undo()
    {
        if (_undo.Count == 0 ||
            !TryCreateHistoryEntry(out HistoryEntry? current))
        {
            return false;
        }

        HistoryEntry target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current!);
        Restore(target);
        Revision++;
        EnsureHistoryLimits();
        return true;
    }

    internal bool Redo()
    {
        if (_redo.Count == 0 ||
            !TryCreateHistoryEntry(out HistoryEntry? current))
        {
            return false;
        }

        HistoryEntry target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current!);
        Restore(target);
        Revision++;
        EnsureHistoryLimits();
        return true;
    }

    internal KeySchemes Snapshot() => Schemes.DeepClone();

    private bool TryCommit(KeySchemes candidate, string selectedId, int selectedSlot)
    {
        if (!TryValidateCandidate(candidate) ||
            !TryCreateHistoryEntry(out HistoryEntry? previous))
        {
            return false;
        }

        _redo.Clear();
        _undo.Add(previous!);
        Schemes = candidate;
        SelectedSchemeId = FindScheme(candidate, selectedId)?.Id ??
            candidate.Items.FirstOrDefault()?.Id ?? "";
        KeyScheme? selected = FindScheme(candidate, SelectedSchemeId);
        SelectedSlot = selected == null || selected.Keys.Count == 0
            ? 0
            : Math.Min(Math.Max(selectedSlot, 0), selected.Keys.Count - 1);
        Revision++;
        EnsureHistoryLimits();
        return true;
    }

    private void Restore(HistoryEntry entry)
    {
        Schemes = entry.Schemes;
        SelectedSchemeId = FindScheme(Schemes, entry.SelectedSchemeId)?.Id ??
            Schemes.Items.FirstOrDefault()?.Id ?? "";
        KeyScheme? selected = FindScheme(Schemes, SelectedSchemeId);
        SelectedSlot = selected == null || selected.Keys.Count == 0
            ? 0
            : Math.Min(Math.Max(entry.SelectedSlot, 0), selected.Keys.Count - 1);
    }

    private bool TryCreateHistoryEntry(out HistoryEntry? entry)
    {
        entry = null;
        try
        {
            KeySchemes snapshot = Schemes.DeepClone();
            int jsonBytes = Encoding.UTF8.GetByteCount(snapshot.Serialize());
            string selectedSchemeId = SelectedSchemeId ?? "";
            int selectionBytes = Encoding.UTF8.GetByteCount(selectedSchemeId);
            entry = new HistoryEntry(
                snapshot,
                selectedSchemeId,
                SelectedSlot,
                jsonBytes + selectionBytes + 16L);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void EnsureHistoryLimits()
    {
        while (_undo.Count + _redo.Count > MaxHistoryEntries)
        {
            RemoveOldestHistoryEntry();
        }

        while (HistoryBytes > MaxHistoryBytes &&
               (_undo.Count > 0 || _redo.Count > 0))
        {
            RemoveOldestHistoryEntry();
        }
    }

    private long HistoryBytes =>
        _undo.Sum(entry => entry.EstimatedBytes) +
        _redo.Sum(entry => entry.EstimatedBytes);

    private void RemoveOldestHistoryEntry()
    {
        if (_undo.Count > 0)
        {
            _undo.RemoveAt(0);
        }
        else if (_redo.Count > 0)
        {
            _redo.RemoveAt(0);
        }
    }

    private static bool TryValidateCandidate(KeySchemes candidate)
    {
        if (!candidate.TryValidate(out _))
        {
            return false;
        }

        try
        {
            _ = candidate.Serialize();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = (name ?? "").Trim();
        return normalized.Length > 0 &&
            normalized.Length <= KeySchemes.MaxSchemeNameLength;
    }

    private static bool NameExists(
        KeySchemes schemes,
        string name,
        string? exceptSchemeId)
    {
        return schemes.Items.Any(scheme =>
            !string.Equals(scheme.Id, exceptSchemeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((scheme.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static KeyScheme? FindScheme(KeySchemes schemes, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return schemes.Items.FirstOrDefault(scheme =>
            string.Equals(scheme.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static KeyScheme CloneScheme(KeyScheme source, string id, string name)
    {
        return new KeyScheme
        {
            Id = id,
            Name = name,
            Rows = source.Rows,
            Columns = source.Columns,
            Keys = source.Keys.Select(CloneBinding).ToList(),
            ExtensionData = KeySchemes.CloneExtensionData(source.ExtensionData),
        };
    }

    private static KeyBinding? CloneBinding(KeyBinding? binding)
    {
        if (binding == null)
        {
            return null;
        }

        return new KeyBinding
        {
            KeyId = binding.KeyId ?? "",
            Label = binding.Label ?? "",
            ExtensionData = KeySchemes.CloneExtensionData(binding.ExtensionData),
        };
    }

    private static bool BindingsEqual(KeyBinding? left, KeyBinding? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left != null && right != null &&
            string.Equals(left.KeyId ?? "", right.KeyId ?? "", StringComparison.Ordinal) &&
            string.Equals(left.Label ?? "", right.Label ?? "", StringComparison.Ordinal) &&
            ExtensionDataEqual(left.ExtensionData, right.ExtensionData);
    }

    private static bool ExtensionDataEqual(
        Dictionary<string, JsonElement>? left,
        Dictionary<string, JsonElement>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        // Raw JSON comparison deliberately treats property order and whitespace
        // inside an unknown value as a change; dictionary key order is irrelevant.
        foreach (KeyValuePair<string, JsonElement> item in left)
        {
            bool found = false;
            foreach (KeyValuePair<string, JsonElement> candidate in right)
            {
                if (!string.Equals(item.Key, candidate.Key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(
                        item.Value.GetRawText(),
                        candidate.Value.GetRawText(),
                        StringComparison.Ordinal))
                {
                    return false;
                }

                found = true;
                break;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static string NewSchemeId(KeySchemes schemes)
    {
        string id;
        do
        {
            id = "scheme-" + Guid.NewGuid().ToString("N");
        }
        while (schemes.Items.Any(scheme =>
            string.Equals(scheme.Id, id, StringComparison.OrdinalIgnoreCase)));
        return id;
    }

    private bool IsSlot(int index)
    {
        KeyScheme? current = CurrentScheme;
        return current != null && index >= 0 && index < current.Keys.Count;
    }

    private sealed class HistoryEntry
    {
        internal HistoryEntry(
            KeySchemes schemes,
            string selectedSchemeId,
            int selectedSlot,
            long estimatedBytes)
        {
            Schemes = schemes;
            SelectedSchemeId = selectedSchemeId ?? "";
            SelectedSlot = selectedSlot;
            EstimatedBytes = estimatedBytes;
        }

        internal KeySchemes Schemes { get; }

        internal string SelectedSchemeId { get; }

        internal int SelectedSlot { get; }

        internal long EstimatedBytes { get; }
    }
}
