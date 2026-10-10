using StarPie.Plugin;
namespace StarPie.Plugin.Sound;
internal sealed class SoundPreviewAction(SoundPlugin owner, IPluginContext context) : IActionContribution
{
    public ActionDescriptor Descriptor => new() { Id = "preview", DisplayNameKey = "preview.title", Kind = ActionKind.Background, TimeoutSeconds = 30 };
    public IReadOnlyList<ParameterField> Parameters => new[] { new ParameterField { Key = "event", LabelKey = "event", Type = ParameterFieldType.Enum, DefaultValue = "SectorHover", Required = true,
        Options = Enum.GetNames<SoundType>().Append("Flow").Select(v => new ParameterOption { Value = v, LabelKey = "event." + v }).ToArray() } };
    public string? Validate(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue("event", out var value) && (value == "Flow" || (Enum.TryParse<SoundType>(value, out var type) && Enum.IsDefined(type))) ? null : context.I18n.T("invalid");
    public string Preview(IReadOnlyDictionary<string, string> parameters) => context.I18n.T("preview.title");
    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string? error = Validate(input.Parameters); if (error != null) return ActionResult.Fail(error);
        bool done = await owner.AuditionAsync(input.Parameter("event")!, cancellationToken).ConfigureAwait(false);
        return done ? ActionResult.Ok(context.I18n.T("preview.done"), silent: false) : ActionResult.Fail(context.I18n.T("preview.failed"));
    }
}
