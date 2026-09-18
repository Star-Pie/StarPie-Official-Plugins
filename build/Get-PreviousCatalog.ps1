[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('stable','beta')][string]$Channel,
  [Parameter(Mandatory)][string]$Repository,
  [string]$OutputDirectory = 'artifacts/previous-catalog'
)

$ErrorActionPreference = 'Stop'

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$catalogPath = Join-Path $outputRoot 'module-catalog.json'

$releaseJson = gh release list `
  --repo $Repository `
  --exclude-drafts `
  --limit 1000 `
  --json tagName,isPrerelease,publishedAt,createdAt
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($releaseJson)) {
  $releases = @($releaseJson | ConvertFrom-Json)
  $releases = @($releases | Sort-Object -Property publishedAt -Descending)

  foreach ($release in $releases) {
    $tag = [string]$release.tagName
    if ([string]::IsNullOrWhiteSpace($tag)) { continue }

    gh release download $tag `
      --repo $Repository `
      --pattern 'module-catalog.json' `
      --dir $outputRoot `
      --clobber 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $catalogPath)) { continue }

    try {
      $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    }
    catch {
      Write-Warning "Skipping release '$tag': catalog is not valid JSON."
      continue
    }

    if ($catalog.releaseTag -ne $tag) {
      Write-Warning "Skipping release '$tag': catalog releaseTag is '$($catalog.releaseTag)'."
      continue
    }
    if ($catalog.releaseChannel -ne $Channel) {
      Write-Host "Skipping release '$tag': channel '$($catalog.releaseChannel)' does not match '$Channel'."
      continue
    }

    Write-Output $catalogPath
    exit 0
  }
}

if (Test-Path -LiteralPath 'catalog/module-catalog.json') {
  try {
    $bootstrapCatalog = Get-Content -LiteralPath 'catalog/module-catalog.json' -Raw | ConvertFrom-Json
    if ($bootstrapCatalog.releaseChannel -eq $Channel) {
      Copy-Item -LiteralPath 'catalog/module-catalog.json' -Destination $catalogPath -Force
      Write-Output $catalogPath
      exit 0
    }
  }
  catch {
    Write-Warning 'Ignoring invalid bootstrap catalog/module-catalog.json.'
  }
}

if (Test-Path -LiteralPath $catalogPath) {
  Remove-Item -LiteralPath $catalogPath -Force
}
Write-Output ''