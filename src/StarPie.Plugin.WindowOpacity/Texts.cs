using System.Collections.Generic;

namespace StarPie.Plugin.WindowOpacity;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份</b>：插件拿到的 <see cref="II18nRegistry"/> 只能注册与读取
/// <b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeWindowOpacityShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>带占位符的模板</b>（<c>{0}</c>）也在这里登记，由调用方 <c>string.Format</c> 填充。
/// 这样英文用户填错透明度时看到的也是英文提示，而不是一句中文。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题、字段标签与校验提示给了 zh-CN / zh-TW / en / ja 四套
/// （这些是用户真正会读到的）；帮助文案只给了 zh-CN / en，其余语言回落 zh-CN ——
/// 与宿主内置面板的现状一致，是如实告知而不是遗漏。
/// </para>
/// </summary>
internal static class Texts
{
    internal const string OpacityTitle = "窗口透明度";
    internal const string OpacityValue = "透明度 (%)";

    // 下面两条<b>刻意带占位符</b>：范围由宿主服务给出（IHostWindowService.OpacityMinPercent /
    // OpacityMaxPercent），不在这里写死数字。写死的话宿主调整范围后，
    // 提示文案会继续把旧范围告诉用户 —— 而用户会照着它填。
    internal const string OpacityHelp = "取值 {0}~{1}，{1} 表示完全不透明。";
    internal const string OpacityEmpty = "未设置窗口透明度。";
    internal const string OpacityNotNumber = "透明度「{0}」不是有效数字，请填写范围内的数值。";
    internal const string OpacityOutOfRange = "透明度需在 {0}~{1} 之间（当前填写 {2}）。";

    /// <summary>
    /// 清单漏声明 <c>WindowControl</c> 时的兜底提示。
    /// 正常走不到这里；但只要它发生就必须出声 —— 否则用户看到的是
    /// 「按下去什么也没发生」，而不是「这个包缺一行声明」。
    /// </summary>
    internal const string CapabilityMissing =
        "本插件缺少「窗口控制」能力声明，该动作已被拒绝。请重新安装本插件，或联系插件作者。";

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("opacity.title", OpacityTitle, "Window Opacity"),
        ("opacity.value", OpacityValue, "Opacity (%)"),
        ("opacity.help", OpacityHelp, "Range {0}-{1}; {1} means fully opaque."),
        ("opacity.empty", OpacityEmpty, "No opacity value set. Please fill it in the action settings."),
        ("opacity.notNumber", OpacityNotNumber,
            "\"{0}\" is not a valid number. Please enter a value within the allowed range."),
        ("opacity.outOfRange", OpacityOutOfRange, "Opacity must be between {0} and {1} (currently {2})."),

        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Window control\" capability, so the action was rejected. " +
            "Please reinstall the plugin or contact the author."),
    };

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["opacity.title"] = "視窗透明度",
        ["opacity.value"] = "透明度 (%)",
        ["opacity.empty"] = "未設定視窗透明度。",
        ["capability.missing"] = "本外掛缺少「視窗控制」能力宣告，該動作已被拒絕。請重新安裝本外掛，或聯絡外掛作者。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["opacity.title"] = "ウィンドウの透明度",
        ["opacity.value"] = "透明度 (%)",
        ["opacity.empty"] = "透明度が設定されていません。",
        ["capability.missing"] =
            "このプラグインは「ウィンドウ制御」権限を宣言していないため、操作は拒否されました。再インストールするか作者に連絡してください。",
    };

    /// <summary>把三张表一次性登记进宿主。</summary>
    internal static void Register(IPluginContext context)
    {
        foreach ((string key, string zhCn, string en) in Base)
        {
            context.I18n.Register(key, zhCn, en);
        }

        context.I18n.RegisterTable("zh-TW", ZhTw);
        context.I18n.RegisterTable("ja", Ja);
    }
}
