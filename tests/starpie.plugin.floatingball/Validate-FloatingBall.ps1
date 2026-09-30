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

Write-Host "=== Validating starpie.plugin.floatingball Module Contract & Geometry Regressions ===" -ForegroundColor Cyan

# ---------------------------------------------------------------------------------
# 1. Generate and check the temporary module registry
# ---------------------------------------------------------------------------------
$registryFile = Join-Path $root 'artifacts/generated/module-registry.json'
& (Join-Path $root 'build/Import-ModuleRegistryFromSource.ps1') -OutputPath $registryFile
Assert-Condition (Test-Path -LiteralPath $registryFile) "generated module registry exists"

$registry = Get-Content -LiteralPath $registryFile -Raw -Encoding UTF8 | ConvertFrom-Json
$module = @($registry.modules) | Where-Object { $_.id -eq 'starpie.plugin.floatingball' } | Select-Object -First 1
Assert-Condition ($null -ne $module) "starpie.plugin.floatingball is registered in generated module registry"

if ($module) {
    Assert-Condition ($module.name -eq '悬浮球') "Module name is '悬浮球'"
    Assert-Condition ($module.project -eq 'src/StarPie.Plugin.FloatingBall/FloatingBall.csproj') "Module project path is correct"
    Assert-Condition ($module.assembly -eq 'StarPie.Plugin.FloatingBall.dll') "Module assembly is 'StarPie.Plugin.FloatingBall.dll'"
    Assert-Condition ($module.targetFramework -eq 'net8.0-windows') "Module targetFramework is 'net8.0-windows'"
    Assert-Condition ($module.version -eq '1.0.3') "Module version in registry is '1.0.3'"
    Assert-Condition ($module.pluginId -eq 'starpie.plugin.floatingball') "Module pluginId is 'starpie.plugin.floatingball'"
    Assert-Condition ($module.contributionId -eq 'showBall') "Module contributionId is 'showBall'"
    Assert-Condition ($module.entryType -eq 'StarPie.Plugin.FloatingBall.FloatingBallPlugin') "Module entryType is correct"
    Assert-Condition (@($module.capabilities).Count -eq 2 -and @($module.capabilities) -contains 'Ui' -and @($module.capabilities) -contains 'Wheel') "Module capabilities contains 'Ui' and 'Wheel'"
    Assert-Condition ($module.apiVersion -eq '1.6') "Module apiVersion is '1.6'"
    Assert-Condition ($module.minHostVersion -eq '1.8.0-beta.3') "Module minHostVersion is '1.8.0-beta.3'"
    Assert-Condition ($module.enabled -eq $true) "Module is enabled"
}

# ---------------------------------------------------------------------------------
# 2. Check plugin.json
# ---------------------------------------------------------------------------------
$pluginJsonFile = Join-Path $root 'src/StarPie.Plugin.FloatingBall/plugin.json'
Assert-Condition (Test-Path -LiteralPath $pluginJsonFile) "plugin.json exists"
if (Test-Path -LiteralPath $pluginJsonFile) {
    $pluginJson = Get-Content -LiteralPath $pluginJsonFile -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($pluginJson.id -eq 'starpie.plugin.floatingball') "plugin.json id is 'starpie.plugin.floatingball'"
    Assert-Condition ($pluginJson.version -eq '1.0.3') "plugin.json version is '1.0.3'"
    Assert-Condition ($pluginJson.apiVersion -eq '1.6') "plugin.json apiVersion is '1.6'"
    Assert-Condition ($pluginJson.minHostVersion -eq '1.8.0-beta.3') "plugin.json minHostVersion is '1.8.0-beta.3'"
    Assert-Condition ($pluginJson.assembly -eq 'StarPie.Plugin.FloatingBall.dll') "plugin.json assembly is 'StarPie.Plugin.FloatingBall.dll'"
}

# ---------------------------------------------------------------------------------
# 3. Check csproj and narrowing constraints (zero NuGet, private=false)
# ---------------------------------------------------------------------------------
$csprojPath = Join-Path $root 'src/StarPie.Plugin.FloatingBall/FloatingBall.csproj'
Assert-Condition (Test-Path -LiteralPath $csprojPath) "FloatingBall.csproj exists"

