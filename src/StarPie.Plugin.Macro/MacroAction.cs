using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Macro;

internal sealed class MacroAction : IActionContribution
{
    public const int DefaultPreDelayMs = 50;
    public const int MaxPreDelayMs = 1000;

    public const int DefaultStepDelayMs = 30;
    public const int MaxStepDelayMs = 500;

    public const int DefaultRepeatCount = 1;
    public const int MaxRepeatCount = 20;

    private readonly IPluginContext _context;
    private readonly string _iconKey;

    /// <summary>
    /// 供无副作用回归测试注入的等待钩子；默认使用可响应取消的 WaitHandle.WaitOne。
    /// </summary>
    internal Action<int, CancellationToken>? DelayOverride { get; set; }

    public MacroAction(IPluginContext context, string iconKey)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _iconKey = iconKey ?? "";
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = "macro",
        DisplayName = _context.I18n.T("macro.title", Texts.ActionTitle),
        DisplayNameKey = "macro.title",
        Description = _context.I18n.T("macro.desc", Texts.ActionDesc),
        Category = _context.I18n.T("macro.category", Texts.ActionCategory),
        IconKey = _iconKey,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 35,
    };

    public IReadOnlyList<ParameterField> Parameters => new[]
    {
        new ParameterField
        {
            Key = "script",
            Label = _context.I18n.T("macro.script", Texts.FieldScript),
            LabelKey = "macro.script",
            Type = ParameterFieldType.MultilineText,
            Required = true,
            DefaultValue = "HOTKEY: Alt+F\nWAIT: 100\nKEY: E",
            Placeholder = Texts.FieldScriptPlaceholder,
            HelpText = _context.I18n.T("macro.scriptHelp", Texts.FieldScriptHelp),
        },
        new ParameterField
        {
            Key = "preDelayMs",
            Label = _context.I18n.T("macro.preDelay", Texts.FieldPreDelay),
            LabelKey = "macro.preDelay",
            Type = ParameterFieldType.Number,
            Required = false,
            DefaultValue = "50",
            Min = 0,
            Max = MaxPreDelayMs,
            Placeholder = "50",
            HelpText = _context.I18n.T("macro.preDelayHelp", Texts.FieldPreDelayHelp),
        },
        new ParameterField
        {
            Key = "stepDelayMs",
            Label = _context.I18n.T("macro.stepDelay", Texts.FieldStepDelay),
            LabelKey = "macro.stepDelay",
            Type = ParameterFieldType.Number,
            Required = false,
            DefaultValue = "30",
            Min = 0,
            Max = MaxStepDelayMs,
            Placeholder = "30",
            HelpText = _context.I18n.T("macro.stepDelayHelp", Texts.FieldStepDelayHelp),
        },
        new ParameterField
        {
            Key = "repeatCount",
            Label = _context.I18n.T("macro.repeatCount", Texts.FieldRepeatCount),
            LabelKey = "macro.repeatCount",
            Type = ParameterFieldType.Number,
            Required = false,
            DefaultValue = "1",
            Min = 1,
            Max = MaxRepeatCount,
            Placeholder = "1",
            HelpText = _context.I18n.T("macro.repeatCountHelp", Texts.FieldRepeatCountHelp),
        },
    };

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        MacroVisualBuilder.RequestAttach(_context.I18n.T);

        if (!TryParseOptions(parameters, out int preDelayMs, out int stepDelayMs, out int repeatCount, out string? optionError))
        {
            return optionError;
        }

        string? script = parameters != null && parameters.TryGetValue("script", out string? rawScript)
            ? rawScript
            : null;

        MacroParseResult parsed = MacroScriptParser.Parse(script, _context.I18n.T);
        if (!parsed.IsValid)
        {
            return parsed.ErrorMessage;
        }

        long totalWaitMs = CalculateTotalWaitMs(parsed.Steps, preDelayMs, stepDelayMs, repeatCount);
        if (totalWaitMs > MacroScriptParser.MaxTotalWaitMs)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("macro.err.totalWaitExceeded", Texts.ErrTotalWaitExceeded),
                totalWaitMs,
                MacroScriptParser.MaxTotalWaitMs);
        }

        return null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        MacroVisualBuilder.RequestAttach(_context.I18n.T);

        string? script = parameters != null && parameters.TryGetValue("script", out string? rawScript)
            ? rawScript
            : null;

        MacroParseResult parsed = MacroScriptParser.Parse(script, _context.I18n.T);
        if (!parsed.IsValid || parsed.Steps.Count == 0)
        {
            return _context.I18n.T("macro.title", Texts.ActionTitle);
        }

        int repeatCount = DefaultRepeatCount;
        if (parameters != null &&
            parameters.TryGetValue("repeatCount", out string? rawRepeat) &&
            !string.IsNullOrWhiteSpace(rawRepeat) &&
            int.TryParse(rawRepeat.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) &&
            r >= 1 && r <= MaxRepeatCount)
        {
            repeatCount = r;
        }

        string joined = string.Join(" ➔ ", parsed.Steps.Select(s => s.ToPreviewToken()));
        string suffix = repeatCount > 1 ? $" (×{repeatCount})" : "";
        string full = joined + suffix;

        const int maxPreviewLen = 48;
        if (full.Length <= maxPreviewLen)
        {
            return full;
        }

        if (suffix.Length > 0 && suffix.Length < maxPreviewLen - 4)
        {
            int headBudget = maxPreviewLen - suffix.Length - 1;
            return joined.Substring(0, headBudget) + "…" + suffix;
        }

        return full.Substring(0, maxPreviewLen - 1) + "…";
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, string> parameters = input.Parameters;
        if (!TryParseOptions(parameters, out int preDelayMs, out int stepDelayMs, out int repeatCount, out string? optionError))
        {
            return Task.FromResult(ActionResult.Fail(optionError!));
        }

        string? script = parameters != null && parameters.TryGetValue("script", out string? rawScript)
            ? rawScript
            : null;

        MacroParseResult parsed = MacroScriptParser.Parse(script, _context.I18n.T);
        if (!parsed.IsValid)
        {
            return Task.FromResult(ActionResult.Fail(parsed.ErrorMessage!));
        }

        long totalWaitMs = CalculateTotalWaitMs(parsed.Steps, preDelayMs, stepDelayMs, repeatCount);
        if (totalWaitMs > MacroScriptParser.MaxTotalWaitMs)
        {
            return Task.FromResult(ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                _context.I18n.T("macro.err.totalWaitExceeded", Texts.ErrTotalWaitExceeded),
                totalWaitMs,
                MacroScriptParser.MaxTotalWaitMs)));
        }

        try
        {
            if (preDelayMs > 0)
            {
                WaitWithCancellation(preDelayMs, cancellationToken);
            }

            bool previousWasAction = false;

            for (int loop = 0; loop < repeatCount; loop++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i < parsed.Steps.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MacroStep step = parsed.Steps[i];

                    if (step.Kind == MacroStepKind.Wait)
                    {
                        if (step.WaitMs > 0)
                        {
                            WaitWithCancellation(step.WaitMs, cancellationToken);
                        }
                        previousWasAction = false;
                        continue;
                    }

                    if (previousWasAction && stepDelayMs > 0)
                    {
                        WaitWithCancellation(stepDelayMs, cancellationToken);
                    }

                    switch (step.Kind)
                    {
                        case MacroStepKind.Hotkey:
                        case MacroStepKind.Key:
                        {
                            for (int r = 0; r < step.Repeat; r++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (r > 0 && stepDelayMs > 0)
                                {
                                    WaitWithCancellation(stepDelayMs, cancellationToken);
                                }

                                if (!_context.Host.SendHotkey(step.Value))
                                {
                                    return Task.FromResult(FailStep(step));
                                }
                            }
                            break;
                        }

                        case MacroStepKind.Text:
                        {
                            IReadOnlyList<MacroTypingUnit> units = MacroScriptParser.DecomposeTypingUnits(step.Value);
                            for (int u = 0; u < units.Count; u++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (u > 0 && step.CharDelayMs > 0)
                                {
                                    WaitWithCancellation(step.CharDelayMs, cancellationToken);
                                }

                                MacroTypingUnit unit = units[u];
                                bool ok = unit.IsKey
                                    ? _context.Host.SendHotkey(unit.Value)
                                    : _context.Host.SendText(unit.Value);

                                if (!ok)
                                {
                                    return Task.FromResult(FailStep(step));
                                }
                            }
                            break;
                        }

                        case MacroStepKind.Clipboard:
                        {
                            if (!_context.Host.SetClipboardText(step.Value))
                            {
                                return Task.FromResult(FailStep(step));
                            }
                            break;
                        }

                        case MacroStepKind.Paste:
                        {
                            if (!_context.Host.SetClipboardText(step.Value))
                            {
                                return Task.FromResult(FailStep(step));
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                            if (!_context.Host.SendHotkey("Ctrl+V"))
                            {
                                return Task.FromResult(FailStep(step));
                            }
                            break;
                        }
                    }

                    previousWasAction = true;
                }
            }

            return Task.FromResult(ActionResult.Ok());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Warn($"Macro capability denied: {ex.Message}");
            return Task.FromResult(ActionResult.Fail(
                _context.I18n.T("macro.err.capability", Texts.ErrCapabilityDenied)));
        }
        catch (Exception ex)
        {
            _context.Log.Error("Macro execution failed unexpectedly.", ex);
            return Task.FromResult(ActionResult.Fail(ex.Message));
        }
    }

    internal static long CalculateTotalWaitMs(
        IReadOnlyList<MacroStep> steps,
        int preDelayMs,
        int stepDelayMs,
        int repeatCount)
    {
        long total = preDelayMs;
        bool previousWasAction = false;

        for (int loop = 0; loop < repeatCount; loop++)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                MacroStep step = steps[i];
                if (step.Kind == MacroStepKind.Wait)
                {
                    total += step.WaitMs;
                    previousWasAction = false;
                }
                else
                {
                    if (previousWasAction && stepDelayMs > 0)
                    {
                        total += stepDelayMs;
                    }

                    if ((step.Kind == MacroStepKind.Hotkey || step.Kind == MacroStepKind.Key) &&
                        step.Repeat > 1 &&
                        stepDelayMs > 0)
                    {
                        total += (long)(step.Repeat - 1) * stepDelayMs;
                    }
                    else if (step.Kind == MacroStepKind.Text && step.CharDelayMs > 0)
                    {
                        int unitCount = MacroScriptParser.DecomposeTypingUnits(step.Value).Count;
                        if (unitCount > 1)
                        {
                            total += (long)(unitCount - 1) * step.CharDelayMs;
                        }
                    }

                    previousWasAction = true;
                }
            }
        }

        return total;
    }

    private ActionResult FailStep(MacroStep step)
    {
        string msg = string.Format(
            CultureInfo.InvariantCulture,
            _context.I18n.T("macro.err.stepFailed", Texts.ErrStepExecutionFailed),
            step.LineNumber,
            step.ToPreviewToken());
        _context.Log.Warn(msg);
        return ActionResult.Fail(msg);
    }

    private void WaitWithCancellation(int ms, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ms <= 0)
        {
            return;
        }

        if (DelayOverride != null)
        {
            DelayOverride(ms, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (cancellationToken.WaitHandle.WaitOne(ms))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private bool TryParseOptions(
        IReadOnlyDictionary<string, string>? parameters,
        out int preDelayMs,
        out int stepDelayMs,
        out int repeatCount,
        out string? errorMessage)
    {
        preDelayMs = DefaultPreDelayMs;
        stepDelayMs = DefaultStepDelayMs;
        repeatCount = DefaultRepeatCount;
        errorMessage = null;

        if (parameters == null)
        {
            return true;
        }

        if (parameters.TryGetValue("preDelayMs", out string? rawPreDelay) &&
            !string.IsNullOrWhiteSpace(rawPreDelay))
        {
            if (!int.TryParse(rawPreDelay.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out preDelayMs) ||
                preDelayMs < 0 || preDelayMs > MaxPreDelayMs)
            {
                errorMessage = _context.I18n.T("macro.err.invalidPreDelay", Texts.ErrInvalidPreDelay);
                return false;
            }
        }

        if (parameters.TryGetValue("stepDelayMs", out string? rawStepDelay) &&
            !string.IsNullOrWhiteSpace(rawStepDelay))
        {
            if (!int.TryParse(rawStepDelay.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stepDelayMs) ||
                stepDelayMs < 0 || stepDelayMs > MaxStepDelayMs)
            {
                errorMessage = _context.I18n.T("macro.err.invalidStepDelay", Texts.ErrInvalidStepDelay);
                return false;
            }
        }

        if (parameters.TryGetValue("repeatCount", out string? rawRepeatCount) &&
            !string.IsNullOrWhiteSpace(rawRepeatCount))
        {
            if (!int.TryParse(rawRepeatCount.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out repeatCount) ||
                repeatCount < 1 || repeatCount > MaxRepeatCount)
            {
                errorMessage = _context.I18n.T("macro.err.invalidRepeatCount", Texts.ErrInvalidRepeatCount);
                return false;
            }
        }

        return true;
    }
}
