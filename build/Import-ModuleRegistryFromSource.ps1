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
    $runtime = if ($manifest.runtime) { ([string]$manifest.runtime).Trim().ToLowerInvariant() } else { 'assembly' }
    if ($runtime -notin @('assembly', 'process')) { throw "Module '$($moduleDirectory.Name)' declares unsupported runtime '$runtime'." }
    # 进程插件不是 .NET 程序集：没有 assembly / entryType / targetFramework，取而代之的是 command + actions。
    $requiredFields = if ($runtime -eq 'process') { @('id', 'name', 'version') } else { @('id', 'name', 'version', 'assembly', 'entryType', 'targetFramework') }
    foreach ($required in $requiredFields) { if ([string]::IsNullOrWhiteSpace([string]$manifest.$required)) { throw "Module '$($moduleDirectory.Name)' plugin.json is missing '$required'." } }
    if ($runtime -eq 'process') {
        if (-not $manifest.command -or @($manifest.command).Count -eq 0) { throw "Module '$($moduleDirectory.Name)' declares runtime=process but has no command." }
        if (-not $manifest.actions -or @($manifest.actions).Count -eq 0) { throw "Module '$($moduleDirectory.Name)' declares runtime=process but has no actions." }
    }
    if (-not $seenIds.Add([string]$manifest.id)) { throw "Duplicate plugin ID in source manifests: $($manifest.id)" }
    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw -Encoding UTF8
    $propertyGroup = $project.Project.PropertyGroup | Where-Object { $_.TargetFramework -or $_.AssemblyName -or $_.Version } | Select-Object -First 1
    $projectTargetFramework = [string]$propertyGroup.TargetFramework
    $projectAssembly = "$([string]$propertyGroup.AssemblyName).dll"
    if ($runtime -eq 'assembly') {
        if ($projectAssembly -and $manifest.assembly -ne $projectAssembly) { throw "plugin.json assembly mismatch for '$($manifest.id)'." }
        if ($projectTargetFramework -and $manifest.targetFramework -ne $projectTargetFramework) { throw "plugin.json targetFramework mismatch for '$($manifest.id)'." }
    }
    if ($propertyGroup.Version -and $manifest.version -ne [string]$propertyGroup.Version) { throw "plugin.json version mismatch for '$($manifest.id)'." }
    $actionFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*Action.cs' -File | Select-Object -First 1
    $fallbackContributionId = ''
    if ($actionFile) { $match = [regex]::Match((Get-Content -LiteralPath $actionFile.FullName -Raw -Encoding UTF8), 'Id\s*=\s*"([^"]+)"'); if ($match.Success) { $fallbackContributionId = $match.Groups[1].Value } }
    # 进程插件没有 Action.cs，贡献点 ID 只能来自清单里的 actions[0].id。
    if ($runtime -eq 'process' -and [string]::IsNullOrWhiteSpace($fallbackContributionId)) { $firstAction = @($manifest.actions | Select-Object -First 1); if ($firstAction.Count -gt 0) { $fallbackContributionId = [string]$firstAction[0].id } }
    $claims = @($manifest.claimedTypes)
    $typeClaims = @($claims | ForEach-Object { [string]$_.typeName } | Where-Object { $_ })
    $firstClaim = @($claims | Select-Object -First 1)
    $contributionId = if ($firstClaim.Count -gt 0 -and $null -ne $firstClaim[0].PSObject.Properties['contributionId']) { [string]$firstClaim[0].contributionId } else { '' }
    if ([string]::IsNullOrWhiteSpace($contributionId)) { $contributionId = $fallbackContributionId }
    if ([string]::IsNullOrWhiteSpace($contributionId)) { throw "Unable to determine contributionId for '$($manifest.id)'." }
    $enabled = $true
    if ($manifest.release -and $null -ne $manifest.release.PSObject.Properties['enabled']) { $enabled = [bool]$manifest.release.enabled }
    $modules += [ordered]@{
        id = [string]$manifest.id; name = [string]$manifest.name; project = ConvertTo-NormalizedRelativePath -BasePath $root -Path $projectFile.FullName; assembly = [string]$manifest.assembly; targetFramework = [string]$manifest.targetFramework; version = [string]$manifest.version; pluginId = [string]$manifest.id; contributionId = $contributionId; entryType = [string]$manifest.entryType; typeClaims = @($typeClaims); capabilities = @($manifest.capabilities); releaseDirectory = $moduleDirectory.Name; description = [string]$manifest.description; author = [string]$manifest.author; license = if ($manifest.license) { [string]$manifest.license } else { 'MIT' }; homepage = [string]$manifest.homepage; icon = $manifest.icon; tags = @($manifest.tags); features = @($manifest.features); enabled = $enabled; apiVersion = [string]$manifest.apiVersion; minHostVersion = [string]$manifest.minHostVersion; maxHostVersion = $manifest.maxHostVersion; runtime = $runtime; command = @($manifest.command); actions = @($manifest.actions)
    }
}
$registry = [ordered]@{ schemaVersion = 1; sdkApiVersion = '1.14'; minimumHostVersion = '1.8.0-beta.1'; modules = $modules }
Write-JsonFile -Value $registry -Path $OutputPath
Write-Host "Generated $($modules.Count) modules from plugin.json into $OutputPath"
