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

Write-Host "=== Validating starpie.plugin.keypadlayer Module Contract ===" -ForegroundColor Cyan

# 1. Check module-registry.json
$registryFile = Join-Path $root 'module-registry.json'
Assert-Condition (Test-Path -LiteralPath $registryFile) "module-registry.json exists"

$registry = Get-Content -LiteralPath $registryFile -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Condition ($registry.schemaVersion -eq 1) "Registry schemaVersion is 1"
Assert-Condition ($registry.sdkApiVersion -eq '1.4') "Registry top-level sdkApiVersion remains '1.4' (INV-OLD-MODULE-COMPAT)"
Assert-Condition ($registry.minimumHostVersion -eq '1.8.0-beta.1') "Registry top-level minimumHostVersion remains '1.8.0-beta.1' (INV-OLD-MODULE-COMPAT)"

$module = @($registry.modules) | Where-Object { $_.id -eq 'starpie.plugin.keypadlayer' } | Select-Object -First 1
Assert-Condition ($null -ne $module) "starpie.plugin.keypadlayer is registered in module-registry.json"

if ($module) {
    Assert-Condition ($module.name -eq '按键映射') "Module name is '按键映射'"
    Assert-Condition ($module.project -eq 'src/StarPie.Plugin.KeypadLayer/StarPie.Plugin.KeypadLayer.csproj') "Module project path is correct"
    Assert-Condition ($module.assembly -eq 'StarPie.Plugin.KeypadLayer.dll') "Module assembly is 'StarPie.Plugin.KeypadLayer.dll'"
    Assert-Condition ($module.targetFramework -eq 'net8.0-windows') "Module targetFramework is 'net8.0-windows'"
    Assert-Condition ($module.version -eq '1.0.1') "Module version is '1.0.1'"
    Assert-Condition ($module.pluginId -eq 'starpie.plugin.keypadlayer') "Module pluginId is 'starpie.plugin.keypadlayer' (INV-MODULE-IDENTITY)"
    Assert-Condition ($module.contributionId -eq 'keypadLayer') "Module contributionId is 'keypadLayer' (INV-MODULE-IDENTITY)"
    Assert-Condition ($module.entryType -eq 'StarPie.Plugin.KeypadLayer.KeypadLayerPlugin') "Module entryType is 'StarPie.Plugin.KeypadLayer.KeypadLayerPlugin'"
    Assert-Condition (@($module.typeClaims).Count -eq 0) "Module typeClaims is empty (INV-MODULE-IDENTITY)"
    Assert-Condition (@($module.capabilities).Count -eq 1 -and @($module.capabilities)[0] -eq 'InputRemapping') "Module capabilities contains only 'InputRemapping' (INV-MODULE-IDENTITY)"
    Assert-Condition ($module.apiVersion -eq '1.7') "Module declared apiVersion is '1.7' (INV-OLD-MODULE-COMPAT)"
    Assert-Condition ($module.minHostVersion -eq '1.8.0-beta.3') "Module declared minHostVersion is '1.8.0-beta.3' (INV-OLD-MODULE-COMPAT)"
    Assert-Condition ($module.enabled -eq $true) "Module is enabled"
}

# 2. Check StarPie.OfficialPlugins.sln
$slnFile = Join-Path $root 'StarPie.OfficialPlugins.sln'
Assert-Condition (Test-Path -LiteralPath $slnFile) "StarPie.OfficialPlugins.sln exists"
$slnText = Get-Content -LiteralPath $slnFile -Raw
Assert-Condition ($slnText -match 'StarPie\.Plugin\.KeypadLayer\.csproj') "StarPie.OfficialPlugins.sln contains StarPie.Plugin.KeypadLayer project"

# 3. Check csproj and narrowing constraints (INV-NARROW-PLUGIN)
$csprojPath = Join-Path $root 'src/StarPie.Plugin.KeypadLayer/StarPie.Plugin.KeypadLayer.csproj'
Assert-Condition (Test-Path -LiteralPath $csprojPath) "KeypadLayer.csproj exists"

