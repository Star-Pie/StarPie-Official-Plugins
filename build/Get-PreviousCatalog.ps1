[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$Channel,
    [Parameter(Mandatory)][string]$Repository,
    [string]$OutputDirectory = 'artifacts\previous-catalog',
    [ValidateSet('stable','beta')][string]$TargetChannel = $Channel,
    [string]$RestoreFromTag,
    [string[]]$RestoreModuleIds
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Catalog.psm1') -Force
$Channel = $Channel.ToLowerInvariant()
$TargetChannel = $TargetChannel.ToLowerInvariant()
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$catalogPath = Join-Path $outputRoot 'module-catalog.json'
$channels = @($TargetChannel, $Channel | Select-Object -Unique)
$restoreSelection = $null
if ($RestoreFromTag -or $RestoreModuleIds) {
    if ($RestoreFromTag -cnotmatch '^modules-\d{4}\.\d{2}\.\d{2}\.\d+$' -or $channels.Count -ne 1) {
        throw 'Catalog restoration requires a published modules tag and the target channel as its base.'
    }
    Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
    $restoreSelection = Get-CatalogReleaseSelection -ModuleIds $RestoreModuleIds -EnabledModules @(Get-EnabledModules (Get-ModuleRegistry))
}

function Read-ReleasedCatalog([string]$Tag) {
    $assetNames = @(gh release view $Tag --repo $Repository --json assets --jq '.assets[].name')
    if ($LASTEXITCODE -ne 0) { throw "Unable to inspect released assets: $Tag" }
    if ($assetNames -cnotcontains 'module-catalog.json') { return }
    $rawDir = Join-Path $outputRoot ('raw-' + [guid]::NewGuid().ToString('N'))
    gh release download $Tag --repo $Repository --pattern 'module-catalog.json' --dir $rawDir --clobber 2>$null | Out-Null
    $rawPath = Join-Path $rawDir 'module-catalog.json'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $rawPath -PathType Leaf)) { throw "Unable to download released catalog: $Tag" }
    $catalog = Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json -Depth 100
    if ($catalog.releaseTag -cne $Tag -or $catalog.schemaVersion -notin @(1,2) -or $catalog.releaseChannel -cnotin @('stable','beta')) {
        throw "Invalid released catalog identity or schema: $Tag"
    }
    return [pscustomobject]@{ Catalog = $catalog; Path = $rawPath }
}

$releasesJson = gh release list --repo $Repository --exclude-drafts --limit 1000 --json tagName,publishedAt
if ($LASTEXITCODE -ne 0) { throw 'Unable to list released catalogs; do not treat a query failure as empty history.' }
$releases = @($releasesJson | ConvertFrom-Json | Sort-Object publishedAt -Descending)
$catalogsByChannel = @{}
foreach ($name in $channels) { $catalogsByChannel[$name] = [Collections.Generic.List[object]]::new() }
$completeChannels = 0
foreach ($release in $releases) {
    $tag = [string]$release.tagName
    if (-not $tag) { continue }
    $released = Read-ReleasedCatalog $tag
    if ($null -eq $released -or -not $catalogsByChannel.ContainsKey($released.Catalog.releaseChannel)) { continue }
    $catalogs = $catalogsByChannel[$released.Catalog.releaseChannel]
    if ($catalogs.Count -gt 0 -and $catalogs[0].Catalog.schemaVersion -eq 2) { continue }
    $catalogs.Add($released)
    if ($catalogs.Count -eq 1 -and $released.Catalog.schemaVersion -eq 2) {
        $completeChannels++
        if ($completeChannels -eq $channels.Count) { break }
    }
}

$catalogs = @(foreach ($name in $channels) { $catalogsByChannel[$name].ToArray() })
if ($catalogs.Count -eq 1 -and $catalogs[0].Catalog.schemaVersion -eq 2 -and -not $RestoreFromTag) {
    Copy-Item -LiteralPath $catalogs[0].Path -Destination $catalogPath -Force
    Write-Output $catalogPath
    return
}

