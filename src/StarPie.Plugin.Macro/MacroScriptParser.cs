using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StarPie.Plugin.Macro;

internal enum MacroStepKind
{
    Hotkey,
    Key,
    Text,
    Paste,
    Clipboard,
    Wait,
}

internal readonly record struct MacroTypingUnit(bool IsKey, string Value);

internal readonly record struct MacroStep(
    int LineNumber,
    MacroStepKind Kind,
    string Value,
    int WaitMs = 0,
    int Repeat = 1,
    int CharDelayMs = MacroScriptParser.DefaultCharDelayMs)
{
    public string ToPreviewToken() => Kind switch
    {
        MacroStepKind.Hotkey or MacroStepKind.Key => Repeat > 1 ? $"{Value}×{Repeat}" : Value,
        MacroStepKind.Wait => $"{WaitMs}ms",
        MacroStepKind.Text => $"\"{ShortenText(Value, 10)}\"",
        MacroStepKind.Paste => $"Paste({ShortenText(Value, 8)})",
        MacroStepKind.Clipboard => $"Copy({ShortenText(Value, 8)})",
        _ => Value,
    };

    private static string ShortenText(string text, int maxLen)
    {
        string clean = (text ?? "").Replace("\r", " ").Replace("\n", " ");
        return clean.Length <= maxLen ? clean : clean.Substring(0, maxLen - 1) + "…";
    }
}

internal sealed class MacroParseResult
{
    public bool IsValid => ErrorMessage == null;
    public IReadOnlyList<MacroStep> Steps { get; init; } = Array.Empty<MacroStep>();
    public int TotalExplicitWaitMs { get; init; }
    public int TotalActionCount { get; init; }
    public string? ErrorMessage { get; init; }

    public static MacroParseResult Fail(string message) => new() { ErrorMessage = message };
}

/// <summary>
/// 轻量级、无正则回溯风险的按键宏 DSL 解析器与静态语法校验器。
/// </summary>
internal static class MacroScriptParser
{
    public const int MaxSteps = 100;
    public const int MaxSingleWaitMs = 5000;
    public const int MaxSingleRepeat = 20;
    public const int MaxTotalWaitMs = 30000;
    public const int DefaultCharDelayMs = 35;
    public const int MaxCharDelayMs = 1000;

