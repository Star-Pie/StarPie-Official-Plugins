[CmdletBinding()]
param([string]$OutputRoot)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Import-Module (Join-Path $root 'build\StarPie.Modules.psm1') -Force
Import-Module (Join-Path $root 'build\StarPie.Catalog.psm1') -Force

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $root ('artifacts\release-notes-tests\' + [guid]::NewGuid().ToString('N'))
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (-not $OutputRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test output must stay inside repository artifacts.'
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$script:checks = 0
function Assert-Condition([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
    $script:checks++
}
function Assert-Fails([scriptblock]$Call, [string]$ExpectedMessage) {
    $failed = $false
    try { & $Call | Out-Null } catch { $failed = $_.Exception.Message.Contains($ExpectedMessage) }
    Assert-Condition $failed 'Invalid release-note input was accepted.'
}

# 1. Prepare Mock Catalog with special characters and known versions
$catalogObj = [ordered]@{
    schemaVersion = 2
    catalogVersion = '2026.10.18.120000'
    releaseTag = 'modules-test-tag'
    releaseChannel = 'beta'
    sdkApiVersion = '1.4'
    minimumHostVersion = '1.8.0-beta.1'
    modules = @(
        [ordered]@{
            id = 'starpie.builtin.command'
            versions = @(
                [ordered]@{
                    version = '1.1.1'
                    name = '运行命令'
                    releaseTag = 'modules-2026.10.13.1'
                    assetName = 'StarPie.Plugin.Command-1.1.1.spkg'
                    packageUrl = 'https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.13.1/StarPie.Plugin.Command-1.1.1.spkg'
                    sha256 = ('a' * 64)
                    size = 12345
                    apiVersion = '1.8'
                    targetFramework = 'net8.0-windows'
                    minHostVersion = '1.8.0-beta.5'
                    typeClaims = @('Command')
                    capabilities = @()
                }
            )
        },
        [ordered]@{
            id = 'starpie.plugin.qrscanner'
            versions = @(
                [ordered]@{
                    version = '0.1.0'
                    name = '二维码扫描'
                    releaseTag = 'modules-test-tag'
                    assetName = 'StarPie.Plugin.QrScanner-0.1.0.spkg'
                    packageUrl = 'https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-test-tag/StarPie.Plugin.QrScanner-0.1.0.spkg'
                    sha256 = ('b' * 64)
                    size = 336965
                    apiVersion = '1.12'
                    targetFramework = 'net8.0-windows'
                    minHostVersion = '1.8.0-beta.7'
                    typeClaims = @('Plugin')
                    capabilities = @('ScreenCapture')
                }
            )
        },
        [ordered]@{
            id = 'starpie.builtin.special'
            versions = @(
                [ordered]@{
                    version = '1.0.0'
                    name = '特殊字符测试 | Pipeline & Slashes <b>'
                    releaseTag = 'modules-2026.10.01.1'
                    assetName = 'StarPie.Plugin.Special-1.0.0.spkg'
                    packageUrl = 'https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026.10.01.1/StarPie.Plugin.Special-1.0.0.spkg'
                    sha256 = ('c' * 64)
                    size = 50000
                    apiVersion = '1.4'
                    targetFramework = 'net8.0-windows'
                    minHostVersion = '1.8.0-beta.1'
                    typeClaims = @('Special')
                    capabilities = @()
                }
            )
        }
    )
}

$mockCatalogPath = Join-Path $OutputRoot 'mock-catalog.json'
Write-JsonFile -Value $catalogObj -Path $mockCatalogPath

# ============================================================
# Test Case A: Beta Release with 1 Changed Module
# ============================================================
$betaNotesPath = Join-Path $OutputRoot 'beta-notes.md'
$scriptPath = Join-Path $root 'build\New-ReleaseNotes.ps1'

& $scriptPath `
    -CatalogPath $mockCatalogPath `
    -ChangedModuleIds @('starpie.plugin.qrscanner') `
    -ReleaseTag 'modules-test-tag' `
    -ReleaseChannel 'beta' `
    -RepositoryUrl 'https://github.com/Star-Pie/StarPie-Official-Plugins' `
    -OutputPath $betaNotesPath

Assert-Condition (Test-Path -LiteralPath $betaNotesPath) 'Beta release notes file was not created.'
$betaRaw = [System.IO.File]::ReadAllText($betaNotesPath, [System.Text.Encoding]::UTF8)
$betaLines = [System.IO.File]::ReadAllLines($betaNotesPath, [System.Text.Encoding]::UTF8)

# Check 1: UTF-8 No BOM
$bytes = [System.IO.File]::ReadAllBytes($betaNotesPath)
Assert-Condition ($bytes.Length -ge 3) 'Beta file is too small.'
$hasBom = ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
Assert-Condition (-not $hasBom) 'Beta release notes must not contain UTF-8 BOM.'

# Check 2: Banner URL is HTTPS absolute
Assert-Condition ($betaRaw -match '<img src="https://[^"]+/docs/media/starpie-plugins-banner\.svg"') 'Banner URL must be an absolute HTTPS URL.'

# Check 3: Beta channel fidelity (never claiming stable)
Assert-Condition ($betaRaw -match '\*\*发布通道\*\*：beta') 'Beta release notes must state beta channel.'
Assert-Condition ($betaRaw -match 'Beta 预发布通道') 'Beta release notes must state Beta pre-release description.'
Assert-Condition ($betaRaw -notmatch '\*\*发布通道\*\*：stable') 'Beta release notes must not claim stable channel.'

# Check 4: Updated vs historical reference counts
Assert-Condition ($betaRaw -match '共收录 \*\*3\*\* 个官方模块（本次更新：\*\*1\*\* 个，历史沿用：\*\*2\*\* 个）') 'Module statistics line mismatch.'

# Check 5: Updated section has exactly 1 entry for qrscanner
$updatedSectionMatches = @($betaLines | Where-Object { $_ -match '二维码扫描' -and $_ -match 'starpie\.plugin\.qrscanner' -and $_ -match '0\.1\.0' })
Assert-Condition ($updatedSectionMatches.Count -ge 2) 'QrScanner must appear in both updated table and full registry table.'

# Check 6: Direct download links exist and point to HTTPS releases
Assert-Condition ($betaRaw -match '\[StarPie\.Plugin\.QrScanner-0\.1\.0\.spkg\]\(https://github\.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-test-tag/StarPie\.Plugin\.QrScanner-0\.1\.0\.spkg\)') 'QrScanner package download link missing or wrong.'
Assert-Condition ($betaRaw -match '\[StarPie\.Plugin\.Command-1\.1\.1\.spkg\]\(https://github\.com/Star-Pie/StarPie-Official-Plugins/releases/download/modules-2026\.10\.13\.1/StarPie\.Plugin\.Command-1\.1\.1\.spkg\)') 'Command package historical download link missing or wrong.'

# Check 7: Special character escaping (pipes in table)
Assert-Condition ($betaRaw.Contains('特殊字符测试 \| Pipeline &amp; Slashes &lt;b&gt;')) 'Module names must preserve literal pipes and HTML characters.'

# Check 8: Compatibility data fidelity
Assert-Condition ($betaRaw -match '1\.8\.0-beta\.7') 'MinHostVersion must match catalog.'
Assert-Condition ($betaRaw -match 'API 1\.12') 'ApiVersion must match catalog.'
Assert-Condition ($betaRaw -notmatch 'v2\.0\.0\+') 'Must not promise compatibility with all future hosts.'
Assert-Condition ($betaRaw.Contains('社区 `.dll` 安装入口不接收 `.spkg`')) 'Installation guidance must distinguish official packages from community DLLs.'
Assert-Condition ($betaRaw -notmatch '手动离线安装|解压内容放置') 'Unsupported manual installation route returned.'
Assert-Condition ($betaRaw.Contains('ALC 不提供安全沙箱或进程级崩溃隔离')) 'In-process loading boundary must be stated accurately.'
Assert-Condition ($betaRaw -notmatch '已通过完整回归|已通过增量打包') 'Generating notes must not invent verification results.'

# ============================================================
# Test Case B: Stable Release with No Changed Modules (Full Reference)
# ============================================================
$stableNotesPath = Join-Path $OutputRoot 'stable-notes.md'
$stableCatalogPath = Join-Path $OutputRoot 'stable-catalog.json'
$catalogObj.releaseChannel = 'stable'
Write-JsonFile -Value $catalogObj -Path $stableCatalogPath

& $scriptPath `
    -CatalogPath $stableCatalogPath `
    -ChangedModuleIds @() `
    -ReleaseTag 'modules-stable-v2' `
    -ReleaseChannel 'stable' `
    -RepositoryUrl 'https://github.com/Star-Pie/StarPie-Official-Plugins' `
    -OutputPath $stableNotesPath

$stableRaw = [System.IO.File]::ReadAllText($stableNotesPath, [System.Text.Encoding]::UTF8)

# Check 9: Stable channel fidelity
Assert-Condition ($stableRaw -match '\*\*发布通道\*\*：stable') 'Stable release notes must state stable channel.'
Assert-Condition ($stableRaw -match 'Stable 稳定正式版') 'Stable release notes must state stable description.'

# Check 10: Zero updated modules handled cleanly
Assert-Condition ($stableRaw -match '共收录 \*\*3\*\* 个官方模块（本次更新：\*\*0\*\* 个，历史沿用：\*\*3\*\* 个）') 'Stable statistics line mismatch.'
Assert-Condition ($stableRaw -match '本次发布为模块目录同步与全量状态汇总，未包含新构建的独立模块安装包') 'Zero-change update summary text missing.'

# Promotion changes publication metadata, not the original signed catalog.
$catalogHash = (Get-FileHash $mockCatalogPath -Algorithm SHA256).Hash
$inputArgs = @{
    CatalogPath = $mockCatalogPath
    ChangedModuleIds = @('starpie.plugin.qrscanner')
    ReleaseTag = 'modules-test-tag'
    ReleaseChannel = 'beta'
    RepositoryUrl = 'https://github.com/Star-Pie/StarPie-Official-Plugins'
    OutputPath = (Join-Path $OutputRoot 'promoted-notes.md')
}
& $scriptPath @inputArgs -PromotedRelease
$promotedRaw = [IO.File]::ReadAllText($inputArgs.OutputPath)
Assert-Condition ($promotedRaw.Contains('**发布通道**：正式发布（由预发布转正）')) 'Promoted release must state official GitHub status.'
Assert-Condition ($promotedRaw.Contains('**原始目录通道**：`beta`')) 'Promotion must retain original beta catalog metadata.'
Assert-Condition ($promotedRaw.Contains('未生成新的 stable 目录')) 'Promotion must not claim a newly signed stable catalog.'
Assert-Condition ((Get-FileHash $mockCatalogPath -Algorithm SHA256).Hash -eq $catalogHash) 'Generating promoted notes changed the catalog.'

$mismatchedArgs = $inputArgs.Clone()
$mismatchedArgs.ReleaseChannel = 'stable'
Assert-Fails { & $scriptPath @mismatchedArgs } 'ReleaseChannel must match'
foreach ($invalidBanner in @('http://example.com/banner.svg', 'javascript:alert(1)', '/banner.svg', 'https://user:secret@example.com/banner.svg')) {
    Assert-Fails { & $scriptPath @inputArgs -BannerUrl $invalidBanner } 'absolute HTTPS URLs without credentials'
}
foreach ($invalidRepository in @('http://github.com/Star-Pie/StarPie-Official-Plugins', 'https://user:secret@github.com/Star-Pie/StarPie-Official-Plugins')) {
    $badArgs = $inputArgs.Clone()
    $badArgs.RepositoryUrl = $invalidRepository
    Assert-Fails { & $scriptPath @badArgs } 'absolute HTTPS URLs without credentials'
}
& $scriptPath @inputArgs -BannerUrl 'https://example.com/banner.svg?label="quoted"&x=1'
$escapedRaw = [IO.File]::ReadAllText($inputArgs.OutputPath)
Assert-Condition ($escapedRaw.Contains('label=&quot;quoted&quot;&amp;x=1')) 'Banner URL must be escaped in HTML attributes.'

# ============================================================
# Test Case C: Real Repository Catalog Validation
# ============================================================
$realCatalogBootstrap = Join-Path $root 'artifacts\temp-catalog\module-catalog.json'
if (Test-Path -LiteralPath $realCatalogBootstrap) {
    $realNotesPath = Join-Path $OutputRoot 'real-candidate-notes.md'
    & $scriptPath `
        -CatalogPath $realCatalogBootstrap `
        -ChangedModuleIds @('starpie.plugin.qrscanner') `
        -ReleaseTag 'modules-2026.10.15.1' `
        -ReleaseChannel 'beta' `
        -RepositoryUrl 'https://github.com/Star-Pie/StarPie-Official-Plugins' `
        -OutputPath $realNotesPath

    $realRaw = [System.IO.File]::ReadAllText($realNotesPath, [System.Text.Encoding]::UTF8)
    $realLines = [System.IO.File]::ReadAllLines($realNotesPath, [System.Text.Encoding]::UTF8)

    # Must contain all 19 modules in the full table
    $fullSection = ($realRaw -split '### 📋 完整官方模块清单与下载')[1]
    $tableRows = @($fullSection -split "`r?`n" | Where-Object { $_ -match '^\|\s*[^|]+\s*\|\s*`starpie\.' })
    Assert-Condition ($tableRows.Count -eq 19) "Real catalog notes must contain exactly 19 module rows in the full table, found $($tableRows.Count)."

    # Verify QrScanner is marked updated
    $qrRow = @($tableRows | Where-Object { $_ -match 'starpie\.plugin\.qrscanner' })[0]
    Assert-Condition ($qrRow -match 'Updated in this release') 'QrScanner row must be marked Updated in this release.'

    # Verify Command is referenced from 2026.10.13.1
    $cmdRow = @($tableRows | Where-Object { $_ -match 'starpie\.builtin\.command' })[0]
    Assert-Condition ($cmdRow -match 'modules-2026\.10\.13\.1') 'Command row must reference modules-2026.10.13.1.'
}

Write-Host "PASS: $script:checks release notes behavioral checks passed. Output: $OutputRoot"
