[CmdletBinding()]
param(
    [string]$PluginDirectory,
    [string]$CandidateSdkDirectory,
    [string]$BaselineCommit = 'e3aadbbfb656a89ba68c4c211461c3b87e0caece'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $PluginDirectory) { $PluginDirectory = Join-Path $repository 'src/StarPie.Plugin.QrScanner/bin/Release/net8.0-windows' }
$PluginDirectory = [IO.Path]::GetFullPath($PluginDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $PluginDirectory 'StarPie.Plugin.QrScanner.dll'))) { throw 'Build the candidate QR plugin first.' }
if (-not $CandidateSdkDirectory) { $CandidateSdkDirectory = Join-Path $repository 'sdk/StarPie.Plugin.Abstractions/bin/Release/net8.0-windows' }
$CandidateSdkDirectory = [IO.Path]::GetFullPath($CandidateSdkDirectory)
$frozenImageContract = Join-Path $PSScriptRoot 'ImageTools-1.12.cs.txt'
$frozenText = [IO.File]::ReadAllText($frozenImageContract).Replace([string][char]13 + [char]10, [string][char]10)
$frozenHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($frozenText)))
if ($frozenHash -ne '93D4076E83ADDE684DEB846A1D481FF5ABB313EEB3A1A01B63DDCD34759DE034') {
    throw 'Frozen 1.12 contract no longer matches the accepted functional candidate.'
}
$output = Join-Path $repository ('artifacts/qr-sdk-compat/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$sdkPrefix = 'sdk/StarPie.Plugin.Abstractions/'
$baselineFiles = @(git -C $repository ls-tree -r --name-only $BaselineCommit -- $sdkPrefix)
if ($LASTEXITCODE -ne 0 -or $baselineFiles.Count -eq 0) { throw 'Pinned SDK 1.11 baseline is unavailable.' }
foreach ($apiMinor in @(11, 12)) {
    $sdkDirectory = Join-Path $output "sdk$apiMinor"
    New-Item -ItemType Directory -Path $sdkDirectory | Out-Null
    foreach ($relative in $baselineFiles) {
        if ([IO.Path]::GetExtension($relative) -notin @('.cs','.csproj')) { continue }
        $content = git -C $repository show ("${BaselineCommit}:" + $relative)
        if ($LASTEXITCODE -ne 0) { throw "Cannot read pinned SDK source: $relative" }
        [IO.File]::WriteAllText((Join-Path $sdkDirectory ([IO.Path]::GetFileName($relative))),
            (($content -join [Environment]::NewLine) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }
    $apiPath = Join-Path $sdkDirectory 'PluginApi.cs'
    $apiText = [IO.File]::ReadAllText($apiPath)
    if ($apiText -notmatch 'ApiVersionMinor = 11;' -or $apiText -notmatch 'ApiVersion = "1.11";') {
        throw 'Pinned source must describe SDK 1.11.'
    }
    if ($apiMinor -eq 12) {
        Copy-Item -LiteralPath $frozenImageContract -Destination (Join-Path $sdkDirectory 'ImageTools.cs')
        $apiText = $apiText.Replace('ApiVersionMinor = 11;', 'ApiVersionMinor = 12;').Replace('ApiVersion = "1.11";', 'ApiVersion = "1.12";')
        [IO.File]::WriteAllText($apiPath, $apiText, [Text.UTF8Encoding]::new($false))
    }
}
$sdk12Project = Join-Path $output 'sdk12/StarPie.Plugin.Abstractions.csproj'
$probeBuildArgs = @('build', (Join-Path $PSScriptRoot 'Sdk12HostProbe.csproj'), '-c', 'Release', '--nologo',
    "-p:CompatibilitySdkProject=$sdk12Project", "-p:CompatibilityOutputRoot=$output", "-p:QrPluginOutput=$PluginDirectory")
& dotnet @probeBuildArgs
if ($LASTEXITCODE -ne 0) { throw 'SDK 1.12 binary probe build failed.' }
$probeDll = Join-Path $output 'probe12/Release/net8.0-windows/QrSdk12HostProbe.dll'
& dotnet $probeDll $PluginDirectory
if ($LASTEXITCODE -ne 0) { throw 'Candidate failed against the actual old SDK binary.' }
$sdk11Project = Join-Path $output 'sdk11/StarPie.Plugin.Abstractions.csproj'
$legacyBuildArgs = @('build', (Join-Path $PSScriptRoot 'LegacyPlugin.csproj'), '-c', 'Release', '--nologo',
    "-p:CompatibilitySdkProject=$sdk11Project", "-p:CompatibilityOutputRoot=$output")
& dotnet @legacyBuildArgs
if ($LASTEXITCODE -ne 0) { throw 'SDK 1.11 legacy plugin build failed.' }
$legacyDll = Join-Path $output 'legacy11/Release/net8.0-windows/QrSdk11LegacyFixture.dll'
[IO.File]::WriteAllText((Join-Path $repository 'artifacts/qr-sdk-compat/last-legacy-fixture.txt'), $legacyDll, [Text.UTF8Encoding]::new($false))
Write-Host "LEGACY_FIXTURE=$legacyDll"
$surfaceBuildArgs = @('build', (Join-Path $PSScriptRoot 'ApiSurfaceProbe.csproj'), '-c', 'Release', '--nologo',
    "-p:CompatibilityOutputRoot=$output")
& dotnet @surfaceBuildArgs
if ($LASTEXITCODE -ne 0) { throw 'Public SDK signature probe build failed.' }
$surfaceDll = Join-Path $output 'api-surface/Release/net8.0-windows/QrSdkApiSurfaceProbe.dll'
$baselineSdkDll = Join-Path $output 'sdk11/bin/Release/net8.0-windows/StarPie.Plugin.Abstractions.dll'
$candidateSdkDll = Join-Path $CandidateSdkDirectory 'StarPie.Plugin.Abstractions.dll'
& dotnet $surfaceDll $baselineSdkDll $candidateSdkDll
if ($LASTEXITCODE -ne 0) { throw 'SDK candidate removed existing public signatures.' }
$baselineSdk12Dll = Join-Path $output 'sdk12/bin/Release/net8.0-windows/StarPie.Plugin.Abstractions.dll'
& dotnet $surfaceDll $baselineSdk12Dll $candidateSdkDll
if ($LASTEXITCODE -ne 0) { throw 'SDK candidate removed the earlier image-service contract.' }
Write-Host "Evidence directory: $output"
