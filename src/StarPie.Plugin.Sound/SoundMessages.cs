using StarPie.Plugin;
namespace StarPie.Plugin.Sound;
internal static class SoundMessages
{
    private static readonly Dictionary<string, string[]> Text = new()
    {
        ["settings.title"] = new[] { "轮盘音效", "輪盤音效", "Wheel sound", "ホイール効果音" },
        ["settings.description"] = new[] { "主题选“自定义”后，可为各事件选择 16-bit PCM WAV（单/双声道，每个≤1MiB）；留空沿用所选方案，清空即恢复。文件仅引用、不复制，请勿移动。导入、导出及独立试听入口暂不提供。", "主題選「自訂」後，可為各事件選擇 16-bit PCM WAV（單/雙聲道，每個≤1MiB）；留空沿用所選方案，清空即恢復。檔案僅引用、不複製，請勿移動。匯入、匯出及獨立試聽入口暫不提供。", "Select Custom, then choose a 16-bit PCM mono/stereo WAV per event (up to 1 MiB each). Empty fields inherit the selected profile; clearing restores it. Files are referenced, not copied. Import, export and standalone preview controls are not available yet.", "テーマをカスタムにして、各イベントに16-bit PCM WAV（モノラル/ステレオ、各1MiBまで）を選択します。空欄は設定を継承し、クリアすると元に戻ります。ファイルはコピーされません。読み込み・書き出し・単独試聴の操作は現在ありません。" },
        ["settings.enabled"] = new[] { "启用音效", "啟用音效", "Enable sound", "効果音を有効にする" },
        ["settings.volume"] = new[] { "音量（0–1）", "音量（0–1）", "Volume (0–1)", "音量（0–1）" },
        ["settings.theme"] = new[] { "主题", "主題", "Theme", "テーマ" },
        ["settings.profileId"] = new[] { "自定义方案 ID 或名称", "自訂方案 ID 或名稱", "Custom profile ID or name", "カスタム設定のIDまたは名前" },
        ["settings.popupWav"] = new[] { "呈现音效 WAV（可选）", "呈現音效 WAV（選填）", "Presented WAV (optional)", "表示 WAV（任意）" },
        ["settings.hoverWav"] = new[] { "选择变化音效 WAV（可选）", "選擇變化音效 WAV（選填）", "Selection WAV (optional)", "選択 WAV（任意）" },
        ["settings.expandWav"] = new[] { "展开音效 WAV（可选）", "展開音效 WAV（選填）", "Expand WAV (optional)", "展開 WAV（任意）" },
        ["settings.executeWav"] = new[] { "确认音效 WAV（可选）", "確認音效 WAV（選填）", "Confirm WAV (optional)", "確定 WAV（任意）" },
        ["settings.cancelWav"] = new[] { "取消音效 WAV（可选）", "取消音效 WAV（選填）", "Cancel WAV (optional)", "キャンセル WAV（任意）" },
        ["settings.onPopup"] = new[] { "轮盘呈现", "輪盤呈現", "Wheel presented", "ホイール表示" },
        ["settings.onHover"] = new[] { "选择变化", "選擇變化", "Selection feedback", "選択の効果音" },
        ["settings.onExpand"] = new[] { "子菜单展开", "子選單展開", "Submenu expanded", "サブメニュー展開" },
        ["settings.onExecute"] = new[] { "动作确认", "動作確認", "Action committed", "アクション確定" },
        ["settings.onCancel"] = new[] { "取消反馈", "取消回饋", "Cancellation feedback", "キャンセルの効果音" },
        ["theme.Mechanical"] = new[] { "机械", "機械", "Mechanical", "メカニカル" },
        ["theme.Crisp"] = new[] { "清脆", "清脆", "Crisp", "クリスプ" },
        ["theme.Bubble"] = new[] { "气泡", "氣泡", "Bubble", "バブル" },
        ["theme.Minimalist"] = new[] { "极简", "極簡", "Minimalist", "ミニマル" },
        ["theme.Custom"] = new[] { "自定义", "自訂", "Custom", "カスタム" },
        ["preview.title"] = new[] { "试听音效", "試聽音效", "Preview sound", "効果音を試聴" },
        ["import.title"] = new[] { "导入音效方案", "匯入音效方案", "Import sound profiles", "効果音設定を読み込む" },
        ["export.title"] = new[] { "导出音效方案", "匯出音效方案", "Export sound profiles", "効果音設定を書き出す" },
        ["event"] = new[] { "试听事件", "試聽事件", "Preview event", "試聴するイベント" },
        ["path"] = new[] { "方案文件路径", "方案檔案路徑", "Profile file path", "設定ファイルのパス" },
        ["profile"] = new[] { "方案 ID 或名称（空为当前）", "方案 ID 或名稱（空白為目前）", "Profile ID or name (empty means current)", "設定IDまたは名前（空欄は現在の設定）" },
        ["activate"] = new[] { "导入后选择该方案", "匯入後選擇該方案", "Select imported profile", "読み込んだ設定を選択" },
        ["replace"] = new[] { "替换同 ID 方案", "取代同 ID 方案", "Replace matching profile IDs", "同じIDの設定を置換" },
        ["bundle"] = new[] { "导出全部方案", "匯出全部方案", "Export all profiles", "すべての設定を書き出す" },
        ["overwrite"] = new[] { "允许覆盖已有文件", "允許覆寫現有檔案", "Allow overwriting existing file", "既存ファイルの上書きを許可" },
        ["event.WheelPopup"] = new[] { "呈现", "呈現", "Presented", "表示" },
        ["event.SectorHover"] = new[] { "悬停", "懸停", "Hover", "選択" },
        ["event.SubmenuExpand"] = new[] { "展开", "展開", "Expand", "展開" },
        ["event.ActionExecute"] = new[] { "确认", "確認", "Confirm", "確定" },
        ["event.GestureCancel"] = new[] { "取消", "取消", "Cancel", "キャンセル" },
        ["event.Flow"] = new[] { "完整流程", "完整流程", "Full sequence", "すべてを試聴" },
        ["invalid"] = new[] { "参数无效。", "參數無效。", "Invalid parameters.", "パラメーターが無効です。" },
        ["fileRequired"] = new[] { "请填写完整方案文件路径。", "請填寫完整方案檔案路徑。", "Enter a full profile file path.", "設定ファイルのフルパスを入力してください。" },
        ["preview.done"] = new[] { "试听处理完成。", "試聽處理完成。", "Preview handling completed.", "試聴処理が完了しました。" },
        ["preview.failed"] = new[] { "试听被撤销或播放失败，请查看插件日志。", "試聽被撤銷或播放失敗，請查看插件記錄。", "Preview was revoked or playback failed; see plugin logs.", "試聴が取り消されたか再生に失敗しました。プラグインのログを確認してください。" },
        ["import.done"] = new[] { "已导入方案：", "已匯入方案：", "Imported profile: ", "読み込んだ設定：" },
        ["export.done"] = new[] { "已导出方案。", "已匯出方案。", "Profiles exported.", "設定を書き出しました。" },
        ["io.failed"] = new[] { "方案操作失败：", "方案操作失敗：", "Profile operation failed: ", "設定操作に失敗：" },
    };
    internal static void Register(II18nRegistry registry)
    {
        string[] languages = { "zh-CN", "zh-TW", "en", "ja" };
        for (int i = 0; i < languages.Length; i++)
        {
            int column = i;
            registry.RegisterTable(languages[i], Text.ToDictionary(x => x.Key, x => x.Value[column]));
        }
    }
}
