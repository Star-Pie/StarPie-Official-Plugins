using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.MoveMonitor;

/// <summary>
/// 动作「窗口移到下一屏」。对应配置里的 <c>Type="MoveMonitor"</c>。
/// <para>
/// <b>无参数</b>：目标显示器由宿主执行体枚举报当前窗口所在显示器、再取列表里的下一个决定，
/// 用户在界面上没有任何可配置项 —— 所以这里一个字段都不声明。
/// </para>
/// </summary>
internal sealed class MoveMonitorAction : IActionContribution
{
    private readonly IPluginContext _context;

    public MoveMonitorAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "moveMonitor",
        DisplayName = T("monitor.title", Texts.MonitorTitle),
        Description = null,
        Category = "",
        IconKey = null,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        try
        {
            if (!_context.Windows.MoveToNextMonitor())
            {
                return Task.FromResult(ActionResult.Fail("未能把窗口移到下一屏，具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Error("移动窗口到下一屏被宿主拒绝：本插件未声明 WindowControl 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        return Task.FromResult(ActionResult.Empty);
    }
}
