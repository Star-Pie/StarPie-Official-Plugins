using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Command;

/// <summary>
/// 动作「运行命令」。对应配置里的 <c>Type="Command"</c>。
/// <para>
/// <b>本包里唯一需要 <see cref="PluginCapability.Process"/> 才能工作的动作</b> ——
/// 它经 <see cref="IHostCommandService"/> 在终端里执行任意命令，是 SDK 中权限最高的路径。
/// 本包清单已声明该能力（csproj 的 <c>StarPiePluginCapabilities</c>），正常路径上碰不到门禁；
/// 但若有人删掉那一项，这里会接到一个「调用即拒」的服务并给出可操作的提示，
/// 而不是让那次动作无声无息地什么也不做。
/// </para>
/// <para>
/// <b>参数键用 <see cref="HostActionFields"/> 而不是自定义短名</b>：这个动作在用户配置里
/// 仍是老形态（<c>Parameter</c> / <c>CommandTerminal</c> 两个裸字段），宿主调用前会用字段
/// 投影器现读现装成「以宿主属性名为键」的字典。用常量而不是裸字符串，
/// 是为了让「键名写错」变成编译错误，而不是运行期永远读到空值。
/// </para>
/// <para>
/// <b>一处如实记录的体验降级</b>：这个动作的参数表单原本由宿主手写面板渲染，
/// 现在由宿主按本类的声明统一渲染。两者的字段与选项完全一致，
/// 但手写面板上的终端下拉样式不会跟过来 —— 这是「形状统一」换来的代价。
/// </para>
/// </summary>
internal sealed class CommandAction : IActionContribution
{
    private readonly IPluginContext _context;

    public CommandAction(IPluginContext context) => _context = context;

    // 属性而不是缓存字段：界面语言可以在运行时切换，每次访问重新取词条，界面才会跟着语言走。
    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "command",
        DisplayName = T("command.title", Texts.CommandTitle),
        Description = T("command.desc", Texts.CommandDesc),

        // 分类留空：认领了顶层类型的动作出现在「动作类型」主下拉里，不进插件动作的分组体系。
        Category = "",
        IconKey = null,

        // 【必须与拆分前一致】原来它作为内建动作就在动作线程上同步执行。
        // 标成 Background 会把它挪到线程池，用户可感知的时序就变了。
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("command.line", Texts.CommandLine),
            LabelKey = "command.line",
            Type = ParameterFieldType.Text,
            Required = true,
            Placeholder = "ping -t 127.0.0.1",
        },
        new()
        {
            Key = HostActionFields.CommandTerminal,
            Label = T("command.terminal", Texts.CommandTerminalLabel),
            LabelKey = "command.terminal",
            Type = ParameterFieldType.Enum,
            DefaultValue = "cmd",
            Options = BuildTerminalOptions(),
        },
    };

    /// <summary>
    /// 终端选项<b>从宿主现取</b>，不在插件里再抄一份。
    /// <para>
    /// 这是刻意的：终端的显示名（<c>CMD (无终端)</c> 之类）由宿主按当前语言本地化，
    /// 抄一份到插件里必然漂移 —— 宿主改了文案、插件不同步，同一个动作在外移前后
    /// 会出现两个不同的下拉。而且写成属性，用户切换语言时下拉文案跟着变。
    /// </para>
    /// <para>
    /// <b>依赖一个不变量</b>：<see cref="IHostCommandService.Terminals"/> 必须非空 ——
    /// <c>ParameterFieldType.Enum</c> 没有选项时宿主会在注册阶段抛契约异常，
    /// 而契约异常会让整个插件被卸载。宿主侧那是一份静态目录，所以这是安全的；
    /// 自检里有一条断言守着它（「终端清单非空且含 cmd」）。
    /// </para>
    /// </summary>
    private List<ParameterOption> BuildTerminalOptions()
    {
        var options = new List<ParameterOption>();
        foreach (CommandTerminalOption terminal in _context.Commands.Terminals)
        {
            // 显示名已经由宿主本地化过，所以这里给 Label 而不是 LabelKey ——
            // LabelKey 指向的是本插件的命名空间，查不到宿主那几句终端文案。
            options.Add(new ParameterOption { Value = terminal.Id, Label = terminal.DisplayName });
        }
        return options;
    }

    /// <summary>
    /// <b>比拆分之前更严，是刻意的</b>：原执行体遇到空命令直接 <c>return</c> ——
    /// 用户按下了扇区、什么也没发生、也没有任何提示，是那种「界面一切正常、
    /// 行为却悄悄退化」的静默失效。现在改成在执行前拦下并说明原因。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string command = Read(parameters, HostActionFields.Parameter);
        return string.IsNullOrWhiteSpace(command) ? T("command.empty", Texts.CommandEmpty) : null;
    }

    /// <summary>
    /// 列表副标题。<b>必须极快</b>（微秒级）：设置页滚动时会被高频调用，不得有 IO。
    /// </summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters) =>
        Shorten(Read(parameters, HostActionFields.Parameter));

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方（<c>PluginInvoker</c>）在唯一的动作线程上以同步方式等待本方法，
    /// 所以实现里绝不能出现真正的异步等待 —— 一旦 <c>await</c> 到别的上下文就会死锁。
    /// 全程同步完成，<see cref="Task.FromResult{TResult}"/> 只是为了让签名与契约一致。
    /// </para>
    /// <para>
    /// <b>失败不再抛异常</b>：拆分之前本动作的异常会冒泡到 <c>ActionExecutor.Execute</c>
    /// 的 <c>catch</c> 并弹 MessageBox，而插件侧绝不能那么做 —— 无人值守时那个对话框
    /// 会把整个动作线程卡死。所以失败在这里被转成一句可操作的话，
    /// 具体原因交给宿主的日志（<see cref="IHostCommandService.Run"/> 保证不弹窗）。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string command = input?.Parameter(HostActionFields.Parameter) ?? "";
        string terminal = input?.Parameter(HostActionFields.CommandTerminal) ?? "cmd";

        if (string.IsNullOrWhiteSpace(command))
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            // 直接调用本实现时（例如自检）没有那层保护，所以再兜一次。
            return Task.FromResult(ActionResult.Fail(Texts.CommandEmpty));
        }

        try
        {
            if (!_context.Commands.Run(command, terminal))
            {
                return Task.FromResult(ActionResult.Fail(
                    $"未能启动命令「{Shorten(command)}」。请确认该命令本身可用；具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            // 走到这里说明清单的 capabilities 里少了 Process。异常消息本身已写明怎么修，
            // 这里把它记进日志，再给用户一句人话 —— 用户不该看到 .NET 异常文本。
            _context.Log.Error("运行命令被宿主拒绝：本插件未声明 Process 能力", ex);
            return Task.FromResult(ActionResult.Fail(
                "本插件缺少「进程」能力声明，运行命令已被拒绝。请重新安装本插件，或联系插件作者。"));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    /// <summary>换行会让列表项高度跳变，先压平再截断。</summary>
    private static string Shorten(string command)
    {
        string text = (command ?? "").Trim();
        if (text.Length == 0) return "";

        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 48 ? text : text.Substring(0, 47) + "…";
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters, string key) =>
        parameters != null && parameters.TryGetValue(key, out string? value) ? value ?? "" : "";
}
