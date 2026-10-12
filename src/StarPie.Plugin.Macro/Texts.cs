using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.Plugin.Macro;

/// <summary>
/// 按键宏与多步骤脚本插件的本地化词条表。
/// 完整覆盖 zh-CN (简体中文)、zh-TW (繁体中文)、en (英语)、ja (日语)。
/// </summary>
internal static class Texts
{
    internal const string ActionTitle = "按键宏 / 多步骤脚本";
    internal const string ActionDesc = "按顺序执行多步组合键、单键、文本输入、剪贴板与延时等待，轻松搞定没有单一快捷键的多步菜单操作。";
    internal const string ActionCategory = "效率工具";

    internal const string FieldScript = "多步骤脚本 (Macro DSL)";
    internal const string FieldScriptHelp = "每行一条指令：HOTKEY: 组合键 | KEY: 单键 | TEXT: 文本 | PASTE: 粘贴文本 | CLIPBOARD: 写入剪贴板 | WAIT: 毫秒数（支持 # 注释，支持 *N 重复如 KEY: Down * 3）。";
    internal const string FieldScriptPlaceholder =
        "# 示例：唤出菜单 -> 等待 -> 按键选择 -> 输入名称 -> 回车确认\n" +
        "HOTKEY: Alt+F\n" +
        "WAIT: 100\n" +
        "KEY: E\n" +
        "WAIT: 150\n" +
        "TEXT: Report_2026\n" +
        "KEY: Enter";

    internal const string FieldPreDelay = "轮盘退场焦点缓冲 (ms)";
    internal const string FieldPreDelayHelp = "手势释放后等待目标窗口恢复输入焦点的缓冲毫秒数（0 ~ 1000ms，推荐 50ms）。";

    internal const string FieldStepDelay = "默认步间间隔 (ms)";
    internal const string FieldStepDelayHelp = "相邻两个未显式插入 WAIT 的动作步骤之间的默认等待毫秒数（0 ~ 500ms，推荐 30ms）。";

    internal const string FieldRepeatCount = "整体重复次数";
    internal const string FieldRepeatCountHelp = "整个多步骤脚本循环执行的次数（1 ~ 20 次，默认 1 次）。";

    internal const string ErrEmptyScript = "未填写有效的宏步骤。请至少输入一条指令（如 HOTKEY: Alt+F 或 KEY: Enter）。";
    internal const string ErrMissingColon = "第 {0} 行语法错误：缺少冒号 ':'（正确示例：HOTKEY: Alt+F 或 WAIT: 100）。";
    internal const string ErrUnknownDirective = "第 {0} 行未知指令 '{1}'。支持指令：HOTKEY, KEY, TEXT, PASTE, CLIPBOARD, WAIT。";
    internal const string ErrEmptyArgument = "第 {0} 行指令 '{1}' 缺少参数内容。";
    internal const string ErrInvalidWait = "第 {0} 行延时参数 '{1}' 无效：必须为 0 ~ {2} 之间的整数毫秒值。";
    internal const string ErrInvalidRepeat = "第 {0} 行单步重复次数 '{1}' 无效：必须为 1 ~ {2} 之间的整数。";
    internal const string ErrInvalidKeyToken = "第 {0} 行包含无法识别的按键名称 '{1}'。";
    internal const string ErrSingleKeyExpected = "第 {0} 行 KEY 指令仅接受单个按键（当前为 '{1}'）；组合键或多键序列请使用 HOTKEY 指令。";
    internal const string ErrTooManySteps = "脚本步骤过多（当前 {0} 步，上限 {1} 步）。请精简步骤。";
    internal const string ErrTotalWaitExceeded = "脚本累计等待时长超出安全上限（当前 {0}ms，上限 {1}ms）。";
    internal const string ErrInvalidPreDelay = "前置缓冲延时必须为 0 ~ 1000 之间的整数毫秒值。";
    internal const string ErrInvalidStepDelay = "默认步间间隔必须为 0 ~ 500 之间的整数毫秒值。";
    internal const string ErrInvalidRepeatCount = "整体重复次数必须为 1 ~ 20 之间的整数。";
    internal const string ErrCapabilityDenied = "本插件缺少「输入模拟 (InputSimulation)」能力声明，宏执行被拒绝。";
    internal const string ErrStepExecutionFailed = "第 {0} 行步骤 ({1}) 执行失败，已中止后续步骤。";

