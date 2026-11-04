using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StarPie.Plugin.TemporaryKeyboard;

internal static class Program
{
    private static string s_root = "";
    private static int s_passed;
    private static int s_failed;

    private static int Main()
    {
        string? root = Environment.GetEnvironmentVariable("KEYBOARD_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            Console.Error.WriteLine("KEYBOARD_TEST_ROOT must be set to an explicit evidence directory.");
            return 2;
        }

        s_root = Path.GetFullPath(root);
        Directory.CreateDirectory(s_root);

        Run("key whitelist and default scheme", WhitelistAndDefault);
        Run("serialization round trip and case-insensitive JSON", SerializationRoundTrip);
        Run("unknown fields survive load, edit, save, and cloning", UnknownFieldsPreservation);
        Run("scheme validation boundaries", ValidationBoundaries);
        Run("editing operations and deep clone", EditingOperations);
        Run("unknown metadata affects binding equality", UnknownMetadataBindingEquality);
        Run("resize preserves safe linear slots", ResizeSemantics);
        Run("history count and byte limits", HistoryLimits);
        Run("unknown fields count toward history budget", UnknownFieldsHistoryBudget);
        Run("storage round trip and old file untouched", StorageRoundTrip);
        Run("storage corruption recovery", StorageCorruptionRecovery);
        Run("storage oversized recovery", StorageOversizedRecovery);
        Run("storage concurrency and write failure", StorageFailureAndConcurrency);

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
            Console.WriteLine($"FAIL {name}: {ex}");
        }
    }

    private static void WhitelistAndDefault()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        Assert(fullIds.Count > 0, "The full keyboard layout must expose key IDs.");
        Assert(fullIds.Distinct(StringComparer.Ordinal).Count() == fullIds.Count,
            "Full layout key IDs must be unique.");

        KeySchemes schemes = KeySchemes.CreateDefault("Default");
        KeyScheme scheme = schemes.Items.Single();
        Equal(4, scheme.Rows, "Default rows mismatch.");
        Equal(3, scheme.Columns, "Default columns mismatch.");
        Equal(12, scheme.Keys.Count, "Default slot count mismatch.");
        Assert(scheme.Keys.All(binding => binding == null), "Default slots must be empty.");
        Equal("Default", scheme.ToString(), "KeyScheme.ToString must return its name.");
        Assert(schemes.TryValidate(out string? defaultError),
            $"The default scheme must validate: {defaultError}");

        scheme.Keys[0] = Binding(fullIds[0], "A");
        Assert(schemes.TryValidate(out string? validError),
            $"A whitelisted key must validate: {validError}");

        KeySchemes unknown = schemes.DeepClone();
        unknown.Items[0].Keys[0]!.KeyId = "action.not-a-key";
        AssertInvalid(unknown, KeySchemes.ErrorUnknownKeyId);

        KeySchemes emptyId = schemes.DeepClone();
        emptyId.Items[0].Keys[0]!.KeyId = "";
        AssertInvalid(emptyId, KeySchemes.ErrorEmptyKeyId);
    }

    private static void SerializationRoundTrip()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        KeySchemes source = KeySchemes.CreateDefault("Round trip");
        source.Items[0].Keys[0] = Binding(fullIds[0], "A");
        string json = source.Serialize();
        Assert(json.Contains("\"keyId\"", StringComparison.Ordinal),
            "Serialized JSON must use camelCase keyId.");
        Assert(json.Contains("\"rows\"", StringComparison.Ordinal),
            "Serialized JSON must use camelCase rows.");

        Assert(KeySchemes.TryDeserialize(json, out KeySchemes? loaded, out string? error),
            $"Round-trip JSON must deserialize: {error}");
        Equal(fullIds[0], loaded!.Items[0].Keys[0]!.KeyId,
            "Round-trip key ID mismatch.");
        Equal("A", loaded.Items[0].Keys[0]!.Label,
            "Round-trip label mismatch.");

        string keys = string.Join(",", Enumerable.Range(0, 12).Select(index =>
            index == 0
                ? $"{{\"KeyId\":\"{fullIds[0]}\",\"Label\":\"Case\"}}"
                : "null"));
        string alternate = "{\"Items\":[{\"ID\":\"case\",\"NAME\":\"Case\",\"Rows\":4," +
            $"\"Columns\":3,\"Keys\":[{keys}]}}]}}";
        Assert(KeySchemes.TryDeserialize(alternate, out KeySchemes? caseLoaded, out string? caseError),
            $"Case-insensitive JSON must deserialize: {caseError}");
        Equal("Case", caseLoaded!.Items[0].Name,
            "Case-insensitive property names must be accepted.");

        string unknown = json.Replace(fullIds[0], "action.unknown", StringComparison.Ordinal);
        Assert(!KeySchemes.TryDeserialize(unknown, out _, out string? unknownError),
            "Unknown key IDs must be rejected during deserialization.");
        Equal(KeySchemes.ErrorUnknownKeyId, unknownError,
            "Unknown key ID error mismatch.");
        Assert(!KeySchemes.TryDeserialize("{", out _, out string? invalidError),
            "Malformed JSON must be rejected.");
        Equal(KeySchemes.ErrorInvalidJson, invalidError,
            "Malformed JSON error mismatch.");
        Assert(!KeySchemes.TryDeserialize(new string('x', KeySchemes.MaxJsonBytes + 1), out _, out string? sizeError),
            "Oversized JSON must be rejected.");
        Equal(KeySchemes.ErrorJsonTooLarge, sizeError,
            "Oversized JSON error mismatch.");
    }

    private static void UnknownFieldsPreservation()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        KeySchemes source = KeySchemes.CreateDefault("Unknown fields");
        source.Items[0].Keys[0] = Binding(fullIds[0], "Original");
        string json = WithUnknownFields(source.Serialize());

        Assert(KeySchemes.TryDeserialize(json, out KeySchemes? stringLoaded, out string? stringError),
            $"Unknown fields must deserialize from text: {stringError}");
        AssertUnknownFields(stringLoaded!);

        KeySchemes loaded;
        using (JsonDocument external = JsonDocument.Parse(json))
        {
            Assert(KeySchemes.TryDeserialize(
                    external.RootElement,
                    out KeySchemes? elementLoaded,
                    out string? elementError),
                $"Unknown fields must deserialize from an external JsonDocument: {elementError}");
            loaded = elementLoaded!;
        }

        AssertUnknownFields(loaded);
        string afterDocumentDispose = loaded.Serialize();
        Assert(KeySchemes.TryDeserialize(
                afterDocumentDispose,
                out KeySchemes? reparsed,
                out string? reparseError),
            $"Unknown fields must remain valid after the source document is disposed: {reparseError}");
        AssertUnknownFields(reparsed!);

        KeySchemes clone = loaded.DeepClone();
        Assert(!ReferenceEquals(loaded.ExtensionData, clone.ExtensionData),
            "The root extension dictionary must not be shared by DeepClone.");
        Assert(!ReferenceEquals(
                loaded.Items[0].ExtensionData,
                clone.Items[0].ExtensionData),
            "The scheme extension dictionary must not be shared by DeepClone.");
        Assert(!ReferenceEquals(
                loaded.Items[0].Keys[0]!.ExtensionData,
                clone.Items[0].Keys[0]!.ExtensionData),
            "The key extension dictionary must not be shared by DeepClone.");

        clone.ExtensionData["futureRoot"] = JsonSerializer.SerializeToElement(
            new { changed = true });
        clone.Items[0].ExtensionData["futureScheme"] = JsonSerializer.SerializeToElement(
            new[] { "changed" });
        clone.Items[0].Keys[0]!.ExtensionData["futureKey"] = JsonSerializer.SerializeToElement(
            new { changed = true });
        AssertUnknownFields(loaded);
        Assert(clone.ExtensionData["futureRoot"].GetProperty("changed").GetBoolean(),
            "The cloned root extension must be mutable independently.");
        Equal("changed", clone.Items[0].ExtensionData["futureScheme"][0].GetString(),
            "The cloned scheme extension must be mutable independently.");
        Assert(clone.Items[0].Keys[0]!.ExtensionData["futureKey"]
                .GetProperty("changed").GetBoolean(),
            "The cloned key extension must be mutable independently.");

        string directory = NewCaseDirectory("unknown");
        byte[] oldBytes = Encoding.UTF8.GetBytes("{\"old\":true}");
        string oldPath = Path.Combine(directory, "keyboard-schemes.json");
        File.WriteAllBytes(oldPath, oldBytes);
        var storage = new KeySchemeStorage(directory);
        KeySchemeDocument missing = storage.Load("Fallback");
        storage.Save(missing, loaded, allowRecovery: false);
        KeySchemeDocument first = storage.Load("Fallback");
        AssertUnknownFields(first.Schemes);

        var state = new KeyEditingState(first.Schemes);
        True(state.SetLabel(0, "Edited"), "SetLabel must preserve unknown fields.");
        AssertUnknownFields(state.Schemes, "Edited");
        True(state.Resize(2, 3), "Resize must preserve unknown fields.");
        AssertUnknownFields(state.Schemes, "Edited", expectedKeyCount: 6);
        True(state.Undo(), "Undo must restore a snapshot with unknown fields.");
        AssertUnknownFields(state.Schemes, "Edited");

        storage.Save(first, state.Schemes, allowRecovery: false);
        KeySchemeDocument saved = storage.Load("Fallback");
        AssertUnknownFields(saved.Schemes, "Edited");
        EqualBytes(oldBytes, File.ReadAllBytes(oldPath),
            "The legacy keyboard-schemes.json file must remain byte-identical.");

        KeySchemes tooLarge = loaded.DeepClone();
        tooLarge.ExtensionData["futurePadding"] =
            JsonSerializer.SerializeToElement(new string('x', KeySchemes.MaxJsonBytes));
        Throws<InvalidOperationException>(
            () => tooLarge.Serialize(),
            "Unknown extension data must count toward the UTF-8 size limit.");
    }

    private static string WithUnknownFields(string json)
    {
        JsonNode root = JsonNode.Parse(json) ??
            throw new InvalidOperationException("Serialized schemes must be a JSON object.");
        root["futureRoot"] = JsonNode.Parse(
            """{"format":7,"rootNested":{"keep":true}}""");
        JsonNode scheme = root["items"]![0]!;
        scheme["futureScheme"] = JsonNode.Parse(
            """[1,"two",{"three":3}]""");
        scheme["keys"]![0]!["futureKey"] = JsonNode.Parse(
            """{"metadata":{"owner":"future"},"flags":[true,null]}""");
        return root.ToJsonString();
    }

    private static void AssertUnknownFields(
        KeySchemes schemes,
        string expectedLabel = "Original",
        int expectedKeyCount = 12)
    {
        Assert(schemes.ExtensionData.TryGetValue("futureRoot", out JsonElement root),
            "The root unknown field must be preserved.");
        Equal(7, root.GetProperty("format").GetInt32(),
            "The nested root unknown field must be preserved.");
        Assert(root.GetProperty("rootNested").GetProperty("keep").GetBoolean(),
            "The nested root object must be preserved.");

        Assert(schemes.Items.Count == 1, "The known scheme count must remain unchanged.");
        Assert(schemes.Items[0].ExtensionData.TryGetValue(
                "futureScheme",
                out JsonElement scheme),
            "The scheme unknown field must be preserved.");
        Equal(3, scheme.GetArrayLength(), "The scheme unknown array length mismatch.");
        Equal(1, scheme[0].GetInt32(), "The scheme unknown array value mismatch.");
        Equal("two", scheme[1].GetString(), "The scheme unknown array string mismatch.");
        Equal(3, scheme[2].GetProperty("three").GetInt32(),
            "The nested scheme unknown value mismatch.");

        KeyBinding binding = schemes.Items[0].Keys[0]!;
        Assert(binding.ExtensionData.TryGetValue("futureKey", out JsonElement key),
            "The key unknown field must be preserved.");
        Equal("future", key.GetProperty("metadata").GetProperty("owner").GetString(),
            "The nested key unknown metadata mismatch.");
        Assert(key.GetProperty("flags")[1].ValueKind == JsonValueKind.Null,
            "The nested key unknown null value mismatch.");
        Equal(expectedLabel, binding.Label, "The known key label must remain unchanged.");
        Equal(expectedKeyCount, schemes.Items[0].Keys.Count,
            "Unknown fields must not be interpreted as additional keys.");
    }

    private static void ValidationBoundaries()
    {
        KeySchemes duplicateId = KeySchemes.CreateDefault("One");
        KeyScheme second = NewScheme("second", "Two");
        second.Id = duplicateId.Items[0].Id.ToUpperInvariant();
        duplicateId.Items.Add(second);
        AssertInvalid(duplicateId, KeySchemes.ErrorDuplicateSchemeId);

        KeySchemes duplicateName = KeySchemes.CreateDefault("Same");
        duplicateName.Items.Add(NewScheme("different", " same "));
        AssertInvalid(duplicateName, KeySchemes.ErrorDuplicateSchemeName);

        KeySchemes emptyId = KeySchemes.CreateDefault("Empty ID");
        emptyId.Items[0].Id = " ";
        AssertInvalid(emptyId, KeySchemes.ErrorEmptySchemeId);

        KeySchemes emptyName = KeySchemes.CreateDefault("Empty name");
        emptyName.Items[0].Name = " ";
        AssertInvalid(emptyName, KeySchemes.ErrorEmptySchemeName);

        KeySchemes longName = KeySchemes.CreateDefault("Long name");
        longName.Items[0].Name = new string('n', KeySchemes.MaxSchemeNameLength + 1);
        AssertInvalid(longName, KeySchemes.ErrorSchemeNameTooLong);

        KeySchemes rows = KeySchemes.CreateDefault("Rows");
        rows.Items[0].Rows = 0;
        AssertInvalid(rows, KeySchemes.ErrorRows);

        KeySchemes columns = KeySchemes.CreateDefault("Columns");
        columns.Items[0].Columns = 17;
        AssertInvalid(columns, KeySchemes.ErrorColumns);

        KeySchemes count = KeySchemes.CreateDefault("Count");
        count.Items[0].Keys.RemoveAt(0);
        AssertInvalid(count, KeySchemes.ErrorKeyCount);

        KeySchemes longLabel = KeySchemes.CreateDefault("Label");
        longLabel.Items[0].Keys[0] = Binding(FullKeyIds()[0], new string('l', KeySchemes.MaxKeyLabelLength + 1));
        AssertInvalid(longLabel, KeySchemes.ErrorKeyLabelTooLong);

        KeySchemes maxSlots = new()
        {
            Items =
            [
                NewScheme("max", "Max", 10, 16),
            ],
        };
        Assert(maxSlots.TryValidate(out string? maxError),
            $"A 10x16 scheme must be valid: {maxError}");

        var tooMany = new KeySchemes();
        for (int index = 0; index < KeySchemes.MaxSchemeCount; index++)
        {
            tooMany.Items.Add(NewScheme($"scheme-{index}", $"Scheme {index}"));
        }
        Assert(tooMany.TryValidate(out _), "32 schemes must be valid.");
        tooMany.Items.Add(NewScheme("extra", "Extra"));
        AssertInvalid(tooMany, KeySchemes.ErrorTooManySchemes);

        KeySchemes nullItems = new() { Items = null! };
        AssertInvalid(nullItems, KeySchemes.ErrorSchemesNull);
        KeySchemes nullScheme = new() { Items = [null!] };
        AssertInvalid(nullScheme, KeySchemes.ErrorSchemeNull);
    }

    private static void EditingOperations()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        var source = KeySchemes.CreateDefault("Source");
        source.Items[0].Keys[0] = Binding(fullIds[0], "Original");
        var state = new KeyEditingState(source);
        Equal(0L, state.Revision, "Constructor revision mismatch.");
        state.SelectSlot(5);
        Equal(5, state.SelectedSlot, "A valid slot must be selected.");
        Equal(0L, state.Revision, "Selection must not create a revision.");

        source.Items[0].Keys[0]!.Label = "Source changed";
        Equal("Original", state.CurrentScheme!.Keys[0]!.Label,
            "The state must not alias the source binding.");
        True(state.SetLabel(0, "Changed"), "SetLabel must succeed.");
        False(state.SetLabel(0, "Changed"), "An equal label must be a no-op.");
        False(state.SetKey(0, Binding(fullIds[0], "Changed")),
            "An equal key binding must be a no-op.");
        True(state.SwapSlots(0, 1), "Swapping a bound and empty slot must succeed.");
        Assert(state.CurrentScheme.Keys[0] == null, "The first slot must now be empty.");
        Equal("Changed", state.CurrentScheme.Keys[1]!.Label, "The second slot must hold the binding.");

        long revision = state.Revision;
        True(state.Undo(), "Undo must succeed.");
        Equal(revision + 1, state.Revision, "Undo must create a revision.");
        True(state.Redo(), "Redo must succeed.");
        Equal(revision + 2, state.Revision, "Redo must create a revision.");
        True(state.Undo(), "A second undo must succeed.");
        True(state.SetLabel(0, "After undo"), "A new edit must succeed after undo.");
        False(state.CanRedo, "A new edit must clear redo.");

        True(state.RenameScheme(" Renamed "), "Rename must succeed.");
        True(state.DuplicateScheme("Copy"), "Duplicate must succeed.");
        Equal("Copy", state.CurrentScheme!.Name, "The duplicate must be selected.");
        True(state.SetLabel(0, "Copy edit"), "The duplicate must be editable.");
        KeyScheme original = state.Schemes.Items.Single(item => item.Name == "Renamed");
        Equal("After undo", original.Keys[0]!.Label,
            "Editing the duplicate must not alter the source.");

        True(state.AddScheme("Third"), "Add must succeed.");
        True(state.RemoveScheme(), "Removing the selected third scheme must succeed.");
        Equal("Copy", state.CurrentScheme!.Name, "Removal must select the previous neighbor.");
        True(state.RemoveScheme(), "Removing the selected copy must succeed.");
        Equal("Renamed", state.CurrentScheme!.Name, "Removal must select the previous neighbor.");
        False(state.RemoveScheme(), "The last scheme must not be removable.");

        var snapshot = state.Snapshot();
        snapshot.Items[0].Name = "Snapshot changed";
        Assert(state.Schemes.Items[0].Name != "Snapshot changed",
            "Snapshot must be a deep copy.");
    }

    private static void UnknownMetadataBindingEquality()
    {
        string keyId = FullKeyIds()[0];
        const string label = "Same";
        KeyBinding first = BindingWithMetadata(
            keyId,
            label,
            """{"version":1,"owner":"first"}""");
        KeyBinding second = BindingWithMetadata(
            keyId,
            label,
            """{"version":1,"owner":"second"}""");
        var source = KeySchemes.CreateDefault("Metadata equality");
        source.Items[0].Keys[0] = first;
        var state = new KeyEditingState(source);

        False(state.SetKey(0, first),
            "A binding with identical unknown metadata must remain a no-op.");
        True(state.SetKey(0, second),
            "Different nested unknown metadata must be a change.");
        Equal("second", MetadataOwner(state.CurrentScheme!.Keys[0]!),
            "SetKey must replace the unknown metadata.");

        True(state.SetKey(1, first), "The second slot must be populated.");
        True(state.SwapSlots(0, 1),
            "Swapping bindings with different unknown metadata must be a change.");
        Equal("first", MetadataOwner(state.CurrentScheme.Keys[0]!),
            "Swap must move the first metadata.");
        Equal("second", MetadataOwner(state.CurrentScheme.Keys[1]!),
            "Swap must move the second metadata.");

        True(state.Undo(), "Undo must restore the metadata before the swap.");
        Equal("second", MetadataOwner(state.CurrentScheme.Keys[0]!),
            "Undo must restore the second metadata to the first slot.");
        Equal("first", MetadataOwner(state.CurrentScheme.Keys[1]!),
            "Undo must restore the first metadata to the second slot.");
        True(state.Redo(), "Redo must restore the metadata after the swap.");
        Equal("first", MetadataOwner(state.CurrentScheme.Keys[0]!),
            "Redo must restore the first metadata to the first slot.");
        Equal("second", MetadataOwner(state.CurrentScheme.Keys[1]!),
            "Redo must restore the second metadata to the second slot.");

        KeyBinding reordered = BindingWithMetadata(
            keyId,
            label,
            """{"owner":"first","version":1}""");
        True(state.SetKey(0, reordered),
            "Different object property order in unknown metadata must be a change.");
        KeyBinding spaced = BindingWithMetadata(
            keyId,
            label,
            """{ "version": 1, "owner": "first" }""");
        True(state.SetKey(0, spaced),
            "Different whitespace in unknown metadata must be a change.");
        False(state.SetKey(0, spaced),
            "Identical raw unknown metadata must remain a no-op.");

        KeyBinding alternateKey = BindingWithMetadata(
            keyId,
            label,
            """{"version":1,"owner":"first"}""");
        alternateKey.ExtensionData.Remove("rootMetadata");
        alternateKey.ExtensionData["alternateMetadata"] =
            RawJson("""{"version":1,"owner":"first"}""");
        True(state.SetKey(0, alternateKey),
            "Different unknown metadata dictionary keys must be a change.");

        KeySchemes snapshot = state.Snapshot();
        Assert(!ReferenceEquals(
                state.CurrentScheme.Keys[0]!.ExtensionData,
                snapshot.Items[0].Keys[0]!.ExtensionData),
            "A snapshot must not share the binding metadata dictionary.");
        snapshot.Items[0].Keys[0]!.ExtensionData["alternateMetadata"] =
            RawJson("""{"version":99,"owner":"snapshot"}""");
        Equal(1, state.CurrentScheme.Keys[0]!.ExtensionData["alternateMetadata"]
                .GetProperty("version").GetInt32(),
            "Mutating a snapshot must not change the original metadata.");

        True(state.DuplicateScheme("Metadata copy"),
            "A scheme with unknown binding metadata must be duplicable.");
        KeyScheme duplicate = state.CurrentScheme!;
        Equal(1, duplicate.Keys[0]!.ExtensionData["alternateMetadata"]
                .GetProperty("version").GetInt32(),
            "Duplicating a scheme must copy the unknown metadata.");
        KeyScheme original = state.Schemes.Items.Single(
            scheme => scheme.Name == "Metadata equality");
        True(state.SetKey(0, second),
            "The duplicated scheme must remain independently editable.");
        Equal(1, original.Keys[0]!.ExtensionData["alternateMetadata"]
                .GetProperty("version").GetInt32(),
            "Editing the duplicate must not alter the original metadata.");
    }

    private static void ResizeSemantics()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        var state = new KeyEditingState(KeySchemes.CreateDefault("Resize"));
        True(state.SetKey(0, Binding(fullIds[0], "Zero")), "Slot zero must be bound.");
        True(state.SetKey(11, Binding(fullIds[1], "Eleven")), "Slot eleven must be bound.");
        state.SelectSlot(8);
        long revision = state.Revision;

        False(state.Resize(2, 3), "Shrinking must reject truncation of a non-empty slot.");
        Equal(revision, state.Revision, "Rejected resize must not create a revision.");
        Equal(4, state.CurrentScheme!.Rows, "Rejected resize must not change rows.");
        Equal(3, state.CurrentScheme.Columns, "Rejected resize must not change columns.");

        True(state.SetKey(11, null), "The tail binding must be removable.");
        True(state.Resize(2, 3), "Shrinking empty tail slots must succeed.");
        Equal(2, state.CurrentScheme!.Rows, "Rows must be updated.");
        Equal(3, state.CurrentScheme.Columns, "Columns must be updated.");
        Equal(6, state.CurrentScheme.Keys.Count, "Resized key count mismatch.");
        Equal("Zero", state.CurrentScheme.Keys[0]!.Label, "Linear key zero must be preserved.");
        Equal(5, state.SelectedSlot, "Selected slot must clamp to the new last slot.");

        True(state.Resize(4, 4), "Growing must succeed.");
        Equal(16, state.CurrentScheme!.Keys.Count, "Grown key count mismatch.");
        Equal("Zero", state.CurrentScheme.Keys[0]!.Label, "Existing keys must survive growth.");
        Assert(state.CurrentScheme.Keys[6] == null, "New slots must be empty.");
        False(state.Resize(4, 4), "A no-op resize must return false.");
        False(state.Resize(0, 3), "Invalid rows must be rejected.");
        False(state.Resize(4, 17), "Invalid columns must be rejected.");

        True(state.Resize(1, 6), "Changing dimensions without changing the total must succeed.");
        Equal("Zero", state.CurrentScheme!.Keys[0]!.Label,
            "Linear ordering must be preserved when reflowing rows and columns.");
    }

    private static void HistoryLimits()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        var state = new KeyEditingState(KeySchemes.CreateDefault("History"));
        True(state.SetKey(0, Binding(fullIds[0], "Base")), "The history slot must be bound.");
        for (int index = 0; index < 40; index++)
        {
            True(state.SetLabel(0, $"Label {index}"), $"History edit {index} must succeed.");
        }

        int undoCount = 0;
        while (state.Undo())
        {
            undoCount++;
            Assert(undoCount <= 40, "Undo must terminate.");
        }
        Equal(32, undoCount, "The history must retain exactly 32 entries.");
        int redoCount = 0;
        while (state.Redo())
        {
            redoCount++;
            Assert(redoCount <= 32, "Redo must terminate.");
        }
        Equal(32, redoCount, "Redo must restore the retained history.");

        KeySchemes large = BuildLargeSchemes(24, 160);
        var largeState = new KeyEditingState(large);
        int snapshotBytes = Encoding.UTF8.GetByteCount(largeState.Snapshot().Serialize());
        int expectedMaximum = (int)(4L * 1024 * 1024 / (snapshotBytes + 64L));
        Assert(expectedMaximum >= 1, "The large fixture must be below the history cap.");
        for (int index = 0; index < 8; index++)
        {
            True(largeState.SetLabel(0, $"Large {index}"), $"Large edit {index} must succeed.");
        }

        int largeUndo = 0;
        while (largeState.Undo())
        {
            largeUndo++;
            Assert(largeUndo <= 8, "Large undo must terminate.");
        }
        Assert(largeUndo < 8, "The four MiB cap must discard older history.");
        Assert(largeUndo <= expectedMaximum,
            $"History retained {largeUndo} entries but the cap allows {expectedMaximum}.");
    }

    private static void UnknownFieldsHistoryBudget()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        KeySchemes source = KeySchemes.CreateDefault("Unknown history");
        source.Items[0].Keys[0] = Binding(fullIds[0], "Base");
        source.ExtensionData["futurePayload"] =
            JsonSerializer.SerializeToElement(new string('x', 600_000));
        var state = new KeyEditingState(source);
        for (int index = 0; index < 8; index++)
        {
            True(state.SetLabel(0, $"Payload {index}"),
                $"Unknown-field history edit {index} must succeed.");
            Assert(state.Schemes.ExtensionData["futurePayload"].GetString()!.Length == 600_000,
                "Unknown history data must survive each edit.");
        }

        int undoCount = 0;
        while (state.Undo())
        {
            undoCount++;
            Assert(undoCount <= 8, "Unknown-field history undo must terminate.");
        }

        Assert(undoCount < 8,
            "Unknown extension bytes must count toward the history capacity.");
    }

    private static void StorageRoundTrip()
    {
        IReadOnlyList<string> fullIds = FullKeyIds();
        string directory = NewCaseDirectory("roundtrip");
        byte[] oldBytes = Encoding.UTF8.GetBytes("{\"old\":true}");
        string oldPath = Path.Combine(directory, "keyboard-schemes.json");
        File.WriteAllBytes(oldPath, oldBytes);

        var storage = new KeySchemeStorage(directory);
        KeySchemeDocument document = storage.Load("Default");
        False(document.FromFile, "A missing new file must not report FromFile.");
        False(document.RequiresRecovery, "A missing new file must not require recovery.");
        Equal("Default", document.Schemes.Items[0].Name, "Default name mismatch.");

        document.Schemes.Items[0].Keys[0] = Binding(fullIds[0], "Saved");
        storage.Save(document, document.Schemes, allowRecovery: false);
        string newPath = Path.Combine(directory, "keyboard-key-schemes.json");
        Assert(File.Exists(newPath), "The new key scheme file must be created.");
        EqualBytes(oldBytes, File.ReadAllBytes(oldPath),
            "The legacy keyboard-schemes.json file must remain byte-identical.");

        KeySchemeDocument loaded = storage.Load("Fallback");
        True(loaded.FromFile, "The new file must be loaded from disk.");
        False(loaded.RequiresRecovery, "A valid new file must not require recovery.");
        Equal("Saved", loaded.Schemes.Items[0].Keys[0]!.Label,
            "Stored binding mismatch.");
    }

    private static void StorageCorruptionRecovery()
    {
        string directory = NewCaseDirectory("corrupt");
        string path = Path.Combine(directory, "keyboard-key-schemes.json");
        byte[] corrupt = [0xFF, 0xFE, 0x00, 0x01];
        File.WriteAllBytes(path, corrupt);

        var storage = new KeySchemeStorage(directory);
        KeySchemeDocument document = storage.Load("Recovered");
        True(document.FromFile, "A corrupt file must report FromFile.");
        True(document.RequiresRecovery, "A corrupt file must require recovery.");
        EqualBytes(corrupt, document.OriginalBytes!,
            "The corrupt source bytes must be retained.");

        Throws<IOException>(
            () => storage.Save(document, document.Schemes, allowRecovery: false),
            "Saving corrupt data without explicit recovery must fail.");
        EqualBytes(corrupt, File.ReadAllBytes(path),
            "A rejected recovery save must not overwrite the corrupt file.");

        storage.Save(document, document.Schemes, allowRecovery: true);
        string[] backups = Directory.GetFiles(directory, "keyboard-key-schemes.recovery-*.json");
        Equal(1, backups.Length, "One recovery backup must be written.");
        EqualBytes(corrupt, File.ReadAllBytes(backups[0]),
            "The recovery backup must contain the original bytes.");
        KeySchemeDocument recovered = storage.Load("After recovery");
        False(recovered.RequiresRecovery, "The recovered new file must be valid.");
    }

    private static void StorageOversizedRecovery()
    {
        string directory = NewCaseDirectory("oversized");
        string path = Path.Combine(directory, "keyboard-key-schemes.json");
        byte[] oversized = new byte[KeySchemes.MaxJsonBytes + 1];
        File.WriteAllBytes(path, oversized);

        var storage = new KeySchemeStorage(directory);
        KeySchemeDocument document = storage.Load("Oversized");
        True(document.RequiresRecovery, "An oversized file must require recovery.");
        Assert(document.OriginalBytes == null,
            "An oversized file must not be implicitly retained as a backup.");
        Throws<IOException>(
            () => storage.Save(document, document.Schemes, allowRecovery: true),
            "Oversized files must require manual recovery.");
        EqualBytes(oversized, File.ReadAllBytes(path),
            "An oversized file must remain unchanged.");
    }

    private static void StorageFailureAndConcurrency()
    {
        string directory = NewCaseDirectory("failure");
        var storage = new KeySchemeStorage(
            directory,
            (_, _) => throw new IOException("injected write failure"));
        KeySchemeDocument document = storage.Load("Failure");
        Throws<IOException>(
            () => storage.Save(document, document.Schemes, allowRecovery: false),
            "An injected write failure must propagate.");
        Assert(!File.Exists(Path.Combine(directory, "keyboard-key-schemes.json")),
            "A failed write must not create the scheme file.");

        string concurrencyDirectory = NewCaseDirectory("concurrency");
        var concurrencyStorage = new KeySchemeStorage(concurrencyDirectory);
        KeySchemeDocument original = concurrencyStorage.Load("Concurrency");
        string path = Path.Combine(concurrencyDirectory, "keyboard-key-schemes.json");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("{}"));
        Throws<IOException>(
            () => concurrencyStorage.Save(original, original.Schemes, allowRecovery: false),
            "A changed file signature must reject the save.");
        Equal("{}", File.ReadAllText(path),
            "A concurrent-change rejection must not overwrite the file.");
    }

    private static KeySchemes BuildLargeSchemes(int count, int slots)
    {
        IReadOnlyList<string> ids = FullKeyIds();
        var schemes = new KeySchemes();
        string label = new('x', KeySchemes.MaxKeyLabelLength);
        for (int schemeIndex = 0; schemeIndex < count; schemeIndex++)
        {
            var keys = new List<KeyBinding?>(slots);
            for (int slot = 0; slot < slots; slot++)
            {
                keys.Add(Binding(ids[slot % ids.Count], label));
            }

            schemes.Items.Add(new KeyScheme
            {
                Id = $"large-{schemeIndex}",
                Name = $"Large {schemeIndex}",
                Rows = 10,
                Columns = 16,
                Keys = keys,
            });
        }

        return schemes;
    }

    private static KeyScheme NewScheme(
        string id,
        string name,
        int rows = 4,
        int columns = 3)
    {
        return new KeyScheme
        {
            Id = id,
            Name = name,
            Rows = rows,
            Columns = columns,
            Keys = Enumerable.Repeat<KeyBinding?>(null, rows * columns).ToList(),
        };
    }

    private static KeyBinding Binding(string keyId, string label)
    {
        return new KeyBinding
        {
            KeyId = keyId,
            Label = label,
        };
    }

    private static KeyBinding BindingWithMetadata(
        string keyId,
        string label,
        string metadataJson)
    {
        KeyBinding binding = Binding(keyId, label);
        binding.ExtensionData["rootMetadata"] = RawJson(metadataJson);
        return binding;
    }

    private static string MetadataOwner(KeyBinding binding)
    {
        return binding.ExtensionData["rootMetadata"]
            .GetProperty("owner")
            .GetString() ?? "";
    }

    private static JsonElement RawJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> FullKeyIds()
    {
        return KeyboardLayouts.Create(KeyboardMode.Full)
            .SelectMany(row => row)
            .Select(key => key.Id)
            .ToList();
    }

    private static string NewCaseDirectory(string name)
    {
        string path = Path.Combine(s_root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertInvalid(KeySchemes schemes, string expectedError)
    {
        Assert(!schemes.TryValidate(out string? error),
            $"Expected validation failure: {expectedError}");
        Equal(expectedError, error, "Validation error mismatch.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void True(bool value, string message) => Assert(value, message);

    private static void False(bool value, string message) => Assert(!value, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void EqualBytes(byte[] expected, byte[] actual, string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(message);
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
