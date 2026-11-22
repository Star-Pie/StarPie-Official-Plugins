using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.EmojiPicker;

internal sealed record ClipboardImageReadResult(byte[]? Bytes, string Format, string SuggestedName, bool TooLarge);

internal interface IEmojiClipboardBridge
{
    uint GetSequenceNumber();
    string? GetText();
    bool SetText(string text);
    bool SetStickerData(string filePath, bool includeBitmapFallback);
    ClipboardImageReadResult ReadImage();
}

internal interface IFocusRestorer
{
    nint GetForegroundWindow();
    bool IsExternalTargetWindow(nint hwnd);
    Task<bool> TryRestoreForegroundAsync(nint targetHwnd, int timeoutMs = 120, CancellationToken cancellationToken = default);
}

internal enum DispatchOutcome
{
    Sent = 0,
    Busy = 1,
    Closed = 2,
    TargetUnavailable = 3,
    Failed = 4
}

internal sealed class WpfClipboardBridge : IEmojiClipboardBridge
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public uint GetSequenceNumber() => GetClipboardSequenceNumber();

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; }
    }

    public bool SetText(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch { return false; }
    }

    public bool SetStickerData(string filePath, bool includeBitmapFallback)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var data = new DataObject();
            var drops = new StringCollection { Path.GetFullPath(filePath) };
            data.SetFileDropList(drops);

            if (includeBitmapFallback)
            {
                BitmapSource? thumb = EmojiImageCache.DecodeThumbnail(filePath, 512);
                if (thumb != null)
                    data.SetImage(thumb);
            }

            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public ClipboardImageReadResult ReadImage()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                StringCollection files = Clipboard.GetFileDropList();
                foreach (string? path in files)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    var fi = new FileInfo(path);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (fi.Length > EmojiLibraryStorage.MaxStickerBytes)
                        return new ClipboardImageReadResult(null, "", fi.Name, TooLarge: true);

                    byte[] bytes = File.ReadAllBytes(fi.FullName);
                    string? fmt = EmojiLibraryStorage.DetectImageFormat(bytes, fi.Extension);
                    if (fmt != null)
                        return new ClipboardImageReadResult(bytes, fmt, Path.GetFileNameWithoutExtension(fi.Name), TooLarge: false);
                }
            }

            if (Clipboard.ContainsImage())
            {
                BitmapSource? img = Clipboard.GetImage();
                if (img != null)
                {
                    using var ms = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img));
                    encoder.Save(ms);
                    byte[] bytes = ms.ToArray();
                    if (bytes.Length > EmojiLibraryStorage.MaxStickerBytes)
                        return new ClipboardImageReadResult(null, "PNG", "Screenshot", TooLarge: true);
                    return new ClipboardImageReadResult(bytes, "PNG", $"Clip_{DateTime.Now:HHmmss}", TooLarge: false);
                }
            }
        }
        catch { }

        return new ClipboardImageReadResult(null, "", "", TooLarge: false);
    }
}

internal sealed class Win32FocusRestorer : IFocusRestorer
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    nint IFocusRestorer.GetForegroundWindow() => GetForegroundWindow();

    public bool IsExternalTargetWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd) || !IsWindowVisible(hwnd)) return false;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != 0 && pid != (uint)Environment.ProcessId;
    }

    public async Task<bool> TryRestoreForegroundAsync(nint targetHwnd, int timeoutMs = 120, CancellationToken cancellationToken = default)
    {
        if (targetHwnd == 0 || !IsWindow(targetHwnd)) return false;
        if (GetForegroundWindow() == targetHwnd) return true;

        SetForegroundWindow(targetHwnd);
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetForegroundWindow() == targetHwnd) return true;
            await Task.Delay(15, cancellationToken).ConfigureAwait(true);
            elapsed += 15;
        }
        return GetForegroundWindow() == targetHwnd;
    }
}

internal sealed class EmojiInputDispatcher
{
    private readonly IEmojiClipboardBridge _clipboard;
    private readonly IFocusRestorer _focus;
    private readonly Func<int, CancellationToken, Task> _delayAsync;

