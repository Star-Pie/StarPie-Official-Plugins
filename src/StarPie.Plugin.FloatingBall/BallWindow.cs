using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 悬浮球的球体窗口 —— 纯代码构建，不带 XAML（产物里也不该出现第二个依赖）。
/// <para>
/// <b>位置用物理像素、尺寸由「DIU 直径 × 所在显示器的 DPI」现算。</b>这不是混了两套单位，
/// 而是各自取最优：位置必须与「显示器怎么排布」这个物理事实一致 —— 宿主
/// <see cref="IHostWheelService.ShowWheel"/> 要的就是虚拟屏幕坐标系的物理像素，
/// 改用 WPF 的 <c>Left</c>/<c>Top</c>（DIU，且相对当前显示器）在混合 DPI 多屏下会得到
/// 「球在副屏上看着对，点它轮盘却跑到主屏」；而直径按 DIU 声明，换到 200% 屏上球应当
/// 跟着变大，而不是缩成一个点。落位时两个值一起交给 <c>SetWindowPos</c>，
/// 与宿主 <c>RadialWindow.PositionWindowOnTargetMonitor</c> 同一套路子。
/// </para>
/// <para>
/// <b>实例不可变</b>：直径 / 颜色 / 不透明度都在构造时定死，要改就换一个新的球。
/// 少掉三个 setter 就少掉三类「参数改了但视觉没跟上」的漂移，而插件侧没有任何机器护栏
/// 查得见这种漂移 —— 唯一守得住它的手段是让它没有地方发生。
/// </para>
/// </summary>
internal sealed class BallWindow : Window
{
    /// <summary>按下到抬起的位移不超过这个物理像素数就算「点击」，否则算拖动。</summary>
    private const int ClickSlopPhysical = BallPlacement.DefaultClickSlopPhysical;

    /// <summary>第一次落位时右侧留的空（物理像素）：贴住屏幕右缘会压住系统托盘图标。</summary>
    private const int DefaultRightMargin = BallPlacement.DefaultRightMarginPhysical;

    private static readonly Color DefaultFill = Color.FromRgb(88, 132, 222);
    private static readonly SolidColorBrush BallBorderBrush = CreateFrozenBrush(Color.FromArgb(90, 255, 255, 255));

    private readonly double _diameterDiu;

    private POINT _grabOffset;
    private bool _dragging;
    private bool _pressed;
    private POINT _pressCursor;

    /// <summary>落位点（物理像素，窗口左上角）。null 表示「还没定过，用默认落点」。</summary>
    private POINT? _location;

    private HwndSource? _hwndSource;

    public BallWindow(double diameterDiu, double opacity, string fillColor)
    {
        _diameterDiu = Math.Clamp(diameterDiu, 12, 220);

        // 与宿主轮盘同一条纪律：收点击但绝不抢前台。
        // 一个会抢焦点的球意味着「点完球再按 Ctrl+C，复制打进的是球的窗口」。
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = _diameterDiu;
        Height = _diameterDiu;
        Content = BuildShape(opacity, fillColor);

        // 事件订阅而不是覆写 OnXxx：宿主工程 UseWindowsForms 与 WPF 并存，
        // 覆写会在两个同名消息类型之间撞车（CS0115，实测踩过）。
        MouseLeftButtonDown += OnLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    /// <summary>用户点了一下球。参数是球心在虚拟屏幕坐标系里的物理像素坐标。</summary>
    public event Action<double, double>? WheelRequested;

    /// <summary>一次拖动结束或显示器变更导致落位改变（松手或重定位时触发一次，供调用方把位置落盘）。</summary>
    public event Action? Moved;

    /// <summary>
    /// 指定落位坐标（物理像素，虚拟屏幕坐标系）。可以在 <c>Show()</c> 之前调用。
    /// <para>
    /// 刻意不做成「Show 之后再定位」：那样窗口会先在系统默认位置画一帧，用户看到的是一次跳动。
    /// </para>
    /// </summary>
    public void RequestPhysicalLocation(int left, int top)
    {
        _location = new POINT(left, top);
        if (new WindowInteropHelper(this).Handle != nint.Zero) ApplyLocation(notifyOnRelocation: true);
    }

    /// <summary>球在虚拟屏幕坐标系里的物理矩形。拿不到句柄时是 0×0，调用方据此判断「还没落位」。</summary>
    public PhysicalRect ReadPhysicalRect()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero || !GetWindowRect(hwnd, out RECT rect)) return default;

