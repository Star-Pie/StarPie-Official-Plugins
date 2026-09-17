using System.Collections.Generic;

namespace StarPie.Plugin.ShellTool;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeShellToolShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>18 项工具的显示名不在本表里</b>：那一份由宿主服务给出（<c>IHostShellService.Verbs</c>），
/// 插件不另抄一份。抄一份的后果是「宿主加了工具、插件里没有」，且不会有任何报错。
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

    internal const string ShellToolTitle = "系统与右键工具";
    internal const string ShellToolVerb = "功能标识";
    internal const string ShellToolVerbHelp = "填功能标识，例如 copy_path / lock_screen / run_as_admin。";
    internal const string ShellToolEmpty = "未选择系统工具，请指定一个功能标识。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("shell.tool.title", ShellToolTitle, "Shell & System Tools"),
        ("shell.tool.verb", ShellToolVerb, "Tool ID"),
        ("shell.tool.verbHelp", ShellToolVerbHelp, "Enter a tool ID such as copy_path, lock_screen or run_as_admin."),
        ("shell.tool.empty", ShellToolEmpty, "No system tool selected. Please specify a tool ID."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["shell.tool.title"] = "系統與右鍵工具",
        ["shell.tool.verb"] = "功能識別碼",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["shell.tool.title"] = "シェル・右クリックツール",
        ["shell.tool.verb"] = "ツール ID",
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
