using System;
using System.Threading;

namespace StarPie.Plugin.EmojiPicker;

public sealed class EmojiPickerPlugin : IStarPiePlugin
{
    private EmojiController? _controller;

    public void Initialize(IPluginContext context)
    {
        EmojiTexts.Register(context);
        if (context is not IInputPanelPluginContext panels)
            throw new InvalidOperationException(EmojiTexts.T(context, "requiresHost"));

        string iconKey = context.Icons.RegisterSvg("emoji", EmojiUiTheme.ActionSvgData);
        _controller = new EmojiController(context, panels.InputPanels);

        context.Actions.Register(new EmojiAction(context, _controller, manage: false, iconKey));
        context.Actions.Register(new EmojiAction(context, _controller, manage: true, iconKey));

        context.SettingsPage.Register(new SettingsPageDescriptor
        {
            TitleKey = "settingsTitle",
            DescriptionKey = "settingsDesc",
            Fields =
            [
                EmojiAction.ModeField("defaultMode", includeDefaultOption: false),
                new ParameterField { Key = "autoCloseOnPick", LabelKey = "fieldAutoClose", Type = ParameterFieldType.Bool, DefaultValue = "true" },
                new ParameterField { Key = "gifHoverPlay", LabelKey = "fieldGifHover", Type = ParameterFieldType.Bool, DefaultValue = "true" },
                new ParameterField { Key = "trackRecent", LabelKey = "fieldTrackRecent", Type = ParameterFieldType.Bool, DefaultValue = "true" },
                new ParameterField { Key = "emojiClipboardFallback", LabelKey = "fieldEmojiClipFallback", Type = ParameterFieldType.Bool, DefaultValue = "false" }
            ],
            ActionIds = ["manage"]
        });
    }

    public void Shutdown()
    {
        EmojiController? controller = Interlocked.Exchange(ref _controller, null);
        controller?.Dispose();
    }
}
