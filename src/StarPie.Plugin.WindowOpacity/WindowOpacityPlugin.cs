using StarPie.Plugin;

namespace StarPie.Plugin.WindowOpacity;

/// <summary>
/// StarPie 随包动作包「窗口透明度」。
/// <para>
/// 它认领一个顶层动作类型 <c>WindowOpacity</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。这个动作正是「不想被乱动窗口」的
/// 用户最容易想单独关掉的一个 —— 而它与「平铺窗口」原本同属一个包，要关就得一起关。
/// </para>
/// <para>
/// <b>为什么声明 <c>WindowControl</c></b>：安装确认页上展示的能力必须对应一个真实后果 ——
/// 这个动作会改<b>其它程序的</b>窗口透明度，<c>Ui</c> 与 <c>Process</c> 都名不副实。
/// </para>
/// <para>
/// <b>取值范围的唯一来源是宿主</b>：合法性区间由 <c>IHostWindowService.OpacityMinPercent</c> /
/// <c>OpacityMaxPercent</c> 现取，插件里不写死数字 —— 写死的话宿主调整范围后，
/// 提示文案会继续把旧范围告诉用户，而用户会照着它填。
/// </para>
/// </summary>
public sealed class WindowOpacityPlugin : IStarPiePlugin
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
        context.Actions.Register(new WindowOpacityAction(context));

        context.Log.Info("窗口透明度包已就绪");
    }

    public void Shutdown() => _context = null;
}
