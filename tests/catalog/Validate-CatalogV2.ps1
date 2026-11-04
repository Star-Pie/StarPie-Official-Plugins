[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Import-Module (Join-Path $root 'build\StarPie.Modules.psm1') -Force
Import-Module (Join-Path $root 'build\StarPie.Catalog.psm1') -Force
if (-not $OutputRoot) { $OutputRoot = Join-Path $root ('artifacts\catalog-v2-tests\' + [guid]::NewGuid().ToString('N')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (-not $OutputRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Test output must stay inside repository artifacts.' }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$script:checks = 0
function Assert-Condition([bool]$Value,[string]$Message) { if (-not $Value) { throw $Message }; $script:checks++ }
function Assert-Fails([scriptblock]$Call) { $failed=$false; try { & $Call | Out-Null } catch { $failed=$true }; Assert-Condition $failed 'Expected failure did not occur.' }
Assert-Condition ((Compare-CatalogVersions '1.8.0-beta.10' '1.8.0-beta.5') -gt 0) 'beta numbers compared lexically'
Assert-Condition ((Compare-CatalogVersions '1.0.0-alpha.1' '1.0.0-alpha.beta') -lt 0) 'numeric identifiers must precede strings'
Assert-Condition ((Compare-CatalogVersions '1.0.0+one' '1.0.0+two') -eq 0) 'build metadata changes priority'
Assert-Fails { ConvertTo-CatalogVersion '1.0.0-beta.01' }
$previousRegistryOverride=$env:STARPIE_MODULE_REGISTRY
$nativeExitVariable=Get-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$previousNativeExit=if($null -eq $nativeExitVariable){0}else{$nativeExitVariable.Value}
try {
$env:STARPIE_MODULE_REGISTRY=Join-Path $OutputRoot 'module-registry.json'
& (Join-Path $root 'build\Import-ModuleRegistryFromSource.ps1') -OutputPath $env:STARPIE_MODULE_REGISTRY | Out-Null
$registry = Get-ModuleRegistry
$modules = @(Get-EnabledModules $registry)
$records = @()
Add-Type -AssemblyName System.IO.Compression.FileSystem
# ZipArchiveMode 住在 System.IO.Compression 里，Windows PowerShell 5.1 下不会随 FileSystem 程序集一起载入。
# CI 跑 pwsh 7 时这条是无害的重复加载。
Add-Type -AssemblyName System.IO.Compression
foreach ($module in $modules) {
    $source = Join-Path $root (Split-Path -Parent $module.project)
    $manifestPath = Join-Path $source 'plugin.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    # 进程插件没有程序集，包名退到打包工程名（与 Pack-Module.ps1 的规则一致）。
    $assetBase = if ($module.assembly) { [IO.Path]::GetFileNameWithoutExtension($module.assembly) } else { [IO.Path]::GetFileNameWithoutExtension($module.project) }
    $assetName = $assetBase + '-' + $module.version + '.spkg'
    $packagePath = Join-Path $OutputRoot $assetName
    $archive = [IO.Compression.ZipFile]::Open($packagePath,[IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('plugin.json')
        $writer = [IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
        try { $writer.Write($manifest) } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    $records += [pscustomobject]@{ id=$module.id;version=$module.version;packagePath=$packagePath;assetName=$assetName;sha256=(Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant();size=(Get-Item -LiteralPath $packagePath).Length;apiVersion=$module.apiVersion;minHostVersion=$module.minHostVersion;typeClaims=@($module.typeClaims);capabilities=@($module.capabilities);signature=$null }
}
$packagesPath = Join-Path $OutputRoot 'packages.json'; Write-JsonFile -Value @($records) -Path $packagesPath
$catalogPath = Join-Path $OutputRoot 'first.json'
$args = @{ReleaseTag='test-catalog-v2';ReleaseChannel='beta';PackagesJson=$packagesPath;PreviousCatalogPath='';Repository='Star-Pie/StarPie-Official-Plugins';OutputPath=$catalogPath;RequirePackageManifest=$true}
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @args | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $catalogPath
$first = Read-JsonFile $catalogPath
Assert-Condition ($first.schemaVersion -eq 2 -and $first.modules.Count -eq $modules.Count) 'catalog v2 module count incorrect'
foreach ($group in $first.modules) { Assert-Condition ($null -eq $group.PSObject.Properties['version'] -and $group.versions.Count -eq 1) 'legacy top-level default exists' }
# 运行时形态必须显式落进目录：客户端与审查工具要能分清"没写"和"是程序集"。
# 进程插件还要保证没有退化成"靠目标框架猜"——它的目录条目里 runtime 只能是 process。
$processModules = @($modules | Where-Object runtime -EQ 'process')
Assert-Condition ($processModules.Count -ge 1) '缺少进程插件夹具：runtime 断言无从验证'
foreach ($processModule in $processModules) {
    $processEntry = @($first.modules | Where-Object id -CEQ $processModule.id)[0].versions[0]
    Assert-Condition ($processEntry.runtime -eq 'process') "进程插件 $($processModule.id) 的目录条目丢失 runtime=process"
}
$assemblyEntry = @($first.modules | Where-Object id -CEQ 'starpie.builtin.launch')[0].versions[0]
Assert-Condition ($assemblyEntry.runtime -eq 'assembly') '程序集插件的目录条目未显式标注 runtime=assembly'
$launch = @($first.modules | Where-Object id -EQ 'starpie.builtin.launch')[0]
$currentLaunchVersion = [string](@($modules | Where-Object id -EQ $launch.id)[0].version)
Assert-Condition ((Compare-CatalogVersions $currentLaunchVersion '1.0.1') -gt 0) 'fixture historical version must precede the current module version'
$old = $launch.versions[0] | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$old.version='1.0.1';$old.apiVersion='1.4';$old.minHostVersion='1.8.0-beta.1';$old.releaseTag='old-test';$old.assetName='StarPie.Plugin.Launch-1.0.1.spkg';$old.packageUrl='https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/old-test/StarPie.Plugin.Launch-1.0.1.spkg';$old.sha256=('b'*64);$old.size=1024
$launch.versions += $old
# Feed unsorted history to the publisher; keep $first canonical for the later v1 fixture.
$previousCatalog = $first | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$previousLaunch = @($previousCatalog.modules | Where-Object id -EQ $launch.id)[0]
$previousLaunch.versions = @($previousLaunch.versions[1], $previousLaunch.versions[0])
$previousPath=Join-Path $OutputRoot 'previous.json'; Write-JsonFile $previousCatalog $previousPath
$emptyPath=Join-Path $OutputRoot 'empty-packages.json';[IO.File]::WriteAllText($emptyPath,'[]')
$args.PackagesJson=$emptyPath;$args.PreviousCatalogPath=$previousPath;$args.OutputPath=Join-Path $OutputRoot 'second.json'
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @args | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $args.OutputPath
$second=Read-JsonFile $args.OutputPath
$history=@($second.modules | Where-Object id -EQ $launch.id)[0].versions
Assert-Condition ($history.Count -eq 2) 'historical version lost'
Assert-Condition ((@($history | Where-Object version -EQ '1.0.1')[0]).minHostVersion -eq '1.8.0-beta.1') 'old requirements overwritten by current source'
Assert-Condition ((@($history | Where-Object version -EQ '1.0.1')[0]).sha256 -eq ('b'*64)) 'historical hash changed'
Assert-Condition (($history.version -join '|') -ceq (@($currentLaunchVersion, '1.0.1') -join '|')) 'history not sorted descending or versions changed'
$changed=& (Join-Path $root 'build\Get-VersionChanges.ps1') -PreviousCatalogPath $args.OutputPath
Assert-Condition (-not ($changed | ConvertFrom-Json).hasModules -and ($changed | ConvertFrom-Json).changed.Count -eq 0) 'v2 version change detection fails'
& (Join-Path $root 'build\New-ReleaseNotes.ps1') -CatalogPath $args.OutputPath -ChangedModuleIds @($launch.id) -ReleaseTag test-catalog-v2 -ReleaseChannel beta -RepositoryUrl 'https://github.com/Star-Pie/StarPie-Official-Plugins' -OutputPath (Join-Path $OutputRoot 'notes.md')
$expectedLaunchRow = "| $($launch.versions[0].name) | ``$($launch.id)`` | $currentLaunchVersion | Updated in this release | $($launch.versions[0].minHostVersion) | $($launch.versions[0].apiVersion) | [$($launch.versions[0].assetName)](https://github.com/Star-Pie/StarPie-Official-Plugins/releases/download/test-catalog-v2/$($launch.versions[0].assetName)) |"
Assert-Condition ((Get-Content -LiteralPath (Join-Path $OutputRoot 'notes.md')) -ccontains $expectedLaunchRow) 'release notes omit the latest launch module version'
# Stable promotion keeps every immutable beta version, including old compatibility records.
$args.PreviousCatalogPath=$args.OutputPath;$args.OutputPath=Join-Path $OutputRoot 'promoted.json';$args.ReleaseChannel='stable'
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @args | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $args.OutputPath
$promoted=Read-JsonFile $args.OutputPath
Assert-Condition ($promoted.releaseChannel -eq 'stable') 'stable promotion kept the beta output channel'
foreach($group in $second.modules){
    $retained=@($promoted.modules | Where-Object id -CEQ $group.id)
    Assert-Condition ($retained.Count -eq 1 -and ($group.versions | ConvertTo-Json -Depth 100 -Compress) -ceq ($retained[0].versions | ConvertTo-Json -Depth 100 -Compress)) "stable promotion changed history for $($group.id)"
}
$args.ReleaseChannel='beta'
# A selected release must retain old packages without publishing other pending source modules.
$partialPrevious=$second | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$selectedId='starpie.plugin.temporarykeyboard'
$unreleasedIds=@('starpie.plugin.qrscanner','starpie.plugin.selinga')
$partialPrevious.modules=@($partialPrevious.modules | Where-Object { $_.id -ne $selectedId -and $_.id -notin $unreleasedIds })
$partialLaunch=@($partialPrevious.modules | Where-Object id -EQ $launch.id)[0]
$partialLaunch.versions=@($partialLaunch.versions | Where-Object version -EQ '1.0.1')
$partialPreviousPath=Join-Path $OutputRoot 'partial-previous.json';Write-JsonFile $partialPrevious $partialPreviousPath
$partialPackagesPath=Join-Path $OutputRoot 'partial-packages.json';Write-JsonFile @($records | Where-Object id -EQ $selectedId) $partialPackagesPath
$partialArgs=@{ReleaseTag='partial-test';ReleaseChannel='beta';PackagesJson=$partialPackagesPath;PreviousCatalogPath=$partialPreviousPath;Repository='Star-Pie/StarPie-Official-Plugins';OutputPath=(Join-Path $OutputRoot 'partial.json');RequirePackageManifest=$true}
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.ReleasedModuleIds=@($selectedId)
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs | Out-Null
$verifyPartial=@{CatalogPath=$partialArgs.OutputPath;ReleasedModuleIds=@($selectedId);PreviousCatalogPath=$partialPreviousPath}
& (Join-Path $root 'build\Verify-Catalog.ps1') @verifyPartial
$partial=Read-JsonFile $partialArgs.OutputPath
Assert-Condition ($partial.modules.Count -eq $modules.Count-2) 'partial release included unrequested new modules or lost retained modules'
Assert-Condition ((@($partial.modules | Where-Object id -EQ $selectedId)[0]).versions[0].releaseTag -eq 'partial-test') 'selected package was not included'
foreach($group in $partialPrevious.modules){
    $retained=@($partial.modules | Where-Object id -CEQ $group.id)[0]
    Assert-Condition (($group.versions | ConvertTo-Json -Depth 100 -Compress) -ceq ($retained.versions | ConvertTo-Json -Depth 100 -Compress)) "partial release changed unselected history: $($group.id)"
}
Assert-Fails { & (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $partialArgs.OutputPath }
Assert-Fails { & (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $partialArgs.OutputPath -ReleasedModuleIds $selectedId }
$badPartial=$partial | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$badPartial.modules=@($badPartial.modules | Where-Object id -NE $launch.id)
$badPath=Join-Path $OutputRoot 'partial-missing-history.json';Write-JsonFile $badPartial $badPath
Assert-Fails { & (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $badPath -ReleasedModuleIds $selectedId -PreviousCatalogPath $partialPreviousPath }
$badPartial=$partial | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
(@($badPartial.modules | Where-Object id -EQ $launch.id)[0]).versions[0].minHostVersion='1.0.0'
$badPath=Join-Path $OutputRoot 'partial-altered-history.json';Write-JsonFile $badPartial $badPath
Assert-Fails { & (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $badPath -ReleasedModuleIds $selectedId -PreviousCatalogPath $partialPreviousPath }
$partialArgs.PackagesJson=$emptyPath
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.PackagesJson=$packagesPath
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.PackagesJson=$partialPackagesPath;$partialArgs.ReleasedModuleIds=@('starpie.unknown')
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.ReleasedModuleIds=@()
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.ReleasedModuleIds=@($selectedId,$selectedId)
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.ReleasedModuleIds=@($selectedId)
$duplicatePackagesPath=Join-Path $OutputRoot 'partial-duplicate-packages.json'
$selectedRecord=@($records | Where-Object id -CEQ $selectedId)[0]
Write-JsonFile @($selectedRecord,$selectedRecord) $duplicatePackagesPath
$partialArgs.PackagesJson=$duplicatePackagesPath
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.PackagesJson=$partialPackagesPath
$disabledRegistry=$registry | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
(@($disabledRegistry.modules | Where-Object id -CEQ $selectedId)[0]).enabled=$false
$disabledRegistryPath=Join-Path $OutputRoot 'disabled-registry.json';Write-JsonFile $disabledRegistry $disabledRegistryPath
$savedRegistry=$env:STARPIE_MODULE_REGISTRY
try {
    $env:STARPIE_MODULE_REGISTRY=$disabledRegistryPath
    Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
} finally { $env:STARPIE_MODULE_REGISTRY=$savedRegistry }
$extraVersion=$partial | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
(@($extraVersion.modules | Where-Object id -CEQ $launch.id)[0]).versions += $launch.versions[0]
$extraPath=Join-Path $OutputRoot 'partial-unrequested-version.json';Write-JsonFile $extraVersion $extraPath
Assert-Fails { & (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $extraPath -ReleasedModuleIds $selectedId -PreviousCatalogPath $partialPreviousPath }
# Historical modules survive source removal; only a separately approved withdrawal may drop them.
$retired=$partialPrevious.modules[0] | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$retired.id='starpie.retired.fixture'
$partialPrevious.modules += $retired;Write-JsonFile $partialPrevious $partialPreviousPath
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') @verifyPartial
$retainedRetired=@((Read-JsonFile $partialArgs.OutputPath).modules | Where-Object id -CEQ $retired.id)
Assert-Condition ($retainedRetired.Count -eq 1 -and ($retainedRetired[0].versions | ConvertTo-Json -Depth 100 -Compress) -ceq ($retired.versions | ConvertTo-Json -Depth 100 -Compress)) 'source-removed history lost or changed'
$partialArgs.PreviousCatalogPath=Join-Path $OutputRoot 'missing-history.json'
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs }
$partialArgs.PreviousCatalogPath=''
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @partialArgs | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $partialArgs.OutputPath -ReleasedModuleIds $selectedId -PreviousCatalogPath ''
Assert-Condition (@((Read-JsonFile $partialArgs.OutputPath).modules).Count -eq 1) 'explicit initial release included unselected modules'
# 移行 v1 输入，但 v2 输出绝不保留默认包。
$v1=$first | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$v1.schemaVersion=1
# Explicitly construct v1 entries, avoiding any dependency on the new client's DTO.
$v1Entries=@()
foreach($group in $first.modules){$item=[ordered]@{id=$group.id};foreach($property in $group.versions[0].PSObject.Properties){$item[$property.Name]=$property.Value};$v1Entries += [pscustomobject]$item}
$v1.modules=$v1Entries
$v1Path=Join-Path $OutputRoot 'v1.json';Write-JsonFile $v1 $v1Path
$args.PreviousCatalogPath=$v1Path;$args.OutputPath=Join-Path $OutputRoot 'migrated.json'
& (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @args | Out-Null
& (Join-Path $root 'build\Verify-Catalog.ps1') -CatalogPath $args.OutputPath
Assert-Condition ((Read-JsonFile $args.OutputPath).schemaVersion -eq 2) 'v1 publishing migration failed'
$changedHash=$launch.versions[0] | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100;$changedHash.sha256=('c'*64)
Assert-Fails { Merge-CatalogVersions -Id $launch.id -Previous @($launch.versions) -NewRelease $changedHash }
Assert-Fails { Merge-CatalogVersions -Id $launch.id -Previous @($old,$old) }
$wrongRange=$old | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100;$wrongRange.maxHostVersion='1.0.0'
Assert-Fails { Assert-CatalogRelease $wrongRange $launch.id }
$corruptRecords=$records | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
$corruptRecords[0].sha256=('d'*64)
$corruptPath=Join-Path $OutputRoot 'corrupt-packages.json';Write-JsonFile @($corruptRecords) $corruptPath
$args.PackagesJson=$corruptPath;$args.PreviousCatalogPath='';$args.OutputPath=Join-Path $OutputRoot 'corrupt.json'
Assert-Fails { & (Join-Path $root 'build\Generate-ModuleCatalog.ps1') @script:args }
# Mock gh to prove v1 bootstrap collects versions superseded in older catalogs; no network.
$global:fixtureCatalogs=@{}
$recent=$v1 | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100;$recent.releaseTag='recent';$global:fixtureCatalogs['recent']=$recent
$older=$v1 | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100;$older.releaseTag='older'
$olderLaunch=@($older.modules | Where-Object id -EQ $launch.id)[0]
foreach($property in $old.PSObject.Properties){$olderLaunch.$($property.Name)=$property.Value}
$global:fixtureCatalogs['older']=$older
function global:gh {
    $arguments=@($args);$global:LASTEXITCODE=0
    if($arguments[1] -eq 'list'){return '[{"tagName":"recent","publishedAt":"2026-09-30T00:00:00Z"},{"tagName":"older","publishedAt":"2026-09-29T00:00:00Z"}]'}
    if($arguments[1] -eq 'view'){return 'module-catalog.json'}
    if($arguments[1] -eq 'download'){$directory=$arguments[[Array]::IndexOf($arguments,'--dir')+1];New-Item -ItemType Directory -Path $directory -Force | Out-Null;[IO.File]::WriteAllText((Join-Path $directory 'module-catalog.json'),($global:fixtureCatalogs[$arguments[2]] | ConvertTo-Json -Depth 100));return}
    throw 'Unexpected gh call in test.'
}
$previousResult=& (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'bootstrap')
$boot=Read-JsonFile $previousResult
Assert-Condition ($boot.schemaVersion -eq 2 -and (@($boot.modules | Where-Object id -EQ $launch.id)[0]).versions.Count -eq 2) 'v1 bootstrap lost superseded versions'
$global:fixtureCatalogs['recent'].releaseTag='wrong-tag'
Assert-Fails { & (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'invalid-tag') }
$global:fixtureCatalogs['recent'].releaseTag='recent';$global:fixtureCatalogs['recent'].schemaVersion=99
Assert-Fails { & (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'unsupported-schema') }
Remove-Item -LiteralPath Function:\gh
# Query and asset-download failures must not masquerade as an empty release history.
function global:gh { $global:LASTEXITCODE=1;return '' }
try { Assert-Fails { & (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'query-failure') } }
finally { Remove-Item -LiteralPath Function:\gh }
function global:gh {
    $global:LASTEXITCODE=0
    if($args[1] -eq 'list'){return '[{"tagName":"recent","publishedAt":"2026-09-30T00:00:00Z"}]'}
    if($args[1] -eq 'view'){return 'module-catalog.json'}
    $global:LASTEXITCODE=1;return ''
}
try { Assert-Fails { & (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'download-failure') } }
finally { Remove-Item -LiteralPath Function:\gh }
& (Join-Path $PSScriptRoot 'Validate-ReleaseNotes.ps1') -OutputRoot (Join-Path $OutputRoot 'release-notes')
Write-Host "PASS: $script:checks catalog publisher/schema/history checks; all assets are local fixtures. Output: $OutputRoot"
} finally {
    $env:STARPIE_MODULE_REGISTRY=$previousRegistryOverride
    # Mock gh failures are asserted above; do not leak their exit code to the Actions pwsh wrapper.
    $global:LASTEXITCODE=$previousNativeExit
}
