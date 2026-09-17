using StarPie.Plugin;

namespace StarPie.Plugin.SwitchWindow;

/// <summary>
/// StarPie 随包动作包「切换窗口」。
/// <para>
/// 它认领一个顶层动作类型 <c>SwitchWindow</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。它与「平铺窗口」「窗口透明度」
/// 等原本同属一个窗口动作包，要关就得五个一起关。
/// </para>
/// <para>
/// <b>为什么声明 <c>WindowControl</c></b>：安装确认页上展示的能力必须对应一个真实后果 ——
/// 这个动作会把<b>别的程序的</b>窗口切到前台，<c>Ui</c> 与 <c>Process</c> 都名不副实。
/// </para>
/// </summary>
public sealed class SwitchWindowPlugin : IStarPiePlugin
{
    // 刻意不缓存 IPluginContext 的任何「服务实例」：Shutdown 之后任何一次残留调用
    // 都会摸到一个已被卸载的 ALC 里的对象。只留 context 一个引用、置空即断链。
    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // ① 词条先登记：Descriptor 与 Parameters 会在属性访问时查当前语言文案。
        Texts.Register(context);

        // ② 动作登记。短 ID 必须与 csproj 里认领串右侧的值逐字一致。
        context.Actions.Register(new SwitchWindowAction(context));

        context.Log.Info("切换窗口包已就绪");
    }

    public void Shutdown() => _context = null;
}
