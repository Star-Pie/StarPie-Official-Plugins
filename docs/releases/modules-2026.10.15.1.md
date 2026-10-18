<p align="center"><img src="https://raw.githubusercontent.com/Star-Pie/StarPie-Official-Plugins/main/docs/media/starpie-plugins-banner.svg" alt="StarPie 官方插件与模块发布" width="100%" /></p>

# StarPie 官方插件发布 (modules-2026.10.15.1)

> - **发布标签**：`modules-2026.10.15.1`
> - **发布通道**：beta (公测预发布 / Pre-release)
> - **目录版本**：`2026.10.15.040312`
> - **宿主兼容**：支持 **StarPie v2.0.0+** 正式版及对应更新版本
> - **模块统计**：共收录 **19** 个官方模块（本次更新：**1** 个，历史沿用：**18** 个）

本发布为 **Beta 预发布通道** 产物。已通过增量打包、本地契约校验与目录签名验证；StarPie 主程序可在「插件与扩展」中直接刷新获取。

### 🌟 本次新增 / 更新模块 (Updated Packages in this Release)

本次发布包含 **1** 个重新打包或新发布的官方插件模块。以下模块安装包已直接附加于本次 Release 资产列表中：

| 模块名称 | 插件 ID | 版本 | 最低宿主 | SDK 契约 | 包大小 | 下载链接 (.spkg) |
|:---|:---|:---:|:---:|:---:|---:|:---|
| 二维码扫描 | `starpie.plugin.qrscanner` | 0.1.0 | `>= 1.8.0-beta.7` | `API 1.12` | 262.5 KB | [StarPie.Plugin.QrScanner-0.1.0.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.15.1/StarPie.Plugin.QrScanner-0.1.0.spkg) |

### 📦 安装与使用指引 (Installation Guide)

1. **StarPie 客户端内一键刷新与安装（推荐）**：
   - 打开 StarPie 宿主程序，进入「**设置**」→「**插件与扩展**」页面；
   - 点击顶部「**刷新官方目录**」或「**检查更新**」，客户端将自动同步最新的签名目录；
   - 在模块列表中找到所需插件，点击「**安装**」，完成后勾选「**启用**」即可生效。
