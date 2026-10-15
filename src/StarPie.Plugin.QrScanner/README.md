# 二维码扫描插件（候选 0.1.0）

提供“框选扫码”和“识别鼠标附近二维码”两个轮盘动作。本地识别后在宿主主题窗口中展示多条结果，点击后复制或打开默认浏览器。支持标准 QR Code；首版不包含摄像头、图片文件导入、持续扫描或其他条码。

一次操作最长五分钟（框选、解码、阅读结果合计）；超过期限会取消窗口。关闭结果或取消框选后可再次触发。

最低要求 **SDK 1.12** 候选宿主。1.13 的动态语言标题通过可选服务检测启用，缺少时回退到 ShowAsync 的当前语言已翻译标题，沿用旧宿主窗口。minHostVersion=1.8.0-beta.7 只是版本下限；已发布 beta.7 的 SDK 是 1.11，没有原始图片服务，仍会拒绝该插件。1.12 候选可继续使用，完整新版 UI/语言热切换需要支持增强服务的宿主。本插件 release.enabled=false，尚未进入公开发布。

## 构建与验证

从官方插件仓库根目录：

    dotnet build src/StarPie.Plugin.QrScanner/StarPie.Plugin.QrScanner.csproj -c Release --nologo
    dotnet run --project tests/starpie.plugin.qrscanner/QrScanner.Tests.csproj -c Release
    pwsh -NoProfile -File tests/host-compatibility/qr-sdk/Run-SdkCompatibility.ps1

输出必须包含 StarPie.Plugin.QrScanner.dll、zxing.dll、plugin.json、THIRD-PARTY-NOTICES.txt 和 ZXing-LICENSE.txt，不能含 SDK 或 StarPie.dll。ZXing 是包内私有程序集，不是 plugin.json 的插件依赖，因此不能以“只选主 DLL”的裸文件形态发布。

## 依赖论证

使用固定 ZXing.Net 0.16.11 核心包（Apache-2.0）。本次 net8.0 构建 zxing.dll 实测 514,560 字节，无其他传递包或原生图像栈。仅插件按需加载，不增加主程序 NuGet 引用；生成灰度像素后交核心解码器处理，避免 System.Drawing/ImageSharp/Skia 图像绑定。

图片/放大重试单次不超过 4M 像素；鼠标半径 64–1024 物理像素。解码与窗口存在期间持有真实动作任务，取消不提前释放租约。停用回收必须绑定最终候选验证，不以“托管依赖”推断可卸载。

数据不上传、不落盘、不记录识别内容；含网页链接的二维码经确认后才打开。依赖按 ZXing 自己的 ECI/编码解释返回文本，不按有无替换字符盲目改为 Latin-1。

## 已验证与待验证

结果窗口支持单码全宽、多码列表、窄窗口下拉选择，使用宿主主题和共享标题栏。插件卡片、动作、详情、框选提示及结果控件覆盖简中/繁中/英文/日文；切换语言保留选中的原始内容。新建绑定的自动名称使用当前语言，既有自定义名称不回写。

自动覆盖真实编码夹具的原图/旋转/反色、中文、多个 QR、空图、小码、预算、URL 策略与假服务下的确认/取消/并发。截图 DPI、窗口主题、真实剪贴板和浏览器、实际延迟与工作集仍需人工验收。
