# 识图翻译上架清单

把 `starpie.plugin.selinga` 从"仓库里能打包"推进到"用户在插件中心能装"，按顺序做完下面七步。
每一步都给了验收点 —— 没通过就不要进入下一步，否则问题会以"用户装不上"的形式暴露在后面。

## 前置条件（两个硬门槛）

| 条件 | 为什么是门槛 | 怎么确认 |
|---|---|---|
| **支持 SDK 1.14 的宿主已经正式发布** | 模块声明 `apiVersion: "1.14"`，旧宿主在扫描阶段直接拒绝（`PluginManifestReader` 会比较次版本） | 在目标宿主里打开「插件与扩展」→ 手动装一次本模块，看是否提示"当前最高支持 1.13" |
| **Selinga 已有带 `selinga.exe` 的正式 Release** | `payload.json` 指向的就是它；没有资产，打包时下载必失败 | `gh release view <tag> -R Sunse666/Selinga` 能看到该资产 |

签名**不**是用户可见性的前置条件（见最后一节），但它决定了发布工作流能不能以非 dry-run 跑。

---

## 第 1 步：发布 Selinga，拿到哈希

先构建并取哈希（这一步与是否有 `gh` 无关）：

```powershell
cd D:\IO\Rust_obj\Selinga-plugin
cargo build --release -p selinga-app
(Get-FileHash .\target\release\selinga.exe -Algorithm SHA256).Hash.ToLowerInvariant()
```

然后二选一发布。**资产名必须逐字是 `selinga.exe`**，否则 `payload.json` 的 `assetName` 对不上。

### 方式 A：网页（无需任何 CLI）

1. 打开 `https://github.com/Sunse666/Selinga/releases/new`
2. Tag 填 `v0.5.0`，Target 选 `main`（或你发布版所在的分支）
3. 标题写 `Selinga v0.5.0`
4. **Attach binaries** 拖入 `target\release\selinga.exe`
5. Publish release

### 方式 B：REST API（可脚本化，只需一个 PAT）

PAT 需要 `repo` 权限（fine-grained 则为 Contents: Read and write）。

```powershell
$repo  = 'Sunse666/Selinga'
$token = Read-Host -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
           [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token))
$headers = @{ Authorization = "Bearer $plain"; 'X-GitHub-Api-Version' = '2022-11-28' }

# 1) 建 release（tag 不存在时会按 target_commitish 自动创建）
$body = @{ tag_name = 'v0.5.0'; name = 'Selinga v0.5.0'; target_commitish = 'main'; draft = $false } | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" `
  -Headers $headers -ContentType 'application/json' -Body $body

# 2) 上传资产（注意走 uploads.github.com 且用二进制流）
$upload = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=selinga.exe"
Invoke-RestMethod -Method Post -Uri $upload -Headers $headers `
  -ContentType 'application/octet-stream' `
  -InFile '.\target\release\selinga.exe' | Out-Null
```

### 验收（同样不需要 gh）

```powershell
# 资产存在且大小一致
$r = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/v0.5.0"
$r.assets | Select-Object name, size, state

# 或者最直接的：用打包脚本自己证明能下载到、哈希对得上
./build/Fetch-ModulePayload.ps1 -ModuleId starpie.plugin.selinga `
  -DestinationDirectory src/StarPie.Plugin.Selinga/bin/Release/net8.0-windows
```

最后那条是**最强验收**：不带 `-PayloadPath` 就说明走的是真实下载路径 + 哈希校验，
它通过等于"第 1 步真的成功了"，而不只是"文件传上去了"。

## 第 2 步：更新 payload.json 并在本地打通

`src/StarPie.Plugin.Selinga/payload.json` 里改两处：`releaseTag` 与 `sha256`。若功能有变化，同步 `plugin.json` 的 `version`（它是目录判重的依据）。

```powershell
./build/Import-ModuleRegistryFromSource.ps1 -OutputPath artifacts/generated/module-registry.json
./build/Build-Modules.ps1 -ModuleIds starpie.plugin.selinga -Configuration Release -RunTests
./build/Pack-Module.ps1 -ModuleId starpie.plugin.selinga -Configuration Release -ReleaseTag local
./build/Verify-ModulePackage.ps1 -PackagePath artifacts/packages/StarPie.Plugin.Selinga-0.5.0.spkg -ModuleId starpie.plugin.selinga
```

> 需要 PowerShell 7（`pwsh`）。目录/校验脚本用了 `ConvertFrom-Json -Depth`，5.1 会报"找不到参数 Depth"。

**验收**：`Package verification passed (process)`，且包内 `files[]` 中 `bin/selinga.exe` 的哈希与第 1 步一致。

## 第 3 步：标定最低宿主版本

把 `plugin.json` 的 `minHostVersion` 从占位值改成**真实已发布**的宿主版本（例如 `2.0.0` 或 `2.1.0`）。这一步不能靠"本机能跑"判断 —— 用户装的是发行版。

**验收**：在**发行版**宿主里装一遍本地打出的 `.spkg`（「插件与扩展」→ 从文件安装），能正常启用并触发。

## 第 4 步：打开签名门禁

仓库变量：

```powershell
gh variable set SIGNING_ENABLED --body "true" -R Star-Pie/StarPie-Official-Plugins
```

