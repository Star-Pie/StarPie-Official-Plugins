namespace StarPie.Plugin.TemporaryKeyboard;

internal enum KeyboardMode
{
    Numeric,
    Letters,
    Functions,
    Full,
    Custom,
}

[Flags]
internal enum KeyboardModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

internal enum KeyboardKeyKind
{
    Text,
    Hotkey,
    Modifier,
    Caps,
}

internal sealed record KeyboardKey(
    string Id,
    string Label,
    KeyboardKeyKind Kind,
    string Value,
    string? ShortcutKey = null,
    double Width = 1);

internal sealed class ModifierLatch
{
    private const KeyboardModifiers AllModifiers =
        KeyboardModifiers.Shift |
        KeyboardModifiers.Ctrl |
        KeyboardModifiers.Alt |
        KeyboardModifiers.Win;

    public KeyboardModifiers Current { get; private set; } = KeyboardModifiers.None;

    public void Toggle(KeyboardModifiers modifiers)
    {
        KeyboardModifiers normalized = modifiers & AllModifiers;
        if (normalized == KeyboardModifiers.None)
        {
            return;
        }

        Current = (Current ^ normalized) & AllModifiers;
    }

    public KeyboardModifiers Consume()
    {
        KeyboardModifiers value = Current;
        Current = KeyboardModifiers.None;
        return value;
    }

    public void Clear()
    {
        Current = KeyboardModifiers.None;
    }
}
