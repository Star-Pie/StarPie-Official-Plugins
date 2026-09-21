using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.Plugin.CadCommand;

/// <summary>
/// CAD 模拟按键插件的本地化词条表。
/// 包含 zh-CN (简体中文，作为默认/兜底)、zh-TW (繁体中文)、en (英语)、ja (日语)。
/// </summary>
internal static class Texts
{
    // ---------------------------------------------------------------- zh-CN (默认)

    internal const string ActionTitle = "CAD 模拟按键 / 命令输入";
    internal const string ActionDesc = "向当前窗口模拟输入 CAD 命令文本与按键，内置 Unicode 防输入法拦截、^C^C 前置取消与空格/回车提交。";

    internal const string FieldText = "CAD 命令 / 输入文本";
    internal const string FieldTextHelp = "要输入的命令内容。例如 REC、PL、LINE、C、ZOOM E，或多行宏。";

    internal const string FieldSubmitKey = "结尾提交按键";
    internal const string FieldSubmitKeyHelp = "输入命令后自动按下的键。CAD 经典首选空格 (Space) 或回车 (Enter)。";

    internal const string FieldPrefixEsc = "执行前取消旧命令";
    internal const string FieldPrefixEscHelp = "发送 ESC 清理命令行。两次 ESC 相当于 CAD 宏的 ^C^C，可彻底重置正在运行的命令。";

    internal const string FieldSendMode = "输入模式";
    internal const string FieldSendModeHelp = "Unicode 字符流模式可规避拼音输入法弹窗阻断；剪贴板模式适合超长 Lisp 脚本。";

    internal const string FieldPreDelay = "窗口激活等待延时 (ms)";
    internal const string FieldPreDelayHelp = "等待 StarPie 轮盘关闭并将输入焦点归还 CAD 窗口的等待时间，极速瞬发推荐 0ms。";

    internal const string FieldMultiLine = "逐行连续执行模式";
    internal const string FieldMultiLineHelp = "若为多行文本，按行逐一输入并在每行后发送提交键。";

    internal const string TextEmpty = "未填写要执行的 CAD 命令，请在动作设置里输入内容。";

    internal const string SubmitEnter = "回车 (Enter)";
    internal const string SubmitSpace = "空格 (Space - CAD经典)";
    internal const string SubmitNone = "无 (不提交，保留在命令行等待输入参数)";
    internal const string SubmitTab = "制表键 (Tab)";

    internal const string EscTwice = "两次 ESC (^C^C - CAD标准重置)";
    internal const string EscOnce = "一次 ESC";
    internal const string EscNone = "不发送";

    internal const string ModeFastUnicode = "极速瞬发 (0延时批量注入，一滑即用，强烈推荐)";
    internal const string ModeUnicode = "标准逐字 (带安全延时，防丢字)";
    internal const string ModePaste = "剪贴板粘贴 (Ctrl+V)";

    // ---------------------------------------------------------------- 注册表 (zh-CN & en)

    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("cad.title", ActionTitle, "CAD Command / Text Keystroke"),
        ("cad.desc", ActionDesc, "Simulate typing CAD commands into the active window with IME bypass, ^C^C cancel, and Space/Enter submit."),

        ("cad.text", FieldText, "CAD Command / Text"),
        ("cad.textHelp", FieldTextHelp, "Command to enter, e.g. REC, PL, LINE, C, ZOOM E, or multi-line scripts."),

        ("cad.submitKey", FieldSubmitKey, "Submit Key"),
        ("cad.submitKeyHelp", FieldSubmitKeyHelp, "Key pressed after typing. Space or Enter is standard for CAD."),

        ("cad.prefixEsc", FieldPrefixEsc, "Pre-execution Cancel (ESC)"),
        ("cad.prefixEscHelp", FieldPrefixEscHelp, "Send ESC to clean the command line. Twice ESC (^C^C) resets any ongoing command."),

        ("cad.sendMode", FieldSendMode, "Input Mode"),
        ("cad.sendModeHelp", FieldSendModeHelp, "Fast Unicode batches characters instantly (0 delay); standard stream has safe inter-char delays."),

        ("cad.preDelay", FieldPreDelay, "Activation Delay (ms)"),
        ("cad.preDelayHelp", FieldPreDelayHelp, "Wait time after wheel closes for CAD to capture input focus, 0ms recommended for instant execution."),

