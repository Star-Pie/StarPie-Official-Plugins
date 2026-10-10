[CmdletBinding()]
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
$root = Get-RepositoryRoot
$id = 'io.github.softblack42.desktoppet'
$output = Join-Path $root 'artifacts/desktop-pet-release'
if (Test-Path -LiteralPath $output) { throw 'DesktopPet output exists; preserve it and use a fresh checkout.' }
New-Item -ItemType Directory -Path $output | Out-Null
& (Join-Path $PSScriptRoot 'Import-ModuleRegistryFromSource.ps1')
$registry = Get-ModuleRegistry -RegistryPath 'artifacts/generated/module-registry.json'
$module = @($registry.modules | Where-Object id -CEQ $id)
if ($module.Count -ne 1 -or $module[0].enabled -ne $false -or $module[0].apiVersion -cne '1.11' -or $module[0].minHostVersion -cne '1.8.0-beta.7') {
    throw 'DesktopPet must retain its ID and be excluded from the official catalog.'
}
$version = [string]$module[0].version
$tag = "desktop-pet-v$version"
$staging = [IO.Path]::GetFullPath((Join-Path $root "artifacts/staging/$id/$version"))
$prefix = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $staging.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $staging)) {
    throw 'Staging must be a new path inside this checkout artifacts directory.'
}
# This registry is local to the standalone build, never used to generate a module catalog.
$module[0].enabled = $true
$registryPath = Join-Path $output 'standalone-registry.json'
Write-JsonFile -Value $registry -Path $registryPath
$oldRegistry = $env:STARPIE_MODULE_REGISTRY
try {
    $env:STARPIE_MODULE_REGISTRY = $registryPath
    & (Join-Path $PSScriptRoot 'Build-Modules.ps1') -ModuleIds $id -Configuration $Configuration -RunTests
    & (Join-Path $PSScriptRoot 'Pack-Module.ps1') -ModuleId $id -Configuration $Configuration -ReleaseTag $tag -OutputDirectory 'artifacts/desktop-pet-release' -OutputMetadataPath 'artifacts/desktop-pet-release/package.json'
    $package = Read-JsonFile 'artifacts/desktop-pet-release/package.json'
    & (Join-Path $PSScriptRoot 'Verify-ModulePackage.ps1') -PackagePath $package.packagePath -ModuleId $id
    $zip = [IO.Compression.ZipFile]::OpenRead($package.packagePath)
    try {
        $expected = @('StarPie.Plugin.DesktopPet.dll','module.manifest.json','plugin.json') | Sort-Object
        $actual = @($zip.Entries.FullName) | Sort-Object
        if (@(Compare-Object $expected $actual).Count -ne 0) { throw 'DesktopPet payload must be exactly one DLL and two metadata files.' }
    } finally { $zip.Dispose() }
    # A ZIP alias lets Windows users extract the same bytes and install the DLL using beta.7's file picker.
    $zipPath = [IO.Path]::ChangeExtension($package.packagePath, '.zip')
    Copy-Item -LiteralPath $package.packagePath -Destination $zipPath
    @($package.packagePath, $zipPath) | ForEach-Object {
        "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
    } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
    "release_tag=$tag" | Out-File -LiteralPath (Join-Path $output 'release.properties') -Encoding utf8
    if ($env:GITHUB_OUTPUT) { "release_tag=$tag" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
} finally { $env:STARPIE_MODULE_REGISTRY = $oldRegistry }
