using System.Collections.Generic;

namespace StarPie.Plugin.MoveMonitor;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份</b>：插件拿到的 <see cref="II18nRegistry"/> 只能注册与读取
/// <b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeMoveMonitorShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题给了 zh-CN / zh-TW / en / ja 四套；能力缺失提示同四套。
/// </para>
/// </summary>
internal static class Texts
{
    internal const string MonitorTitle = "窗口移到下一屏";

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
        ("monitor.title", MonitorTitle, "Move to Next Monitor"),
        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Window control\" capability, so the action was rejected. " +
            "Please reinstall the plugin or contact the author."),
    };

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["monitor.title"] = "視窗移到下一螢幕",
        ["capability.missing"] = "本外掛缺少「視窗控制」能力宣告，該動作已被拒絕。請重新安裝本外掛，或聯絡外掛作者。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["monitor.title"] = "次のモニターへ移動",
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
