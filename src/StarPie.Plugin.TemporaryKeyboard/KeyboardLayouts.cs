namespace StarPie.Plugin.TemporaryKeyboard;

internal static class KeyboardLayouts
{
    public static IReadOnlyList<IReadOnlyList<KeyboardKey>> Create(
        KeyboardMode mode,
        bool uppercase = false)
    {
        return mode switch
        {
            KeyboardMode.Numeric => CreateNumeric(),
            KeyboardMode.Letters => CreateLetters(uppercase),
            KeyboardMode.Functions => CreateFunctions(),
            KeyboardMode.Full => CreateFull(uppercase),
            KeyboardMode.Custom => Array.Empty<IReadOnlyList<KeyboardKey>>(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown keyboard mode."),
        };
    }

    private static IReadOnlyList<IReadOnlyList<KeyboardKey>> CreateNumeric()
    {
        return new List<IReadOnlyList<KeyboardKey>>
        {
            Row(
                TextKey("num-7", "7", "7", "D7"),
                TextKey("num-8", "8", "8", "D8"),
                TextKey("num-9", "9", "9", "D9"),
                TextKey("num-divide", "/", "/", "Divide")),
            Row(
                TextKey("num-4", "4", "4", "D4"),
                TextKey("num-5", "5", "5", "D5"),
                TextKey("num-6", "6", "6", "D6"),
                TextKey("num-multiply", "*", "*", "Multiply")),
            Row(
                TextKey("num-1", "1", "1", "D1"),
                TextKey("num-2", "2", "2", "D2"),
                TextKey("num-3", "3", "3", "D3"),
                TextKey("num-minus", "-", "-", "OemMinus")),
            Row(
                TextKey("num-0", "0", "0", "D0"),
                TextKey("num-period", ".", ".", "OemPeriod"),
                TextKey("num-plus", "+", "+", "OemPlus"),
                HotkeyKey("num-back", "Backspace", "Back")),
            Row(HotkeyKey("num-enter", "Enter", "Return", 4)),
        };
    }

    private static IReadOnlyList<IReadOnlyList<KeyboardKey>> CreateLetters(bool uppercase)
    {
        return new List<IReadOnlyList<KeyboardKey>>
        {
            Row("qwertyuiop".Select(c => LetterKey(c, uppercase)).ToArray()),
            Row("asdfghjkl".Select(c => LetterKey(c, uppercase)).ToArray()),
            Row("zxcvbnm".Select(c => LetterKey(c, uppercase)).ToArray()),
            Row(
                TextKey("punct-open-bracket", "[", "[", "OemOpenBrackets"),
                TextKey("punct-close-bracket", "]", "]", "OemCloseBrackets"),
                TextKey("punct-backslash", "\\", "\\", "OemBackslash"),
                TextKey("punct-semicolon", ";", ";", "OemSemicolon"),
                TextKey("punct-quote", "'", "'", "OemQuotes"),
                TextKey("punct-comma", ",", ",", "OemComma"),
                TextKey("punct-period", ".", ".", "OemPeriod"),
                TextKey("punct-slash", "/", "/", "OemQuestion"),
                TextKey("punct-minus", "-", "-", "OemMinus"),
                TextKey("punct-equals", "=", "=", "OemPlus")),
            Row(
                HotkeyKey("letters-tab", "Tab", "Tab"),
                HotkeyKey("letters-space", "Space", "Space", 5),
                HotkeyKey("letters-enter", "Enter", "Return"),
                HotkeyKey("letters-back", "Backspace", "Back")),
        };
    }

    private static IReadOnlyList<IReadOnlyList<KeyboardKey>> CreateFunctions()
    {
        return new List<IReadOnlyList<KeyboardKey>>
        {
            Row(
                HotkeyKey("fn-esc", "Esc", "Escape"),
                HotkeyKey("fn-f1", "F1", "F1"),
                HotkeyKey("fn-f2", "F2", "F2"),
                HotkeyKey("fn-f3", "F3", "F3"),
                HotkeyKey("fn-f4", "F4", "F4")),
            Row(
                HotkeyKey("fn-f5", "F5", "F5"),
                HotkeyKey("fn-f6", "F6", "F6"),
                HotkeyKey("fn-f7", "F7", "F7"),
                HotkeyKey("fn-f8", "F8", "F8")),
            Row(
                HotkeyKey("fn-f9", "F9", "F9"),
                HotkeyKey("fn-f10", "F10", "F10"),
                HotkeyKey("fn-f11", "F11", "F11"),
                HotkeyKey("fn-f12", "F12", "F12")),
            Row(
                HotkeyKey("fn-insert", "Insert", "Insert"),
                HotkeyKey("fn-home", "Home", "Home"),
                HotkeyKey("fn-page-up", "PageUp", "PageUp"),
                HotkeyKey("fn-delete", "Delete", "Delete"),
                HotkeyKey("fn-end", "End", "End"),
                HotkeyKey("fn-page-down", "PageDown", "PageDown")),
            Row(
                HotkeyKey("fn-left", "Left", "Left"),
                HotkeyKey("fn-up", "Up", "Up"),
                HotkeyKey("fn-down", "Down", "Down"),
                HotkeyKey("fn-right", "Right", "Right")),
            Row(
                HotkeyKey("fn-back", "Backspace", "Back"),
                HotkeyKey("fn-tab", "Tab", "Tab"),
                HotkeyKey("fn-return", "Enter", "Return"),
                HotkeyKey("fn-space", "Space", "Space", 5)),
        };
    }

    private static IReadOnlyList<IReadOnlyList<KeyboardKey>> CreateFull(bool uppercase)
    {
        return new List<IReadOnlyList<KeyboardKey>>
        {
            Row(
                HotkeyKey("full-esc", "Esc", "Escape"),
                HotkeyKey("full-f1", "F1", "F1"),
                HotkeyKey("full-f2", "F2", "F2"),
                HotkeyKey("full-f3", "F3", "F3"),
                HotkeyKey("full-f4", "F4", "F4"),
                HotkeyKey("full-f5", "F5", "F5"),
                HotkeyKey("full-f6", "F6", "F6"),
                HotkeyKey("full-f7", "F7", "F7"),
                HotkeyKey("full-f8", "F8", "F8"),
                HotkeyKey("full-f9", "F9", "F9"),
                HotkeyKey("full-f10", "F10", "F10"),
                HotkeyKey("full-f11", "F11", "F11"),
                HotkeyKey("full-f12", "F12", "F12")),
            Row(
                HotkeyKey("full-1", "1", "D1"),
                HotkeyKey("full-2", "2", "D2"),
                HotkeyKey("full-3", "3", "D3"),
                HotkeyKey("full-4", "4", "D4"),
                HotkeyKey("full-5", "5", "D5"),
                HotkeyKey("full-6", "6", "D6"),
                HotkeyKey("full-7", "7", "D7"),
                HotkeyKey("full-8", "8", "D8"),
                HotkeyKey("full-9", "9", "D9"),
                HotkeyKey("full-0", "0", "D0"),
                HotkeyKey("full-minus", "-", "OemMinus"),
                HotkeyKey("full-equals", "=", "OemPlus"),
                HotkeyKey("full-back", "Backspace", "Back")),
            Row(
                HotkeyKey("full-tab", "Tab", "Tab"),
                FullLetter('q', uppercase),
                FullLetter('w', uppercase),
                FullLetter('e', uppercase),
                FullLetter('r', uppercase),
                FullLetter('t', uppercase),
                FullLetter('y', uppercase),
                FullLetter('u', uppercase),
                FullLetter('i', uppercase),
                FullLetter('o', uppercase),
                FullLetter('p', uppercase),
                HotkeyKey("full-open-bracket", "[", "OemOpenBrackets"),
                HotkeyKey("full-close-bracket", "]", "OemCloseBrackets"),
                HotkeyKey("full-backslash", "\\", "OemBackslash")),
            Row(
                CapsKey("full-caps"),
                FullLetter('a', uppercase),
                FullLetter('s', uppercase),
                FullLetter('d', uppercase),
                FullLetter('f', uppercase),
                FullLetter('g', uppercase),
                FullLetter('h', uppercase),
                FullLetter('j', uppercase),
                FullLetter('k', uppercase),
                FullLetter('l', uppercase),
                HotkeyKey("full-semicolon", ";", "OemSemicolon"),
                HotkeyKey("full-quote", "'", "OemQuotes"),
                HotkeyKey("full-return", "Enter", "Return")),
            Row(
                ModifierKey("full-shift-left", "Shift", KeyboardModifiers.Shift),
                FullLetter('z', uppercase),
                FullLetter('x', uppercase),
                FullLetter('c', uppercase),
                FullLetter('v', uppercase),
                FullLetter('b', uppercase),
                FullLetter('n', uppercase),
                FullLetter('m', uppercase),
                HotkeyKey("full-comma", ",", "OemComma"),
                HotkeyKey("full-period", ".", "OemPeriod"),
                HotkeyKey("full-slash", "/", "OemQuestion"),
                ModifierKey("full-shift-right", "Shift", KeyboardModifiers.Shift)),
            Row(
                ModifierKey("full-ctrl-left", "Ctrl", KeyboardModifiers.Ctrl),
                ModifierKey("full-win-left", "Win", KeyboardModifiers.Win),
                ModifierKey("full-alt-left", "Alt", KeyboardModifiers.Alt),
                HotkeyKey("full-space", "Space", "Space", 5),
                ModifierKey("full-alt-right", "Alt", KeyboardModifiers.Alt),
                ModifierKey("full-ctrl-right", "Ctrl", KeyboardModifiers.Ctrl)),
            Row(
                HotkeyKey("full-insert", "Insert", "Insert"),
                HotkeyKey("full-home", "Home", "Home"),
                HotkeyKey("full-page-up", "PageUp", "PageUp"),
                HotkeyKey("full-delete", "Delete", "Delete"),
                HotkeyKey("full-end", "End", "End"),
                HotkeyKey("full-page-down", "PageDown", "PageDown")),
            Row(
                HotkeyKey("full-left", "Left", "Left"),
                HotkeyKey("full-up", "Up", "Up"),
                HotkeyKey("full-down", "Down", "Down"),
                HotkeyKey("full-right", "Right", "Right")),
        };
    }

    private static KeyboardKey LetterKey(char letter, bool uppercase)
    {
        char lower = char.ToLowerInvariant(letter);
        string value = uppercase
            ? char.ToUpperInvariant(lower).ToString()
            : lower.ToString();
        return TextKey($"letter-{lower}", value, value, char.ToUpperInvariant(lower).ToString());
    }

    private static KeyboardKey FullLetter(char letter, bool uppercase)
    {
        char lower = char.ToLowerInvariant(letter);
        string label = uppercase
            ? char.ToUpperInvariant(lower).ToString()
            : lower.ToString();
        return HotkeyKey($"full-letter-{lower}", label, char.ToUpperInvariant(lower).ToString());
    }

    private static KeyboardKey TextKey(string id, string label, string value, string shortcutKey)
    {
        return new KeyboardKey(id, label, KeyboardKeyKind.Text, value, shortcutKey);
    }

    private static KeyboardKey HotkeyKey(string id, string label, string value, double width = 1)
    {
        return new KeyboardKey(id, label, KeyboardKeyKind.Hotkey, value, null, width);
    }

    private static KeyboardKey ModifierKey(string id, string label, KeyboardModifiers modifier)
    {
        return new KeyboardKey(id, label, KeyboardKeyKind.Modifier, modifier.ToString());
    }

    private static KeyboardKey CapsKey(string id)
    {
        return new KeyboardKey(id, "Caps", KeyboardKeyKind.Caps, "CapsLock");
    }

    private static IReadOnlyList<KeyboardKey> Row(params KeyboardKey[] keys)
    {
        return keys;
    }
}
