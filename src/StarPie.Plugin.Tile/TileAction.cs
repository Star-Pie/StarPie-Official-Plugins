using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Tile;

/// <summary>
/// 动作「平铺窗口」。对应配置里的 <c>Type="Tile"</c>。
/// <para>
/// <b>这一个动作承载了四种用户可见的语义</b>：指定布局、循环切换、反向循环、还原。
/// 它们在配置里都是同一个 Type，靠主参数区分 ——
/// 具体布局码（<c>"2L"</c> 左半屏、<c>"4G"</c> 四宫格…）与三个特殊标记
/// （<c>Cycle</c> / <c>CycleBack</c> / <c>Restore</c>）。宿主界面侧则是
/// 「子模式下拉 + 二级下拉」两级联动来产生这些组合。
/// </para>
/// <para>
/// 这里把两者合并成<b>一枚下拉</b>：布局码 + 3 个标记。这不是简化了功能，
/// 而是发现「子模式」本身就是多余的中间层 —— 用户要表达的东西从头到尾只有
/// 「平铺成什么样」这一个选择。
/// </para>
/// <para>
/// <b>布局码与显示名全部从宿主服务现取</b>（<see cref="IHostWindowService.Layouts"/>），
/// 插件里不抄一份。抄一份的后果是以后加布局要改两处，漏一处就会出现
/// 「新布局在下拉里能选、在动作里不生效」—— 而且是静默失效，没有任何报错。
/// </para>
/// </summary>
internal sealed class TileAction : IActionContribution
{
    private readonly IPluginContext _context;

    public TileAction(IPluginContext context) => _context = context;

    // 属性而不是缓存字段：界面语言可以在运行时切换，每次访问重新取词条，界面才会跟着语言走。
    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "tile",
        DisplayName = T("tile.title", Texts.TileTitle),
        Description = null,

        // 分类留空：认领了顶层类型的动作出现在「动作类型」主下拉里，不进插件动作的分组体系。
        Category = "",
        IconKey = null,

        // 【必须与拆分前一致】原来它作为内建动作就在动作线程上同步执行。
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("tile.layout", Texts.TileLayout),
            LabelKey = "tile.layout",
            Type = ParameterFieldType.Enum,
            Required = true,
            DefaultValue = "2L",
            Options = BuildLayoutOptions(),
        },
    };

    /// <summary>
    /// 构建选项表：先铺全部布局码，再追加三个特殊标记。
    /// <para>
    /// 顺序是刻意的 —— 特殊标记放最后，因为它们不是「一种布局」，而是对布局的操作。
    /// 混在布局码中间会让用户以为「循环切换」是某种分屏方式。
    /// </para>
    /// <para>
    /// <b>依赖一个不变量</b>：<see cref="IHostWindowService.Layouts"/> 必须非空 ——
    /// <c>ParameterFieldType.Enum</c> 没有选项时宿主会在注册阶段抛契约异常，
    /// 而契约异常会让整个插件被卸载。宿主侧那是一份静态表，所以是安全的；
    /// 自检里有一条断言守着它（「布局清单非空且与宿主 LayoutKeys 同源」）。
    /// </para>
    /// </summary>
    private List<ParameterOption> BuildLayoutOptions()
    {
        var options = new List<ParameterOption>();
        IHostWindowService windows = _context.Windows;

        foreach (WindowLayoutOption layout in windows.Layouts)
        {
            // 显示名已由宿主按当前语言本地化过，所以给 Label 而不是 LabelKey ——
            // LabelKey 指向的是本插件的命名空间，查不到宿主那十几个布局文案。
            options.Add(new ParameterOption { Value = layout.Key, Label = layout.DisplayName });
        }

        options.Add(new ParameterOption
        {
            Value = windows.CycleToken,
            Label = T("tile.cycle", Texts.TileCycle),
        });
        options.Add(new ParameterOption
        {
            Value = windows.CycleBackToken,
            Label = T("tile.cycleBack", Texts.TileCycleBack),
        });
        options.Add(new ParameterOption
        {
            Value = windows.RestoreToken,
            Label = T("tile.restore", Texts.TileRestore),
        });

        return options;
    }

    /// <summary>
    /// <b>比原先更严，是刻意的</b>：宿主执行体把空参数<b>静默</b>当成默认布局
    /// <c>"2L"</c> 处理 —— 用户按下去会看到窗口被排成左半屏，而他根本没有配过这个布局。
    /// 「用了一个你没设过的值」比「告诉你没设过」糟糕得多。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string layout = Read(parameters);
        return string.IsNullOrWhiteSpace(layout) ? T("tile.empty", Texts.TileEmpty) : null;
    }

    /// <summary>列表副标题：布局码一律翻译成中文名，配置里看不到 <c>2L</c> 这种代号。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string layout = Read(parameters);
        if (layout.Length == 0) return "";

        IHostWindowService windows = _context.Windows;

        if (string.Equals(layout, windows.CycleToken, StringComparison.OrdinalIgnoreCase))
        {
            return T("tile.cycle", Texts.TileCycle);
        }

        if (string.Equals(layout, windows.CycleBackToken, StringComparison.OrdinalIgnoreCase))
        {
            return T("tile.cycleBack", Texts.TileCycleBack);
        }

        if (string.Equals(layout, windows.RestoreToken, StringComparison.OrdinalIgnoreCase))
        {
            return T("tile.restore", Texts.TileRestore);
        }

        foreach (WindowLayoutOption option in windows.Layouts)
        {
            if (string.Equals(option.Key, layout, StringComparison.OrdinalIgnoreCase))
            {
                return option.DisplayName;
            }
        }

        // 认不出来的布局码原样显示：它多半是旧版本留下的、或手改配置文件写进去的。
        // 显示出来至少能让用户看见「这里有个我不认识的值」，而不是一行空白。
        return layout;
    }

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方（<c>PluginInvoker</c>）在唯一的动作线程上以同步方式等待本方法，
    /// 所以实现里绝不能出现真正的异步等待 —— 一旦 <c>await</c> 到别的上下文就会死锁。
    /// 全程同步完成，<see cref="Task.FromResult{TResult}"/> 只是为了让签名与契约一致。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string layout = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(layout))
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            // 直接调用本实现时（例如自检）没有那层保护，所以再兜一次。
            return Task.FromResult(ActionResult.Fail(Texts.TileEmpty));
        }

        try
        {
            // 三个标记（Cycle / CycleBack / Restore）与具体布局码的分派全在执行体内部，
            // 这里不再分一遍 —— 分两处迟早会漏掉一个。
            if (!_context.Windows.ApplyLayout(layout))
            {
                return Task.FromResult(ActionResult.Fail($"未能应用平铺方式「{layout}」，具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            // 走到这里说明清单的 capabilities 里少了 WindowControl。
            // 异常消息本身已写明怎么修，这里把它记进日志，再给用户一句人话 ——
            // 用户不该看到 .NET 异常文本。
            _context.Log.Error("窗口平铺被宿主拒绝：本插件未声明 WindowControl 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters) =>
        parameters != null && parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? (value ?? "").Trim()
            : "";
}
