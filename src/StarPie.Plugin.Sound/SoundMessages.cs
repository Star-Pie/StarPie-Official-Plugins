using StarPie.Plugin;
namespace StarPie.Plugin.Sound;
internal static class SoundMessages
{
    private static readonly Dictionary<string, string[]> Text = new()
    {
        ["settings.title"] = new[] { "轮盘音效", "輪盤音效", "Wheel sound", "ホイール効果音" },
        ["settings.description"] = new[] { "需要安装、启用及按需预加载插件；未启用时宿主不会兜底。自定义参数通过方案文件导入/导出编辑，方案可用 ID 或名称选择。", "需要安裝、啟用及按需預載插件；未啟用時宿主不會備援。自訂參數透過方案檔匯入/匯出編輯，方案可用 ID 或名稱選擇。", "Install, enable and opt into preload to receive events. There is no host fallback. Edit custom parameters through profile import/export; select a profile by ID or name.", "導入、有効化、必要に応じて事前読み込みを設定してください。ホスト側の代替音声はありません。カスタム設定はファイルの読み書きで編集し、IDまたは名前で選択します。" },
        ["settings.enabled"] = new[] { "启用音效", "啟用音效", "Enable sound", "効果音を有効にする" },
        ["settings.volume"] = new[] { "音量（0–1）", "音量（0–1）", "Volume (0–1)", "音量（0–1）" },
        ["settings.theme"] = new[] { "主题", "主題", "Theme", "テーマ" },
        ["settings.profileId"] = new[] { "自定义方案 ID 或名称", "自訂方案 ID 或名稱", "Custom profile ID or name", "カスタム設定のIDまたは名前" },
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
