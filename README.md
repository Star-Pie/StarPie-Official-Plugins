# StarPie 官方插件仓库

StarPie 官方内建动作模块的独立源码与发布仓库。

> 当前状态：13 个官方动作插件已迁入本仓库，支持增量构建、`.spkg` 打包、签名 catalog 和 GitHub Release 发布。主程序侧的远程模块安装客户端仍待接入。

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

所有官方插件均以单动作模块形式发布，可以独立更新、启用或停用。下表中的“动作类型”是插件兼容的 StarPie 配置类型；历史别名用于继续支持旧配置。

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

### 行业与绘图工具

| 插件 | 插件 ID | 动作类型 | 基本功能 |
|---|---|---|---|
| CAD 模拟按键与命令输入 | `starpie.builtin.cadcommand` | `CadCommand` | 专为 CAD 设计师优化的命令与按键输入工具：内置 Unicode 防输入法拦截、^C^C 前置取消与空格/回车提交。 |

## 仓库结构

```text
src/                         13 个官方插件源码与插件 SDK 契约快照
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
  - 在 Pull Request 和 `main` 分支推送时运行。
  - 只构建和测试本次 diff 影响的模块。
  - 公共构建文件、catalog、workflow、Schema 或模块注册表变化时，触发全量验证。
- `full-validation.yml`
  - 支持手动触发、每周定时触发，以及公共构建基础设施变化时触发。
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

1. 将 13 个官方模块工程迁入 `src/`。
2. 填充 `module-registry.json`。
3. 建立模块打包、catalog 生成、签名、增量发布与通道校验流水线。
4. 建立模块包验证与宿主兼容性测试目录。

待完成：

1. 在主程序中增加远程 catalog 读取、签名验证与 `.spkg` 安装客户端。
2. 完成远程安装链路验证。
3. 只有在远程安装链路验证通过后，才移除主仓库的 `ProjectReference` 和随包 DLL 复制逻辑。
