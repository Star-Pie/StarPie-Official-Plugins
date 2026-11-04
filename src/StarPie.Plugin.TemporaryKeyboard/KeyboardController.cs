using System.Windows;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed class KeyboardController : IDisposable
{
    private readonly IPluginContext _context;
    private readonly IHostInputPanelService _panels;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly KeySchemeStorage _storage;
    private KeyboardWindow? _window;
    private KeyboardSchemeEditor? _editor;
    private bool _disposed;

    internal KeyboardController(IPluginContext context, IHostInputPanelService panels)
    {
        _context = context; _panels = panels;
        _storage = new KeySchemeStorage(context.DataDirectory);
    }

    private KeySchemeDocument ReadSchemes()
    {
        KeySchemeDocument document = _storage.Load(KeyboardTexts.T(_context, "defaultScheme"));
        if (document.RequiresRecovery) _context.Log.Warn("Custom keyboard schemes could not be read; original data was retained.");
        return document;
    }

    internal Task ShowAsync(ActionContext target, KeyboardMode mode, string? scheme, CancellationToken cancellationToken) =>
        KeyboardDispatch.RunAsync(_context.Dispatcher.InvokeAsync, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new OperationCanceledException(cancellationToken);
            ClosePanel();
            _editor?.Close();
            KeySchemeDocument document = ReadSchemes();
            var window = new KeyboardWindow(_context, _panels, document.Schemes, mode, scheme, target.CursorX, target.CursorY, document.RequiresRecovery);
            IInputPanelSession session;
            try
            {
                window.PrepareWithoutActivation();
                session = _panels.Open(target, reason =>
                { _context.Log.Info("Keyboard session closed: " + reason); window.Close(); });
            }
            catch { window.Close(); throw; }
            if (!session.IsOpen) { session.Dispose(); window.Close(); throw new InvalidOperationException(KeyboardTexts.T(_context, "targetUnavailable")); }
            window.AttachSession(session);
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            _window = window;
            try { window.Show(); }
            catch { window.Close(); throw; }
        }, cancellationToken);

    internal Task ConfigureAsync(CancellationToken cancellationToken) => KeyboardDispatch.RunAsync(_context.Dispatcher.InvokeAsync, () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return;
        ClosePanel();
        if (_editor != null) { _editor.Activate(); return; }
        KeySchemeDocument document = ReadSchemes();
        var editor = new KeyboardSchemeEditor(_context, _panels, document, _lifetime.Token);
        editor.Saved += (schemes, recovery) => _storage.Save(document, schemes, recovery);
        editor.Closed += (_, _) => { if (ReferenceEquals(_editor, editor)) _editor = null; };
        _editor = editor;
        editor.Show();
    }, cancellationToken);

    private void ClosePanel() { _window?.Close(); _window = null; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        void CloseWindows() { ClosePanel(); _editor?.Close(); _editor = null; }
        if (_window != null || _editor != null)
        {
            if (_context.Dispatcher.IsOnUiThread) CloseWindows();
            else if (!_context.Dispatcher.InvokeAsync(CloseWindows).Wait(TimeSpan.FromSeconds(2)))
                _context.Log.Warn("Keyboard windows did not close before the shutdown grace period elapsed.");
        }
        _lifetime.Dispose();
    }
}
