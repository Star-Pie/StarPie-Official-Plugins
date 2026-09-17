using System.Collections.Generic;

namespace StarPie.Plugin.Command;

/// <summary>
/// 本包的词条表。
/// <para>
/// <b>为什么要自带一份，而不是去读宿主的中文表</b>：插件拿到的 <see cref="II18nRegistry"/>
/// 只能注册与读取<b>自己命名空间下</b>的词条（<c>plugin.&lt;id&gt;.*</c>），读不到宿主的
/// <c>ActionTypeCommandShort</c> 这类键 —— 这是刻意的隔离，插件不该依赖宿主内部键名。
/// </para>
/// <para>
/// <b>终端的显示名不在本表里</b>：那一份由宿主服务给出（<c>IHostCommandService.Terminals</c>），
/// 插件不另抄一份。抄一份必然漂 —— 宿主支持新终端后，插件下拉里不会有它，而且不会有任何报错。
/// </para>
/// <para>
/// <b>覆盖范围</b>：标题与字段标签给了 zh-CN / zh-TW / en / ja 四套
/// （这些是用户真正会读到的）；描述与校验提示只给了 zh-CN / en，其余语言回落 zh-CN ——
/// 与宿主内置面板的现状一致，是如实告知而不是遗漏。
/// </para>
/// </summary>
internal static class Texts
{
    // ---------------------------------------------------------------- zh-CN（兜底语言，也是各处的 fallback）

    internal const string CommandTitle = "运行命令";
    internal const string CommandDesc = "在指定的终端中执行一条命令行语句。";
    internal const string CommandLine = "命令行";
    internal const string CommandTerminalLabel = "终端";
    internal const string CommandEmpty = "未填写要执行的命令，请在动作设置里输入命令行内容。";

    // ---------------------------------------------------------------- 注册表

    /// <summary>zh-CN 与 en 两套。<see cref="II18nRegistry.Register"/> 一次接收这两种语言。</summary>
    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("command.title", CommandTitle, "Run Command"),
        ("command.desc", CommandDesc, "Run a command line in the selected terminal."),
        ("command.line", CommandLine, "Command line"),
        ("command.terminal", CommandTerminalLabel, "Terminal"),
        ("command.empty", CommandEmpty, "No command entered. Please fill in the command line in the action settings."),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["command.title"] = "執行命令",
        ["command.line"] = "命令列",
        ["command.terminal"] = "終端",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["command.title"] = "コマンド実行",
        ["command.line"] = "コマンド",
        ["command.terminal"] = "ターミナル",
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
