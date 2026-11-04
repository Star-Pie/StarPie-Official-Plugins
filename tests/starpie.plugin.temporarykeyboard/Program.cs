using StarPie.Plugin.TemporaryKeyboard;

internal static class Program
{
    private static int s_passed;
    private static int s_failed;

    private static int Main()
    {
        Run("layouts are distinct and custom is empty", LayoutsAreDistinct);
        Run("numeric layout contains required keys", NumericLayoutContainsRequiredKeys);
        Run("letters layout supports case and punctuation", LettersLayoutSupportsCaseAndPunctuation);
        Run("functions layout contains function, navigation, and edit keys", FunctionsLayoutContainsRequiredKeys);
        Run("full layout uses hotkeys for letters and modifiers", FullLayoutUsesHotkeysAndModifiers);
        Run("modifier latch toggles and consumes once", ModifierLatchTogglesAndConsumesOnce);
        Run("default scheme has twelve empty slots", DefaultSchemeHasTwelveEmptySlots);
        Run("scheme round trip preserves unknown bindings", SchemeRoundTripPreservesUnknownBindings);
        Run("deep clone copies parameters", DeepCloneCopiesParameters);
        Run("scheme validation rejects invalid boundaries", SchemeValidationRejectsInvalidBoundaries);
        Run("scheme limits accept and reject boundary values", SchemeLimitsAcceptAndRejectBoundaries);
        Run("deserialization rejects corrupt and oversized JSON", DeserializationRejectsCorruptAndOversizedJson);
        StorageTests.Run(Run);

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

    private static void LayoutsAreDistinct()
    {
        var numeric = Flatten(KeyboardLayouts.Create(KeyboardMode.Numeric));
        var letters = Flatten(KeyboardLayouts.Create(KeyboardMode.Letters));
        var functions = Flatten(KeyboardLayouts.Create(KeyboardMode.Functions));
        var full = Flatten(KeyboardLayouts.Create(KeyboardMode.Full));
        var custom = KeyboardLayouts.Create(KeyboardMode.Custom);

        Assert(numeric.Count > 0, "Numeric layout must not be empty.");
        Assert(letters.Count > 0, "Letters layout must not be empty.");
        Assert(functions.Count > 0, "Functions layout must not be empty.");
        Assert(full.Count > 0, "Full layout must not be empty.");
        Assert(custom.Count == 0, "Custom layout must be empty until a scheme is rendered.");

        string[] signatures =
        [
            Signature(numeric),
            Signature(letters),
            Signature(functions),
            Signature(full),
        ];
        Assert(signatures.Distinct(StringComparer.Ordinal).Count() == 4,
            "Numeric, letters, functions, and full layouts must be distinct.");
    }

    private static void NumericLayoutContainsRequiredKeys()
    {
        List<KeyboardKey> keys = Flatten(KeyboardLayouts.Create(KeyboardMode.Numeric));

        for (int digit = 0; digit <= 9; digit++)
        {
            string value = digit.ToString();
            Assert(Has(keys, KeyboardKeyKind.Text, value), $"Missing numeric digit {value}.");
        }

        foreach (string punctuation in new[] { ".", "+", "-", "*", "/" })
        {
            Assert(Has(keys, KeyboardKeyKind.Text, punctuation), $"Missing numeric operator {punctuation}.");
        }

        Assert(Has(keys, KeyboardKeyKind.Hotkey, "Back"), "Missing Backspace.");
        Assert(Has(keys, KeyboardKeyKind.Hotkey, "Return"), "Missing Enter.");

        KeyboardKey seven = FindById(keys, "num-7");
        Equal("D7", seven.ShortcutKey, "Digit shortcut key must use the WPF D-key name.");
        KeyboardKey period = FindById(keys, "num-period");
        Equal("OemPeriod", period.ShortcutKey, "Period shortcut key must use the WPF OEM name.");
    }

    private static void LettersLayoutSupportsCaseAndPunctuation()
    {
        List<KeyboardKey> lower = Flatten(KeyboardLayouts.Create(KeyboardMode.Letters, uppercase: false));
        List<KeyboardKey> upper = Flatten(KeyboardLayouts.Create(KeyboardMode.Letters, uppercase: true));

        List<KeyboardKey> lowerLetters = lower.Where(key => key.Id.StartsWith("letter-", StringComparison.Ordinal)).ToList();
        Equal(26, lowerLetters.Count, "Letters layout must contain exactly 26 letter keys.");

        for (char letter = 'a'; letter <= 'z'; letter++)
        {
            KeyboardKey lowerKey = FindById(lower, $"letter-{letter}");
            KeyboardKey upperKey = FindById(upper, $"letter-{letter}");
            Equal(letter.ToString(), lowerKey.Value, $"Lowercase value mismatch for {letter}.");
            Equal(char.ToUpperInvariant(letter).ToString(), upperKey.Value, $"Uppercase value mismatch for {letter}.");
            Equal(char.ToUpperInvariant(letter).ToString(), lowerKey.ShortcutKey, $"Shortcut mismatch for {letter}.");
        }

        foreach (string punctuation in new[] { ",", ".", "/", ";", "'", "[", "]", "\\", "-", "=" })
        {
            Assert(Has(lower, KeyboardKeyKind.Text, punctuation), $"Missing punctuation {punctuation}.");
        }

        Assert(Has(lower, KeyboardKeyKind.Hotkey, "Tab"), "Missing Tab.");
        Assert(Has(lower, KeyboardKeyKind.Hotkey, "Space"), "Missing Space.");
        Assert(Has(lower, KeyboardKeyKind.Hotkey, "Return"), "Missing Enter.");
        Assert(Has(lower, KeyboardKeyKind.Hotkey, "Back"), "Missing Backspace.");
    }

    private static void FunctionsLayoutContainsRequiredKeys()
    {
        List<KeyboardKey> keys = Flatten(KeyboardLayouts.Create(KeyboardMode.Functions));
        string[] required =
        [
            "Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Insert", "Home", "PageUp", "Delete", "End", "PageDown",
            "Left", "Up", "Down", "Right", "Back", "Tab", "Return", "Space",
        ];

        foreach (string value in required)
        {
            Assert(Has(keys, KeyboardKeyKind.Hotkey, value), $"Missing function/navigation key {value}.");
        }
    }

    private static void FullLayoutUsesHotkeysAndModifiers()
    {
        List<KeyboardKey> keys = Flatten(KeyboardLayouts.Create(KeyboardMode.Full, uppercase: false));
        for (char letter = 'a'; letter <= 'z'; letter++)
        {
            KeyboardKey key = FindById(keys, $"full-letter-{letter}");
            Equal(KeyboardKeyKind.Hotkey, key.Kind, $"Full letter {letter} must be a hotkey.");
            Equal(char.ToUpperInvariant(letter).ToString(), key.Value, $"Full letter {letter} key value mismatch.");
            Equal(letter.ToString(), key.Label, $"Full letter {letter} label mismatch.");
        }

        Assert(FindById(keys, "full-caps").Kind == KeyboardKeyKind.Caps, "Full layout must expose CapsLock.");
        foreach (string modifier in new[] { "Shift", "Ctrl", "Alt", "Win" })
        {
            Assert(keys.Any(key => key.Kind == KeyboardKeyKind.Modifier && key.Value == modifier),
                $"Full layout must expose modifier {modifier}.");
        }

        Assert(FindById(keys, "full-space").Width > 1, "Full layout must give Space a wider key.");
        Assert(keys.Any(key => key.Id.StartsWith("full-f", StringComparison.Ordinal) && key.Value == "F12"),
            "Full layout must include F12.");
    }

    private static void ModifierLatchTogglesAndConsumesOnce()
    {
        var latch = new ModifierLatch();
        Equal(KeyboardModifiers.None, latch.Current, "Latch must start empty.");

        latch.Toggle(KeyboardModifiers.Shift | KeyboardModifiers.Ctrl);
        Equal(KeyboardModifiers.Shift | KeyboardModifiers.Ctrl, latch.Current, "Combined toggle failed.");

        latch.Toggle(KeyboardModifiers.Ctrl);
        Equal(KeyboardModifiers.Shift, latch.Current, "Second toggle failed.");

        latch.Toggle(KeyboardModifiers.Shift);
        Equal(KeyboardModifiers.None, latch.Current, "Toggling the same modifier must clear it.");

        latch.Toggle(KeyboardModifiers.Shift);
        latch.Toggle(KeyboardModifiers.Alt);
        Equal(KeyboardModifiers.Shift | KeyboardModifiers.Alt, latch.Consume(), "Consume must return the latched value.");
        Equal(KeyboardModifiers.None, latch.Current, "Consume must clear the latch.");
        Equal(KeyboardModifiers.None, latch.Consume(), "A second consume must return None.");

        latch.Toggle(KeyboardModifiers.Win);
        latch.Clear();
        Equal(KeyboardModifiers.None, latch.Current, "Clear must reset the latch.");
    }

    private static void DefaultSchemeHasTwelveEmptySlots()
    {
        KeyboardSchemes schemes = KeyboardSchemes.CreateDefault("Localized name");
        Equal(1, schemes.Items.Count, "Default scheme count mismatch.");
        Assert(!string.IsNullOrWhiteSpace(schemes.Items[0].Id), "Default scheme must have an ID.");
        Equal("Localized name", schemes.Items[0].Name, "Default scheme name mismatch.");
        Equal(KeyboardSchemes.SlotCount, schemes.Items[0].Keys.Count, "Default scheme slot count mismatch.");
        Assert(schemes.Items[0].Keys.All(binding => binding == null), "Default slots must be unbound.");
        Assert(schemes.TryValidate(out string? error), $"Default scheme should validate: {error}");
    }

    private static void SchemeRoundTripPreservesUnknownBindings()
    {
        KeyboardSchemes schemes = KeyboardSchemes.CreateDefault("Work");
        schemes.Items[0].Keys[0] = new CustomKeyBinding
        {
            Label = "Missing action",
            ActionId = "unavailable.plugin.contribution",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["path"] = @"C:\not-checked",
                ["mode"] = "unknown",
            },
        };

        string json = schemes.Serialize();
        Assert(json.Contains("unavailable.plugin.contribution", StringComparison.Ordinal),
            "Serialized JSON must preserve the unknown action ID.");
        Assert(KeyboardSchemes.TryDeserialize(json, out KeyboardSchemes? loaded, out string? error),
            $"Valid JSON should deserialize: {error}");
        CustomKeyBinding binding = loaded!.Items[0].Keys[0]!;
        Equal("unavailable.plugin.contribution", binding.ActionId, "Unknown action ID must be preserved.");
        Equal(@"C:\not-checked", binding.Parameters["path"], "Unknown parameter must be preserved.");
        Equal("unknown", binding.Parameters["mode"], "Unknown parameter mode must be preserved.");
    }