2. **手动离线安装 (.spkg)**：
   - 在下方模块列表中点击直接下载所需模块的 `.spkg` 文件；
   - 打开 StarPie「**插件与扩展**」页面，点击「**手动安装插件**」选择下载的包文件完成解包与登记；
   - 或直接将解压内容放置于 `%LOCALAPPDATA%\StarPie\plugin-data\` 对应目录。
3. **安全性与完整性防护**：
   - 每次发布的模块包均具有唯一的 SHA-256 哈希；
   - 客户端下载时将严格验证文件大小与 SHA-256，拒绝任何不匹配或损坏的包；
   - 模块在独立的 AssemblyLoadContext (ALC) 沙箱中加载，异常被自动隔离，不影响宿主本体。

### ⚙️ 宿主兼容性与运行时环境 (Compatibility & Runtime)

- **主程序兼容性**：支持 StarPie v2.0.0 正式版及以上版本。
- **目标运行时**：.NET 8.0 WPF 进程内架构，Windows 10 / 11 (x64)。
- **契约规范**：严格限定于 SDK 抽象层与 BCL，零外部隐式动态库污染。

### 📋 完整官方模块清单与下载 (Complete Module Registry & Direct Downloads)

本发布代表当前官方启用的完整模块集。未变更模块继续保留其历史不可变 Release 资产与下载链接；客户端按需选择最高兼容版本。

| 模块名称 | 插件 ID | 版本 | 发布状态 | 最低宿主 | SDK 契约 | 下载资产 (.spkg) |
|:---|:---|:---:|:---|:---:|:---:|:---|
| 运行命令 | `starpie.builtin.command` | 1.1.1 | Referenced from [`modules-2026.10.13.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.13.1) | 1.8.0-beta.5 | 1.8 | [StarPie.Plugin.Command-1.1.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.13.1/StarPie.Plugin.Command-1.1.1.spkg) |
| 打开文件夹 | `starpie.builtin.folder` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.Folder-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.Folder-1.0.1.spkg) |
| 启动程序 | `starpie.builtin.launch` | 1.1.1 | Referenced from [`modules-2026.10.13.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.13.1) | 1.8.0-beta.5 | 1.8 | [StarPie.Plugin.Launch-1.1.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.13.1/StarPie.Plugin.Launch-1.1.1.spkg) |
| 窗口移到下一屏 | `starpie.builtin.movemonitor` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.MoveMonitor-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.MoveMonitor-1.0.1.spkg) |
| 截屏识字 (OCR) | `starpie.builtin.ocr` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.Ocr-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.Ocr-1.0.1.spkg) |
| 系统与右键工具 | `starpie.builtin.shelltool` | 1.1.1 | Referenced from [`modules-2026.10.13.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.13.1) | 1.8.0-beta.5 | 1.8 | [StarPie.Plugin.ShellTool-1.1.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.13.1/StarPie.Plugin.ShellTool-1.1.1.spkg) |
| 切换窗口 | `starpie.builtin.switchwindow` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.SwitchWindow-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.SwitchWindow-1.0.1.spkg) |
| 系统控制 | `starpie.builtin.system` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.System-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.System-1.0.1.spkg) |
| 平铺窗口 | `starpie.builtin.tile` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.Tile-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.Tile-1.0.1.spkg) |
| 窗口置顶/取消置顶 | `starpie.builtin.toggletopmost` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.ToggleTopmost-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.ToggleTopmost-1.0.1.spkg) |
| 打开网址 | `starpie.builtin.weburl` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.WebUrl-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.WebUrl-1.0.1.spkg) |
| 窗口透明度 | `starpie.builtin.windowopacity` | 1.0.1 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.WindowOpacity-1.0.1.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.WindowOpacity-1.0.1.spkg) |
| CAD 模拟按键与命令输入 | `starpie.plugin.cadcommand` | 1.0.3 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.CadCommand-1.0.3.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.CadCommand-1.0.3.spkg) |
| 桌宠 | `starpie.plugin.desktoppet` | 0.1.6 | Referenced from [`modules-2026.10.11.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.11.1) | 1.8.0-beta.7 | 1.11 | [StarPie.Plugin.DesktopPet-0.1.6.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.11.1/StarPie.Plugin.DesktopPet-0.1.6.spkg) |
| 悬浮球 | `starpie.plugin.floatingball` | 1.0.3 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.3 | 1.6 | [StarPie.Plugin.FloatingBall-1.0.3.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.FloatingBall-1.0.3.spkg) |
| 按键映射 | `starpie.plugin.keypadlayer` | 1.0.2 | Referenced from [`modules-2026.10.01.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.01.1) | 1.8.0-beta.3 | 1.7 | [StarPie.Plugin.KeypadLayer-1.0.2.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.KeypadLayer-1.0.2.spkg) |
| 按键宏与多步骤脚本 | `starpie.plugin.macro` | 1.0.0 | Referenced from [`modules-2026.10.12.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.12.1) | 1.8.0-beta.1 | 1.4 | [StarPie.Plugin.Macro-1.0.0.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.12.1/StarPie.Plugin.Macro-1.0.0.spkg) |
| 二维码扫描 | `starpie.plugin.qrscanner` | 0.1.0 | Updated in this release | 1.8.0-beta.7 | 1.12 | [StarPie.Plugin.QrScanner-0.1.0.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.15.1/StarPie.Plugin.QrScanner-0.1.0.spkg) |
| 轮盘音效 | `starpie.plugin.sound` | 1.0.0 | Referenced from [`modules-2026.10.10.1`](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/tag/modules-2026.10.10.1) | 1.8.0-beta.7 | 1.10 | [StarPie.Plugin.Sound-1.0.0.spkg](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.10.1/StarPie.Plugin.Sound-1.0.0.spkg) |
