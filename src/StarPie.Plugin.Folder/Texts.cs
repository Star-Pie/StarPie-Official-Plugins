using System.Collections.Generic;

namespace StarPie.Plugin.Folder;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeFolderShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
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

    internal const string FolderTitle = "打开文件夹";
    internal const string FolderPath = "文件夹路径";
    internal const string FolderPathHelp = "支持普通路径、文件路径（会定位并选中该文件）以及 shell: 命名空间。";
    internal const string FolderEmpty = "未设置文件夹路径，请在动作设置里选择要打开的目录。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("folder.title", FolderTitle, "Open Folder"),
        ("folder.path", FolderPath, "Folder path"),
        ("folder.pathHelp", FolderPathHelp, "Plain paths, file paths (the containing folder opens with the file selected) and shell: namespaces are supported."),
        ("folder.empty", FolderEmpty, "No folder path set. Please choose a directory in the action settings."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["folder.title"] = "開啟資料夾",
        ["folder.path"] = "資料夾路徑",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["folder.title"] = "フォルダーを開く",
        ["folder.path"] = "フォルダーのパス",
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
