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
& (Join-Path $root 'build\Import-ModuleRegistryFromSource.ps1') | Out-Null
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
    if($arguments[1] -eq 'download'){$directory=$arguments[[Array]::IndexOf($arguments,'--dir')+1];New-Item -ItemType Directory -Path $directory -Force | Out-Null;[IO.File]::WriteAllText((Join-Path $directory 'module-catalog.json'),($global:fixtureCatalogs[$arguments[2]] | ConvertTo-Json -Depth 100));return}
    throw 'Unexpected gh call in test.'
}
$previousResult=& (Join-Path $root 'build\Get-PreviousCatalog.ps1') -Channel beta -Repository Star-Pie/StarPie-Official-Plugins -OutputDirectory (Join-Path $OutputRoot 'bootstrap')
$boot=Read-JsonFile $previousResult
Assert-Condition ($boot.schemaVersion -eq 2 -and (@($boot.modules | Where-Object id -EQ $launch.id)[0]).versions.Count -eq 2) 'v1 bootstrap lost superseded versions'
Remove-Item -LiteralPath Function:\gh
& (Join-Path $PSScriptRoot 'Validate-ReleaseNotes.ps1')
Write-Host "PASS: $script:checks catalog publisher/schema/history checks; all assets are local fixtures. Output: $OutputRoot"
