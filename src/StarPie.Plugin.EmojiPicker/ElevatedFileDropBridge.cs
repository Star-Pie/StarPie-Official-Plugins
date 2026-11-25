using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace StarPie.Plugin.EmojiPicker;

/// <summary>
/// Enables reliable file and folder drops onto Emoji Picker windows both when running
/// normally (WPF tunneling PreviewDrag/Drop) and when StarPie runs as Administrator
/// (where Windows UIPI silently drops medium-integrity OLE IDropTarget drags, requiring
/// WM_DROPFILES via DragAcceptFiles + ChangeWindowMessageFilterEx).
/// </summary>
internal static class ElevatedFileDropBridge
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    private const uint WmCopyGlobalData = 0x0049;
    private const uint MsgfltAllow = 1;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;

    private static readonly Lazy<bool> s_isProcessElevated = new(QueryCurrentProcessElevation);

    internal static bool IsProcessElevated => s_isProcessElevated.Value;

    internal static (List<string> Files, List<string> Folders) PartitionDroppedPaths(IEnumerable<string>? rawPaths)
    {
        var files = new List<string>();
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (rawPaths == null)
        {
            return (files, folders);
        }

        foreach (string? raw in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            string candidate = raw.Trim();
            if (!seen.Add(candidate))
            {
                continue;
            }
            if (File.Exists(candidate))
            {
                files.Add(candidate);
            }
            else if (Directory.Exists(candidate))
            {
                folders.Add(candidate);
            }
        }

        return (files, folders);
    }

    internal static IDisposable Attach(
        Window window,
        Action<string[]> onPathsDropped,
        Action<bool>? onDragHoverChanged = null,
        bool? isElevatedOverride = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onPathsDropped);

        bool elevated = isElevatedOverride ?? IsProcessElevated;
        return elevated
            ? new WmDropFilesAttachment(window, onPathsDropped, onDragHoverChanged)
            : new WpfPreviewDropAttachment(window, onPathsDropped, onDragHoverChanged);
    }

    private sealed class WmDropFilesAttachment : IDisposable
    {
        private readonly Window _window;
        private readonly Action<string[]> _onPathsDropped;
        private readonly Action<bool>? _onDragHoverChanged;
        private HwndSource? _hwndSource;
        private nint _hwnd;
        private bool _disposed;

        public WmDropFilesAttachment(Window window, Action<string[]> onPathsDropped, Action<bool>? onDragHoverChanged)
        {
            _window = window;
            _onPathsDropped = onPathsDropped;
            _onDragHoverChanged = onDragHoverChanged;

            // Keep WPF's OLE IDropTarget disabled so UIPI-exempt WM_DROPFILES reaches WndProc.
            _window.AllowDrop = false;
            _window.SourceInitialized += OnSourceInitialized;
            _window.Closed += OnWindowClosed;

            nint existingHwnd = new WindowInteropHelper(_window).Handle;
            if (existingHwnd != 0)
            {
                RegisterHwnd(existingHwnd);
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            nint hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != 0)
            {
                RegisterHwnd(hwnd);
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        private void RegisterHwnd(nint hwnd)
        {
            if (_disposed || hwnd == 0 || _hwnd == hwnd)
            {
                return;
            }

            _hwnd = hwnd;
            try { _ = RevokeDragDrop(hwnd); } catch { }
            try { ChangeWindowMessageFilterEx(hwnd, WmDropFiles, MsgfltAllow, 0); } catch { }
            try { ChangeWindowMessageFilterEx(hwnd, WmCopyData, MsgfltAllow, 0); } catch { }
            try { ChangeWindowMessageFilterEx(hwnd, WmCopyGlobalData, MsgfltAllow, 0); } catch { }
            try { DragAcceptFiles(hwnd, true); } catch { }

            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (_disposed || msg != (int)WmDropFiles || wParam == 0)
            {
                return 0;
            }

            string[] paths = ReadDropPaths(wParam);
            handled = true;
            _onDragHoverChanged?.Invoke(false);
            if (paths.Length > 0)
            {
                _onPathsDropped(paths);
            }
            return 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _window.SourceInitialized -= OnSourceInitialized;
            _window.Closed -= OnWindowClosed;
            if (_hwndSource != null)
            {
                try { _hwndSource.RemoveHook(WndProc); } catch { }
                _hwndSource = null;
            }
            if (_hwnd != 0)
            {
                try { DragAcceptFiles(_hwnd, false); } catch { }
                _hwnd = 0;
            }
        }
    }

    private sealed class WpfPreviewDropAttachment : IDisposable
    {
        private readonly Window _window;
        private readonly Action<string[]> _onPathsDropped;
        private readonly Action<bool>? _onDragHoverChanged;
        private bool _disposed;

        public WpfPreviewDropAttachment(Window window, Action<string[]> onPathsDropped, Action<bool>? onDragHoverChanged)
        {
            _window = window;
            _onPathsDropped = onPathsDropped;
            _onDragHoverChanged = onDragHoverChanged;

            _window.AllowDrop = true;
            _window.PreviewDragEnter += OnPreviewDragEnterOrOver;
            _window.PreviewDragOver += OnPreviewDragEnterOrOver;
            _window.PreviewDragLeave += OnPreviewDragLeave;
            _window.PreviewDrop += OnPreviewDrop;
            _window.Closed += OnWindowClosed;
        }

        private void OnPreviewDragEnterOrOver(object sender, DragEventArgs e)
        {
            if (_disposed) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                _onDragHoverChanged?.Invoke(true);
            }
            else
            {
                e.Effects = DragDropEffects.None;
                _onDragHoverChanged?.Invoke(false);
            }
            e.Handled = true;
        }

        private void OnPreviewDragLeave(object sender, DragEventArgs e)
        {
            if (_disposed) return;
            Point pos = e.GetPosition(_window);
            if (pos.X <= 1 || pos.Y <= 1 || pos.X >= _window.ActualWidth - 1 || pos.Y >= _window.ActualHeight - 1)
            {
                _onDragHoverChanged?.Invoke(false);
            }
        }

        private void OnPreviewDrop(object sender, DragEventArgs e)
        {
            if (_disposed) return;
            _onDragHoverChanged?.Invoke(false);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            e.Handled = true;
            _onPathsDropped(paths);
        }

        private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.PreviewDragEnter -= OnPreviewDragEnterOrOver;
            _window.PreviewDragOver -= OnPreviewDragEnterOrOver;
            _window.PreviewDragLeave -= OnPreviewDragLeave;
            _window.PreviewDrop -= OnPreviewDrop;
            _window.Closed -= OnWindowClosed;
        }
    }

    private static string[] ReadDropPaths(nint hDrop)
    {
        var paths = new List<string>();
        try
        {
            uint count = DragQueryFile(hDrop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint len = DragQueryFile(hDrop, i, null, 0);
                if (len == 0) continue;
                var sb = new StringBuilder((int)len + 2);
                if (DragQueryFile(hDrop, i, sb, sb.Capacity) > 0)
                {
                    paths.Add(sb.ToString());
                }
            }
        }
        finally
        {
            try { DragFinish(hDrop); } catch { }
        }
        return paths.ToArray();
    }

    private static bool QueryCurrentProcessElevation()
    {
        nint token = 0;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token) || token == 0)
            {
                return false;
            }
            return GetTokenInformation(token, TokenElevationClass, out uint isElevated, sizeof(uint), out _) &&
                   isElevated != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != 0)
            {
                try { CloseHandle(token); } catch { }
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint hDrop, uint iFile, StringBuilder? lpszFile, int cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(nint hDrop);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(nint hWnd, uint message, uint action, nint pChangeFilterStruct);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        out uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