    internal const string UiHeader = "可视化步骤编排（点击热键框直接录制，或使用右侧按钮拼装/配置）";
    internal const string UiKindKey = "⌨️ 快捷键 / 按键";
    internal const string UiKindPaste = "📋 粘贴指定文本";
    internal const string UiKindText = "🔤 逐字输入";
    internal const string UiKindWait = "⏱️ 延时等待";
    internal const string UiKindCopy = "📎 复制指定文本";
    internal const string UiRecordPrompt = "🔴 请按下组合键或单键... (按 Esc 取消)";
    internal const string UiExclusiveRecordPrompt = "🔴 独占录制中：请直接按下系统或软件快捷键 (Esc 取消)";
    internal const string UiRecordEmpty = "点击录制快捷键 / 单键...";
    internal const string UiPauseHotkeysBtn = "⏸️ 暂停全局热键";
    internal const string UiResumeHotkeysBtn = "🔴 恢复全局热键";
    internal const string UiPauseHotkeysTip = "暂停桌面系统及其他软件的所有全局快捷键，在此独占录入快捷键而不会触发系统动作（如 Win+D、Alt+Tab、截屏等）";
    internal const string UiBuildHotkeyBtn = "⚙️ 拼装组合";
    internal const string UiBuildHotkeyTip = "打开快捷键与组合按键拼装构建器";
    internal const string UiConfigTextBtn = "⚙️ 配置文本";
    internal const string UiConfigTextTip = "弹出多行文本输入窗口，全面直观地编辑大段或多行文本";
    internal const string UiCharDelayLabel = "字间隔";
    internal const string UiCharDelayTip = "逐字输入时每个字符之间的打字间隔毫秒数（0 ~ 1000ms，推荐 35ms）";
    internal const string UiPickKeyBtn = "▾ 选键";
    internal const string UiTextPlaceholder = "输入文本内容，或点击右侧「⚙️ 配置文本」输入大段文字...";
    internal const string UiAddKeyBtn = "+ ⌨️ 录按键";
    internal const string UiAddPasteBtn = "+ 📋 粘贴文本";
    internal const string UiAddTextBtn = "+ 🔤 逐字输入";
    internal const string UiAddWaitBtn = "+ ⏱️ 加等待";
    internal const string UiToggleSourceBtn = "📝 脚本源码";
    internal const string UiPreDelayShort = "焦点缓冲";
    internal const string UiStepDelayShort = "步间间隔";
    internal const string UiRepeatShort = "整体循环";
    internal const string UiMoveUpTip = "上移此步骤";
    internal const string UiMoveDownTip = "下移此步骤";
    internal const string UiDeleteTip = "删除此步骤";
    internal const string UiClearKeyTip = "清除热键";
    internal const string UiRepeatTip = "设置此按键步骤连续重复按下的次数（点击选择或滚动鼠标滚轮调节）";

