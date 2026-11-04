[CmdletBinding()]
param([Parameter(Mandatory)][string]$CatalogPath,[AllowEmptyCollection()][string[]]$ReleasedModuleIds,[AllowEmptyString()][string]$PreviousCatalogPath)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'StarPie.Catalog.psm1') -Force
$catalog = Read-JsonFile $CatalogPath
if ($catalog.schemaVersion -ne 2) { throw 'Catalog schemaVersion must be 2.' }
$registry = Get-ModuleRegistry
$enabled = @(Get-EnabledModules $registry)
$scopedRelease = $PSBoundParameters.ContainsKey('ReleasedModuleIds')
$selected = if ($scopedRelease) { Get-CatalogReleaseSelection -ModuleIds $ReleasedModuleIds -EnabledModules $enabled }
$previous = $null
if ($scopedRelease) {
    if (-not $PSBoundParameters.ContainsKey('PreviousCatalogPath')) { throw 'Selected releases require the same explicit history path as generation.' }
    if ($PreviousCatalogPath) {
        $previous = Read-JsonFile $PreviousCatalogPath
        if ($previous.schemaVersion -notin @(1,2)) { throw 'Unsupported previous catalog schema.' }
    }
    Assert-CatalogHistoryPreserved -Catalog $catalog -PreviousCatalog $previous -Selected $selected -EnabledModules $enabled
}
$ids = @{}
foreach ($group in @($catalog.modules)) {
    if ($group.id -notmatch '^starpie\.' -or $ids.ContainsKey($group.id)) { throw "Invalid or duplicate catalog ID: $($group.id)" }
    $ids[$group.id] = $true
    foreach ($key in @('version','packageUrl','sha256','minHostVersion','apiVersion')) { if ($null -ne $group.PSObject.Properties[$key]) { throw "Legacy top-level version fields are forbidden: $($group.id)" } }
    if ($null -eq $group.PSObject.Properties['versions'] -or @($group.versions).Count -eq 0) { throw "Empty history: $($group.id)" }
    Merge-CatalogVersions -Id $group.id -Previous @($group.versions) | Out-Null
    if (-not $scopedRelease -and $enabled.id -notcontains $group.id) { throw "Module is not enabled in source registry: $($group.id)" }
}
foreach ($module in $enabled) {
    if ($scopedRelease -and -not $selected.Contains($module.id)) { continue }
    $entry = @($catalog.modules | Where-Object id -EQ $module.id) | Select-Object -First 1
    if ($null -eq $entry -or -not ($entry.versions | Where-Object version -CEQ $module.version)) { throw "Current source version absent: $($module.id) $($module.version)" }
}
$schema = Join-Path (Get-RepositoryRoot) 'catalog/module-catalog.schema.json'
if (Get-Command Test-Json -ErrorAction SilentlyContinue) {
    if (-not (Test-Json -Json (Get-Content -LiteralPath $CatalogPath -Raw) -SchemaFile $schema -ErrorAction Stop)) { throw 'Catalog schema validation failed.' }
}
Write-Host "Catalog v2 verification passed: $CatalogPath ($($catalog.modules.Count) modules)."
