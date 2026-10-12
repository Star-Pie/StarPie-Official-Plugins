# StarPie 官方插件仓库

StarPie 官方内建动作模块的独立源码与发布仓库。

> 当前状态：15 个既有官方模块和 1 个音效插件源码候选位于本仓库，支持增量构建、`.spkg` 打包、签名 catalog 和 GitHub Release 发布。主程序提供手动刷新官方目录与安装入口；当前只验证包哈希，catalog 签名验证尚待接入。

## 仓库目标

本仓库负责维护从 StarPie 主程序中拆分出来的官方动作模块：

- 所有官方模块集中在同一个仓库中维护；
- 每个模块拥有独立版本号；
- 每次只为发生变化的模块生成不可变发布资产；
- 每次模块集合发布生成一份签名的 `module-catalog.json`；
- 未变化模块不重复上传，由 catalog 引用其历史 Release 资产；
- 发布通道分为 `stable` 和 `beta`，`beta` Release 会自动标记为 GitHub Pre-release。

StarPie 运行时实际加载的仍然是 DLL 程序集。`.spkg` 只是分发容器：StarPie 验证并解压后，再把其中的 DLL 交给现有插件运行时加载。

## 官方插件模块

既有动作模块独立发布；音效模块同时注册交互观察、设置及三个管理动作，可以独立更新、启用或停用。下表中的“动作类型”是插件兼容的 StarPie 配置类型；历史别名用于继续支持旧配置。

### 启动与文件

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| 启动程序 | `starpie.builtin.launch` | `Launch` | 选择可执行文件并启动；支持命令行参数和以标准用户身份运行。 |
| 打开文件夹 | `starpie.builtin.folder` | `Folder`、`OpenFolder` | 打开指定目录或 Shell 命名空间；传入文件路径时打开所在目录并选中文件。 |
| 打开网址 | `starpie.builtin.weburl` | `WebUrl`、`Url` | 使用默认浏览器或 Chrome、Edge、Firefox、自定义浏览器打开网址。 |
| 运行命令 | `starpie.builtin.command` | `Command` | 在选定终端中执行命令行，适合需要保留终端窗口的命令。 |
| 系统与右键工具 | `starpie.builtin.shelltool` | `ShellTool` | 调用资源管理器与系统 Shell 能力，例如复制路径、以管理员身份运行等。 |

### 窗口管理

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| 窗口移到下一屏 | `starpie.builtin.movemonitor` | `MoveMonitor` | 将当前窗口移动到枚举顺序中的下一个显示器；无额外参数。 |
| 切换窗口 | `starpie.builtin.switchwindow` | `SwitchWindow` | 按任务栏从左到右的位置激活窗口，位置从 1 开始计数。 |
| 平铺窗口 | `starpie.builtin.tile` | `Tile` | 按宿主提供的布局排列当前窗口，支持指定布局、顺序循环、反向循环和恢复。 |
| 窗口置顶/取消置顶 | `starpie.builtin.toggletopmost` | `ToggleTopmost` | 切换当前窗口的置顶状态。 |
| 窗口透明度 | `starpie.builtin.windowopacity` | `WindowOpacity` | 设置当前窗口透明度百分比，默认值为 80%，合法范围由宿主提供。 |

### 系统能力

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| 系统控制 | `starpie.builtin.system` | `System` | 通过预设下拉执行最小化、任务视图、音量、锁屏、关机等系统操作。 |
| 截屏识字 (OCR) | `starpie.builtin.ocr` | `Ocr`、`ScreenOcr` | 触发后现场框选屏幕区域，交给宿主 OCR 服务识别文字；无预配置参数。 |

### 输入增强与按键映射

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| 按键映射 | `starpie.plugin.keypadlayer` | `Plugin`（`keypadLayer`） | 通过轮盘开启或关闭自定义按键映射，将物理按键映射为目标按键；支持可视化配置，并在切换应用或长按 Esc 时安全释放。默认提供左手数字小键盘预设。 |
| 按键宏与多步骤脚本 | `starpie.plugin.macro` | `Plugin`（`macro`） | 按顺序执行多步组合键、单键、逐字文本输入、剪贴板粘贴/复制与延时等待；提供可视化步骤编排器、实时录制、多行文本配置及 DSL 脚本双向同步。 |
| 悬浮球 | `starpie.plugin.floatingball` | `Plugin`（`showBall`） | 显示可拖动悬浮球，点击呼出用户配置的轮盘。 |

### 行业与绘图工具

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| CAD 模拟按键与命令输入 | `starpie.plugin.cadcommand` | `Plugin`（`cadCommand`） | 专为 CAD 设计师优化的命令与按键输入工具：内置 Unicode 防输入法拦截、^C^C 前置取消与空格/回车提交。 |

## 仓库结构

```text
src/                         16 个官方插件源码（含音效候选）
sdk/                         与宿主共享的插件 SDK 契约快照
tests/                       模块包验证与宿主兼容性测试预留目录
build/                       构建、打包、catalog、校验与发布脚本
catalog/                     模块 catalog JSON Schema 与示例
docs/                        发布模型、模块契约与安全模型文档
templates/                   模块注册表与 plugin.json 示例
.github/workflows/           增量验证、全量验证与发布工作流
artifacts/                   仅用于 CI 产物，不提交 Git
release-metadata/            仅用于 CI 元数据，不提交 Git
```

## 发布模型

完整说明见 [`docs/release-model.md`](docs/release-model.md)。

整体行为如下：

