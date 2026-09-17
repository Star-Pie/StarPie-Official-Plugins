[CmdletBinding()]
param([Parameter(Mandatory)][string]$CatalogPath)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$catalog = Read-JsonFile -Path $CatalogPath
$registry = Get-ModuleRegistry
$enabled = @(Get-EnabledModules -Registry $registry)

$errors = New-Object System.Collections.Generic.List[string]
$ids = @($catalog.modules | ForEach-Object { $_.id })
$duplicates = @($ids | Group-Object | Where-Object Count -gt 1)
foreach ($duplicate in $duplicates) {
    $errors.Add("Duplicate module id in catalog: $($duplicate.Name)")
}

foreach ($module in $enabled) {
    $entry = @($catalog.modules | Where-Object { $_.id -eq $module.id }) | Select-Object -First 1
    if ($null -eq $entry) {
        $errors.Add("Catalog is missing module: $($module.id)")
        continue
    }
    if ($entry.version -ne $module.version) {
        $errors.Add("Catalog version mismatch for $($module.id): catalog=$($entry.version), registry=$($module.version)")
    }
    if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        $errors.Add("Invalid SHA-256 for $($module.id)")
    }
    if ($entry.packageUrl -notmatch '^https://') {
        $errors.Add("Invalid package URL for $($module.id)")
    }
    if ($entry.assetName -notmatch '\.spkg$') {
        $errors.Add("Invalid asset name for $($module.id)")
    }
}

foreach ($entry in @($catalog.modules)) {
    if ($ids -notcontains $entry.id) { continue }
    if ($enabled.id -notcontains $entry.id) {
        $errors.Add("Catalog contains module not enabled in registry: $($entry.id)")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Catalog verification passed: $CatalogPath ($($catalog.modules.Count) modules)."