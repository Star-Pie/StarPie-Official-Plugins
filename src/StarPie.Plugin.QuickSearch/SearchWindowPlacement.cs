using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StarPie.Plugin.QuickSearch;

internal static class SearchWindowPlacement
{
    internal static void AtPointer(Window window, int x, int y, double desiredWidth, double desiredHeight)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        nint monitor = MonitorFromPoint(new NativePoint(x, y), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        // Move the HWND onto the target display first so the DPI read is for that display.
        SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi == 0 ? 1d : dpi / 96d;
        int areaWidth = info.Work.Right - info.Work.Left;
        int areaHeight = info.Work.Bottom - info.Work.Top;
        int inset = Math.Min((int)Math.Round(12 * scale), Math.Min(areaWidth, areaHeight) / 8);
        window.MinWidth = window.MinHeight = 0;
        window.MaxWidth = Math.Max(1, areaWidth - 2 * inset) / scale;
        window.MaxHeight = Math.Max(1, areaHeight - 2 * inset) / scale;
        window.MinWidth = Math.Min(560, window.MaxWidth);
        window.MinHeight = Math.Min(380, window.MaxHeight);
        window.Width = Math.Clamp(desiredWidth, window.MinWidth, window.MaxWidth);
        window.Height = Math.Clamp(desiredHeight, window.MinHeight, window.MaxHeight);
        int width = Math.Min(areaWidth - 2 * inset, (int)Math.Round(window.Width * scale));
        int height = Math.Min(areaHeight - 2 * inset, (int)Math.Round(window.Height * scale));
        int left = Math.Clamp(x - width / 2, info.Work.Left + inset, info.Work.Right - inset - width);
        int top = Math.Clamp(y - (int)Math.Round(45 * scale), info.Work.Top + inset, info.Work.Bottom - inset - height);
        SetWindowPos(hwnd, 0, left, top, width, height, 0x0004 | 0x0010);
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
