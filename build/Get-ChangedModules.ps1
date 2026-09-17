[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$BaseRef,
  [string]$HeadRef = 'HEAD',
  [string[]]$ModuleIds,
  [switch]$ForceAll,
  [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

if ($ModuleIds) {
    $ModuleIds = @($ModuleIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
$root = Get-RepositoryRoot
Push-Location $root
try {
    $registry = Get-ModuleRegistry
    $enabled = @(Get-EnabledModules -Registry $registry)

    $sharedPrefixes = @(
        '.github/',
        'build/',
        'catalog/',
        'docs/',
        'templates/',
        'tests/'
    )
    $sharedFiles = @(
        'Directory.Build.props',
        'Directory.Build.targets',
        'global.json',
        'NuGet.config',
        'module-registry.json',
        'StarPie.OfficialPlugins.sln'
    )

    $changedPaths = @()
    if (-not $ForceAll) {
        $changedPaths = @(git diff --name-only "$BaseRef...$HeadRef")
    }

    $forceBySharedChange = $ForceAll -or @($changedPaths | Where-Object {
        $path = $_
        ($sharedFiles -contains $path) -or @($sharedPrefixes | Where-Object { $path.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
    }).Count -gt 0

    $selected = @()
    if ($forceBySharedChange) {
        $selected = $enabled
    }
    else {
        foreach ($module in $enabled) {
            $moduleDirectory = ConvertTo-NormalizedRelativePath -BasePath $root -Path (Resolve-ModuleDirectory -Module $module)
            $prefix = $moduleDirectory.TrimEnd('/') + '/'
            if (@($changedPaths | Where-Object { $_.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
                $selected += $module
            }
        }
    }

    if ($ModuleIds -and $ModuleIds.Count -gt 0) {
        $selected = @($selected | Where-Object { $ModuleIds -contains $_.id })
        if (-not $forceBySharedChange) {
            $selected = @($enabled | Where-Object { $ModuleIds -contains $_.id })
        }
    }

    $selected = @($selected | Sort-Object id -Unique)
    $matrixModules = @($selected | ForEach-Object {
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
        forceAll = [bool]$forceBySharedChange
        reason = if ($forceBySharedChange) { 'shared-change-or-force-all' } elseif ($selected.Count -gt 0) { 'module-change' } else { 'no-module-change' }
        changedPaths = @($changedPaths)
        changed = @($selected | ForEach-Object { $_.id })
        hasModules = $selected.Count -gt 0
        matrix = $matrixModules
    }

    if ($OutputPath) {
        Write-JsonFile -Value $result -Path $OutputPath
    }

    if ($env:GITHUB_OUTPUT) {
        "has_modules=$($result.hasModules.ToString().ToLowerInvariant())" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "force_all=$($result.forceAll.ToString().ToLowerInvariant())" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "reason=$($result.reason)" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "matrix=$($matrixModules | ConvertTo-Json -Compress -Depth 100)" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    $result | ConvertTo-Json -Depth 100
    exit 0
}
finally {
    Pop-Location
}