if (Test-Path -LiteralPath $csprojPath) {
    $csprojXml = [xml](Get-Content -LiteralPath $csprojPath -Raw)
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
    Assert-Condition $hasSdkRef "KeypadLayer.csproj references StarPie.Plugin.Abstractions"
    Assert-Condition $privateFalse "KeypadLayer.csproj sets Private=false on StarPie.Plugin.Abstractions"

    $packageRefs = $csprojXml.SelectNodes("//PackageReference")
    Assert-Condition ($packageRefs.Count -eq 0) "KeypadLayer.csproj has zero NuGet package references (INV-NARROW-PLUGIN)"

    # Check AssemblyMetadata
    $metaNodes = $csprojXml.SelectNodes("//AssemblyMetadata")
    $metaDict = @{}
    foreach ($m in $metaNodes) {
        $metaDict[$m.GetAttribute('Include')] = $m.GetAttribute('Value')
    }
    Assert-Condition ($metaDict['StarPiePluginId'] -eq 'starpie.plugin.keypadlayer') "AssemblyMetadata StarPiePluginId is 'starpie.plugin.keypadlayer'"
    Assert-Condition ($metaDict['StarPiePluginName'] -eq '按键映射') "AssemblyMetadata StarPiePluginName is '按键映射'"
    Assert-Condition ($metaDict['StarPiePluginCapabilities'] -eq 'InputRemapping') "AssemblyMetadata StarPiePluginCapabilities is 'InputRemapping'"
    Assert-Condition ($metaDict['StarPiePluginApiVersion'] -eq '1.7') "AssemblyMetadata StarPiePluginApiVersion is '1.7'"
    Assert-Condition ($metaDict['StarPiePluginMinHostVersion'] -eq '1.8.0-beta.3') "AssemblyMetadata StarPiePluginMinHostVersion is '1.8.0-beta.3'"
    Assert-Condition (-not $metaDict.ContainsKey('StarPiePluginTypeClaims')) "AssemblyMetadata StarPiePluginTypeClaims is absent (typeClaims empty)"
}

# 4. Check source files for forbidden patterns (INV-NARROW-PLUGIN)
$srcDir = Join-Path $root 'src/StarPie.Plugin.KeypadLayer'
$csFiles = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' -File)
Assert-Condition ($csFiles.Count -ge 3) "Found expected C# source files in KeypadLayer"

foreach ($cs in $csFiles) {
    $text = Get-Content -LiteralPath $cs.FullName -Raw
    Assert-Condition ($text -notmatch 'DllImport') "$($cs.Name): No DllImport (INV-NARROW-PLUGIN)"
    Assert-Condition ($text -notmatch 'user32') "$($cs.Name): No user32 reference (INV-NARROW-PLUGIN)"
    Assert-Condition ($text -notmatch 'SendInput') "$($cs.Name): No SendInput call (INV-NARROW-PLUGIN)"
    Assert-Condition ($text -notmatch 'SetWindowsHookEx') "$($cs.Name): No global hook (INV-NARROW-PLUGIN)"
    Assert-Condition ($text -notmatch 'StarPie\.dll') "$($cs.Name): No reference to StarPie.dll (INV-NARROW-PLUGIN)"
}

# 5. Check KeypadLayerAction.cs (INV-DEFAULT-PRESET, INV-KEYMAP-OPAQUE, INV-TOGGLE, INV-I18N)
$actionFile = Join-Path $srcDir 'KeypadLayerAction.cs'
Assert-Condition (Test-Path -LiteralPath $actionFile) "KeypadLayerAction.cs exists"
if (Test-Path -LiteralPath $actionFile) {
    $actionText = Get-Content -LiteralPath $actionFile -Raw
    $expectedPreset = 'v1|Q:Num7,W:Num8,E:Num9,A:Num4,S:Num5,D:Num6,Z:Num1,X:Num2,C:Num3,R:Num0'
    Assert-Condition ($actionText.Contains($expectedPreset)) "KeypadLayerAction has exact default KeyMap preset (INV-DEFAULT-PRESET)"
    Assert-Condition ($actionText -match 'ParameterFieldType\.KeyMap') "KeypadLayerAction uses ParameterFieldType.KeyMap (INV-KEYMAP-OPAQUE)"
    Assert-Condition ($actionText -match 'ActionKind\.Sequential') "KeypadLayerAction specifies ActionKind.Sequential (INV-TOGGLE)"
    Assert-Condition ($actionText -match 'ForegroundProcessName') "KeypadLayerAction accesses ForegroundProcessName (INV-FOREGROUND-SCOPE)"
    Assert-Condition ($actionText -match '\.Toggle\(') "KeypadLayerAction invokes host Toggle operation (INV-TOGGLE)"

    # Check that raw host result message and raw exception message are not passed to user
    Assert-Condition ($actionText -notmatch 'ActionResult\.Ok\s*\([^)]*remapResult\.Message') "ExecuteAsync does not return raw remapResult.Message to user (INV-I18N)"
    Assert-Condition ($actionText -notmatch 'ActionResult\.Ok\s*\([^)]*ex\.Message') "ExecuteAsync does not return raw ex.Message to user (INV-I18N)"

    # Check that raw host result and exception are logged to plugin log
    Assert-Condition ($actionText -match 'Log\.(Warn|Error)\s*\([^)]*remapResult\.Message') "ExecuteAsync logs raw remapResult.Message to plugin log (INV-I18N)"
    Assert-Condition ($actionText -match 'Log\.Error\s*\([^)]*ex') "ExecuteAsync logs raw exceptions to plugin log (INV-I18N)"

    # Check Descriptor localization (INV-I18N)
    Assert-Condition ($actionText -match 'Category\s*=\s*_context\.I18n\.T\(\s*"keypad\.category"') "Descriptor Category uses localized entry keypad.category (INV-I18N)"
    Assert-Condition ($actionText -notmatch 'Category\s*=\s*Texts\.Category') "Descriptor Category does not expose raw Texts.Category (INV-I18N)"
    Assert-Condition ($actionText -match 'Description\s*=\s*_context\.I18n\.T\(\s*"keypad\.desc"') "Descriptor Description uses localized entry keypad.desc (INV-I18N)"
    Assert-Condition ($actionText -notmatch 'Description\s*=\s*Texts\.ActionDesc[,\r\n]') "Descriptor Description does not use unlocalized static ActionDesc directly (INV-I18N)"

    # Check Preview custom key mapping branch (INV-KEYMAP-PREVIEW)
    Assert-Condition ($actionText -match 'FormatCustomPreview\(') "Preview uses FormatCustomPreview for custom mappings (INV-KEYMAP-PREVIEW)"
    Assert-Condition ($actionText -match 'string\.Equals\s*\(\s*keyMap\s*,\s*DefaultKeyMap') "Preview distinguishes DefaultKeyMap from custom mappings (INV-KEYMAP-PREVIEW)"
}