function Assert-RestoredPackage([object]$Version) {
    $tag = [string]$Version.releaseTag
    $asset = [string]$Version.assetName
    if ($releases.tagName -cnotcontains $tag) { throw "Historical package release is not published: $tag" }
    $assetDir = Join-Path $outputRoot ('recovered-' + [guid]::NewGuid().ToString('N'))
    gh release download $tag --repo $Repository --pattern $asset --dir $assetDir 2>$null | Out-Null
    $assetPath = Join-Path $assetDir $asset
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Recoverable package was not found: $tag/$asset"
    }
    if ((Get-Item -LiteralPath $assetPath).Length -ne [long]$Version.size -or (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash -ine $Version.sha256) {
        throw "Recoverable package differs from its published record: $tag/$asset"
    }
}

$history = @{}
function Add-CatalogHistory([object[]]$Groups, [switch]$VerifyRestoredPackages) {
    $added = 0
    $seen = @{}
    foreach ($group in $Groups) {
        $id = [string]$group.id
        $incoming = @(Get-CatalogVersions $group)
        if (-not $id -or $seen.ContainsKey($id) -or $incoming.Count -eq 0) { throw "Invalid or duplicate historical module: $id" }
        $seen[$id] = $true
        if (-not $history.ContainsKey($id)) { $history[$id] = [Collections.Generic.List[object]]::new() }
        $incomingVersions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($version in $incoming) {
            if (-not $version.version -or -not $incomingVersions.Add($version.version)) { throw "Invalid or duplicate historical version: $id $($version.version)" }
            $versions = $history[$id]
            $same = @($versions | Where-Object version -CEQ $version.version)
            if ($same.Count -gt 0) {
                if ($same[0].sha256 -ine $version.sha256 -or [long]$same[0].size -ne [long]$version.size) { throw "Historical version was overwritten: $id $($version.version)" }
                continue
            }
            if ($VerifyRestoredPackages) { Assert-RestoredPackage $version }
            $record = [ordered]@{}
            foreach ($property in $version.PSObject.Properties) {
                if ($property.Name -ne 'id') { $record[$property.Name] = $property.Value }
            }
            $versions.Add([pscustomobject]$record)
            $added++
        }
    }
    return $added
}

if ($RestoreFromTag) {
    if ($catalogs.Count -ne 1 -or $catalogs[0].Catalog.schemaVersion -ne 2 -or $releases.tagName -cnotcontains $RestoreFromTag) {
        throw 'Catalog restoration requires a current v2 base and a published recovery tag.'
    }
    $recovery = Read-ReleasedCatalog $RestoreFromTag
    if ($null -eq $recovery -or $recovery.Catalog.schemaVersion -ne 2 -or $recovery.Catalog.releaseChannel -cne $TargetChannel) {
        throw 'The recovery tag must carry a v2 catalog in the target channel.'
    }
    $recoveryGroups = foreach ($id in $restoreSelection) {
        $groups = @($recovery.Catalog.modules | Where-Object id -CEQ $id)
        if ($groups.Count -ne 1 -or -not $groups[0].versions) { throw "The recovery tag does not contain a unique version history for $id." }
        Merge-CatalogVersions -Id $id -Previous $groups[0].versions | Out-Null
        $groups[0]
    }
    Add-CatalogHistory $catalogs[0].Catalog.modules | Out-Null
    if ((Add-CatalogHistory $recoveryGroups -VerifyRestoredPackages) -eq 0) { throw 'The selected records are already present in the current catalog.' }
} else {
    foreach ($released in $catalogs) { Add-CatalogHistory $released.Catalog.modules | Out-Null }
}

if ($catalogs.Count -gt 0) {
    $latest = $catalogs[0].Catalog
    $latest.schemaVersion = 2
    $latest.modules = @($history.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ id = $_; versions = $history[$_].ToArray() } })
    [IO.File]::WriteAllText($catalogPath, ($latest | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Write-Output $catalogPath
    return
}
$bootstrap = 'catalog\module-catalog.json'
if (Test-Path -LiteralPath $bootstrap) {
    try { $catalog = Get-Content -LiteralPath $bootstrap -Raw | ConvertFrom-Json -Depth 100 } catch { $catalog = $null }
    if ($null -ne $catalog -and $catalog.releaseChannel -in $channels -and $catalog.schemaVersion -in @(1,2)) {
        Copy-Item -LiteralPath $bootstrap -Destination $catalogPath -Force
        Write-Output $catalogPath
        return
    }
}
Write-Output ''
