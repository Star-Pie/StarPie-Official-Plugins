[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ReleaseTag,
  [Parameter(Mandatory)][ValidateSet('stable','beta','dev')][string]$ReleaseChannel,
  [Parameter(Mandatory)][string]$PackagesJson,
  [string]$PreviousCatalogPath = 'catalog/module-catalog.json',
  [Parameter(Mandatory)][string]$Repository,
  [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$root = Get-RepositoryRoot
$registry = Get-ModuleRegistry
$enabled = @(Get-EnabledModules -Registry $registry)

$packagesRaw = if (Test-Path -LiteralPath $PackagesJson) { Get-Content -LiteralPath $PackagesJson -Raw } else { $PackagesJson }
$packages = @($packagesRaw | ConvertFrom-Json -Depth 100)
if ($packages.Count -eq 1 -and $null -ne $packages[0].packages) {
    $packages = @($packages[0].packages)
}

$previousCatalog = $null
$previousModules = @{}
if ($PreviousCatalogPath -and (Test-Path -LiteralPath $PreviousCatalogPath)) {
    $previousCatalog = Read-JsonFile -Path $PreviousCatalogPath
    foreach ($item in @($previousCatalog.modules)) {
        $previousModules[$item.id] = $item
    }
}

$newPackages = @{}
foreach ($package in $packages) {
    $newPackages[$package.id] = $package
}

$catalogModules = @()
foreach ($module in $enabled) {
    $entry = $null
    if ($newPackages.ContainsKey($module.id)) {
        $package = $newPackages[$module.id]
        if ($package.version -ne $module.version) {
            throw "Package version '$($package.version)' for module '$($module.id)' does not match registry version '$($module.version)'."
        }

        $entry = [ordered]@{
            id = $module.id
            name = $module.name
            version = $module.version
            releaseTag = $ReleaseTag
            assetName = $package.assetName
            packageUrl = "https://github.com/$Repository/releases/download/$ReleaseTag/$($package.assetName)"
            sha256 = $package.sha256
            size = [int64]$package.size
            apiVersion = $package.apiVersion
            minHostVersion = $package.minHostVersion
            maxHostVersion = $package.maxHostVersion
            typeClaims = @($package.typeClaims)
            capabilities = @($package.capabilities)
            signature = $package.signature
        }
    }
    elseif ($previousModules.ContainsKey($module.id)) {
        $entry = $previousModules[$module.id]
        if ($entry.version -ne $module.version) {
            throw "Module '$($module.id)' requires version '$($module.version)', but no new package was supplied and the previous catalog has '$($entry.version)'."
        }
    }
    else {
        throw "Module '$($module.id)' has no package in this release and no entry in the previous catalog."
    }

    $catalogModules += $entry
}

$now = [DateTimeOffset]::UtcNow
$catalog = [ordered]@{
    schemaVersion = 1
    catalogVersion = $now.ToString('yyyy.MM.dd.HHmmss')
    releaseTag = $ReleaseTag
    releaseChannel = $ReleaseChannel
    sdkApiVersion = $registry.sdkApiVersion
    minimumHostVersion = $registry.minimumHostVersion
    generatedAt = $now.ToString('o')
    modules = $catalogModules
}

Write-JsonFile -Value $catalog -Path $OutputPath
$catalog | ConvertTo-Json -Depth 100