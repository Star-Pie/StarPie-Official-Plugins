using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

// 命名空间遮蔽：本包内不要写 `System.` 前缀，理由见 SystemPlugin.cs 顶部那段警告。
namespace StarPie.Plugin.System;

/// <summary>
/// 动作「系统控制」。对应配置里的 <c>Type="System"</c>。
/// <para>
/// <b>参数是一个预设键</b>（<c>Minimize</c> / <c>TaskView</c> / <c>Shutdown</c> …），
/// 由用户在统一表单的一枚下拉里选。下拉的选项<b>直接来自宿主的预设表</b>
/// （<see cref="IHostSystemService.Presets"/>），本包不另抄一份 ——
/// 抄一份的代价是宿主以后加系统功能时要改两处，漏一处就会出现
/// 「新功能在这个面板里能选、在那个面板里选不到」的分裂，而且不会有任何报错。
/// </para>
/// <para>
/// <b>参数键用 <see cref="HostActionFields.Parameter"/></b>，不是自己起一个语义化名字。
/// 认领顶层类型的插件在语义上就是宿主的一部分，说的自然是宿主的语言：
/// 老配置把预设写在 <c>ActionItem.Parameter</c> 这个裸字段上，宿主的通用投影器
/// （<c>ActionParameterProjection</c>）会把它投影成键 <c>Parameter</c>。
/// 若这里改叫 <c>preset</c>，投影器根本不会产出这个键 —— 结果是<b>所有老配置的预设全部读不到</b>，
/// 而现象只是「按下去没反应」。拆分前那个内建实现之所以能叫 <c>preset</c>，
/// 是因为它自带一个把裸字段搬过去的投影函数；外移之后那一层没有了。
/// </para>
/// <para>
/// <b>它的两个读取来源有优先级</b>（宿主那侧的行为，这里如实记一笔）：
/// 投影器先铺 <c>ExtensionData</c>、再补裸字段。所以「统一表单写入的值」优先于
/// 「老式焦点面板写入的 <c>item.Parameter</c>」。这与其余十一个已外移的动作完全同形，
/// 不是本包引入的行为；写在这里是因为排查「改了没生效」时第一处就该看它。
/// </para>
/// </summary>
internal sealed class SystemAction : IActionContribution
{
    private readonly IPluginContext _context;

    public SystemAction(IPluginContext context) => _context = context;

    // 属性而不是缓存字段：界面语言可以在运行时切换，每次访问重新取词条，界面才会跟着语言走。
    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "system",
        DisplayName = T("system.title", Texts.SystemTitle),
        Description = T("system.desc", Texts.SystemDesc),

        // 分类留空：认领了顶层类型的动作出现在「动作类型」主下拉里，不进插件动作的分组体系。
        Category = "",
        IconKey = null,

