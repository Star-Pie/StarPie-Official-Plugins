[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ModuleOutputDirectory = '',
    [string]$SdkCandidatePath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($ModuleOutputDirectory)) {
    $ModuleOutputDirectory = Join-Path $repoRoot "src\StarPie.Plugin.EmojiPicker\bin\$Configuration\net8.0-windows"
}
if ([string]::IsNullOrWhiteSpace($SdkCandidatePath)) {
    $SdkCandidatePath = Join-Path $repoRoot "sdk\StarPie.Plugin.Abstractions\bin\$Configuration\net8.0-windows\StarPie.Plugin.Abstractions.dll"
}

$pluginDll = Join-Path $ModuleOutputDirectory 'StarPie.Plugin.EmojiPicker.dll'
$manifestPath = Join-Path $ModuleOutputDirectory 'plugin.json'
$leakedSdk = Join-Path $ModuleOutputDirectory 'StarPie.Plugin.Abstractions.dll'

if (-not (Test-Path -LiteralPath $pluginDll)) { throw "Missing module assembly: $pluginDll" }
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing module manifest: $manifestPath" }
if (Test-Path -LiteralPath $leakedSdk) { throw "Forbidden SDK leakage detected in module output: $leakedSdk" }
if (-not (Test-Path -LiteralPath $SdkCandidatePath)) { throw "Missing SDK candidate assembly: $SdkCandidatePath" }

$pluginHash = (Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256).Hash
$sdkHash = (Get-FileHash -LiteralPath $SdkCandidatePath -Algorithm SHA256).Hash
Write-Host "Plugin DLL SHA-256 : $pluginHash ($pluginDll)"
Write-Host "SDK DLL SHA-256    : $sdkHash ($SdkCandidatePath)"

$testProj = Join-Path $PSScriptRoot 'EmojiPicker.Tests.csproj'
dotnet build $testProj -c $Configuration "-p:SdkCandidatePath=$SdkCandidatePath" --nologo
if ($LASTEXITCODE -ne 0) {
    throw "EmojiPicker test project build failed with exit code $LASTEXITCODE"
}

$testDll = Join-Path $PSScriptRoot "bin\$Configuration\net8.0-windows\EmojiPicker.Tests.dll"
if (-not (Test-Path -LiteralPath $testDll)) {
    throw "Built test runner not found: $testDll"
}

dotnet $testDll $pluginDll $SdkCandidatePath
if ($LASTEXITCODE -ne 0) {
    throw "EmojiPicker offline validation suite failed with exit code $LASTEXITCODE"
}
