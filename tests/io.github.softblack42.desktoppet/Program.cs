using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using StarPie.Plugin.DesktopPet;

namespace DesktopPetTests;

public static class Program
{
    private static int _failureCount = 0;

    [STAThread]
    public static int Main()
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" StarPie Desktop Pet (SP-DESKTOPPET-001) Deterministic Tests");
        Console.WriteLine("============================================================");

        try
        {
            TestGestureClassification();
            TestAnimationStateMachineAndClick();
            TestAssetLoadingFallbacksAndRefresh();
            TestParameterPreferencesAndValidation();
            TestMultiMonitorAndPlacementRecovery();
            TestWheelForwardingPath();
            TestLifecycleShutdownSafety();
            TestDragInteractionAndModes();
            TestV3AssetsAndDragSway();
            TestTrackedWheelSessionAndPointing();
            TestLibraryManagementAndSafeSwitching();
            _failureCount += ExternalSettingsTests.Run();
            _failureCount += DesktopPetUiTests.Run();
            _failureCount += PetEnhancementTests.Run();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FATAL UNHANDLED EXCEPTION] {ex}");
            Console.ResetColor();
            _failureCount++;
        }

        Console.WriteLine("------------------------------------------------------------");
        if (_failureCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All SP-DESKTOPPET-001 tests PASSED successfully!");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Tests FAILED with {_failureCount} failure(s).");
            Console.ResetColor();
            return 1;
        }
    }

    private static void Assert(bool condition, string testName, string? details = null)
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($" [PASS] {testName}");
            Console.ResetColor();
        }
        else
        {
            _failureCount++;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($" [FAIL] {testName} {(details != null ? "- " + details : "")}");
            Console.ResetColor();
        }
    }

    // =========================================================================
    // 1. Click vs Drag classification & slop threshold
    // =========================================================================
    private static void TestGestureClassification()
    {
        Console.WriteLine("\n--- 1. Gesture Classification & Slop Boundary ---");

        // 1.1 无位移松手 -> Click
        var g0 = PetPlacement.ClassifyGesture(100, 100, 100, 100, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g0 == PetPlacement.GestureKind.Click, "Displacement (0,0) classified as Click");

        // 1.2 临界值内 (dx=4, dy=0) -> Click
        var g4x = PetPlacement.ClassifyGesture(100, 100, 104, 100, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g4x == PetPlacement.GestureKind.Click, "dx=4, dy=0 within slop classified as Click");

        // 1.3 临界值内 (dx=0, dy=4) -> Click
        var g4y = PetPlacement.ClassifyGesture(100, 100, 100, 104, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g4y == PetPlacement.GestureKind.Click, "dx=0, dy=4 within slop classified as Click");

        // 1.4 对角线 (dx=4, dy=4) -> Click
        var g4xy = PetPlacement.ClassifyGesture(100, 100, 104, 104, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g4xy == PetPlacement.GestureKind.Click, "dx=4, dy=4 within slop classified as Click");

        // 1.5 超出阈值 (dx=5, dy=0) -> Drag
        var g5x = PetPlacement.ClassifyGesture(100, 100, 105, 100, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g5x == PetPlacement.GestureKind.Drag, "dx=5, dy=0 beyond slop classified as Drag");

        // 1.6 超出阈值 (dx=0, dy=5) -> Drag
        var g5y = PetPlacement.ClassifyGesture(100, 100, 100, 105, wasAlreadyDragging: false, clickSlop: 4);
        Assert(g5y == PetPlacement.GestureKind.Drag, "dx=0, dy=5 beyond slop classified as Drag");

        // 1.7 负向位移超出阈值 (dx=-5, dy=0) -> Drag
        var gNeg = PetPlacement.ClassifyGesture(100, 100, 95, 100, wasAlreadyDragging: false, clickSlop: 4);
        Assert(gNeg == PetPlacement.GestureKind.Drag, "dx=-5, dy=0 negative displacement classified as Drag");

        // 1.8 wasAlreadyDragging 为 true 时，即便瞬时位移为 0 也锁定为 Drag
        var gLock = PetPlacement.ClassifyGesture(100, 100, 100, 100, wasAlreadyDragging: true, clickSlop: 4);
        Assert(gLock == PetPlacement.GestureKind.Drag, "wasAlreadyDragging=true preserves Drag state at (0,0)");
    }

    // =========================================================================
    // 2. Animation state machine transitions & click behavior with/without animation
    // =========================================================================
    private static void TestAnimationStateMachineAndClick()
    {
        Console.WriteLine("\n--- 2. Animation State Machine Transitions & Click Behavior ---");

        var sm = new PetAnimationStateMachine();
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "Initial frame is Idle");
        Assert(Math.Abs(sm.BreathingScaleY - 1.0) < 0.001, "Initial breathing scale is ~1.0");

        // 2.1 推进半个呼吸周期（1.0 秒），呼吸缩放应接近最大峰值 ~1.025
        sm.Tick(TimeSpan.FromSeconds(1.0));
        Assert(sm.BreathingScaleY > 1.020, "Breathing scale peaks around 1.025 at phase=0.5", $"Scale: {sm.BreathingScaleY}");

        // 2.2 推进到完整呼吸周期（再加 1.0 秒），回到 ~1.0
        sm.Tick(TimeSpan.FromSeconds(1.0));
        Assert(Math.Abs(sm.BreathingScaleY - 1.0) < 0.002, "Breathing scale returns to ~1.0 after 2.0s cycle", $"Scale: {sm.BreathingScaleY}");

        // 2.3 触发轻点反应表情
        sm.TriggerReact();
        Assert(sm.CurrentFrame == PetFrameKind.React, "TriggerReact sets frame to React");

        // 250ms 后仍处于 React
        sm.Tick(TimeSpan.FromMilliseconds(250));
        Assert(sm.CurrentFrame == PetFrameKind.React, "Frame remains React after 250ms");

        // 再过 300ms（总计 550ms > 500ms），恢复 Idle
        sm.Tick(TimeSpan.FromMilliseconds(300));
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "Frame returns to Idle after React expires");

        // 2.4 触发眨眼
        sm.TriggerBlink();
        Assert(sm.CurrentFrame == PetFrameKind.Blink, "TriggerBlink sets frame to Blink");

        // 100ms 后仍处于 Blink
        sm.Tick(TimeSpan.FromMilliseconds(100));
        Assert(sm.CurrentFrame == PetFrameKind.Blink, "Frame remains Blink after 100ms");

        // 再过 100ms（总计 200ms > 180ms），恢复 Idle
        sm.Tick(TimeSpan.FromMilliseconds(100));
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "Frame returns to Idle after Blink expires");

        // 2.5 React 优先于 Blink：在 React 期间触发 Blink 不会打断 React
        sm.TriggerReact();
        sm.TriggerBlink();
        Assert(sm.CurrentFrame == PetFrameKind.React, "React takes precedence over Blink");

        // 2.6 关闭动画（EnableAnimation = false）
        sm.EnableAnimation = false;
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "When animation disabled, CurrentFrame is forced Idle");
        Assert(Math.Abs(sm.BreathingScaleY - 1.0) < 0.0001, "When animation disabled, BreathingScaleY is strictly 1.0");

        sm.TriggerReact();
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "TriggerReact ignored when animation disabled");

        sm.TriggerBlink();
        Assert(sm.CurrentFrame == PetFrameKind.Idle, "TriggerBlink ignored when animation disabled");

        // 2.7 真实窗口轻点行为分流验证（动画关闭时保持 IdleBitmap，不设置 ReactBitmap，不重启定时器）
        string tempDir = Path.Combine(Path.GetTempPath(), "StarPie_PetTestAnim_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CreateMinimalPng(Path.Combine(tempDir, "idle.png"), 4, 4, 255, 0, 0);   // Red
            CreateMinimalPng(Path.Combine(tempDir, "react.png"), 4, 4, 0, 255, 0);  // Green
            var assetMgr = new PetAssetManager();
            assetMgr.LoadFromDirectory(tempDir);

            // 静态窗口 (EnableAnimation = false)
            PetWindow? staticWindow = null;
            PetWindow? animWindow = null;
            try
            {
                staticWindow = new PetWindow(140, assetMgr, enableAnimation: false);
                Assert(ReferenceEquals(staticWindow.ImageControl.Source, assetMgr.IdleBitmap), "Initial static window source is IdleBitmap");
                Assert(!staticWindow.AnimationTimer.IsEnabled, "Static window animation timer is stopped");

                bool staticWheelRequested = false;
                staticWindow.WheelRequested += (x, y) => staticWheelRequested = true;
                staticWindow.PerformClickAction();

                Assert(staticWheelRequested, "Clicking static window still requests wheel");
                Assert(ReferenceEquals(staticWindow.ImageControl.Source, assetMgr.IdleBitmap), "When animation disabled, click keeps IdleBitmap (does not set ReactBitmap)");
                Assert(!staticWindow.AnimationTimer.IsEnabled, "When animation disabled, click does not reactivate animation timer");

                // 动态窗口 (EnableAnimation = true)
                animWindow = new PetWindow(140, assetMgr, enableAnimation: true);
                Assert(animWindow.AnimationTimer.IsEnabled, "Animated window animation timer is running");

                bool animWheelRequested = false;
                animWindow.WheelRequested += (x, y) => animWheelRequested = true;
                animWindow.PerformClickAction();

                Assert(animWheelRequested, "Clicking animated window requests wheel");
                Assert(ReferenceEquals(animWindow.ImageControl.Source, assetMgr.ReactBitmap), "When animation enabled, click sets ReactBitmap");

                // 模拟时间推进使 React 表情过期恢复
                animWindow.StateMachine.Tick(TimeSpan.FromMilliseconds(550));
                Assert(animWindow.StateMachine.CurrentFrame == PetFrameKind.Idle, "State machine returns to Idle after React expires");
                animWindow.RefreshAssets();
                Assert(ReferenceEquals(animWindow.ImageControl.Source, assetMgr.IdleBitmap), "Animated window restores IdleBitmap after reaction expires");
            }
            finally
            {
                staticWindow?.Close();
                animWindow?.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // =========================================================================
    // 3. Asset loading, fallbacks & refresh consistency
    // =========================================================================
    private static void TestAssetLoadingFallbacksAndRefresh()
    {
        Console.WriteLine("\n--- 3. Asset Loading, Fallbacks & Refresh Consistency ---");

        var assetMgr = new PetAssetManager();

        // 3.1 空目录与不存在目录
        var resEmpty = assetMgr.LoadFromDirectory("");
        Assert(!resEmpty.Success, "Empty directory fails cleanly without crash");

        var resMissingDir = assetMgr.LoadFromDirectory(@"C:\Path\That\Does\Not\Exist_StarPiePet");
        Assert(!resMissingDir.Success, "Non-existent directory fails cleanly");

        // 3.2 构造临时素材目录测试必需帧与可选帧回退
        string tempDir = Path.Combine(Path.GetTempPath(), "StarPie_PetTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 缺少 idle.png
            var resNoIdle = assetMgr.LoadFromDirectory(tempDir);
            Assert(!resNoIdle.Success, "Directory missing idle.png fails cleanly");

            // 写入极简合法 4x4 PNG 作为 idle.png
            string idleFile = Path.Combine(tempDir, "idle.png");
            CreateMinimalPng(idleFile, 4, 4);

            var resWithIdleOnly = assetMgr.LoadFromDirectory(tempDir);
            Assert(resWithIdleOnly.Success, "Directory with idle.png loads successfully");
            Assert(!resWithIdleOnly.HasBlinkFrame, "HasBlinkFrame is false when blink.png missing");
            Assert(!resWithIdleOnly.HasReactFrame, "HasReactFrame is false when react.png missing");

            // 可选帧缺失时回退 idle
            Assert(ReferenceEquals(assetMgr.BlinkBitmap, assetMgr.IdleBitmap), "BlinkBitmap falls back to IdleBitmap when missing");
            Assert(ReferenceEquals(assetMgr.ReactBitmap, assetMgr.IdleBitmap), "ReactBitmap falls back to IdleBitmap when missing");
            Assert(assetMgr.IdleBitmap != null && assetMgr.IdleBitmap.IsFrozen, "IdleBitmap is loaded and frozen");

            // Alpha 掩膜命中测试
            Assert(assetMgr.HitTest(2, 2, 4, 4), "HitTest returns true on opaque pixel");
            Assert(!assetMgr.HitTest(-1, 2, 4, 4), "HitTest returns false outside window");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        // 3.3 测试素材刷新一致性（静态模式切换素材目录、同目录重载、加载失败状态保留）
        string dirA = Path.Combine(Path.GetTempPath(), "StarPie_PetDirA_" + Guid.NewGuid().ToString("N"));
        string dirB = Path.Combine(Path.GetTempPath(), "StarPie_PetDirB_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        try
        {
            string idleA = Path.Combine(dirA, "idle.png");
            string idleB = Path.Combine(dirB, "idle.png");
            CreateMinimalPng(idleA, 4, 4, 10, 20, 30);
            CreateMinimalPng(idleB, 4, 4, 100, 110, 120);

            var mockContext = new MockPluginContext();
            var controller = new PetController(mockContext) { IsHeadlessTestMode = true };
            try
            {
                // 1) 在静态模式（EnableAnimation = false）下显示 DirA
                bool showA = controller.Show(customSize: 140, customAssetDir: dirA, customEnableAnimation: false);
                Assert(showA, "Show DirA in static mode succeeded");
                Assert(controller.Window != null, "Window created for DirA");
                var winA = controller.Window!;
                var initialIdleSource = winA.ImageControl.Source;
                Assert(initialIdleSource != null, "Window image source loaded for DirA");
                Assert(!winA.AnimationTimer.IsEnabled, "Animation timer stopped in static mode");

                // 2) 切换到 DirB，尺寸保持 140，静态模式保持 false
                bool showB = controller.Show(customSize: 140, customAssetDir: dirB, customEnableAnimation: false);
                Assert(showB, "Show DirB with same size succeeded");
                Assert(ReferenceEquals(controller.Window, winA), "Window is reused when size is identical");
                Assert(!ReferenceEquals(winA.ImageControl.Source, initialIdleSource), "ImageControl.Source refreshed to new DirB asset in static mode");
                Assert(ReferenceEquals(winA.ImageControl.Source, controller.Assets.IdleBitmap), "ImageControl.Source matches new Assets.IdleBitmap");
                Assert(string.Equals(controller.Assets.CurrentDirectory, Path.GetFullPath(dirB), StringComparison.OrdinalIgnoreCase), "Current directory updated to DirB");

                // 3) 同目录显式重载：覆盖 DirB 的 idle.png，调用 ReloadAssets()
                var prevBBitmap = controller.Assets.IdleBitmap;
                CreateMinimalPng(idleB, 4, 4, 200, 210, 220); // 重新生成不同的像素内容
                bool reloaded = controller.ReloadAssets();
                Assert(reloaded, "ReloadAssets() on same directory succeeded");
                Assert(!ReferenceEquals(controller.Assets.IdleBitmap, prevBBitmap), "Same-directory reload decoded fresh bitmap");
                Assert(ReferenceEquals(winA.ImageControl.Source, controller.Assets.IdleBitmap), "ImageControl.Source refreshed on same-directory reload");

                // 4) 加载失败时保留上一套有效素材与显示状态
                var validBitmapBeforeFail = controller.Assets.IdleBitmap;
                string invalidDir = Path.Combine(Path.GetTempPath(), "NonExistent_PetDir_" + Guid.NewGuid().ToString("N"));
                bool reloadInvalid = controller.ReloadAssets(invalidDir);
                Assert(!reloadInvalid, "Reload from invalid directory returned false");
                Assert(controller.Window != null, "Window preserved on reload failure");
                Assert(ReferenceEquals(controller.Assets.IdleBitmap, validBitmapBeforeFail), "Prior valid asset preserved on reload failure");
                Assert(ReferenceEquals(winA.ImageControl.Source, validBitmapBeforeFail), "Window image source preserved on reload failure");
                Assert(string.Equals(controller.Assets.CurrentDirectory, Path.GetFullPath(dirB), StringComparison.OrdinalIgnoreCase), "CurrentDirectory preserved on reload failure");
            }
            finally
            {
                controller.Shutdown();
            }
        }
        finally
        {
            if (Directory.Exists(dirA)) try { Directory.Delete(dirA, true); } catch { }
            if (Directory.Exists(dirB)) try { Directory.Delete(dirB, true); } catch { }
        }
    }

    // =========================================================================
    // 4. Parameter preferences, defaults & validation
    // =========================================================================
    private static void TestParameterPreferencesAndValidation()
    {
        Console.WriteLine("\n--- 4. Parameter Preferences & Validation ---");

        var mockContext = new MockPluginContext();

        // 4.1 尺寸默认值与边界钳位
        mockContext.Settings.Set(PetPreference.SizeKey, null);
        double defaultSize = PetPreference.Size(mockContext);
        Assert(Math.Abs(defaultSize - Defaults.SizeDiu) < 0.001, "Unset size falls back to default 140");

        mockContext.Settings.Set(PetPreference.SizeKey, "20"); // 低于最小值 48
        Assert(Math.Abs(PetPreference.Size(mockContext) - 48.0) < 0.001, "Below-min size clamps to 48");

        mockContext.Settings.Set(PetPreference.SizeKey, "500"); // 高于最大值 360
        Assert(Math.Abs(PetPreference.Size(mockContext) - 360.0) < 0.001, "Above-max size clamps to 360");

        mockContext.Settings.Set(PetPreference.SizeKey, "180.5"); // 不变文化浮点
        Assert(Math.Abs(PetPreference.Size(mockContext) - 180.5) < 0.001, "InvariantCulture float parsed correctly");

        // 4.2 动画开关默认值
        mockContext.Settings.Set(PetPreference.EnableAnimationKey, null);
        Assert(PetPreference.EnableAnimation(mockContext) == true, "Unset enableAnimation defaults to true");

        mockContext.Settings.Set(PetPreference.EnableAnimationKey, "false");
        Assert(PetPreference.EnableAnimation(mockContext) == false, "Explicit 'false' enableAnimation parsed as false");

        // 4.3 动作参数校验
        var controller = new PetController(mockContext);
        try
        {
            var showAction = new ShowPetContribution(mockContext, controller, null);

            var validParams = new Dictionary<string, string> { [PetPreference.SizeKey] = "150" };
            Assert(showAction.Validate(validParams) == null, "Valid size parameter passes validation");

            var invalidParams = new Dictionary<string, string> { [PetPreference.SizeKey] = "not_a_number" };
            Assert(showAction.Validate(invalidParams) != null, "Non-numeric size fails validation");

            var outOfRangeParams = new Dictionary<string, string> { [PetPreference.SizeKey] = "999" };
            Assert(showAction.Validate(outOfRangeParams) != null, "Out-of-range size fails validation");

            var emptyParams = new Dictionary<string, string>();
            Assert(showAction.Validate(emptyParams) == null, "Empty parameters pass validation (fallback allowed)");
        }
        finally
        {
            controller.Shutdown();
        }
    }

    // =========================================================================
    // 5. Multi-monitor geometry, placement & recovery
    // =========================================================================
    private static void TestMultiMonitorAndPlacementRecovery()
    {
        Console.WriteLine("\n--- 5. Multi-Monitor Placement & Recovery ---");

        var primary = new PetPlacement.MonitorWorkArea(0, 0, 1920, 1040, IsPrimary: true);
        var secondary = new PetPlacement.MonitorWorkArea(1920, 0, 3840, 1080, IsPrimary: false);
        var monitors = new List<PetPlacement.MonitorWorkArea> { primary, secondary };

        // 5.1 初始落位：主屏右下角，离右边 40，离底边 24
        (int initLeft, int initTop) = PetPlacement.CalculateInitialPlacement(140, 140, primary, rightMargin: 40, bottomMargin: 24);
        Assert(initLeft == 1920 - 140 - 40, $"Initial placement Left is 1740 (actual: {initLeft})");
        Assert(initTop == 1040 - 140 - 24, $"Initial placement Top is 876 (actual: {initTop})");

        // 5.2 越界钳制到单屏
        (int clampLeft, int clampTop) = PetPlacement.ClampToMonitor(2000, 1000, 140, 140, primary);
        Assert(clampLeft == 1920 - 140, "Exceeding right edge clamps to 1780");
        Assert(clampTop == 1040 - 140, "Exceeding bottom edge clamps to 900");

        // 5.3 跨屏拖拽到副屏 (2500, 400)
        (int secLeft, int secTop) = PetPlacement.ClampToMonitors(2500, 400, 140, 140, monitors);
        Assert(secLeft == 2500 && secTop == 400, "Coordinate on secondary monitor stays preserved");

        // 5.4 断屏恢复：原坐标在副屏 (2500, 400)，副屏断开，仅剩主屏
        var singleMonitorList = new List<PetPlacement.MonitorWorkArea> { primary };
        (int recoveredLeft, int recoveredTop) = PetPlacement.ClampToMonitors(2500, 400, 140, 140, singleMonitorList);
        Assert(recoveredLeft == 1920 - 140, "Disconnected coordinate recovers to primary monitor right edge (1780)");
        Assert(recoveredTop == 400, "Y coordinate stays preserved within primary height");

        // 5.5 负坐标显示器（副屏在主屏左侧：-1920..0）
        var leftSecondary = new PetPlacement.MonitorWorkArea(-1920, 0, 0, 1080, IsPrimary: false);
        var dualWithLeft = new List<PetPlacement.MonitorWorkArea> { primary, leftSecondary };
        (int negLeft, int negTop) = PetPlacement.ClampToMonitors(-1000, 300, 140, 140, dualWithLeft);
        Assert(negLeft == -1000 && negTop == 300, "Negative coordinate on left-side monitor preserved");
    }

    // =========================================================================
    // 6. Wheel service forwarding path, coordinates & capabilities
    // =========================================================================
    private static void TestWheelForwardingPath()
    {
        Console.WriteLine("\n--- 6. Wheel Service Forwarding Path, Coordinates & Capabilities ---");

        var mockWheel = new MockHostWheelService();
        var mockInfo = new MockHostInfo { HasWheelCapability = true };
        var mockContext = new MockPluginContext(mockWheel, info: mockInfo);
        var controller = new PetController(mockContext) { IsHeadlessTestMode = true };

        // 6.1 正常调用受理：正物理坐标转发
        mockWheel.AcceptNext = true;
        bool res1 = controller.RequestWheel(500, 300);
        Assert(res1 == true, "RequestWheel returns true when accepted by host");
        Assert(mockWheel.ShowWheelCalls == 1, "ShowWheel call count is 1");
        Assert(Math.Abs(mockWheel.LastX - 500) < 0.001 && Math.Abs(mockWheel.LastY - 300) < 0.001, "Positive coordinates forwarded accurately");

        // 6.2 负坐标转发（副屏在主屏左侧/上方）
        mockWheel.AcceptNext = true;
        bool resNeg = controller.RequestWheel(-800, 250);
        Assert(resNeg == true, "Negative coordinates accepted through RequestWheel");
        Assert(mockWheel.ShowWheelCalls == 2, "ShowWheel call count is 2");
        Assert(Math.Abs(mockWheel.LastX - (-800)) < 0.001 && Math.Abs(mockWheel.LastY - 250) < 0.001, "Negative X coordinate forwarded accurately");

        // 6.3 宿主拒绝分支（例如正在执行手势或无可用显示区域）
        mockWheel.AcceptNext = false;
        bool resReject = controller.RequestWheel(600, 400);
        Assert(resReject == false, "RequestWheel returns false when rejected by host");
        Assert(mockWheel.ShowWheelCalls == 3, "ShowWheel call count incremented to 3");

        // 6.4 权限不足分支：清单未声明 Wheel 能力
        mockInfo.HasWheelCapability = false;
        bool resNoCap = controller.RequestWheel(100, 100);
        Assert(resNoCap == false, "RequestWheel returns false when Wheel capability is missing");
        Assert(mockWheel.ShowWheelCalls == 3, "ShowWheel is NOT called when capability is missing (call count stays 3)");

        // 恢复能力以继续后续测试
        mockInfo.HasWheelCapability = true;

        // 6.5 停用守卫：Shutdown 之后禁止呼出轮盘
        controller.Shutdown();
        bool resShutdown = controller.RequestWheel(200, 200);
        Assert(resShutdown == false, "RequestWheel returns false after controller is shut down");
        Assert(mockWheel.ShowWheelCalls == 3, "ShowWheel is NOT called after shutdown (call count stays 3)");

        // 6.6 从 PetWindow 点击直通转发全链路测试
        string tempDir = Path.Combine(Path.GetTempPath(), "StarPie_WheelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CreateMinimalPng(Path.Combine(tempDir, "idle.png"), 4, 4);
            var mockWheel2 = new MockHostWheelService();
            var mockContext2 = new MockPluginContext(mockWheel2);
            var controller2 = new PetController(mockContext2) { IsHeadlessTestMode = true };
            try
            {
                bool shown = controller2.Show(customSize: 100, customAssetDir: tempDir);
                Assert(shown, "Show succeeded for window-to-wheel test");
                var window = controller2.Window!;
                window.RequestPhysicalLocation(200, 150); // 中心应为 200+50=250, 150+50=200

                mockWheel2.AcceptNext = true;
                window.PerformClickAction();

                Assert(mockWheel2.ShowWheelCalls == 1, "Window click forwards to host wheel service");
                Assert(Math.Abs(mockWheel2.LastX - 250) < 1.0 && Math.Abs(mockWheel2.LastY - 200) < 1.0, "Physical center coordinates calculated and forwarded correctly", $"X: {mockWheel2.LastX}, Y: {mockWheel2.LastY}");
            }
            finally
            {
                controller2.Shutdown();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // =========================================================================
    // 7. Lifecycle shutdown, queueable dispatcher & generational guards
    // =========================================================================
    private static void TestLifecycleShutdownSafety()
    {
        Console.WriteLine("\n--- 7. Lifecycle Shutdown, Queueable Dispatcher & Generational Guards ---");

        var queueDispatcher = new QueueableFakeDispatcher();
        var triggerSettings = new TriggerableFakeSettings();
        var mockContext = new MockPluginContext(dispatcher: queueDispatcher, settings: triggerSettings);
        var plugin = new DesktopPetPlugin();

        plugin.Initialize(mockContext);
        Assert(mockContext.MockActions.RegisteredActions.Count == 4, "Plugin registers toggle plus all three compatible legacy actions");
        Assert(mockContext.MockActions.RegisteredActions.Count(a => a.Descriptor.ShowInActionPicker) == 1, "Only DesktopPet toggle appears as a new wheel choice");
        Assert(mockContext.MockActions.RegisteredActions.Any(a => a.Descriptor.Id == "managePet"), "Plugin registers managePet action");
        Assert(mockContext.MockSettingsPage.HasRegisteredPage, "Plugin registers settings page");
        Assert(triggerSettings.ActiveSubscriptionCount == 8, "Plugin registers 8 settings change subscriptions including animation speed");

        // 7.1 在队列中排入设置变更回调（模拟高频或异步操作）
        triggerSettings.TriggerChange(PetPreference.SizeKey, "140", "180");
        triggerSettings.TriggerChange(PetPreference.EnableAnimationKey, "true", "false");
        triggerSettings.TriggerChange(PetPreference.AssetDirectoryKey, "", @"C:\SomeDir");
        triggerSettings.TriggerChange(PetPreference.DragModeKey, Defaults.DragModeImmediate, Defaults.DragModeLongPress);
        triggerSettings.TriggerChange(PetPreference.LongPressDurationKey, "350", "500");
        triggerSettings.TriggerChange(PetPreference.EnableDragPoseKey, "true", "false");
        triggerSettings.TriggerChange(PetPreference.EnablePointingPoseKey, "true", "false");
        triggerSettings.TriggerChange(PetPreference.AnimationSpeedKey, "1", "2");
        Assert(queueDispatcher.QueuedCount == 8, "Settings changes posted 8 callbacks to queueable dispatcher");

        // 7.2 执行插件 Shutdown
        plugin.Shutdown();
        Assert(triggerSettings.ActiveSubscriptionCount == 0, "All settings subscriptions disposed on Shutdown");

        // 7.3 执行排队的旧回调（验证 Shutdown 后旧回调不可产生窗口创建请求、素材重载或动画重启）
        queueDispatcher.Drain();
        Assert(queueDispatcher.QueuedCount == 0, "Queued callbacks drained");

        // 7.4 验证幂等清理
        bool repeatedThrew = false;
        try
        {
            plugin.Shutdown();
        }
        catch
        {
            repeatedThrew = true;
        }
        Assert(!repeatedThrew, "Repeated Shutdown does not throw exception (idempotent)");

        // 7.5 单独验证 PetController 的停用与代际守卫
        var ctrl = new PetController(mockContext) { IsHeadlessTestMode = true };
        ctrl.Shutdown();
        Assert(ctrl.IsShutdown, "PetController marked IsShutdown after Shutdown()");
        Assert(!ctrl.Show(customSize: 140), "Show() returns false after controller shutdown");
        Assert(!ctrl.ReloadAssets(@"C:\Test"), "ReloadAssets() returns false after controller shutdown");
        Assert(!ctrl.RequestWheel(100, 100), "RequestWheel() returns false after controller shutdown");
        Assert(!ctrl.HasWindow, "HasWindow remains false after shutdown");
    }

    // =========================================================================
    // 8. Drag interaction, immediate vs long-press modes, drag pose & fallback
    // =========================================================================
    private static void TestDragInteractionAndModes()
    {
        Console.WriteLine("\n--- 8. Drag Interaction, Modes, Drag Pose & Fallbacks ---");

        var mockContext = new MockPluginContext();

        // 8.1 拖动偏好解析与默认值
        Assert(PetPreference.DragMode(mockContext) == Defaults.DragModeImmediate, "Default drag mode is Immediate");
        mockContext.Settings.Set(PetPreference.DragModeKey, "LongPress");
        Assert(PetPreference.DragMode(mockContext) == Defaults.DragModeLongPress, "LongPress mode parsed correctly");
        mockContext.Settings.Set(PetPreference.DragModeKey, "invalid_mode");
        Assert(PetPreference.DragMode(mockContext) == Defaults.DragModeImmediate, "Invalid drag mode falls back to Immediate");

        // 8.2 长按时长解析与边界钳位
        Assert(Math.Abs(PetPreference.LongPressDuration(mockContext) - 350.0) < 0.001, "Default long-press duration is 350ms");
        mockContext.Settings.Set(PetPreference.LongPressDurationKey, "100"); // below min 200
        Assert(Math.Abs(PetPreference.LongPressDuration(mockContext) - 200.0) < 0.001, "Below-min duration clamps to 200ms");
        mockContext.Settings.Set(PetPreference.LongPressDurationKey, "2000"); // above max 1000
        Assert(Math.Abs(PetPreference.LongPressDuration(mockContext) - 1000.0) < 0.001, "Above-max duration clamps to 1000ms");
        mockContext.Settings.Set(PetPreference.LongPressDurationKey, "NaN");
        Assert(Math.Abs(PetPreference.LongPressDuration(mockContext) - 350.0) < 0.001, "NaN duration falls back to 350ms");

        // 8.3 启用拖动姿势
        Assert(PetPreference.EnableDragPose(mockContext) == true, "Default EnableDragPose is true");
        mockContext.Settings.Set(PetPreference.EnableDragPoseKey, "false");
        Assert(PetPreference.EnableDragPose(mockContext) == false, "Explicit 'false' EnableDragPose parsed as false");

        // 8.4 设置页字段定义完整性
        var fields = PetSettingsFields.Build();
        Assert(fields.Count == 8, "Settings page declares 8 fields including animation speed");
        var dragModeField = fields.FirstOrDefault(f => f.Key == PetPreference.DragModeKey);
        Assert(dragModeField != null && dragModeField.Type == ParameterFieldType.Enum, "DragMode is declared as Enum field");
        Assert(dragModeField!.Options != null && dragModeField.Options.Count == 2, "DragMode provides 2 options");
        var pointingField = fields.FirstOrDefault(f => f.Key == PetPreference.EnablePointingPoseKey);
        Assert(pointingField != null && pointingField.Type == ParameterFieldType.Bool, "EnablePointingPose is declared as Bool field");

        // 8.5 drag.png 素材加载与缺失回退
        string tempDir = Path.Combine(Path.GetTempPath(), "StarPie_PetDragTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string idlePng = Path.Combine(tempDir, "idle.png");
            string dragPng = Path.Combine(tempDir, "drag.png");
            CreateMinimalPng(idlePng, 4, 4, 255, 0, 0); // Red
            CreateMinimalPng(dragPng, 4, 4, 0, 0, 255); // Blue

            var assetMgr = new PetAssetManager();
            var loadRes = assetMgr.LoadFromDirectory(tempDir);
            Assert(loadRes.Success, "Load asset directory with drag.png succeeded");
            Assert(loadRes.HasDragFrame, "HasDragFrame is true when drag.png present");
            Assert(assetMgr.DragBitmap != null && assetMgr.DragBitmap.IsFrozen, "DragBitmap is loaded and frozen");
            Assert(!ReferenceEquals(assetMgr.DragBitmap, assetMgr.IdleBitmap), "DragBitmap is distinct from IdleBitmap");

            var rect = new PetWindow.PhysicalRect(100, 100, 140, 140);

            // 8.6 立即模式 (Immediate Mode)
            PetWindow? winImmediate = null;
            try
            {
                winImmediate = new PetWindow(140, assetMgr, enableAnimation: true)
                {
                    DragMode = DragMode.Immediate,
                    EnableDragPose = true,
                };

                // 8.6.1 立即模式：轻点（位移 <= 4px）触发轮盘，不触发 Moved
                bool wheelReq = false;
                bool movedFired = false;
                winImmediate.WheelRequested += (x, y) => wheelReq = true;
                winImmediate.Moved += () => movedFired = true;

                winImmediate.HandleMouseDown(120, 120, rect);
                Assert(winImmediate.IsPressed, "Window marked IsPressed on mouse down");
                winImmediate.HandleMouseUp(122, 122); // dx=2, dy=2 -> 轴向 <= 4
                Assert(!winImmediate.IsPressed, "Window cleared IsPressed on mouse up");
                Assert(wheelReq, "Quick click in immediate mode requests wheel");
                Assert(!movedFired, "Quick click does not fire Moved event");

                // 8.6.1b 对角线 (4,4) 容差保持轻点触发轮盘
                wheelReq = false;
                movedFired = false;
                winImmediate.HandleMouseDown(120, 120, rect);
                winImmediate.HandleMouseUp(124, 124); // dx=4, dy=4
                Assert(wheelReq, "Diagonal (4,4) displacement within slop classified as Click and triggers wheel");
                Assert(!movedFired, "Diagonal (4,4) does not fire Moved event");

                // 8.6.2 立即模式：拖动（位移 > 4px）切换 drag.png，松手持久化位置，不触发轮盘
                wheelReq = false;
                movedFired = false;
                winImmediate.HandleMouseDown(120, 120, rect);
                winImmediate.HandleMouseMove(130, 120); // dx=10 > 4
                Assert(winImmediate.IsDragging, "Window enters dragging on movement > 4px in immediate mode");
                Assert(ReferenceEquals(winImmediate.ImageControl.Source, assetMgr.DragBitmap), "Dragging in immediate mode switches image to DragBitmap");

                winImmediate.HandleMouseUp(130, 120);
                Assert(!winImmediate.IsDragging, "Dragging cleared on mouse up");
                Assert(movedFired, "Drag in immediate mode fires Moved event on release");
                Assert(!wheelReq, "Drag release NEVER requests wheel");
                Assert(ReferenceEquals(winImmediate.ImageControl.Source, assetMgr.IdleBitmap), "Image restored to IdleBitmap after drag release");

                // 8.6.3 立即模式：拖动后移回原点释放（位移超出后复原），依然判定为拖动，不误触轮盘
                wheelReq = false;
                movedFired = false;
                winImmediate.HandleMouseDown(120, 120, rect);
                winImmediate.HandleMouseMove(130, 120); // 触发拖动
                winImmediate.HandleMouseMove(120, 120); // 移回原点
                winImmediate.HandleMouseUp(120, 120);
                Assert(!wheelReq, "Drag that returned to origin does NOT trigger wheel");
            }
            finally
            {
                winImmediate?.Close();
            }

            // 8.7 长按模式 (LongPress Mode)
            PetWindow? winLongPress = null;
            try
            {
                winLongPress = new PetWindow(140, assetMgr, enableAnimation: true)
                {
                    DragMode = DragMode.LongPress,
                    LongPressDurationMs = 350.0,
                    EnableDragPose = true,
                };
                bool wheelReq = false;
                bool movedFired = false;
                winLongPress.WheelRequested += (x, y) => wheelReq = true;
                winLongPress.Moved += () => movedFired = true;

                // 8.7.1 长按模式：长按阈值前轻点（未超位移，未超时）-> 呼出轮盘
                wheelReq = false;
                movedFired = false;
                winLongPress.HandleMouseDown(120, 120, rect);
                winLongPress.HandleMouseUp(120, 120);
                Assert(wheelReq, "Tap before long-press timeout requests wheel");
                Assert(!movedFired, "Tap does not fire Moved");

                // 8.7.2 长按模式：长按阈值前越界移动 -> 取消长按，松手不能误判轻点，不呼出轮盘
                wheelReq = false;
                movedFired = false;
                winLongPress.HandleMouseDown(120, 120, rect);
                winLongPress.HandleMouseMove(130, 120); // dx=10 > 4 在长按前发生
                Assert(winLongPress.MovedBeyondSlop, "MovedBeyondSlop flag set on early move");
                Assert(!winLongPress.IsDragging, "Window does NOT enter dragging on early move in LongPress mode");
                // 此时计时器即使触发也被守卫忽略
                winLongPress.TriggerLongPressTimeout();
                Assert(!winLongPress.IsLongPressTriggered, "LongPress timeout ignored when MovedBeyondSlop is true");
                winLongPress.HandleMouseUp(130, 120);
                Assert(!wheelReq, "Early move does NOT request wheel on release");
                Assert(!movedFired, "Early move without qualified drag does not fire Moved");

                // 8.7.3 长按模式：达到长按阈值但未产生位移，松手不意外呼出轮盘
                wheelReq = false;
                movedFired = false;
                winLongPress.HandleMouseDown(120, 120, rect);
                winLongPress.TriggerLongPressTimeout(); // 模拟长按达到 350ms
                Assert(winLongPress.IsLongPressTriggered, "Long-press successfully triggered");
                Assert(ReferenceEquals(winLongPress.ImageControl.Source, assetMgr.DragBitmap), "Image switches to DragBitmap on long-press threshold");
                winLongPress.HandleMouseUp(120, 120); // 原地松手，无位移
                Assert(!wheelReq, "Release after long-press without drag NEVER requests wheel");
                Assert(!movedFired, "Release without displacement does not fire Moved");
                Assert(ReferenceEquals(winLongPress.ImageControl.Source, assetMgr.IdleBitmap), "Image restored to IdleBitmap after long-press release");

                // 8.7.4 长按模式：达到长按阈值后拖动 -> 产生位移，松手写盘，不呼出轮盘
                wheelReq = false;
                movedFired = false;
                winLongPress.HandleMouseDown(120, 120, rect);
                winLongPress.TriggerLongPressTimeout();
                winLongPress.HandleMouseMove(150, 160); // 拖动
                Assert(winLongPress.IsDragging, "Window enters dragging after long-press timeout");
                winLongPress.HandleMouseUp(150, 160);
                Assert(movedFired, "Long-press drag release fires Moved event");
                Assert(!wheelReq, "Long-press drag release NEVER requests wheel");
                Assert(ReferenceEquals(winLongPress.ImageControl.Source, assetMgr.IdleBitmap), "Image restored to IdleBitmap after long-press drag");

                // 8.8 捕获丢失 (Lost Capture)
                wheelReq = false;
                movedFired = false;
                winLongPress.HandleMouseDown(120, 120, rect);
                winLongPress.HandleLostCapture();
                Assert(!winLongPress.IsPressed, "Pressed state cleared on lost capture");
                Assert(!wheelReq, "Lost capture does not request wheel");
            }
            finally
            {
                winLongPress?.Close();
            }

            // 8.9 总动画关闭 (EnableAnimation = false) 优先约束
            PetWindow? winStatic = null;
            try
            {
                winStatic = new PetWindow(140, assetMgr, enableAnimation: false)
                {
                    DragMode = DragMode.Immediate,
                    EnableDragPose = true,
                };
                bool wheelReq = false;
                bool movedFired = false;
                winStatic.WheelRequested += (x, y) => wheelReq = true;
                winStatic.Moved += () => movedFired = true;

                winStatic.HandleMouseDown(120, 120, rect);
                winStatic.HandleMouseMove(140, 140);
                Assert(winStatic.IsDragging, "Dragging still functions when animation is disabled");
                Assert(ReferenceEquals(winStatic.ImageControl.Source, assetMgr.IdleBitmap), "When animation disabled, image STRICTLY remains IdleBitmap (never DragBitmap)");
                winStatic.HandleMouseUp(140, 140);
                Assert(movedFired, "Static window drag still fires Moved event");
                Assert(!wheelReq, "Static window drag release does not request wheel");
                Assert(ReferenceEquals(winStatic.ImageControl.Source, assetMgr.IdleBitmap), "Static window image remains IdleBitmap");
            }
            finally
            {
                winStatic?.Close();
            }

            // 8.10 禁用拖动姿势 (EnableDragPose = false)
            PetWindow? winNoPose = null;
            try
            {
                winNoPose = new PetWindow(140, assetMgr, enableAnimation: true)
                {
                    DragMode = DragMode.Immediate,
                    EnableDragPose = false,
                };
                winNoPose.HandleMouseDown(120, 120, rect);
                winNoPose.HandleMouseMove(140, 140);
                Assert(ReferenceEquals(winNoPose.ImageControl.Source, assetMgr.IdleBitmap), "When EnableDragPose is false, dragging does not switch to DragBitmap");
                winNoPose.HandleMouseUp(140, 140);
            }
            finally
            {
                winNoPose?.Close();
            }

            // 8.11 缺失 drag.png 时的安全降级
            string fallbackDir = Path.Combine(Path.GetTempPath(), "StarPie_PetNoDrag_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fallbackDir);
            try
            {
                CreateMinimalPng(Path.Combine(fallbackDir, "idle.png"), 4, 4);
                var fallbackMgr = new PetAssetManager();
                fallbackMgr.LoadFromDirectory(fallbackDir);
                Assert(!fallbackMgr.HasDragFrame, "HasDragFrame is false when drag.png missing");
                Assert(ReferenceEquals(fallbackMgr.DragBitmap, fallbackMgr.IdleBitmap), "DragBitmap falls back to IdleBitmap");

                PetWindow? winFallback = null;
                try
                {
                    winFallback = new PetWindow(140, fallbackMgr, enableAnimation: true)
                    {
                        DragMode = DragMode.Immediate,
                        EnableDragPose = true,
                    };
                    winFallback.HandleMouseDown(120, 120, rect);
                    winFallback.HandleMouseMove(140, 140);
                    Assert(ReferenceEquals(winFallback.ImageControl.Source, fallbackMgr.IdleBitmap), "Window safely displays IdleBitmap during drag when drag.png missing");
                    winFallback.HandleMouseUp(140, 140);
                }
                finally
                {
                    winFallback?.Close();
                }
            }
            finally
            {
                if (Directory.Exists(fallbackDir)) try { Directory.Delete(fallbackDir, true); } catch { }
            }

            // 8.12 生产调用链验证：通过 PetController 验证拖动释放绝不抵达宿主轮盘服务
            var mockWheel = new MockHostWheelService();
            var ctx = new MockPluginContext(mockWheel);
            ctx.Settings.Set(PetPreference.DragModeKey, Defaults.DragModeLongPress);
            var controller = new PetController(ctx) { IsHeadlessTestMode = true };
            try
            {
                bool shown = controller.Show(customSize: 140, customAssetDir: tempDir);
                Assert(shown, "PetController.Show succeeded");
                var prodWin = controller.Window!;
                Assert(prodWin.DragMode == DragMode.LongPress, "Window initialized with LongPress drag mode from preference");

                // 模拟长按拖拽松手
                prodWin.HandleMouseDown(100, 100, rect);
                prodWin.TriggerLongPressTimeout();
                prodWin.HandleMouseMove(150, 150);
                prodWin.HandleMouseUp(150, 150);
                Assert(mockWheel.ShowWheelCalls == 0, "Production chain: Drag release NEVER forwards to host wheel service (ShowWheel calls strictly 0)");

                // 模拟轻点
                prodWin.HandleMouseDown(100, 100, rect);
                prodWin.HandleMouseUp(100, 100);
                Assert(mockWheel.ShowWheelCalls == 1, "Production chain: Quick click correctly forwards to host wheel service (ShowWheel calls == 1)");
            }
            finally
            {
                controller.Shutdown();
            }

            // 8.13 验证捕获释放重入防护（ReleaseMouseCapture 触发 LostMouseCapture 时不吞掉正常轻点）
            PetWindow? winCapture = null;
            try
            {
                winCapture = new PetWindow(140, assetMgr, enableAnimation: true);
                bool captureWheelReq = false;
                winCapture.WheelRequested += (x, y) => captureWheelReq = true;
                winCapture.HandleMouseDown(120, 120, rect);
                winCapture.HandleMouseUp(120, 120);
                Assert(captureWheelReq, "Normal mouse up does not get swallowed by capture release reentrancy");
                Assert(!winCapture.IsPressed, "IsPressed is false after mouse up");
            }
            finally
            {
                winCapture?.Close();
            }

            // 8.14 拖拽中途动态关闭 EnableDragPose，或在 EnableDragPose=false 下 tick，绝不复活 DragBitmap
            PetWindow? winDynamicPose = null;
            try
            {
                winDynamicPose = new PetWindow(140, assetMgr, enableAnimation: true)
                {
                    DragMode = DragMode.Immediate,
                    EnableDragPose = true,
                };
                winDynamicPose.HandleMouseDown(120, 120, rect);
                winDynamicPose.HandleMouseMove(150, 150); // 触发拖拽，此时显示 DragBitmap
                Assert(ReferenceEquals(winDynamicPose.ImageControl.Source, assetMgr.DragBitmap), "Active drag displays DragBitmap initially");

                // 动态关闭 EnableDragPose
                winDynamicPose.EnableDragPose = false;
                Assert(ReferenceEquals(winDynamicPose.ImageControl.Source, assetMgr.IdleBitmap), "Dynamic toggle to EnableDragPose=false immediately restores IdleBitmap");

                // 执行动画 Tick，验证不会被动画帧复活为 DragBitmap
                winDynamicPose.StateMachine.Tick(TimeSpan.FromMilliseconds(200));
                Assert(ReferenceEquals(winDynamicPose.ImageControl.Source, assetMgr.IdleBitmap), "Animation tick during drag with EnableDragPose=false strictly preserves IdleBitmap");
                winDynamicPose.HandleMouseUp(150, 150);
            }
            finally
            {
                winDynamicPose?.Close();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // =========================================================================
    // 9. V3 Drag Assets (Hanging/Teddy), SHA-256 & Drag Sway Animation
    // =========================================================================
    private static void TestV3AssetsAndDragSway()
    {
        Console.WriteLine("\n--- 9. V3 Drag Assets (Hanging/Teddy), SHA-256 & Drag Sway Animation ---");

        string v3Dir = @"G:\Users\2 Better\.codex\visualizations\2026\09\28\01a0e84e-617d-7752-bc81-e14d2bc9a2d1\stocking-desktop-pet-local-v3";
        bool v3Exists = Directory.Exists(v3Dir);
        Assert(v3Exists, "Local v3 asset directory exists on test machine");

        PetAssetManager v3Assets = new PetAssetManager();
        if (v3Exists)
        {
            string dragPng = Path.Combine(v3Dir, "drag.png");
            Assert(File.Exists(dragPng), "v3 drag.png exists");

            byte[] dragBytes = File.ReadAllBytes(dragPng);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            string actualSha256 = Convert.ToHexString(sha256.ComputeHash(dragBytes));
            const string expectedSha256 = "AA0CAC111FB5AF77DC186859E6224DA1BD810A0278B36419F145CC0148253E8A";
            Assert(string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase),
                $"v3 drag.png SHA-256 matches specification ({expectedSha256})", $"actual: {actualSha256}");

            var loadRes = v3Assets.LoadFromDirectory(v3Dir);
            Assert(loadRes.Success, "v3 assets load successfully with production loader");
            Assert(loadRes.HasDragFrame, "v3 HasDragFrame is true");
            Assert(v3Assets.DragBitmap != null && v3Assets.DragBitmap.IsFrozen, "v3 DragBitmap is decoded and frozen");
            Assert(v3Assets.PointingFrameCount == 8, "v3 directory contains all 8 pointing directional frames");
            var pointE = v3Assets.GetBitmapForFrame(PetFrameKind.PointE);
            Assert(pointE != null && pointE.IsFrozen, "PointE is decoded and frozen");
            var pointN = v3Assets.GetBitmapForFrame(PetFrameKind.PointN);
            Assert(pointN != null && pointN.IsFrozen, "PointN is decoded and frozen");
        }
        else
        {
            // Synthetic fallback if running in sandbox without v3 files
            string synDir = Path.Combine(Path.GetTempPath(), "StarPie_SynV3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(synDir);
            CreateMinimalPng(Path.Combine(synDir, "idle.png"), 4, 4, 255, 0, 0);
            CreateMinimalPng(Path.Combine(synDir, "drag.png"), 4, 4, 0, 255, 0);
            v3Assets.LoadFromDirectory(synDir);
        }

        // 9.2 状态机中的悬空平滑摆动计算 (±5°, 周期 1.0s, 正弦往返)
        var sm = new PetAnimationStateMachine { EnableAnimation = true, EnableDragPose = true };
        Assert(sm.DragSwayAngle == 0.0, "DragSwayAngle is initially 0.0 when idle");

        sm.SetDragging(true);
        Assert(sm.IsDragging, "State machine is marked dragging");
        Assert(sm.DragSwayAngle == 0.0, "DragSwayAngle is 0.0 at the instant dragging begins");

        // 推进 0.25 秒（四分之一周期，应达到正峰值 +5.0°）
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(Math.Abs(sm.DragSwayAngle - 5.0) < 0.05, $"DragSwayAngle reaches +5.0° at T/4 (actual: {sm.DragSwayAngle:F2}°)");

        // 再推进 0.25 秒（半周期，回落至 0.0°）
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(Math.Abs(sm.DragSwayAngle) < 0.05, $"DragSwayAngle returns to 0.0° at T/2 (actual: {sm.DragSwayAngle:F2}°)");

        // 再推进 0.25 秒（四分之三周期，达到负峰值 -5.0°）
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(Math.Abs(sm.DragSwayAngle - (-5.0)) < 0.05, $"DragSwayAngle reaches -5.0° at 3T/4 (actual: {sm.DragSwayAngle:F2}°)");

        // 再推进 0.25 秒（满周期，归零）
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(Math.Abs(sm.DragSwayAngle) < 0.05, $"DragSwayAngle returns to 0.0° at 1.0s (actual: {sm.DragSwayAngle:F2}°)");

        // 动画关闭时，SwayAngle 必须严格为 0
        sm.EnableAnimation = false;
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(sm.DragSwayAngle == 0.0, "DragSwayAngle is strictly 0.0 when EnableAnimation is false");

        // 拖动姿势关闭时，SwayAngle 必须严格为 0
        sm.EnableAnimation = true;
        sm.EnableDragPose = false;
        sm.Tick(TimeSpan.FromSeconds(0.25));
        Assert(sm.DragSwayAngle == 0.0, "DragSwayAngle is strictly 0.0 when EnableDragPose is false");

        // 9.3 真实 PetWindow 的 Transform 原点与角度联动
        PetWindow? window = null;
        try
        {
            window = new PetWindow(140, v3Assets, enableAnimation: true)
            {
                DragMode = DragMode.Immediate,
                EnableDragPose = true,
            };

            var rect = new PetWindow.PhysicalRect(100, 100, 140, 140);
            Assert(Math.Abs(window.ImageControl.RenderTransformOrigin.X - 0.5) < 0.001 &&
                   Math.Abs(window.ImageControl.RenderTransformOrigin.Y - 1.0) < 0.001,
                   "Initial RenderTransformOrigin is (0.5, 1.0) anchored at bottom center");
            Assert(window.RotateTransform.Angle == 0.0, "Initial RotateTransform.Angle is 0.0");

            // 开始拖动
            window.HandleMouseDown(120, 120, rect);
            window.HandleMouseMove(140, 140); // 移动 20px > 4px slop
            Assert(window.IsDragging, "Window enters dragging");
            Assert(Math.Abs(window.ImageControl.RenderTransformOrigin.X - 0.51) < 0.01 &&
                   Math.Abs(window.ImageControl.RenderTransformOrigin.Y - 0.14) < 0.01,
                   "Dragging switches RenderTransformOrigin to collar/hanging point (0.51, 0.14)");

            // 推进动画帧 250ms
            typeof(PetWindow).GetField("_lastTickTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(window, DateTime.UtcNow.AddMilliseconds(-250));
            typeof(PetWindow).GetMethod("OnAnimationTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, new object?[] { null, EventArgs.Empty });

            Assert(Math.Abs(window.RotateTransform.Angle - 5.0) < 0.1,
                $"Window RotateTransform.Angle swings to ~5.0° on tick during drag (actual: {window.RotateTransform.Angle:F2}°)");
            Assert(window.ScaleTransform.ScaleY == 1.0, "Breathing scale locked to 1.0 during drag");

            // 鼠标松开释放
            window.HandleMouseUp(140, 140);
            Assert(!window.IsDragging, "Window leaves dragging after mouse up");
            Assert(Math.Abs(window.ImageControl.RenderTransformOrigin.X - 0.5) < 0.001 &&
                   Math.Abs(window.ImageControl.RenderTransformOrigin.Y - 1.0) < 0.001,
                   "Mouse up restores RenderTransformOrigin to (0.5, 1.0)");
            Assert(window.RotateTransform.Angle == 0.0, "Mouse up resets RotateTransform.Angle to 0.0");

            // 丢失捕获释放
            window.HandleMouseDown(120, 120, rect);
            window.HandleMouseMove(140, 140);
            typeof(PetWindow).GetField("_lastTickTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(window, DateTime.UtcNow.AddMilliseconds(-250));
            typeof(PetWindow).GetMethod("OnAnimationTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, new object?[] { null, EventArgs.Empty });
            Assert(Math.Abs(window.RotateTransform.Angle) > 1.0, "Angle swung during second drag");

            window.HandleLostCapture();
            Assert(Math.Abs(window.ImageControl.RenderTransformOrigin.X - 0.5) < 0.001 &&
                   Math.Abs(window.ImageControl.RenderTransformOrigin.Y - 1.0) < 0.001,
                   "Lost capture restores RenderTransformOrigin to (0.5, 1.0)");
            Assert(window.RotateTransform.Angle == 0.0, "Lost capture resets RotateTransform.Angle to 0.0");

            // 拖动中动态关闭拖动姿势 (EnableDragPose = false)
            window.HandleMouseDown(120, 120, rect);
            window.HandleMouseMove(140, 140);
            window.EnableDragPose = false;
            Assert(window.RotateTransform.Angle == 0.0, "Toggling EnableDragPose=false during drag immediately cancels rotation to 0.0");
            Assert(Math.Abs(window.ImageControl.RenderTransformOrigin.X - 0.5) < 0.001 &&
                   Math.Abs(window.ImageControl.RenderTransformOrigin.Y - 1.0) < 0.001,
                   "Toggling EnableDragPose=false restores origin to (0.5, 1.0)");
            Assert(ReferenceEquals(window.ImageControl.Source, v3Assets.IdleBitmap), "Toggling EnableDragPose=false during drag restores IdleBitmap");
            window.HandleMouseUp(140, 140);

            // 拖动中动态关闭总动画 (EnableAnimation = false)
            window.EnableDragPose = true;
            window.HandleMouseDown(120, 120, rect);
            window.HandleMouseMove(140, 140);
            window.EnableAnimation = false;
            Assert(window.RotateTransform.Angle == 0.0, "Toggling EnableAnimation=false during drag immediately cancels rotation to 0.0");
            Assert(ReferenceEquals(window.ImageControl.Source, v3Assets.IdleBitmap), "Toggling EnableAnimation=false during drag restores IdleBitmap");
            window.HandleMouseUp(140, 140);
        }
        finally
        {
            window?.Close();
        }
    }

    // =========================================================================
    // 10. Tracked Wheel Session SDK 1.9 & Pointing Poses
    // =========================================================================
    private static void TestTrackedWheelSessionAndPointing()
    {
        Console.WriteLine("\n--- 10. Tracked Wheel Session SDK 1.9 & Pointing Poses ---");

        // 10.1 角度到 8 方向指向帧的最近邻映射算法
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(0.0) == PetFrameKind.PointE, "0° maps to PointE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(45.0) == PetFrameKind.PointSE, "45° maps to PointSE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(90.0) == PetFrameKind.PointS, "90° maps to PointS");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(135.0) == PetFrameKind.PointSW, "135° maps to PointSW");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(180.0) == PetFrameKind.PointW, "180° maps to PointW");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(225.0) == PetFrameKind.PointNW, "225° maps to PointNW");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(270.0) == PetFrameKind.PointN, "270° maps to PointN");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(315.0) == PetFrameKind.PointNE, "315° maps to PointNE");

        // 边界值测试 (22.5° 容差带)
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(22.4) == PetFrameKind.PointE, "22.4° (< 22.5°) maps to PointE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(22.6) == PetFrameKind.PointSE, "22.6° (> 22.5°) maps to PointSE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(337.4) == PetFrameKind.PointNE, "337.4° (< 337.5°) maps to PointNE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(337.6) == PetFrameKind.PointE, "337.6° (> 337.5°) maps to PointE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(-45.0) == PetFrameKind.PointNE, "-45° wraps to PointNE");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(360.0 + 90.0) == PetFrameKind.PointS, "450° wraps to PointS");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(double.NaN) == PetFrameKind.Idle, "NaN angle maps to Idle");
        Assert(PetAnimationStateMachine.MapAngleToPointingFrame(double.PositiveInfinity) == PetFrameKind.Idle, "Infinity angle maps to Idle");

        // 10.2 生产 PetController 接入 IHostWheelSessionService
        string tempDir = Path.Combine(Path.GetTempPath(), "StarPie_PointingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CreateMinimalPng(Path.Combine(tempDir, "idle.png"), 4, 4, 255, 0, 0);
            CreateMinimalPng(Path.Combine(tempDir, "drag.png"), 4, 4, 0, 255, 0);
            CreateMinimalPng(Path.Combine(tempDir, "point_E.png"), 4, 4, 1, 1, 1);
            CreateMinimalPng(Path.Combine(tempDir, "point_S.png"), 4, 4, 2, 2, 2);
            CreateMinimalPng(Path.Combine(tempDir, "point_W.png"), 4, 4, 3, 3, 3);
            CreateMinimalPng(Path.Combine(tempDir, "point_N.png"), 4, 4, 4, 4, 4);

            var sessionService = new MockHostWheelSessionService();
            var context = new MockPluginContext(wheel: sessionService);
            context.Settings.Set(PetPreference.AssetDirectoryKey, tempDir);
            context.Settings.Set(PetPreference.EnablePointingPoseKey, "true");

            var controller = new PetController(context) { IsHeadlessTestMode = true };
            try
            {
                bool shown = controller.Show();
                Assert(shown, "PetController.Show succeeded");
                var window = controller.Window!;

                // 10.2.1 模拟轻点呼出轮盘
                window.PerformClickAction();
                Assert(sessionService.RequestTrackedWheelCalls == 1, "Click requests tracked wheel session via IHostWheelSessionService");
                Assert(sessionService.LastStateCallback != null, "State callback registered");
                Assert(sessionService.LastSelectionCallback != null, "Selection callback registered");

                // 呈现完成
                sessionService.LastStateCallback!(new WheelSessionStateChangedEventArgs
                {
                    SessionId = "session-1",
                    State = WheelSessionState.Presented,
                });

                // 模拟光标指向南方 (90°)
                sessionService.LastSelectionCallback!(new WheelSelectionChangedEventArgs
                {
                    SessionId = "session-1",
                    PhysicalCenterX = 100,
                    PhysicalCenterY = 100,
                    IsEmptySelection = false,
                    AngleDegrees = 90.0,
                    SectorIndex = 2,
                    TotalSectors = 8,
                });
                Assert(window.StateMachine.CurrentFrame == PetFrameKind.PointS, "Window state machine switched to PointS on 90° selection");
                Assert(!ReferenceEquals(window.ImageControl.Source, controller.Assets.IdleBitmap), "Image source changed to PointS bitmap");

                // 模拟光标移入中心死区 (IsEmptySelection = true)
                sessionService.LastSelectionCallback!(new WheelSelectionChangedEventArgs
                {
                    SessionId = "session-1",
                    PhysicalCenterX = 100,
                    PhysicalCenterY = 100,
                    IsEmptySelection = true,
                    AngleDegrees = double.NaN,
                    SectorIndex = -1,
                    TotalSectors = 8,
                });
                Assert(window.StateMachine.CurrentFrame == PetFrameKind.Idle, "Empty selection clears pointing and returns to Idle");
                Assert(ReferenceEquals(window.ImageControl.Source, controller.Assets.IdleBitmap), "Image source restored to IdleBitmap on empty selection");

                // 模拟光标指向东方 (0°)
                sessionService.LastSelectionCallback!(new WheelSelectionChangedEventArgs
                {
                    SessionId = "session-1",
                    PhysicalCenterX = 100,
                    PhysicalCenterY = 100,
                    IsEmptySelection = false,
                    AngleDegrees = 0.0,
                    SectorIndex = 0,
                    TotalSectors = 8,
                });
                Assert(window.StateMachine.CurrentFrame == PetFrameKind.PointE, "Window state machine switched to PointE on 0° selection");

                // 10.2.2 拖动开始打断轮盘会话与指向（互斥保证）
                var rect = new PetWindow.PhysicalRect(100, 100, 140, 140);
                window.HandleMouseDown(120, 120, rect);
                window.HandleMouseMove(140, 140); // 触发拖拽
                Assert(window.IsDragging, "Window enters dragging");
                Assert(sessionService.TokenDisposed, "Active wheel session token was disposed when dragging started");
                Assert(!window.StateMachine.IsPointing, "Pointing mode cancelled when dragging started");
                Assert(window.StateMachine.CurrentFrame == PetFrameKind.Drag, "Frame switches to Drag when dragging starts");

                window.HandleMouseUp(140, 140);
                Assert(window.StateMachine.CurrentFrame == PetFrameKind.Idle, "Frame restores to Idle after drag ended");

                // 10.2.3 禁用指向姿势开关 (EnablePointingPose = false)
                controller.SetEnablePointingPose(false);
                Assert(!window.EnablePointingPose, "EnablePointingPose is false");

                // 再次呼出轮盘并悬停
                window.PerformClickAction();
                Assert(sessionService.RequestTrackedWheelCalls == 2, "Second wheel session requested");
                sessionService.LastSelectionCallback!(new WheelSelectionChangedEventArgs
                {
                    SessionId = "session-2",
                    PhysicalCenterX = 100,
                    PhysicalCenterY = 100,
                    IsEmptySelection = false,
                    AngleDegrees = 90.0,
                    SectorIndex = 2,
                    TotalSectors = 8,
                });
                Assert(ReferenceEquals(window.ImageControl.Source, controller.Assets.IdleBitmap),
                    "When EnablePointingPose is false, ImageControl strictly displays IdleBitmap");

                // 轮盘关闭通知恢复
                sessionService.LastStateCallback!(new WheelSessionStateChangedEventArgs
                {
                    SessionId = "session-2",
                    State = WheelSessionState.Closed,
                });
                Assert(!window.StateMachine.IsPointing, "Pointing is inactive after session closed");
            }
            finally
            {
                controller.Shutdown();
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // =========================================================================
    // 11. Local Pet Library Import, Preview & Safe Switching (SP-DESKTOPPET-LIBRARY-001)
    // =========================================================================
    private static void TestLibraryManagementAndSafeSwitching()
    {
        Console.WriteLine("\n--- 11. Library Import, 12-State Preview & Safe Switching ---");

        string sandbox = Path.Combine(Path.GetTempPath(), "starpie-libtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);

        try
        {
            string dataDir = Path.Combine(sandbox, "data");
            Directory.CreateDirectory(dataDir);

            string srcFolder1 = Path.Combine(sandbox, "src1");
            Directory.CreateDirectory(srcFolder1);
            // Folder 1: Valid 512x512 with idle, blink, react, drag, and some pointing frames
            CreateMinimalPng(Path.Combine(srcFolder1, "idle.png"), 512, 512, 100, 150, 200);
            CreateMinimalPng(Path.Combine(srcFolder1, "blink.png"), 512, 512, 110, 160, 210);
            CreateMinimalPng(Path.Combine(srcFolder1, "react.png"), 512, 512, 120, 170, 220);
            CreateMinimalPng(Path.Combine(srcFolder1, "drag.png"), 512, 512, 130, 180, 230);
            CreateMinimalPng(Path.Combine(srcFolder1, "point_N.png"), 512, 512, 140, 190, 240);
            File.WriteAllText(Path.Combine(srcFolder1, "asset-info.json"), "{\"name\":\"Character Alpha\",\"id\":\"char-alpha\"}");

            string srcFolder2 = Path.Combine(sandbox, "src2");
            Directory.CreateDirectory(srcFolder2);
            // Folder 2: 1024x1024 canvas (tests >512 whole-canvas scaling)
            CreateMinimalPng(Path.Combine(srcFolder2, "idle.png"), 1024, 1024, 50, 60, 70);
            CreateMinimalPng(Path.Combine(srcFolder2, "react.png"), 1024, 1024, 55, 65, 75);
            File.WriteAllText(Path.Combine(srcFolder2, "asset-info.json"), "{\"name\":\"Character Beta\",\"id\":\"char-beta\"}");

            var manager = new PetLibraryManager(dataDir);
            Assert(manager.GetItems().Count == 0, "Initial library is empty");

            // 11.1 导入文件夹 1
            var res1 = manager.ImportFolderAsync(srcFolder1).GetAwaiter().GetResult();
            Assert(res1.Success, "Folder 1 imported successfully");
            Assert(!res1.IsDuplicate, "Folder 1 is not duplicate");
            Assert(res1.Item != null && res1.Item.Name == "Character Alpha", "Folder 1 name read from asset-info.json");
            Assert(res1.Item!.CanvasWidth == 512 && res1.Item.CanvasHeight == 512, "Folder 1 preserves 512 canvas size");
            Assert(res1.Item.AvailableFrames.Contains("idle.png") && res1.Item.AvailableFrames.Contains("point_N.png"), "Folder 1 contains expected frames");

            // 11.2 重复导入文件夹 1 (内容指纹命中)
            var dup1 = manager.ImportFolderAsync(srcFolder1).GetAwaiter().GetResult();
            Assert(dup1.Success && dup1.IsDuplicate, "Re-importing Folder 1 detected as duplicate");
            Assert(dup1.Item?.Id == res1.Item.Id, "Duplicate returns existing item ID");
            Assert(manager.GetItems().Count == 1, "Duplicate does not add second entry in index");

            // 11.3 导入文件夹 2 (大画布 1024x1024 缩放至 512)
            var res2 = manager.ImportFolderAsync(srcFolder2).GetAwaiter().GetResult();
            Assert(res2.Success, "Folder 2 imported successfully");
            Assert(res2.Item!.CanvasWidth == 512 && res2.Item.CanvasHeight == 512, "Folder 2 whole canvas scaled down to 512");
            Assert(manager.GetItems().Count == 2, "Library now has 2 items in index");

            // 11.4 缩略图与 12 状态预览加载
            var thumb = manager.LoadThumbnail(res1.Item);
            Assert(thumb != null && thumb.IsFrozen, "Thumbnail loaded and frozen");
            Assert(thumb!.PixelWidth <= 96, "Thumbnail width clamped to small size");

            var prevIdle = manager.LoadPreviewFrame(res1.Item, "idle.png");
            Assert(prevIdle != null && prevIdle.IsFrozen, "Preview idle.png loaded and frozen");

            var prevPointN = manager.LoadPreviewFrame(res1.Item, "point_N.png");
            Assert(prevPointN != null && prevPointN.IsFrozen, "Preview point_N.png loaded");

            // 请求不存在的图帧回退到 idle.png
            var prevMissing = manager.LoadPreviewFrame(res1.Item, "point_S.png");
            Assert(prevMissing != null && prevMissing.IsFrozen, "Missing frame falls back to idle.png");

            // 11.5 异常与校验拦截
            // 11.5.1 缺少 idle.png
            string badDir1 = Path.Combine(sandbox, "bad1");
            Directory.CreateDirectory(badDir1);
            CreateMinimalPng(Path.Combine(badDir1, "blink.png"), 512, 512);
            var badRes1 = manager.ImportFolderAsync(badDir1).GetAwaiter().GetResult();
            Assert(!badRes1.Success, "Folder without idle.png is rejected");
            Assert(badRes1.Message.Contains("idle.png"), "Error message clarifies missing idle.png");

            // 11.5.2 画布尺寸不一致 (idle 512x512 vs blink 300x300)
            string badDir2 = Path.Combine(sandbox, "bad2");
            Directory.CreateDirectory(badDir2);
            CreateMinimalPng(Path.Combine(badDir2, "idle.png"), 512, 512);
            CreateMinimalPng(Path.Combine(badDir2, "blink.png"), 300, 300);
            var badRes2 = manager.ImportFolderAsync(badDir2).GetAwaiter().GetResult();
            Assert(!badRes2.Success, "Folder with canvas mismatch is rejected");
            Assert(badRes2.Message.Contains("画布尺寸不一致"), "Error message clarifies canvas mismatch");

            // 11.5.3 损坏的 PNG 文件
            string badDir3 = Path.Combine(sandbox, "bad3");
            Directory.CreateDirectory(badDir3);
            File.WriteAllBytes(Path.Combine(badDir3, "idle.png"), new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var badRes3 = manager.ImportFolderAsync(badDir3).GetAwaiter().GetResult();
            Assert(!badRes3.Success, "Corrupt PNG is rejected");

            // 11.5.4 导入取消
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool cancelledCaught = false;
                try
                {
                    manager.ImportFolderAsync(srcFolder1, cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    cancelledCaught = true;
                }
                Assert(cancelledCaught, "Import throws OperationCanceledException when token cancelled");
            }

            // 11.6 控制器安全应用与 Busy 保护
            var sessionService = new MockHostWheelSessionService();
            var settings = new TriggerableFakeSettings();
            var ctx = new MockPluginContext(
                wheel: sessionService,
                settings: settings,
                dataDirectory: dataDir);

            var controller = new PetController(ctx) { IsHeadlessTestMode = true };

            // 11.6.1 导入并不自动激活或显示桌宠
            Assert(string.IsNullOrEmpty(PetPreference.ActiveLibraryId(ctx)), "Importing does not set ActiveLibraryId");
            Assert(!controller.HasWindow, "Importing does not show window");

            // 显示初始桌宠 (使用 Item1)
            string item1Dir = Path.Combine(manager.LibraryRoot, res1.Item.RelativeDirectory);
            bool shown = controller.Show(customAssetDir: item1Dir);
            Assert(shown && controller.HasWindow, "Controller shown initially with Item 1 assets");
            Assert(controller.Assets.PixelWidth == 512, "AssetManager loaded Item 1");

            var window = controller.Window!;

            // 11.6.2 空闲时应用 Item 2
            var applyRes2 = controller.ApplyLibraryItem(res2.Item);
            Assert(applyRes2.IsSuccess, "ApplyLibraryItem succeeds when idle");
            Assert(PetPreference.ActiveLibraryId(ctx) == res2.Item.Id, "ActiveLibraryId updated in settings");
            Assert(settings.Get(PetPreference.AssetDirectoryKey) == Path.Combine(manager.LibraryRoot, res2.Item.RelativeDirectory), "AssetDirectory updated in settings");
            Assert(controller.Assets.PixelWidth == 512, "Controller refreshed assets to Item 2");

            // 11.6.3 Busy 保护：拖动中拒绝切换
            window.HandleMouseDown(100, 100, new PetWindow.PhysicalRect(0, 0, 140, 140));
            window.HandleMouseMove(150, 150);
            Assert(controller.IsBusy, "Controller is busy during mouse down / drag");
            var busyRes1 = controller.ApplyLibraryItem(res1.Item);
            Assert(busyRes1.IsBusy, "ApplyLibraryItem returns Busy during mouse down");
            Assert(PetPreference.ActiveLibraryId(ctx) == res2.Item.Id, "Settings remain unchanged on busy reject");

            // 释放拖动鼠标（位移超出位移阈值，不触发轻点轮盘）
            window.HandleMouseUp(150, 150);
            Assert(!controller.IsBusy, "Controller is not busy after mouse up");

            // 11.6.4 Busy 保护：活动追踪轮盘会话中拒绝切换
            window.PerformClickAction();
            Assert(sessionService.RequestTrackedWheelCalls == 1, "Tracked wheel session requested");
            Assert(controller.IsBusy, "Controller is busy while wheel session is active");

            var busyRes2 = controller.ApplyLibraryItem(res1.Item);
            Assert(busyRes2.IsBusy, "ApplyLibraryItem returns Busy during active wheel session");
            Assert(PetPreference.ActiveLibraryId(ctx) == res2.Item.Id, "ActiveLibraryId remains Item 2 on busy reject");

            // 结束轮盘会话
            sessionService.LastStateCallback!(new WheelSessionStateChangedEventArgs
            {
                SessionId = "req-1",
                State = WheelSessionState.Closed,
            });
            Assert(!controller.IsBusy, "Controller is not busy after wheel session closed");

            // 11.6.5 隐藏状态下应用切换：不偷偷弹出窗口
            controller.Hide();
            Assert(!controller.HasWindow, "Pet is hidden");
            var applyHidden = controller.ApplyLibraryItem(res1.Item);
            Assert(applyHidden.IsSuccess, "ApplyLibraryItem succeeds when pet is hidden");
            Assert(!controller.HasWindow, "Applying while hidden does NOT secretly show window");
            Assert(PetPreference.ActiveLibraryId(ctx) == res1.Item.Id, "Settings updated to Item 1 while hidden");

            // 11.6.6 管理窗口右键与生命周期关闭
            controller.Show(customAssetDir: item1Dir);
            window = controller.Window!;

            bool manageRequestedFired = false;
            window.ManageRequested += () => manageRequestedFired = true;
            window.TriggerManageRequested();
            Assert(manageRequestedFired, "Right-click / TriggerManageRequested fires ManageRequested");

            controller.OpenManagerWindow();
            Assert(controller.LibraryWindow != null, "Manager window created on OpenManagerWindow");
            var libWin = controller.LibraryWindow;

            // 再次调用 OpenManagerWindow 复用已有窗口
            controller.OpenManagerWindow();
            Assert(ReferenceEquals(controller.LibraryWindow, libWin), "OpenManagerWindow reuses existing window");

            // 关闭管理窗口
            controller.CloseManagerWindow();
            Assert(controller.LibraryWindow == null, "CloseManagerWindow closes and nulls LibraryWindow");

            // 打开后跟随 Shutdown 销毁
            controller.OpenManagerWindow();
            Assert(controller.LibraryWindow != null, "Manager window opened again");
            controller.Shutdown();
            Assert(controller.LibraryWindow == null, "Shutdown closes LibraryWindow");
            Assert(!controller.HasWindow, "Shutdown closes PetWindow");
            Assert(controller.IsShutdown, "Controller is shutdown");

            // 11.7 真实参考素材只读兼容性验证（如存在）
            string voyanisaFixture = @"G:\Users\2 Better\Desktop\桌宠素材\沃雅妮莎-v1\runtime-512";
            if (Directory.Exists(voyanisaFixture))
            {
                var vRes = manager.ImportFolderAsync(voyanisaFixture).GetAwaiter().GetResult();
                Assert(vRes.Success, "Real fixture 沃雅妮莎-v1 imports successfully");
                Assert(vRes.Item!.CanvasWidth == 512, "沃雅妮莎 canvas width is 512");
            }

            // 11.8 R2 返修：设置持久化失败与静默失败回滚、索引事务回滚与 Shutdown 保护
            string r2Sandbox = Path.Combine(sandbox, "r2-tests");
            Directory.CreateDirectory(r2Sandbox);
            string r2FolderA = Path.Combine(r2Sandbox, "itemA");
            string r2FolderB = Path.Combine(r2Sandbox, "itemB");
            Directory.CreateDirectory(r2FolderA);
            Directory.CreateDirectory(r2FolderB);
            CreateMinimalPng(Path.Combine(r2FolderA, "idle.png"), 512, 512, 100, 100, 100);
            CreateMinimalPng(Path.Combine(r2FolderB, "idle.png"), 512, 512, 200, 200, 200);

            // 11.8.1 索引破坏性写入事务回滚
            string r2BrokenDir = Path.Combine(r2Sandbox, "broken-index-mgr");
            var brokenMgr = new PetLibraryManager(r2BrokenDir);
            Directory.CreateDirectory(brokenMgr.IndexFilePath); // 阻断索引保存为文件
            var brokenRes = brokenMgr.ImportFolderAsync(r2FolderA).GetAwaiter().GetResult();
            Assert(!brokenRes.Success, "Import fails when index cannot be saved");
            Assert(brokenMgr.GetItems().Count == 0, "No cached items retained after index failure");
            int orphanCount = Directory.GetDirectories(brokenMgr.LibraryRoot)
                .Count(p => !p.EndsWith("library-index.json", StringComparison.OrdinalIgnoreCase));
            Assert(orphanCount == 0, "No orphan directories left after index save failure");

            // 11.8.2 Save 抛出异常时的回滚
            var failingSettings = new FailingMockSettings();
            var failCtx = new MockPluginContext(settings: failingSettings, dataDirectory: Path.Combine(r2Sandbox, "fail-data"));
            var failController = new PetController(failCtx) { IsHeadlessTestMode = true };
            var r2ItemA = failController.LibraryManager.ImportFolderAsync(r2FolderA).GetAwaiter().GetResult().Item!;
            var r2ItemB = failController.LibraryManager.ImportFolderAsync(r2FolderB).GetAwaiter().GetResult().Item!;

            var initApply = failController.ApplyLibraryItem(r2ItemA);
            Assert(initApply.IsSuccess, "Initial apply succeeded");
            string? assetDirBeforeFail = failController.Assets.CurrentDirectory;

            failingSettings.ThrowOnSave = true;
            var failApplyResult = failController.ApplyLibraryItem(r2ItemB);
            Assert(!failApplyResult.IsSuccess, "ApplyLibraryItem returns Fail when Save throws exception");
            Assert(failController.Assets.CurrentDirectory == assetDirBeforeFail, "Assets restored to previous directory on Save exception");
            Assert(failingSettings.Get(PetPreference.ActiveLibraryIdKey) == r2ItemA.Id, "ActiveLibraryId setting rolled back on Save exception");
            Assert(failingSettings.Get(PetPreference.AssetDirectoryKey) == assetDirBeforeFail, "AssetDirectory setting rolled back on Save exception");

            // 11.8.3 Save 静默失败（未触发持久化变更通知）时的回滚与拒绝
            failingSettings.ThrowOnSave = false;
            failingSettings.SilentFailure = true;
            var silentApplyResult = failController.ApplyLibraryItem(r2ItemB);
            Assert(!silentApplyResult.IsSuccess, "ApplyLibraryItem returns Fail when Save fails silently without persistence notification");
            Assert(failController.Assets.CurrentDirectory == assetDirBeforeFail, "Assets restored to previous directory on silent Save failure");
            Assert(failingSettings.Get(PetPreference.ActiveLibraryIdKey) == r2ItemA.Id, "ActiveLibraryId setting rolled back on silent Save failure");

            // 11.8.4 Shutdown 保护
            failController.Shutdown();
            var afterShutdownApply = failController.ApplyLibraryItem(r2ItemA);
            Assert(!afterShutdownApply.IsSuccess, "ApplyLibraryItem returns Fail after Shutdown");
            var afterShutdownAsync = failController.ApplyLibraryItemAsync(r2ItemA).GetAwaiter().GetResult();
            Assert(!afterShutdownAsync.IsSuccess, "ApplyLibraryItemAsync returns Fail after Shutdown");

            // 11.9 R3 返修验证：真异步后台解码快照、请求乱序代际守卫、已有目录保护与取消安全
            string r3Sandbox = Path.Combine(sandbox, "r3-tests");
            Directory.CreateDirectory(r3Sandbox);
            string r3FolderA = Path.Combine(r3Sandbox, "itemA");
            string r3FolderB = Path.Combine(r3Sandbox, "itemB");
            Directory.CreateDirectory(r3FolderA);
            Directory.CreateDirectory(r3FolderB);
            CreateMinimalPng(Path.Combine(r3FolderA, "idle.png"), 512, 512, 120, 120, 120);
            CreateMinimalPng(Path.Combine(r3FolderB, "idle.png"), 512, 512, 180, 180, 180);

            var r3Settings = new TriggerableFakeSettings();
            var r3Ctx = new MockPluginContext(settings: r3Settings, dataDirectory: Path.Combine(r3Sandbox, "r3-data"));
            var r3Controller = new PetController(r3Ctx) { IsHeadlessTestMode = true };
            var r3ItemA = r3Controller.LibraryManager.ImportFolderAsync(r3FolderA).GetAwaiter().GetResult().Item!;
            var r3ItemB = r3Controller.LibraryManager.ImportFolderAsync(r3FolderB).GetAwaiter().GetResult().Item!;

            // 11.9.1 真异步后台准备与快照冻结验证
            var (loadRes, snap) = PetAssetManager.LoadSnapshotFromDirectory(Path.Combine(r3Controller.LibraryManager.LibraryRoot, r3ItemA.RelativeDirectory));
            Assert(loadRes.Success, "LoadSnapshotFromDirectory succeeds independently");
            Assert(snap != null && snap.IdleBitmap != null && snap.IdleBitmap.IsFrozen, "Idle bitmap in snapshot is frozen");
            Assert(r3Controller.Assets.CurrentDirectory == null, "AssetManager CurrentDirectory is unchanged by background snapshot loading");

            // 11.9.2 异步生产入口 ApplyLibraryItemAsync
            var r3ApplyAsync = r3Controller.ApplyLibraryItemAsync(r3ItemA).GetAwaiter().GetResult();
            Assert(r3ApplyAsync.IsSuccess, "ApplyLibraryItemAsync completes successfully");
            Assert(r3Controller.Assets.CurrentDirectory == Path.Combine(r3Controller.LibraryManager.LibraryRoot, r3ItemA.RelativeDirectory), "Assets updated after async apply");

            // 11.9.3 既有目录保护：目标目录存在有效 idle.png 时不进行破坏性删除
            string targetDirA = Path.Combine(r3Controller.LibraryManager.LibraryRoot, r3ItemA.RelativeDirectory);
            string markerFile = Path.Combine(targetDirA, "marker.txt");
            File.WriteAllText(markerFile, "protected-data");
            var duplicateImport = r3Controller.LibraryManager.ImportFolderAsync(r3FolderA).GetAwaiter().GetResult();
            Assert(duplicateImport.IsDuplicate, "Importing folder with existing target returns Duplicate or Fail");
            Assert(File.Exists(markerFile), "Existing valid directory content (marker.txt) was NOT deleted");

            // 11.9.4 取消令牌安全响应
            using var ctsCanceled = new CancellationTokenSource();
            ctsCanceled.Cancel();
            bool caughtCancel = false;
            try
            {
                r3Controller.ApplyLibraryItemAsync(r3ItemB, ctsCanceled.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                caughtCancel = true;
            }
            Assert(caughtCancel, "ApplyLibraryItemAsync responds to pre-canceled token with OperationCanceledException");
            Assert(r3Controller.Assets.CurrentDirectory == Path.Combine(r3Controller.LibraryManager.LibraryRoot, r3ItemA.RelativeDirectory), "Assets unchanged when canceled");

            // 11.10 R4: UI 线程零磁盘 IO、有界分页容量与 i18n 多语言消息格式化
            // 11.10.1 UI 线程零磁盘 IO：ApplyLibraryItemAsync 在后台工作线程执行持久化 Save()
            int currentThreadId = Environment.CurrentManagedThreadId;
            var threadSettings = new ThreadRecordingFakeSettings();
            var r4Context = new MockPluginContext(dataDirectory: Path.Combine(sandbox, "r4-data"), settings: threadSettings);
            var r4Controller = new PetController(r4Context) { IsHeadlessTestMode = true };
            var r4ImportRes = r4Controller.LibraryManager.ImportFolderAsync(r3FolderA).GetAwaiter().GetResult();
            Assert(r4ImportRes.Success, "R4 test item imported successfully");

            var r4ApplyAsync = r4Controller.ApplyLibraryItemAsync(r4ImportRes.Item!).GetAwaiter().GetResult();
            Assert(r4ApplyAsync.IsSuccess, "R4 ApplyLibraryItemAsync succeeded");
            Assert(threadSettings.SaveThreadId != 0, "Settings.Save() was called during apply");
            Assert(threadSettings.SaveThreadId != currentThreadId, "Settings.Save() was executed on a background worker thread, NOT UI thread");

            // 11.10.2 有界分页容量：即使形象库有 30 个条目，界面卡片数依然被限制在 <= 24
            for (int i = 0; i < 28; i++)
            {
                string dummyFolder = Path.Combine(sandbox, $"dummy-{i}");
                Directory.CreateDirectory(dummyFolder);
                CreateMinimalPng(Path.Combine(dummyFolder, "idle.png"), 16, 16, (byte)(i * 8), 100, 150);
                r4Controller.LibraryManager.ImportFolderAsync(dummyFolder).GetAwaiter().GetResult();
            }
            int totalItemsCount = r4Controller.LibraryManager.GetItems().Count;
            Assert(totalItemsCount >= 28, "Library has >= 28 items total");

            r4Controller.OpenManagerWindow();
            Assert(r4Controller.LibraryWindow != null, "Manager window opened for pagination verification");
            var r4LibWin = r4Controller.LibraryWindow!;
            Assert(r4LibWin.CardCount <= PetLibraryWindow.MaxPageCards, $"CardCount ({r4LibWin.CardCount}) is strictly capped by MaxPageCards ({PetLibraryWindow.MaxPageCards})");
            Assert(r4LibWin.TotalPages > 1, "TotalPages reflects multi-page pagination");
            r4Controller.CloseManagerWindow();

            // 11.10.3 消息 i18n 格式化：在英文环境下输出英文字符串，无裸中文
            var enInfo = new MockHostInfo { LanguageCode = "en" };
            var zhInfo = new MockHostInfo { LanguageCode = "zh-CN" };
            var enContext = new MockPluginContext(dataDirectory: Path.Combine(sandbox, "en-data"), info: enInfo);
            var zhContext = new MockPluginContext(dataDirectory: Path.Combine(sandbox, "zh-data"), info: zhInfo);
            DesktopPetPlugin.RegisterLocalization(enContext);
            DesktopPetPlugin.RegisterLocalization(zhContext);

            var okApplyResult = PetApplyResult.Ok("test-id", "已切换至形象「Test」。", "controller.apply.success", "Test");
            string enApplyMsg = okApplyResult.FormatMessage(enContext);
            string zhApplyMsg = okApplyResult.FormatMessage(zhContext);
            Assert(enApplyMsg.Contains("Switched to character 'Test'."), $"English message formatting: {enApplyMsg}");
            Assert(zhApplyMsg.Contains("已切换至形象「Test」。"), $"Chinese message formatting: {zhApplyMsg}");

            var failImportResult = PetLibraryImportResult.Fail("素材文件夹不存在：D:\\nonexistent", "library.import.not_found", "D:\\nonexistent");
            string enImportMsg = failImportResult.FormatMessage(enContext);
            string zhImportMsg = failImportResult.FormatMessage(zhContext);
            Assert(enImportMsg.Contains("Asset directory does not exist: D:\\nonexistent"), $"English import fail formatting: {enImportMsg}");
            Assert(zhImportMsg.Contains("素材文件夹不存在：D:\\nonexistent"), $"Chinese import fail formatting: {zhImportMsg}");

            r4Controller.Shutdown();

            r3Controller.Shutdown();
        }
        finally
        {
            if (Directory.Exists(sandbox))
            {
                try { Directory.Delete(sandbox, true); } catch { }
            }
        }
    }

    private static void CreateMinimalPng(string filePath, int width, int height, byte b = 255, byte g = 255, byte r = 255)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;       // B
            pixels[i + 1] = g;   // G
            pixels[i + 2] = r;   // R
            pixels[i + 3] = 255; // A (不透明)
        }

        var source = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }
}

// =============================================================================
// Mock / Stubs for Testing
// =============================================================================

internal sealed class MockPluginContext : IPluginContext
{
    private readonly string? _customDataDir;
    public PluginMetadata Me { get; } = new() { Id = "io.github.softblack42.desktoppet" };
    public string PluginDirectory => AppContext.BaseDirectory;
    public string DataDirectory => _customDataDir ?? AppContext.BaseDirectory;
    public IPluginLogger Log { get; } = new MockPluginLogger();
    public IPluginSettings Settings { get; }
    public MockActionRegistry MockActions { get; } = new();
    public IActionRegistry Actions => MockActions;
    public II18nRegistry I18n { get; }
    public IIconRegistry Icons { get; } = new MockIconRegistry();
    public MockSettingsPageRegistry MockSettingsPage { get; } = new();
    public ISettingsPageRegistry SettingsPage => MockSettingsPage;
    public IHostActionInvoker Host => throw new NotImplementedException();
    public IHostCommandService Commands => throw new NotImplementedException();
    public IHostShellService Shell => throw new NotImplementedException();
    public IHostWindowService Windows => throw new NotImplementedException();
    public IHostScreenCaptureService ScreenCapture => throw new NotImplementedException();
    public IHostSystemService System => throw new NotImplementedException();
    public IHostWheelService Wheel { get; }
    public IHostKeyboardRemapService KeyboardRemap => throw new NotImplementedException();
    public MockHostInfo MockInfo { get; }
    public IHostInfo Info => MockInfo;
    public INotificationService Notify { get; } = new MockNotificationService();
    public IPluginEvents Events => throw new NotImplementedException();
    public IDispatcherFacade Dispatcher { get; }

    public MockPluginContext(
        IHostWheelService? wheel = null,
        IDispatcherFacade? dispatcher = null,
        IPluginSettings? settings = null,
        MockHostInfo? info = null,
        string? dataDirectory = null)
    {
        _customDataDir = dataDirectory;
        Wheel = wheel ?? new MockHostWheelService();
        Dispatcher = dispatcher ?? new MockDispatcherFacade();
        Settings = settings ?? new TriggerableFakeSettings();
        MockInfo = info ?? new MockHostInfo();
        I18n = new MockI18nRegistry(() => MockInfo.LanguageCode);
    }
}

internal sealed class TriggerableFakeSettings : IPluginSettings
{
    private readonly Dictionary<string, string> _dict = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Subscription>> _subscribers = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => _dict.TryGetValue(key, out string? val) ? val : null;

    public void Set(string key, string? value)
    {
        if (value == null) _dict.Remove(key);
        else _dict[key] = value;
    }

    public bool GetBool(string key, bool defaultValue = false) =>
        bool.TryParse(Get(key), out bool v) ? v : defaultValue;

    public int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : defaultValue;

    public double GetDouble(string key, double defaultValue = 0) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : defaultValue;

    private Dictionary<string, string> _persistedSnapshot = new();

    public void Save()
    {
        var prev = _persistedSnapshot;
        _persistedSnapshot = new Dictionary<string, string>(_dict);
        foreach (var key in prev.Keys.Union(_persistedSnapshot.Keys).ToList())
        {
            string? oldVal = prev.GetValueOrDefault(key);
            string? newVal = _persistedSnapshot.GetValueOrDefault(key);
            if (oldVal != newVal)
            {
                TriggerChange(key, oldVal, newVal);
            }
        }
    }

    public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler)
    {
        var sub = new Subscription(this, key, handler);
        if (!_subscribers.TryGetValue(key, out var list))
        {
            list = new List<Subscription>();
            _subscribers[key] = list;
        }
        list.Add(sub);
        return sub;
    }

    public void TriggerChange(string key, string? oldValue, string? newValue)
    {
        Set(key, newValue);
        if (_subscribers.TryGetValue(key, out var list))
        {
            var change = new PluginSettingChanged
            {
                Key = key,
                OldValue = oldValue,
                NewValue = newValue,
            };
            var copy = list.ToArray();
            foreach (var sub in copy)
            {
                if (!sub.IsDisposed)
                {
                    sub.Handler(change);
                }
            }
        }
    }

    public int ActiveSubscriptionCount
    {
        get
        {
            int count = 0;
            foreach (var list in _subscribers.Values)
            {
                foreach (var s in list)
                {
                    if (!s.IsDisposed) count++;
                }
            }
            return count;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly TriggerableFakeSettings _owner;
        private readonly string _key;
        public Action<PluginSettingChanged> Handler { get; }
        public bool IsDisposed { get; private set; }

        public Subscription(TriggerableFakeSettings owner, string key, Action<PluginSettingChanged> handler)
        {
            _owner = owner;
            _key = key;
            Handler = handler;
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (_owner._subscribers.TryGetValue(_key, out var list))
            {
                list.Remove(this);
            }
        }
    }
}

internal sealed class QueueableFakeDispatcher : IDispatcherFacade
{
    private readonly Queue<Action> _queue = new();

    public bool IsOnUiThread => true;
    public int QueuedCount => _queue.Count;

    public void Post(Action action)
    {
        _queue.Enqueue(action);
    }

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Drain()
    {
        while (_queue.Count > 0)
        {
            var action = _queue.Dequeue();
            action();
        }
    }

    public void Clear()
    {
        _queue.Clear();
    }
}

internal sealed class MockHostWheelService : IHostWheelService
{
    public int ShowWheelCalls { get; private set; }
    public double LastX { get; private set; }
    public double LastY { get; private set; }
    public bool AcceptNext { get; set; } = true;

    public bool ShowWheel(double physicalCenterX, double physicalCenterY)
    {
        ShowWheelCalls++;
        LastX = physicalCenterX;
        LastY = physicalCenterY;
        return AcceptNext;
    }

    public bool DismissWheel() => true;
}

internal sealed class MockHostWheelSessionService : IHostWheelSessionService
{
    public int ShowWheelCalls { get; private set; }
    public int RequestTrackedWheelCalls { get; private set; }
    public double LastX { get; private set; }
    public double LastY { get; private set; }
    public bool AcceptNext { get; set; } = true;
    public Action<WheelSessionStateChangedEventArgs>? LastStateCallback { get; private set; }
    public Action<WheelSelectionChangedEventArgs>? LastSelectionCallback { get; private set; }
    public bool TokenDisposed { get; set; }

    public bool ShowWheel(double physicalCenterX, double physicalCenterY)
    {
        ShowWheelCalls++;
        LastX = physicalCenterX;
        LastY = physicalCenterY;
        return AcceptNext;
    }

    public bool DismissWheel() => true;

    public IDisposable? RequestTrackedWheel(
        double physicalCenterX,
        double physicalCenterY,
        Action<WheelSessionStateChangedEventArgs> onStateChanged,
        Action<WheelSelectionChangedEventArgs> onSelectionChanged)
    {
        RequestTrackedWheelCalls++;
        LastX = physicalCenterX;
        LastY = physicalCenterY;
        LastStateCallback = onStateChanged;
        LastSelectionCallback = onSelectionChanged;

        if (!AcceptNext)
        {
            onStateChanged(new WheelSessionStateChangedEventArgs
            {
                SessionId = "mock-rejected",
                State = WheelSessionState.Rejected,
                Reason = "Mock rejection"
            });
            return null;
        }

        onStateChanged(new WheelSessionStateChangedEventArgs
        {
            SessionId = "mock-req",
            State = WheelSessionState.Requested,
        });

        return new MockSessionToken(() =>
        {
            TokenDisposed = true;
            onStateChanged(new WheelSessionStateChangedEventArgs
            {
                SessionId = "mock-closed",
                State = WheelSessionState.Closed,
                Reason = "Token disposed"
            });
        });
    }

    private sealed class MockSessionToken : IDisposable
    {
        private Action? _onDispose;
        public MockSessionToken(Action onDispose) => _onDispose = onDispose;
        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}

internal sealed class MockHostInfo : IHostInfo
{
    public string HostVersion => "1.8.0-beta.5";
    public string ApiVersion => "1.11";
    public string LanguageCode { get; set; } = "zh-CN";
    public bool IsElevated => false;
    public bool IsPortable => false;
    public string HostExecutablePath => @"C:\StarPie\StarPie.exe";
    public bool HasWheelCapability { get; set; } = true;
    public bool HasUiCapability { get; set; } = true;

    public bool HasCapability(PluginCapability capability) => capability switch
    {
        PluginCapability.Wheel => HasWheelCapability,
        PluginCapability.Ui => HasUiCapability,
        _ => true,
    };
}

internal sealed class MockPluginLogger : IPluginLogger
{
    public string LogFilePath => "mock.log";
    public void Debug(string message) { }
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

internal sealed class MockNotificationService : INotificationService
{
    public void Notify(string title, string message) { }
}

internal sealed class MockDispatcherFacade : IDispatcherFacade
{
    public bool IsOnUiThread => true;
    public void Post(Action action) => action();
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

internal sealed class MockI18nRegistry : II18nRegistry
{
    private readonly Func<string> _getLang;
    private readonly Dictionary<string, (string Zh, string? En)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public MockI18nRegistry(Func<string>? getLang = null)
    {
        _getLang = getLang ?? (() => "zh-CN");
    }

    public void Register(string key, string zhCn, string? en = null)
    {
        _entries[key] = (zhCn, en);
    }
    public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table) => _tables[languageCode] = table;
    public string T(string key, string? fallback = null)
    {
        string languageCode = _getLang();
        if (_tables.TryGetValue(languageCode, out var table) && table.TryGetValue(key, out var text)) return text;
        if (_entries.TryGetValue(key, out var entry))
        {
            string lang = _getLang();
            if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(entry.En))
            {
                return entry.En;
            }
            return entry.Zh;
        }
        return fallback ?? key;
    }
}

internal sealed class ThreadRecordingFakeSettings : IPluginSettings
{
    private readonly TriggerableFakeSettings _inner = new();
    public int SaveThreadId { get; private set; }

    public string? Get(string key) => _inner.Get(key);
    public void Set(string key, string? value) => _inner.Set(key, value);
    public bool GetBool(string key, bool defaultValue = false) => _inner.GetBool(key, defaultValue);
    public int GetInt(string key, int defaultValue = 0) => _inner.GetInt(key, defaultValue);
    public double GetDouble(string key, double defaultValue = 0) => _inner.GetDouble(key, defaultValue);
    public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler) => _inner.OnChanged(key, handler);
    public void Save()
    {
        SaveThreadId = Environment.CurrentManagedThreadId;
        _inner.Save();
    }
}

internal sealed class MockIconRegistry : IIconRegistry
{
    public string RegisterSvg(string key, string svgPathData) => "plugin:mock:" + key;
    public string? ResolveKey(string key) => "plugin:mock:" + key;
}

internal sealed class MockActionRegistry : IActionRegistry
{
    public List<IActionContribution> RegisteredActions { get; } = new();
    public IDisposable Register(IActionContribution contribution)
    {
        RegisteredActions.Add(contribution);
        return new DummyDisposable();
    }

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class MockSettingsPageRegistry : ISettingsPageRegistry
{
    public bool HasRegisteredPage { get; private set; }
    public SettingsPageDescriptor? RegisteredPage { get; private set; }
    public IDisposable Register(SettingsPageDescriptor page)
    {
        HasRegisteredPage = true;
        RegisteredPage = page;
        return new DummyDisposable();
    }
    public string? GetValue(string key) => null;

    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class FailingMockSettings : IPluginSettings
{
    private readonly TriggerableFakeSettings _inner = new();
    public bool ThrowOnSave { get; set; }
    public bool SilentFailure { get; set; }

    public string? Get(string key) => _inner.Get(key);
    public void Set(string key, string? value) => _inner.Set(key, value);
    public bool GetBool(string key, bool defaultValue = false) => _inner.GetBool(key, defaultValue);
    public int GetInt(string key, int defaultValue = 0) => _inner.GetInt(key, defaultValue);
    public double GetDouble(string key, double defaultValue = 0) => _inner.GetDouble(key, defaultValue);
    public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler) => _inner.OnChanged(key, handler);

    public void Save()
    {
        if (ThrowOnSave) throw new IOException("Injected Save exception");
        if (SilentFailure) return;
        _inner.Save();
    }
}