# 6. Check Texts.cs (INV-I18N & language-neutral static strings)
$textsFile = Join-Path $srcDir 'Texts.cs'
Assert-Condition (Test-Path -LiteralPath $textsFile) "Texts.cs exists"
if (Test-Path -LiteralPath $textsFile) {
    $textsText = Get-Content -LiteralPath $textsFile -Raw
    $chineseRegex = '[\u4e00-\u9fa5]'

    # Check static constants for no Chinese prose (language-neutral key notation)
    if ($textsText -match 'Category\s*=\s*"([^"]+)"') {
        $catVal = $Matches[1]
        Assert-Condition ($catVal -notmatch $chineseRegex) "Static Category '$catVal' contains no Chinese prose (INV-I18N)"
    }
    else {
        Assert-Condition $false "Texts.cs Category constant found"
    }

    if ($textsText -match 'ActionDesc\s*=\s*"([^"]+)"') {
        $descVal = $Matches[1]
        Assert-Condition ($descVal -notmatch $chineseRegex) "Static ActionDesc '$descVal' contains no Chinese prose (INV-I18N)"
    }
    else {
        Assert-Condition $false "Texts.cs ActionDesc constant found"
    }

    if ($textsText -match 'FieldKeyMapHelp\s*=\s*"([^"]+)"') {
        $helpVal = $Matches[1]
        Assert-Condition ($helpVal -notmatch $chineseRegex) "Static FieldKeyMapHelp '$helpVal' contains no Chinese prose (INV-I18N)"
    }
    else {
        Assert-Condition $false "Texts.cs FieldKeyMapHelp constant found"
    }

    # Check i18n keys
    Assert-Condition ($textsText -match 'keypad\.title') "Texts.cs defines keypad.title"
    Assert-Condition ($textsText -match 'keypad\.desc') "Texts.cs defines keypad.desc"
    Assert-Condition ($textsText -match 'keypad\.category') "Texts.cs defines keypad.category"
    Assert-Condition ($textsText -match 'keypad\.field\.keyMap') "Texts.cs defines keypad.field.keyMap"
    Assert-Condition ($textsText -match 'keypad\.preview') "Texts.cs defines keypad.preview"
    Assert-Condition ($textsText -match 'keypad\.msg\.activated') "Texts.cs defines keypad.msg.activated"
    Assert-Condition ($textsText -match 'keypad\.msg\.deactivated') "Texts.cs defines keypad.msg.deactivated"
    Assert-Condition ($textsText -match 'keypad\.err\.noForeground') "Texts.cs defines keypad.err.noForeground"
    Assert-Condition ($textsText -match 'keypad\.err\.noCapability') "Texts.cs defines keypad.err.noCapability"
    Assert-Condition ($textsText -match 'keypad\.err\.busyByOther') "Texts.cs defines keypad.err.busyByOther (INV-I18N)"
    Assert-Condition ($textsText -match 'keypad\.err\.hostFailed') "Texts.cs defines keypad.err.hostFailed"
    Assert-Condition ($textsText -match 'keypad\.err\.toggleFailed') "Texts.cs defines keypad.err.toggleFailed"
    Assert-Condition ($textsText -match 'zh-TW') "Texts.cs includes zh-TW table (INV-I18N)"
    Assert-Condition ($textsText -match 'ja') "Texts.cs includes ja table (INV-I18N)"
}

