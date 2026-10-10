using StarPie.Plugin;
namespace StarPie.Plugin.Sound;
internal abstract class SoundFileAction(SoundPlugin owner, IPluginContext context) : IActionContribution
{
    protected SoundPlugin Owner => owner;
    protected IPluginContext Context => context;
    public abstract ActionDescriptor Descriptor { get; }
    public abstract IReadOnlyList<ParameterField> Parameters { get; }
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return context.I18n.T("fileRequired");
        try { _ = Path.GetFullPath(path); return null; } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return context.I18n.T("invalid"); }
    }
    public string Preview(IReadOnlyDictionary<string, string> parameters) => context.I18n.T(Descriptor.DisplayNameKey!);
    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string? error = Validate(input.Parameters); if (error != null) return ActionResult.Fail(error);
        try { cancellationToken.ThrowIfCancellationRequested(); return await Perform(input, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { context.Log.Warn("Sound profile operation failed: " + ex.Message); return ActionResult.Fail(context.I18n.T("io.failed") + ex.Message); }
    }
    protected abstract Task<ActionResult> Perform(PluginActionInput input, CancellationToken token);
}
internal sealed class SoundImportAction(SoundPlugin owner, IPluginContext context) : SoundFileAction(owner, context)
{
    public override ActionDescriptor Descriptor => new() { Id = "importProfiles", DisplayNameKey = "import.title", Kind = ActionKind.Background, TimeoutSeconds = 30 };
    public override IReadOnlyList<ParameterField> Parameters => new[]
    {
        new ParameterField { Key = "path", LabelKey = "path", Type = ParameterFieldType.File, Required = true },
        new ParameterField { Key = "activate", LabelKey = "activate", Type = ParameterFieldType.Bool, DefaultValue = "true" },
        new ParameterField { Key = "replace", LabelKey = "replace", Type = ParameterFieldType.Bool, DefaultValue = "false" }
    };
    protected override Task<ActionResult> Perform(PluginActionInput input, CancellationToken token)
    {
        string id = Owner.Import(input.Parameter("path")!, input.Bool("activate", true), input.Bool("replace"), token);
        return Task.FromResult(ActionResult.Ok(Context.I18n.T("import.done") + id, silent: false));
    }
}
internal sealed class SoundExportAction(SoundPlugin owner, IPluginContext context) : SoundFileAction(owner, context)
{
    public override ActionDescriptor Descriptor => new() { Id = "exportProfiles", DisplayNameKey = "export.title", Kind = ActionKind.Background, TimeoutSeconds = 30 };
    public override IReadOnlyList<ParameterField> Parameters => new[]
    {
        new ParameterField { Key = "path", LabelKey = "path", Type = ParameterFieldType.Text, Required = true },
        new ParameterField { Key = "profileId", LabelKey = "profile", Type = ParameterFieldType.Text },
        new ParameterField { Key = "bundle", LabelKey = "bundle", Type = ParameterFieldType.Bool, DefaultValue = "false" },
        new ParameterField { Key = "overwrite", LabelKey = "overwrite", Type = ParameterFieldType.Bool, DefaultValue = "false" }
    };
    protected override Task<ActionResult> Perform(PluginActionInput input, CancellationToken token)
    {
        Owner.Export(input.Parameter("path")!, input.Parameter("profileId"), input.Bool("bundle"), input.Bool("overwrite"), token);
        return Task.FromResult(ActionResult.Ok(Context.I18n.T("export.done"), silent: false));
    }
}
