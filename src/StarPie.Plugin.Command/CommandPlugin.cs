using StarPie.Plugin;

namespace StarPie.Plugin.Command;

/// <summary>
/// StarPie 随包动作包「运行命令」。
/// <para>
/// 它认领一个顶层动作类型 <c>Command</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。而这个动作的粒度尤其有意义 ——
/// 它的后果是「执行任意命令行」，是这批动作里用户最可能想单独关掉的一类。
/// 从前它与「启动程序」「打开文件夹」同属一个包，想关掉它就得把那些一起关掉。
/// </para>
/// <para>
/// <b>声明 <c>Process</c> 不是装饰</b>：执行走宿主的 <c>IHostCommandService.Run</c>，
/// 未声明会在调用那一刻抛 <c>PluginCapabilityDeniedException</c>，且**绝不静默降级**。
/// </para>
/// </summary>
public sealed class CommandPlugin : IStarPiePlugin
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
        context.Actions.Register(new CommandAction(context));

        context.Log.Info("运行命令包已就绪");
    }

    public void Shutdown() => _context = null;
}
