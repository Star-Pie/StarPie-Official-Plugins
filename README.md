<p align="center">
  <img src="docs/media/starpie-plugins-hero-v2.png" alt="StarPie 官方插件 · 你的轮盘，按需扩展" width="100%" />
</p>

# StarPie 官方插件

[![Modules](https://img.shields.io/badge/Modules-22-7c6ee6?style=flat-square)](#模块一览)
[![Host](https://img.shields.io/badge/Host-StarPie_v2.0.0-547fea?style=flat-square)](https://github.com/Star-Pie/StarPie/releases/tag/v2.0.0)
[![Packages](https://img.shields.io/badge/Packages-.spkg-29a99b?style=flat-square)](https://github.com/Star-Pie/StarPie-Official-Plugins/releases)
[![License](https://img.shields.io/badge/License-MIT-8790a6?style=flat-square)](LICENSE)

把常用的启动、窗口、输入和桌面功能放进你的轮盘。这里维护 StarPie 官方插件的源码、构建脚本和独立安装包；主程序见 [StarPie](https://github.com/Star-Pie/StarPie)。

**插件源码**　[GitHub](https://github.com/Star-Pie/StarPie-Official-Plugins) · [AtomGit 镜像](https://atomgit.com/Star-Pie/StarPie-Official-Plugins)

**第一次使用？** [下载主程序](https://github.com/Star-Pie/StarPie/releases/latest) · [查看安装步骤](#安装与使用) · [GitHub 正式版插件目录](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/latest) · [AtomGit 插件发行页](https://atomgit.com/Star-Pie/StarPie-Official-Plugins/releases)

## 安装与使用

建议使用当前的 **StarPie v2.0.0 正式版**。具体兼容条件以各模块的最低宿主版本与 SDK 契约为准，客户端会选择兼容版本。

1. 打开 StarPie 设置，进入「**插件与扩展**」。
2. 在「**官方插件**」区域点击「**刷新目录**」。
3. 选择需要的模块，点击「**安装**」，再根据需要启用。
4. 在轮盘动作配置中选择已启用插件提供的动作。轮盘音效等功能通过插件设置管理。

v2.0.0 通过客户端目录下载和校验官方插件。在 v2.0.1-beta.4 中，也可用「导入插件文件」离线安装官方 `.spkg`。客户端会用内置或已获取的可信目录核验包大小和 SHA-256。[宿主预发布说明](https://github.com/Star-Pie/StarPie/releases/tag/v2.0.1-beta.4)

## 模块一览

下列 19 个插件按用途分组。点击插件名，可查看它在 StarPie v2.0.0 中的配置入口、示例和完整截图。[浏览完整配置图册](docs/plugin-configuration-gallery.md)

[启动与文件](#启动与文件) · [窗口管理](#窗口管理) · [系统与识别](#系统与识别) · [输入与桌面扩展](#输入与桌面扩展)

### 启动与文件

| 插件 | 用途 |
| --- | --- |
| [启动程序](docs/plugin-configuration-gallery.md#launch) | 从轮盘启动指定程序，可附带启动参数。 |
| [打开文件夹](docs/plugin-configuration-gallery.md#folder) | 直接打开项目目录、下载目录等常用位置。 |
| [打开网址](docs/plugin-configuration-gallery.md#weburl) | 用默认或指定的浏览器打开常用网页。 |
| [运行命令](docs/plugin-configuration-gallery.md#command) | 在选定的终端中运行预设命令。 |
| [系统与右键工具](docs/plugin-configuration-gallery.md#shelltool) | 从工具库中挑选系统工具或右键菜单功能，绑定到扇区。 |

### 窗口管理

在「手势动作 → 窗口管理」中选择对应模式。

| 插件 | 用途 |
| --- | --- |
| [切换窗口](docs/plugin-configuration-gallery.md#switchwindow) | 切换到任务栏中指定序号的窗口。 |
| [窗口移到下一屏](docs/plugin-configuration-gallery.md#movemonitor) | 把活动窗口移到下一台显示器。 |
| [平铺窗口](docs/plugin-configuration-gallery.md#tile) | 按预设布局排列窗口，例如左右对半。 |
| [窗口置顶/取消置顶](docs/plugin-configuration-gallery.md#toggletopmost) | 让当前窗口保持在前，再次触发可取消置顶。 |
| [窗口透明度](docs/plugin-configuration-gallery.md#windowopacity) | 通过预设值或滑块调整窗口的不透明度。 |

### 系统与识别

| 插件 | 用途 |
| --- | --- |
| [系统控制](docs/plugin-configuration-gallery.md#system) | 从轮盘打开任务视图等系统功能。 |
| [截屏识字（OCR）](docs/plugin-configuration-gallery.md#ocr) | 提取屏幕上的文字，可在「接口配置」中选择识别引擎。 |
| [二维码扫描](docs/plugin-configuration-gallery.md#qrscanner) | 识别鼠标附近或框选区域内的二维码，先预览内容再选择操作。 |

### 输入与桌面扩展

| 插件 | 用途 |
| --- | --- |
| [按键宏与多步骤脚本](docs/plugin-configuration-gallery.md#macro) | 将按键、等待和文本输入编排成一个动作。 |
| [按键映射](docs/plugin-configuration-gallery.md#keypadlayer) | 把左手键区映射成数字小键盘，例如用 Q、W、E 输入 7、8、9。 |
| [CAD 模拟按键与命令输入](docs/plugin-configuration-gallery.md#cadcommand) | 向 CAD 输入命令，例如发送 `_LINE` 后按 Enter。 |
| [桌宠](docs/plugin-configuration-gallery.md#desktoppet) | 从桌面伙伴唤出轮盘，可调整素材、动画和显示大小。 |
| [悬浮球](docs/plugin-configuration-gallery.md#floatingball) | 用可拖动的悬浮球呼出轮盘，可调整大小、颜色和透明度。 |
| [轮盘音效](docs/plugin-configuration-gallery.md#sound) | 为轮盘展开、选择、确认和取消等操作分别设置声音。 |

主线另含[临时小键盘](src/StarPie.Plugin.TemporaryKeyboard)、[识图翻译](src/StarPie.Plugin.Selinga)和[快速搜索](src/StarPie.Plugin.QuickSearch)，共 22 个源码模块。快速搜索 0.2.2 可独立索引所选磁盘和目录，无需 Everything。当前发布目录尚未提供快速搜索安装包。

## 分发与加载方式

- **按需获取**：主程序发行包不携带官方插件 DLL；插件下载与更新由用户操作触发。
- **完整目录，增量发布**：每次模块集发布包含全部模块的目录；只上传变更模块的包，其余模块继续引用历史 Release 的不可变资产。
- **完整性检查**：签名目录提供包的大小、SHA-256 与兼容条件，客户端在安装流程中核对。
- **进程内加载**：ALC 用于管理程序集加载与卸载，不是安全沙箱，也不提供进程级崩溃隔离。请仅安装信任的插件。

`modules-2026.10.15.1` 已转为 GitHub 正式发布。其原始签名目录中的 `beta` 字段保留，标签、安装包和签名未改写；这与创建新的 stable 目录是不同操作。

<details>
<summary><b>开发、验证与仓库维护</b></summary>

### 本地验证

```powershell
./build/Build-Modules.ps1 -ModuleIds <module-id> -Configuration Release -RunTests
./build/Pack-Module.ps1 -ModuleId <module-id> -Configuration Release -ReleaseTag local -OutputMetadataPath release-metadata/packages/<module-id>.json -Clean
./build/Verify-ModulePackage.ps1 -PackagePath <package-path> -ModuleId <module-id>
pwsh -NoProfile -File tests/catalog/Validate-CatalogV2.ps1
pwsh -NoProfile -File tests/catalog/Validate-ReleaseNotes.ps1
```

### 目录与发布约定

| 路径 | 内容 |
|:---|:---|
| `src/` | 各模块源码与 `plugin.json` |
| `sdk/` | 公共 SDK 契约快照 |
| `build/` | 构建、打包、目录与发布说明生成 |
| `tests/` | 契约、行为与目录验证 |
| `docs/media/` | 品牌封面与实机配置截图 |
| `docs/plugin-configuration-gallery.md` | 19 个插件的配置入口、截图与示例 |
| `.github/workflows/` | PR 验证、全量检查与模块集发布 |

共享构建或发布脚本的变动会触发全量模块验证。发布工作流支持 stable/beta 通道与目录基线选择；已有历史包和签名目录不得通过改写来替换。详见 [发布模型](docs/release-model.md)、[安全模型](docs/security-model.md) 与 [贡献指南](CONTRIBUTING.md)。

插件引用公共 SDK；第三方依赖须符合仓库审批约定。二维码扫描包含 ZXing.Net 及对应许可声明。分发包不得携带主程序或私有 SDK 副本。

</details>

源码许可见 [MIT License](LICENSE)。
