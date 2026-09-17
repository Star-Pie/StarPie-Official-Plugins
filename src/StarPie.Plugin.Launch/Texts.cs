using System.Collections.Generic;

namespace StarPie.Plugin.Launch;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeLaunchShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// 所以动作的显示名必须在插件里再声明一次。
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

    internal const string LaunchTitle = "启动程序";
    internal const string LaunchPath = "程序路径";
    internal const string LaunchPathHelp = "可执行文件或快捷方式的完整路径，也支持 shell:AppsFolder 形式的应用。";
    internal const string LaunchArguments = "启动参数";
    internal const string LaunchStandardUser = "以常规普通权限启动";
    internal const string LaunchStandardUserHelp =
        "当 StarPie 以管理员权限运行时，通过 Windows Shell 降权启动目标程序，恢复文件拖拽交互支持。";
    internal const string LaunchEmpty = "未设置要启动的程序，请在动作设置里选择可执行文件。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("launch.title", LaunchTitle, "Run App"),
        ("launch.path", LaunchPath, "Application path"),
        ("launch.pathHelp", LaunchPathHelp, "Full path to an executable or shortcut. shell:AppsFolder apps are also supported."),
        ("launch.arguments", LaunchArguments, "Arguments"),
        ("launch.standardUser", LaunchStandardUser, "Launch as standard user"),
        ("launch.standardUserHelp", LaunchStandardUserHelp,
            "When StarPie runs elevated, launch the target through the Windows shell token so file drag-and-drop keeps working."),
        ("launch.empty", LaunchEmpty, "No application selected. Please pick an executable in the action settings."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["launch.title"] = "啟動程式",
        ["launch.path"] = "程式路徑",
        ["launch.arguments"] = "啟動參數",
        ["launch.standardUser"] = "以一般使用者權限啟動",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["launch.title"] = "アプリ起動",
        ["launch.path"] = "アプリのパス",
        ["launch.arguments"] = "起動引数",
        ["launch.standardUser"] = "標準ユーザーとして起動",
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