if (Test-Path -LiteralPath $csprojPath) {
    $csprojXml = [xml](Get-Content -LiteralPath $csprojPath -Raw)
    $versionNode = $csprojXml.SelectSingleNode("//Version")
    Assert-Condition ($null -ne $versionNode -and $versionNode.InnerText.Trim() -eq '1.0.3') "FloatingBall.csproj Version is '1.0.3'"

    $packageRefs = $csprojXml.SelectNodes("//PackageReference")
    Assert-Condition ($packageRefs.Count -eq 0) "FloatingBall.csproj has zero NuGet package references (zero NuGet constraint)"

    $frameworkRefs = $csprojXml.SelectNodes("//FrameworkReference")
    $hasWpfRef = $false
    foreach ($fr in $frameworkRefs) {
        if ($fr.GetAttribute('Include') -eq 'Microsoft.WindowsDesktop.App.WPF') { $hasWpfRef = $true }
    }
    Assert-Condition $hasWpfRef "FloatingBall.csproj references Microsoft.WindowsDesktop.App.WPF framework"

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
    Assert-Condition $hasSdkRef "FloatingBall.csproj references StarPie.Plugin.Abstractions"
    Assert-Condition $privateFalse "FloatingBall.csproj sets Private=false on StarPie.Plugin.Abstractions"
}

# ---------------------------------------------------------------------------------
# 4. Load Built DLL and perform automated regression testing
# ---------------------------------------------------------------------------------
$builtDll = Join-Path $root 'src/StarPie.Plugin.FloatingBall/bin/Release/net8.0-windows/StarPie.Plugin.FloatingBall.dll'
if (-not (Test-Path -LiteralPath $builtDll)) {
    Write-Host "Building FloatingBall in Release mode for testing..." -ForegroundColor Yellow
    & dotnet build $csprojPath -c Release --nologo
}

Assert-Condition (Test-Path -LiteralPath $builtDll) "Built FloatingBall assembly exists"

