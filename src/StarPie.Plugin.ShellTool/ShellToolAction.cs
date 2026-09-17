using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.ShellTool;

/// <summary>
/// 动作「系统与右键工具」。对应配置里的 <c>Type="ShellTool"</c>。
/// <para>
/// <b>参数刻意声明成自由文本，而不是 <see cref="ParameterFieldType.Enum"/></b>，
/// 理由与它作为内建动作时完全相同，外移并没有改变它：
/// </para>
/// <list type="number">
/// <item>这个动作有一张 18 项的功能表，而它在宿主 UI 上已经有一个比下拉更好的入口 ——
/// 带搜索、带分类、带说明的专用挑选器。把 18 项压进一枚通用下拉是<b>体验降级</b>；</item>
/// <item>更重要的是：执行体对每个功能同时接受<b>两套命名</b>（<c>copy_path</c> 与
/// <c>Windows.CopyAsPath</c>），历史配置里两种都有。声明成枚举就等于按清单做白名单校验，
/// 会把另一套命名的老配置整体判死 —— 本想防静默失效，结果造出更糟的。</item>
/// </list>
/// <para>
/// 所以值就是功能标识，校验只挡空值。<see cref="IHostShellService.Verbs"/> 提供了那份清单，
/// 但本动作<b>刻意不用它做下拉</b>：那会同时踩中上面两条。它留给真正想做「工具选择器」
/// 类动作的插件。
/// </para>
/// <para>
/// <b>参数键用 <see cref="HostActionFields"/> 而不是自定义短名</b>：配置里这个动作的参数
/// 仍存在裸字段 <c>Parameter</c> 上，宿主调用前会用字段投影器现读现装成以宿主属性名为键的字典。
/// </para>
/// </summary>
internal sealed class ShellToolAction : IActionContribution
{
    private readonly IPluginContext _context;

    public ShellToolAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "shellTool",
        DisplayName = T("shell.tool.title", Texts.ShellToolTitle),
        Description = null,
        Category = "",
        IconKey = null,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("shell.tool.verb", Texts.ShellToolVerb),
            LabelKey = "shell.tool.verb",
            Type = ParameterFieldType.Text,
            Required = true,
            Placeholder = "copy_path",
            HelpText = T("shell.tool.verbHelp", Texts.ShellToolVerbHelp),
        },
    };

    /// <summary>
    /// <b>只挡空值</b>：执行体为每个功能同时接受短 ID 与规范名两套写法，
    /// 按某一套做白名单校验会把另一套的老配置判死。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string verb = Read(parameters);
        return string.IsNullOrWhiteSpace(verb) ? T("shell.tool.empty", Texts.ShellToolEmpty) : null;
    }

    /// <summary>列表副标题。功能标识本身是英文，回显它比留空有用。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string verb = Read(parameters).Trim();
        return verb.Length <= 48 ? verb : verb.Substring(0, 47) + "…";
    }

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方在唯一的动作线程上同步等待本方法，实现里绝不能出现真正的
    /// 异步等待（会死锁）。全程同步，<see cref="Task.FromResult{TResult}"/> 只为签名一致。
    /// </para>
    /// <para>
    /// <b>返回值语义刻意保守</b>：<see cref="IHostShellService.Invoke"/> 返回 true 只表示
    /// 「宿主接受了这次调用」。执行体是 33 个分支的 switch，其中若干分支在上下文不适用时
    /// 静默 return（例如「以管理员身份运行」时没选中可执行文件），它本身不产生失败信号。
    /// 与其在这里编一个不可靠的成功/失败判断，不如把语义写窄。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string verb = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(verb))
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            return Task.FromResult(ActionResult.Fail(Texts.ShellToolEmpty));
        }

        try
        {
            if (!_context.Shell.Invoke(verb))
            {
                return Task.FromResult(ActionResult.Fail(
                    $"未能执行系统工具「{verb.Trim()}」。请确认该功能标识可用；具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            // 与 CommandAction 同一条处理：异常消息已写明怎么修，记进日志后给用户一句人话。
            _context.Log.Error("系统工具调用被宿主拒绝：本插件未声明 Process 能力", ex);
            return Task.FromResult(ActionResult.Fail(
                "本插件缺少「进程」能力声明，系统工具已被拒绝。请重新安装本插件，或联系插件作者。"));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters) =>
        parameters != null && parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? value ?? ""
            : "";
}