        // 【必须与拆分前一致】原来它作为内建动作就在动作线程上同步执行。
        // 标成 Background 会把它挪到线程池，用户可感知的时序就变了。
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    /// <summary>
    /// 参数声明：一枚预设下拉。
    /// <para>
    /// 取的是宿主<b>有序</b>的那份清单（<see cref="IHostSystemService.Presets"/> 就是
    /// 宿主自己的下拉在用的同一份），顺序即用户看到的顺序。
    /// </para>
    /// </summary>
    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("system.preset", Texts.SystemPresetLabel),
            LabelKey = "system.preset",
            Type = ParameterFieldType.Enum,
            Required = true,
            Options = BuildPresetOptions(),
        },
    };

    /// <summary>
    /// 预设选项<b>从宿主现取</b>，不在插件里再抄一份。
    /// <para>
    /// <b>依赖一个不变量</b>：<see cref="IHostSystemService.Presets"/> 必须非空 ——
    /// <c>ParameterFieldType.Enum</c> 没有选项时宿主会在注册阶段抛契约异常，
    /// 而契约异常会让整个插件被卸载。宿主侧那是一份静态表，所以这是安全的；
    /// 自检里有一条断言守着它（认领链路上比对选项数与宿主预设表的项数）。
    /// </para>
    /// <para>
    /// 显示名已经由宿主拼好（<c>"[分类] 名称"</c>），所以这里给 <c>Label</c> 而不是
    /// <c>LabelKey</c> —— <c>LabelKey</c> 指向的是本插件的命名空间，查不到宿主那几十条预设文案。
    /// </para>
    /// </summary>
    private List<ParameterOption> BuildPresetOptions()
    {
        var options = new List<ParameterOption>();

        foreach (SystemPresetOption preset in _context.System.Presets)
        {
            options.Add(new ParameterOption { Value = preset.Key, Label = preset.DisplayName });
        }

        return options;
    }

    /// <summary>
    /// <b>只挡空值，刻意不校验「这个键在不在预设表里」</b>：
    /// 宿主执行体的 <c>switch</c> 里保留着大量不在当前预设表里的历史别名
    /// （<c>SnapLeft</c> / <c>靠左分屏</c> / <c>lock</c> / <c>锁屏</c> / <c>starpie控制台</c> …），
    /// 一旦按表校验，那些年代的配置会被判成非法而无法执行 ——
    /// 本想防静默失效，结果造出一个更糟的静默失效。
    /// 「认不认识这个键」由宿主的执行体说了算，并把结论经
    /// <see cref="IHostSystemService.RunPreset"/> 的返回值交回来。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string preset = Read(parameters, HostActionFields.Parameter);

        return string.IsNullOrWhiteSpace(preset)
            ? T("system.empty", Texts.SystemEmpty)
            : null;
    }

    /// <summary>
    /// 列表副标题：回显预设的显示名（认不出来就回显原键，至少不是一行空白）。
    /// <para>
    /// <b>必须极快</b>（微秒级）：设置页滚动时会被高频调用，不得有 IO。
    /// 这里遍历宿主预设表 —— 与 <c>TileAction.Preview</c> 遍历布局表是同一个量级，
    /// 四十来项，没有 IO。
    /// </para>
    /// </summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string preset = Read(parameters, HostActionFields.Parameter);
        if (preset.Length == 0) return "";

        foreach (SystemPresetOption option in _context.System.Presets)
        {
            if (string.Equals(option.Key, preset, StringComparison.OrdinalIgnoreCase))
            {
                return option.DisplayName;
            }
        }

        // 认不出来的键原样显示：它多半是旧版本留下的、或手改配置文件写进去的。
        // 显示出来至少能让用户看见「这里有个我不认识的值」，而不是一行空白。
        return preset;
    }

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方（<c>PluginInvoker</c>）在唯一的动作线程上以同步方式等待本方法，
    /// 所以实现里绝不能出现真正的异步等待 —— 一旦 <c>await</c> 到别的上下文就会死锁。
    /// 全程同步完成，<see cref="Task.FromResult{TResult}"/> 只是为了让签名与契约一致。
    /// </para>
    /// <para>
    /// <b>比拆分之前更严，是刻意的</b>：宿主的 <c>ExecuteSystem</c> 对认不出的键<b>什么也不做</b>。
    /// 拆分前那条路径上，用户按下扇区后既没有反应也没有提示 —— 与「按键本身没生效」一模一样。
    /// 现在 <see cref="IHostSystemService.RunPreset"/> 会把「认不认识」回传回来，
    /// 这里据此给出一句能照着修的话。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string preset = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(preset))
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            // 直接调用本实现时（例如自检）没有那层保护，所以再兜一次。
            return Task.FromResult(ActionResult.Fail(T("system.empty", Texts.SystemEmpty)));
        }

        bool recognized;

        try
        {
            recognized = _context.System.RunPreset(preset);
        }
        catch (PluginCapabilityDeniedException ex)
        {
            // 走到这里说明清单的 capabilities 里少了 InputSimulation。异常消息本身已写明怎么修，
            // 这里把它记进日志，再给用户一句人话 —— 用户不该看到 .NET 异常文本。
            _context.Log.Error("系统功能被宿主拒绝：本插件未声明 InputSimulation 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        if (!recognized)
        {
            // InvariantCulture 而不是当前区域：这句模板里的 {0} 是从配置读来的原键，
            // 它含中文或符号时，某些区域设置下的格式化会做出意料之外的转换。
            // 这里只需要「把原键插进去」这一件事，不用任何区域相关的规则。
            return Task.FromResult(ActionResult.Fail(string.Format(
                CultureInfo.InvariantCulture,
                T("system.unknown", Texts.SystemUnknown),
                preset)));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters, string key) =>
        parameters != null && parameters.TryGetValue(key, out string? value) ? value ?? "" : "";
}
