using StarPie.Plugin;

namespace StarPie.Plugin.Launch;

/// <summary>
/// StarPie 随包动作包「启动程序」。
/// <para>
/// 它认领一个顶层动作类型 <c>Launch</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。它与「打开网址」「运行命令」等
/// 原本同属一个基础动作包，要关就得五个一起关。
/// </para>
/// <para>
/// <b>为什么声明 <c>Process</c></b>：它的后果就是启动一个进程 ——
/// 安装确认页上「进程」两个字与用户脑子里想的事情一致。
/// 它调用的是既有的 <c>IHostActionInvoker.Launch</c>（无门禁契约），
/// 这条能力声明换来的是「确认页说了实话」，不是一道安全边界。
/// </para>
/// </summary>
public sealed class LaunchPlugin : IStarPiePlugin
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
        context.Actions.Register(new LaunchAction(context));

        context.Log.Info("启动程序包已就绪");
    }

    public void Shutdown() => _context = null;
}
