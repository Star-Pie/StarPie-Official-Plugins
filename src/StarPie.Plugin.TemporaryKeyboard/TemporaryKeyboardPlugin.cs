namespace StarPie.Plugin.TemporaryKeyboard;

public sealed class TemporaryKeyboardPlugin : IStarPiePlugin
{
    private KeyboardController? _controller;

    public void Initialize(IPluginContext context)
    {
        KeyboardTexts.Register(context);
        if (context is not IInputPanelPluginContext panels)
            throw new InvalidOperationException(KeyboardTexts.T(context, "requiresHost"));
        _controller = new KeyboardController(context, panels.InputPanels);
        context.Actions.Register(new KeyboardAction(context, _controller, configure: false));
        context.Actions.Register(new KeyboardAction(context, _controller, configure: true));
        context.SettingsPage.Register(new SettingsPageDescriptor
        {
            TitleKey = "title",
            Fields = [KeyboardAction.ModeField("defaultMode")],
            ActionIds = ["configure"],
        });
    }

    public void Shutdown()
    {
        KeyboardController? controller = Interlocked.Exchange(ref _controller, null);
        controller?.Dispose();
    }
}

internal sealed class KeyboardAction : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly KeyboardController _controller;
    private readonly bool _configure;
    internal KeyboardAction(IPluginContext context, KeyboardController controller, bool configure)
    { _context = context; _controller = controller; _configure = configure; }

    public ActionDescriptor Descriptor => new()
    {
        Id = _configure ? "configure" : "show", DisplayNameKey = _configure ? "configure" : "show",
        Kind = ActionKind.Sequential, IconKey = "Keyboard",
    };

    internal static ParameterField ModeField(string key) => new()
    {
        Key = key, LabelKey = "mode", Type = ParameterFieldType.Enum, DefaultValue = key == "mode" ? "Default" : "Numeric",
        Options = (key == "mode" ? new[] { new ParameterOption { Value = "Default", LabelKey = "useDefault" } } : Array.Empty<ParameterOption>())
            .Concat(Enum.GetValues<KeyboardMode>().Select(mode => new ParameterOption { Value = mode.ToString(), LabelKey = mode.ToString() })).ToArray(),
    };

    public IReadOnlyList<ParameterField> Parameters => _configure ? Array.Empty<ParameterField>() :
        [ModeField("mode"), new ParameterField { Key = "scheme", LabelKey = "scheme", Type = ParameterFieldType.Text, MaxLength = 64 }];
    public string? Validate(IReadOnlyDictionary<string, string> parameters) =>
        !_configure && parameters.TryGetValue("mode", out string? value) &&
        value != "Default" && (!Enum.TryParse<KeyboardMode>(value, out var mode) || !Enum.IsDefined(mode)) ? KeyboardTexts.T(_context, "invalid") : null;
    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_configure) await _controller.ConfigureAsync(cancellationToken);
        else
        {
            string? raw = input.Parameter("mode");
            if (string.IsNullOrEmpty(raw) || raw == "Default") raw = _context.Settings.Get("defaultMode");
            KeyboardMode mode = Enum.TryParse(raw, out KeyboardMode parsed) && Enum.IsDefined(parsed) ? parsed : KeyboardMode.Numeric;
            await _controller.ShowAsync(input.Context, mode, input.Parameter("scheme"), cancellationToken);
        }
        return ActionResult.Ok();
    }
}