if (Test-Path -LiteralPath $builtDll) {
    $abstractionsDll = Join-Path $root 'sdk/StarPie.Plugin.Abstractions/bin/Release/net8.0-windows/StarPie.Plugin.Abstractions.dll'
    if (Test-Path -LiteralPath $abstractionsDll) {
        [System.Reflection.Assembly]::LoadFrom($abstractionsDll) | Out-Null
    }
    $asm = [System.Reflection.Assembly]::LoadFrom($builtDll)

    $placementType = $asm.GetType('StarPie.Plugin.FloatingBall.BallPlacement')
    Assert-Condition ($null -ne $placementType) "BallPlacement helper exists in assembly"

    $workAreaType = $asm.GetType('StarPie.Plugin.FloatingBall.BallPlacement+MonitorWorkArea')
    Assert-Condition ($null -ne $workAreaType) "BallPlacement+MonitorWorkArea struct exists"

    if ($placementType -and $workAreaType) {
        # Helper to instantiate MonitorWorkArea(int left, int top, int right, int bottom, bool isPrimary)
        function New-WorkArea([int]$l, [int]$t, [int]$r, [int]$b, [bool]$primary) {
            return [System.Activator]::CreateInstance($workAreaType, @($l, $t, $r, $b, $primary))
        }

        # Helper to convert array of WorkAreas to typed IReadOnlyList
        function New-WorkAreaList($areas) {
            $listType = [System.Collections.Generic.List``1].MakeGenericType($workAreaType)
            $list = [System.Activator]::CreateInstance($listType)
            foreach ($a in $areas) {
                [void]$list.Add($a)
            }
            return ,$list
        }

        # Method references
        $clampMethod = $placementType.GetMethod('ClampToMonitors', [System.Reflection.BindingFlags]'Public, Static')
        $targetMethod = $placementType.GetMethod('FindTargetMonitor', [System.Reflection.BindingFlags]'Public, Static')
        $initMethod = $placementType.GetMethod('CalculateInitialPlacement', [System.Reflection.BindingFlags]'Public, Static')
        $gestureMethod = $placementType.GetMethod('ClassifyGesture', [System.Reflection.BindingFlags]'Public, Static')

        Assert-Condition ($null -ne $clampMethod) "BallPlacement.ClampToMonitors method exists"
        Assert-Condition ($null -ne $targetMethod) "BallPlacement.FindTargetMonitor method exists"
        Assert-Condition ($null -ne $initMethod) "BallPlacement.CalculateInitialPlacement method exists"
        Assert-Condition ($null -ne $gestureMethod) "BallPlacement.ClassifyGesture method exists"

        # =========================================================================
        # REGRESSION CASE 1: Monitor at negative X and Y
        # Monitor 1 (Primary): (0, 0) to (1920, 1040)
        # Monitor 2 (Secondary): (-1920, -1080) to (0, -40) (WorkArea)
        # Ball saved position at (-500, -200), size 56x56
        # Bug in original code: anchor.X < 0 && anchor.Y < 0 was treated as firstPlacement,
        # resetting ball to primary monitor.
        # Fixed behavior: recognized as valid saved coordinate and clamped on negative screen.
        # =========================================================================
        Write-Host "--- Regression Test 1: Monitor at negative X and Y ---" -ForegroundColor Yellow
        $mPrimary = New-WorkArea 0 0 1920 1040 $true
        $mSecondaryNegative = New-WorkArea -1920 -1080 0 -40 $false
        $monitorsNeg = New-WorkAreaList @($mPrimary, $mSecondaryNegative)

        # 1.1 Center of (-500, -200, 56x56) is (-472, -172). Target monitor must be the negative monitor.
        $targetNeg = $targetMethod.Invoke($null, @(-472, -172, $monitorsNeg))
        Assert-Condition ($targetNeg.Left -eq -1920 -and $targetNeg.Top -eq -1080) "FindTargetMonitor finds negative monitor for point (-472, -172)"

        # 1.2 Clamping to negative monitor preserves negative coordinates
        $resClampNeg = $clampMethod.Invoke($null, @(-500, -200, 56, 56, $monitorsNeg))
        Assert-Condition ($resClampNeg.Item1 -eq -500 -and $resClampNeg.Item2 -eq -200) "ClampToMonitors preserves negative coordinates (-500, -200) on negative monitor"

        # 1.3 Boundary clamping on negative monitor: left=-2000 clamps to -1920, top=-1200 clamps to -1080
        $resEdgeNeg = $clampMethod.Invoke($null, @(-2000, -1200, 56, 56, $monitorsNeg))
        Assert-Condition ($resEdgeNeg.Item1 -eq -1920 -and $resEdgeNeg.Item2 -eq -1080) "ClampToMonitors clamps out-of-bounds negative coordinates to (-1920, -1080)"

        # 1.4 Contrast assertion against old bug behavior: old code evaluated firstPlacement = (X < 0 && Y < 0)
        # Old code calculated: primary screen 1920 - 56 - 40 = 1824, top = (1080 - 56)*2/5 = 409
        $oldBuggyX = 1824
        $oldBuggyY = 409
        Assert-Condition ($resClampNeg.Item1 -ne $oldBuggyX -and $resClampNeg.Item2 -ne $oldBuggyY) "Regression verified: ball does NOT jump to primary screen ($oldBuggyX, $oldBuggyY)"

        # =========================================================================
        # REGRESSION CASE 2: L-shaped layout gap (empty void)
        # Monitor 1 (Primary): (0, 1080) to (1920, 2120) (bottom-left)
        # Monitor 2 (Secondary): (1920, 0) to (3840, 2120) (right side, full height)
        # Virtual bounding box is (0, 0, 3840, 2120).
        # Void gap: (0, 0, 1920, 1080).
        # Old behavior: ClampToVirtualScreen accepted (500, 200) because it was inside virtual bounding box,
        # leaving the ball stranded in the void!
        # Fixed behavior: ClampToMonitors detects (500, 200) is in void, finds nearest monitor (Monitor 1),
        # and clamps Top >= 1080 into Monitor 1's work area!
        # =========================================================================
        Write-Host "--- Regression Test 2: L-shaped layout gap ---" -ForegroundColor Yellow
        $mL1 = New-WorkArea 0 1080 1920 2120 $true
        $mL2 = New-WorkArea 1920 0 3840 2120 $false
        $monitorsL = New-WorkAreaList @($mL1, $mL2)

        # 2.1 Void point at (500, 200) with size 56x56
        $resVoidClamp = $clampMethod.Invoke($null, @(500, 200, 56, 56, $monitorsL))
        Assert-Condition ($resVoidClamp.Item2 -ge 1080) "ClampToMonitors moves ball out of void: Top ($($resVoidClamp.Item2)) is >= 1080"
        Assert-Condition ($resVoidClamp.Item1 -eq 500 -and $resVoidClamp.Item2 -eq 1080) "ClampToMonitors clamps void coordinate (500, 200) to Monitor 1 work area top-edge (500, 1080)"

        # 2.2 Contrast assertion against old bug behavior: old ClampToVirtualScreen kept (500, 200)
        Assert-Condition ($resVoidClamp.Item2 -ne 200) "Regression verified: ball does NOT remain in empty void at Top=200"

        # 2.3 Point at (1800, 100) closer to Monitor 2 (Left=1920)
        $resVoidClamp2 = $clampMethod.Invoke($null, @(1800, 100, 56, 56, $monitorsL))
        Assert-Condition ($resVoidClamp2.Item1 -ge 1920) "Point (1800, 100) closer to Monitor 2 is clamped to Monitor 2: Left ($($resVoidClamp2.Item1)) >= 1920"

        # =========================================================================
        # REGRESSION CASE 3: Unplugged monitor recovery
        # Saved coordinate (2500, 500) was on Monitor 2 (1920..3840).
        # Monitor 2 was unplugged. Available monitors now only: Monitor 1 (0, 0, 1920, 1040).
        # Old behavior: coordinate was not repositioned to remaining monitor work area.
        # Fixed behavior: ClampToMonitors falls back to Monitor 1, clamping Left to 1920 - 56 = 1864.
        # =========================================================================
        Write-Host "--- Regression Test 3: Unplugged monitor recovery ---" -ForegroundColor Yellow
        $monitorsRemaining = New-WorkAreaList @($mPrimary)

        $resUnplugged = $clampMethod.Invoke($null, @(2500, 500, 56, 56, $monitorsRemaining))
        Assert-Condition ($resUnplugged.Item1 -eq 1864) "Disconnected coordinate (2500, 500) clamps to primary work area right edge (1864)"
        Assert-Condition ($resUnplugged.Item2 -eq 500) "Disconnected coordinate Y is preserved within primary work area (500)"
        Assert-Condition ($resUnplugged.Item1 -lt 1920) "Regression verified: ball is strictly within remaining monitor bounds"

        # =========================================================================
        # REGRESSION CASE 4: Click vs Drag threshold boundary
        # Slop = 4
        # Displacement <= 4 -> Click
        # Displacement > 4 -> Drag
        # Old behavior: Mouse-up relied solely on _dragging flag set during MouseMove.
        # If MouseMove was delayed/missed, releasing at dx=5 misclassified as Click!
        # Fixed behavior: ClassifyGesture accurately classifies by final displacement.
        # =========================================================================
        Write-Host "--- Regression Test 4: Click vs Drag boundary ---" -ForegroundColor Yellow
        $gestureEnum = $asm.GetType('StarPie.Plugin.FloatingBall.BallPlacement+GestureKind')
        $clickVal = [System.Enum]::Parse($gestureEnum, 'Click')
        $dragVal = [System.Enum]::Parse($gestureEnum, 'Drag')

        # 4.1 Stationary press and release -> Click
        $g0 = $gestureMethod.Invoke($null, @(100, 100, 100, 100, $false, 4))
        Assert-Condition ($g0 -eq $clickVal) "Displacement (0,0) classified as Click"

        # 4.2 Exact boundary (dx=4, dy=0) -> Click
        $g4x = $gestureMethod.Invoke($null, @(100, 100, 104, 100, $false, 4))
        Assert-Condition ($g4x -eq $clickVal) "Boundary displacement dx=4, dy=0 classified as Click"

        # 4.3 Exact boundary (dx=0, dy=4) -> Click
        $g4y = $gestureMethod.Invoke($null, @(100, 100, 100, 104, $false, 4))
        Assert-Condition ($g4y -eq $clickVal) "Boundary displacement dx=0, dy=4 classified as Click"

        # 4.4 Exact boundary (dx=4, dy=4) -> Click
        $g4xy = $gestureMethod.Invoke($null, @(100, 100, 104, 104, $false, 4))
        Assert-Condition ($g4xy -eq $clickVal) "Boundary displacement dx=4, dy=4 classified as Click"

        # 4.5 Beyond threshold (dx=5, dy=0) -> Drag (even if wasAlreadyDragging is false!)
        $g5x = $gestureMethod.Invoke($null, @(100, 100, 105, 100, $false, 4))
        Assert-Condition ($g5x -eq $dragVal) "Displacement dx=5, dy=0 classified as Drag (missed MouseMove recovery)"

        # 4.6 Beyond threshold (dx=0, dy=5) -> Drag
        $g5y = $gestureMethod.Invoke($null, @(100, 100, 100, 105, $false, 4))
        Assert-Condition ($g5y -eq $dragVal) "Displacement dx=0, dy=5 classified as Drag (missed MouseMove recovery)"

        # 4.7 Negative displacement beyond threshold (dx=-5, dy=0) -> Drag
        $gNeg = $gestureMethod.Invoke($null, @(100, 100, 95, 100, $false, 4))
        Assert-Condition ($gNeg -eq $dragVal) "Negative displacement dx=-5, dy=0 classified as Drag"

        # 4.8 If wasAlreadyDragging was true, even (0,0) remains Drag
        $gAlready = $gestureMethod.Invoke($null, @(100, 100, 100, 100, $true, 4))
        Assert-Condition ($gAlready -eq $dragVal) "wasAlreadyDragging=true preserves Drag state at (0,0)"

        # =========================================================================
        # REGRESSION CASE 5: Initial Placement calculation
        # primaryWorkArea: (0, 0, 1920, 1040), diameter=56, rightMargin=40
        # Expected: left = 1920 - 56 - 40 = 1824, top = 0 + (1040 - 56) * 2 / 5 = 984 * 2 / 5 = 393
        # =========================================================================
        Write-Host "--- Regression Test 5: Initial placement calculation ---" -ForegroundColor Yellow
        $initPos = $initMethod.Invoke($null, @(56, 56, $mPrimary, 40))
        Assert-Condition ($initPos.Item1 -eq 1824) "Initial placement Left is 1824 (RightMargin=40)"
        Assert-Condition ($initPos.Item2 -eq 393) "Initial placement Top is 393 (WorkArea aware, 2/5 of height)"
    }
}

