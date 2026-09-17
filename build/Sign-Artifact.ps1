[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Path,
  [ValidateSet('none','cosign')][string]$Mode = 'none',
  [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Artifact not found: $Path"
}

if ($Mode -eq 'none') {
    if ($OutputPath) {
        [System.IO.File]::WriteAllText($OutputPath, '', (New-Object System.Text.UTF8Encoding($false)))
    }
    Write-Warning 'Signature mode is none. The artifact is not cryptographically signed.'
    exit 0
}

$cosign = Get-Command cosign -ErrorAction SilentlyContinue
if (-not $cosign) {
    throw 'cosign is required for signature mode cosign.'
}

$signaturePath = if ($OutputPath) { $OutputPath } else { "$Path.sig" }
& $cosign.Source sign-blob --yes --bundle $signaturePath $Path
if ($LASTEXITCODE -ne 0) {
    throw "cosign sign-blob failed for $Path"
}

Write-Host "Signature bundle written: $signaturePath"