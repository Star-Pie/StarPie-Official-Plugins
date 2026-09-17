using StarPie.Plugin;

// 【命名空间遮蔽警告 —— 动这个包之前先读这段】
//
// 本包的命名空间叫 `StarPie.Plugin.System`，也就是说 `StarPie.Plugin` 下有一个成员叫 `System`。
// 于是在下面的命名空间内部，**简单名 `System` 解析到的是本命名空间，而不是 BCL 的 `System`**
// （C# 从最内层往外找，`StarPie.Plugin.System` 里没有 `System` 成员，再往外一层
// `StarPie.Plugin` 里就有一个 —— 正是我们自己）。
//
// 后果：命名空间内部任何 `System.Xxx` 的**限定写法**都会编译失败（CS0234），
// 而用 `using System;` 引入的**简单名**（`Exception` / `Array` / `StringComparison` …）一切正常，
// 因为 `using` 指令位于全局作用域。所以规矩只有一条：
//
//     **本包内部不要写 `System.` 前缀；实在需要就写 `global::System.`。**
//
// 这不是理论风险：本文件最初那一版就写了 `System.Globalization.CultureInfo`，
// 构建当场报错。留着这段注释是给下一个往这个包里加代码的人省一次同样的困惑。

namespace StarPie.Plugin.System;

/// <summary>
/// StarPie 随包动作包「系统控制」。
/// <para>
/// 它认领一个顶层动作类型：<c>System</c> —— 认领声明在 csproj 的
/// <c>StarPiePluginTypeClaims</c> 程序集元数据里，宿主<b>不加载本程序集</b>就能读到它。
/// </para>
/// <para>
/// <b>单动作包的意义</b>：停用粒度精确到一个动作。它从前与「截屏识字」同属一个包，
/// 要关就得两个一起关。
/// </para>
/// <para>
/// <b>它声明 <c>InputSimulation</c> 与 <c>Process</c> 两项能力</b>：
/// 前者是执行面 <c>IHostSystemService.RunPreset</c> 的门禁，后者对应「关机 / 重启 /
/// 任务管理器」那几个会真的起进程的预设。两项都有真实后果，不是凑数。
/// </para>
/// </summary>
public sealed class SystemPlugin : IStarPiePlugin
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
        context.Actions.Register(new SystemAction(context));

        context.Log.Info("系统控制包已就绪");
    }

    public void Shutdown() => _context = null;
}
