[CmdletBinding()]
param(
  [string]$PreviousCatalogPath,
  [string[]]$ModuleIds,
  [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

if ($ModuleIds) {
    $ModuleIds = @($ModuleIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
$root = Get-RepositoryRoot
$registry = Get-ModuleRegistry
$enabled = @(Get-EnabledModules -Registry $registry)
$previous = @{}

if ($PreviousCatalogPath -and (Test-Path -LiteralPath $PreviousCatalogPath)) {
    $catalog = Read-JsonFile -Path $PreviousCatalogPath
    foreach ($module in @($catalog.modules)) {
        $previous[$module.id] = $module.version
    }
}

$selected = @()
foreach ($module in $enabled) {
    $requested = (-not $ModuleIds -or $ModuleIds.Count -eq 0 -or $ModuleIds -contains $module.id)
    if (-not $requested) { continue }
    if (-not $previous.ContainsKey($module.id) -or $previous[$module.id] -ne $module.version) {
        $selected += $module
    }
}

$matrix = @($selected | ForEach-Object {
    [ordered]@{
        id = $_.id
        name = $_.name
        project = $_.project
        assembly = $_.assembly
        targetFramework = $_.targetFramework
        version = $_.version
        pluginId = $_.pluginId
        contributionId = $_.contributionId
        typeClaims = @($_.typeClaims)
        capabilities = @($_.capabilities)
        releaseDirectory = $_.releaseDirectory
    }
})

$result = [ordered]@{
    changed = @($selected | ForEach-Object { $_.id })
    hasModules = $selected.Count -gt 0
    matrix = $matrix
}

if ($OutputPath) { Write-JsonFile -Value $result -Path $OutputPath }
$result | ConvertTo-Json -Depth 100
exit 0
