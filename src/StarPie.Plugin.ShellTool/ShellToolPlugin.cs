using StarPie.Plugin;

namespace StarPie.Plugin.ShellTool;

/// <summary>
/// StarPie 随包动作包「系统与右键工具」。
/// <para>
/// 它认领一个顶层动作类型 <c>ShellTool</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。它与「运行命令」一样属于
/// 「让别人替我干活」的一类，想单独关掉的用户不在少数。
/// </para>
/// <para>
/// <b>参数是自由文本，不是列表</b>：宿主有 18 项 Shell 工具（<c>IHostShellService.Verbs</c>），
/// 但把 18 项压进通用下拉是体验降级，而且会让工具清单出现两份。正式入口仍是
/// 宿主的专用挑选器（带搜索与分类），参数声明只如实反映「它就是一个字符串」。
/// </para>
/// </summary>
public sealed class ShellToolPlugin : IStarPiePlugin
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
        context.Actions.Register(new ShellToolAction(context));

        context.Log.Info("系统与右键工具包已就绪");
    }

    public void Shutdown() => _context = null;
}
