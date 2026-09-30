[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ReleaseTag,
  [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$ReleaseChannel,
  [Parameter(Mandatory)][string]$PackagesJson,
  [string]$PreviousCatalogPath = 'catalog/module-catalog.json',
  [Parameter(Mandatory)][string]$Repository,
  [Parameter(Mandatory)][string]$OutputPath,
  [switch]$RequirePackageManifest
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

function Read-PackagePluginManifest {
    param([Parameter(Mandatory)][string]$PackagePath)
    if (-not (Test-Path -LiteralPath $PackagePath)) { throw "Package not found while generating catalog: $PackagePath" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.GetEntry('plugin.json')
        if ($null -eq $entry) { throw "Package is missing plugin.json: $PackagePath" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return ($reader.ReadToEnd() | ConvertFrom-Json -Depth 100) }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Get-ManifestMetadata {
    param([Parameter(Mandatory)]$Module)
    $pluginPath = Join-Path $root ($Module.project -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $pluginJsonPath = Join-Path (Split-Path -Parent $pluginPath) 'plugin.json'
    if (Test-Path -LiteralPath $pluginJsonPath) { return (Read-JsonFile -Path $pluginJsonPath) }
    return [pscustomobject]@{ description = $Module.description; author = $Module.author; homepage = $Module.homepage; license = $Module.license; icon = $Module.icon; tags = @($Module.tags); features = @($Module.features); targetFramework = $Module.targetFramework }
}

$newPackages = @{}
foreach ($package in $packages) { $newPackages[$package.id] = $package }

$catalogModules = @()
foreach ($module in $enabled) {
    $entry = $null
    if ($newPackages.ContainsKey($module.id)) {
        $package = $newPackages[$module.id]
        if ($package.version -ne $module.version) {
            throw "Package version '$($package.version)' for module '$($module.id)' does not match registry version '$($module.version)'."
        }

        $packagePath = [string]$package.packagePath
        if (-not (Test-Path -LiteralPath $packagePath)) {
            $packagePath = Join-Path $root ('artifacts/packages/' + $package.assetName)
        }
        $manifest = Read-PackagePluginManifest -PackagePath $packagePath
        if ($RequirePackageManifest -and (-not $manifest)) { throw "Unable to read plugin.json for module '$($module.id)'." }
        if ($manifest.id -and $manifest.id -ne $module.pluginId) { throw "Package plugin.json id mismatch for '$($module.id)': $($manifest.id)." }
        if ($manifest.version -and $manifest.version -ne $module.version) { throw "Package plugin.json version mismatch for '$($module.id)': $($manifest.version)." }
        $entry = [ordered]@{
            id = $module.id
            name = if ($manifest.name) { $manifest.name } else { $module.name }
            description = if ($manifest.description) { $manifest.description } else { $module.description }
            author = if ($manifest.author) { $manifest.author } else { $module.author }
            homepage = if ($manifest.homepage) { $manifest.homepage } else { $module.homepage }
            license = if ($manifest.license) { $manifest.license } else { $module.license }
            icon = $manifest.icon
            tags = @($manifest.tags)
            features = @(if ($manifest.features) { @($manifest.features) } else { @($module.features) })
            version = $module.version
            releaseTag = $ReleaseTag
            assetName = $package.assetName
            packageUrl = "https://github.com/$Repository/releases/download/$ReleaseTag/$($package.assetName)"
            sha256 = $package.sha256
            size = [int64]$package.size
            apiVersion = if ($manifest.apiVersion) { $manifest.apiVersion } else { $package.apiVersion }
            targetFramework = $manifest.targetFramework
            minHostVersion = if ($manifest.minHostVersion) { $manifest.minHostVersion } else { $package.minHostVersion }
            maxHostVersion = $manifest.maxHostVersion
            typeClaims = @($package.typeClaims)
            capabilities = @(if ($manifest.capabilities) { @($manifest.capabilities) } else { @($package.capabilities) })
            signature = $package.signature
        }
    }
    elseif ($previousModules.ContainsKey($module.id)) {
        $entry = $previousModules[$module.id]
        $static = Get-ManifestMetadata -Module $module
        foreach ($property in @('description', 'author', 'homepage', 'license', 'icon', 'tags', 'features', 'targetFramework')) {
            if (-not ($entry.PSObject.Properties.Name -contains $property) -or $null -eq $entry.$property) { $entry | Add-Member -NotePropertyName $property -NotePropertyValue $static.$property -Force }
        }
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