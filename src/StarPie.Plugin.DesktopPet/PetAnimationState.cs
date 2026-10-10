using System;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 桌宠动画帧类型。
/// </summary>
public enum PetFrameKind
{
    Idle = 0,
    Blink = 1,
    React = 2,
    Drag = 3,
    PointE = 4,
    PointSE = 5,
    PointS = 6,
    PointSW = 7,
    PointW = 8,
    PointNW = 9,
    PointN = 10,
    PointNE = 11,
}

/// <summary>
/// 桌宠动画状态机纯逻辑。
/// <para>
/// 负责驱动底部锚定呼吸、定期眨眼与点击反应、拖拽表情切换与平滑悬空小幅摆动、8 方向轮盘指向姿势。
/// 独立于具体的 UI 定时器，方便在无界面测试中确定性验证状态流转。
/// </para>
/// </summary>
public sealed class PetAnimationStateMachine
{
    public const double DragSwayAmplitudeDegrees = 5.0; // 摆动幅度 ±5°
    public const double DragSwayPeriodSeconds = 1.0;    // 摆动周期 1.0 秒

    private bool _enableAnimation = true;
    private bool _enableDragPose = true;
    private bool _enablePointingPose = true;
    private bool _isDragging = false;
    private bool _isPointing = false;
    private PetFrameKind _pointingFrame = PetFrameKind.Idle;
    private PetFrameKind _currentFrame = PetFrameKind.Idle;
    private TimeSpan _frameTimeRemaining = TimeSpan.Zero;
    private TimeSpan _timeUntilNextBlink = TimeSpan.FromSeconds(4.0);
    private double _breathingPhase = 0.0;
    private double _dragSwayPhase = 0.0;
    private double _speed = 1.0;
    public double SpeedMultiplier
    {
        get => _speed;
        set => _speed = double.IsFinite(value) ? Math.Clamp(value, 0.25, 3.0) : 1.0;
    }

    public bool EnableAnimation
    {
        get => _enableAnimation;
        set
        {
            _enableAnimation = value;
            if (!value)
            {
                _currentFrame = PetFrameKind.Idle;
                _pointingFrame = PetFrameKind.Idle;
                _frameTimeRemaining = TimeSpan.Zero;
                _breathingPhase = 0.0;
                _dragSwayPhase = 0.0;
            }
        }
    }

