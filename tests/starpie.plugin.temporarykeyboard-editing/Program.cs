using System.Text;
using StarPie.Plugin.TemporaryKeyboard;

internal static class Program
{
    private static int s_passed;
    private static int s_failed;

    private static int Main()
    {
        Run("constructor clones and validates source", ConstructorClonesAndValidatesSource);
        Run("selection is revision-neutral", SelectionIsRevisionNeutral);
        Run("rename validates names and duplicates", RenameValidatesNamesAndDuplicates);
        Run("add works from empty and selects new scheme", AddWorksFromEmpty);
        Run("duplicate deep-clones the selected scheme", DuplicateDeepClonesSelectedScheme);
        Run("remove selects a neighbor and refuses the last scheme", RemoveSelectsNeighbor);
        Run("set key clones parameters and ignores exact no-op", SetKeyClonesParameters);
        Run("set key compares dictionary keys ordinally", SetKeyComparesDictionaryKeysOrdinally);
        Run("set label edits only a bound slot", SetLabelEditsBoundSlot);
        Run("swap slots is transactional and selection-neutral", SwapSlotsIsTransactional);
        Run("undo redo revisions and redo clearing", UndoRedoRevisionsAndRedoClearing);
        Run("undo redo restore selection and unknown bindings", UndoRedoRestoresSelectionAndUnknownBindings);
        Run("invalid operations do not change revision or history", InvalidOperationsAreNoOps);
        Run("scheme count limit is enforced", SchemeCountLimitIsEnforced);
        Run("parameter limit accepts 8192 and rejects 8193", ParameterLimitIsEnforced);
        Run("serialization limit rejects an oversized mutation", SerializationLimitIsEnforced);
        Run("history count is capped at 32", HistoryCountIsCapped);
        Run("history bytes are capped at four MiB", HistoryBytesAreCapped);
        Run("snapshot is a deep copy", SnapshotIsDeepCopy);
        Run("serialized snapshot preserves the model", SerializedSnapshotPreservesModel);

        Console.WriteLine($"RESULT: {(s_failed == 0 ? "PASS" : "FAIL")} ({s_passed} passed, {s_failed} failed)");
        return s_failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            s_passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            s_failed++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void ConstructorClonesAndValidatesSource()
    {
        KeyboardSchemes source = KeyboardSchemes.CreateDefault("Source");
        source.Items[0].Keys[0] = Binding("Base", "unknown.action", "Path", "original");

        var state = new KeyboardEditingState(source);
        Equal("Source", state.CurrentScheme!.Name, "Constructor must select the first scheme.");
        Equal(0L, state.Revision, "Constructor must not create a revision.");
        False(state.CanUndo, "Constructor must not create undo history.");
        False(state.CanRedo, "Constructor must not create redo history.");

        source.Items[0].Name = "Changed source";
        source.Items[0].Keys[0]!.Parameters["Path"] = "changed source";
        Equal("Source", state.CurrentScheme.Name, "Constructor must clone scheme names.");
        Equal("original", state.CurrentScheme.Keys[0]!.Parameters["Path"],
            "Constructor must clone binding parameters.");

        KeyboardSchemes invalid = KeyboardSchemes.CreateDefault("Invalid");
        invalid.Items[0].Keys.RemoveAt(0);
        Throws<ArgumentException>(
            () => _ = new KeyboardEditingState(invalid),
            "Invalid source schemes must be rejected.");
        var nullScheme = new KeyboardSchemes();
        nullScheme.Items.Add(null!);
        Throws<ArgumentException>(
            () => _ = new KeyboardEditingState(nullScheme),
            "A source containing a null scheme must be rejected before cloning.");
        Throws<ArgumentNullException>(
            () => _ = new KeyboardEditingState(null!),
            "A null source must be rejected.");
    }

    private static void SelectionIsRevisionNeutral()
    {
        KeyboardSchemes source = KeyboardSchemes.CreateDefault("First");
        source.Items.Add(NewScheme("second", "Second"));
        var state = new KeyboardEditingState(source);

        state.SelectSlot(11);
        Equal(11, state.SelectedSlot, "A valid slot must be selected.");
        state.SelectSlot(12);
        state.SelectSlot(-1);
        Equal(11, state.SelectedSlot, "Invalid slots must leave the selection unchanged.");

        string firstId = state.SelectedSchemeId;
        state.SelectScheme(firstId.ToUpperInvariant());
        Equal(firstId, state.SelectedSchemeId, "Scheme IDs must be matched case-insensitively.");
        state.SelectScheme("missing");
        Equal(firstId, state.SelectedSchemeId, "An unknown scheme ID must be ignored.");
        state.SelectScheme(null!);
        Equal(firstId, state.SelectedSchemeId, "A null scheme ID must be ignored.");

        Equal(0L, state.Revision, "Selection changes must not create a revision.");
        False(state.CanUndo, "Selection changes must not create history.");
        False(state.CanRedo, "Selection changes must not create redo history.");
    }

    private static void RenameValidatesNamesAndDuplicates()
    {
        var state = new KeyboardEditingState(KeyboardSchemes.CreateDefault("Alpha"));

        False(state.RenameScheme("Alpha"), "Renaming to the same name must be a no-op.");
        False(state.RenameScheme(" Alpha "), "Whitespace normalization must make this a no-op.");
        False(state.RenameScheme(""), "An empty name must be rejected.");
        False(state.RenameScheme(new string('x', KeyboardSchemes.MaxSchemeNameLength + 1)),
            "An overlong name must be rejected.");

        True(state.RenameScheme(" Beta "), "A valid rename must succeed.");
        Equal("Beta", state.CurrentScheme!.Name, "Names must be trimmed.");
        Equal(1L, state.Revision, "A rename must create one revision.");

        False(state.AddScheme("beta"), "Duplicate names must be rejected case-insensitively.");
        False(state.DuplicateScheme("BETA"), "Duplicate names must be rejected for duplication.");
        Equal(1L, state.Revision, "Rejected names must not create revisions.");
    }

    private static void AddWorksFromEmpty()
    {
        var state = new KeyboardEditingState(new KeyboardSchemes());
        Assert(state.CurrentScheme == null, "An empty source must have no current scheme.");

        False(state.RenameScheme("name"), "Renaming without a scheme must fail.");
        False(state.DuplicateScheme("name"), "Duplicating without a scheme must fail.");
        False(state.RemoveScheme(), "Removing without a scheme must fail.");
        False(state.SetKey(0, Binding("key", "action")), "Setting a key without a scheme must fail.");
        False(state.SetLabel(0, "label"), "Setting a label without a scheme must fail.");
        False(state.SwapSlots(0, 1), "Swapping without a scheme must fail.");
        Equal(0L, state.Revision, "Rejected empty-state operations must not create revisions.");

        True(state.AddScheme(" First "), "Adding the first scheme must succeed.");
        Equal(1, state.Schemes.Items.Count, "The first scheme must be added.");
        Equal("First", state.CurrentScheme!.Name, "The new scheme must be selected.");
        Equal(0, state.SelectedSlot, "Adding a scheme must select slot zero.");
        Equal(1L, state.Revision, "Adding a scheme must create one revision.");
    }

    private static void DuplicateDeepClonesSelectedScheme()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("Source", Binding("Base", "unknown.action", "Path", "original")));
        string sourceId = state.SelectedSchemeId;

