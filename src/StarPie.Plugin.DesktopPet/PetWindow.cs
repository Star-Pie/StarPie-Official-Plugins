using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 桌宠的无边框透明交互窗口。
/// <para>
/// 核心特性：
/// 1. 纯代码构建，无 XAML，透明背景（AllowsTransparency = true，Background = null）；
/// 2. 具备 WS_EX_NOACTIVATE、WS_EX_TOOLWINDOW 与 MA_NOACTIVATE，收点击但绝不抢夺前台焦点；
/// 3. WM_NCHITTEST 像素级 Alpha 穿透，透明空白区域不阻挡桌面底层窗口操作；
/// 4. 底部锚定呼吸缩放与眨眼、点击表情切换；
/// 5. 拖拽与点击精准分类，支持立即拖动与长按后拖动，松手时持久化位置，支持混合 DPI 多显示器与断屏回退。
/// </para>
/// </summary>
public enum DragMode
{
    Immediate = 0,
    LongPress = 1,
}

internal sealed class PetWindow : Window
{
    private const int ClickSlopPhysical = PetPlacement.DefaultClickSlopPhysical;

    private readonly PetAssetManager _assetManager;
    private readonly PetAnimationStateMachine _stateMachine;
    private readonly double _sizeDiu;

    private readonly Image _imageControl;
    private readonly ScaleTransform _scaleTransform;
    private readonly RotateTransform _rotateTransform;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _longPressTimer;

    private DragMode _dragMode = DragMode.Immediate;
    private double _longPressDurationMs = Defaults.DefaultLongPressDurationMs;
    private bool _enableDragPose = Defaults.DefaultEnableDragPose;
    private bool _enablePointingPose = Defaults.DefaultEnablePointingPose;

    private POINT _grabOffset;
    private bool _dragging;
    private bool _pressed;
    private bool _longPressTriggered;
    private bool _movedBeyondSlop;
    private POINT _pressCursor;
    private POINT _dragStartLocation;
    private POINT? _location;
    private HwndSource? _hwndSource;
    private bool _isApplyingLocation;
    private DateTime _lastTickTime;

    public PetWindow(
        double sizeDiu,
        PetAssetManager assetManager,
        bool enableAnimation)
    {
        _sizeDiu = Math.Clamp(sizeDiu, Defaults.SizeMin, Defaults.SizeMax);
        _assetManager = assetManager;

        _stateMachine = new PetAnimationStateMachine
        {
            EnableAnimation = enableAnimation,
            EnableDragPose = _enableDragPose,
            EnablePointingPose = _enablePointingPose,
        };

        // 基础窗体属性：透明、无任务栏、无焦点掠夺
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = null; // 必须为 null，避免 WPF 默认拦截透明区域鼠标消息
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = _sizeDiu;
        Height = _sizeDiu;

        // 构建视觉树
        _scaleTransform = new ScaleTransform(1.0, 1.0);
        _rotateTransform = new RotateTransform(0.0);
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scaleTransform);
        transformGroup.Children.Add(_rotateTransform);

