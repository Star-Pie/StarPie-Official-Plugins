using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.ToggleTopmost;

/// <summary>
/// 动作「窗口置顶 / 取消置顶」。对应配置里的 <c>Type="ToggleTopmost"</c>。
/// <para>
/// <b>无参数。</b> 宿主执行体其实还支持一个「强制置顶」的取值（<c>"1"</c> / <c>"on"</c>），
/// 但界面上从来只写入空串 —— 也就是「切换」这一种行为。为一个界面上并不存在的选择
/// 声明参数字段，等于凭空发明一份用户无法理解的配置；将来真需要时再加字段即可。
/// </para>
/// </summary>
internal sealed class ToggleTopmostAction : IActionContribution
{
    private readonly IPluginContext _context;

    public ToggleTopmostAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "toggleTopmost",
        DisplayName = T("topmost.title", Texts.TopmostTitle),
        Description = null,
        Category = "",
        IconKey = null,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    /// <summary>
    /// 无参数。
    /// <para>
    /// 注意<b>不要</b>因为「执行体接受一个参数」就补一个字段：那个参数的默认语义
    /// （空串 = 切换）正是界面上唯一会产生的配置，补字段只会让统一表单多出一个
    /// 没人知道该怎么填的输入框。
    /// </para>
    /// </summary>
    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        try
        {
            // 传空串 = 切换当前置顶状态，与拆分前内建动作的行为一致。
            if (!_context.Windows.ToggleTopmost())
            {
                return Task.FromResult(ActionResult.Fail("未能切换窗口置顶状态，具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Error("切换窗口置顶被宿主拒绝：本插件未声明 WindowControl 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        return Task.FromResult(ActionResult.Empty);
    }
}
