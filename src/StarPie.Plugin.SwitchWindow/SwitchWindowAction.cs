using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.SwitchWindow;

/// <summary>
/// 动作「切换窗口」。对应配置里的 <c>Type="SwitchWindow"</c>。
/// <para>
/// 参数是任务栏上的第几个槽位（从 1 开始，从左往右数）。宿主执行体通过 UIA 遍历
/// 任务栏的按钮并激活对应那一项，全程在后台线程上完成。
/// </para>
/// </summary>
internal sealed class SwitchWindowAction : IActionContribution
{
    /// <summary>
    /// 参数声明的上界。它<b>只是界面提示</b>，不是硬约束：任务栏能放多少个图标
    /// 取决于用户怎么设置，这里给 20 是「常见情况够用」的宽松值。
    /// 真正的判据是「正整数」—— 所以 <see cref="Validate"/> 只拦下界。
    /// </summary>
    private const int MaxSlotHint = 20;

    private readonly IPluginContext _context;

    public SwitchWindowAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "switchWindow",
        DisplayName = T("switch.title", Texts.SwitchTitle),
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
            Label = T("switch.index", Texts.SwitchIndex),
            LabelKey = "switch.index",
            Type = ParameterFieldType.Number,
            Required = true,
            DefaultValue = "1",
            Min = 1,
            Max = MaxSlotHint,
            HelpText = T("switch.help", Texts.SwitchHelp),
        },
    };

    /// <summary>
    /// <b>比原先更严，是刻意的</b>：宿主执行体对解析失败或非正数的参数会静默退回第 1 个槽位 ——
    /// 用户配的是「第 3 个」，按下去却是第 1 个应用被切出来，而且没有任何提示说明为什么。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string raw = Read(parameters);

        if (raw.Length == 0) return T("switch.empty", Texts.SwitchEmpty);

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return string.Format(T("switch.notNumber", Texts.SwitchNotNumber), raw);
        }

        return parsed < 1
            ? T("switch.tooSmall", Texts.SwitchTooSmall)
            : null;
    }

    /// <summary>列表副标题。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string raw = Read(parameters);
        return raw.Length == 0 ? "" : $"第 {raw} 个";
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string raw = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot < 1)
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            return Task.FromResult(ActionResult.Fail(Texts.SwitchEmpty));
        }

        try
        {
            if (!_context.Windows.ActivateTaskbarSlot(slot))
            {
                return Task.FromResult(ActionResult.Fail(
                    $"未能切换到任务栏第 {slot} 个窗口，具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Error("切换窗口被宿主拒绝：本插件未声明 WindowControl 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        // 返回成功只表示「宿主已受理」：真正的激活在后台线程上做，
        // 第 N 个槽位不存在时唯一能看到的线索是宿主日志。
        return Task.FromResult(ActionResult.Empty);
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters) =>
        parameters != null && parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? (value ?? "").Trim()
            : "";
}