    public bool EnableDragPose
    {
        get => _enableDragPose;
        set
        {
            _enableDragPose = value;
            if (!value)
            {
                _dragSwayPhase = 0.0;
                if (_currentFrame == PetFrameKind.Drag)
                {
                    _currentFrame = PetFrameKind.Idle;
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
            if (!value && _isPointing)
            {
                _pointingFrame = PetFrameKind.Idle;
            }
        }
    }

    public bool IsDragging => _isDragging;
    public bool IsPointing => _isPointing;

    public TimeSpan BlinkDuration { get; set; } = TimeSpan.FromMilliseconds(180);
    public TimeSpan ReactDuration { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan BlinkInterval { get; set; } = TimeSpan.FromSeconds(4.0);

    public PetFrameKind CurrentFrame
    {
        get
        {
            if (!_enableAnimation) return PetFrameKind.Idle;
            if (_isDragging)
            {
                return _enableDragPose ? PetFrameKind.Drag : PetFrameKind.Idle;
            }
            if (_isPointing)
            {
                return _enablePointingPose ? _pointingFrame : PetFrameKind.Idle;
            }
            if (_currentFrame == PetFrameKind.Drag && !_enableDragPose) return PetFrameKind.Idle;
            return _currentFrame;
        }
    }

    /// <summary>
    /// 底部锚定呼吸缩放比例（1.0 ~ 1.025）。
    /// 当禁用动画或处于拖拽/指向中时恒为 1.0。
    /// </summary>
    public double BreathingScaleY
    {
        get
        {
            if (!_enableAnimation || _isDragging || _isPointing) return 1.0;
            // 2 秒一个完整起伏周期，正弦平滑插值
            double sin = 0.5 - 0.5 * Math.Cos(_breathingPhase * 2.0 * Math.PI);
            return 1.0 + 0.025 * sin;
        }
    }

    /// <summary>
    /// 拖拽时轻微悬空往返摆动旋转角度（±5°，周期 1.0s）。
    /// 当禁用动画、禁用拖拽姿势或未处于拖拽时恒为 0.0。
    /// </summary>
    public double DragSwayAngle
    {
        get
        {
            if (!_enableAnimation || !_enableDragPose || !_isDragging) return 0.0;
            return DragSwayAmplitudeDegrees * Math.Sin(_dragSwayPhase * 2.0 * Math.PI);
        }
    }

    /// <summary>
    /// 设置拖动状态。处于拖动中时优先展示 Drag 帧（若启用了动画与拖拽姿势），并启动平滑摆动。
    /// </summary>
    public void SetDragging(bool isDragging)
    {
        _isDragging = isDragging;
        _dragSwayPhase = 0.0;

        if (!_enableAnimation || !_enableDragPose)
        {
            _currentFrame = PetFrameKind.Idle;
            return;
        }

        if (isDragging)
        {
            _currentFrame = PetFrameKind.Drag;
            _frameTimeRemaining = TimeSpan.Zero;
        }
        else
        {
            if (_currentFrame == PetFrameKind.Drag)
            {
                _currentFrame = PetFrameKind.Idle;
            }
        }
    }

    /// <summary>
    /// 设置轮盘指向帧。
    /// </summary>
    public void SetPointing(PetFrameKind pointingFrame)
    {
        _isPointing = true;
        _pointingFrame = pointingFrame;
        _currentFrame = PetFrameKind.Idle;
        _frameTimeRemaining = TimeSpan.Zero;
    }

    /// <summary>
    /// 清除轮盘指向状态，恢复平静。
    /// </summary>
    public void ClearPointing()
    {
        _isPointing = false;
        _pointingFrame = PetFrameKind.Idle;
        _currentFrame = PetFrameKind.Idle;
        _frameTimeRemaining = TimeSpan.Zero;
    }

    /// <summary>
    /// 触发轻点反应表情。优先于眨眼；重复点击刷新持续时间。
    /// </summary>
    public void TriggerReact()
    {
        if (!_enableAnimation || _isDragging || _isPointing) return;

        _currentFrame = PetFrameKind.React;
        _frameTimeRemaining = ReactDuration;
    }

    /// <summary>
    /// 触发单次眨眼。若正在展示反应表情、正在拖动或正在指向，则不打断。
    /// </summary>
    public void TriggerBlink()
    {
        if (!_enableAnimation || _isDragging || _isPointing) return;
        if (_currentFrame == PetFrameKind.React) return;

        _currentFrame = PetFrameKind.Blink;
        _frameTimeRemaining = BlinkDuration;
    }

    /// <summary>
    /// 时间推进。
    /// </summary>
    public void Tick(TimeSpan delta)
    {
        if (!_enableAnimation)
        {
            _currentFrame = PetFrameKind.Idle;
            _pointingFrame = PetFrameKind.Idle;
            _frameTimeRemaining = TimeSpan.Zero;
            _dragSwayPhase = 0.0;
            return;
        }

        if (delta <= TimeSpan.Zero) return;
        // Animation time only: pointer direction and long-press input use their original clocks.
        delta = TimeSpan.FromSeconds(Math.Min(delta.TotalSeconds, 60) * _speed);

        // 推进呼吸相位（周期 2.0 秒）
        _breathingPhase = (_breathingPhase + delta.TotalSeconds / 2.0) % 1.0;

        // 拖拽中平滑推进摆动相位
        if (_isDragging)
        {
            _currentFrame = _enableDragPose ? PetFrameKind.Drag : PetFrameKind.Idle;
            if (_enableAnimation && _enableDragPose)
            {
                _dragSwayPhase = (_dragSwayPhase + delta.TotalSeconds / DragSwayPeriodSeconds) % 1.0;
            }
            else
            {
                _dragSwayPhase = 0.0;
            }
            return;
        }

        // 指向模式下静止显示对应方向，不处理眨眼
        if (_isPointing)
        {
            return;
        }

        // 处理当前限时表情状态
        if (_frameTimeRemaining > TimeSpan.Zero)
        {
            _frameTimeRemaining -= delta;
            if (_frameTimeRemaining <= TimeSpan.Zero)
            {
                _frameTimeRemaining = TimeSpan.Zero;
                _currentFrame = PetFrameKind.Idle;
            }
        }
        else
        {
            // 处于 Idle 状态时计算下一次眨眼倒计时
            _timeUntilNextBlink -= delta;
            if (_timeUntilNextBlink <= TimeSpan.Zero)
            {
                _timeUntilNextBlink = BlinkInterval;
                TriggerBlink();
            }
        }
    }

    /// <summary>
    /// 重置状态机至初始 Idle。
    /// </summary>
    public void Reset()
    {
        _isDragging = false;
        _isPointing = false;
        _pointingFrame = PetFrameKind.Idle;
        _currentFrame = PetFrameKind.Idle;
        _frameTimeRemaining = TimeSpan.Zero;
        _timeUntilNextBlink = BlinkInterval;
        _breathingPhase = 0.0;
        _dragSwayPhase = 0.0;
    }

    /// <summary>
    /// 物理方向角到 8 方向指向帧的最近邻映射算法。
    /// 正东为 0°，顺时针，正Y轴向下（标准屏幕坐标系）。
    /// </summary>
    public static PetFrameKind MapAngleToPointingFrame(double angleDegrees)
    {
        if (double.IsNaN(angleDegrees) || double.IsInfinity(angleDegrees))
        {
            return PetFrameKind.Idle;
        }

        double normalized = (angleDegrees % 360.0 + 360.0) % 360.0;
        // 8 扇区，每区间 45°，以 22.5° 偏移定中心：
        // [337.5, 360) U [0, 22.5) -> E
        // [22.5, 67.5) -> SE
        // [67.5, 112.5) -> S
        // [112.5, 157.5) -> SW
        // [157.5, 202.5) -> W
        // [202.5, 247.5) -> NW
        // [247.5, 292.5) -> N
        // [292.5, 337.5) -> NE
        int index = (int)Math.Floor((normalized + 22.5) / 45.0) % 8;
        return index switch
        {
            0 => PetFrameKind.PointE,
            1 => PetFrameKind.PointSE,
            2 => PetFrameKind.PointS,
            3 => PetFrameKind.PointSW,
            4 => PetFrameKind.PointW,
            5 => PetFrameKind.PointNW,
            6 => PetFrameKind.PointN,
            7 => PetFrameKind.PointNE,
            _ => PetFrameKind.Idle,
        };
    }
}