    internal static readonly (string Key, string ZhCn, string En)[] Base =
    {
        ("macro.title", ActionTitle, "Key Macro / Multi-Step Script"),
        ("macro.desc", ActionDesc, "Execute sequential hotkeys, keystrokes, text input, clipboard operations, and delays for multi-step workflows."),
        ("macro.category", ActionCategory, "Productivity"),

        ("macro.script", FieldScript, "Macro Script (DSL)"),
        ("macro.scriptHelp", FieldScriptHelp, "One command per line: HOTKEY: combo | KEY: key | TEXT: text | PASTE: text | CLIPBOARD: text | WAIT: ms (supports # comments and *N repeat)."),
        ("macro.preDelay", FieldPreDelay, "Focus Recovery Delay (ms)"),
        ("macro.preDelayHelp", FieldPreDelayHelp, "Wait time after wheel closes for target window to regain input focus (0-1000ms, 50ms recommended)."),
        ("macro.stepDelay", FieldStepDelay, "Default Step Delay (ms)"),
        ("macro.stepDelayHelp", FieldStepDelayHelp, "Default delay between consecutive non-WAIT steps (0-500ms, 30ms recommended)."),
        ("macro.repeatCount", FieldRepeatCount, "Repeat Count"),
        ("macro.repeatCountHelp", FieldRepeatCountHelp, "Number of times to repeat the entire macro script (1-20, default 1)."),

        ("macro.err.empty", ErrEmptyScript, "Macro script has no executable steps. Please enter at least one command (e.g. HOTKEY: Alt+F)."),
        ("macro.err.missingColon", ErrMissingColon, "Line {0} syntax error: missing colon ':' (example: HOTKEY: Alt+F or WAIT: 100)."),
        ("macro.err.unknownDirective", ErrUnknownDirective, "Line {0} unknown command '{1}'. Supported: HOTKEY, KEY, TEXT, PASTE, CLIPBOARD, WAIT."),
        ("macro.err.emptyArg", ErrEmptyArgument, "Line {0} command '{1}' requires a value."),
        ("macro.err.invalidWait", ErrInvalidWait, "Line {0} invalid WAIT value '{1}': must be an integer between 0 and {2} ms."),
        ("macro.err.invalidRepeat", ErrInvalidRepeat, "Line {0} invalid repeat count '{1}': must be an integer between 1 and {2}."),
        ("macro.err.invalidKey", ErrInvalidKeyToken, "Line {0} unrecognized key token '{1}'."),
        ("macro.err.singleKeyExpected", ErrSingleKeyExpected, "Line {0} KEY command expects a single key (got '{1}'); use HOTKEY for key combinations."),
        ("macro.err.tooManySteps", ErrTooManySteps, "Too many steps in script ({0} steps, maximum {1})."),
        ("macro.err.totalWaitExceeded", ErrTotalWaitExceeded, "Total wait duration exceeds safety limit ({0}ms, maximum {1}ms)."),
        ("macro.err.invalidPreDelay", ErrInvalidPreDelay, "Focus recovery delay must be an integer between 0 and 1000 ms."),
        ("macro.err.invalidStepDelay", ErrInvalidStepDelay, "Default step delay must be an integer between 0 and 500 ms."),
        ("macro.err.invalidRepeatCount", ErrInvalidRepeatCount, "Repeat count must be an integer between 1 and 20."),
        ("macro.err.capability", ErrCapabilityDenied, "Missing InputSimulation capability; macro execution denied."),
        ("macro.err.stepFailed", ErrStepExecutionFailed, "Step at line {0} ({1}) failed; subsequent steps aborted."),

        ("macro.ui.header", UiHeader, "Visual Step Builder (click hotkey box to record, or use buttons on the right)"),
        ("macro.ui.kindKey", UiKindKey, "⌨️ Hotkey / Key"),
        ("macro.ui.kindPaste", UiKindPaste, "📋 Paste Specified Text"),
        ("macro.ui.kindText", UiKindText, "🔤 Type Character by Character"),
        ("macro.ui.kindWait", UiKindWait, "⏱️ Wait Delay"),
        ("macro.ui.kindCopy", UiKindCopy, "📎 Copy Specified Text"),
        ("macro.ui.recordPrompt", UiRecordPrompt, "🔴 Press key or combination... (Esc to cancel)"),
        ("macro.ui.exclusiveRecordPrompt", UiExclusiveRecordPrompt, "🔴 Exclusive recording: press any system/app shortcut (Esc to cancel)"),
        ("macro.ui.recordEmpty", UiRecordEmpty, "Click to record hotkey / key..."),
        ("macro.ui.pauseHotkeysBtn", UiPauseHotkeysBtn, "⏸️ Pause Global Hotkeys"),
        ("macro.ui.resumeHotkeysBtn", UiResumeHotkeysBtn, "🔴 Resume Global Hotkeys"),
        ("macro.ui.pauseHotkeysTip", UiPauseHotkeysTip, "Temporarily suppress OS and third-party global hotkeys to record keys like Win+D or Alt+Tab"),
        ("macro.ui.buildHotkeyBtn", UiBuildHotkeyBtn, "⚙️ Build Combo"),
        ("macro.ui.buildHotkeyTip", UiBuildHotkeyTip, "Open Hotkey Combination Builder dialog"),
        ("macro.ui.configTextBtn", UiConfigTextBtn, "⚙️ Configure Text"),
        ("macro.ui.configTextTip", UiConfigTextTip, "Open multi-line text editor window for long or multi-line content"),
        ("macro.ui.charDelayLabel", UiCharDelayLabel, "Char Gap"),
        ("macro.ui.charDelayTip", UiCharDelayTip, "Delay in ms between each character when typing character-by-character (0-1000ms, 35ms recommended)"),
        ("macro.ui.pickKeyBtn", UiPickKeyBtn, "▾ Keys"),
        ("macro.ui.textPlaceholder", UiTextPlaceholder, "Enter text or click '⚙️ Configure Text'..."),
        ("macro.ui.addKeyBtn", UiAddKeyBtn, "+ ⌨️ Add Key"),
        ("macro.ui.addPasteBtn", UiAddPasteBtn, "+ 📋 Paste Text"),
        ("macro.ui.addTextBtn", UiAddTextBtn, "+ 🔤 Type Text"),
        ("macro.ui.addWaitBtn", UiAddWaitBtn, "+ ⏱️ Add Wait"),
        ("macro.ui.toggleSourceBtn", UiToggleSourceBtn, "📝 Raw Script"),
        ("macro.ui.preDelayShort", UiPreDelayShort, "Focus Delay"),
        ("macro.ui.stepDelayShort", UiStepDelayShort, "Step Gap"),
        ("macro.ui.repeatShort", UiRepeatShort, "Loop"),
        ("macro.ui.moveUpTip", UiMoveUpTip, "Move step up"),
        ("macro.ui.moveDownTip", UiMoveDownTip, "Move step down"),
        ("macro.ui.deleteTip", UiDeleteTip, "Delete step"),
        ("macro.ui.clearKeyTip", UiClearKeyTip, "Clear hotkey"),
        ("macro.ui.repeatTip", UiRepeatTip, "Repeat count for this key step (click or scroll mouse wheel)"),
    };

