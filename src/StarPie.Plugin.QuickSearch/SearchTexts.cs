namespace StarPie.Plugin.QuickSearch;

internal static class SearchTexts
{
    internal static void Register(II18nRegistry registry)
    {
        (string Key, string Cn, string Tw, string En, string Ja)[] entries =
        [
            ("title", "快速搜索", "快速搜尋", "Quick Search", "クイック検索"),
            ("description", "搜索文件、文件夹、应用与系统工具", "搜尋檔案、資料夾、應用程式與系統工具", "Search files, folders, apps and system tools", "ファイル、フォルダー、アプリ、システムツールを検索"),
            ("search", "搜索", "搜尋", "Search", "検索"),
            ("placeholder", "搜索文件、文件夹或应用…", "搜尋檔案、資料夾或應用程式…", "Search files, folders or apps…", "ファイル、フォルダー、アプリを検索…"),
            ("close", "关闭", "關閉", "Close", "閉じる"),
            ("results", "搜索结果", "搜尋結果", "Search results", "検索結果"),
            ("file", "文件", "檔案", "File", "ファイル"),
            ("pin", "固定窗口", "固定視窗", "Pin window", "ウィンドウを固定"),
            ("unpin", "取消固定", "取消固定", "Unpin window", "固定を解除"),
            ("scope", "索引目录", "索引資料夾", "Indexed folders", "索引フォルダー"),
            ("scopeHelp", "选择要索引的本地磁盘或目录。关闭搜索窗口后，索引会继续更新。", "選擇要建立索引的本機磁碟或資料夾。關閉搜尋視窗後，索引仍會更新。", "Choose drives or folders to index. Indexing continues after you close this window.", "索引を作成するドライブやフォルダーを選択してください。ウィンドウを閉じた後も更新は続きます。"),
            ("localDrives", "索引所有本地固定磁盘", "索引所有本機固定磁碟", "Index all local fixed drives", "すべてのローカル固定ドライブを索引に追加"),
            ("noDrives", "没有可用的本地固定磁盘", "沒有可用的本機固定磁碟", "No local fixed drives are ready", "利用可能なローカル固定ドライブがありません"),
            ("indexedCount", "已索引 {0:N0} 项", "已索引 {0:N0} 項", "{0:N0} items indexed", "{0:N0} 件を索引に登録"),
            ("folderIndex", "目录索引", "資料夾索引", "Folder index", "フォルダー索引"),
            ("retry", "重试索引", "重試索引", "Retry index", "索引を再試行"),
            ("indexFailed", "索引已停止。请在「索引目录」中点击「重试索引」。", "索引已停止。請在「索引資料夾」中按「重試索引」。", "Indexing stopped. Open Indexed folders and choose Retry index.", "索引作成が停止しました。「索引フォルダー」で「索引を再試行」を選んでください。"),
            ("clearFailed", "索引缓存未清除，请检查插件数据目录的权限并重试。", "索引快取尚未清除，請檢查外掛資料夾權限並重試。", "Could not clear the index cache. Check plugin data folder permissions and retry.", "索引キャッシュを削除できません。プラグインのデータフォルダーの権限を確認して再試行してください。"),
            ("chooseRoots", "请添加要搜索的目录", "請新增要搜尋的資料夾", "Add a folder to search", "検索対象のフォルダーを追加してください"),
            ("refreshing", "正在更新", "正在更新", "Updating", "更新中"),
            ("unavailableRoots", "不可访问的目录", "無法存取的資料夾", "Unavailable roots", "アクセスできないルート"),
            ("skippedDirs", "跳过的子目录", "略過的子資料夾", "Skipped subfolders", "スキップしたサブフォルダー"),
            ("unwatchedRoots", "使用定期检查的目录", "使用定期檢查的資料夾", "Folders using periodic checks", "定期チェックするフォルダー"),
            ("add", "添加目录…", "新增資料夾…", "Add folder…", "フォルダーを追加…"),
            ("remove", "移除目录", "移除資料夾", "Remove folder", "フォルダーを除外"),
            ("appsRoot", "添加开始菜单", "新增開始功能表", "Add Start menu", "スタートメニューを追加"),
            ("all", "全部", "全部", "All", "すべて"),
            ("apps", "应用", "應用程式", "Apps", "アプリ"),
            ("cad", "机械 CAD", "機械 CAD", "CAD", "機械 CAD"),
            ("docs", "文档代码", "文件程式碼", "Docs & code", "文書・コード"),
            ("video", "视频", "影片", "Video", "動画"),
            ("folders", "目录", "資料夾", "Folders", "フォルダー"),
            ("system", "系统", "系統", "System", "システム"),
            ("taskmgr", "任务管理器", "工作管理員", "Task Manager", "タスク マネージャー"),
            ("control", "控制面板", "控制台", "Control Panel", "コントロール パネル"),
            ("calculator", "计算器", "小算盤", "Calculator", "電卓"),
            ("explorer", "文件资源管理器", "檔案總管", "File Explorer", "エクスプローラー"),
            ("open", "打开", "開啟", "Open", "開く"),
            ("locate", "打开所在位置", "開啟所在位置", "Show in File Explorer", "保存場所を開く"),
            ("runAdmin", "以管理员身份运行", "以系統管理員身分執行", "Run as administrator", "管理者として実行"),
            ("copyPath", "复制完整路径", "複製完整路徑", "Copy full path", "フルパスをコピー"),
            ("copyName", "复制名称", "複製名稱", "Copy name", "名前をコピー"),
            ("copied", "已复制", "已複製", "Copied", "コピーしました"),
            ("shortcuts", "↑↓ 选择  ·  Enter 打开  ·  Shift+Enter 定位  ·  右键 更多  ·  Esc 关闭", "↑↓ 選取  ·  Enter 開啟  ·  Shift+Enter 定位  ·  右鍵 更多  ·  Esc 關閉", "↑↓ Select  ·  Enter Open  ·  Shift+Enter Locate  ·  Right-click More  ·  Esc Close", "↑↓ 選択  ·  Enter 開く  ·  Shift+Enter 保存場所  ·  右クリック その他  ·  Esc 閉じる"),
            ("previous", "上一页", "上一頁", "Previous page", "前のページ"),
            ("next", "下一页", "下一頁", "Next page", "次のページ"),
            ("searching", "搜索中…", "搜尋中…", "Searching…", "検索中…"),
            ("empty", "没有匹配结果", "沒有相符結果", "No matches", "一致する結果がありません"),
            ("permission", "快速搜索缺少所需权限", "快速搜尋缺少必要權限", "Quick Search lacks a required permission", "クイック検索に必要な権限がありません"),
            ("failure", "操作失败", "操作失敗", "Operation failed", "操作に失敗しました")
        ];
        foreach (var row in entries) registry.Register(row.Key, row.Cn, row.En);
        registry.RegisterTable("zh-TW", entries.ToDictionary(row => row.Key, row => row.Tw));
        registry.RegisterTable("ja", entries.ToDictionary(row => row.Key, row => row.Ja));
    }
}
