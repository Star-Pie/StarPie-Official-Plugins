using System;
using StarPie.Plugin;

namespace StarPie.Plugin.Macro;

/// <summary>
/// 按键宏与多步骤脚本官方插件入口（starpie.plugin.macro）。
/// </summary>
public sealed class MacroPlugin : IStarPiePlugin
{
    internal const string IconSvgPath =
        "M4,6 H14 V8 H4 Z M4,11 H11 V13 H4 Z M4,16 H9 V18 H4 Z M15,10 L21,14 L15,18 Z";

    private IDisposable? _actionRegistration;
    internal MacroAction? ActionInstance { get; private set; }

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Texts.Register(context);
        string iconKey = context.Icons.RegisterSvg("macro", IconSvgPath);
        var action = new MacroAction(context, iconKey);
        ActionInstance = action;
        _actionRegistration = context.Actions.Register(action);
    }

    public void Shutdown()
    {
        try
        {
            MacroVisualBuilder.DetachAll();
            _actionRegistration?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _actionRegistration = null;
            ActionInstance = null;
        }
    }
}