        True(state.DuplicateScheme("Copy"), "Duplicating a scheme must succeed.");
        string copyId = state.SelectedSchemeId;
        Assert(!string.Equals(sourceId, copyId, StringComparison.Ordinal),
            "The duplicate must receive a new scheme ID.");
        Equal("Copy", state.CurrentScheme!.Name, "The duplicate must be selected.");
        Equal("unknown.action", state.CurrentScheme.Keys[0]!.ActionId,
            "The duplicate must preserve unknown action IDs.");

        True(state.SetKey(0, Binding("Changed", "changed.action", "Path", "changed")),
            "The duplicate binding must be editable.");
        CustomKeyboardScheme original = state.Schemes.Items.Single(item => item.Id == sourceId);
        CustomKeyboardScheme duplicate = state.Schemes.Items.Single(item => item.Id == copyId);
        Equal("original", original.Keys[0]!.Parameters["Path"],
            "Editing the duplicate must not mutate the source scheme.");
        Equal("changed", duplicate.Keys[0]!.Parameters["Path"],
            "The duplicate must retain its edit.");
    }

    private static void RemoveSelectsNeighbor()
    {
        KeyboardSchemes source = KeyboardSchemes.CreateDefault("First");
        source.Items.Add(NewScheme("second", "Second"));
        var state = new KeyboardEditingState(source);

        True(state.RemoveScheme(), "Removing the first scheme must succeed.");
        Equal(1, state.Schemes.Items.Count, "One scheme must remain.");
        Equal("Second", state.CurrentScheme!.Name, "The next scheme must be selected.");
        Equal(0, state.SelectedSlot, "Removal must select slot zero.");
        False(state.RemoveScheme(), "The last scheme must not be removable.");

        True(state.AddScheme("Third"), "A second scheme must be addable after removal.");
        True(state.RemoveScheme(), "Removing the newly selected last scheme must succeed.");
        Equal("Second", state.CurrentScheme!.Name,
            "Removing the last item must select the previous neighbor.");
    }

    private static void SetKeyClonesParameters()
    {
        var state = new KeyboardEditingState(KeyboardSchemes.CreateDefault("Keys"));
        var input = Binding("Input", "unknown.action", "Path", "original");

        True(state.SetKey(0, input), "Setting a binding must succeed.");
        input.Parameters["Path"] = "changed input";
        Equal("original", state.CurrentScheme!.Keys[0]!.Parameters["Path"],
            "SetKey must copy the binding parameter dictionary.");
        Equal(1L, state.Revision, "Setting a binding must create one revision.");

        False(state.SetKey(0, Binding("Input", "unknown.action", "Path", "original")),
            "An exact binding replacement must be a no-op.");
        True(state.SetKey(0, null), "Clearing a bound slot must succeed.");
        False(state.SetKey(0, null), "Clearing an already empty slot must be a no-op.");
        Equal(2L, state.Revision, "Clearing a binding must create one revision.");
        Assert(state.CurrentScheme.Keys[0] == null, "Clearing a binding must store null.");
    }

    private static void SetKeyComparesDictionaryKeysOrdinally()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("Keys", Binding("Input", "unknown.action", "Path", "value")));
        var differentlyCased = new CustomKeyBinding
        {
            Label = "Input",
            ActionId = "unknown.action",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = "value",
            },
        };

        True(state.SetKey(0, differentlyCased),
            "Dictionary keys must be compared by ordinal spelling, not the supplied comparer.");
        Assert(state.CurrentScheme!.Keys[0]!.Parameters.ContainsKey("path"),
            "The replacement binding must preserve the supplied key spelling.");
        False(state.SetKey(0, Binding("Input", "unknown.action", "path", "value")),
            "The same ordinal binding must then be a no-op.");
    }

    private static void SetLabelEditsBoundSlot()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("Labels", Binding("Base", "action", "Path", "value")));

        True(state.SetLabel(0, "Changed"), "A bound slot label must be editable.");
        Equal("Changed", state.CurrentScheme!.Keys[0]!.Label, "The label must be stored.");
        False(state.SetLabel(0, "Changed"), "An identical label must be a no-op.");
        True(state.SetLabel(0, null!), "A null label must normalize to an empty label.");
        Equal("", state.CurrentScheme.Keys[0]!.Label, "A null label must become empty.");
        False(state.SetLabel(1, "missing"), "An empty slot must not receive a label.");
        False(state.SetLabel(-1, "missing"), "An invalid slot must not receive a label.");
        Equal(2L, state.Revision, "Only successful label changes must create revisions.");
    }

    private static void SwapSlotsIsTransactional()
    {
        var state = new KeyboardEditingState(KeyboardSchemes.CreateDefault("Swap"));
        True(state.SetKey(0, Binding("Zero", "action.zero", "p", "0")),
            "The first slot must be bound.");
        True(state.SetKey(1, Binding("One", "action.one", "p", "1")),
            "The second slot must be bound.");
        state.SelectSlot(7);
        long revision = state.Revision;

        False(state.SwapSlots(0, 0), "Swapping a slot with itself must be a no-op.");
        False(state.SwapSlots(-1, 1), "Invalid source slots must be rejected.");
        False(state.SwapSlots(0, 12), "Invalid target slots must be rejected.");
        Equal(revision, state.Revision, "Rejected swaps must not create revisions.");

        True(state.SwapSlots(0, 1), "Swapping two distinct slots must succeed.");
        Equal("One", state.CurrentScheme!.Keys[0]!.Label, "The first slot must receive the second binding.");
        Equal("Zero", state.CurrentScheme.Keys[1]!.Label, "The second slot must receive the first binding.");
        Equal(7, state.SelectedSlot, "Swapping must not change the selected slot.");
        Equal(revision + 1, state.Revision, "A successful swap must create one revision.");

        True(state.SetKey(1, Binding("One", "action.one", "p", "1")),
            "The second slot must be replaced with an equal binding.");
        False(state.SwapSlots(0, 1), "Swapping equal bindings must be a no-op.");
        Equal(revision + 2, state.Revision, "The equal swap must not create a revision.");
    }

    private static void UndoRedoRevisionsAndRedoClearing()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("History", Binding("Base", "action", "p", "1")));
        state.SelectSlot(4);
        True(state.SetLabel(0, "Changed"), "The first edit must succeed.");
        long editedRevision = state.Revision;

        state.SelectSlot(7);
        True(state.Undo(), "Undo must succeed after an edit.");
        Equal("Base", state.CurrentScheme!.Keys[0]!.Label, "Undo must restore the old label.");
        Equal(4, state.SelectedSlot, "Undo must restore the pre-edit slot selection.");
        Equal(editedRevision + 1, state.Revision, "Undo must create one revision.");
        True(state.CanRedo, "Undo must create redo history.");

        True(state.Redo(), "Redo must succeed after undo.");
        Equal("Changed", state.CurrentScheme.Keys[0]!.Label, "Redo must restore the edited label.");
        Equal(7, state.SelectedSlot, "Redo must restore the state captured before undo.");
        Equal(editedRevision + 2, state.Revision, "Redo must create one revision.");

        True(state.Undo(), "A second undo must succeed.");
        True(state.SetLabel(0, "New edit"), "A new edit after undo must succeed.");
        False(state.CanRedo, "A new edit must clear redo history.");
        False(state.Redo(), "Redo must be unavailable after a new edit.");
    }

    private static void UndoRedoRestoresSelectionAndUnknownBindings()
    {
        KeyboardSchemes source = KeyboardSchemes.CreateDefault("First");
        source.Items.Add(NewScheme("second", "Second"));
        var state = new KeyboardEditingState(source);
        state.SelectScheme("second");
        True(state.SetKey(0, Binding("Unknown", "unknown.action", "Path", "value")),
            "The second scheme must receive an unknown binding.");
        state.SelectSlot(9);

        True(state.Undo(), "Undo must restore the binding-free state.");
        Equal("Second", state.CurrentScheme!.Name, "Undo must restore the selected scheme.");
        Equal(0, state.SelectedSlot, "Undo must restore the pre-edit slot selection.");
        Assert(state.CurrentScheme.Keys[0] == null, "Undo must remove the binding.");

        True(state.Redo(), "Redo must restore the unknown binding.");
        Equal("unknown.action", state.CurrentScheme.Keys[0]!.ActionId,
            "Redo must preserve unknown action IDs.");
        Equal("value", state.CurrentScheme.Keys[0]!.Parameters["Path"],
            "Redo must preserve binding parameters.");
    }

    private static void InvalidOperationsAreNoOps()
    {
        var state = new KeyboardEditingState(KeyboardSchemes.CreateDefault("Noops"));
        long revision = state.Revision;

        False(state.SetKey(-1, Binding("key", "action")), "Negative key slots must fail.");
        False(state.SetKey(12, Binding("key", "action")), "Out-of-range key slots must fail.");
        False(state.SetLabel(0, "label"), "Labels cannot attach to empty slots.");
        False(state.SwapSlots(0, 1), "Empty equal slots must not swap.");
        False(state.RenameScheme(""), "Empty names must fail.");
        False(state.AddScheme(""), "Empty added names must fail.");
        False(state.DuplicateScheme(""), "Empty duplicate names must fail.");
        False(state.RemoveScheme(), "The only scheme must not be removable.");
        Equal(revision, state.Revision, "Rejected operations must not create revisions.");
        False(state.CanUndo, "Rejected operations must not create undo history.");
        False(state.CanRedo, "Rejected operations must not create redo history.");
    }

    private static void SchemeCountLimitIsEnforced()
    {
        var source = new KeyboardSchemes();
        for (int i = 0; i < KeyboardSchemes.MaxSchemeCount - 1; i++)
        {
            source.Items.Add(NewScheme($"limit-{i}", $"Limit {i}"));
        }

        var state = new KeyboardEditingState(source);
        True(state.AddScheme("Last allowed"), "The 32nd scheme must be accepted.");
        Equal(KeyboardSchemes.MaxSchemeCount, state.Schemes.Items.Count,
            "The scheme count must reach the documented maximum.");
        False(state.AddScheme("Over limit"), "A 33rd scheme must be rejected.");
        False(state.DuplicateScheme("Over limit copy"), "Duplication must respect the scheme limit.");
        Equal(KeyboardSchemes.MaxSchemeCount, state.Schemes.Items.Count,
            "Rejected additions must not change the scheme count.");
    }

    private static void ParameterLimitIsEnforced()
    {
        var state = new KeyboardEditingState(KeyboardSchemes.CreateDefault("Limits"));
        string maxValue = new('x', KeyboardSchemes.MaxParameterValueLength);
        True(state.SetKey(0, Binding("Max", "action", "value", maxValue)),
            "An 8192-character parameter must be accepted.");
        Equal(maxValue.Length, state.CurrentScheme!.Keys[0]!.Parameters["value"].Length,
            "The maximum parameter must be preserved.");

        long revision = state.Revision;
        string overLimit = new('x', KeyboardSchemes.MaxParameterValueLength + 1);
        False(state.SetKey(0, Binding("Over", "action", "value", overLimit)),
            "An 8193-character parameter must be rejected.");
        Equal(revision, state.Revision, "A rejected parameter must not create a revision.");
    }

    private static void SerializationLimitIsEnforced()
    {
        KeyboardEditingState state = NewLargeState();
        int snapshotBytes = Encoding.UTF8.GetByteCount(state.Snapshot().Serialize());
        Assert(snapshotBytes < KeyboardSchemes.MaxJsonBytes,
            $"The large test fixture must remain valid before the mutation ({snapshotBytes} bytes).");

        long revision = state.Revision;
        var oversized = new CustomKeyBinding
        {
            Label = "Oversized",
            ActionId = "action",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [new string('k', KeyboardSchemes.MaxJsonBytes)] = "x",
            },
        };
        False(state.SetKey(0, oversized),
            "A mutation whose serialized model exceeds 1 MiB must be rejected.");
        Equal(revision, state.Revision, "A serialization rejection must not create a revision.");
        Assert(state.CurrentScheme!.Keys[0]!.ActionId != "action",
            "A rejected serialization must not partially apply.");
    }

    private static void HistoryCountIsCapped()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("History", Binding("Base", "action", "p", "1")));
        for (int i = 0; i < 40; i++)
        {
            True(state.SetLabel(0, $"Label {i}"), $"Edit {i} must succeed.");
        }

        int undoCount = 0;
        while (state.Undo())
        {
            undoCount++;
            Assert(undoCount <= 40, "Undo must terminate.");
        }
        Equal(32, undoCount, "The undo history must retain exactly the newest 32 entries.");
        True(state.CanRedo, "The undone entries must be available for redo.");

        int redoCount = 0;
        while (state.Redo())
        {
            redoCount++;
            Assert(redoCount <= 32, "Redo must terminate.");
        }
        Equal(32, redoCount, "Redo must restore the retained history.");
    }

    private static void HistoryBytesAreCapped()
    {
        KeyboardEditingState state = NewLargeState();
        int snapshotBytes = Encoding.UTF8.GetByteCount(state.Snapshot().Serialize());
        long estimatedEntryBytes = snapshotBytes + Encoding.UTF8.GetByteCount(state.SelectedSchemeId) + 16L;
        int expectedMaximum = (int)(4L * 1024 * 1024 / estimatedEntryBytes);
        Assert(expectedMaximum >= 1, "The byte-cap fixture must be below the cap.");

        for (int i = 0; i < 8; i++)
        {
            True(state.SetLabel(0, $"Large edit {i}"), $"Large edit {i} must succeed.");
        }

        int undoCount = 0;
        while (state.Undo())
        {
            undoCount++;
            Assert(undoCount <= 8, "Byte-capped undo must terminate.");
        }
        Assert(undoCount <= Math.Min(8, expectedMaximum),
            $"History retained {undoCount} entries but the estimated cap allows {expectedMaximum}.");
        Assert(undoCount < 8, "The four MiB cap must discard older history.");
    }

    private static void SnapshotIsDeepCopy()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("Snapshot", Binding("Base", "unknown.action", "Path", "original")));
        KeyboardSchemes snapshot = state.Snapshot();
        snapshot.Items[0].Name = "Changed snapshot";
        snapshot.Items[0].Keys[0]!.Parameters["Path"] = "changed snapshot";

        Equal("Snapshot", state.CurrentScheme!.Name, "Snapshot mutation must not change the state.");
        Equal("original", state.CurrentScheme.Keys[0]!.Parameters["Path"],
            "Snapshot parameter mutation must not change the state.");
        Equal(0L, state.Revision, "Taking a snapshot must not create a revision.");
    }

    private static void SerializedSnapshotPreservesModel()
    {
        var state = new KeyboardEditingState(
            SchemeWithBinding("Round trip", Binding("Unknown", "unknown.action", "Path", "value")));
        string json = state.Snapshot().Serialize();
        Assert(KeyboardSchemes.TryDeserialize(json, out KeyboardSchemes? loaded, out string? error),
            $"The serialized snapshot must deserialize: {error}");
        Equal("unknown.action", loaded!.Items[0].Keys[0]!.ActionId,
            "Round-trip action IDs must be preserved.");
        Equal("value", loaded.Items[0].Keys[0]!.Parameters["Path"],
            "Round-trip parameters must be preserved.");
    }

    private static KeyboardEditingState NewLargeState()
    {
        string value = new('x', KeyboardSchemes.MaxParameterValueLength);
        var schemes = new KeyboardSchemes();
        for (int schemeIndex = 0; schemeIndex < 9; schemeIndex++)
        {
            CustomKeyboardScheme scheme = NewScheme(
                $"large-{schemeIndex}",
                $"Large {schemeIndex}");
            for (int slot = 0; slot < KeyboardSchemes.SlotCount; slot++)
            {
                scheme.Keys[slot] = Binding(
                    $"Slot {slot}",
                    $"action.{schemeIndex}.{slot}",
                    "p",
                    value);
            }
            schemes.Items.Add(scheme);
        }

        return new KeyboardEditingState(schemes);
    }

    private static KeyboardSchemes SchemeWithBinding(
        string name,
        CustomKeyBinding binding)
    {
        KeyboardSchemes schemes = KeyboardSchemes.CreateDefault(name);
        schemes.Items[0].Keys[0] = binding;
        return schemes;
    }

    private static CustomKeyboardScheme NewScheme(string id, string name)
    {
        var keys = new List<CustomKeyBinding?>(KeyboardSchemes.SlotCount);
        for (int i = 0; i < KeyboardSchemes.SlotCount; i++)
        {
            keys.Add(null);
        }

        return new CustomKeyboardScheme
        {
            Id = id,
            Name = name,
            Keys = keys,
        };
    }

    private static CustomKeyBinding Binding(
        string label,
        string actionId,
        string parameterKey = "p",
        string parameterValue = "v")
    {
        return new CustomKeyBinding
        {
            Label = label,
            ActionId = actionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [parameterKey] = parameterValue,
            },
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void True(bool value, string message) =>
        Assert(value, message);

    private static void False(bool value, string message) =>
        Assert(!value, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}.");
    }
}