# 7. Check SDK_SOURCE.json (INV-SDK-SNAPSHOT)
$sdkSourceFile = Join-Path $root 'sdk/StarPie.Plugin.Abstractions/SDK_SOURCE.json'
Assert-Condition (Test-Path -LiteralPath $sdkSourceFile) "SDK_SOURCE.json exists"
if (Test-Path -LiteralPath $sdkSourceFile) {
    $sdkSource = Get-Content -LiteralPath $sdkSourceFile -Raw | ConvertFrom-Json
    Assert-Condition ($sdkSource.sourceCommit -eq 'd8ddbce2669d9471e09f03f153403e5fdd733d63') "SDK_SOURCE.json records commit d8ddbce2669d9471e09f03f153403e5fdd733d63"
    Assert-Condition ($sdkSource.apiVersion -eq '1.7') "SDK_SOURCE.json records apiVersion 1.7"
}

# 8. Check Preview formatting and custom mapping regression tests (INV-KEYMAP-PREVIEW)
$builtDll = Join-Path $root 'src/StarPie.Plugin.KeypadLayer/bin/Release/net8.0-windows/StarPie.Plugin.KeypadLayer.dll'
if (-not (Test-Path -LiteralPath $builtDll)) {
    $builtDll = Join-Path $root 'artifacts/staging/starpie.plugin.keypadlayer/1.0.1/StarPie.Plugin.KeypadLayer.dll'
}
if (Test-Path -LiteralPath $builtDll) {
    $abstractionsDll = Join-Path $root 'sdk/StarPie.Plugin.Abstractions/bin/Release/net8.0-windows/StarPie.Plugin.Abstractions.dll'
    if (Test-Path -LiteralPath $abstractionsDll) {
        [System.Reflection.Assembly]::LoadFrom($abstractionsDll) | Out-Null
    }
    $asm = [System.Reflection.Assembly]::LoadFrom($builtDll)
    $type = $asm.GetType('StarPie.Plugin.KeypadLayer.KeypadLayerAction')
    $textsType = $asm.GetType('StarPie.Plugin.KeypadLayer.Texts')

    if ($type -and $textsType) {
        $formatMethod = $type.GetMethod('FormatCustomPreview', [System.Reflection.BindingFlags]'Public, Static')
        Assert-Condition ($null -ne $formatMethod) "KeypadLayerAction exposes FormatCustomPreview static helper (INV-KEYMAP-PREVIEW)"

        if ($formatMethod) {
            $defaultPreview = $textsType.GetField('Preview', [System.Reflection.BindingFlags]'NonPublic, Static').GetValue($null)

            # Case 1: single custom mapping
            $res1 = $formatMethod.Invoke($null, @('v1|A:Space', $null))
            Assert-Condition ($res1 -eq 'A ➔ Space') "Custom preview formats single pair 'v1|A:Space' -> 'A ➔ Space'"
            Assert-Condition ($res1 -ne $defaultPreview) "Custom mapping does not return default preset preview"
            Assert-Condition ($res1 -notmatch 'CAD') "Custom mapping preview contains no CAD terminology"

            # Case 2: two custom mappings
            $res2 = $formatMethod.Invoke($null, @('v1|A:Space,B:Enter', $null))
            Assert-Condition ($res2 -eq 'A ➔ Space, B ➔ Enter') "Custom preview formats multiple pairs 'v1|A:Space,B:Enter'"
            Assert-Condition ($res2 -ne $defaultPreview) "Two-pair custom mapping does not return default preset preview"

            # Case 3: more than three mappings
            $res4 = $formatMethod.Invoke($null, @('v1|Q:Num7,W:Num8,E:Num9,R:Num0', $null))
            Assert-Condition ($res4 -match 'Q ➔ Num7, W ➔ Num8, E ➔ Num9') "Custom preview truncates after 3 pairs with suffix"
            Assert-Condition ($res4 -match '4') "Custom preview suffix indicates count (4)"
            Assert-Condition ($res4 -ne $defaultPreview) "Four-pair custom mapping does not return default preset preview"

            # Case 4: delimiters (semicolon and equals)
            $resSemi = $formatMethod.Invoke($null, @('v1|X=1;Y=2', $null))
            Assert-Condition ($resSemi -eq 'X ➔ 1, Y ➔ 2') "Custom preview handles ';' delimiter and '=' separator"

            # Case 5: empty or whitespace falls back
            $resEmpty = $formatMethod.Invoke($null, @('', $null))
            Assert-Condition ($resEmpty -eq $defaultPreview) "Empty keyMap falls back to default preset preview"
        }
    }
    else {
        Assert-Condition $false "KeypadLayerAction or Texts type found in assembly"
    }
}

Write-Host "--------------------------------------------------------"
if ($failures.Count -eq 0) {
    Write-Host "All StarPie.Plugin.KeypadLayer contract tests PASSED!" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "$($failures.Count) contract test assertions FAILED!" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    exit 1
}
