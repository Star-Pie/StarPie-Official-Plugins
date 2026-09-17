[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$MetadataDirectory,
  [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'StarPie.Modules.psm1') -Force

if (-not (Test-Path -LiteralPath $MetadataDirectory)) {
    throw "Package metadata directory not found: $MetadataDirectory"
}

$packages = @()
foreach ($file in Get-ChildItem -LiteralPath $MetadataDirectory -Filter '*.json' -File | Sort-Object Name) {
    $packages += Read-JsonFile -Path $file.FullName
}

if ($packages.Count -eq 0) {
    throw "No package metadata files found in $MetadataDirectory"
}

Write-JsonFile -Value @($packages) -Path $OutputPath
Write-Host "Merged $($packages.Count) package metadata records into $OutputPath"