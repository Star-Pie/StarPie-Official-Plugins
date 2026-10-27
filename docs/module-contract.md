# Official Module Contract

## Module metadata and generated registry

Every official module is declared in src/**/plugin.json. CI generates a temporary registry from those manifests for build and release selection.

Required fields:

- `id`: stable module ID; historical migrated actions use `starpie.builtin.*`, while new official plugins use `starpie.plugin.*`;
- `name`: display name used by build and release output;
- `project`: relative path to the module `.csproj`;
- `assembly`: expected output DLL name;
- `targetFramework`: expected output target framework;
- `version`: module package version;
- `pluginId`: runtime plugin ID written to `plugin.json`;
- `contributionId`: action contribution ID implemented by the module;
- `typeClaims`: legacy top-level action types claimed by the module; new `starpie.plugin.*` modules should leave this empty, while historical migrated `starpie.builtin.*` modules may claim their original action types;
- `capabilities`: declared host capabilities;
- `releaseDirectory`: logical release grouping for diagnostics.

## Package layout

An `.spkg` package is a deterministic ZIP archive containing:

```text
plugin.json
module.manifest.json
<module assembly>.dll
optional module dependencies
optional assets/
```

`plugin.json` is consumed by StarPie's existing plugin scanner and loader.

`module.manifest.json` is consumed by the module installer and package-verification pipeline. It records the module version, assembly hash, SDK version, host compatibility range, Type Claims and capabilities.

## Runtime contract

The package installer extracts `.spkg` into the writable plugin host directory. StarPie then uses the existing scanner and runtime:

```text
.spkg
-> verify catalog hash
-> extract to plugin-data/<pluginId>/
-> PluginScanner
-> PluginManifestReader
-> PluginInstance
-> PluginLoadContext
-> PluginRuntime
```

The runtime does not load `.spkg` directly.

## SDK 1.8 进程启动权限

Launch / Command / ShellTool 1.1.0 声明 apiVersion 1.8，并在插件自己的扩展参数中保存 launchMode（Default / Administrator / StandardUser）。它不是 HostActionFields 或宿主裸配置字段。参数表单使用现有 Enum 类型。
三个插件分别调用 Host.LaunchWithMode、Commands.RunWithMode、Shell.InvokeWithMode；宿主负责机制，插件负责模式选择。旧接口保留；固定权限失败或取消不改用其他模式，环境不支持应给出可见提示而不是计入插件缺陷熔断。
Launch 的旧 RunAsStandardUser 仅在新键缺失时回落；插件用通用 FallbackParameterKey / FallbackValueMap 声明旧值回填。新键显式 Default 优先于旧 true。
ShellTool 的显式模式仅支持 CMD、PowerShell、Windows Terminal、Git Bash、VS Code 启动，不适用于剪贴板、回收站或本身请求管理员的其他动词。打包应用管理员激活不支持。此启动模式不是插件沙箱，不能阻止目标应用后续自行提权。

Launch、Command、ShellTool 1.1.0 的 minHostVersion 统一为 `1.8.0-beta.5`；apiVersion 仍为 `1.8`。应先发布支持该契约的 beta.5 宿主，再发布三个模块。版本同步本身不创建 Release。

## 进程模块（runtime = process，SDK 1.14）

模块的 plugin.json 里写 "runtime": "process"，它就由宿主**拉起一个独立进程**执行，宿主不加载任何程序集。

与程序集模块的差别：

| | 程序集模块 | 进程模块 |
|---|---|---|
| plugin.json 必填 | assembly / entryType / targetFramework | 都不需要；改为 command 与 actions |
| 动作来源 | 运行时 IActionContribution 自描述 | 清单 actions[] 静态声明 |
| 打包核心 | 入口程序集 | command[0] 指向的可执行文件 |
| 完整性 | assemblySha256 | files[]（逐文件 sha256 + size） |

### payload 不进仓库

可执行文件与 C# 的 DLL 同属构建产物：DLL 由 CI 用**本仓源码**编译，进程模块的 exe 来自**发布仓库的 Release 资产**。两者都不提交。

模块目录里放 payload.json 描述来源：

    { "repository": "Sunse666/Selinga", "releaseTag": "v0.5.0", "assetName": "selinga.exe",
      "sha256": "…", "extractTo": "bin/selinga.exe" }

build/Pack-Module.ps1 在打包前调用 build/Fetch-ModulePayload.ps1 取用它（支持 -PayloadPath 离线路径与 STARPIE_PAYLOAD_CACHE 缓存），任何哈希不符都会中止打包。

进程模块仍然需要一个 csproj（只复制 plugin.json、不编译代码）：整套构建/打包/目录链路以「工程 → bin/<配置>/<TFM> → .spkg」为骨架，没有工程就得给流程开第二条路。打包器会排除该工程自身产出的空壳程序集。

### 目录条目

- 每个 release 条目都带 runtime（assembly 或 process），缺省视为 assembly；
- 进程模块的 targetFramework 不是必填，目录条目里可以为 null；
- tests/catalog/Validate-CatalogV2.ps1 断言：进程模块条目必须是 runtime=process，程序集模块必须显式写 assembly。

### 工具链要求

目录生成与校验脚本使用 ConvertFrom-Json -Depth，**需要 PowerShell 7（pwsh）**。Windows PowerShell 5.1 会以「找不到参数 Depth」失败 —— CI 用 pwsh，本地手动跑这些脚本时同样要用 pwsh。