        ("cad.multiLine", FieldMultiLine, "Line-by-line Execution"),
        ("cad.multiLineHelp", FieldMultiLineHelp, "For multi-line scripts, send and submit each line in sequence."),

        ("cad.empty", TextEmpty, "No CAD command specified. Please enter content in action settings."),

        ("cad.opt.enter", SubmitEnter, "Enter"),
        ("cad.opt.space", SubmitSpace, "Space (CAD standard)"),
        ("cad.opt.none", SubmitNone, "None (Keep in command line)"),
        ("cad.opt.tab", SubmitTab, "Tab"),

        ("cad.opt.escTwice", EscTwice, "Twice ESC (^C^C - Standard reset)"),
        ("cad.opt.escOnce", EscOnce, "Once ESC"),
        ("cad.opt.escNone", EscNone, "Do not send"),

        ("cad.opt.modeFastUnicode", ModeFastUnicode, "Instant Batch (0 delay, recommended)"),
        ("cad.opt.modeUnicode", ModeUnicode, "Standard Stream (Safe delay)"),
        ("cad.opt.modePaste", ModePaste, "Clipboard Paste (Ctrl+V)"),
    };

    // ---------------------------------------------------------------- 其余语言

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["cad.title"] = "CAD 模擬按鍵 / 指令輸入",
        ["cad.desc"] = "向目前視窗模擬輸入 CAD 指令文字與按鍵，內建 Unicode 防輸入法攔截、^C^C 前置取消與空白鍵/Enter提交。",
        ["cad.text"] = "CAD 指令 / 輸入文字",
        ["cad.submitKey"] = "結尾提交按鍵",
        ["cad.prefixEsc"] = "執行前取消舊指令",
        ["cad.sendMode"] = "輸入模式",
        ["cad.preDelay"] = "視窗啟用等待延遲 (ms)",
        ["cad.multiLine"] = "逐行連續執行模式",
        ["cad.empty"] = "未填寫要執行的 CAD 指令，請在動作設定中輸入內容。",
        ["cad.opt.enter"] = "Enter (回車)",
        ["cad.opt.space"] = "空白鍵 (Space - CAD經典)",
        ["cad.opt.none"] = "無 (不提交，保留在命令列等待輸入參數)",
        ["cad.opt.tab"] = "Tab",
        ["cad.opt.escTwice"] = "兩次 ESC (^C^C - CAD標準重置)",
        ["cad.opt.escOnce"] = "一次 ESC",
        ["cad.opt.escNone"] = "不發送",
        ["cad.opt.modeFastUnicode"] = "極速瞬發 (0延遲批量注入，一滑即用，強烈推薦)",
        ["cad.opt.modeUnicode"] = "標準逐字 (帶安全延遲，防丟字)",
        ["cad.opt.modePaste"] = "剪貼簿貼上 (Ctrl+V)",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["cad.title"] = "CAD キーストローク / コマンド入力",
        ["cad.desc"] = "CAD コマンドとキーストロークをアクティブウィンドウに入力します（IME回避、^C^C キャンセル、Space/Enter 確定対応）。",
        ["cad.text"] = "CAD コマンド / 入力テキスト",
        ["cad.submitKey"] = "確定キー",
        ["cad.prefixEsc"] = "実行前コマンドクリア (ESC)",
        ["cad.sendMode"] = "入力モード",
        ["cad.preDelay"] = "フォーカス待機時間 (ms)",
        ["cad.multiLine"] = "行ごとの連続実行モード",
        ["cad.empty"] = "CAD コマンドが入力されていません。",
        ["cad.opt.enter"] = "Enter",
        ["cad.opt.space"] = "スペース (Space - CAD推奨)",
        ["cad.opt.none"] = "なし (確定せず維持)",
        ["cad.opt.tab"] = "Tab",
        ["cad.opt.escTwice"] = "ESC 2回 (^C^C - CAD標準リセット)",
        ["cad.opt.escOnce"] = "ESC 1回",
        ["cad.opt.escNone"] = "送信しない",
        ["cad.opt.modeFastUnicode"] = "超高速一括入力 (0遅延、推奨)",
        ["cad.opt.modeUnicode"] = "標準順次入力 (安全遅延あり)",
        ["cad.opt.modePaste"] = "クリップボード貼り付け (Ctrl+V)",
    };

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
