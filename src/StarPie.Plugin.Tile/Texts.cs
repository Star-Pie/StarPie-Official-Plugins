using System.Collections.Generic;

namespace StarPie.Plugin.Tile;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeTileShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// 所以动作的显示名必须在插件里再声明一次。
/// </para>
/// <para>
/// <b>布局名刻意不在这里</b>：那 17 个布局的显示名从宿主服务现取
/// （<see cref="IHostWindowService.Layouts"/>），不登记第二份。布局表是宿主执行体的
/// 领域知识，抄一份的后果是「宿主加了新布局、插件下拉里没有」，或反过来
/// 「插件里能选、宿主执行体不认」—— 两种都是静默失效。
/// 这里只登记三个标记的文案（<c>Cycle</c> / <c>CycleBack</c> / <c>Restore</c>）：
/// 它们是「对布局的操作」而不是布局本身，宿主没有对应的清单可查。
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

    internal const string TileTitle = "平铺窗口";
    internal const string TileLayout = "平铺方式";
    internal const string TileCycle = "循环切换布局";
    internal const string TileCycleBack = "循环返回";
    internal const string TileRestore = "还原所有窗口（回到平铺前）";
    internal const string TileEmpty = "未选择平铺方式。";

    /// <summary>
    /// 清单漏声明 <c>WindowControl</c> 时的兜底提示。
    /// <para>
    /// 正常情况下走不到这里：本包清单已声明该能力。<b>但只要它发生，就必须出声</b> ——
    /// 否则用户看到的是「按下去什么也没发生」，而不是「这个包缺一行声明」。
    /// </para>
    /// </summary>
    internal const string CapabilityMissing =
        "本插件缺少「窗口控制」能力声明，该动作已被拒绝。请重新安装本插件，或联系插件作者。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("tile.title", TileTitle, "Tile Windows"),
        ("tile.layout", TileLayout, "Layout"),
        ("tile.cycle", TileCycle, "Cycle layouts"),
        ("tile.cycleBack", TileCycleBack, "Cycle previous"),
        ("tile.restore", TileRestore, "Restore all windows (pre-tile state)"),
        ("tile.empty", TileEmpty, "No layout selected. Please choose one in the action settings."),

        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Window control\" capability, so the action was rejected. " +
            "Please reinstall the plugin or contact the author."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["tile.title"] = "平鋪視窗",
        ["tile.layout"] = "平鋪方式",
        ["tile.cycle"] = "循環切換佈局",
        ["tile.cycleBack"] = "循環返回",
        ["tile.restore"] = "還原所有視窗（回到平鋪前）",
        ["tile.empty"] = "未選擇平鋪方式。",
        ["capability.missing"] = "本外掛缺少「視窗控制」能力宣告，該動作已被拒絕。請重新安裝本外掛，或聯絡外掛作者。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["tile.title"] = "ウィンドウを並べる",
        ["tile.layout"] = "並べ方",
        ["tile.cycle"] = "レイアウトを順番に切替",
        ["tile.cycleBack"] = "前のレイアウトへ",
        ["tile.restore"] = "すべてのウィンドウを元に戻す（並べる前の状態）",
        ["tile.empty"] = "並べ方が選択されていません。",
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
