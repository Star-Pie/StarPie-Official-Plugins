[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$CatalogPath,
  [string[]]$ChangedModuleIds = @(),
  [Parameter(Mandatory)][string]$ReleaseTag,
  [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$ReleaseChannel,
  [Parameter(Mandatory)][string]$OutputPath
)

function Write-Utf8NoBom {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Content)
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$catalog = Read-JsonFile -Path $CatalogPath
$changed = @{}
foreach ($id in $ChangedModuleIds) { $changed[$id] = $true }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("## StarPie Official Modules")
$lines.Add('')
$lines.Add("Release: ``$ReleaseTag``")
$lines.Add("Channel: ``$ReleaseChannel``")
$lines.Add("Catalog version: ``$($catalog.catalogVersion)``")
$lines.Add('')
$lines.Add('This release represents the complete current official module set. Only changed module packages are attached;')
$lines.Add('unchanged modules are referenced from their previous immutable release assets in `module-catalog.json`.')
$lines.Add('')
$lines.Add('| Module | Version | Release status |')
$lines.Add('|---|---:|---|')
foreach ($module in @($catalog.modules | Sort-Object name)) {
    $status = if ($changed.ContainsKey($module.id)) { 'Updated in this release' } else { "Referenced from ``$($module.releaseTag)``" }
    $lines.Add("| $($module.name) | $($module.version) | $status |")
}
$lines.Add('')
$lines.Add('### Assets')
$lines.Add('')
$lines.Add('- `module-catalog.json`')
if ($changed.Count -gt 0) {
    foreach ($module in @($catalog.modules | Where-Object { $changed.ContainsKey($_.id) } | Sort-Object name)) {
        $lines.Add("- ``$($module.assetName)``")
    }
}
else {
    $lines.Add('- No module package changes')
}

Write-Utf8NoBom -Path $OutputPath -Content ($lines -join [Environment]::NewLine)

function Write-Utf8NoBom {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$Content)
    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}