# ---------------------------------------------------------------------------------
# 5. Check Brush & Effect Freezing on STA thread
# ---------------------------------------------------------------------------------
Write-Host "--- Test 6: Freezable (Brushes & Effects) Freezing ---" -ForegroundColor Yellow
$ballWinType = $asm.GetType('StarPie.Plugin.FloatingBall.BallWindow')
if ($ballWinType) {
    # 1. Inspect BuildShape (which also initializes BallBorderBrush)
    $buildShapeMethod = $ballWinType.GetMethod('BuildShape', [System.Reflection.BindingFlags]'NonPublic, Static')
    if ($buildShapeMethod) {
        $shape = $buildShapeMethod.Invoke($null, @(0.8, '#5884DE'))
        $gridChildrenProp = $shape.GetType().GetProperty('Children')
        $children = $gridChildrenProp.GetValue($shape)
        $ellipse = $children[0]
        $effectProp = $ellipse.GetType().GetProperty('Effect')
        $effect = $effectProp.GetValue($ellipse)
        $effectFrozen = $effect.GetType().GetProperty('IsFrozen').GetValue($effect)
        Assert-Condition ($effectFrozen -eq $true) "DropShadowEffect in BuildShape is frozen (Freezable.Freeze)"
    }

    # 2. Inspect BallBorderBrush static field
    $borderField = $ballWinType.GetField('BallBorderBrush', [System.Reflection.BindingFlags]'NonPublic, Static')
    if ($borderField) {
        $borderBrush = $borderField.GetValue($null)
        $borderFrozen = $borderBrush.GetType().GetProperty('IsFrozen').GetValue($borderBrush)
        Assert-Condition ($borderFrozen -eq $true) "BallBorderBrush is frozen (Freezable.Freeze)"
    }

    # 3. Inspect ParseFill
    $parseFillMethod = $ballWinType.GetMethod('ParseFill', [System.Reflection.BindingFlags]'NonPublic, Static')
    if ($parseFillMethod) {
        $fillBrush = $parseFillMethod.Invoke($null, @('#5884DE'))
        $fillFrozen = $fillBrush.GetType().GetProperty('IsFrozen').GetValue($fillBrush)
        Assert-Condition ($fillFrozen -eq $true) "Fill brush from ParseFill is frozen (Freezable.Freeze)"
    }
}
else {
    Assert-Condition $false "BallWindow type found in assembly"
}

