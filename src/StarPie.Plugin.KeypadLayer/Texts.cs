using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.Plugin.KeypadLayer;

/// <summary>
/// 按键映射插件的本地化词条表。
/// 包含 zh-CN (简体中文，作为默认/兜底)、zh-TW (繁体中文)、en (英语)、ja (日语)。
/// </summary>
internal static class Texts
{
    // ---------------------------------------------------------------- 静态显示标记（语言无关键位标记，无中文散文）

    internal const string Category = "KeyMap";
    internal const string ActionDesc = "QWE/ASD/ZXC/R → Num7–Num0";
    internal const string FieldKeyMapHelp = "QWE/ASD/ZXC/R → Num7–Num0";

    // ---------------------------------------------------------------- zh-CN (默认/兜底)

    internal const string ActionTitle = "按键映射";
    internal const string FieldKeyMap = "键盘映射配置";
    internal const string Preview = "按键映射 (默认: QWE/ASD/ZXC/R ➔ 789/456/123/0)";
    internal const string PreviewMore = "... (共 {0} 项)";

    internal const string MsgActivated = "已开启按键映射（目标进程：{0}）";
    internal const string MsgDeactivated = "已关闭按键映射";

    internal const string ErrNoForeground = "未能获取当前前台窗口进程，无法激活按键映射。";
    internal const string ErrNoCapability = "插件未声明「按键映射 (InputRemapping)」能力，无法激活按键映射。";
    internal const string ErrBusyByOther = "按键映射当前已被其他插件占用（{0}），暂时无法激活。";
    internal const string ErrHostFailed = "按键映射服务执行异常，请查看插件日志。";
    internal const string ErrToggleFailed = "切换按键映射失败，请确认前台窗口焦点后重试。";

    // ---------------------------------------------------------------- 注册表 (zh-CN & en)

    internal static readonly (string Key, string ZhCn, string En)[] Base = new[]
    {
        ("keypad.title", ActionTitle, "Key Mapping"),
        ("keypad.desc", ActionDesc, ActionDesc),
        ("keypad.category", "按键映射", "Key Mapping"),

        ("keypad.field.keyMap", FieldKeyMap, "Keyboard Remapping"),
        ("keypad.field.keyMapHelp", FieldKeyMapHelp, FieldKeyMapHelp),

        ("keypad.preview", Preview, "Key Mapping (Default: QWE/ASD/ZXC/R ➔ 789/456/123/0)"),
        ("keypad.preview.more", PreviewMore, "... ({0} total)"),

        ("keypad.msg.activated", MsgActivated, "Key mapping activated (Target: {0})"),
        ("keypad.msg.deactivated", MsgDeactivated, "Key mapping deactivated"),

        ("keypad.err.noForeground", ErrNoForeground, "Unable to determine foreground process. Cannot activate key mapping."),
        ("keypad.err.noCapability", ErrNoCapability, "Plugin lacks InputRemapping capability. Cannot activate key mapping."),
        ("keypad.err.busyByOther", ErrBusyByOther, "Key mapping is currently in use by another plugin ({0})."),
        ("keypad.err.hostFailed", ErrHostFailed, "Key mapping service encountered an error. Please check plugin logs."),
        ("keypad.err.toggleFailed", ErrToggleFailed, "Failed to toggle key mapping. Please check window focus and try again.")
    };

    // ---------------------------------------------------------------- 繁体中文 (zh-TW)

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["keypad.title"] = "按鍵對應",
        ["keypad.desc"] = ActionDesc,
        ["keypad.category"] = "按鍵對應",
        ["keypad.field.keyMap"] = "鍵盤對應設定",
        ["keypad.field.keyMapHelp"] = FieldKeyMapHelp,
        ["keypad.preview"] = "按鍵對應 (預設: QWE/ASD/ZXC/R ➔ 789/456/123/0)",
        ["keypad.preview.more"] = "... (共 {0} 項)",
        ["keypad.msg.activated"] = "已開啟按鍵對應（目標處理程序：{0}）",
        ["keypad.msg.deactivated"] = "已關閉按鍵對應",
        ["keypad.err.noForeground"] = "未能取得目前前景視窗處理程序，無法啟用按鍵對應。",
        ["keypad.err.noCapability"] = "外掛程式未宣告「按鍵對應 (InputRemapping)」權限，無法啟用按鍵對應。",
        ["keypad.err.busyByOther"] = "按鍵對應目前已被其他外掛程式占用（{0}），暫時無法啟用。",
        ["keypad.err.hostFailed"] = "按鍵對應服務執行異常，請查看外掛程式記錄檔。",
        ["keypad.err.toggleFailed"] = "切換按鍵對應失敗，請確認前景視窗焦點後重試。"
    };

    // ---------------------------------------------------------------- 日语 (ja)

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["keypad.title"] = "キーマッピング",
        ["keypad.desc"] = ActionDesc,
        ["keypad.category"] = "キーマッピング",
        ["keypad.field.keyMap"] = "キーマッピング設定",
        ["keypad.field.keyMapHelp"] = FieldKeyMapHelp,
        ["keypad.preview"] = "キーマッピング (デフォルト: QWE/ASD/ZXC/R ➔ 789/456/123/0)",
        ["keypad.preview.more"] = "... (計 {0} 件)",
        ["keypad.msg.activated"] = "キーマッピングを有効化しました（対象：{0}）",
        ["keypad.msg.deactivated"] = "キーマッピングを無効化しました",
        ["keypad.err.noForeground"] = "フォアグラウンドプロセスの取得に失敗したため、キーマッピングを有効化できません。",
        ["keypad.err.noCapability"] = "プラグインにキーマッピング権限が付与されていないため、マッピングを有効化できません。",
        ["keypad.err.busyByOther"] = "キーマッピングは現在他のプラグイン（{0}）によって使用されているため、有効化できません。",
        ["keypad.err.hostFailed"] = "キーマッピングサービスの実行でエラーが発生しました。ログをご確認ください。",
        ["keypad.err.toggleFailed"] = "キーマッピングの切り替えに失敗しました。フォーカスを確認して再試行してください。"
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
