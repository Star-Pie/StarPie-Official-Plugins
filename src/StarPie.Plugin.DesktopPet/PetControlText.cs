using StarPie.Plugin;

namespace StarPie.Plugin.DesktopPet;

internal static class PetControlText
{
    internal static void Register(II18nRegistry i18n)
    {
        var entries = new Dictionary<string, string[]>
        {
            ["settings.animation.title"] = ["动画设置", "動畫設定", "Animation settings", "アニメーション設定"],
            ["settings.animation.description"] = ["调整动画开关与播放节奏。速度影响呼吸、眨眼、反应和拖动摆动，不改变长按时长或轮盘方向。", "調整動畫開關與播放節奏。速度影響呼吸、眨眼、反應與拖動擺動，不改變長按時長或輪盤方向。", "Control animation and playback speed. Speed affects breathing, blinks, reactions and drag sway, not long-press timing or wheel directions.", "アニメーションと再生速度を調整します。呼吸、まばたき、反応、揺れに適用され、長押し時間やホイール方向は変わりません。"],
            ["field.animation-speed.label"] = ["动画速度", "動畫速度", "Animation speed", "アニメーション速度"],
            ["field.animation-speed.help"] = ["1× 为原速。拖动滑块或使用减加按钮调整，关闭设置后生效。", "1× 為原速。拖動滑塊或使用減加按鈕調整，關閉設定後生效。", "1× is the original speed. Drag or use −/+; changes apply when settings close.", "1×が元の速度です。スライダーまたは−/+で調整し、設定を閉じると反映されます。"],
            ["library.btn.up"] = ["↑ 上移", "↑ 上移", "↑ Up", "↑ 上へ"],
            ["library.btn.down"] = ["↓ 下移", "↓ 下移", "↓ Down", "↓ 下へ"],
            ["library.btn.remove"] = ["删除", "刪除", "Remove", "削除"],
            ["library.remove.confirm"] = ["从形象列表中删除此项？原始素材和库内副本都会保留，重新导入相同素材可以恢复。", "從形象清單中刪除此項？原始素材與庫內副本都會保留，重新匯入相同素材可以恢復。", "Remove this character from the list? Original files and the library copy are retained. Importing the same assets restores it.", "一覧から削除しますか？元の素材とライブラリのコピーは保持されます。同じ素材を追加すると復元できます。"],
            ["library.remove.help"] = ["从列表移除，保留素材以便恢复。", "從清單移除，保留素材以便恢復。", "Remove from the list while retaining assets for recovery.", "素材を保持して一覧から削除します。"],
            ["library.edit.active"] = ["正在使用的形象不能删除，请先切换到其他形象。", "正在使用的形象不能刪除，請先切換至其他形象。", "Switch to another character before removing the active one.", "使用中のキャラクターは削除できません。先に別のキャラクターに切り替えてください。"],
            ["library.edit.busy"] = ["桌宠正在交互或已停止，请稍后再试。", "桌寵正在互動或已停止，請稍後再試。", "The pet is busy or stopped. Try again later.", "ペットは操作中または停止しています。後でもう一度お試しください。"],
            ["library.edit.removed"] = ["已从列表删除，重新导入相同素材可恢复。", "已從清單刪除，重新匯入相同素材可恢復。", "Removed from the list. Import the same assets to restore.", "一覧から削除しました。同じ素材を追加すると復元できます。"],
            ["library.edit.moved"] = ["显示顺序已保存。", "顯示順序已儲存。", "Display order saved.", "表示順序を保存しました。"],
            ["library.edit.boundary"] = ["此形象已经位于列表边界。", "此形象已位於清單邊界。", "This character is already at the end of the list.", "これ以上移動できません。"],
            ["library.edit.missing"] = ["此形象已不在列表中，请刷新。", "此形象已不在清單中，請重新整理。", "The character is no longer listed. Refresh the library.", "キャラクターは一覧にありません。更新してください。"],
            ["library.edit.invalid_path"] = ["此形象目录不属于可管理的形象库。", "此形象目錄不屬於可管理的形象庫。", "The character folder is outside the managed library.", "管理対象のライブラリ内にあるフォルダーではありません。"],
            ["library.edit.failed"] = ["操作未保存：{0}", "操作未儲存：{0}", "The change was not saved: {0}", "変更を保存できませんでした：{0}"],
            ["library.import.restored"] = ["已恢复形象「{0}」。", "已恢復形象「{0}」。", "Restored '{0}'.", "「{0}」を復元しました。"],
            ["action.toggle-pet.name"] = ["桌宠", "桌寵", "Desktop pet", "デスクトップペット"],
            ["action.toggle-pet.description"] = ["触发一次显示桌宠，再次触发收起。形象和互动方式在桌宠设置中统一管理。", "觸發一次顯示桌寵，再次觸發收起。形象與互動方式在桌寵設定中統一管理。", "Trigger once to show your pet, again to hide it. Manage its character and interactions in pet settings.", "一度実行すると表示、もう一度実行すると非表示になります。キャラクターと操作はペット設定で管理します。"],
            ["action.manage-pet.name"] = ["管理桌宠", "管理桌寵", "Manage characters", "キャラクター管理"],
            ["error.toggle-failed"] = ["桌宠切换失败，请查看日志。", "桌寵切換失敗，請查看記錄。", "Could not toggle the pet. See the log for details.", "ペットを切り替えられませんでした。ログを確認してください。"],
            ["settings.page.title"] = ["桌宠设置", "桌寵設定", "Pet settings", "ペット設定"],
            ["settings.page.description"] = ["管理形象、显示大小和互动方式。轮盘上的「桌宠」动作使用这里的设置。", "管理形象、顯示大小與互動方式。輪盤上的「桌寵」動作使用這裡的設定。", "Manage characters, size and interactions. The wheel's Desktop pet action uses these settings.", "キャラクター、表示サイズ、操作を設定します。ホイールのペットアクションに適用されます。"],
            ["window.library.header"] = ["桌宠形象", "桌寵形象", "Characters", "キャラクター"],
            ["window.library.desc"] = [" · 选择形象，预览动作", " · 選擇形象，預覽動作", " · Choose a character and preview its poses", " · キャラクターとポーズを選択"],
            ["window.library.select.prompt"] = ["从左侧选择一个形象，查看预览与动作。", "從左側選擇形象，查看預覽與動作。", "Select a character to preview its poses.", "キャラクターを選択してポーズを確認してください。"],
            ["window.library.meta.format"] = ["画布 {0}×{1} · {2}/12 个状态", "畫布 {0}×{1} · {2}/12 個狀態", "Canvas {0}×{1} · {2}/12 poses", "キャンバス {0}×{1} · {2}/12 ポーズ"],
            ["window.library.states.title"] = ["动作预览", "動作預覽", "Pose preview", "ポーズプレビュー"],
            ["field.asset-directory.help"] = ["当前形象的素材目录。推荐通过「管理桌宠」导入和切换。", "目前形象的素材目錄。建議透過「管理桌寵」匯入與切換。", "Current character folder. Import and switch using Manage characters.", "現在のキャラクターフォルダー。管理画面から追加・変更できます。"],
            ["field.asset-directory.label"] = ["当前素材目录", "目前素材目錄", "Current asset folder", "現在の素材フォルダー"],
            ["field.size.label"] = ["桌宠大小", "桌寵大小", "Pet size", "ペットサイズ"],
            ["field.animation.label"] = ["播放动画", "播放動畫", "Animation", "アニメーション"],
            ["field.drag-mode.label"] = ["拖动方式", "拖動方式", "Drag mode", "ドラッグ方式"],
            ["field.drag-mode.immediate"] = ["立即拖动", "立即拖動", "Immediate drag", "すぐにドラッグ"],
            ["field.drag-mode.long-press"] = ["长按后拖动", "長按後拖動", "Long-press drag", "長押しでドラッグ"],
            ["field.long-press-duration.label"] = ["长按时长", "長按時長", "Long-press duration", "長押し時間"],
            ["field.drag-pose.label"] = ["拖动时切换姿势", "拖動時切換姿勢", "Drag pose", "ドラッグ時のポーズ"],
            ["field.pointing-pose.label"] = ["随轮盘方向切换姿势", "隨輪盤方向切換姿勢", "Wheel pointing poses", "ホイール方向のポーズ"],
            ["window.library.title"] = ["桌宠形象库", "桌寵形象庫", "Desktop pet library", "ペットライブラリ"],
            ["window.library.btn.import"] = ["导入文件夹…", "匯入資料夾…", "Import folder…", "フォルダーを追加…"],
            ["window.library.btn.refresh"] = ["刷新", "重新整理", "Refresh", "更新"],
            ["window.library.list.title"] = ["我的形象", "我的形象", "My characters", "キャラクター一覧"],
            ["window.library.btn.apply"] = ["使用此形象", "使用此形象", "Use character", "このキャラクターを使用"],
            ["window.library.btn.current"] = ["正在使用", "正在使用", "In use", "使用中"],
            ["window.library.badge.active"] = ["当前使用", "目前使用", "Active", "使用中"],
            ["window.library.card.states"] = ["{0} 个状态", "{0} 個狀態", "{0} poses", "{0} ポーズ"],
            ["window.library.status.active"] = ["桌宠正在使用此形象。", "桌寵正在使用此形象。", "Your pet is using this character.", "このキャラクターを使用しています。"],
            ["window.library.status.ready"] = ["预览满意后，点击「使用此形象」。", "預覽滿意後，點擊「使用此形象」。", "Choose Use character when you are ready.", "確認したら「このキャラクターを使用」を選択してください。"],
            ["field.size.help"] = ["显示大小，单位为逻辑像素。", "顯示大小，單位為邏輯像素。", "Display size in logical pixels.", "表示サイズ（論理ピクセル）。"],
            ["field.animation.help"] = ["开启呼吸、眨眼与互动姿势。关闭后保持静态形象。", "開啟呼吸、眨眼與互動姿勢。關閉後保持靜態形象。", "Enable breathing, blinking and interaction poses. Off keeps a static image.", "呼吸、まばたき、操作ポーズを有効にします。オフでは静止画になります。"],
            ["field.drag-mode.help"] = ["立即拖动，或长按后再拖动；轻点均可呼出轮盘。", "立即拖動，或長按後再拖動；輕點均可叫出輪盤。", "Drag immediately or after a long press. A click opens the wheel.", "すぐにドラッグ、または長押し後にドラッグできます。クリックでホイールが開きます。"],
            ["field.long-press-duration.help"] = ["仅在长按拖动模式下生效，单位为毫秒。", "僅在長按拖動模式下生效，單位為毫秒。", "Only used for long-press dragging, in milliseconds.", "長押しドラッグ時のみ使用します（ミリ秒）。"],
            ["field.drag-pose.help"] = ["拖动时使用悬空姿势，需要开启播放动画。", "拖動時使用懸空姿勢，需要開啟播放動畫。", "Use the hanging pose while dragging. Requires animation enabled.", "ドラッグ中に持ち上げポーズを使用します。アニメーションの有効化が必要です。"],
            ["field.pointing-pose.help"] = ["随轮盘扇区切换指向姿势，需要开启播放动画。", "隨輪盤扇區切換指向姿勢，需要開啟播放動畫。", "Point toward the selected wheel sector. Requires animation enabled.", "ホイールの選択方向を指します。アニメーションの有効化が必要です。"],
            ["frame.idle"] = ["常驻", "常駐", "Idle", "待機"],
            ["frame.blink"] = ["眨眼", "眨眼", "Blink", "まばたき"],
            ["frame.react"] = ["点击反馈", "點擊回饋", "Click reaction", "クリック反応"],
            ["frame.drag"] = ["拖动姿势", "拖動姿勢", "Drag pose", "ドラッグ"],
            ["frame.point_E"] = ["向右", "向右", "Right", "右"],
            ["frame.point_SE"] = ["右下", "右下", "Down-right", "右下"],
            ["frame.point_S"] = ["向下", "向下", "Down", "下"],
            ["frame.point_SW"] = ["左下", "左下", "Down-left", "左下"],
            ["frame.point_W"] = ["向左", "向左", "Left", "左"],
            ["frame.point_NW"] = ["左上", "左上", "Up-left", "左上"],
            ["frame.point_N"] = ["向上", "向上", "Up", "上"],
            ["frame.point_NE"] = ["右上", "右上", "Up-right", "右上"],
        };
        string[] languages = ["zh-CN", "zh-TW", "en-US", "ja-JP"];
        for (int i = 0; i < languages.Length; i++)
        {
            int language = i;
            i18n.RegisterTable(languages[i], entries.ToDictionary(entry => entry.Key, entry => entry.Value[language]));
        }
    }
}