    private static readonly HashSet<string> ValidModifierTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctrl", "control", "lctrl", "rctrl",
        "shift", "lshift", "rshift",
        "alt", "menu", "lalt", "ralt",
        "win", "lwin", "rwin", "windows",
    };

    private static readonly HashSet<string> ValidNamedKeyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "up", "right", "down",
        "home", "end",
        "pgup", "prior", "pageup",
        "pgdn", "next", "pagedown",
        "ins", "insert",
        "del", "delete",
        "back", "backspace",
        "tab",
        "enter", "return",
        "esc", "escape",
        "space", "spacebar",
        "prtscn", "prtsc", "prntscrn", "snapshot", "printscreen", "print_screen", "print screen", "print",
        "pause", "capslock", "scrolllock", "numlock",
        "plus", "minus", "comma", "dot", "period", "slash", "backslash",
        "semicolon", "quote", "bracketleft", "openbracket", "bracketright", "closebracket",
        "tilde", "backquote",
        "volumeup", "volumedown", "mute", "volumemute",
        "playpause", "mediaplaypause", "nexttrack", "medianext", "mediaprev", "prevtrack", "stopmedia", "mediastop",
        "browserback", "browserforward", "browserrefresh", "browserstop", "browsersearch", "browserfavorites", "browserhome",
    };

    private static readonly HashSet<char> ValidSingleCharPunctuation = new()
    {
        ';', '+', '=', ',', '-', '.', '/', '`', '[', '\\', ']', '\''
    };

    public static MacroParseResult Parse(string? script, Func<string, string, string>? localize = null)
    {
        string L(string key, string fallback) => localize != null ? localize(key, fallback) : fallback;

        if (string.IsNullOrWhiteSpace(script))
        {
            return MacroParseResult.Fail(L("macro.err.empty", Texts.ErrEmptyScript));
        }

        string[] lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var steps = new List<MacroStep>();
        int totalExplicitWaitMs = 0;
        int totalActionCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNum = i + 1;
            string rawLine = lines[i];
            string trimmed = rawLine.Trim();

            if (trimmed.Length == 0 ||
                trimmed.StartsWith('#') ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith(';'))
            {
                continue;
            }

            IReadOnlyList<string> segments = SplitArrowSegmentsIfApplicable(trimmed);
            for (int segIdx = 0; segIdx < segments.Count; segIdx++)
            {
                string segment = segments[segIdx].Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                MacroParseResult? segError = ParseSingleSegment(
                    segment,
                    lineNum,
                    L,
                    steps,
                    ref totalExplicitWaitMs,
                    ref totalActionCount);

                if (segError != null)
                {
                    return segError;
                }

                if (steps.Count > MaxSteps)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.tooManySteps", Texts.ErrTooManySteps),
                        steps.Count,
                        MaxSteps));
                }
            }
        }

        if (steps.Count == 0)
        {
            return MacroParseResult.Fail(L("macro.err.empty", Texts.ErrEmptyScript));
        }

        return new MacroParseResult
        {
            Steps = steps,
            TotalExplicitWaitMs = totalExplicitWaitMs,
            TotalActionCount = totalActionCount,
        };
    }

    public static string FormatSteps(IReadOnlyList<MacroStep> steps)
    {
        if (steps == null || steps.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            MacroStep step = steps[i];
            int repeat = Math.Clamp(step.Repeat, 1, MaxSingleRepeat);
            string repeatSuffix = repeat > 1
                ? " * " + repeat.ToString(CultureInfo.InvariantCulture)
                : "";

            switch (step.Kind)
            {
                case MacroStepKind.Hotkey:
                case MacroStepKind.Key:
                {
                    string keyVal = string.IsNullOrWhiteSpace(step.Value) ? "Enter" : step.Value.Trim();
                    string prefix = IsSingleKeyExpression(keyVal) ? "KEY" : "HOTKEY";
                    sb.Append(prefix).Append(": ").Append(keyVal).Append(repeatSuffix);
                    break;
                }

                case MacroStepKind.Wait:
                {
                    int waitMs = Math.Clamp(step.WaitMs, 0, MaxSingleWaitMs);
                    sb.Append("WAIT: ").Append(waitMs.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                case MacroStepKind.Paste:
                    sb.Append("PASTE: ").Append(FormatTextArgument(step.Value));
                    break;

                case MacroStepKind.Text:
                {
                    int charDelay = Math.Clamp(step.CharDelayMs, 0, MaxCharDelayMs);
                    if (charDelay == DefaultCharDelayMs)
                    {
                        sb.Append("TEXT: ").Append(FormatTextArgument(step.Value));
                    }
                    else
                    {
                        sb.Append("TEXT@")
                          .Append(charDelay.ToString(CultureInfo.InvariantCulture))
                          .Append(": ")
                          .Append(FormatTextArgument(step.Value));
                    }
                    break;
                }

                case MacroStepKind.Clipboard:
                    sb.Append("CLIPBOARD: ").Append(FormatTextArgument(step.Value));
                    break;
            }
        }

        return sb.ToString();
    }

    internal static IReadOnlyList<MacroTypingUnit> DecomposeTypingUnits(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<MacroTypingUnit>();
        }

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var units = new List<MacroTypingUnit>(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (c == '\n')
            {
                units.Add(new MacroTypingUnit(IsKey: true, Value: "Enter"));
            }
            else if (c == '\t')
            {
                units.Add(new MacroTypingUnit(IsKey: true, Value: "Tab"));
            }
            else if (char.IsHighSurrogate(c) && i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
            {
                units.Add(new MacroTypingUnit(IsKey: false, Value: normalized.Substring(i, 2)));
                i++;
            }
            else
            {
                units.Add(new MacroTypingUnit(IsKey: false, Value: c.ToString()));
            }
        }

        return units;
    }

    internal static bool IsSingleKeyExpression(string keyExpr)
    {
        string trimmed = (keyExpr ?? "").Trim();
        return trimmed.Length > 0 &&
               !ContainsMultipleKeysForKeyDirective(trimmed) &&
               IsValidKeyOrModifierToken(trimmed);
    }

    private static string FormatTextArgument(string? value)
    {
        string text = value ?? "";
        if (text.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes =
            char.IsWhiteSpace(text[0]) ||
            char.IsWhiteSpace(text[text.Length - 1]) ||
            text.Contains('"') ||
            text.Contains('\\') ||
            text.Contains('\r') ||
            text.Contains('\n') ||
            text.Contains('\t') ||
            text.Contains('#') ||
            text.Contains("//", StringComparison.Ordinal) ||
            text.Contains("->", StringComparison.Ordinal) ||
            text.Contains('➔') ||
            text.Contains('»');

        if (!needsQuotes)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 4);
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static IReadOnlyList<string> SplitArrowSegmentsIfApplicable(string trimmedLine)
    {
        int firstColon = FindColonIndexOutsideQuotes(trimmedLine);
        if (firstColon > 0)
        {
            string headDirective = trimmedLine.Substring(0, firstColon).Trim();
            if (TryMapDirectiveWithOptions(headDirective, out MacroStepKind headKind, out _, out _) &&
                (headKind == MacroStepKind.Text || headKind == MacroStepKind.Paste || headKind == MacroStepKind.Clipboard))
            {
                // Preserve literal arrows inside unquoted TEXT/PASTE/CLIPBOARD lines
                return new[] { trimmedLine };
            }
        }

        string cleanLine = StripInlineCommentOutsideQuotes(trimmedLine).Trim();
        if (!ContainsArrowOutsideQuotes(cleanLine))
        {
            return new[] { trimmedLine };
        }

        var result = new List<string>();
        bool inQuotes = false;
        int segStart = 0;

        for (int i = 0; i < cleanLine.Length; i++)
        {
            char c = cleanLine[i];
            if (c == '\\' && inQuotes && i + 1 < cleanLine.Length)
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes)
            {
                if (c == '-' && i + 1 < cleanLine.Length && cleanLine[i + 1] == '>')
                {
                    result.Add(cleanLine.Substring(segStart, i - segStart));
                    i++;
                    segStart = i + 1;
                }
                else if (c == '➔' || c == '»')
                {
                    result.Add(cleanLine.Substring(segStart, i - segStart));
                    segStart = i + 1;
                }
            }
        }

        if (segStart <= cleanLine.Length)
        {
            result.Add(cleanLine.Substring(segStart));
        }

        return result;
    }

    private static bool ContainsArrowOutsideQuotes(string text)
    {
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && inQuotes && i + 1 < text.Length)
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (!inQuotes)
            {
                if ((c == '-' && i + 1 < text.Length && text[i + 1] == '>') || c == '➔' || c == '»')
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string StripInlineCommentOutsideQuotes(string raw)
    {
        bool inQuotes = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\\' && inQuotes && i + 1 < raw.Length)
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (!inQuotes)
            {
                if (c == '#' && (i == 0 || char.IsWhiteSpace(raw[i - 1])))
                {
                    return raw.Substring(0, i);
                }
                if (c == '/' && i + 1 < raw.Length && raw[i + 1] == '/' && (i == 0 || char.IsWhiteSpace(raw[i - 1])))
                {
                    return raw.Substring(0, i);
                }
            }
        }
        return raw;
    }

    private static MacroParseResult? ParseSingleSegment(
        string trimmed,
        int lineNum,
        Func<string, string, string> L,
        List<MacroStep> steps,
        ref int totalExplicitWaitMs,
        ref int totalActionCount)
    {
        int colonIdx = FindColonIndexOutsideQuotes(trimmed);
        MacroStepKind kind;
        string directive;
        string rawRemainder;
        int charDelayMs = DefaultCharDelayMs;

        if (colonIdx <= 0)
        {
            string withoutComment = StripInlineCommentOutsideQuotes(trimmed).Trim();
            if (withoutComment.Length == 0)
            {
                return null;
            }

            string firstWord = ExtractFirstWord(withoutComment);
            if (TryMapDirectiveWithOptions(firstWord, out _, out _, out _))
            {
                return MacroParseResult.Fail(string.Format(
                    CultureInfo.InvariantCulture,
                    L("macro.err.missingColon", Texts.ErrMissingColon),
                    lineNum));
            }

            if (withoutComment[0] == '"')
            {
                kind = MacroStepKind.Paste;
                directive = "PASTE";
                rawRemainder = withoutComment;
            }
            else if (withoutComment.Length > 2 &&
                     withoutComment.EndsWith("ms", StringComparison.OrdinalIgnoreCase) &&
                     (char.IsDigit(withoutComment[0]) || withoutComment[0] == '-'))
            {
                kind = MacroStepKind.Wait;
                directive = "WAIT";
                rawRemainder = withoutComment;
            }
            else
            {
                if (!TryExtractRepeatSuffix(withoutComment, out string shortKeyExpr, out _, out _))
                {
                    kind = MacroStepKind.Key;
                }
                else
                {
                    kind = IsSingleKeyExpression(shortKeyExpr) ? MacroStepKind.Key : MacroStepKind.Hotkey;
                }
                directive = kind == MacroStepKind.Key ? "KEY" : "HOTKEY";
                rawRemainder = withoutComment;
            }
        }
        else
        {
            directive = trimmed.Substring(0, colonIdx).Trim();
            rawRemainder = trimmed.Substring(colonIdx + 1);

            if (!TryMapDirectiveWithOptions(directive, out kind, out charDelayMs, out string? badDelayRaw))
            {
                if (badDelayRaw != null)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.invalidWait", Texts.ErrInvalidWait),
                        lineNum,
                        badDelayRaw,
                        MaxCharDelayMs));
                }

                return MacroParseResult.Fail(string.Format(
                    CultureInfo.InvariantCulture,
                    L("macro.err.unknownDirective", Texts.ErrUnknownDirective),
                    lineNum,
                    directive));
            }
        }

        switch (kind)
        {
            case MacroStepKind.Wait:
            {
                string arg = StripInlineComment(rawRemainder).Trim();
                if (arg.Length == 0)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.emptyArg", Texts.ErrEmptyArgument),
                        lineNum,
                        directive));
                }

                string numericPart = arg.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                    ? arg.Substring(0, arg.Length - 2).Trim()
                    : arg;

                if (!int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int waitMs) ||
                    waitMs < 0 || waitMs > MaxSingleWaitMs)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.invalidWait", Texts.ErrInvalidWait),
                        lineNum,
                        arg,
                        MaxSingleWaitMs));
                }

                totalExplicitWaitMs += waitMs;
                if (totalExplicitWaitMs > MaxTotalWaitMs)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.totalWaitExceeded", Texts.ErrTotalWaitExceeded),
                        totalExplicitWaitMs,
                        MaxTotalWaitMs));
                }

                steps.Add(new MacroStep(lineNum, MacroStepKind.Wait, $"{waitMs}ms", WaitMs: waitMs, Repeat: 1));
                return null;
            }

            case MacroStepKind.Hotkey:
            case MacroStepKind.Key:
            {
                string arg = StripInlineComment(rawRemainder).Trim();
                if (arg.Length == 0)
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.emptyArg", Texts.ErrEmptyArgument),
                        lineNum,
                        directive));
                }

                if (!TryExtractRepeatSuffix(arg, out string keyExpr, out int repeat, out string? invalidRepeatRaw))
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.invalidRepeat", Texts.ErrInvalidRepeat),
                        lineNum,
                        invalidRepeatRaw ?? arg,
                        MaxSingleRepeat));
                }

                if (kind == MacroStepKind.Key)
                {
                    if (ContainsMultipleKeysForKeyDirective(keyExpr))
                    {
                        return MacroParseResult.Fail(string.Format(
                            CultureInfo.InvariantCulture,
                            L("macro.err.singleKeyExpected", Texts.ErrSingleKeyExpected),
                            lineNum,
                            keyExpr));
                    }

                    if (!IsValidKeyOrModifierToken(keyExpr))
                    {
                        return MacroParseResult.Fail(string.Format(
                            CultureInfo.InvariantCulture,
                            L("macro.err.invalidKey", Texts.ErrInvalidKeyToken),
                            lineNum,
                            keyExpr));
                    }
                }
                else
                {
                    if (!ValidateHotkeyExpression(keyExpr, out string? badToken))
                    {
                        return MacroParseResult.Fail(string.Format(
                            CultureInfo.InvariantCulture,
                            L("macro.err.invalidKey", Texts.ErrInvalidKeyToken),
                            lineNum,
                            badToken ?? keyExpr));
                    }
                }

                totalActionCount += repeat;
                steps.Add(new MacroStep(lineNum, kind, keyExpr, WaitMs: 0, Repeat: repeat));
                return null;
            }

            case MacroStepKind.Text:
            case MacroStepKind.Paste:
            case MacroStepKind.Clipboard:
            {
                if (!TryParseTextArgument(rawRemainder, out string parsedText))
                {
                    return MacroParseResult.Fail(string.Format(
                        CultureInfo.InvariantCulture,
                        L("macro.err.emptyArg", Texts.ErrEmptyArgument),
                        lineNum,
                        directive));
                }

                totalActionCount++;
                steps.Add(new MacroStep(
                    lineNum,
                    kind,
                    parsedText,
                    WaitMs: 0,
                    Repeat: 1,
                    CharDelayMs: kind == MacroStepKind.Text ? charDelayMs : DefaultCharDelayMs));
                return null;
            }
        }

        return null;
    }

    private static string ExtractFirstWord(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return text.Substring(0, i);
            }
        }
        return text;
    }

    private static int FindColonIndexOutsideQuotes(string line)
    {
        if (line.Length > 0 && line[0] == '"')
        {
            return -1;
        }

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ':' || line[i] == '：')
            {
                return i;
            }
        }
        return -1;
    }

    private static bool TryMapDirectiveWithOptions(
        string directive,
        out MacroStepKind kind,
        out int charDelayMs,
        out string? badDelayRaw)
    {
        charDelayMs = DefaultCharDelayMs;
        badDelayRaw = null;

        int atIdx = directive.IndexOf('@');
        if (atIdx > 0)
        {
            string baseDir = directive.Substring(0, atIdx).Trim();
            string rawOption = directive.Substring(atIdx + 1).Trim();
            if (!TryMapDirective(baseDir, out kind) || kind != MacroStepKind.Text)
            {
                kind = default;
                return false;
            }

            string numStr = rawOption.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                ? rawOption.Substring(0, rawOption.Length - 2).Trim()
                : rawOption;

            if (!int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDelay) ||
                parsedDelay < 0 || parsedDelay > MaxCharDelayMs)
            {
                badDelayRaw = rawOption;
                return false;
            }

            charDelayMs = parsedDelay;
            return true;
        }

        return TryMapDirective(directive, out kind);
    }

    private static bool TryMapDirective(string directive, out MacroStepKind kind)
    {
        switch (directive.ToLowerInvariant())
        {
            case "hotkey":
            case "hk":
            case "shortcut":
            case "组合键":
            case "快捷键":
                kind = MacroStepKind.Hotkey;
                return true;

            case "key":
            case "press":
            case "按键":
            case "单键":
                kind = MacroStepKind.Key;
                return true;

            case "text":
            case "type":
            case "input":
            case "文本":
            case "输入":
            case "逐字输入":
                kind = MacroStepKind.Text;
                return true;

            case "paste":
            case "粘贴":
            case "粘贴指定文本":
                kind = MacroStepKind.Paste;
                return true;

            case "clipboard":
            case "copy":
            case "剪贴板":
            case "复制指定文本":
                kind = MacroStepKind.Clipboard;
                return true;

            case "wait":
            case "sleep":
            case "delay":
            case "延时":
            case "等待":
                kind = MacroStepKind.Wait;
                return true;

            default:
                kind = default;
                return false;
        }
    }

    private static string StripInlineComment(string raw)
    {
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#')
            {
                if (i == 0 || char.IsWhiteSpace(raw[i - 1]))
                {
                    return raw.Substring(0, i);
                }
            }
            else if (raw[i] == '/' && i + 1 < raw.Length && raw[i + 1] == '/')
            {
                if (i == 0 || char.IsWhiteSpace(raw[i - 1]))
                {
                    return raw.Substring(0, i);
                }
            }
        }
        return raw;
    }

    private static bool TryParseTextArgument(string rawRemainder, out string result)
    {
        result = "";
        string trimmedStart = rawRemainder.TrimStart();
        if (trimmedStart.Length == 0)
        {
            return false;
        }

        if (trimmedStart[0] == '"')
        {
            var sb = new StringBuilder();
            bool closed = false;
            int i = 1;
            for (; i < trimmedStart.Length; i++)
            {
                char c = trimmedStart[i];
                if (c == '\\' && i + 1 < trimmedStart.Length)
                {
                    char next = trimmedStart[++i];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => next,
                    });
                }
                else if (c == '"')
                {
                    closed = true;
                    i++;
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (!closed)
            {
                return false;
            }

            string trailing = StripInlineComment(trimmedStart.Substring(i)).Trim();
            if (trailing.Length > 0)
            {
                return false;
            }

            result = sb.ToString();
            return result.Length > 0;
        }

        string unquoted = StripInlineComment(trimmedStart).TrimEnd();
        if (unquoted.Length == 0)
        {
            return false;
        }

        result = unquoted;
        return true;
    }

    private static bool TryExtractRepeatSuffix(
        string arg,
        out string keyExpr,
        out int repeat,
        out string? invalidRepeatRaw)
    {
        keyExpr = arg;
        repeat = 1;
        invalidRepeatRaw = null;

        // 1. Check '*' multiplier when there is a non-empty key expression before '*'
        int starIdx = arg.LastIndexOf('*');
        if (starIdx > 0)
        {
            string left = arg.Substring(0, starIdx).Trim();
            string right = arg.Substring(starIdx + 1).Trim();
            // Beware of "Ctrl+*" where left ends with '+'
            if (left.Length > 0 && !left.EndsWith('+'))
            {
                if (!int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ||
                    r < 1 || r > MaxSingleRepeat)
                {
                    invalidRepeatRaw = right.Length == 0 ? "*" : right;
                    return false;
                }

                keyExpr = left;
                repeat = r;
                return true;
            }
        }

        // 2. Check ' x <N>' or ' X <N>' multiplier when preceded and followed by whitespace/digits
        for (int i = arg.Length - 1; i >= 1; i--)
        {
            if ((arg[i] == 'x' || arg[i] == 'X') && char.IsWhiteSpace(arg[i - 1]))
            {
                string left = arg.Substring(0, i).Trim();
                string right = arg.Substring(i + 1).Trim();
                if (left.Length > 0 && right.Length > 0 && !left.EndsWith('+'))
                {
                    if (!int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ||
                        r < 1 || r > MaxSingleRepeat)
                    {
                        invalidRepeatRaw = right;
                        return false;
                    }

                    keyExpr = left;
                    repeat = r;
                    return true;
                }
            }
        }

        return true;
    }

    private static bool ContainsMultipleKeysForKeyDirective(string keyExpr)
    {
        string trimmed = keyExpr.Trim();
        if (trimmed.Length <= 1 || IsValidKeyOrModifierToken(trimmed))
        {
            return false;
        }

        return trimmed.Contains('+') || trimmed.Contains(',') || trimmed.Contains(';') || trimmed.Contains(' ');
    }

    private static bool ValidateHotkeyExpression(string hotkeyExpr, out string? badToken)
    {
        badToken = null;
        string trimmed = hotkeyExpr.Trim();
        if (trimmed.Length == 0)
        {
            badToken = hotkeyExpr;
            return false;
        }

        string[] steps = trimmed.Split(new[] { ',', ';' }, StringSplitOptions.None);
        foreach (string rawStep in steps)
        {
            string step = rawStep.Trim();
            if (step.Length == 0)
            {
                badToken = hotkeyExpr;
                return false;
            }

            if (IsValidKeyOrModifierToken(step))
            {
                continue;
            }

            if (step.StartsWith('+') || (step.EndsWith('+') && step.Length > 1 && !step.EndsWith("++", StringComparison.Ordinal)))
            {
                badToken = step;
                return false;
            }

            // Replace "print screen" with "printscreen" before splitting on '+' / ' '
            string normalizedStep = ReplacePrintScreen(step);
            string[] tokens = normalizedStep.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                badToken = step;
                return false;
            }

            foreach (string token in tokens)
            {
                if (!IsValidKeyOrModifierToken(token))
                {
                    badToken = token;
                    return false;
                }
            }
        }

        return true;
    }

    private static string ReplacePrintScreen(string input)
    {
        int idx = input.IndexOf("print screen", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return input;
        }

        return input.Substring(0, idx) + "printscreen" + input.Substring(idx + "print screen".Length);
    }

    internal static bool IsValidKeyOrModifierToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string text = token.Trim().ToLowerInvariant();
        if (ValidModifierTokens.Contains(text) || ValidNamedKeyTokens.Contains(text))
        {
            return true;
        }

        if (text.Length == 1)
        {
            char c = text[0];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || ValidSingleCharPunctuation.Contains(c))
            {
                return true;
            }
            return false;
        }

        // D0 ~ D9
        if (text.Length == 2 && text[0] == 'd' && char.IsDigit(text[1]))
        {
            return true;
        }

        // F1 ~ F24
        if (text.StartsWith('f') &&
            int.TryParse(text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int fNum) &&
            fNum >= 1 && fNum <= 24)
        {
            return true;
        }

        // NumPad / Num keys
        if (text.StartsWith("numpad", StringComparison.Ordinal) || text.StartsWith("num", StringComparison.Ordinal))
        {
            string suffix = text.StartsWith("numpad", StringComparison.Ordinal)
                ? text.Substring(6)
                : text.Substring(3);

            if (suffix.Length == 1 && suffix[0] >= '0' && suffix[0] <= '9')
            {
                return true;
            }

            return suffix is "add" or "plus" or "+"
                or "subtract" or "minus" or "-"
                or "multiply" or "star" or "*"
                or "divide" or "slash" or "/"
                or "decimal" or "dot" or ".";
        }

        return false;
    }
}