        return new PhysicalRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>球心的物理像素坐标。</summary>
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
                int r = (int)Math.Round(_diameterDiu / 2);
                return new POINT(_location.Value.X + r, _location.Value.Y + r);
            }
            return default;
        }
    }

    // ------------------------------------------------------------------ 交互

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out POINT cursor))
        {
            _pressed = false;
            _dragging = false;
            return;
        }

        PhysicalRect rect = ReadPhysicalRect();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            // 窗口矩形不可读或尚未布局完成时，放弃本次抓取，防止因无效偏移跳动到 (0,0)
            _pressed = false;
            _dragging = false;
            return;
        }

        _pressed = true;
        _dragging = false;
        _pressCursor = cursor;
        _grabOffset = new POINT(cursor.X - rect.X, cursor.Y - rect.Y);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        if (!GetCursorPos(out POINT cursor)) return;

        var gesture = BallPlacement.ClassifyGesture(
            _pressCursor.X, _pressCursor.Y,
            cursor.X, cursor.Y,
            _dragging, ClickSlopPhysical);

        if (gesture != BallPlacement.GestureKind.Drag) return;
        _dragging = true;

        // 跟手用的是「光标物理坐标 - 按下时的抓取偏移」，全程不涉及 WPF 坐标：
        // 拖拽过程仅刷新视觉位置，绝不触发写盘通知。
        UpdateDragPosition(cursor.X - _grabOffset.X, cursor.Y - _grabOffset.Y);
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        bool isDrag = _dragging;
        if (GetCursorPos(out POINT upCursor))
        {
            var gesture = BallPlacement.ClassifyGesture(
                _pressCursor.X, _pressCursor.Y,
                upCursor.X, upCursor.Y,
                _dragging, ClickSlopPhysical);

            if (gesture == BallPlacement.GestureKind.Drag)
            {
                isDrag = true;
                UpdateDragPosition(upCursor.X - _grabOffset.X, upCursor.Y - _grabOffset.Y);
            }
        }

        _dragging = false;

        if (isDrag)
        {
            // Moved 只在松手时响一次（不在每次 MouseMove 上响）：落盘的是「松手时看到的位置」，
            // 而拖动中途的每一个坐标都只是路过 —— 挂在这上面等于把一次拖拽变成几十次写盘。
            Moved?.Invoke();
            return;
        }

        // 这里不判断轮盘是否真的呼出来了：ShowWheel 的 true 只代表宿主受理
        // （SDK 注释里写的就是这个语义）。球跟着改视觉状态，只会多出
        // 「球的样式和屏幕上实际有的东西不一致」这第二种现象。
        POINT center = CenterPhysical;
        WheelRequested?.Invoke(center.X, center.Y);
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;

        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;

        if (wasDragging)
        {
            Moved?.Invoke();
        }
    }

    // ------------------------------------------------------------------ 外观

    private static UIElement BuildShape(double opacity, string fillColor)
    {
        var grid = new Grid { Opacity = Math.Clamp(opacity, 0.08, 1.0) };

        var effect = new DropShadowEffect
        {
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = 0.35,
            Color = Colors.Black,
        };
        if (effect.CanFreeze) effect.Freeze();

        grid.Children.Add(new Ellipse
        {
            Fill = ParseFill(fillColor),

            // 球是半透明的，没有描边就看不出边界 —— 而「哪儿算球、哪儿算球后面那个窗口」
            // 直接决定用户的下一次点击落在谁身上。
            Stroke = BallBorderBrush,
            StrokeThickness = 1.2,
            Margin = new Thickness(2),
            Effect = effect,
        });

        return grid;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static SolidColorBrush ParseFill(string fillColor)
    {
        Color parsed;

        try
        {
            // 只认颜色串，不认「Sc#R0.5G0.5B0.5 #RRGGBBAA」之外的形态：ConvertFromString 的返回是 object，
            // 硬转会在用户填了个怪字符串时抛出异常 —— 那由下面的 catch 接住并退回默认色。
            parsed = (Color)ColorConverter.ConvertFromString(fillColor)!;
        }
        catch (Exception)
        {
            // 用户在颜色参数里手打了半个 `#FF0`。这不该让球干脆不出来。
            parsed = DefaultFill;
        }

        // 颜色串里的 alpha 被丢掉：透明度归 opacity 参数独管。
        // 两处各乘一遍的结果是「设成 80% 实际得到 32%」，而用户看到的只是「颜色没生效」。
        return CreateFrozenBrush(Color.FromRgb(parsed.R, parsed.G, parsed.B));
    }

    // ------------------------------------------------------------------ Win32

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            // AllowsTransparency 会让 WPF 自己加上 WS_EX_LAYERED，但 NOACTIVATE / TOOLWINDOW
            // 没人给 —— 缺前者会抢前台，缺后者会在 Alt+Tab 列表里多出一项「球」。
            nint ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, ex | (nint)(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        // 无条件定位：如果此前指定过落点且该点需要因屏幕变更纠正，在静止恢复路径下通知并持久化
        ApplyLocation(notifyOnRelocation: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        const int WM_DISPLAYCHANGE = 0x007E;
        const int WM_DPICHANGED = 0x02E0;

        // 显示器拓扑改变、分辨率改变、DPI 改变或工作区（任务栏挪动）改变时，重新校准位置与尺寸
        if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED || (msg == WM_SETTINGCHANGE && (int)wParam == 0x002F /* SPI_SETWORKAREA */))
        {
            ApplyLocation(notifyOnRelocation: true);
        }

        return nint.Zero;
    }

    private bool _isApplyingLocation;

    /// <summary>
    /// 用户拖拽过程中的位置更新。仅更新视觉位置和当前坐标，<b>绝不触发 Moved 事件写盘</b>。
    /// </summary>
    internal void UpdateDragPosition(int rawLeft, int rawTop)
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == nint.Zero) return;

        var monitors = GetSystemMonitors();
        POINT anchor = new(rawLeft, rawTop);
        double scale = ReadDpiScale(CenterOf(anchor));
        int width = (int)Math.Round(_diameterDiu * scale);
        int height = width;

        (int left, int top) = BallPlacement.ClampToMonitors(rawLeft, rawTop, width, height, monitors);
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
                int guessedWidth = (int)Math.Round(_diameterDiu);
                (int guessLeft, int guessTop) = BallPlacement.CalculateInitialPlacement(
                    guessedWidth, guessedWidth, primary, DefaultRightMargin);
                double scale = ReadDpiScale(CenterOf(new POINT(guessLeft, guessTop), guessedWidth));
                int width = (int)Math.Round(_diameterDiu * scale);
                int height = width;

                (int left, int top) = BallPlacement.CalculateInitialPlacement(
                    width, height, primary, DefaultRightMargin);
                (left, top) = BallPlacement.ClampToMonitors(left, top, width, height, monitors);

                _location = new POINT(left, top);
                SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
            }
            else
            {
                double scale = ReadDpiScale(CenterOf(anchor));
                int width = (int)Math.Round(_diameterDiu * scale);
                int height = width;

                (int left, int top) = BallPlacement.ClampToMonitors(anchor.X, anchor.Y, width, height, monitors);
                bool moved = anchor.X != left || anchor.Y != top;
                _location = new POINT(left, top);
                SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);

                // 仅在非用户拖拽状态（如恢复位置、断屏重排、分辨率/DPI 变化）导致静止球位置改变时才持久化
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
        int radius = width > 0 ? width / 2 : (int)Math.Round(_diameterDiu / 2);
        return new POINT(topLeft.X + radius, topLeft.Y + radius);
    }

    /// <summary>
    /// 指定点所在显示器的 DPI 缩放系数。
    /// <para>
    /// 按<b>球心</b>而不是窗口左上角取显示器：贴左边缘放置时左上角可能落到另一块屏上，
    /// 于是尺寸按副屏算、位置按主屏摆 —— 正是要避开的那类混合 DPI 偏差。
    /// </para>
    /// </summary>
    private static double ReadDpiScale(POINT at)
    {
        try
        {
            nint monitor = MonitorFromPoint(at, MONITOR_DEFAULTTONEAREST);
            if (monitor != nint.Zero
                && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0
                && dpiX > 0 && dpiY > 0)
            {
                // 两轴取较大者：本插件把球当正方形画，混着缩放会让它变成椭圆。
                return Math.Max(dpiX, dpiY) / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
            // SHCore 缺失（极老系统）时退回 100%：球只是尺寸不跟随缩放，比崩掉好。
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1.0;
    }

    private static List<BallPlacement.MonitorWorkArea> GetSystemMonitors()
    {
        var monitors = new List<BallPlacement.MonitorWorkArea>();

        EnumDisplayMonitors(nint.Zero, nint.Zero, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                monitors.Add(new BallPlacement.MonitorWorkArea(
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
                monitors.Add(new BallPlacement.MonitorWorkArea(0, 0, w, h, true));
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

    /// <summary>物理像素矩形（虚拟屏幕坐标系）。默认值 0×0 表示「还没拿到句柄 / 未落位」。</summary>
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

    // GetWindowLongPtr / SetWindowLongPtr 只是 64 位上的名字，32 位要退回不带 Ptr 的那套。
    // 宿主只出 x64，但成对入口是宿主里的现成写法，照抄比自创省事。
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
