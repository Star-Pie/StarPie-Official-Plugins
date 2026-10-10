[CmdletBinding()]
param(
    [string]$RepositoryRoot = '.'
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$failures = @()

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        $script:failures += $Message
        Write-Host " [FAIL] $Message" -ForegroundColor Red
    }
    else {
        Write-Host " [PASS] $Message" -ForegroundColor Green
    }
}

Write-Host "=== Validating starpie.plugin.desktoppet Plugin Prototype ===" -ForegroundColor Cyan

# ---------------------------------------------------------------------------------
# 1. Check plugin.json
# ---------------------------------------------------------------------------------
$pluginJsonFile = Join-Path $root 'src/StarPie.Plugin.DesktopPet/plugin.json'
Assert-Condition (Test-Path -LiteralPath $pluginJsonFile) "plugin.json exists"
if (Test-Path -LiteralPath $pluginJsonFile) {
    $pluginJson = Get-Content -LiteralPath $pluginJsonFile -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($pluginJson.id -eq 'starpie.plugin.desktoppet') "plugin.json id is 'starpie.plugin.desktoppet'"
    Assert-Condition ($pluginJson.author -eq 'SoftBlack42') "plugin author is SoftBlack42"
    Assert-Condition ($pluginJson.name -eq '桌宠') "plugin.json name is '桌宠'"
    Assert-Condition ($pluginJson.version -eq '0.1.6') "plugin.json version is '0.1.6'"
    Assert-Condition ($pluginJson.apiVersion -eq '1.11') "plugin.json apiVersion is '1.11'"
    Assert-Condition ($pluginJson.minHostVersion -eq '1.8.0-beta.7') "plugin.json minHostVersion is '1.8.0-beta.7'"
    Assert-Condition ($pluginJson.assembly -eq 'StarPie.Plugin.DesktopPet.dll') "plugin.json assembly is 'StarPie.Plugin.DesktopPet.dll'"
    Assert-Condition ($pluginJson.entryType -eq 'StarPie.Plugin.DesktopPet.DesktopPetPlugin') "plugin.json entryType is correct"
    Assert-Condition (@($pluginJson.capabilities).Count -eq 2 -and @($pluginJson.capabilities) -contains 'Ui' -and @($pluginJson.capabilities) -contains 'Wheel') "plugin.json capabilities contains 'Ui' and 'Wheel'"
    Assert-Condition (@($pluginJson.claimedTypes).Count -eq 0) "plugin.json claimedTypes is empty (does not claim top-level action types)"
}

# ---------------------------------------------------------------------------------
# 2. Check csproj constraints (zero NuGet, private=false, framework reference)
# ---------------------------------------------------------------------------------
$csprojPath = Join-Path $root 'src/StarPie.Plugin.DesktopPet/StarPie.Plugin.DesktopPet.csproj'
Assert-Condition (Test-Path -LiteralPath $csprojPath) "StarPie.Plugin.DesktopPet.csproj exists"

if (Test-Path -LiteralPath $csprojPath) {
    $csprojXml = [xml][System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)
    $versionNode = $csprojXml.SelectSingleNode("//Version")
    Assert-Condition ($null -ne $versionNode -and $versionNode.InnerText.Trim() -eq '0.1.6') "StarPie.Plugin.DesktopPet.csproj Version is '0.1.6'"

    $packageRefs = $csprojXml.SelectNodes("//PackageReference")
    Assert-Condition ($packageRefs.Count -eq 0) "StarPie.Plugin.DesktopPet.csproj has zero NuGet package references"

    $frameworkRefs = $csprojXml.SelectNodes("//FrameworkReference")
    $hasWpfRef = $false
    foreach ($fr in $frameworkRefs) {
        if ($fr.GetAttribute('Include') -eq 'Microsoft.WindowsDesktop.App.WPF') { $hasWpfRef = $true }
    }
    Assert-Condition $hasWpfRef "StarPie.Plugin.DesktopPet.csproj references Microsoft.WindowsDesktop.App.WPF framework"

    $projRefs = $csprojXml.SelectNodes("//ProjectReference")
    $hasSdkRef = $false
    $privateFalse = $false
    foreach ($ref in $projRefs) {
        if ($ref.GetAttribute('Include') -match 'StarPie\.Plugin\.Abstractions\.csproj') {
            $hasSdkRef = $true
            $privateNode = $ref.SelectSingleNode('Private')
            if ($null -ne $privateNode -and $privateNode.InnerText.Trim() -eq 'false') {
                $privateFalse = $true
            }
        }
    }
    Assert-Condition $hasSdkRef "StarPie.Plugin.DesktopPet.csproj references StarPie.Plugin.Abstractions"
    Assert-Condition $privateFalse "StarPie.Plugin.DesktopPet.csproj sets Private=false on StarPie.Plugin.Abstractions"
}

