[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$CatalogPath,
  [string[]]$ChangedModuleIds = @(),
  [Parameter(Mandatory)][string]$ReleaseTag,
  [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$ReleaseChannel,
  [Parameter(Mandatory)][uri]$RepositoryUrl,
  [Parameter(Mandatory)][string]$OutputPath,
  [string]$BannerUrl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'StarPie.Catalog.psm1') -Force

function Write-Utf8NoBom {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Escape-MarkdownTableCell {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    return $Text.Replace('|', '\|').Replace("`r", '').Replace("`n", ' ')
}

function Format-FileSize {
    param([long]$Bytes)
    if ($Bytes -ge 1MB) {
        return "$([math]::Round($Bytes / 1MB, 2)) MB"
    }
    if ($Bytes -ge 1KB) {
        return "$([math]::Round($Bytes / 1KB, 1)) KB"
    }
    return "$Bytes B"
}

# Resolve banner URL to an accessible HTTPS absolute URL
if ([string]::IsNullOrWhiteSpace($BannerUrl)) {
    $repoClean = $RepositoryUrl.AbsoluteUri.TrimEnd('/')
    if ($RepositoryUrl.Host -eq 'github.com') {
        $BannerUrl = "https://raw.githubusercontent.com$($RepositoryUrl.AbsolutePath.TrimEnd('/'))/main/docs/media/starpie-plugins-banner.svg"
    } else {
        $BannerUrl = "$repoClean/raw/main/docs/media/starpie-plugins-banner.svg"
    }
}

$catalog = Read-JsonFile -Path $CatalogPath
$changed = @{}
foreach ($id in $ChangedModuleIds) { $changed[$id] = $true }

$totalModules = @($catalog.modules).Count
$updatedModules = [System.Collections.Generic.List[object]]::new()
$allModules = [System.Collections.Generic.List[object]]::new()

foreach ($group in @($catalog.modules | Sort-Object id)) {
    $latest = Get-LatestCatalogVersion $group
    $item = [pscustomobject]@{
        Id = $group.id
        Latest = $latest
        IsUpdated = $changed.ContainsKey($group.id)
    }
    $allModules.Add($item)
    if ($item.IsUpdated) {
        $updatedModules.Add($item)
    }
}

$updatedCount = $updatedModules.Count
$referencedCount = $totalModules - $updatedCount
$isBeta = ($ReleaseChannel -eq 'beta')

$channelDisplay = if ($isBeta) { 'beta (公测预发布 / Pre-release)' } else { 'stable (稳定正式版 / Stable Release)' }
$channelBadge = if ($isBeta) { 'Beta Pre-release' } else { 'Stable Official' }
$channelDescription = if ($isBeta) {
    '本发布为 **Beta 预发布通道** 产物。已通过增量打包、本地契约校验与目录签名验证；StarPie 主程序可在「插件与扩展」中直接刷新获取。'
} else {
    '本发布为 **Stable 稳定正式版** 产物。已通过完整回归测试与目录签名校验，建议所有生产环境用户更新使用。'
}

$lines = New-Object System.Collections.Generic.List[string]

# 1. Top Brand Banner
$lines.Add("<p align=""center""><img src=""$BannerUrl"" alt=""StarPie 官方插件与模块发布"" width=""100%"" /></p>")
$lines.Add('')

# 2. Release Header & Overview
$catalogVersion = [string]$catalog.catalogVersion
$lines.Add("# StarPie 官方插件发布 ($ReleaseTag)")
$lines.Add('')
$lines.Add("> - **发布标签**：``$ReleaseTag``")
$lines.Add("> - **发布通道**：$channelDisplay")
$lines.Add("> - **目录版本**：``$catalogVersion``")
$lines.Add("> - **宿主兼容**：支持 **StarPie v2.0.0+** 正式版及对应更新版本")
$lines.Add("> - **模块统计**：共收录 **$totalModules** 个官方模块（本次更新：**$updatedCount** 个，历史沿用：**$referencedCount** 个）")
$lines.Add('')
$lines.Add($channelDescription)
$lines.Add('')

# 3. Updated Modules Section
if ($updatedCount -gt 0) {
    $lines.Add('### 🌟 本次新增 / 更新模块 (Updated Packages in this Release)')
    $lines.Add('')
    $lines.Add("本次发布包含 **$updatedCount** 个重新打包或新发布的官方插件模块。以下模块安装包已直接附加于本次 Release 资产列表中：")
    $lines.Add('')
    $lines.Add('| 模块名称 | 插件 ID | 版本 | 最低宿主 | SDK 契约 | 包大小 | 下载链接 (.spkg) |')
    $lines.Add('|:---|:---|:---:|:---:|:---:|---:|:---|')

    foreach ($item in $updatedModules) {
        $mod = $item.Latest
        $name = Escape-MarkdownTableCell $mod.name
        $size = Format-FileSize ([long]$mod.size)
        $lines.Add("| $name | ``$($item.Id)`` | $($mod.version) | ``>= $($mod.minHostVersion)`` | ``API $($mod.apiVersion)`` | $size | [$($mod.assetName)]($($mod.packageUrl)) |")
    }
    $lines.Add('')
} else {
    $lines.Add('### 🌟 本次更新摘要 (Update Summary)')
    $lines.Add('')
    $lines.Add('本次发布为模块目录同步与全量状态汇总，未包含新构建的独立模块安装包；所有启用模块均引用历史不可变 Release 资产。')
    $lines.Add('')
}

# 4. Installation & Usage Guide
$lines.Add('### 📦 安装与使用指引 (Installation Guide)')
$lines.Add('')
$lines.Add('1. **StarPie 客户端内一键刷新与安装（推荐）**：')
$lines.Add('   - 打开 StarPie 宿主程序，进入「**设置**」→「**插件与扩展**」页面；')
$lines.Add('   - 点击顶部「**刷新官方目录**」或「**检查更新**」，客户端将自动同步最新的签名目录；')
$lines.Add('   - 在模块列表中找到所需插件，点击「**安装**」，完成后勾选「**启用**」即可生效。')
$lines.Add('2. **手动离线安装 (.spkg)**：')
$lines.Add('   - 在下方模块列表中点击直接下载所需模块的 `.spkg` 文件；')
$lines.Add('   - 打开 StarPie「**插件与扩展**」页面，点击「**手动安装插件**」选择下载的包文件完成解包与登记；')
$lines.Add('   - 或直接将解压内容放置于 `%LOCALAPPDATA%\StarPie\plugin-data\` 对应目录。')
$lines.Add('3. **安全性与完整性防护**：')
$lines.Add('   - 每次发布的模块包均具有唯一的 SHA-256 哈希；')
$lines.Add('   - 客户端下载时将严格验证文件大小与 SHA-256，拒绝任何不匹配或损坏的包；')
$lines.Add('   - 模块在独立的 AssemblyLoadContext (ALC) 沙箱中加载，异常被自动隔离，不影响宿主本体。')
$lines.Add('')

# 5. Host Compatibility & Runtime Notes
$lines.Add('### ⚙️ 宿主兼容性与运行时环境 (Compatibility & Runtime)')
$lines.Add('')
$lines.Add('- **主程序兼容性**：支持 StarPie v2.0.0 正式版及以上版本。')
$lines.Add('- **目标运行时**：.NET 8.0 WPF 进程内架构，Windows 10 / 11 (x64)。')
$lines.Add('- **契约规范**：严格限定于 SDK 抽象层与 BCL，零外部隐式动态库污染。')
$lines.Add('')

# 6. Complete Official Modules Table
$lines.Add('### 📋 完整官方模块清单与下载 (Complete Module Registry & Direct Downloads)')
$lines.Add('')
$lines.Add('本发布代表当前官方启用的完整模块集。未变更模块继续保留其历史不可变 Release 资产与下载链接；客户端按需选择最高兼容版本。')
$lines.Add('')
$lines.Add('| 模块名称 | 插件 ID | 版本 | 发布状态 | 最低宿主 | SDK 契约 | 下载资产 (.spkg) |')
$lines.Add('|:---|:---|:---:|:---|:---:|:---:|:---|')

foreach ($item in $allModules) {
    $mod = $item.Latest
    $name = Escape-MarkdownTableCell $mod.name
    if ($item.IsUpdated) {
        $status = 'Updated in this release'
    } else {
        $encodedReleaseTag = [uri]::EscapeDataString([string]$mod.releaseTag)
        $releaseUrl = "$($RepositoryUrl.AbsoluteUri.TrimEnd('/'))/releases/tag/$encodedReleaseTag"
        $status = "Referenced from [``$($mod.releaseTag)``]($releaseUrl)"
    }
    $lines.Add("| $name | ``$($item.Id)`` | $($mod.version) | $status | $($mod.minHostVersion) | $($mod.apiVersion) | [$($mod.assetName)]($($mod.packageUrl)) |")
}
$lines.Add('')

Write-Utf8NoBom -Path $OutputPath -Content ($lines -join [Environment]::NewLine)