        _imageControl = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            RenderTransformOrigin = new Point(0.5, 1.0), // 底部中心锚定
            RenderTransform = transformGroup,
            Source = _assetManager.IdleBitmap,
        };

        Content = _imageControl;

        // 动画定时器（~33 FPS，每 30ms 推进一次，极低 CPU 开销）
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _animationTimer.Tick += OnAnimationTick;
        _lastTickTime = DateTime.UtcNow;

        if (enableAnimation)
        {
            _animationTimer.Start();
        }

        // 长按判定定时器
        _longPressTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(_longPressDurationMs),
        };
        _longPressTimer.Tick += OnLongPressTimerTick;

        // 交互与生命周期事件订阅
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnLeftButtonUp;
        MouseRightButtonUp += OnRightButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    /// <summary>用户轻点桌宠。参数为桌宠中心在虚拟屏幕物理像素中的坐标。</summary>
    public event Action<double, double>? WheelRequested;

    /// <summary>拖拽结束或显示器布局变更引起桌宠落位变更（触发位置写盘）。</summary>
    public event Action? Moved;

    /// <summary>桌宠拖拽手势开始（用于主动取消现有轮盘会话与指向态）。</summary>
    public event Action? DragStarted;

    /// <summary>用户右键点击桌宠（请求打开形象管理面板）。</summary>
    public event Action? ManageRequested;

    public double SizeDiu => _sizeDiu;

    public DragMode DragMode
    {
        get => _dragMode;
        set
        {
            _dragMode = value;
            if (value != DragMode.LongPress && _longPressTimer.IsEnabled)
            {
                _longPressTimer.Stop();
            }
        }
    }

    public double LongPressDurationMs
    {
        get => _longPressDurationMs;
        set
        {
            double safeValue = (!double.IsFinite(value))
                ? Defaults.DefaultLongPressDurationMs
                : Math.Clamp(value, Defaults.MinLongPressDurationMs, Defaults.MaxLongPressDurationMs);
            _longPressDurationMs = safeValue;
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(_longPressDurationMs);
        }
    }

    public bool EnableDragPose
    {
        get => _enableDragPose;
        set
        {
            _enableDragPose = value;
            _stateMachine.EnableDragPose = value;
            if (_dragging)
            {
                if (EnableAnimation && value)
                {
                    _imageControl.RenderTransformOrigin = new Point(0.51, 0.14);
                    _rotateTransform.Angle = _stateMachine.DragSwayAngle;
                    _imageControl.Source = _assetManager.DragBitmap;
                }
                else
                {
                    _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
                    _rotateTransform.Angle = 0.0;
                    _imageControl.Source = _assetManager.IdleBitmap;
                }
            }
        }
    }

    public bool EnablePointingPose
    {
        get => _enablePointingPose;
        set
        {
            _enablePointingPose = value;
            _stateMachine.EnablePointingPose = value;
            if (_stateMachine.IsPointing && !_dragging)
            {
                if (EnableAnimation && value)
                {
                    _imageControl.Source = _assetManager.GetBitmapForFrame(_stateMachine.CurrentFrame);
                }
                else
                {
                    _imageControl.Source = _assetManager.IdleBitmap;
                }
            }
        }
    }

    internal bool IsPressed => _pressed;
    internal bool IsDragging => _dragging;
    internal bool IsLongPressTriggered => _longPressTriggered;
    internal bool MovedBeyondSlop => _movedBeyondSlop;
    internal POINT? CurrentLocation => _location;

    internal Image ImageControl => _imageControl;
    internal DispatcherTimer AnimationTimer => _animationTimer;
    internal PetAnimationStateMachine StateMachine => _stateMachine;
    public double AnimationSpeed
    {
        get => _stateMachine.SpeedMultiplier;
        set => _stateMachine.SpeedMultiplier = value;
    }
    internal RotateTransform RotateTransform => _rotateTransform;
    internal ScaleTransform ScaleTransform => _scaleTransform;

    public bool EnableAnimation
    {
        get => _stateMachine.EnableAnimation;
        set
        {
            if (_stateMachine.EnableAnimation == value) return;
            _stateMachine.EnableAnimation = value;
            if (value)
            {
                _lastTickTime = DateTime.UtcNow;
                if (!_animationTimer.IsEnabled) _animationTimer.Start();
            }
            else
            {
                if (_animationTimer.IsEnabled) _animationTimer.Stop();
                _scaleTransform.ScaleY = 1.0;
                _rotateTransform.Angle = 0.0;
                _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
                _imageControl.Source = _assetManager.IdleBitmap;
            }
        }
    }

    /// <summary>
    /// 素材重新加载后刷新图片源、缩放与动画状态。
    /// </summary>
    internal void RefreshAssets()
    {
        if (EnableAnimation)
        {
            PetFrameKind frame = _stateMachine.CurrentFrame;
            if (frame == PetFrameKind.Drag && !_enableDragPose)
            {
                frame = PetFrameKind.Idle;
            }
            var target = _assetManager.GetBitmapForFrame(frame);
            _imageControl.Source = target;
            if (_dragging && _enableDragPose)
            {
                _imageControl.RenderTransformOrigin = new Point(0.51, 0.14);
                _rotateTransform.Angle = _stateMachine.DragSwayAngle;
            }
            else
            {
                _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
                _rotateTransform.Angle = 0.0;
            }
            _lastTickTime = DateTime.UtcNow;
            if (!_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
            }
        }
        else
        {
            if (_animationTimer.IsEnabled)
            {
                _animationTimer.Stop();
            }
            _scaleTransform.ScaleY = 1.0;
            _rotateTransform.Angle = 0.0;
            _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
            _imageControl.Source = _assetManager.IdleBitmap;
        }
    }

    /// <summary>
    /// 请求指定物理像素位置（虚拟屏幕坐标系）。
    /// </summary>
    public void RequestPhysicalLocation(int left, int top)
    {
        _location = new POINT(left, top);
        if (new WindowInteropHelper(this).Handle != nint.Zero)
        {
            ApplyLocation(notifyOnRelocation: true);
        }
    }

    /// <summary>桌宠在虚拟屏幕坐标系中的物理矩形。未就绪时为默认 0×0。</summary>
    public PhysicalRect ReadPhysicalRect()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !GetWindowRect(hwnd, out RECT rect)) return default;

        return new PhysicalRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>桌宠中心的物理像素坐标。</summary>
    public POINT CenterPhysical
    {
        get
        {
            PhysicalRect rect = ReadPhysicalRect();
            if (rect.Width > 0 && rect.Height > 0)
            {
                return new POINT(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            }
            if (_location.HasValue)
            {
                int r = (int)Math.Round(_sizeDiu / 2);
                return new POINT(_location.Value.X + r, _location.Value.Y + r);
            }
            return default;
        }
    }

    // ------------------------------------------------------------------ 动画与视觉推进

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan elapsed = now - _lastTickTime;
        _lastTickTime = now;

        if (elapsed <= TimeSpan.Zero) return;

        _stateMachine.Tick(elapsed);

        // 若处于拖拽中且启用了拖拽姿势，保持 DragBitmap 并按 (0.51, 0.14) 施加轻微摆动
        if (_dragging && _enableDragPose)
        {
            _scaleTransform.ScaleY = 1.0;
            _imageControl.RenderTransformOrigin = new Point(0.51, 0.14);
            _rotateTransform.Angle = _stateMachine.DragSwayAngle;
            if (!ReferenceEquals(_imageControl.Source, _assetManager.DragBitmap))
            {
                _imageControl.Source = _assetManager.DragBitmap;
            }
            return;
        }

        // 若处于拖拽中但未启用拖拽姿势，严格保持 IdleBitmap 与零旋转
        if (_dragging && !_enableDragPose)
        {
            _scaleTransform.ScaleY = 1.0;
            _rotateTransform.Angle = 0.0;
            _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
            if (!ReferenceEquals(_imageControl.Source, _assetManager.IdleBitmap))
            {
                _imageControl.Source = _assetManager.IdleBitmap;
            }
            return;
        }

        // 非拖拽状态：重置旋转与锚点
        _rotateTransform.Angle = 0.0;
        _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);

        // 若处于轮盘指向模式中
        if (_stateMachine.IsPointing)
        {
            _scaleTransform.ScaleY = 1.0;
            var targetPointing = _enablePointingPose
                ? _assetManager.GetBitmapForFrame(_stateMachine.CurrentFrame)
                : _assetManager.IdleBitmap;
            if (!ReferenceEquals(_imageControl.Source, targetPointing))
            {
                _imageControl.Source = targetPointing;
            }
            return;
        }

        // 呼吸缩放
        _scaleTransform.ScaleY = _stateMachine.BreathingScaleY;

        // 帧切换（idle / blink / react）
        PetFrameKind frame = _stateMachine.CurrentFrame;
        if (frame == PetFrameKind.Drag && !_enableDragPose)
        {
            frame = PetFrameKind.Idle;
        }
        var targetBitmap = _assetManager.GetBitmapForFrame(frame);
        if (!ReferenceEquals(_imageControl.Source, targetBitmap))
        {
            _imageControl.Source = targetBitmap;
        }
    }

    // ------------------------------------------------------------------ 鼠标交互与手势分类

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out POINT cursor))
        {
            HandleLostCapture();
            return;
        }

        PhysicalRect rect = ReadPhysicalRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            HandleLostCapture();
            return;
        }

        CaptureMouse();
        HandleMouseDown(cursor.X, cursor.Y, rect);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        if (!GetCursorPos(out POINT cursor)) return;

        HandleMouseMove(cursor.X, cursor.Y);
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        POINT upCursor = _pressCursor;
        GetCursorPos(out upCursor);

        HandleMouseUp(upCursor.X, upCursor.Y);
    }

    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging || _pressed) return;
        ManageRequested?.Invoke();
    }

    internal void TriggerManageRequested()
    {
        ManageRequested?.Invoke();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        HandleLostCapture();
    }

    private void OnLongPressTimerTick(object? sender, EventArgs e)
    {
        TriggerLongPressTimeout();
    }

    internal void HandleMouseDown(int cursorX, int cursorY, PhysicalRect windowRect)
    {
        _pressed = true;
        _dragging = false;
        _longPressTriggered = false;
        _movedBeyondSlop = false;
        _pressCursor = new POINT(cursorX, cursorY);
        _grabOffset = new POINT(cursorX - windowRect.X, cursorY - windowRect.Y);
        _dragStartLocation = _location ?? new POINT(windowRect.X, windowRect.Y);

        if (_dragMode == DragMode.LongPress)
        {
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(_longPressDurationMs);
            _longPressTimer.Start();
        }
    }

    internal void HandleMouseMove(int cursorX, int cursorY)
    {
        if (!_pressed) return;

        bool exceedsSlop = PetPlacement.ClassifyGesture(
            _pressCursor.X, _pressCursor.Y,
            cursorX, cursorY,
            false, ClickSlopPhysical) == PetPlacement.GestureKind.Drag;

        if (_dragMode == DragMode.Immediate)
        {
            if (!_dragging && exceedsSlop)
            {
                _dragging = true;
                _stateMachine.SetDragging(true);
                DragStarted?.Invoke();
                if (EnableAnimation && _enableDragPose)
                {
                    _imageControl.RenderTransformOrigin = new Point(0.51, 0.14);
                    _rotateTransform.Angle = _stateMachine.DragSwayAngle;
                    _imageControl.Source = _assetManager.DragBitmap;
                }
            }

            if (_dragging)
            {
                UpdateDragPosition(cursorX - _grabOffset.X, cursorY - _grabOffset.Y);
            }
        }
        else // DragMode.LongPress
        {
            if (!_longPressTriggered)
            {
                if (exceedsSlop)
                {
                    // 在长按阈值前越界移动，取消长按判定，后续松手不能误判轻点
                    _movedBeyondSlop = true;
                    if (_longPressTimer.IsEnabled)
                    {
                        _longPressTimer.Stop();
                    }
                }
            }
            else
            {
                // 已达到长按阈值，处于拖拽状态
                _dragging = true;
                UpdateDragPosition(cursorX - _grabOffset.X, cursorY - _grabOffset.Y);
            }
        }
    }

    internal void TriggerLongPressTimeout()
    {
        if (_longPressTimer.IsEnabled)
        {
            _longPressTimer.Stop();
        }

        if (!_pressed || _movedBeyondSlop) return;

        _longPressTriggered = true;
        _dragging = true;
        _stateMachine.SetDragging(true);
        DragStarted?.Invoke();

        if (EnableAnimation && _enableDragPose)
        {
            _imageControl.RenderTransformOrigin = new Point(0.51, 0.14);
            _rotateTransform.Angle = _stateMachine.DragSwayAngle;
            _imageControl.Source = _assetManager.DragBitmap;
        }
    }

    internal void HandleMouseUp(int cursorX, int cursorY)
    {
        if (!_pressed) return;

        _pressed = false;

        if (_longPressTimer.IsEnabled)
        {
            _longPressTimer.Stop();
        }

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        bool wasDragging = _dragging;
        bool wasLongPressTriggered = _longPressTriggered;
        bool movedBeyondSlop = _movedBeyondSlop;

        var gesture = PetPlacement.ClassifyGesture(
            _pressCursor.X, _pressCursor.Y,
            cursorX, cursorY,
            _dragging, ClickSlopPhysical);

        if (gesture == PetPlacement.GestureKind.Drag)
        {
            movedBeyondSlop = true;
            if (_dragMode == DragMode.Immediate && !wasDragging)
            {
                wasDragging = true;
                DragStarted?.Invoke();
                UpdateDragPosition(cursorX - _grabOffset.X, cursorY - _grabOffset.Y);
            }
        }

        _dragging = false;
        _longPressTriggered = false;
        _movedBeyondSlop = false;
        _stateMachine.SetDragging(false);

        _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
        _rotateTransform.Angle = 0.0;

        // 恢复图像
        if (EnableAnimation)
        {
            PetFrameKind frame = _stateMachine.CurrentFrame;
            if (frame == PetFrameKind.Drag && !_enableDragPose)
            {
                frame = PetFrameKind.Idle;
            }
            _imageControl.Source = _assetManager.GetBitmapForFrame(frame);
        }
        else
        {
            _imageControl.Source = _assetManager.IdleBitmap;
        }

        // 判定位置是否发生变化
        bool positionChanged = false;
        if (_location.HasValue)
        {
            positionChanged = (_location.Value.X != _dragStartLocation.X || _location.Value.Y != _dragStartLocation.Y);
        }

        if (positionChanged)
        {
            Moved?.Invoke();
            return;
        }

        if (wasDragging || wasLongPressTriggered || movedBeyondSlop)
        {
            // 长按消耗（即便未位移）、已拖拽或松手前超出位移阈值，均不得触发轮盘
            return;
        }

        // 纯轻点动作
        PerformClickAction();
    }

    internal void HandleLostCapture()
    {
        if (!_pressed) return;

        _pressed = false;

        if (_longPressTimer.IsEnabled)
        {
            _longPressTimer.Stop();
        }

        bool wasDragging = _dragging;
        _dragging = false;
        _longPressTriggered = false;
        _movedBeyondSlop = false;
        _stateMachine.SetDragging(false);

        _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
        _rotateTransform.Angle = 0.0;

        if (EnableAnimation)
        {
            PetFrameKind frame = _stateMachine.CurrentFrame;
            if (frame == PetFrameKind.Drag && !_enableDragPose)
            {
                frame = PetFrameKind.Idle;
            }
            _imageControl.Source = _assetManager.GetBitmapForFrame(frame);
        }
        else
        {
            _imageControl.Source = _assetManager.IdleBitmap;
        }

        if (wasDragging && _location.HasValue &&
            (_location.Value.X != _dragStartLocation.X || _location.Value.Y != _dragStartLocation.Y))
        {
            Moved?.Invoke();
        }
    }

    /// <summary>
    /// 轮盘已呈现，进入指向交互态。
    /// </summary>
    internal void OnWheelSessionPresented()
    {
    }

    /// <summary>
    /// 轮盘选中项变化（主扇区、子扇区、死区或越界）。
    /// </summary>
    internal void OnWheelSelectionChanged(WheelSelectionChangedEventArgs args)
    {
        if (_dragging) return;

        if (args.IsEmptySelection || double.IsNaN(args.AngleDegrees))
        {
            _stateMachine.ClearPointing();
        }
        else
        {
            PetFrameKind frame = PetAnimationStateMachine.MapAngleToPointingFrame(args.AngleDegrees);
            _stateMachine.SetPointing(frame);
        }

        if (EnableAnimation)
        {
            PetFrameKind frame = _stateMachine.CurrentFrame;
            var targetBitmap = (_enablePointingPose && _stateMachine.IsPointing)
                ? _assetManager.GetBitmapForFrame(frame)
                : _assetManager.IdleBitmap;
            _imageControl.Source = targetBitmap;
            _scaleTransform.ScaleY = 1.0;
            _rotateTransform.Angle = 0.0;
            _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
        }
        else
        {
            _imageControl.Source = _assetManager.IdleBitmap;
        }
    }

    /// <summary>
    /// 轮盘会话结束（关闭、拒绝、覆盖或被拖动打断），清除指向并恢复平静。
    /// </summary>
    internal void OnWheelSessionEnded()
    {
        _stateMachine.ClearPointing();
        if (_dragging) return;

        _imageControl.RenderTransformOrigin = new Point(0.5, 1.0);
        _rotateTransform.Angle = 0.0;

        if (EnableAnimation)
        {
            PetFrameKind frame = _stateMachine.CurrentFrame;
            _imageControl.Source = _assetManager.GetBitmapForFrame(frame);
        }
        else
        {
            _imageControl.Source = _assetManager.IdleBitmap;
        }
    }

    /// <summary>
    /// 执行轻点动作逻辑。供鼠标松开事件与确定性无界面测试统一调用。
    /// </summary>
    internal void PerformClickAction()
    {
        if (EnableAnimation)
        {
            _stateMachine.TriggerReact();
            _imageControl.Source = _assetManager.ReactBitmap;
        }
        else
        {
            // 动画关闭时始终保持 IdleBitmap，不设置 ReactBitmap，也不重启计时器
            _imageControl.Source = _assetManager.IdleBitmap;
        }

        POINT center = CenterPhysical;
        WheelRequested?.Invoke(center.X, center.Y);
    }

    // ------------------------------------------------------------------ 坐标校准与 Win32

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            nint ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | (nint)(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        ApplyLocation(notifyOnRelocation: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;

        _longPressTimer.Stop();
        _longPressTimer.Tick -= OnLongPressTimerTick;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        MouseRightButtonUp -= OnRightButtonUp;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;

        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        const int HTCLIENT = 1;

        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_DPICHANGED = 0x02E0;

        // 1. 点击时绝对不抢前台窗口激活
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (nint)MA_NOACTIVATE;
        }

        // 2. 鼠标命中测试：若未命中不透明身体部分，直接返回 HTTRANSPARENT 实现透明穿透
        if (msg == WM_NCHITTEST)
        {
            if (_assetManager.HasIdleImage && !_pressed)
            {
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                if (GetWindowRect(hwnd, out RECT rect))
                {
                    int relX = screenX - rect.Left;
                    int relY = screenY - rect.Top;
                    int winWidth = rect.Right - rect.Left;
                    int winHeight = rect.Bottom - rect.Top;

                    if (relX >= 0 && relX < winWidth && relY >= 0 && relY < winHeight)
                    {
                        if (_assetManager.HitTest(relX, relY, winWidth, winHeight, _scaleTransform.ScaleY))
                        {
                            handled = true;
                            return (nint)HTCLIENT;
                        }
                    }
                }

                // 落在透明空白区，穿透到底层窗口
                handled = true;
                return (nint)HTTRANSPARENT;
            }
        }

        // 3. 屏幕拓扑、DPI 或工作区变更时重定位
        if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED || (msg == WM_SETTINGCHANGE && (int)wParam == 0x002F /* SPI_SETWORKAREA */))
        {
            ApplyLocation(notifyOnRelocation: true);
        }

        return nint.Zero;
    }

    internal void UpdateDragPosition(int rawLeft, int rawTop)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero)
        {
            _location = new POINT(rawLeft, rawTop);
            return;
        }

        var monitors = GetSystemMonitors();
        POINT anchor = new(rawLeft, rawTop);
        double scale = ReadDpiScale(CenterOf(anchor));
        int width = (int)Math.Round(_sizeDiu * scale);
        int height = width;

        (int left, int top) = PetPlacement.ClampToMonitors(rawLeft, rawTop, width, height, monitors);
        _location = new POINT(left, top);

        SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
    }

    internal void ApplyLocation(bool notifyOnRelocation = false)
    {
        if (_isApplyingLocation) return;
        _isApplyingLocation = true;
        try
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == nint.Zero) return;

            var monitors = GetSystemMonitors();
            var primary = monitors.Find(m => m.IsPrimary);
            if (primary.Width <= 0 && monitors.Count > 0) primary = monitors[0];

            if (_location is not POINT anchor)
            {
                int guessedWidth = (int)Math.Round(_sizeDiu);
                (int guessLeft, int guessTop) = PetPlacement.CalculateInitialPlacement(
                    guessedWidth, guessedWidth, primary);
                double scale = ReadDpiScale(CenterOf(new POINT(guessLeft, guessTop), guessedWidth));
                int width = (int)Math.Round(_sizeDiu * scale);
                int height = width;

                (int left, int top) = PetPlacement.CalculateInitialPlacement(
                    width, height, primary);
                (left, top) = PetPlacement.ClampToMonitors(left, top, width, height, monitors);

                _location = new POINT(left, top);
                SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
            }
            else
            {
                double scale = ReadDpiScale(CenterOf(anchor));
                int width = (int)Math.Round(_sizeDiu * scale);
                int height = width;

                (int left, int top) = PetPlacement.ClampToMonitors(anchor.X, anchor.Y, width, height, monitors);
                bool moved = anchor.X != left || anchor.Y != top;
                _location = new POINT(left, top);
                SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);

                if (moved && notifyOnRelocation && !_pressed && !_dragging)
                {
                    Moved?.Invoke();
                }
            }
        }
        finally
        {
            _isApplyingLocation = false;
        }
    }

    private POINT CenterOf(POINT topLeft, int width = 0)
    {
        int radius = width > 0 ? width / 2 : (int)Math.Round(_sizeDiu / 2);
        return new POINT(topLeft.X + radius, topLeft.Y + radius);
    }

    private static double ReadDpiScale(POINT at)
    {
        try
        {
            nint monitor = MonitorFromPoint(at, MONITOR_DEFAULTTONEAREST);
            if (monitor != nint.Zero
                && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0
                && dpiX > 0 && dpiY > 0)
            {
                return Math.Max(dpiX, dpiY) / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1.0;
    }

    private static List<PetPlacement.MonitorWorkArea> GetSystemMonitors()
    {
        var monitors = new List<PetPlacement.MonitorWorkArea>();

        EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                monitors.Add(new PetPlacement.MonitorWorkArea(
                    mi.rcWork.Left,
                    mi.rcWork.Top,
                    mi.rcWork.Right,
                    mi.rcWork.Bottom,
                    isPrimary));
            }
            return true;
        }, nint.Zero);

        if (monitors.Count == 0)
        {
            int w = GetSystemMetrics(SM_CXSCREEN);
            int h = GetSystemMetrics(SM_CYSCREEN);
            if (w > 0 && h > 0)
            {
                monitors.Add(new PetPlacement.MonitorWorkArea(0, 0, w, h, true));
            }
        }

        return monitors;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;
    private static readonly nint HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public readonly record struct PhysicalRect(int X, int Y, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("SHCore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    private static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
}