# ---------------------------------------------------------------------------------
# 3. Build DesktopPet in Release mode
# ---------------------------------------------------------------------------------
Write-Host "--- Building DesktopPet Release ---" -ForegroundColor Yellow
$buildOutput = & dotnet build (Join-Path $root 'src/StarPie.Plugin.DesktopPet/StarPie.Plugin.DesktopPet.csproj') -c Release --nologo 2>&1
$buildExit = $LASTEXITCODE
Assert-Condition ($buildExit -eq 0) "DesktopPet builds with exit code 0"

# ---------------------------------------------------------------------------------
# 4. Verify output directory isolation
# ---------------------------------------------------------------------------------
$outDir = Join-Path $root 'src/StarPie.Plugin.DesktopPet/bin/Release/net8.0-windows'
$outDll = Join-Path $outDir 'StarPie.Plugin.DesktopPet.dll'
Assert-Condition (Test-Path -LiteralPath $outDll) "Output DLL exists at $outDll"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'StarPie.Plugin.Abstractions.dll'))) "Output directory does NOT contain StarPie.Plugin.Abstractions.dll"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'StarPie.dll'))) "Output directory does NOT contain StarPie.dll"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'idle.png'))) "Output directory does NOT contain unapproved asset idle.png"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'blink.png'))) "Output directory does NOT contain unapproved asset blink.png"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'react.png'))) "Output directory does NOT contain unapproved asset react.png"
Assert-Condition (-not (Test-Path (Join-Path $outDir 'drag.png'))) "Output directory does NOT contain unapproved asset drag.png"

# ---------------------------------------------------------------------------------
# 5. Verify AssemblyMetadata attributes match plugin.json
# ---------------------------------------------------------------------------------
if (Test-Path -LiteralPath $outDll) {
    $bytes = [System.IO.File]::ReadAllBytes($outDll)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $attrs = $asm.GetCustomAttributes([System.Reflection.AssemblyMetadataAttribute], $false)
    $attrDict = @{}
    foreach ($a in $attrs) { $attrDict[$a.Key] = $a.Value }

    Assert-Condition ($attrDict['StarPiePluginId'] -eq 'starpie.plugin.desktoppet') "Assembly metadata StarPiePluginId is 'starpie.plugin.desktoppet'"
    Assert-Condition ($asm.GetCustomAttributes([System.Reflection.AssemblyCompanyAttribute], $false)[0].Company -eq 'SoftBlack42') "bare DLL author is SoftBlack42"
    Assert-Condition ($attrDict['StarPiePluginName'] -eq '桌宠') "Assembly metadata StarPiePluginName is '桌宠'"
    Assert-Condition ($attrDict['StarPiePluginCapabilities'] -eq 'Ui;Wheel') "Assembly metadata StarPiePluginCapabilities is 'Ui;Wheel'"
    Assert-Condition ($attrDict['StarPiePluginApiVersion'] -eq '1.11') "Assembly metadata StarPiePluginApiVersion is '1.11'"
    Assert-Condition ($attrDict['StarPiePluginMinHostVersion'] -eq '1.8.0-beta.7') "Assembly metadata StarPiePluginMinHostVersion is '1.8.0-beta.7'"
}

# ---------------------------------------------------------------------------------
# 6. Build and run deterministic test suite
# ---------------------------------------------------------------------------------
Write-Host "--- Running DesktopPet Tests ---" -ForegroundColor Yellow
$testProj = Join-Path $root 'tests/starpie.plugin.desktoppet/DesktopPetTests.csproj'
$testOutput = & dotnet run --project $testProj -c Release --nologo 2>&1
$testExit = $LASTEXITCODE
Write-Host ($testOutput -join "`n")
Assert-Condition ($testExit -eq 0) "DesktopPet deterministic test suite passed with exit code 0"

Write-Host "--------------------------------------------------------"
if ($failures.Count -eq 0) {
    Write-Host "All StarPie.Plugin.DesktopPet verification checks PASSED!" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "$($failures.Count) verification assertions FAILED!" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    exit 1
}
