using System;
using System.Collections.Generic;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 悬浮球几何与交互判定纯逻辑。
/// <para>
/// 独立于 WPF Window 与 HWND 实例，使多屏坐标、工作区钳制、断屏回落与手势分类逻辑
/// 具备确定性并能进行无界面的自动化回归测试。
/// </para>
/// </summary>
public static class BallPlacement
{
    public const int DefaultClickSlopPhysical = 4;
    public const int DefaultRightMarginPhysical = 40;

    /// <summary>
    /// 显示器工作区矩形（物理像素，虚拟桌面坐标系）。
    /// </summary>
    public readonly record struct MonitorWorkArea(
        int Left,
        int Top,
        int Right,
        int Bottom,
        bool IsPrimary = false)
    {
        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);

        public bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;

        /// <summary>
        /// 点到该工作区矩形边界的最短距离平方（点在矩形内则为 0）。
        /// </summary>
        public double DistanceSquaredTo(int x, int y)
        {
            int cx = Math.Clamp(x, Left, Right);
            int cy = Math.Clamp(y, Top, Bottom);
            long dx = x - cx;
            long dy = y - cy;
            return (double)dx * dx + (double)dy * dy;
        }
    }

    public enum GestureKind
    {
        None,
        Click,
        Drag
    }

    /// <summary>
    /// 根据起点、当前/释放点与已拖动标志对鼠标手势进行分类。
    /// 解决中途丢失 MouseMove 或释放时光标产生微小位移的竞态。
    /// </summary>
    public static GestureKind ClassifyGesture(
        int startX, int startY,
        int currentX, int currentY,
        bool wasAlreadyDragging,
        int clickSlop = DefaultClickSlopPhysical)
    {
        if (wasAlreadyDragging) return GestureKind.Drag;

        int dx = Math.Abs(currentX - startX);
        int dy = Math.Abs(currentY - startY);

        if (dx > clickSlop || dy > clickSlop)
        {
            return GestureKind.Drag;
        }

        return GestureKind.Click;
    }

    /// <summary>
    /// 在给定的一组显示器工作区中寻找目标显示器。
    /// 1. 若点落在某个显示器工作区内，直接返回该显示器；
    /// 2. 若点落在空白区域（如 L 形多屏空隙或已断开屏幕坐标），返回欧氏距离最近的显示器工作区；若距离相同，优先主屏。
    /// </summary>
    public static MonitorWorkArea FindTargetMonitor(
        int centerX,
        int centerY,
        IReadOnlyList<MonitorWorkArea> monitors)
    {
        if (monitors == null || monitors.Count == 0) return default;

        // 1. 直击包含
        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Contains(centerX, centerY))
            {
                return monitors[i];
            }
        }

        // 2. 最近工作区（非矩形布局间隙、外部回落）
        MonitorWorkArea best = monitors[0];
        double minDistance = best.DistanceSquaredTo(centerX, centerY);

        for (int i = 1; i < monitors.Count; i++)
        {
            var m = monitors[i];
            double dist = m.DistanceSquaredTo(centerX, centerY);
            if (dist < minDistance || (Math.Abs(dist - minDistance) < 0.001 && m.IsPrimary && !best.IsPrimary))
            {
                minDistance = dist;
                best = m;
            }
        }

        return best;
    }

    /// <summary>
    /// 将窗口限制在单个显示器的工作区内，确保球完整可见且不被任务栏遮挡。
    /// </summary>
    public static (int Left, int Top) ClampToMonitor(
        int left,
        int top,
        int width,
        int height,
        MonitorWorkArea workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return (left, top);

        int maxLeft = workArea.Left + Math.Max(0, workArea.Width - width);
        int maxTop = workArea.Top + Math.Max(0, workArea.Height - height);

        int clampedLeft = Math.Clamp(left, workArea.Left, maxLeft);
        int clampedTop = Math.Clamp(top, workArea.Top, maxTop);
        return (clampedLeft, clampedTop);
    }

    /// <summary>
    /// 根据球心所在位置找到目标显示器工作区，并将球完整限制在该工作区内。
    /// </summary>
    public static (int Left, int Top) ClampToMonitors(
        int left,
        int top,
        int width,
        int height,
        IReadOnlyList<MonitorWorkArea> monitors)
    {
        if (monitors == null || monitors.Count == 0) return (left, top);

        int centerX = left + width / 2;
        int centerY = top + height / 2;
        MonitorWorkArea target = FindTargetMonitor(centerX, centerY, monitors);
        return ClampToMonitor(left, top, width, height, target);
    }

    /// <summary>
    /// 计算首次落位坐标（在指定主显示器工作区中右侧留白、垂直偏上）。
    /// </summary>
    public static (int Left, int Top) CalculateInitialPlacement(
        int width,
        int height,
        MonitorWorkArea primaryWorkArea,
        int rightMargin = DefaultRightMarginPhysical)
    {
        if (primaryWorkArea.Width <= 0 || primaryWorkArea.Height <= 0) return (0, 0);

        int left = primaryWorkArea.Right - width - rightMargin;
        int top = primaryWorkArea.Top + (primaryWorkArea.Height - height) * 2 / 5;
        return ClampToMonitor(left, top, width, height, primaryWorkArea);
    }
}