# ---------------------------------------------------------------------------------
# 6. Focused Regression Checks (SP-FLOATBALL-001 Repair Round)
# ---------------------------------------------------------------------------------
Write-Host "--- Test 7: Separation of Drag Moves from Persistence Notifications (Anti-Churn) ---" -ForegroundColor Yellow
$updateDragMethod = $ballWinType.GetMethod('UpdateDragPosition', [System.Reflection.BindingFlags]'NonPublic, Instance')
Assert-Condition ($null -ne $updateDragMethod) "BallWindow exposes dedicated UpdateDragPosition method for user dragging"

$windowSource = Get-Content -LiteralPath (Join-Path $root 'src/StarPie.Plugin.FloatingBall/BallWindow.cs') -Raw
# Verify OnMouseMove routes to UpdateDragPosition, never RequestPhysicalLocation or Moved
Assert-Condition ($windowSource -match 'UpdateDragPosition\s*\(\s*cursor\.X\s*-\s*_grabOffset\.X') "OnMouseMove calls UpdateDragPosition during dragging (no direct RequestPhysicalLocation)"
Assert-Condition ($windowSource -notmatch 'RequestPhysicalLocation\s*\(\s*cursor\.X') "OnMouseMove does NOT call RequestPhysicalLocation (prevents drag persistence churn)"