    private static void DeepCloneCopiesParameters()
    {
        KeyboardSchemes schemes = KeyboardSchemes.CreateDefault("Clone");
        schemes.Items[0].Keys[3] = new CustomKeyBinding
        {
            Label = "Copy me",
            ActionId = "unknown.action",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = "original",
            },
        };

        KeyboardSchemes clone = schemes.DeepClone();
        CustomKeyBinding original = schemes.Items[0].Keys[3]!;
        CustomKeyBinding copied = clone.Items[0].Keys[3]!;
        Assert(!ReferenceEquals(original, copied), "Deep clone must create a new binding.");
        Assert(!ReferenceEquals(original.Parameters, copied.Parameters), "Deep clone must create a new parameter dictionary.");
        copied.Parameters["value"] = "changed";
        Equal("original", original.Parameters["value"], "Mutating the clone must not mutate the source.");
    }

    private static void SchemeValidationRejectsInvalidBoundaries()
    {
        KeyboardSchemes emptyId = NewValidSchemes();
        emptyId.Items[0].Id = " ";
        AssertInvalid(emptyId, KeyboardSchemes.ErrorEmptySchemeId);

        KeyboardSchemes duplicateId = NewValidSchemes();
        duplicateId.Items.Add(NewScheme("SAME", "Second"));
        duplicateId.Items[0].Id = "same";
        AssertInvalid(duplicateId, KeyboardSchemes.ErrorDuplicateSchemeId);

        KeyboardSchemes emptyName = NewValidSchemes();
        emptyName.Items[0].Name = "";
        AssertInvalid(emptyName, KeyboardSchemes.ErrorEmptySchemeName);

        KeyboardSchemes longName = NewValidSchemes();
        longName.Items[0].Name = new string('n', KeyboardSchemes.MaxSchemeNameLength + 1);
        AssertInvalid(longName, KeyboardSchemes.ErrorSchemeNameTooLong);

        KeyboardSchemes shortSlots = NewValidSchemes();
        shortSlots.Items[0].Keys.RemoveAt(0);
        AssertInvalid(shortSlots, KeyboardSchemes.ErrorSlotCount);

        KeyboardSchemes longParameter = NewValidSchemes();
        longParameter.Items[0].Keys[0] = new CustomKeyBinding
        {
            Label = "Too long",
            ActionId = "unknown.action",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = new string('x', KeyboardSchemes.MaxParameterValueLength + 1),
            },
        };
        AssertInvalid(longParameter, KeyboardSchemes.ErrorParameterValueTooLong);

        KeyboardSchemes invalid = NewValidSchemes();
        invalid.Items[0].Keys.RemoveAt(0);
        Throws<InvalidOperationException>(() => invalid.Serialize(), "Serialize must reject invalid schemes.");
    }

    private static void SchemeLimitsAcceptAndRejectBoundaries()
    {
        KeyboardSchemes maxName = NewValidSchemes();
        maxName.Items[0].Name = new string('n', KeyboardSchemes.MaxSchemeNameLength);
        Assert(maxName.TryValidate(out string? maxNameError), $"64-character name should be valid: {maxNameError}");

        KeyboardSchemes maxParameter = NewValidSchemes();
        maxParameter.Items[0].Keys[0] = new CustomKeyBinding
        {
            Label = "Boundary",
            ActionId = "unknown.action",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["value"] = new string('x', KeyboardSchemes.MaxParameterValueLength),
            },
        };
        Assert(maxParameter.TryValidate(out string? parameterError), $"8192-character parameter should be valid: {parameterError}");

        var maxSchemes = new KeyboardSchemes();
        for (int i = 0; i < KeyboardSchemes.MaxSchemeCount; i++)
        {
            maxSchemes.Items.Add(NewScheme($"scheme-{i}", $"Scheme {i}"));
        }
        Assert(maxSchemes.TryValidate(out string? maxSchemesError), $"32 schemes should be valid: {maxSchemesError}");

        maxSchemes.Items.Add(NewScheme("scheme-extra", "Extra"));
        AssertInvalid(maxSchemes, KeyboardSchemes.ErrorTooManySchemes);
    }

    private static void DeserializationRejectsCorruptAndOversizedJson()
    {
        Assert(!KeyboardSchemes.TryDeserialize("{", out _, out string? corruptError),
            "Corrupt JSON must fail.");
        Equal(KeyboardSchemes.ErrorInvalidJson, corruptError, "Corrupt JSON error mismatch.");

        string shortSlotJson = "{\"items\":[{\"id\":\"x\",\"name\":\"x\",\"keys\":[]}]}";
        Assert(!KeyboardSchemes.TryDeserialize(shortSlotJson, out _, out string? slotError),
            "A scheme with the wrong slot count must fail.");
        Equal(KeyboardSchemes.ErrorSlotCount, slotError, "Slot count error mismatch.");

        string oversized = new('x', KeyboardSchemes.MaxJsonBytes + 1);
        Assert(!KeyboardSchemes.TryDeserialize(oversized, out _, out string? sizeError),
            "Oversized JSON must fail before parsing.");
        Equal(KeyboardSchemes.ErrorJsonTooLarge, sizeError, "Oversized JSON error mismatch.");

        Assert(!KeyboardSchemes.TryDeserialize("", out _, out string? emptyError),
            "Empty JSON must fail.");
        Equal(KeyboardSchemes.ErrorEmptyJson, emptyError, "Empty JSON error mismatch.");
    }

    private static KeyboardSchemes NewValidSchemes()
    {
        return KeyboardSchemes.CreateDefault("Valid");
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

    private static void AssertInvalid(KeyboardSchemes schemes, string expectedError)
    {
        Assert(!schemes.TryValidate(out string? error), $"Expected validation failure: {expectedError}");
        Equal(expectedError, error, "Validation error mismatch.");
    }

    private static List<KeyboardKey> Flatten(IReadOnlyList<IReadOnlyList<KeyboardKey>> layout)
    {
        return layout.SelectMany(row => row).ToList();
    }

    private static KeyboardKey FindById(IEnumerable<KeyboardKey> keys, string id)
    {
        return keys.FirstOrDefault(key => string.Equals(key.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Missing key ID {id}.");
    }

    private static bool Has(IEnumerable<KeyboardKey> keys, KeyboardKeyKind kind, string value)
    {
        return keys.Any(key => key.Kind == kind && string.Equals(key.Value, value, StringComparison.Ordinal));
    }

    private static string Signature(IEnumerable<KeyboardKey> keys)
    {
        return string.Join("|", keys.Select(key => $"{key.Id}:{key.Kind}:{key.Value}"));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

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
