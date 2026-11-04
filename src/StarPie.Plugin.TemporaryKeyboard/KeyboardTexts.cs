namespace StarPie.Plugin.TemporaryKeyboard;

internal static class KeyboardTexts
{
    internal static string T(IPluginContext context, string key) => context.I18n.T(key);
    internal static void Register(IPluginContext context)
    {
        (string Key, string Cn, string Tw, string En, string Ja)[] rows =
        [
            ("title", "临时小键盘", "臨時小鍵盤", "Temporary keyboard", "一時キーボード"),
            ("show", "呼出小键盘", "叫出小鍵盤", "Open keyboard", "キーボードを開く"),
            ("configure", "编辑自定义键盘", "編輯自訂鍵盤", "Edit custom keyboards", "カスタムキーボードを編集"),
            ("mode", "初始模式", "初始模式", "Initial mode", "初期モード"),
            ("useDefault", "使用插件默认模式", "使用外掛預設模式", "Use plugin default", "プラグインの標準モード"),
            ("Numeric", "数字", "數字", "Numbers", "数字"),
            ("Letters", "字母", "字母", "Letters", "英字"),
            ("Functions", "功能键", "功能鍵", "Functions", "機能キー"),
            ("Full", "全键盘", "全鍵盤", "Full keyboard", "フルキーボード"),
            ("Custom", "自定义", "自訂", "Custom", "カスタム"),
            ("scheme", "初始自定义方案名称（可留空）", "初始自訂方案名稱（可留空）", "Initial custom scheme name (optional)", "初期カスタム設定名（省略可）"),
            ("defaultScheme", "默认方案", "預設方案", "Default", "標準"),
            ("close", "关闭", "關閉", "Close", "閉じる"),
            ("hint", "连续点击输入 · Esc或切换应用关闭", "連續點擊輸入 · Esc或切換應用程式關閉", "Click to type · Esc or switch apps to close", "クリックで入力・Escまたはアプリ切替で閉じる"),
            ("busy", "输入队列繁忙，请稍后再点。", "輸入佇列忙碌，請稍後再點。", "The input queue is busy. Try again shortly.", "入力キューが混雑しています。少し待ってください。"),
            ("unavailable", "动作不可用；原绑定已保留。", "動作無法使用；原綁定已保留。", "Action unavailable; its binding is retained.", "アクションは利用できません。割り当ては保持されています。"),
            ("empty", "未配置", "未設定", "Unassigned", "未設定"),
            ("editHint", "在设置中编辑方案；关闭后回到目标应用重新呼出。", "在設定中編輯方案；關閉後回到目標應用程式重新叫出。", "Edit schemes here, then reopen the keyboard in the target app.", "ここで設定を編集し、入力先アプリでキーボードを開き直してください。"),
            ("name", "方案名称", "方案名稱", "Scheme name", "設定名"),
            ("add", "新增方案", "新增方案", "Add scheme", "設定を追加"),
            ("remove", "删除方案", "刪除方案", "Remove scheme", "設定を削除"),
            ("save", "保存", "儲存", "Save", "保存"),
            ("cancel", "取消", "取消", "Cancel", "キャンセル"),
            ("newScheme", "新方案", "新方案", "New scheme", "新しい設定"),
            ("invalid", "方案数据无效或超过限制，未保存。", "方案資料無效或超過限制，未儲存。", "Scheme data is invalid or exceeds a limit. Nothing was saved.", "設定データが無効か上限を超えています。保存していません。"),
            ("loadFailed", "自定义方案无法读取；原数据未覆盖，可在编辑器中重新配置。", "自訂方案無法讀取；原資料未覆寫，可在編輯器中重新設定。", "Custom schemes could not be read. Original data was retained; reconfigure in the editor.", "カスタム設定を読めません。元のデータは保持されています。エディターで再設定してください。"),
            ("rebuild", "重建并备份原数据", "重建並備份原資料", "Rebuild and back up original", "元のデータをバックアップして再作成"),
            ("backupHint", "保存时先备份原始数据；备份或写入失败将保留原文件。", "儲存時先備份原始資料；備份或寫入失敗將保留原檔案。", "Saving backs up the original first. Backup or write failures retain the original file.", "保存前に元のデータをバックアップします。失敗した場合は元のファイルを保持します。"),
            ("requiresHost", "需要支持SDK 1.15的StarPie宿主。", "需要支援SDK 1.15的StarPie宿主。", "A StarPie host supporting SDK 1.15 is required.", "SDK 1.15対応のStarPieが必要です。"),
            ("targetUnavailable", "输入目标已改变，请回到目标应用重新呼出。", "輸入目標已改變，請回到目標應用程式重新叫出。", "The input target changed. Reopen the keyboard in the target app.", "入力先が変わりました。入力先アプリで開き直してください。"),
            ("editorHint", "点击画布中的按键配置；拖拽换位，修改实时预览。", "點擊畫布中的按鍵設定；拖曳換位，修改即時預覽。", "Select a key to edit. Drag to swap positions and preview your changes.", "キーを選んで編集。ドラッグで入れ替え、変更をプレビューできます。"),
            ("schemes", "键盘方案", "鍵盤方案", "Keyboard schemes", "キーボード設定"),
            ("addShort", "新增", "新增", "Add", "追加"),
            ("duplicate", "复制", "複製", "Duplicate", "複製"),
            ("usedActions", "已用动作", "已用動作", "Used actions", "使用中のアクション"),
            ("libraryHint", "拖到画布可复用；通过下方按钮添加其他插件动作。", "拖到畫布可重用；透過下方按鈕新增其他外掛動作。", "Drag onto a key to reuse, or add another plugin action below.", "キーにドラッグして再利用。下のボタンで追加できます。"),
            ("addAction", "添加插件动作", "新增外掛動作", "Add plugin action", "アクションを追加"),
            ("undo", "撤销", "復原", "Undo", "元に戻す"),
            ("redo", "重做", "重做", "Redo", "やり直す"),
            ("preview", "实时键盘画布", "即時鍵盤畫布", "Live keyboard canvas", "キーボードプレビュー"),
            ("canvasHint", "自定义：点击选中，双击绑定动作，拖拽交换位置。其他布局仅预览，不会发送输入。", "自訂：點擊選取，雙擊綁定動作，拖曳交換位置。其他配置僅預覽，不會傳送輸入。", "Custom: select, double-click to bind, or drag to swap. Other layouts are previews and never send input.", "カスタム：クリックで選択、ダブルクリックで割り当て、ドラッグで入替。他の配列も入力せずプレビューできます。"),
            ("keyLabel", "按键名称", "按鍵名稱", "Key label", "キー名"),
            ("selectedKey", "按键 {0}", "按鍵 {0}", "Key {0}", "キー {0}"),
            ("selectedHint", "先选中画布按键，再选择插件动作。也可从左侧拖入已用动作。", "先選取畫布按鍵，再選擇外掛動作。也可從左側拖入已用動作。", "Select a canvas key, then choose an action. You can also drag a used action from the left.", "キーを選択してアクションを割り当てます。左側からドラッグして再利用もできます。"),
            ("chooseAction", "选择／编辑动作", "選擇／編輯動作", "Choose / edit action", "アクションを選択・編集"),
            ("clearKey", "清空此按键", "清除此按鍵", "Clear this key", "このキーを解除"),
            ("unsaved", "预览已更新，保存后生效。", "預覽已更新，儲存後生效。", "Preview updated. Save to apply your changes.", "プレビューを更新しました。保存すると反映されます。"),
            ("noChanges", "绑定未改变。", "綁定未改變。", "The binding is unchanged.", "割り当てに変更はありません。"),
            ("keyEditorHint", "上方配置自定义按键，下方全键盘模板可复制、粘贴或拖入；所有预览均不发送输入。", "上方設定自訂按鍵，下方全鍵盤範本可複製、貼上或拖入；預覽不傳送輸入。", "Configure keys above; copy, paste or drag from the full keyboard below. Previews never send input.", "上のキーを設定し、下の全配列からコピー・貼付け・ドラッグ。プレビューは入力を送りません。"),
            ("legacyRetained", "旧插件动作方案文件已保留，可从宿主动作面板显式导入。这里仅配置键盘按键。", "舊外掛動作方案檔已保留，可從宿主動作面板明確匯入。此處僅設定鍵盤按鍵。", "Legacy action files are retained for explicit import into host action panels. This editor configures keyboard keys only.", "旧アクション設定は保持され、ホスト側で明示的に読み込めます。ここではキーボードキーのみ設定します。"),
            ("dimensions", "行 × 列", "行 × 列", "Rows × columns", "行 × 列"),
            ("applySize", "应用尺寸", "套用尺寸", "Apply size", "サイズを適用"),
            ("resizeBlocked", "行列超限，或缩小会移除已配置按键；请先清空多余位置。", "行列超限，或縮小會移除已設定按鍵；請先清空多餘位置。", "Invalid size, or shrinking would remove configured keys. Clear those keys first.", "範囲外か、縮小すると設定済みキーが失われます。先に該当キーを解除してください。"),
            ("copyKey", "复制按键", "複製按鍵", "Copy key", "キーをコピー"),
            ("pasteKey", "粘贴按键", "貼上按鍵", "Paste key", "キーを貼付け"),
            ("pureKeyHint", "从下方模板拖入按键，或先点击模板复制、再粘贴到选中位置。画布内拖拽交换。", "從下方範本拖入按鍵，或點擊範本複製、再貼到選取位置。畫布內拖曳交換。", "Drag a template key here, or click one to copy and paste it into the selection. Drag canvas keys to swap.", "下のキーをドラッグ、またはクリックでコピーして選択位置に貼付け。上のキー同士は入替できます。"),
            ("templateHint", "全键盘模板 · 点击复制，双击填入当前格，拖入上方画布", "全鍵盤範本 · 點擊複製，雙擊填入目前格，拖入上方畫布", "Full keyboard template · Click to copy, double-click to assign, or drag above", "全キーテンプレート · クリックでコピー、ダブルクリックで設定、上へドラッグ"),
            ("templateKeyHint", "点击复制／双击填入：", "點擊複製／雙擊填入：", "Click to copy / double-click to assign:", "クリックでコピー・ダブルクリックで設定："),
            ("templateCopied", "按键已复制，请在上方选择位置并粘贴。", "按鍵已複製，請在上方選取位置並貼上。", "Key copied. Select a canvas slot and paste.", "キーをコピーしました。上の位置を選んで貼り付けてください。"),
        ];
        foreach (var row in rows) context.I18n.Register(row.Key, row.Cn, row.En);
        context.I18n.RegisterTable("zh-TW", rows.ToDictionary(row => row.Key, row => row.Tw));
        context.I18n.RegisterTable("ja", rows.ToDictionary(row => row.Key, row => row.Ja));
    }
}
