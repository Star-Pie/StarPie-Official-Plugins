using System.Collections.Generic;

namespace StarPie.Plugin.Ocr;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeOcrShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// 所以动作的显示名必须在插件里再声明一次（这里与宿主那份逐字一致）。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题给了 zh-CN / zh-TW / en / ja 四套；能力缺失提示同四套。
/// </para>
/// </summary>
internal static class Texts
{
    internal const string OcrTitle = "截屏识字 (OCR)";

    /// <summary>
    /// 清单漏声明 <c>ScreenCapture</c> 时的兜底提示。
    /// 正常走不到这里；但只要它发生就必须出声 —— 否则用户看到的是
    /// 「按下去什么也没发生」，而不是「这个包缺一行声明」。
    /// </summary>
    internal const string CapabilityMissing =
        "本插件缺少「屏幕截取」能力声明，该动作已被拒绝。请重新安装本插件，或联系插件作者。";

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("ocr.title", OcrTitle, "Screen OCR"),
        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Screen capture\" capability, so the action was rejected. " +
            "Please reinstall the plugin or contact the author."),
    };

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["ocr.title"] = "截圖識字 (OCR)",
        ["capability.missing"] = "本外掛缺少「螢幕擷取」能力宣告，該動作已被拒絕。請重新安裝本外掛，或聯絡外掛作者。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["ocr.title"] = "画面OCR",
        ["capability.missing"] =
            "このプラグインは「画面キャプチャ」権限を宣言していないため、操作は拒否されました。再インストールするか作者に連絡してください。",
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
