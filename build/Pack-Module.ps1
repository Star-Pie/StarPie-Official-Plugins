[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$ModuleId,
  [string]$Configuration = 'Release',
  [string]$OutputDirectory = 'artifacts/packages',
  [string]$ReleaseTag = 'local',
  [string]$OutputMetadataPath,
  [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$root = Get-RepositoryRoot
$registry = Get-ModuleRegistry
$module = Get-ModuleById -ModuleId $ModuleId -Registry $registry
$projectDirectory = Resolve-ModuleDirectory -Module $module
$buildOutputDirectory = Resolve-ModuleOutputDirectory -Module $module -Configuration $Configuration
$assemblyPath = Join-Path $buildOutputDirectory $module.assembly

if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Module assembly not found after build: $assemblyPath. Build module '$ModuleId' first."
}

$packageOutputDirectory = Join-Path $root $OutputDirectory
if (-not [System.IO.Path]::IsPathRooted($packageOutputDirectory)) {
    $packageOutputDirectory = [System.IO.Path]::GetFullPath($packageOutputDirectory)
}
if ($Clean -and (Test-Path -LiteralPath $packageOutputDirectory)) {
    Remove-Item -LiteralPath $packageOutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageOutputDirectory -Force | Out-Null

$stagingRoot = Join-Path $root ('artifacts/staging/' + $module.id + '/' + $module.version)
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$excludeNames = @(
    'StarPie.Plugin.Abstractions.dll',
    'StarPie.dll'
)
$excludePatterns = @(
    '*.pdb',
    '*.xml',
    '*.deps.json',
    '*.runtimeconfig.json'
)

function Copy-OutputPayload {
    param([string]$SourceRoot,[string]$DestinationRoot,[string]$RelativePath)

    $source = Join-Path $SourceRoot $RelativePath
    $destination = Join-Path $DestinationRoot $RelativePath
    $directory = Split-Path -Parent $destination
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$allFiles = @(Get-ChildItem -LiteralPath $buildOutputDirectory -Recurse -File)
foreach ($file in $allFiles) {
    $relative = ConvertTo-NormalizedRelativePath -BasePath $buildOutputDirectory -Path $file.FullName
    $fileName = $file.Name
    if ($excludeNames -contains $fileName) { continue }
    $skip = $false
    foreach ($pattern in $excludePatterns) {
        if ($fileName -like $pattern) { $skip = $true; break }
    }
    if ($skip) { continue }
    if ($relative.StartsWith('ref/', [System.StringComparison]::OrdinalIgnoreCase)) { continue }

    Copy-OutputPayload -SourceRoot $buildOutputDirectory -DestinationRoot $stagingRoot -RelativePath $relative
}

$pluginJsonSource = Join-Path $projectDirectory 'plugin.json'
if (Test-Path -LiteralPath $pluginJsonSource) {
    Copy-Item -LiteralPath $pluginJsonSource -Destination (Join-Path $stagingRoot 'plugin.json') -Force
}
else {
    $claims = @()
    foreach ($claim in @($module.typeClaims)) {
        $claims += [ordered]@{
            typeName = $claim
            contributionId = $module.contributionId
        }
    }

    $pluginManifest = [ordered]@{
        schemaVersion = 1
        id = $module.pluginId
        name = $module.name
        description = if ($module.description) { $module.description } else { "$($module.name) official StarPie module" }
        author = if ($module.author) { $module.author } else { 'StarPie Studio' }
        homepage = if ($module.homepage) { $module.homepage } else { 'https://github.com/Star-Pie/StarPie' }
        license = if ($module.license) { $module.license } else { 'MIT' }
        version = $module.version
        apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
        minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
        maxHostVersion = $module.maxHostVersion
        targetFramework = $module.targetFramework
        platform = 'win-x64'
        assembly = $module.assembly
        entryType = $module.entryType
        capabilities = @($module.capabilities)
        claimedTypes = $claims
        contributions = [ordered]@{
            actions = $true
            icons = $false
            i18n = $false
        }
        dependencies = @()
        icon = $module.icon
        tags = @($module.tags)
    }
    Write-JsonFile -Value $pluginManifest -Path (Join-Path $stagingRoot 'plugin.json')
}

$assemblySha256 = Get-Sha256Hex -Path $assemblyPath
$moduleManifest = [ordered]@{
    schemaVersion = 1
    id = $module.id
    name = $module.name
    version = $module.version
    apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
    minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
    maxHostVersion = $module.maxHostVersion
    targetFramework = $module.targetFramework
    assembly = $module.assembly
    assemblySha256 = $assemblySha256
    typeClaims = @($module.typeClaims)
    capabilities = @($module.capabilities)
    releaseTag = $ReleaseTag
}
Write-JsonFile -Value $moduleManifest -Path (Join-Path $stagingRoot 'module.manifest.json')
$assetName = "$([System.IO.Path]::GetFileNameWithoutExtension($module.assembly))-$($module.version).spkg"
$packagePath = Join-Path $packageOutputDirectory $assetName
New-DeterministicZip -SourceDirectory $stagingRoot -DestinationPath $packagePath

$sha256 = Get-Sha256Hex -Path $packagePath
$size = (Get-Item -LiteralPath $packagePath).Length
$package = [ordered]@{
    id = $module.id
    name = $module.name
    version = $module.version
    assetName = $assetName
    packagePath = $packagePath
    sha256 = $sha256
    size = $size
    apiVersion = if ($module.apiVersion) { $module.apiVersion } else { $registry.sdkApiVersion }
    minHostVersion = if ($module.minHostVersion) { $module.minHostVersion } else { $registry.minimumHostVersion }
    maxHostVersion = $module.maxHostVersion
    typeClaims = @($module.typeClaims)
    capabilities = @($module.capabilities)
    releaseTag = $ReleaseTag
}

if ($OutputMetadataPath) {
    Write-JsonFile -Value $package -Path $OutputMetadataPath
}

$package | ConvertTo-Json -Depth 100