[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$CatalogPath,
  [string[]]$ChangedModuleIds = @(),
  [Parameter(Mandatory)][string]$ReleaseTag,
  [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$ReleaseChannel,
  [Parameter(Mandatory)][uri]$RepositoryUrl,
  [Parameter(Mandatory)][string]$OutputPath,
  [string]$BannerUrl,
  [switch]$PromotedRelease
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
    return [System.Security.SecurityElement]::Escape($Text).Replace('|', '\|').Replace("`r", '').Replace("`n", ' ')
}

function Assert-HttpsUrl {
    param([string]$Value)
    $parsed = $null
    if (-not [uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsed) -or
        $parsed.Scheme -ne 'https' -or [string]::IsNullOrWhiteSpace($parsed.Host) -or $parsed.UserInfo) {
        throw 'Repository and banner URLs must use absolute HTTPS URLs without credentials.'
    }
}
Assert-HttpsUrl $RepositoryUrl.AbsoluteUri

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

# Resolve the banner to an absolute HTTPS URL; publishing checks accessibility.
if ([string]::IsNullOrWhiteSpace($BannerUrl)) {
    $repoClean = $RepositoryUrl.AbsoluteUri.TrimEnd('/')
    if ($RepositoryUrl.Host -eq 'github.com') {
        $BannerUrl = "https://raw.githubusercontent.com$($RepositoryUrl.AbsolutePath.TrimEnd('/'))/main/docs/media/starpie-plugins-banner.svg"
    } else {
        $BannerUrl = "$repoClean/raw/main/docs/media/starpie-plugins-banner.svg"
    }
}
Assert-HttpsUrl $BannerUrl
$bannerAttribute = [System.Security.SecurityElement]::Escape($BannerUrl)

$catalog = Read-JsonFile -Path $CatalogPath
if ($ReleaseChannel -cne $catalog.releaseChannel) {
    throw 'ReleaseChannel must match the original catalog releaseChannel.'
}
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

$channelDisplay = if ($PromotedRelease) { '正式发布（由预发布转正）' } elseif ($isBeta) { 'beta (公测预发布 / Pre-release)' } else { 'stable (稳定正式版 / Stable Release)' }
$channelDescription = if ($PromotedRelease) {
    "本 GitHub Release 已由预发布转为**正式发布**。原标签、插件包、目录及签名文件保持不变；目录中的 ``$ReleaseChannel`` 是创建时的原始签名字段，本次未改写，也未生成新的 stable 目录。"
} elseif ($isBeta) {
    '本发布使用 **Beta 预发布通道**。请依据下表中的最低宿主版本与 SDK 契约选择兼容模块。'
} else {
    '本发布使用 **Stable 稳定正式版** 通道。各模块的宿主与 SDK 要求见下表；通道标识不代替实际测试记录。'
}

$lines = New-Object System.Collections.Generic.List[string]

# 1. Top Brand Banner
$lines.Add("<p align=""center""><img src=""$bannerAttribute"" alt=""StarPie 官方插件与模块发布"" width=""100%"" /></p>")
$lines.Add('')

# 2. Release Header & Overview
$catalogVersion = [string]$catalog.catalogVersion
$lines.Add("# StarPie 官方插件发布 ($ReleaseTag)")
$lines.Add('')
$lines.Add("> - **发布标签**：``$ReleaseTag``")
$lines.Add("> - **发布通道**：$channelDisplay")
$lines.Add("> - **原始目录通道**：``$ReleaseChannel``")
$lines.Add("> - **目录版本**：``$catalogVersion``")
$lines.Add('> - **宿主兼容**：以各模块的最低宿主版本与 SDK 契约为准')
$lines.Add("> - **模块统计**：共收录 **$totalModules** 个官方模块（本次更新：**$updatedCount** 个，历史沿用：**$referencedCount** 个）")
$lines.Add('')
$lines.Add($channelDescription)
$lines.Add('')
if ($changed.ContainsKey('starpie.plugin.qrscanner')) {
    $lines.Add('### 本次看点：二维码扫描')
    $lines.Add('')
    $lines.Add('支持框选屏幕区域，或扫描鼠标附近的二维码。本地解码后先预览内容，再由你选择复制或打开网页链接；不会自动打开识别结果。')
    $lines.Add('')
}

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
$lines.Add('   - 在官方插件区域点击「**刷新目录**」，获取模块列表；')
$lines.Add('   - 在模块列表中找到所需插件，点击「**安装**」，完成后勾选「**启用**」即可生效。')
$lines.Add('2. **关于下方下载链接**：')
$lines.Add('   - `.spkg` 是官方分发容器，直链用于归档与检查。官方插件请通过客户端目录安装；社区 `.dll` 安装入口不接收 `.spkg`。')
$lines.Add('3. **完整性与加载边界**：')
$lines.Add('   - 每次发布的模块包均具有唯一的 SHA-256 哈希；')
$lines.Add('   - 客户端下载时将严格验证文件大小与 SHA-256，拒绝任何不匹配或损坏的包；')
$lines.Add('   - 模块使用独立 AssemblyLoadContext (ALC) 管理加载与卸载，仍运行在宿主进程内；ALC 不提供安全沙箱或进程级崩溃隔离。')
$lines.Add('')

# 5. Host Compatibility & Runtime Notes
$lines.Add('### ⚙️ 宿主兼容性与运行时环境 (Compatibility & Runtime)')
$lines.Add('')
$lines.Add('- **主程序兼容性**：客户端根据各模块的最低宿主版本、版本范围与 SDK 契约选择兼容版本。')
$lines.Add('- **目标运行时**：.NET 8.0 WPF 进程内架构，Windows 10 / 11 (x64)。')
$lines.Add('- **依赖规范**：插件引用公共 SDK；二维码扫描另含 ZXing.Net 解码依赖。分发包不携带主程序或私有 SDK 副本。')
$lines.Add('')

# 6. Complete Official Modules Table
$lines.Add('### 📋 完整官方模块清单与下载 (Complete Module Registry & Direct Downloads)')
$lines.Add('')
$lines.Add('本发布列出完整的官方可选模块集。未变更模块继续保留其历史不可变 Release 资产与下载链接；客户端按需选择最高兼容版本。')
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
