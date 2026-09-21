[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$PackagePath,
  [Parameter(Mandatory)][string]$ModuleId
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$module = Get-ModuleById -ModuleId $ModuleId

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Package not found: $PackagePath"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('starpie-spkg-verify-' + [Guid]::NewGuid().ToString('N'))
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $temp)

    $pluginJson = Join-Path $temp 'plugin.json'
    $moduleManifest = Join-Path $temp 'module.manifest.json'
    $assemblyPath = Join-Path $temp $module.assembly

    foreach ($path in @($pluginJson, $moduleManifest, $assemblyPath)) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Package is missing required entry: $path"
        }
    }

    $plugin = Read-JsonFile -Path $pluginJson
    $manifest = Read-JsonFile -Path $moduleManifest

    if ($plugin.id -ne $module.pluginId) { throw "plugin.json id mismatch: $($plugin.id) != $($module.pluginId)" }
    if ($plugin.version -ne $module.version) { throw "plugin.json version mismatch: $($plugin.version) != $($module.version)" }
    if ($plugin.assembly -ne $module.assembly) { throw "plugin.json assembly mismatch: $($plugin.assembly) != $($module.assembly)" }
    if ($manifest.id -ne $module.id) { throw "module.manifest.json id mismatch: $($manifest.id) != $($module.id)" }
    if ($manifest.version -ne $module.version) { throw "module.manifest.json version mismatch: $($manifest.version) != $($module.version)" }
    if ((Get-Sha256Hex -Path $assemblyPath) -ne $manifest.assemblySha256) {
        throw 'Assembly SHA-256 does not match module.manifest.json.'
    }
    if (Test-Path -LiteralPath (Join-Path $temp 'StarPie.Plugin.Abstractions.dll')) {
        throw 'Package must not contain StarPie.Plugin.Abstractions.dll.'
    }

    Write-Host "Package verification passed: $PackagePath"
}
finally {
    if ([System.IO.Directory]::Exists($temp)) {
        [System.IO.Directory]::Delete($temp, $true)
    }
}