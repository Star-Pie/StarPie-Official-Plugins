using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

internal sealed class TogglePetContribution(IPluginContext context, PetController controller, string? iconKey) : IActionContribution
{
    public ActionDescriptor Descriptor => new()
    {
        Id = "togglePet",
        DisplayName = "桌宠",
        DisplayNameKey = "action.toggle-pet.name",
        Description = "触发一次显示桌宠，再次触发收起。形象与互动方式使用桌宠设置。",
        DescriptionKey = "action.toggle-pet.description",
        Category = "桌宠",
        IconKey = iconKey,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
    public string Preview(IReadOnlyDictionary<string, string> parameters) => context.I18n.T("action.toggle-pet.description");

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return ActionResult.Fail(context.I18n.T("error.cancelled"));
        if (!context.Info.HasCapability(PluginCapability.Ui)) return ActionResult.Ok(context.I18n.T("notify.no-ui-capability"), silent: false);
        bool success = false;
        try
        {
            await context.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (controller.IsShutdown) return;
                if (controller.HasWindow) { controller.Hide(); success = true; }
                else success = controller.Show();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return ActionResult.Fail(context.I18n.T("error.cancelled")); }
        catch (Exception ex)
        {
            context.Log.Error("Desktop pet toggle failed", ex);
            return ActionResult.Fail(context.I18n.T("error.toggle-failed"));
        }
        if (controller.IsShutdown) return ActionResult.Fail(context.I18n.T("controller.apply.shutdown"));
        return success ? ActionResult.Ok(silent: true) : ActionResult.Ok(context.I18n.T("notify.asset-missing"), silent: false);
    }
}
