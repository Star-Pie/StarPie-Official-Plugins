using StarPie.Plugin;

namespace StarPie.Plugin.QrScanner;

internal static class QrTexts
{
    internal static void Register(II18nRegistry registry)
    {
        (string Key, string Cn, string Tw, string En, string Ja)[] rows =
        [
            ("title", "二维码识别结果", "QR 碼辨識結果", "QR scan results", "QRコード認識結果"),
            ("scan.region", "框选扫码", "框選掃碼", "Scan a selected region", "範囲を選択してスキャン"),
            ("scan.region.description", "框选屏幕上的二维码，查看内容后复制或打开链接。", "框選螢幕上的 QR 碼，查看內容後複製或開啟連結。", "Select a QR code on screen, then review, copy or open its content.", "画面上のQRコードを選択し、内容を確認してコピーまたはリンクを開きます。"),
            ("scan.pointer", "识别鼠标附近二维码", "辨識滑鼠附近 QR 碼", "Scan near the pointer", "マウス付近のQRコードをスキャン"),
            ("scan.pointer.description", "识别鼠标周围的二维码，查看内容后复制或打开链接。", "辨識滑鼠周圍的 QR 碼，查看內容後複製或開啟連結。", "Scan QR codes around the pointer, then review, copy or open their content.", "マウス付近のQRコードを認識し、内容を確認してコピーまたはリンクを開きます。"),
            ("category", "效率工具", "效率工具", "Productivity", "効率化ツール"),
            ("radius", "扫描半径（物理像素）", "掃描半徑（實體像素）", "Scan radius (physical pixels)", "スキャン半径（物理ピクセル）"),
            ("radius.invalid", "扫描半径须为 64–1024 的整数。", "掃描半徑須為 64–1024 的整數。", "Radius must be an integer from 64 to 1024.", "半径は64～1024の整数で指定してください。"),
            ("busy", "扫码正在进行，请先关闭当前结果或取消框选。", "掃碼進行中，請先關閉目前結果或取消框選。", "A scan is active. Close its results or cancel the selection first.", "スキャン中です。結果を閉じるか範囲選択をキャンセルしてください。"),
            ("notfound", "未识别到二维码，请包含完整二维码及周围留白后重试。", "未辨識到 QR 碼，請包含完整 QR 碼及周圍留白後重試。", "No QR code found. Include the whole code and its surrounding margin, then retry.", "QRコードが見つかりません。コード全体と周囲の余白を含めて再試行してください。"),
            ("failed", "扫码失败。请缩小框选区域，或使用鼠标附近扫码后重试。", "掃碼失敗。請縮小框選區域，或使用滑鼠附近掃碼後重試。", "Scan failed. Select a smaller region or try scanning near the pointer.", "スキャンに失敗しました。範囲を小さくするかマウス付近をスキャンしてください。"),
            ("operation.failed", "复制或打开链接失败，请重新识别后重试。", "複製或開啟連結失敗，請重新辨識後重試。", "Copying or opening failed. Scan again to retry.", "コピーまたはリンクを開けませんでした。再度スキャンしてください。")
        ];
        registry.RegisterTable("zh-CN", rows.ToDictionary(row => row.Key, row => row.Cn));
        registry.RegisterTable("zh-TW", rows.ToDictionary(row => row.Key, row => row.Tw));
        registry.RegisterTable("en", rows.ToDictionary(row => row.Key, row => row.En));
        registry.RegisterTable("ja", rows.ToDictionary(row => row.Key, row => row.Ja));
    }
}
