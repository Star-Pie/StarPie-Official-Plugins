[CmdletBinding()]
param(
  [string]$ModulesJson,
  [string[]]$ModuleIds,
  [string]$Configuration = 'Release',
  [switch]$RunTests,
  [switch]$AllowMissingProject
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

$root = Get-RepositoryRoot
$registry = Get-ModuleRegistry
$modules = if ($ModulesJson) {
    $jsonText = if (Test-Path -LiteralPath $ModulesJson) { Get-Content -LiteralPath $ModulesJson -Raw } else { $ModulesJson }
    @(Select-ModulesFromJson -ModulesJson $jsonText -Registry $registry)
}
elseif ($ModuleIds) {
    @($ModuleIds | ForEach-Object { Get-ModuleById -ModuleId $_ -Registry $registry })
}
else {
    @(Get-EnabledModules -Registry $registry)
}

if ($modules.Count -eq 0) {
    Write-Host 'No official modules selected; nothing to build.'
    exit 0
}

foreach ($module in $modules) {
    $project = Resolve-ModuleProjectPath -Module $module
    if (-not (Test-Path -LiteralPath $project)) {
        if ($AllowMissingProject) {
            Write-Warning "Skipping missing project for module '$($module.id)': $project"
            continue
        }
        throw "Project not found for module '$($module.id)': $project"
    }

    Write-Host "Building module $($module.id) [$($module.version)] from $project"
    & dotnet build $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for module '$($module.id)'." }

    if ($RunTests) {
        $testsRoot = Join-Path $root 'tests'
        $candidates = @()
        if (Test-Path -LiteralPath $testsRoot) {
            $candidates = @(Get-ChildItem -LiteralPath $testsRoot -Recurse -Filter '*.csproj' -File | Where-Object {
                $_.Name -like "*$($module.name)*Tests*.csproj" -or $_.FullName -match [regex]::Escape($module.id)
            })
        }

        if ($candidates.Count -eq 0) {
            Write-Host "No .NET test project found for module '$($module.id)'."
            continue
        }

        foreach ($testProject in $candidates) {
            Write-Host "Testing module $($module.id): $($testProject.FullName)"
            & dotnet test $testProject.FullName -c $Configuration --nologo --no-build
            if ($LASTEXITCODE -ne 0) { throw "dotnet test failed for module '$($module.id)'." }
        }
    }
}