# Verify ApplyLocation strictly guards Moved notification to resting ball state
Assert-Condition ($windowSource -match 'if\s*\(\s*moved\s*&&\s*notifyOnRelocation\s*&&\s*!_pressed\s*&&\s*!_dragging\s*\)') "ApplyLocation gates Moved invocation behind !_pressed && !_dragging"

Write-Host "--- Test 8: Reentrancy Guard & ALC Cleanup ---" -ForegroundColor Yellow
$reentrancyField = $ballWinType.GetField('_isApplyingLocation', [System.Reflection.BindingFlags]'NonPublic, Instance')
Assert-Condition ($null -ne $reentrancyField) "BallWindow contains _isApplyingLocation re-entrancy guard field"

Assert-Condition ($windowSource -match '_isApplyingLocation\s*=\s*true;') "ApplyLocation sets re-entrancy guard before window positioning"
Assert-Condition ($windowSource -match '_isApplyingLocation\s*=\s*false;') "ApplyLocation clears re-entrancy guard in finally block"
Assert-Condition ($windowSource -match '_hwndSource\?\.RemoveHook\s*\(\s*WndProc\s*\)') "OnClosed unhooks WndProc from HwndSource for clean ALC unload"

Write-Host "--- Test 9: Guarding OnLeftButtonDown against Invalid Window Rect ---" -ForegroundColor Yellow
Assert-Condition ($windowSource -match 'if\s*\(\s*rect\.Width\s*<=\s*0\s*\|\|\s*rect\.Height\s*<=\s*0\s*\)') "OnLeftButtonDown guards against invalid PhysicalRect size to prevent jumping to (0,0)"

Write-Host "--------------------------------------------------------"
if ($failures.Count -eq 0) {
    Write-Host "All StarPie.Plugin.FloatingBall contract and regression tests PASSED!" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "$($failures.Count) test assertions FAILED!" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    exit 1
}
