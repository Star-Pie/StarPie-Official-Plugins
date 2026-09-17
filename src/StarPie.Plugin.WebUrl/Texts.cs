using System.Collections.Generic;

namespace StarPie.Plugin.WebUrl;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeWebUrlShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题与字段标签给了 zh-CN / zh-TW / en / ja 四套
/// （这些是用户真正会读到的）；帮助文案只给了 zh-CN / en，其余语言回落 zh-CN ——
/// 与宿主内置面板的现状一致，是如实告知而不是遗漏。
/// </para>
/// </summary>
internal static class Texts
{
    // ---------------------------------------------------------------- zh-CN（兜底语言，也是各处的 fallback）

    internal const string WebUrlTitle = "打开网址";
    internal const string WebUrlUrl = "网址";
    internal const string WebUrlBrowser = "打开方式";
    internal const string WebUrlBrowserDefault = "系统默认";
    internal const string WebUrlBrowserCustom = "自定义浏览器...";
    internal const string WebUrlCustomPath = "自定义浏览器路径";
    internal const string WebUrlCustomPathHelp = "仅当「打开方式」选择「自定义浏览器...」时生效。";
    internal const string WebUrlEmpty = "未填写网址。";
    internal const string WebUrlCustomMissing = "已选择「自定义浏览器」，但未指定浏览器可执行文件路径。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("weburl.title", WebUrlTitle, "Open URL"),
        ("weburl.url", WebUrlUrl, "URL"),
        ("weburl.browser", WebUrlBrowser, "Open with"),
        ("weburl.browser.default", WebUrlBrowserDefault, "System default"),
        ("weburl.browser.custom", WebUrlBrowserCustom, "Custom browser..."),
        ("weburl.customPath", WebUrlCustomPath, "Custom browser path"),
        ("weburl.customPathHelp", WebUrlCustomPathHelp, "Only used when \"Open with\" is set to \"Custom browser...\"."),
        ("weburl.empty", WebUrlEmpty, "No URL entered."),
        ("weburl.customMissing", WebUrlCustomMissing, "A custom browser was selected, but no executable path was given."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["weburl.title"] = "開啟網址",
        ["weburl.url"] = "網址",
        ["weburl.browser"] = "開啟方式",
        ["weburl.browser.default"] = "系統預設",
        ["weburl.browser.custom"] = "自訂瀏覽器...",
        ["weburl.customPath"] = "自訂瀏覽器路徑",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["weburl.title"] = "URLを開く",
        ["weburl.url"] = "URL",
        ["weburl.browser"] = "開く方法",
        ["weburl.browser.default"] = "システム既定",
        ["weburl.browser.custom"] = "カスタム ブラウザー...",
        ["weburl.customPath"] = "カスタム ブラウザーのパス",
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
