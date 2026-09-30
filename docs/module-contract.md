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