    internal EmojiInputDispatcher(
        IEmojiClipboardBridge? clipboard = null,
        IFocusRestorer? focus = null,
        Func<int, CancellationToken, Task>? delayAsync = null)
    {
        _clipboard = clipboard ?? new WpfClipboardBridge();
        _focus = focus ?? new Win32FocusRestorer();
        _delayAsync = delayAsync ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    internal DispatchOutcome SendText(
        string text,
        IInputPanelSession? session,
        IHostActionInvoker? hostFallback,
        nint targetHwnd,
        bool preferClipboardFallback)
    {
        if (string.IsNullOrEmpty(text)) return DispatchOutcome.Failed;

        if (preferClipboardFallback)
            return PasteTextViaClipboard(text, session, hostFallback, targetHwnd);

        if (session is { IsOpen: true })
            return MapQueueResult(session.QueueText(text));

        if (hostFallback != null && targetHwnd != 0 && _focus.GetForegroundWindow() == targetHwnd)
            return hostFallback.SendText(text) ? DispatchOutcome.Sent : DispatchOutcome.Failed;

        return DispatchOutcome.TargetUnavailable;
    }

    internal DispatchOutcome SendSticker(
        string filePath,
        string format,
        IInputPanelSession? session,
        IHostActionInvoker? hostFallback,
        nint targetHwnd)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return DispatchOutcome.Failed;

        string? backupText = _clipboard.GetText();
        bool includeBitmapFallback = !string.Equals(format, "GIF", StringComparison.OrdinalIgnoreCase);

        if (!_clipboard.SetStickerData(filePath, includeBitmapFallback))
            return DispatchOutcome.Failed;

        uint seqAfterSet = _clipboard.GetSequenceNumber();
        DispatchOutcome outcome = SendPasteHotkey(session, hostFallback, targetHwnd);

        ScheduleClipboardTextRestore(backupText, seqAfterSet);
        return outcome;
    }

    internal bool CopyTextOnly(string text) => !string.IsNullOrEmpty(text) && _clipboard.SetText(text);

    internal bool CopyStickerOnly(string filePath, string format)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;
        bool includeBitmapFallback = !string.Equals(format, "GIF", StringComparison.OrdinalIgnoreCase);
        return _clipboard.SetStickerData(filePath, includeBitmapFallback);
    }

    internal ClipboardImageReadResult ReadImageFromClipboard() => _clipboard.ReadImage();

    internal nint GetForegroundWindow() => _focus.GetForegroundWindow();

    internal bool IsExternalTargetWindow(nint hwnd) => _focus.IsExternalTargetWindow(hwnd);

    internal Task<bool> RestoreForegroundAsync(nint targetHwnd, int timeoutMs = 120, CancellationToken ct = default) =>
        _focus.TryRestoreForegroundAsync(targetHwnd, timeoutMs, ct);

    private DispatchOutcome PasteTextViaClipboard(
        string text,
        IInputPanelSession? session,
        IHostActionInvoker? hostFallback,
        nint targetHwnd)
    {
        string? backupText = _clipboard.GetText();
        if (!_clipboard.SetText(text))
            return DispatchOutcome.Failed;

        uint seqAfterSet = _clipboard.GetSequenceNumber();
        DispatchOutcome outcome = SendPasteHotkey(session, hostFallback, targetHwnd);
        ScheduleClipboardTextRestore(backupText, seqAfterSet);
        return outcome;
    }

    private DispatchOutcome SendPasteHotkey(IInputPanelSession? session, IHostActionInvoker? hostFallback, nint targetHwnd)
    {
        if (session is { IsOpen: true })
            return MapQueueResult(session.QueueHotkey("Ctrl+V"));

        if (hostFallback != null && targetHwnd != 0 && _focus.GetForegroundWindow() == targetHwnd)
            return hostFallback.SendHotkey("Ctrl+V") ? DispatchOutcome.Sent : DispatchOutcome.Failed;

        return DispatchOutcome.TargetUnavailable;
    }

    private async void ScheduleClipboardTextRestore(string? backupText, uint expectedSeq)
    {
        if (string.IsNullOrEmpty(backupText)) return;
        try
        {
            await _delayAsync(420, CancellationToken.None).ConfigureAwait(true);
            if (_clipboard.GetSequenceNumber() == expectedSeq)
                _clipboard.SetText(backupText);
        }
        catch { }
    }

    private static DispatchOutcome MapQueueResult(InputPanelQueueResult result) => result switch
    {
        InputPanelQueueResult.Accepted => DispatchOutcome.Sent,
        InputPanelQueueResult.Busy => DispatchOutcome.Busy,
        InputPanelQueueResult.Closed => DispatchOutcome.Closed,
        _ => DispatchOutcome.Failed
    };
}
