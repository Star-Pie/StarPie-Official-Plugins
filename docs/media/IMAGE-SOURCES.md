# 图片来源与拍摄信息

## README 封面

- 文件：`starpie-plugins-hero-v2.png`。
- 工具：OpenAI ImageGen 内置图片生成，参考 StarPie 蓝紫色分段轮盘项目图标。
- 用途：品牌装饰，不代表程序界面或功能运行结果。
- 生成提示词：

> Recompose the supplied StarPie icon into a premium wide GitHub README hero, approximately 3:1. Preserve the segmented blue-violet wheel, white-lilac pointer, and four-point upper-right sparkle. Place a sculptural optical-glass wheel in the left third, against a matte midnight-indigo quiet gradient. Use sophisticated editorial software branding. Right-hand typography, exactly: StarPie / OFFICIAL PLUGINS / 你的轮盘，按需扩展. No other copy, versions, badges, cards, diagrams, fake interface, screenshots, windows, starfield, HUD, or watermark. Decorative branding only; real screenshots are separate.

## 程序配置截图

- 拍摄日期：2026-10-18。
- 程序：StarPie v2.0.0，宿主源码提交 `248b72362427e9d7492d0bcef55db0af31b3895b`。
- 环境：Windows，独立演示配置；使用已发布的 19 个官方插件包。
- 操作：只查看和配置，没有测试触发、扫描屏幕、执行命令、输入模拟或窗口控制。
- 处理：从原始窗口截图裁切连续矩形，PNG 无损保存；预览缩略图只等比缩放并加纯色留白。
- 原则：未用图片生成工具绘制或修改控件、文字、参数或功能效果。
- `screenshots/manifest.json` 记录插件 ID、版本、原始图摘要、裁切范围与成图摘要，便于更新时核对。
- 示例目录、命令与进程名仅用于演示；真实界面的文字、布局会随宿主版本、主题和系统环境变化。

以后更新某个插件的界面时，请重新截取对应面板，并同步配置图册、预览图与清单。旧流程示意 SVG 保留供历史发布说明使用，README 当前使用真实截图。

## 表情浮窗 (`starpie.plugin.emojipicker`) 界面展示图

- 文件：`screenshots/emojipicker-picker-dark.png`、`screenshots/emojipicker-custom-dark.png`、`screenshots/emojipicker-manager.png`。
- 日期：2026-11-22。
- 拍摄 / 渲染方式：通过离线验证入口 [`Program.cs`](../../tests/starpie.plugin.emojipicker/Program.cs) 的 `STARPIE_EMOJI_UI_PREVIEW` 开关，直接调用真实 WPF 窗口控件（`EmojiPickerWindow` 与 `EmojiManagerWindow`）在 `144 DPI`（`1.5x`）下无损渲染为 PNG；使用独立示例贴纸与挂载目录数据，无 AI 生成控件。
- 说明：WPF 离屏 `RenderTargetBitmap` 将 Unicode Emoji 渲染为单色线框轮廓；实际 Windows 桌面硬件渲染环境下显示彩色 `Segoe UI Emoji`。
