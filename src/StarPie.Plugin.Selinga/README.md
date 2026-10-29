# Selinga 识图翻译（进程插件）

这个模块没有 C# 代码。它是 StarPie 的第一个 `runtime: "process"` 官方模块：
宿主不加载任何程序集，只按 `plugin.json` 里的 `command` 拉起一个独立进程。

实际功能由 [Selinga](https://github.com/Sunse666/Selinga) 提供：

- 框选屏幕区域 → 本地 OCR → 翻译链（TranSmart → Google → MyMemory）+ 可选视觉模型
- 译文按行贴回原位，重新触发即重新框选
- 第二个动作「识图翻译设置」打开 Selinga 的配置文件

## 为什么目录里没有 exe

仓库只存源码，二进制一律走 Release —— 这条对 C# 插件（DLL 由 CI 编译）和进程插件（exe 由 Selinga 发布）是一样的。
所以这里用 `payload.json` 描述"去哪儿取、取哪个、取的必须是哪一枚"：

```json
{
  "repository": "Sunse666/Selinga",
  "releaseTag": "v0.5.0",
  "assetName": "selinga.exe",
  "sha256": "0dbd4166…",
  "extractTo": "bin/selinga.exe"
}
```

`build/Fetch-ModulePayload.ps1` 在打包前把它取到输出目录；`Pack-Module.ps1` 会自动调用它，
所以本地单独打包也是自足的。

## 升级 payload

1. 在 Selinga 仓库打 tag 并发布 Release，资产为单文件 `selinga.exe`
2. 算哈希：`Get-FileHash .\selinga.exe -Algorithm SHA256`（小写十六进制）
3. 更新 `payload.json` 的 `releaseTag` 与 `sha256`，必要时同步 `plugin.json` 的 `version`
4. 本地验证（不联网）：

```powershell
./build/Build-Modules.ps1 -ModuleIds starpie.plugin.selinga -Configuration Release
./build/Fetch-ModulePayload.ps1 -ModuleId starpie.plugin.selinga `
  -DestinationDirectory src/StarPie.Plugin.Selinga/bin/Release/net8.0-windows `
  -PayloadPath C:\path\to\selinga.exe
./build/Pack-Module.ps1 -ModuleId starpie.plugin.selinga -Configuration Release -ReleaseTag local
./build/Verify-ModulePackage.ps1 -PackagePath artifacts/packages/StarPie.Plugin.Selinga-0.5.0.spkg -ModuleId starpie.plugin.selinga
```

缓存目录默认在 `%TEMP%\starpie-payload-cache`，可用 `STARPIE_PAYLOAD_CACHE` 覆盖（CI 里挂缓存可省掉重复下载）。

## 两条容易被忽略的约束

- **能力声明对进程插件是"告知"，不是"门禁"**：`ScreenCapture` / `Ui` 会显示在安装确认页上，
  但宿主拦不住一个独立进程去调任何 Win32 API。真正的边界只有操作系统权限。
- **客户端版本门槛**：本模块声明 `apiVersion: "1.14"`，旧宿主会在扫描阶段拒绝并提示升级。
  发布前请确认支持 1.14 的宿主已经正式发布，且 `minHostVersion` 写的是那个真实版本。