```text
模块源码发生变化
-> 必须同步修改模块版本号
-> 只构建和测试发生变化的模块
-> 只为变化模块生成 .spkg
-> 创建一次模块集合 Release
-> Release 中只上传变化模块的 .spkg
-> 生成包含全部模块的 module-catalog.json
-> 未变化模块继续引用历史 Release 的包地址
-> Release 正文列出当前完整模块集合
```

发布时，历史 catalog 必须来自同一 `releaseChannel`。如果没有同通道历史版本，则该通道首次发布会构建完整模块集。

## 工作流

- `validate.yml`
  - 在 Pull Request 中按路径过滤运行（纯文档或仅工作流修改不触发），也可手动运行；不再在 `main` 推送时自动运行。
  - 只构建和测试本次 diff 影响的模块。
  - 触发验证的 PR 若涉及公共构建文件、catalog、Schema 等共享变更，将验证全部模块。
- `full-validation.yml`
  - 支持手动触发和每周定时触发；不再在 `main` 推送时自动运行。
  - 构建和测试全部已注册模块。
- `release-modules.yml`
  - 手动触发的正式发布工作流。
  - 只与同一 `stable` 或 `beta` 通道的最近 catalog 比较。
  - 只构建、测试、打包和验证发生变化的模块。
  - 为完整模块集合生成 catalog 和 Release 正文表格。
  - 只向新 Release 上传变化模块的包。
  - `stable` 创建正式 Release，`beta` 自动创建 Pre-release。
  - 非 dry-run 发布必须满足 `SIGNING_ENABLED=true`，并对 catalog 执行 Cosign Keyless 签名和验签。

## 开发环境要求

- Windows
- 由 `global.json` 选择的 .NET SDK
- PowerShell 7
- 用于发布工作流的 GitHub CLI
- 用于正式签名发布的 `cosign` 或 GitHub Action 安装器

## 当前进度

已完成：

1. 将 15 个既有官方模块工程迁入 `src/`；beta.7 音效候选增加第 16 个模块。
2. 为每个官方模块维护完整的 src/**/plugin.json。
3. 建立模块打包、catalog 生成、签名、增量发布与通道校验流水线。
4. 建立模块包验证与宿主兼容性测试目录。

待完成：

1. 在主程序中加入 catalog 签名验证（当前仅验证包大小与 SHA-256）。
2. 持续验证远程目录、下载与离线加载链路。

## 轮盘音效候选 `starpie.plugin.sound`

- 源码：`src/StarPie.Plugin.Sound`，候选版本1.0.0，SDK1.10，最低宿主1.8.0-beta.7。只引用SDK/BCL，输出不得携带SDK DLL。本轮不修改宿主或SDK，不打包/发布。
- 用户须单独安装、启用并选择宿主预加载；不安装/禁用时宿主不提供音效兜底。Initialize及打开设置不发声，不订阅旧Opening/Closed。
- 在插件卡片点击「设置」：保留启用、音量、五个事件开关、主题和自定义方案ID/名称；新增呈现、选择变化、展开、确认、取消五个WAV文件选择字段，宿主按现有File字段渲染「浏览」控件。
- 先将主题选为「自定义」，再选择16-bit PCM WAV（单声道或双声道）。使用文件选择器或完整绝对路径（含UNC）；相对路径会显示校验错误。每个WAV最多1MiB，超过限制回退并记录诊断，避免五事件缓存突破后台内存目标；音频可以放任意稳定位置，不会自动复制或扫描。移动/删除文件会触发既有合成回退，设置页不是音频格式检测器。
- 每个文件字段留空时沿用所选方案；清空后恢复该方案的原始配置。覆盖只作用于运行时深拷贝，不改写原方案、旧配置或私有sound.json；沿用事件相对音量，总音量及事件关闭仍生效。非自定义主题忽略这些覆盖但保留路径。
- 移除公开动作 `preview` / `importProfiles` / `exportProfiles`：配置操作不属于扇区动作，该插件尚未发布，按用户要求继续保持1.0.0。原扇区若曾绑定这三项，将变为不可用；本轮不自动改写用户动作配置，请手动移除这些旧绑定。
- 本轮只提供直接WAV自定义；导入/导出方案和独立试听按钮暂不提供，不能通过复选框或路径变化偷偷执行设置操作。已有方案、旧迁移数据及内部方案处理/试听行为保留，不删除用户资产；原方案ID/名称选择方式保持兼容。
- 声明FileSystem：旧config.json只读迁移；WAV只在插件音频缓存刷新时读取，设置变更只增加revision。迁移幂等，显式false及文档/Preferences/方案/事件四层未知字段保留；数据文件限8MiB，不把大型JSON塞入SDK字符串设置。
- 保留静音消费、Custom自引用防护、单槽/单工作线程及既有时限。宿主后台停用时在锁外最多等待5秒排空，超时明确失败，不原生中断同步播放。
- 安全验证：`dotnet run --project tests/starpie.plugin.sound/Sound.Tests.csproj -c Release`；Mock音频，不创建UI/钩子、不执行真实插件动作。新增字段、继承/覆盖/清空/重载/主题/禁用及实际Mock播放行为均有回归；原方案IO/取消/ALC相关验证保留。
- 人工验收：重新安装候选插件后，确认设置页出现五个浏览字段；自定义主题下各事件播放所选WAV，清空恢复原音色；开关及音量正常，手势动作列表不再出现三项设置操作。真实文件选择器、设备和声音仍须用户验收，自动测试不能替代。