    internal static readonly Dictionary<string, string> ZhTw = new()
    {
        ["macro.title"] = "按鍵巨集 / 多步驟腳本",
        ["macro.desc"] = "依序執行多步組合鍵、單鍵、文字輸入、剪貼簿與延遲等待，輕鬆完成無單一快捷鍵的多步選單操作。",
        ["macro.category"] = "效率工具",
        ["macro.script"] = "多步驟腳本 (Macro DSL)",
        ["macro.scriptHelp"] = "每行一條指令：HOTKEY: 組合鍵 | KEY: 單鍵 | TEXT: 文字 | PASTE: 貼上文字 | CLIPBOARD: 寫入剪貼簿 | WAIT: 毫秒數（支援 # 註解與 *N 重複）。",
        ["macro.preDelay"] = "輪盤退場焦點緩衝 (ms)",
        ["macro.preDelayHelp"] = "手勢釋放後等待目標視窗恢復輸入焦點的緩衝毫秒數（0 ~ 1000ms，建議 50ms）。",
        ["macro.stepDelay"] = "預設步間間隔 (ms)",
        ["macro.stepDelayHelp"] = "相鄰兩個未顯式插入 WAIT 的動作步驟之間的預設等待毫秒數（0 ~ 500ms，建議 30ms）。",
        ["macro.repeatCount"] = "整體重複次數",
        ["macro.repeatCountHelp"] = "整個多步驟腳本循環執行的次數（1 ~ 20 次，預設 1 次）。",
        ["macro.err.empty"] = "未填寫有效的巨集步驟。請至少輸入一條指令（如 HOTKEY: Alt+F 或 KEY: Enter）。",
        ["macro.err.missingColon"] = "第 {0} 行語法錯誤：缺少冒號 ':'（正確範例：HOTKEY: Alt+F 或 WAIT: 100）。",
        ["macro.err.unknownDirective"] = "第 {0} 行未知指令 '{1}'。支援指令：HOTKEY, KEY, TEXT, PASTE, CLIPBOARD, WAIT。",
        ["macro.err.emptyArg"] = "第 {0} 行指令 '{1}' 缺少參數內容。",
        ["macro.err.invalidWait"] = "第 {0} 行延遲參數 '{1}' 無效：必須為 0 ~ {2} 之間的整數毫秒值。",
        ["macro.err.invalidRepeat"] = "第 {0} 行單步重複次數 '{1}' 無效：必須為 1 ~ {2} 之間的整數。",
        ["macro.err.invalidKey"] = "第 {0} 行包含無法識別的按鍵名稱 '{1}'。",
        ["macro.err.singleKeyExpected"] = "第 {0} 行 KEY 指令僅接受單個按鍵（目前為 '{1}'）；組合鍵請使用 HOTKEY 指令。",
        ["macro.err.tooManySteps"] = "腳本步驟過多（目前 {0} 步，上限 {1} 步）。",
        ["macro.err.totalWaitExceeded"] = "腳本累計等待時間超出安全上限（目前 {0}ms，上限 {1}ms）。",
        ["macro.err.invalidPreDelay"] = "前置緩衝延遲必須為 0 ~ 1000 之間的整數毫秒值。",
        ["macro.err.invalidStepDelay"] = "預設步間間隔必須為 0 ~ 500 之間的整數毫秒值。",
        ["macro.err.invalidRepeatCount"] = "整體重複次數必須為 1 ~ 20 之間的整數。",
        ["macro.err.capability"] = "本外掛缺少「輸入模擬 (InputSimulation)」能力聲明，巨集執行被拒絕。",
        ["macro.err.stepFailed"] = "第 {0} 行步驟 ({1}) 執行失敗，已中止後續步驟。",
        ["macro.ui.header"] = "視覺化步驟編排（點擊熱鍵框直接錄製，或使用右側按鈕拼裝/配置）",
        ["macro.ui.kindKey"] = "⌨️ 快捷鍵 / 按鍵",
        ["macro.ui.kindPaste"] = "📋 貼上指定文字",
        ["macro.ui.kindText"] = "🔤 逐字輸入",
        ["macro.ui.kindWait"] = "⏱️ 延遲等待",
        ["macro.ui.kindCopy"] = "📎 複製指定文字",
        ["macro.ui.recordPrompt"] = "🔴 請按下組合鍵或單鍵... (按 Esc 取消)",
        ["macro.ui.exclusiveRecordPrompt"] = "🔴 獨佔錄製中：請直接按下系統或軟體快捷鍵 (Esc 取消)",
        ["macro.ui.recordEmpty"] = "點擊錄製快捷鍵 / 單鍵...",
        ["macro.ui.pauseHotkeysBtn"] = "⏸️ 暫停全域熱鍵",
        ["macro.ui.resumeHotkeysBtn"] = "🔴 恢復全域熱鍵",
        ["macro.ui.pauseHotkeysTip"] = "暫停系統及其他軟體的全域快捷鍵，獨佔錄入如 Win+D、Alt+Tab 等熱鍵",
        ["macro.ui.buildHotkeyBtn"] = "⚙️ 拼裝組合",
        ["macro.ui.buildHotkeyTip"] = "開啟快捷鍵與組合按鍵拼裝建構器",
        ["macro.ui.configTextBtn"] = "⚙️ 配置文字",
        ["macro.ui.configTextTip"] = "彈出多行文字輸入視窗，全面直觀地編輯大段或多行文字",
        ["macro.ui.charDelayLabel"] = "字間隔",
        ["macro.ui.charDelayTip"] = "逐字輸入時每個字元之間的打字間隔毫秒數（0 ~ 1000ms，建議 35ms）",
        ["macro.ui.pickKeyBtn"] = "▾ 選鍵",
        ["macro.ui.textPlaceholder"] = "輸入文字內容，或點擊右側「⚙️ 配置文字」...",
        ["macro.ui.addKeyBtn"] = "+ ⌨️ 錄按鍵",
        ["macro.ui.addPasteBtn"] = "+ 📋 貼上文字",
        ["macro.ui.addTextBtn"] = "+ 🔤 逐字輸入",
        ["macro.ui.addWaitBtn"] = "+ ⏱️ 加等待",
        ["macro.ui.toggleSourceBtn"] = "📝 腳本原始碼",
        ["macro.ui.preDelayShort"] = "焦點緩衝",
        ["macro.ui.stepDelayShort"] = "步間間隔",
        ["macro.ui.repeatShort"] = "整體循環",
        ["macro.ui.moveUpTip"] = "上移此步驟",
        ["macro.ui.moveDownTip"] = "下移此步驟",
        ["macro.ui.deleteTip"] = "刪除此步驟",
        ["macro.ui.clearKeyTip"] = "清除熱鍵",
        ["macro.ui.repeatTip"] = "設定此按鍵步驟連續重複按下的次數（點擊選擇或滾動滾輪調節）",
    };

