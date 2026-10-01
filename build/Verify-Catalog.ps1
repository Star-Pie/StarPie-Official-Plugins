[CmdletBinding()]
param([Parameter(Mandatory)][string]$CatalogPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'StarPie.Catalog.psm1') -Force
$catalog = Read-JsonFile $CatalogPath
if ($catalog.schemaVersion -ne 2) { throw 'Catalog schemaVersion must be 2.' }
$registry = Get-ModuleRegistry
$enabled = @(Get-EnabledModules $registry)
$ids = @{}
foreach ($group in @($catalog.modules)) {
    if ($group.id -notmatch '^starpie\.' -or $ids.ContainsKey($group.id)) { throw "Invalid or duplicate catalog ID: $($group.id)" }
    $ids[$group.id] = $true
    foreach ($key in @('version','packageUrl','sha256','minHostVersion','apiVersion')) { if ($null -ne $group.PSObject.Properties[$key]) { throw "Legacy top-level version fields are forbidden: $($group.id)" } }
    if ($null -eq $group.PSObject.Properties['versions'] -or @($group.versions).Count -eq 0) { throw "Empty history: $($group.id)" }
    Merge-CatalogVersions -Id $group.id -Previous @($group.versions) | Out-Null
    if ($enabled.id -notcontains $group.id) { throw "Module is not enabled in source registry: $($group.id)" }
}
foreach ($module in $enabled) {
    $entry = @($catalog.modules | Where-Object id -EQ $module.id) | Select-Object -First 1
    if ($null -eq $entry -or -not ($entry.versions | Where-Object version -CEQ $module.version)) { throw "Current source version absent: $($module.id) $($module.version)" }
}
$schema = Join-Path (Get-RepositoryRoot) 'catalog/module-catalog.schema.json'
if (Get-Command Test-Json -ErrorAction SilentlyContinue) {
    if (-not (Test-Json -Json (Get-Content -LiteralPath $CatalogPath -Raw) -SchemaFile $schema -ErrorAction Stop)) { throw 'Catalog schema validation failed.' }
}
Write-Host "Catalog v2 verification passed: $CatalogPath ($($catalog.modules.Count) modules)."
