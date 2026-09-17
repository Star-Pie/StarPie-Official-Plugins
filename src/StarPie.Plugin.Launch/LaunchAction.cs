using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Launch;

/// <summary>
/// 动作「启动程序」。对应配置里的 <c>Type="Launch"</c>。
/// <para>
/// <b>参数键用 <see cref="HostActionFields"/> 而不是自定义短名</b>：这个动作在用户配置里
/// 仍然是老形态（<c>Parameter</c> / <c>Arguments</c> / <c>RunAsStandardUser</c> 三个裸字段），
/// 宿主会在调用前用字段投影器现读现装成字典，键就是这些常量值。
/// 用常量而不是裸字符串，是为了让「键写错」变成编译错误而不是运行期永远读到空值。
/// </para>
/// <para>
/// <b>一处已知的表达力缺口</b>：宿主的手写面板上另有三枚辅助按钮
/// （📦 软件库选择 / 🎯 捕捉运行窗口 / 📂 浏览），它们是「帮用户填路径」的辅助，不是参数，
/// 因此仍然留在宿主侧，不随本包外移。
/// </para>
/// </summary>
internal sealed class LaunchAction : IActionContribution
{
    private readonly IPluginContext _context;

    public LaunchAction(IPluginContext context) => _context = context;

    // 属性而不是缓存字段：界面语言可以在运行时切换，每次访问重新取词条，界面才会跟着语言走。
    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "launch",
        DisplayName = T("launch.title", Texts.LaunchTitle),
        Description = null,

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
            Label = T("launch.path", Texts.LaunchPath),
            LabelKey = "launch.path",
            Type = ParameterFieldType.File,
            Required = true,
            HelpText = T("launch.pathHelp", Texts.LaunchPathHelp),
        },
        new()
        {
            Key = HostActionFields.Arguments,
            Label = T("launch.arguments", Texts.LaunchArguments),
            LabelKey = "launch.arguments",
            Type = ParameterFieldType.Text,
            Placeholder = "--portable",
        },
        new()
        {
            Key = HostActionFields.RunAsStandardUser,
            Label = T("launch.standardUser", Texts.LaunchStandardUser),
            LabelKey = "launch.standardUser",
            Type = ParameterFieldType.Bool,
            DefaultValue = "false",
            HelpText = T("launch.standardUserHelp", Texts.LaunchStandardUserHelp),
        },
    };

    /// <summary>
    /// <b>比拆分之前更严，是刻意的</b>：原执行体遇到空路径直接 <c>return</c>，
    /// 用户按下去什么也不会发生、也没有任何提示。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string path = parameters != null &&
                      parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? value ?? ""
            : "";

        return string.IsNullOrWhiteSpace(path) ? T("launch.empty", Texts.LaunchEmpty) : null;
    }

    /// <summary>列表副标题：只显示程序文件名，比整条路径易读。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string path = parameters != null &&
                      parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? (value ?? "").Trim().Trim('"')
            : "";

        if (path.Length == 0) return "";

        // 只截文件名：`C:\Program Files\Very\Long\Path\app.exe` 在列表里读起来毫无意义，
        // 而 `app.exe` 一眼就知道是哪个程序。取不到文件名（例如 shell:AppsFolder\...）就回显原串。
        int slash = path.LastIndexOfAny(new[] { '\\', '/' });
        string name = slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;

        return name.Length <= 48 ? name : name.Substring(0, 47) + "…";
    }

    /// <summary>
    /// 执行。
    /// <para>
    /// <b>线程约束</b>：调用方（<c>PluginInvoker</c>）在唯一的动作线程上以同步方式等待本方法，
    /// 因此实现里绝不能出现真正的异步等待 —— 一旦 <c>await</c> 到别的上下文就会死锁。
    /// 全程同步完成，<see cref="Task.FromResult{TResult}"/> 只是为了让签名与契约一致。
    /// </para>
    /// <para>
    /// <b>失败不再抛异常，这是刻意接受的代价</b>：拆分之前本动作作为内建动作，
    /// 失败会让异常冒泡到 <c>ActionExecutor.Execute</c> 的 <c>catch</c> 并弹 MessageBox。
    /// 现在它是一段独立程序集，异常<b>绝不能</b>冒泡到那里 —— 无人值守时那个 MessageBox
    /// 会把整个动作线程卡死。而宿主注入的 <see cref="IHostActionInvoker"/> 把宿主的异常
    /// 统一吞掉、只回一个 bool，于是这里拿不到失败原因。
    /// 结果是：失败提示从「带原因的对话框」变成「一句可操作的话 + 日志里的事实」。
    /// 要恢复原来的信息量，需要让 <see cref="IHostActionInvoker"/> 能回传失败原因 ——
    /// 那是一次 SDK 契约变更，留给下一步做，不在本次范围内。
    /// </para>
    /// </summary>
    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string path = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(path))
        {
            // 正常路径走不到这里：宿主在调用前已用 Validate 拦过。
            // 直接调用本实现时（例如自检）没有那层保护，所以再兜一次。
            return Task.FromResult(ActionResult.Fail(Texts.LaunchEmpty));
        }

        string arguments = input?.Parameter(HostActionFields.Arguments) ?? "";
        bool runAsStandardUser = input?.Bool(HostActionFields.RunAsStandardUser) ?? false;

        if (!_context.Host.Launch(path, arguments, runAsStandardUser))
        {
            return Task.FromResult(ActionResult.Fail(
                $"未能启动「{path}」。请确认该路径存在且可执行；具体原因见插件日志。"));
        }

        return Task.FromResult(ActionResult.Empty);
    }
}
