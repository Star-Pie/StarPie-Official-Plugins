<p align="center">
  <img src="docs/media/starpie-plugins-banner.svg" alt="StarPie 官方插件与模块仓库" width="100%" />
</p>

# StarPie 官方插件与模块仓库 (StarPie Official Plugins)

[![Official Modules](https://img.shields.io/badge/Official%20Modules-19%20Verified-0ea5e9.svg?style=flat-square)](catalog/)
[![Host Compatibility](https://img.shields.io/badge/Host-StarPie%20v2.0.0%2B-6366f1.svg?style=flat-square)](https://github.com/Star-Pie/StarPie)
[![Distribution](https://img.shields.io/badge/Distribution-.spkg%20(SHA--256)-10b981.svg?style=flat-square)](https://github.com/Star-Pie/StarPie-Official-Plugins/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0284c7.svg?style=flat-square)](https://microsoft.com/windows)
[![Runtime](https://img.shields.io/badge/.NET-8.0%20WPF-512bd4.svg?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-38bdf8.svg?style=flat-square)](LICENSE)

StarPie 官方动作模块与桌面扩展插件的独立源码维护、自动化构建及增量分发仓库。

所有 **19 个官方模块** 均从主程序完全解耦，以独立的不可变 `.spkg` 格式打包分发，并在 StarPie 进程内的独立程序集加载上下文（`AssemblyLoadContext`, ALC）中安全运行。

---

## 🌟 核心设计原则

- **全量目录与增量打包**：每次模块集发布生成一份收录当前全部 19 个启用模块的签名 `module-catalog.json`；只有源码或版本发生变化的模块才会打包并上传至对应 Release，未变动的模块继续引用历史不可变资产。
- **零隐式网络请求**：StarPie 发行包不随包预装任何官方插件 DLL，后台常驻保持零自动网络请求。所有模块的获取、安装与升级均由用户在设置中显式知情同意后手动触发。
- **进程内沙箱与故障隔离**：每个插件在独立的 ALC 沙箱中执行。调用租约超时与异常隔离机制可防止单一模块崩溃波及 StarPie 宿主本体；连续异常会自动触发宿主安全模式防护。
- **双重完整性校验**：所有模块包严格校验 SHA-256 哈希与字节大小，确保下载与解压内容与官方 catalog 描述完全一致。

---

## 🧩 官方模块架构矩阵

本仓库当前正式收录并启用全部 **19 个官方模块**，覆盖基础系统操作、桌面窗口编排、视觉文字识别以及丰富的个性化交互扩展：

<p align="center">
  <img src="docs/media/module-matrix.svg" alt="StarPie 19 个官方模块全景矩阵" width="100%" />
</p>

---

## 🚀 安装与使用指引

<p align="center">
  <img src="docs/media/installation-workflow.svg" alt="StarPie 官方插件安装与调用流程" width="100%" />
</p>

### 途径一：StarPie 客户端内一键刷新与安装（推荐）

1. **进入插件管理**：启动 StarPie（**v2.0.0 正式版** 及以上），右键托盘图标或通过快捷键打开「**设置**」，进入「**插件与扩展**」页面；
2. **刷新官方目录**：点击顶部的「**刷新官方目录**」或「**检查更新**」，客户端将安全获取最新签名的模块列表；
3. **按需安装与启用**：在模块列表中选择所需的功能插件，点击「**安装**」；安装完成后勾选「**启用**」即可完成加载；
4. **轮盘扇区编排**：切换至「**轮盘动作**」页面，在动作编辑画布中将已启用的动作拖拽或配置到对应方位扇区即可立即使用。

### 途径二：离线手动安装 (.spkg)

1. 前往本仓库的 [Releases 页面](https://github.com/Star-Pie/StarPie-Official-Plugins/releases)，下载目标模块的 `.spkg` 安装包；
2. 打开 StarPie「**插件与扩展**」页面，点击「**手动安装插件**」，选取下载的 `.spkg` 文件完成校验与解包；
3. 或手动将包解压后置于 `%LOCALAPPDATA%\StarPie\plugin-data\` 对应目录。

---

## 📋 官方模块分组全览

依据真实 `plugin.json` 清单与最新发布 catalog 核对，以下为全部 19 个官方模块的详细信息：

### 1. 基础启动与文件交互 (5 个内置动作)

| 模块名称 | 插件 ID | 当前版本 | 兼容动作类型 | 功能特性说明 |
|:---|:---|:---:|:---|:---|
| **启动程序** | `starpie.builtin.launch` | 1.1.1 | `Launch` | 选择或拖拽可执行文件（EXE/快捷方式/应用）启动；支持命令行参数及以标准用户权限运行。 |
| **打开文件夹** | `starpie.builtin.folder` | 1.0.1 | `Folder`, `OpenFolder` | 打开本地绝对路径或 Shell 虚拟命名空间；传入文件路径时自动在资源管理器中定位并选中该文件。 |
| **打开网址** | `starpie.builtin.weburl` | 1.0.1 | `Url`, `WebUrl` | 使用系统默认浏览器或 Chrome、Edge、Firefox、自定义浏览器打开目标 URL。 |
| **运行命令** | `starpie.builtin.command` | 1.1.1 | `Command` | 在指定终端（CMD、PowerShell、WSL 或静默后台）中执行命令行脚本，支持参数传递。 |
| **系统与右键工具** | `starpie.builtin.shelltool` | 1.1.1 | `ShellTool` | 调用 Windows 系统与资源管理器 Shell 能力（复制路径、以管理员身份运行等）。 |

### 2. 窗口管理与桌面布局 (5 个内置动作)

| 模块名称 | 插件 ID | 当前版本 | 兼容动作类型 | 功能特性说明 |
|:---|:---|:---:|:---|:---|
| **切换窗口** | `starpie.builtin.switchwindow` | 1.0.1 | `SwitchWindow` | 按任务栏从左到右的位置顺序瞬间激活指定窗口，位置从 1 开始计数。 |
| **窗口移到下一屏** | `starpie.builtin.movemonitor` | 1.0.1 | `MoveMonitor` | 将当前前台活动窗口无缝移动到多显示器枚举顺序中的下一个屏幕；无需额外参数。 |
| **平铺窗口** | `starpie.builtin.tile` | 1.0.1 | `Tile` | 按宿主预设布局快速平铺当前窗口；支持指定布局模式、顺序循环切换与快速恢复。 |
| **窗口置顶/取消置顶** | `starpie.builtin.toggletopmost` | 1.0.1 | `ToggleTopmost` | 切换当前活动窗口的顶级置顶（Topmost）状态，便于多任务对照参考。 |
| **窗口透明度** | `starpie.builtin.windowopacity` | 1.0.1 | `WindowOpacity` | 动态调节当前活动窗口的不透明度百分比（默认 80%），提供舒适的透视工作体验。 |

### 3. 系统能力与视觉识别 (2 个核心动作)

| 模块名称 | 插件 ID | 当前版本 | 兼容动作类型 | 功能特性说明 |
|:---|:---|:---:|:---|:---|
| **系统控制** | `starpie.builtin.system` | 1.0.1 | `System` | 通过预设下拉执行锁屏、睡眠、关机、任务视图、显示桌面、系统音量调节等高频操作。 |
| **截屏识字 (OCR)** | `starpie.builtin.ocr` | 1.0.1 | `Ocr`, `ScreenOcr` | 触发后框选屏幕任意区域，由宿主调用 Windows 本地或配置引擎识别提取文本并复制。 |

### 4. 输入增强与桌面扩展 (7 个功能插件)

| 模块名称 | 插件 ID | 当前版本 | 动作标识 / 入口 | 功能特性说明 |
|:---|:---|:---:|:---|:---|
| **按键宏与多步骤脚本** | `starpie.plugin.macro` | 1.0.0 | `Plugin` (`macro`) | 按顺序执行多步组合键、单键、逐字文本键入、剪贴板读写与精准毫秒延时；内置步骤编排器、实时录制及 DSL 脚本双向编辑。 |
| **按键映射** | `starpie.plugin.keypadlayer` | 1.0.2 | `Plugin` (`keypadLayer`) | 通过轮盘快捷开关自定义按键映射层，将物理按键映射至目标按键；提供左手数字小键盘预设，长按 Esc 或切换应用时安全释放。 |
| **桌宠** | `starpie.plugin.desktoppet` | 0.1.6 | `Plugin` (`togglePet` / `petControl`) | 灵动桌面小宠物，支持自由拖动放置、状态互动，轻点可瞬间呼出专属快捷轮盘；提供宠物图库与行为配置。 |
| **悬浮球** | `starpie.plugin.floatingball` | 1.0.3 | `Plugin` (`showBall`) | 在屏幕任意边缘放置常驻悬浮小球，轻触即时呼出轮盘；支持自动贴边停靠、透明度调节与拖拽位置记忆。 |
| **CAD 模拟按键与命令** | `starpie.plugin.cadcommand` | 1.0.3 | `Plugin` (`cadCommand`) | 专为 AutoCAD / SolidWorks 等工程设计软件打造：内置 Unicode 防输入法干扰、`^C^C` 前置取消与空格/回车提交。 |
| **轮盘音效** | `starpie.plugin.sound` | 1.0.0 | `Plugin` (`soundEffects`) | 监听轮盘呼出、选项切换、展开、确认与取消等交互事件并播放反馈音效；在插件卡片设置页支持为各事件指定自定义 16-bit PCM WAV 音频。 |
| **二维码扫描** | `starpie.plugin.qrscanner` | 0.1.0 | `Plugin` (`scanQr`) | 框选或自动识别鼠标邻近区域二维码，通过内置轻量解码器本地离线解析，弹出格式化预览与一键复制面板。 |

---

## ⚙️ 宿主兼容性与运行时规范

- **主程序兼容性**：支持 **StarPie v2.0.0 正式版** 及以上版本。所有模块的最低宿主版本（`minHostVersion`，范围 `1.8.0-beta.1` 至 `1.8.0-beta.7`）在 StarPie v2.0.0 正式版中均获得全量向上兼容支持。
- **目标运行时**：.NET 8.0 WPF 进程内架构，Windows 10 / 11 (x64)。
- **依赖纯净度**：各插件仅依赖 SDK 契约（`StarPie.Plugin.Abstractions`）与 .NET BCL 基类库；输出产物严禁携带 SDK DLL 或未授权外部动态链接库。
- **能力授权透明**：插件在 `plugin.json` 中声明的能力（如 `Process` 启动进程、`InputSimulation` 按键模拟、`ScreenCapture` 屏幕采集、`FileSystem` 本地只读存储等）在用户安装和启用时均清晰展示。

---

<details>
<summary><b>🛠️ 开发者指南、打包测试与仓库维护说明（点击展开）</b></summary>

### 1. 本地模块构建与验证命令

在修改或新增官方模块时，请在本地运行标准构建与验证指令：

```powershell
# 1. 构建模块并运行相关测试
./build/Build-Modules.ps1 -ModuleIds <module-id> -Configuration Release -RunTests

# 2. 模拟本地打包并生成包元数据
./build/Pack-Module.ps1 -ModuleId <module-id> -Configuration Release -ReleaseTag local -OutputMetadataPath release-metadata/packages/<module-id>.json -Clean

# 3. 校验产物 .spkg 格式与契约完整性
./build/Verify-ModulePackage.ps1 -PackagePath <package-path> -ModuleId <module-id>

# 4. 运行无 GUI 的 Catalog 发布流水线测试
pwsh -File tests/catalog/Validate-CatalogV2.ps1
pwsh -File tests/catalog/Validate-ReleaseNotes.ps1
```

### 2. 仓库目录结构

```text
src/                         19 个官方插件与动作源码工程
sdk/                         与宿主共享的插件 SDK 抽象契约快照 (StarPie.Plugin.Abstractions)
tests/                       无 GUI 的 catalog 验证、契约探测与行为测试
build/                       模块构建、打包、catalog 生成、签名校验与发布说明生成脚本
catalog/                     模块 catalog JSON Schema 与本地示例
docs/                        发布模型、模块契约、安全模型与静态媒体资源
  media/                     矢量横幅、模块架构图与安装流程图
.github/workflows/           增量 PR 验证、定时全量检查与模块集合发布工作流
artifacts/                   本地与 CI 临时输出产物（受 .gitignore 忽略，不提交版本库）
```

### 3. CI 工作流矩阵

- `validate.yml`：在针对模块源码或构建脚本的 PR 中自动触发，仅构建并测试受 diff 影响的模块；若涉及公共构建逻辑则运行全量测试。
- `full-validation.yml`：每周定时或手动触发，构建并测试全部 19 个官方模块。
- `release-modules.yml`：手动触发的正式发布流水线，自动与同通道最近发布的 catalog 进行增量比对，只构建、测试、打包和上传变更模块，并生成包含全部模块的签名 catalog。

### 4. 轮盘音效模块 (`starpie.plugin.sound`) 设置规范

- **源码位置**：`src/StarPie.Plugin.Sound`，SDK 1.10，最低宿主 1.8.0-beta.7。
- **自定义音频**：在插件卡片点击「设置」，将主题选为「自定义」，即可为呈现、选择变化、展开、确认、取消五个事件分别指定 16-bit PCM WAV 音频文件（单/双声道，文件大小限制 ≤ 1 MiB）。
- **回退机制**：文件缺失或格式不合规时自动回退至内置方案；清空路径字段即可恢复默认方案配置。

</details>

---

## 📄 开源许可证

本项目源码基于 [MIT License](LICENSE) 许可协议开源。各模块如包含第三方合规引用的开源组件（如二维码扫描中使用的 ZXing.Net 解码逻辑），其第三方开源声明与许可副本均独立存放于各模块源码目录内。
