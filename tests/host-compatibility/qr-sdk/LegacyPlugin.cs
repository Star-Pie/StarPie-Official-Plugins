using StarPie.Plugin;

public sealed class LegacyPlugin : IStarPiePlugin
{
    private IDisposable? _registration;
    public void Initialize(IPluginContext context)
    {
        var manifest = new PluginManifest
        {
            Features = [new PluginFeature { Id = "legacy", Name = "Legacy", Description = "Description",
                Category = "Compatibility", IconKey = "icon" }]
        };
        PluginFeature feature = manifest.Features.Single();
        if (feature.Id != "legacy" || feature.Name != "Legacy" || feature.Description != "Description" ||
            feature.Category != "Compatibility" || feature.IconKey != "icon")
        {
            throw new InvalidOperationException("Old SDK manifest feature members lost their behavior.");
        }
        _registration = context.Actions.Register(new LegacyAction());
    }
    public void Shutdown() { _registration?.Dispose(); _registration = null; }
    private sealed class LegacyAction : IActionContribution
    {
        public ActionDescriptor Descriptor => new() { Id = "noop", DisplayName = "Legacy no-op" };
        public IReadOnlyList<ParameterField> Parameters => [];
        public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
        public string Preview(IReadOnlyDictionary<string, string> parameters) => "";
        public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken token) =>
            throw new InvalidOperationException("Compatibility verification must never invoke this action.");
    }
}
