using System;

namespace StarPie.Plugin;

/// <summary>
/// 轮盘交互会话状态枚举。
/// </summary>
public enum WheelSessionState
{
    /// <summary>
    /// 宿主已受理呼出请求，正在排队或初始化。
    /// </summary>
    Requested,

    /// <summary>
    /// 轮盘窗口与遮罩已完成呈现（进入交互态，不宣称 DWM 物理扫描已完成）。
    /// </summary>
    Presented,

    /// <summary>
    /// 轮盘正常关闭（动作已触发、Esc 取消或点击外部遮罩）。
    /// </summary>
    Closed,

    /// <summary>
    /// 请求被拒绝（宿主忙、全屏独占或前置条件不满足）。
    /// </summary>
    Rejected,

    /// <summary>
    /// 本会话已被新的轮盘呼出请求替换或覆盖。
    /// </summary>
    Superseded
}

/// <summary>
/// 轮盘选中变化事件参数（纯 BCL 基础类型，不依赖任何特定 UI 框架）。
/// </summary>
public sealed class WheelSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// 会话唯一标识符。
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 轮盘物理中心 X 坐标（屏幕物理像素，支持负坐标与多显示器）。
    /// </summary>
    public required double PhysicalCenterX { get; init; }

    /// <summary>
    /// 轮盘物理中心 Y 坐标（屏幕物理像素，支持负坐标与多显示器）。
    /// </summary>
    public required double PhysicalCenterY { get; init; }

    /// <summary>
    /// 当前是否处于无选中状态（例如：光标在中心死区、在扇区外部环、或尚未悬停任何项）。
    /// </summary>
    public required bool IsEmptySelection { get; init; }

    /// <summary>
    /// 当前菜单层级（0: 主轮盘, 1: 二级子轮盘）。
    /// </summary>
    public int MenuDepth { get; init; }

    /// <summary>
    /// 选中的主扇区物理方向角（以正东方向为 0°，顺时针递增，正Y轴向下，范围 [0, 360)）。
    /// 当 <see cref="IsEmptySelection"/> 为 true 时，该值为 double.NaN。
    /// </summary>
    public double AngleDegrees { get; init; }

    /// <summary>
    /// 选中的主扇区索引（0 到 N-1）。当 <see cref="IsEmptySelection"/> 为 true 时为 -1。
    /// </summary>
    public int SectorIndex { get; init; }

    /// <summary>
    /// 选中的二级子扇区索引（0 到 M-1）。若未进入二级或无选中时为 -1。
    /// </summary>
    public int SubSectorIndex { get; init; } = -1;

    /// <summary>
    /// 主扇区总数（如 4, 8, 12 等），方便插件推算分区范围。
    /// </summary>
    public int TotalSectors { get; init; }
}

/// <summary>
/// 轮盘会话生命周期事件参数。
/// </summary>
public sealed class WheelSessionStateChangedEventArgs : EventArgs
{
    public required string SessionId { get; init; }
    public required WheelSessionState State { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// 轮盘会话追踪服务接口。
/// 扩展自基础 <see cref="IHostWheelService"/>，提供强类型的调用者会话监听能力。
/// </summary>
public interface IHostWheelSessionService : IHostWheelService
{
    /// <summary>
    /// 发起轮盘呼出并建立追踪会话。
    /// </summary>
    /// <param name="physicalCenterX">轮盘圆心的物理像素横坐标。</param>
    /// <param name="physicalCenterY">轮盘圆心的物理像素纵坐标。</param>
    /// <param name="onStateChanged">会话状态变更回调（Requested, Presented, Closed, Rejected 等）。</param>
    /// <param name="onSelectionChanged">选中项改变回调（包含角度与死区状态）。</param>
    /// <returns>若受理则返回会话订阅凭证；若立即拒绝则返回 null。</returns>
    /// <exception cref="PluginCapabilityDeniedException">清单未声明 <see cref="PluginCapability.Wheel"/>。</exception>
    IDisposable? RequestTrackedWheel(
        double physicalCenterX,
        double physicalCenterY,
        Action<WheelSessionStateChangedEventArgs> onStateChanged,
        Action<WheelSelectionChangedEventArgs> onSelectionChanged);
}
