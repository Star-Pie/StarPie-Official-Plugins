using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.WindowOpacity;

/// <summary>
/// 动作「设置窗口透明度」。对应配置里的 <c>Type="WindowOpacity"</c>。
/// <para>
/// 参数是一个整数百分比（100 = 完全不透明）。<b>取值范围不在这里写死</b> ——
/// 它从宿主服务现取（<see cref="IHostWindowService.OpacityMinPercent"/> /
/// <see cref="IHostWindowService.OpacityMaxPercent"/>），参数的 <c>Min</c>/<c>Max</c>、
/// 帮助文案与越界提示全部由它生成。写死一份的后果是宿主调整范围之后，
/// 插件仍把旧范围展示给用户，并据此判断合法性。
/// </para>
/// </summary>
internal sealed class WindowOpacityAction : IActionContribution
{
    private readonly IPluginContext _context;

    public WindowOpacityAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "windowOpacity",
        DisplayName = T("opacity.title", Texts.OpacityTitle),
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
            Label = T("opacity.value", Texts.OpacityValue),
            LabelKey = "opacity.value",
            Type = ParameterFieldType.Number,
            Required = true,
            DefaultValue = "80",
            Min = _context.Windows.OpacityMinPercent,
            Max = _context.Windows.OpacityMaxPercent,
            HelpText = string.Format(
                T("opacity.help", Texts.OpacityHelp),
                _context.Windows.OpacityMinPercent,
                _context.Windows.OpacityMaxPercent),
        },
    };

    /// <summary>
    /// 校验。
    /// <para>
    /// 空值拦下是<b>刻意的收紧</b>：宿主执行体对解析失败的参数会静默退回 50%，
    /// 用户按下去会看到窗口变半透明 —— 而他配的可能是 80%。
    /// 「用了一个你没设过的值」比「告诉你没设过」糟糕得多。
    /// </para>
    /// <para>
    /// 越界同样拦下：执行体虽然会钳制到合法范围，但填 150 的人显然理解错了这个参数的含义，
    /// 悄悄把它变成上界只会让误解继续存在。
    /// </para>
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string raw = Read(parameters);

        if (raw.Length == 0) return T("opacity.empty", Texts.OpacityEmpty);

        // 用不变文化解析：宿主写入的是 "80" 这样的不变文化字面量，
        // 按系统区域设置解析在部分区域会得到不同的结果。
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return string.Format(T("opacity.notNumber", Texts.OpacityNotNumber), raw);
        }

        double min = _context.Windows.OpacityMinPercent;
        double max = _context.Windows.OpacityMaxPercent;

        return parsed < min || parsed > max
            ? string.Format(T("opacity.outOfRange", Texts.OpacityOutOfRange), min, max, raw)
            : null;
    }

    /// <summary>列表副标题：把裸数字补上单位与百分号。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string raw = Read(parameters);
        return raw.Length == 0 ? "" : raw + "%";
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string raw = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            // 正常路径走不到这里：宿主调用前已用 Validate 拦过。
            return Task.FromResult(ActionResult.Fail(Texts.OpacityEmpty));
        }

        try
        {
            // 解析与钳制都由宿主执行体负责（见 WindowTiler.SetWindowOpacity），
            // 这里原样透传 —— 参数之所以是字符串而不是 int，就是为了不复制第二份解析规则。
            if (!_context.Windows.SetOpacity(raw))
            {
                return Task.FromResult(ActionResult.Fail(
                    $"未能设置窗口透明度（{raw}%），具体原因见插件日志。"));
            }
        }
        catch (PluginCapabilityDeniedException ex)
        {
            _context.Log.Error("设置窗口透明度被宿主拒绝：本插件未声明 WindowControl 能力", ex);
            return Task.FromResult(ActionResult.Fail(T("capability.missing", Texts.CapabilityMissing)));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    private static string Read(IReadOnlyDictionary<string, string>? parameters) =>
        parameters != null && parameters.TryGetValue(HostActionFields.Parameter, out string? value)
            ? (value ?? "").Trim()
            : "";
}
