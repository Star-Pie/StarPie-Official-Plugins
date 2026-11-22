using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StarPie.Plugin.EmojiPicker;

internal sealed class EmojiAction : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly EmojiController _controller;
    private readonly bool _manage;
    private readonly string _iconKey;

    internal EmojiAction(IPluginContext context, EmojiController controller, bool manage, string iconKey)
    {
        _context = context;
        _controller = controller;
        _manage = manage;
        _iconKey = iconKey;
    }

    public ActionDescriptor Descriptor => new()
    {
        Id = _manage ? "manage" : "show",
        DisplayNameKey = _manage ? "actionManage" : "actionShow",
        Kind = ActionKind.Sequential,
        IconKey = _iconKey
    };

    internal static ParameterField ModeField(string key, bool includeDefaultOption)
    {
        var options = new List<ParameterOption>();
        if (includeDefaultOption)
            options.Add(new ParameterOption { Value = "Default", LabelKey = "useDefault" });

        options.Add(new ParameterOption { Value = nameof(EmojiPickerMode.All), LabelKey = "modeAll" });
        options.Add(new ParameterOption { Value = nameof(EmojiPickerMode.Emoji), LabelKey = "modeEmoji" });
        options.Add(new ParameterOption { Value = nameof(EmojiPickerMode.Custom), LabelKey = "modeCustom" });
        options.Add(new ParameterOption { Value = nameof(EmojiPickerMode.Kaomoji), LabelKey = "modeKaomoji" });

        return new ParameterField
        {
            Key = key,
            LabelKey = "fieldDefaultMode",
            Type = ParameterFieldType.Enum,
            DefaultValue = includeDefaultOption ? "Default" : nameof(EmojiPickerMode.All),
            Options = options
        };
    }

    public IReadOnlyList<ParameterField> Parameters => _manage
        ? Array.Empty<ParameterField>()
        : [ModeField("defaultMode", includeDefaultOption: true)];

    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (_manage) return null;
        if (parameters.TryGetValue("defaultMode", out string? val) &&
            !string.IsNullOrEmpty(val) &&
            !string.Equals(val, "Default", StringComparison.OrdinalIgnoreCase) &&
            (!Enum.TryParse<EmojiPickerMode>(val, true, out var parsed) || !Enum.IsDefined(parsed)))
        {
            return EmojiTexts.T(_context, "fieldDefaultMode");
        }
        return null;
    }

    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_manage)
        {
            await _controller.OpenManagerAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            string? raw = input.Parameter("defaultMode");
            if (string.IsNullOrEmpty(raw) || string.Equals(raw, "Default", StringComparison.OrdinalIgnoreCase))
                raw = _context.Settings.Get("defaultMode");

            EmojiPickerMode mode = Enum.TryParse<EmojiPickerMode>(raw, true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : EmojiPickerMode.All;

            await _controller.ShowPickerAsync(input.Context, mode, cancellationToken).ConfigureAwait(false);
        }
        return ActionResult.Ok();
    }
}
