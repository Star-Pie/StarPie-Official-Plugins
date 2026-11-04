using System.Text;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class KeyboardEditingState
{
    private const int MaxHistoryEntries = 32;
    private const long MaxHistoryBytes = 4L * 1024 * 1024;

    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    internal KeyboardEditingState(KeyboardSchemes source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!TryValidateCandidate(source))
        {
            throw new ArgumentException("Keyboard schemes are invalid.", nameof(source));
        }

        KeyboardSchemes clone = source.DeepClone();
        if (!TryValidateCandidate(clone))
        {
            throw new ArgumentException("Keyboard schemes are invalid.", nameof(source));
        }

        Schemes = clone;
        SelectedSchemeId = clone.Items.FirstOrDefault()?.Id ?? "";
        SelectedSlot = 0;
    }

    internal KeyboardSchemes Schemes { get; private set; }

    internal CustomKeyboardScheme? CurrentScheme =>
        FindScheme(Schemes, SelectedSchemeId);

    internal string SelectedSchemeId { get; private set; }

    internal int SelectedSlot { get; private set; }

    internal long Revision { get; private set; }

    internal bool CanUndo => _undo.Count > 0;

    internal bool CanRedo => _redo.Count > 0;

    internal void SelectScheme(string id)
    {
        CustomKeyboardScheme? scheme = FindScheme(Schemes, id);
        if (scheme != null)
        {
            SelectedSchemeId = scheme.Id;
        }
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
        CustomKeyboardScheme? current = CurrentScheme;
        if (current == null || !TryNormalizeName(name, out string normalized))
        {
            return false;
        }

        if (string.Equals(current.Name, normalized, StringComparison.Ordinal) ||
            NameExists(Schemes, normalized, current.Id))
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme? target = FindScheme(candidate, current.Id);
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

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme addition = KeyboardSchemes.CreateDefault(normalized).Items[0];
        candidate.Items.Add(addition);
        return TryCommit(candidate, addition.Id, 0);
    }

    internal bool DuplicateScheme(string name)
    {
        CustomKeyboardScheme? current = CurrentScheme;
        if (current == null ||
            !TryNormalizeName(name, out string normalized) ||
            NameExists(Schemes, normalized, null))
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme? source = FindScheme(candidate, current.Id);
        if (source == null)
        {
            return false;
        }

        CustomKeyboardScheme duplicate = CloneScheme(source, NewSchemeId(candidate), normalized);
        candidate.Items.Add(duplicate);
        return TryCommit(candidate, duplicate.Id, 0);
    }

    internal bool RemoveScheme()
    {
        CustomKeyboardScheme? current = CurrentScheme;
        if (current == null || Schemes.Items.Count <= 1)
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
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

    internal bool SetKey(int slot, CustomKeyBinding? binding)
    {
        if (!IsSlot(slot) || CurrentScheme == null)
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme? target = FindScheme(candidate, SelectedSchemeId);
        if (target == null ||
            BindingsEqual(target.Keys[slot], binding))
        {
            return false;
        }

        target.Keys[slot] = CloneBinding(binding);
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool SetLabel(int slot, string label)
    {
        if (!IsSlot(slot) || CurrentScheme == null)
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme? target = FindScheme(candidate, SelectedSchemeId);
        CustomKeyBinding? binding = target?.Keys[slot];
        if (binding == null)
        {
            return false;
        }

        string normalized = label ?? "";
        if (string.Equals(binding.Label ?? "", normalized, StringComparison.Ordinal))
        {
            return false;
        }

        binding.Label = normalized;
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
    }

    internal bool SwapSlots(int source, int target)
    {
        if (!IsSlot(source) ||
            !IsSlot(target) ||
            source == target ||
            CurrentScheme == null)
        {
            return false;
        }

        KeyboardSchemes candidate = Schemes.DeepClone();
        CustomKeyboardScheme? scheme = FindScheme(candidate, SelectedSchemeId);
        if (scheme == null ||
            BindingsEqual(scheme.Keys[source], scheme.Keys[target]))
        {
            return false;
        }

        (scheme.Keys[source], scheme.Keys[target]) =
            (scheme.Keys[target], scheme.Keys[source]);
        return TryCommit(candidate, SelectedSchemeId, SelectedSlot);
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

    internal KeyboardSchemes Snapshot() => Schemes.DeepClone();

    private bool TryCommit(KeyboardSchemes candidate, string selectedId, int selectedSlot)
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
        SelectedSlot = IsSlot(selectedSlot) ? selectedSlot : 0;
        Revision++;
        EnsureHistoryLimits();
        return true;
    }

    private void Restore(HistoryEntry entry)
    {
        Schemes = entry.Schemes;
        SelectedSchemeId = FindScheme(Schemes, entry.SelectedSchemeId)?.Id ??
            Schemes.Items.FirstOrDefault()?.Id ?? "";
        SelectedSlot = IsSlot(entry.SelectedSlot) ? entry.SelectedSlot : 0;
    }

    private bool TryCreateHistoryEntry(out HistoryEntry? entry)
    {
        entry = null;
        try
        {
            KeyboardSchemes snapshot = Schemes.DeepClone();
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

    private static bool TryValidateCandidate(KeyboardSchemes candidate)
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
            normalized.Length <= KeyboardSchemes.MaxSchemeNameLength;
    }

    private static bool NameExists(
        KeyboardSchemes schemes,
        string name,
        string? exceptSchemeId)
    {
        return schemes.Items.Any(scheme =>
            !string.Equals(scheme.Id, exceptSchemeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((scheme.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static CustomKeyboardScheme? FindScheme(KeyboardSchemes schemes, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return schemes.Items.FirstOrDefault(scheme =>
            string.Equals(scheme.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static CustomKeyboardScheme CloneScheme(
        CustomKeyboardScheme source,
        string id,
        string name)
    {
        return new CustomKeyboardScheme
        {
            Id = id,
            Name = name,
            Keys = source.Keys.Select(CloneBinding).ToList(),
        };
    }

    private static CustomKeyBinding? CloneBinding(CustomKeyBinding? binding)
    {
        if (binding == null)
        {
            return null;
        }

        return new CustomKeyBinding
        {
            Label = binding.Label ?? "",
            ActionId = binding.ActionId ?? "",
            Parameters = binding.Parameters == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(binding.Parameters, StringComparer.Ordinal),
        };
    }

    private static bool BindingsEqual(
        CustomKeyBinding? left,
        CustomKeyBinding? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null ||
            !string.Equals(left.Label ?? "", right.Label ?? "", StringComparison.Ordinal) ||
            !string.Equals(left.ActionId ?? "", right.ActionId ?? "", StringComparison.Ordinal))
        {
            return false;
        }

        Dictionary<string, string> leftParameters =
            left.Parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string> rightParameters =
            right.Parameters ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return leftParameters.Count == rightParameters.Count &&
            leftParameters.All(pair =>
                rightParameters.Any(other =>
                    string.Equals(pair.Key, other.Key, StringComparison.Ordinal) &&
                    string.Equals(pair.Value, other.Value, StringComparison.Ordinal)));
    }

    private static string NewSchemeId(KeyboardSchemes schemes)
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

    private static bool IsSlot(int index) =>
        index >= 0 && index < KeyboardSchemes.SlotCount;

    private sealed class HistoryEntry
    {
        internal HistoryEntry(
            KeyboardSchemes schemes,
            string selectedSchemeId,
            int selectedSlot,
            long estimatedBytes)
        {
            Schemes = schemes;
            SelectedSchemeId = selectedSchemeId ?? "";
            SelectedSlot = selectedSlot;
            EstimatedBytes = estimatedBytes;
        }

        internal KeyboardSchemes Schemes { get; }

        internal string SelectedSchemeId { get; }

        internal int SelectedSlot { get; }

        internal long EstimatedBytes { get; }
    }
}
