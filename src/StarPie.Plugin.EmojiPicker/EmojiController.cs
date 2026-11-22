using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace StarPie.Plugin.EmojiPicker;

internal static class EmojiDispatch
{
    internal static async Task RunAsync(Func<Action, Task> dispatch, Action action, CancellationToken token)
    {
        Exception? failure = null;
        bool ran = false;
        await dispatch(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ran = true;
            }
        }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        if (!ran) throw new OperationCanceledException("Emoji picker UI dispatch was not executed.", token);
    }
}

internal sealed class EmojiController : IDisposable
{
    private readonly IPluginContext _context;
    private readonly IHostInputPanelService _panels;
    private readonly EmojiLibraryStorage _storage;
    private readonly EmojiImageCache _imageCache = new();
    private readonly EmojiInputDispatcher _inputDispatcher = new();
    private readonly List<IDisposable> _settingSubscriptions = new();

    private EmojiPickerWindow? _pickerWindow;
    private EmojiManagerWindow? _managerWindow;
    private bool _disposed;

    internal EmojiController(IPluginContext context, IHostInputPanelService panels)
    {
        _context = context;
        _panels = panels;
        _storage = new EmojiLibraryStorage(context.DataDirectory);

        foreach (string key in new[] { "defaultMode", "autoCloseOnPick", "gifHoverPlay", "trackRecent", "emojiClipboardFallback" })
        {
            _settingSubscriptions.Add(context.Settings.OnChanged(key, _ =>
            {
                context.Dispatcher.Post(ReloadSettingsIntoOpenWindows);
            }));
        }
    }

    private EmojiLibraryDocument LoadDocumentAndRefreshMounts()
    {
        EmojiLibraryDocument doc = _storage.Load(EmojiTexts.T(_context, "defaultGroupCats"));
        if (doc.RequiresRecovery)
        {
            _context.Log.Warn("Custom emoji library JSON could not be parsed; original file was backed up safely.");
        }
        else
        {
            ApplySettingsPageOverrides(doc.Preferences);
            foreach (StickerGroup g in doc.Groups.Where(x => x.IsMounted).ToList())
                _storage.ScanMountedFolder(doc, g);
        }
        return doc;
    }

    private void ApplySettingsPageOverrides(EmojiPreferences prefs)
    {
        string? defMode = _context.Settings.Get("defaultMode");
        if (!string.IsNullOrWhiteSpace(defMode) && Enum.TryParse<EmojiPickerMode>(defMode, true, out var parsedMode))
            prefs.DefaultMode = parsedMode.ToString();

        if (TryReadSettingBool("autoCloseOnPick", out bool autoClose))
            prefs.AutoCloseOnPick = autoClose;
        if (TryReadSettingBool("gifHoverPlay", out bool gifHover))
            prefs.GifHoverPlay = gifHover;
        if (TryReadSettingBool("trackRecent", out bool trackRecent))
            prefs.TrackRecent = trackRecent;
        if (TryReadSettingBool("emojiClipboardFallback", out bool clipFallback))
            prefs.EmojiClipboardFallback = clipFallback;
    }

    private bool TryReadSettingBool(string key, out bool result)
    {
        result = false;
        string? raw = _context.Settings.Get(key);
        return !string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw, out result);
    }

    private void ReloadSettingsIntoOpenWindows()
    {
        if (_disposed) return;
        EmojiLibraryDocument doc = LoadDocumentAndRefreshMounts();
        _pickerWindow?.RefreshFromLibrary();
        _managerWindow?.ReloadDocument(doc);
    }

    internal Task ShowPickerAsync(ActionContext target, EmojiPickerMode initialMode, CancellationToken cancellationToken) =>
        EmojiDispatch.RunAsync(_context.Dispatcher.InvokeAsync, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new OperationCanceledException(cancellationToken);

            ClosePicker();
            EmojiLibraryDocument doc = LoadDocumentAndRefreshMounts();

            var picker = new EmojiPickerWindow(
                _context,
                _panels,
                _storage,
                doc,
                _imageCache,
                _inputDispatcher,
                target,
                initialMode,
                saveRequired =>
                {
                    if (!doc.RequiresRecovery)
                    {
                        try { _storage.Save(doc); } catch { }
                    }
                    _managerWindow?.ReloadDocument(doc);
                },
                () => _ = OpenManagerAsync(CancellationToken.None));

            IInputPanelSession session;
            try
            {
                picker.PrepareWithoutActivation();
                session = _panels.Open(target, reason =>
                {
                    _context.Log.Info($"Emoji picker session closed: {reason}");
                    picker.OnSessionClosed(reason);
                });
            }
            catch
            {
                picker.Close();
                throw;
            }

            if (!session.IsOpen)
            {
                session.Dispose();
                picker.Close();
                throw new InvalidOperationException(EmojiTexts.T(_context, "targetUnavailable"));
            }

            picker.AttachSession(session);
            picker.Closed += (_, _) =>
            {
                if (ReferenceEquals(_pickerWindow, picker))
                    _pickerWindow = null;
            };
            _pickerWindow = picker;
            try
            {
                picker.Show();
            }
            catch
            {
                picker.Close();
                throw;
            }
        }, cancellationToken);

    internal Task OpenManagerAsync(CancellationToken cancellationToken) =>
        EmojiDispatch.RunAsync(_context.Dispatcher.InvokeAsync, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) return;

            ClosePicker();
            if (_managerWindow != null)
            {
                _managerWindow.Activate();
                return;
            }

            EmojiLibraryDocument doc = LoadDocumentAndRefreshMounts();
            var manager = new EmojiManagerWindow(
                _context,
                _panels,
                _storage,
                doc,
                _imageCache,
                _inputDispatcher,
                updatedDoc =>
                {
                    _pickerWindow?.RefreshFromLibrary();
                });

            manager.Closed += (_, _) =>
            {
                if (ReferenceEquals(_managerWindow, manager))
                    _managerWindow = null;
            };
            _managerWindow = manager;
            manager.Show();
        }, cancellationToken);

    private void ClosePicker()
    {
        _pickerWindow?.Close();
        _pickerWindow = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (IDisposable sub in _settingSubscriptions)
        {
            try { sub.Dispose(); } catch { }
        }
        _settingSubscriptions.Clear();

        void CloseWindows()
        {
            ClosePicker();
            _managerWindow?.Close();
            _managerWindow = null;
            _imageCache.Dispose();
        }

        if (_pickerWindow != null || _managerWindow != null)
        {
            if (_context.Dispatcher.IsOnUiThread)
            {
                CloseWindows();
            }
            else if (!_context.Dispatcher.InvokeAsync(CloseWindows).Wait(TimeSpan.FromSeconds(2)))
            {
                _context.Log.Warn("Emoji picker windows did not close before the shutdown grace period elapsed.");
            }
        }
        else
        {
            _imageCache.Dispose();
        }
    }
}
