[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory)][string]$ModuleOutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$plugin = Join-Path $ModuleOutputDirectory 'StarPie.Plugin.TemporaryKeyboard.dll'
$sdk = Join-Path $root "sdk/StarPie.Plugin.Abstractions/bin/$Configuration/net8.0-windows/StarPie.Plugin.Abstractions.dll"
if (-not (Test-Path -LiteralPath $plugin) -or -not (Test-Path -LiteralPath $sdk)) {
    throw 'Build the module and SDK before validating their exact artifacts.'
}
$runRoot = Join-Path $root ('artifacts/module-validation/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$previous = @{}
foreach ($name in @('KEYBOARD_TEST_ROOT', 'KEYBOARD_UI_OUTPUT', 'KEYBOARD_EXPECTED_SDK_SHA256')) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:KEYBOARD_TEST_ROOT = Join-Path $runRoot 'data'
    $env:KEYBOARD_UI_OUTPUT = Join-Path $runRoot 'images'
    $env:KEYBOARD_EXPECTED_SDK_SHA256 = (Get-FileHash -LiteralPath $sdk -Algorithm SHA256).Hash
    $pluginHash = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
    $suites = @(
        @('starpie.plugin.temporarykeyboard', 'ModelTests', 'test_temporary_keyboard_models'),
        @('starpie.plugin.temporarykeyboard-editing', 'EditingTests', 'test_temporary_keyboard_editing'),
        @('starpie.plugin.temporarykeyboard-keyschemes', 'KeySchemeTests', 'test_temporary_keyboard_keyschemes'),
        @('starpie.plugin.temporarykeyboard-dispatch', 'DispatchTests', 'DispatchTests'),
        @('starpie.plugin.temporarykeyboard-ui', 'UiTests', 'UiTests')
    )
    foreach ($suite in $suites) {
        $project = Join-Path $root ("tests/" + $suite[0] + '/' + $suite[1] + '.csproj')
        $output = Join-Path $runRoot $suite[1]
        & dotnet build $project -c $Configuration --nologo "-p:OutDir=$output\\" "-p:SdkCandidatePath=$sdk"
        if ($LASTEXITCODE -ne 0) { throw "Test build failed: $project" }
        $dll = Join-Path $output ($suite[2] + '.dll')
        if ($suite[1] -eq 'UiTests') { & dotnet $dll $plugin $pluginHash }
        else { & dotnet $dll }
        if ($LASTEXITCODE -ne 0) { throw "Test failed: $project" }
    }
    Write-Host "PASS temporary keyboard validation; plugin=$pluginHash SDK=$env:KEYBOARD_EXPECTED_SDK_SHA256; evidence=$runRoot"
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