没有 `gh` 时走网页：仓库 → **Settings → Secrets and variables → Actions → Variables → New repository variable**，
名字 `SIGNING_ENABLED`，值 `true`。

签名是 **keyless cosign**（GitHub OIDC），不需要自备密钥；工作流用 `sigstore/cosign-installer`，签完立即 `cosign verify-blob` 自证，身份固定在
`release-modules.yml@<ref>`。所以**发布必须从该 ref 触发**（通常是 `main`），换分支会签出另一个身份。

没有这个变量时，非 dry-run 会直接抛：
`Non-dry-run releases require repository variable SIGNING_ENABLED=true and a configured cosign identity.`

## 第 5 步：先 dry-run，再正式发

```powershell
# 5a 空跑：只构建和生成目录，不建 Release
gh workflow run release-modules.yml -R Star-Pie/StarPie-Official-Plugins \
  -f release_channel=beta -f catalog_base_channel=same -f module_ids=starpie.plugin.selinga -f dry_run=true

# 5b 下载空跑产物，确认目录条目正确
#   （从 Actions run 的 artifacts 里取 module-catalog.json）
```

在生成出来的 `module-catalog.json` 里检查该模块的 release 条目：

- `runtime` 为 `process`
- `sha256` / `size` 与第 2 步的 `.spkg` 一致
- `apiVersion` 为 `1.14`，`minHostVersion` 是第 3 步标定的值
- `assetName` 形如 `StarPie.Plugin.Selinga-0.5.0.spkg`

**验收**：dry-run 的 `Verify-Catalog.ps1` 步骤通过，且上面四项都对。

```powershell
# 5c 正式发布（会创建 modules-<日期>.<n> 标签并上传包、目录、签名、证明）
gh workflow run release-modules.yml -R Star-Pie/StarPie-Official-Plugins \
  -f release_channel=beta -f catalog_base_channel=same -f module_ids=starpie.plugin.selinga -f dry_run=false
```

没有 `gh` 时，这两次触发都可以在网页上做：
`https://github.com/Star-Pie/StarPie-Official-Plugins/actions/workflows/release-modules.yml`
→ `Run workflow` → 选分支（**必须是第 4 步里那个 ref**）→ 填同样四个输入。空跑产物在该次 run 页面的 Artifacts 区下载。

**验收**（网页即可）：`https://github.com/Star-Pie/StarPie-Official-Plugins/releases` 里最新那个
`modules-<tag>` 的 Assets 应包含 `module-catalog.json`、`module-catalog.sigstore.json`、`.spkg` 与 `.spkg.sigstore.json`。

## 第 6 步：客户端验证

1. 打开「插件与扩展」→ 刷新目录 → 应出现「识图翻译」
2. 安装 → 确认页应显示：**运行时：独立进程（以你的用户权限运行，不继承宿主提权）** + 启动命令
3. 装完检查 `%LOCALAPPDATA%\StarPie\plugin-data\starpie.plugin.selinga\` 下有 `plugin.json` 与 `bin\selinga.exe`
4. 绑到轮盘扇区 → 触发 → 出现框选 → 译文出现 → 再触发一次应重新框选
5. 停用插件 → 任务管理器里 `selinga.exe` 应消失

**验收**：五条全过。第 5 条不过时看 `%LOCALAPPDATA%\StarPie\logs\` 里的"已结束 N 个由宿主拉起的进程"。

## 第 7 步（可选）：升子模块指针

插件仓库发版后，主仓的 `plugin/StarPie-Official-Plugins` 可以跟到新 tag：

```powershell
cd D:\IO\dotnet\StarPie\plugin\StarPie-Official-Plugins
git fetch --tags && git checkout <new-tag>
cd D:\IO\dotnet\StarPie
git add plugin/StarPie-Official-Plugins && git commit -m "chore: bump official plugins submodule"
```

不升也能发：宿主引用的是自己那份 `plugin/sdk/`，子模块只是文档与快照。

---

## 排错对照

| 现象 | 原因 | 处置 |
|---|---|---|
| 用户端提示"当前最高支持 1.13" | 宿主没到 1.14 | 先发宿主，再发模块（或把模块 `apiVersion` 降回受支持的版本） |
| 打包时 "payload not found" | `payload.json` 的 `extractTo` 与 `command[0]` 不一致 | 两处必须指向同一路径（`bin/selinga.exe`） |
| 下载 404 | `releaseTag`/`assetName` 与实际资产不符 | `gh release view` 核对 |
| "SHA-256 不一致" | 资产被替换，或哈希抄错 | 重新取哈希；**不要**为了通过而改哈希 |
| 发布 job 直接失败 | 没设 `SIGNING_ENABLED` | 第 4 步 |
| 刷新目录看不到新模块 | 客户端只读最近 8 个 release（`per_page=8`） | 发布节奏密集时，把该模块放进最新一次 release |

## 关于签名的一个事实

`docs/security-model.md` 描述的是**规划中的**信任链（原文即 "The planned trust chain is"）。当前客户端并**不校验**目录签名 —— 插件子系统里没有 cosign/sigstore 代码，安装确认页显示的签名是程序集自身的 Authenticode 签名，与目录签名无关。

所以现状是：**签名是发布工作流的门禁（防止未签名目录被发出去），不是客户端的拦截。** 这条差异不要误解成"已经端到端签名保护"；真正的强制边界目前只有操作系统权限。
