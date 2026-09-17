using StarPie.Plugin;

namespace StarPie.Plugin.Folder;

/// <summary>
/// StarPie 随包动作包「打开文件夹」。
/// <para>
/// 它认领两个顶层动作类型：<c>Folder</c> 与历史别名 <c>OpenFolder</c> ——
/// 认领声明在 csproj 的 <c>StarPiePluginTypeClaims</c> 程序集元数据里，
/// 宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>别名为什么必须与主类型同包</b>：它们是同一个动作的两种历史写法。
/// 拆到两个包，就会出现「同一个动作在两种写法下由不同插件执行」，
/// 停用其中一个只失效一半配置。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。它与「启动程序」「打开网址」等
/// 原本同属一个基础动作包，要关就得五个一起关。
/// </para>
/// </summary>
public sealed class FolderPlugin : IStarPiePlugin
{
    // 刻意不缓存 IPluginContext 的任何「服务实例」：Shutdown 之后任何一次残留调用
    // 都会摸到一个已被卸载的 ALC 里的对象。只留 context 一个引用、置空即断链。
    private IPluginContext? _context;

    public void Initialize(IPluginContext context)
    {
        _context = context;

        // ① 词条先登记：Descriptor 与 Parameters 会在属性访问时查当前语言文案。
        Texts.Register(context);

        // ② 动作登记。短 ID 必须与 csproj 里认领串右侧的值逐字一致 ——
        //    两个类型名（Folder / OpenFolder）指向的是同一个短 ID `folder`。
        context.Actions.Register(new FolderAction(context));

        context.Log.Info("打开文件夹包已就绪");
    }

    public void Shutdown() => _context = null;
}