    internal static readonly Dictionary<string, string> Ja = new()
    {
        ["macro.title"] = "キーマクロ / マルチステップスクリプト",
        ["macro.desc"] = "ショートカット、単一キー、テキスト入力、クリップボード操作、待機を順番に実行し、複数ステップの操作を自動化します。",
        ["macro.category"] = "効率化ツール",
        ["macro.script"] = "マクロスクリプト (DSL)",
        ["macro.scriptHelp"] = "1行に1コマンド：HOTKEY: 複合キー | KEY: 単一キー | TEXT: テキスト | PASTE: 貼り付け | CLIPBOARD: コピー | WAIT: ミリ秒（# コメントおよび *N 繰り返し対応）。",
        ["macro.preDelay"] = "フォーカス復帰待機 (ms)",
        ["macro.preDelayHelp"] = "リングメニュー閉鎖後、対象ウィンドウがフォーカスを回復するまでの待機時間（0〜1000ms、推奨50ms）。",
        ["macro.stepDelay"] = "標準ステップ間隔 (ms)",
        ["macro.stepDelayHelp"] = "WAIT が指定されていない連続ステップ間の標準待機時間（0〜500ms、推奨30ms）。",
        ["macro.repeatCount"] = "全体の繰り返し回数",
        ["macro.repeatCountHelp"] = "スクリプト全体を繰り返す回数（1〜20回、標準1回）。",
        ["macro.err.empty"] = "有効なマクロステップがありません。少なくとも1つのコマンドを入力してください（例: HOTKEY: Alt+F）。",
        ["macro.err.missingColon"] = "{0} 行目の構文エラー：コロン ':' がありません（例: HOTKEY: Alt+F または WAIT: 100）。",
        ["macro.err.unknownDirective"] = "{0} 行目の不明なコマンド '{1}'。対応コマンド: HOTKEY, KEY, TEXT, PASTE, CLIPBOARD, WAIT。",
        ["macro.err.emptyArg"] = "{0} 行目のコマンド '{1}' に値がありません。",
        ["macro.err.invalidWait"] = "{0} 行目の待機時間 '{1}' が無効です（0〜{2} ms の整数を指定してください）。",
        ["macro.err.invalidRepeat"] = "{0} 行目の繰り返し回数 '{1}' が無効です（1〜{2} の整数を指定してください）。",
        ["macro.err.invalidKey"] = "{0} 行目に認識できないキー名 '{1}' が含まれています。",
        ["macro.err.singleKeyExpected"] = "{0} 行目の KEY コマンドは単一キーのみ指定可能です（'{1}'）；複合キーには HOTKEY を使用してください。",
        ["macro.err.tooManySteps"] = "ステップ数が多すぎます（現在 {0}、上限 {1}）。",
        ["macro.err.totalWaitExceeded"] = "合計待機時間が安全上限を超えています（現在 {0}ms、上限 {1}ms）。",
        ["macro.err.invalidPreDelay"] = "フォーカス復帰待機時間は 0〜1000 の整数で指定してください。",
        ["macro.err.invalidStepDelay"] = "標準ステップ間隔は 0〜500 の整数で指定してください。",
        ["macro.err.invalidRepeatCount"] = "全体の繰り返し回数は 1〜20 の整数で指定してください。",
        ["macro.err.capability"] = "InputSimulation 権限がないため、マクロの実行が拒否されました。",
        ["macro.err.stepFailed"] = "{0} 行目のステップ ({1}) の実行に失敗したため、以降の処理を中断しました。",
        ["macro.ui.header"] = "ビジュアルステップ編集（キー欄をクリックして記録、または右側のボタンで設定）",
        ["macro.ui.kindKey"] = "⌨️ ショートカット / キー",
        ["macro.ui.kindPaste"] = "📋 指定テキストを貼り付け",
        ["macro.ui.kindText"] = "🔤 1文字ずつ入力",
        ["macro.ui.kindWait"] = "⏱️ 待機 (ミリ秒)",
        ["macro.ui.kindCopy"] = "📎 指定テキストをコピー",
        ["macro.ui.recordPrompt"] = "🔴 キーまたはショートカットを押してください... (Escでキャンセル)",
        ["macro.ui.exclusiveRecordPrompt"] = "🔴 独占記録中：システム/アプリのショートカットを押してください (Escでキャンセル)",
        ["macro.ui.recordEmpty"] = "クリックしてキーを記録...",
        ["macro.ui.pauseHotkeysBtn"] = "⏸️ グローバルホットキー一時停止",
        ["macro.ui.resumeHotkeysBtn"] = "🔴 グローバルホットキー再開",
        ["macro.ui.pauseHotkeysTip"] = "OSや他アプリのホットキーを一時停止し、Win+DやAlt+Tabなどを直接記録します",
        ["macro.ui.buildHotkeyBtn"] = "⚙️ キー組み合わせ",
        ["macro.ui.buildHotkeyTip"] = "ショートカット組み立てダイアログを開く",
        ["macro.ui.configTextBtn"] = "⚙️ テキスト設定",
        ["macro.ui.configTextTip"] = "複数行テキスト入力ウィンドウを開いて長文を編集します",
        ["macro.ui.charDelayLabel"] = "文字間隔",
        ["macro.ui.charDelayTip"] = "1文字ずつ入力する際の文字間隔ミリ秒（0〜1000ms、推奨35ms）",
        ["macro.ui.pickKeyBtn"] = "▾ キー選択",
        ["macro.ui.textPlaceholder"] = "テキストを入力、または「⚙️ テキスト設定」をクリック...",
        ["macro.ui.addKeyBtn"] = "+ ⌨️ キー追加",
        ["macro.ui.addPasteBtn"] = "+ 📋 貼り付け追加",
        ["macro.ui.addTextBtn"] = "+ 🔤 文字入力追加",
        ["macro.ui.addWaitBtn"] = "+ ⏱️ 待機追加",
        ["macro.ui.toggleSourceBtn"] = "📝 スクリプト編集",
        ["macro.ui.preDelayShort"] = "フォーカス待機",
        ["macro.ui.stepDelayShort"] = "ステップ間隔",
        ["macro.ui.repeatShort"] = "繰り返し",
        ["macro.ui.moveUpTip"] = "上へ移動",
        ["macro.ui.moveDownTip"] = "下へ移動",
        ["macro.ui.deleteTip"] = "ステップを削除",
        ["macro.ui.clearKeyTip"] = "クリア",
        ["macro.ui.repeatTip"] = "このキーステップの繰り返し回数（クリックまたはホイールで調整）",
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
