[CmdletBinding()]
param([string]$OutputPath = 'artifacts/generated/module-registry.json')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force
$root = Get-RepositoryRoot
$sourceRoot = Join-Path $root 'src'
$modules = @()
$seenIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($moduleDirectory in Get-ChildItem -LiteralPath $sourceRoot -Directory | Where-Object { $_.Name -like 'StarPie.Plugin.*' -and $_.Name -ne 'StarPie.Plugin.Abstractions' } | Sort-Object Name) {
    $projectFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*.csproj' -File | Select-Object -First 1
    if ($null -eq $projectFile) { continue }
    $manifestPath = Join-Path $moduleDirectory.FullName 'plugin.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Module '$($moduleDirectory.Name)' is missing plugin.json." }
    $manifest = Read-JsonFile -Path $manifestPath
    foreach ($required in @('id', 'name', 'version', 'assembly', 'entryType', 'targetFramework')) { if ([string]::IsNullOrWhiteSpace([string]$manifest.$required)) { throw "Module '$($moduleDirectory.Name)' plugin.json is missing '$required'." } }
    if (-not $seenIds.Add([string]$manifest.id)) { throw "Duplicate plugin ID in source manifests: $($manifest.id)" }
    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw -Encoding UTF8
    $propertyGroup = $project.Project.PropertyGroup | Where-Object { $_.TargetFramework -or $_.AssemblyName -or $_.Version } | Select-Object -First 1
    $projectTargetFramework = [string]$propertyGroup.TargetFramework
    $projectAssembly = "$([string]$propertyGroup.AssemblyName).dll"
    if ($projectAssembly -and $manifest.assembly -ne $projectAssembly) { throw "plugin.json assembly mismatch for '$($manifest.id)'." }
    if ($projectTargetFramework -and $manifest.targetFramework -ne $projectTargetFramework) { throw "plugin.json targetFramework mismatch for '$($manifest.id)'." }
    if ($propertyGroup.Version -and $manifest.version -ne [string]$propertyGroup.Version) { throw "plugin.json version mismatch for '$($manifest.id)'." }
    $actionFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*Action.cs' -File | Select-Object -First 1
    $fallbackContributionId = ''
    if ($actionFile) { $match = [regex]::Match((Get-Content -LiteralPath $actionFile.FullName -Raw -Encoding UTF8), 'Id\s*=\s*"([^"]+)"'); if ($match.Success) { $fallbackContributionId = $match.Groups[1].Value } }
    $claims = @($manifest.claimedTypes)
    $typeClaims = @($claims | ForEach-Object { [string]$_.typeName } | Where-Object { $_ })
    $firstClaim = @($claims | Select-Object -First 1)
    $contributionId = if ($firstClaim.Count -gt 0 -and $null -ne $firstClaim[0].PSObject.Properties['contributionId']) { [string]$firstClaim[0].contributionId } else { '' }
    if ([string]::IsNullOrWhiteSpace($contributionId)) { $contributionId = $fallbackContributionId }
    if ([string]::IsNullOrWhiteSpace($contributionId)) { throw "Unable to determine contributionId for '$($manifest.id)'." }
    $enabled = $true
    if ($manifest.release -and $null -ne $manifest.release.PSObject.Properties['enabled']) { $enabled = [bool]$manifest.release.enabled }
    $modules += [ordered]@{
        id = [string]$manifest.id; name = [string]$manifest.name; project = ConvertTo-NormalizedRelativePath -BasePath $root -Path $projectFile.FullName; assembly = [string]$manifest.assembly; targetFramework = [string]$manifest.targetFramework; version = [string]$manifest.version; pluginId = [string]$manifest.id; contributionId = $contributionId; entryType = [string]$manifest.entryType; typeClaims = @($typeClaims); capabilities = @($manifest.capabilities); releaseDirectory = $moduleDirectory.Name; description = [string]$manifest.description; author = [string]$manifest.author; license = if ($manifest.license) { [string]$manifest.license } else { 'MIT' }; homepage = [string]$manifest.homepage; icon = $manifest.icon; tags = @($manifest.tags); features = @($manifest.features); enabled = $enabled; apiVersion = [string]$manifest.apiVersion; minHostVersion = [string]$manifest.minHostVersion; maxHostVersion = $manifest.maxHostVersion
    }
}
$registry = [ordered]@{ schemaVersion = 1; sdkApiVersion = '1.4'; minimumHostVersion = '1.8.0-beta.1'; modules = $modules }
Write-JsonFile -Value $registry -Path $OutputPath
Write-Host "Generated $($modules.Count) modules from plugin.json into $OutputPath"
