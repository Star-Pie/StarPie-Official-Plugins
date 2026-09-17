using System.Collections.Generic;

namespace StarPie.Plugin.SwitchWindow;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份</b>：插件拿到的 <see cref="II18nRegistry"/> 只能注册与读取
/// <b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeSwitchWindowShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>带占位符的模板</b>（<c>{0}</c>）也在这里登记，由调用方 <c>string.Format</c> 填充。
/// 这样英文用户填错序号时看到的也是英文提示，而不是一句中文。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题、字段标签与校验提示给了 zh-CN / zh-TW / en / ja 四套
/// （这些是用户真正会读到的）；帮助文案只给了 zh-CN / en，其余语言回落 zh-CN ——
/// 与宿主内置面板的现状一致，是如实告知而不是遗漏。
/// </para>
/// </summary>
internal static class Texts
{
    internal const string SwitchTitle = "切换窗口";
    internal const string SwitchIndex = "任务栏位置";
    internal const string SwitchHelp = "任务栏上从左往右数的第几个图标，从 1 开始。";
    internal const string SwitchEmpty = "未设置任务栏位置。";
    internal const string SwitchNotNumber = "「{0}」不是有效的序号，请填写一个正整数。";
    internal const string SwitchTooSmall = "任务栏位置需从 1 开始计数。";

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
        ("switch.title", SwitchTitle, "Switch Window"),
        ("switch.index", SwitchIndex, "Taskbar position"),
        ("switch.help", SwitchHelp, "The icon position on the taskbar, counted from the left starting at 1."),
        ("switch.empty", SwitchEmpty, "No taskbar position set. Please fill it in the action settings."),
        ("switch.notNumber", SwitchNotNumber, "\"{0}\" is not a valid index. Please enter a positive integer."),
        ("switch.tooSmall", SwitchTooSmall, "The taskbar position starts counting from 1."),

        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Window control\" capability, so the action was rejected. " +
            "Please reinstall the plugin or contact the author."),
    };

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["switch.title"] = "切換視窗",
        ["switch.index"] = "工作列位置",
        ["switch.empty"] = "未設定工作列位置。",
        ["capability.missing"] = "本外掛缺少「視窗控制」能力宣告，該動作已被拒絕。請重新安裝本外掛，或聯絡外掛作者。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["switch.title"] = "ウィンドウ切替",
        ["switch.index"] = "タスクバーの位置",
        ["switch.empty"] = "タスクバーの位置が設定されていません。",
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
