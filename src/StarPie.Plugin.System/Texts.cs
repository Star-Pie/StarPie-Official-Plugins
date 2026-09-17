using System.Collections.Generic;

namespace StarPie.Plugin.System;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeSystemShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// 所以动作的显示名必须在插件里再声明一次（这里与宿主那份逐字一致）。
/// </para>
/// <para>
/// <b>预设本身的显示名不在本表里</b>：那一份由宿主服务给出
/// （<c>IHostSystemService.Presets</c>），插件不另抄一份。抄一份必然漂 ——
/// 宿主加一个系统功能后，插件下拉里不会有它，而且不会有任何报错，
/// 用户只知道「这个新功能选不到」。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题与「未选择」提示给了 zh-CN / zh-TW / en / ja 四套；
/// 描述与「认不出这个键」只给了 zh-CN / en，其余语言回落 zh-CN ——
/// 与宿主内置面板的现状一致，是如实告知而不是遗漏。
/// </para>
/// </summary>
internal static class Texts
{
    // ---------------------------------------------------------------- zh-CN（兜底语言，也是各处的 fallback）

    internal const string SystemTitle = "系统控制";
    internal const string SystemDesc = "触发一个系统功能：窗口控制、任务视图、音量与媒体、锁屏与电源等。";
    internal const string SystemPresetLabel = "系统功能";
    internal const string SystemEmpty = "未选择系统功能，请在动作设置里指定要执行的操作。";

    /// <summary>
    /// 「这个预设键已经不认识了」的提示模板（<c>{0}</c> 是键本身）。
    /// <para>
    /// 这一条不是凑数的错误处理：老配置里沉淀着上一代预设键，而宿主的
    /// <c>ExecuteSystem</c> 对认不出的键<b>什么也不做</b>。在本次迁移之前，
    /// 那种情况下用户按下扇区后既没有反应也没有提示 —— 与「按键本身没生效」一模一样。
    /// 现在这条消息是唯一能让用户看出「是配置里的取值过期了」的地方。
    /// </para>
    /// </summary>
    internal const string SystemUnknown =
        "未能识别系统功能「{0}」，本次触发没有执行任何操作。它多半是旧版本留下的取值，"
        + "请到「设置 → 手势与动作」为这个扇区重新选择系统功能。";

    internal const string CapabilityMissing =
        "本插件缺少「模拟输入」能力声明，该动作已被拒绝。请重新安装本插件，或联系插件作者。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("system.title", SystemTitle, "System Control"),
        ("system.desc", SystemDesc,
            "Trigger a system function: window control, task view, volume and media, lock and power, etc."),
        ("system.preset", SystemPresetLabel, "System function"),
        ("system.empty", SystemEmpty,
            "No system function selected. Please pick one in the action settings."),

        // 模板原样登记：两段文案里只有 {0} 一个占位符，没有需要转义的孤立花括号。
        // 调用方用 string.Format(CultureInfo.InvariantCulture, ...) 填充。
        ("system.unknown", SystemUnknown,
            "System function \"{0}\" was not recognized, so nothing was executed. "
            + "It is probably a value left over from an older version — please pick the system function "
            + "again under \"Settings → Gestures & Actions\"."),

        ("capability.missing", CapabilityMissing,
            "This plugin does not declare the \"Simulate input\" capability, so the action was rejected. "
            + "Please reinstall the plugin or contact the author."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["system.title"] = "系統控制",
        ["system.preset"] = "系統功能",
        ["system.empty"] = "未選擇系統功能，請在動作設定裡指定要執行的操作。",
        ["system.unknown"] =
            "未能識別系統功能「{0}」，本次觸發沒有執行任何操作。它多半是舊版本留下的取值，"
            + "請到「設定 → 手勢與動作」為這個扇區重新選擇系統功能。",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["system.title"] = "システム",
        ["system.preset"] = "システム機能",
        ["system.empty"] = "システム機能が選択されていません。動作設定で指定してください。",
        ["system.unknown"] =
            "システム機能「{0}」を識別できなかったため、何も実行しませんでした。"
            + "古いバージョンの値が残っている可能性があります。「設定 → ジェスチャーと動作」で"
            + "選び直してください。",
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
