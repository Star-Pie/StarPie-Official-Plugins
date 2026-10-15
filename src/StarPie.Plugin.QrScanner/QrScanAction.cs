using StarPie.Plugin;

namespace StarPie.Plugin.QrScanner;

internal sealed class QrScanAction(IPluginContext context, QrScanSession session, bool aroundPointer, string icon) : IActionContribution
{
    internal const string RegionId = "scanRegion";
    internal const string PointerId = "scanPointer";
    public ActionDescriptor Descriptor => new()
    {
        Id = aroundPointer ? PointerId : RegionId,
        DisplayName = context.I18n.T(aroundPointer ? "scan.pointer" : "scan.region"),
        DisplayNameKey = aroundPointer ? "scan.pointer" : "scan.region",
        DescriptionKey = aroundPointer ? "scan.pointer.description" : "scan.region.description",
        Category = context.I18n.T("category"), IconKey = icon,
        Kind = ActionKind.Background, TimeoutSeconds = 300
    };
    public IReadOnlyList<ParameterField> Parameters => aroundPointer ?
        [new ParameterField { Key = "radius", Label = context.I18n.T("radius"), LabelKey = "radius",
            Type = ParameterFieldType.Number, DefaultValue = "400", Min = 64, Max = 1024 }] : [];
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (!aroundPointer || !parameters.TryGetValue("radius", out string? value)) return null;
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture,
            out int radius) && radius is >= 64 and <= 1024 ? null : context.I18n.T("radius.invalid");
    }
    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken) =>
        session.ScanAsync(aroundPointer, input.Int("radius", 400), cancellationToken);
}
