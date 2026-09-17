using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.Folder;

/// <summary>
/// 动作「打开文件夹」。对应配置里的 <c>Type="Folder"</c>，也认历史别名 <c>Type="OpenFolder"</c>。
/// </summary>
internal sealed class FolderAction : IActionContribution
{
    private readonly IPluginContext _context;

    public FolderAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "folder",
        DisplayName = T("folder.title", Texts.FolderTitle),
        Description = null,
        Category = "",
        IconKey = null,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    /// <summary>
    /// 参数声明。
    /// <para>
    /// 值<b>不限于真实目录</b>：执行体还认 <c>::{...}</c> 与 <c>shell:</c> 开头的 Shell 命名空间串
    /// （宿主手写面板上的「💻 此电脑」「🗑️ 回收站」两枚预设给的就是这类值），
    /// 也认文件路径（此时会打开所在目录并选中它）。所以校验<b>只挡空值</b> ——
    /// 一旦加上 <c>Directory.Exists</c> 这类的存在性检查，那两枚预设会被判成非法输入。
    /// </para>
    /// <para>
    /// 「常用目录」那五枚芯片是填值辅助，不是参数本身，因此留在宿主侧。
    /// </para>
    /// </summary>
    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("folder.path", Texts.FolderPath),
            LabelKey = "folder.path",
            Type = ParameterFieldType.Folder,
            Required = true,
            HelpText = T("folder.pathHelp", Texts.FolderPathHelp),
        },
    };

    /// <summary>
    /// <b>比拆分之前更严，是刻意的</b>：原执行体遇到空路径直接 <c>return</c>，按下去毫无反应。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string path = parameters != null &&
                      parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? value ?? ""
            : "";

        return string.IsNullOrWhiteSpace(path) ? T("folder.empty", Texts.FolderEmpty) : null;
    }

    /// <summary>列表副标题：取最后一段目录名，Shell 命名空间串原样回显。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string path = parameters != null &&
                      parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? (value ?? "").Trim().Trim('"')
            : "";

        if (path.Length == 0) return "";

        // Shell 命名空间（::{...} / shell:...）不按路径切分 —— 它的「最后一段」
        // 是一串 GUID，切出来不如整串好认。
        bool isShellNamespace =
            path.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

        if (!isShellNamespace)
        {
            int slash = path.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash < path.Length - 1) path = path.Substring(slash + 1);
        }

        return path.Length <= 48 ? path : path.Substring(0, 47) + "…";
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string path = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ActionResult.Fail(Texts.FolderEmpty));
        }

        if (!_context.Host.OpenFolder(path))
        {
            return Task.FromResult(ActionResult.Fail(
                $"未能打开目录「{path}」。它可能不存在、或当前权限不足；详情见插件日志。"));
        }

        return Task.FromResult(ActionResult.Empty);
    }
}
