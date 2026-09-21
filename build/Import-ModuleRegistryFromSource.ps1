[CmdletBinding()]
param([string]$OutputPath = 'module-registry.json')

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$root = Get-RepositoryRoot
$sourceRoot = Join-Path $root 'src'
$modules = @()

foreach ($moduleDirectory in Get-ChildItem -LiteralPath $sourceRoot -Directory |
    Where-Object { $_.Name -like 'StarPie.Plugin.*' -and $_.Name -ne 'StarPie.Plugin.Abstractions' } |
    Sort-Object Name) {
    $projectFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*.csproj' -File | Select-Object -First 1
    if ($null -eq $projectFile) { continue }

    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw -Encoding UTF8
    $propertyGroup = $project.Project.PropertyGroup |
        Where-Object { $_.TargetFramework -or $_.AssemblyName -or $_.Version } |
        Select-Object -First 1

    $metadata = @{}
    foreach ($item in @($project.Project.ItemGroup.AssemblyMetadata)) {
        if ($null -eq $item) { continue }
        $metadata[$item.Include] = [string]$item.Value
    }

    $actionFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*Action.cs' -File | Select-Object -First 1
    if ($null -eq $actionFile) { throw "No action source found for '$($moduleDirectory.Name)'." }
    $contributionMatch = [regex]::Match((Get-Content -LiteralPath $actionFile.FullName -Raw -Encoding UTF8), 'Id\s*=\s*"([^"]+)"')
    if (-not $contributionMatch.Success) { throw "No ActionDescriptor.Id found in '$($actionFile.FullName)'." }

    $pluginFile = Get-ChildItem -LiteralPath $moduleDirectory.FullName -Filter '*Plugin.cs' -File | Select-Object -First 1
    if ($null -eq $pluginFile) { throw "No plugin entry source found for '$($moduleDirectory.Name)'." }
    $pluginText = Get-Content -LiteralPath $pluginFile.FullName -Raw -Encoding UTF8
    $entryMatch = [regex]::Match($pluginText, 'public\s+sealed\s+class\s+([A-Za-z_][A-Za-z0-9_]*)')
    if (-not $entryMatch.Success) { throw "No public plugin class found in '$($pluginFile.FullName)'." }

    $rootNamespace = [string]$propertyGroup.RootNamespace
    if ([string]::IsNullOrWhiteSpace($rootNamespace)) {
        $namespaceMatch = [regex]::Match($pluginText, 'namespace\s+([A-Za-z0-9_.]+)\s*;')
        $rootNamespace = if ($namespaceMatch.Success) { $namespaceMatch.Groups[1].Value } else { $moduleDirectory.Name }
    }

    $typeClaims = @(([string]$metadata['StarPiePluginTypeClaims']) -split ';' | ForEach-Object {
        $pair = $_.Trim()
        if ($pair.Length -eq 0) { return }
        ($pair -split '=', 2)[0].Trim()
    } | Where-Object { $_ } | Sort-Object -Unique)

    $capabilities = @(([string]$metadata['StarPiePluginCapabilities']) -split ';' |
        ForEach-Object { $_.Trim() } | Where-Object { $_ })

    $moduleKind = if ([string]$metadata['StarPiePluginId'] -like 'starpie.builtin.*') { 'builtin' } else { 'plugin' }
    $moduleEntry = [ordered]@{
        id = [string]$metadata['StarPiePluginId']
        name = [string]$metadata['StarPiePluginName']
        project = (ConvertTo-NormalizedRelativePath -BasePath $root -Path $projectFile.FullName)
        assembly = "$([string]$propertyGroup.AssemblyName).dll"
        targetFramework = [string]$propertyGroup.TargetFramework
        version = [string]$propertyGroup.Version
        pluginId = [string]$metadata['StarPiePluginId']
        contributionId = $contributionMatch.Groups[1].Value
        entryType = "$rootNamespace.$($entryMatch.Groups[1].Value)"
        typeClaims = $typeClaims
        capabilities = $capabilities
        releaseDirectory = $moduleDirectory.Name
        description = [string]$propertyGroup.Description
        author = [string]$propertyGroup.Company
        license = if ($metadata['StarPiePluginLicense']) { [string]$metadata['StarPiePluginLicense'] } else { 'MIT' }
        homepage = [string]$metadata['StarPiePluginHomepage']
        icon = $null
        tags = @('official', $moduleKind)
        enabled = $true
    }

    $moduleApiVersion = [string]$metadata['StarPiePluginApiVersion']
    if (-not [string]::IsNullOrWhiteSpace($moduleApiVersion)) {
        $moduleEntry.apiVersion = $moduleApiVersion
    }

    $moduleMinHostVersion = [string]$metadata['StarPiePluginMinHostVersion']
    if (-not [string]::IsNullOrWhiteSpace($moduleMinHostVersion)) {
        $moduleEntry.minHostVersion = $moduleMinHostVersion
    }

    $modules += $moduleEntry
}

$registry = [ordered]@{
    schemaVersion = 1
    sdkApiVersion = '1.4'
    minimumHostVersion = '1.8.0-beta.1'
    modules = $modules
}

Write-JsonFile -Value $registry -Path $OutputPath
Write-Host "Wrote $($modules.Count) modules to $OutputPath"