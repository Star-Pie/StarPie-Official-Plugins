namespace StarPie.Plugin.QuickSearch;

public sealed class QuickSearchPlugin : IStarPiePlugin
{
    private ShowSearchAction? _action;
    private IDisposable? _registration;
    private IDisposable? _languageSubscription;

    public void Initialize(IPluginContext context)
    {
        SearchTexts.Register(context.I18n);
        _action = new ShowSearchAction(context);
        _registration = context.Actions.Register(_action);
        _languageSubscription = context.Events.OnLanguageChanged(_ => _action?.RefreshLanguage());
    }

    public void Shutdown()
    {
        Interlocked.Exchange(ref _languageSubscription, null)?.Dispose();
        Interlocked.Exchange(ref _action, null)?.Stop();
        Interlocked.Exchange(ref _registration, null)?.Dispose();
    }
}

internal sealed class ShowSearchAction(IPluginContext context) : IActionContribution
{
    // Only the dispatcher touches the window. A retired action cannot reopen it
    // even when an invocation was queued before the host unloaded the plugin.
    private readonly SearchResults _search = new(context);
    private volatile QuickSearchWindow? _window;
    private volatile bool _stopped;

    public ActionDescriptor Descriptor => new()
    {
        Id = "show",
        DisplayName = context.I18n.T("title"),
        DisplayNameKey = "title",
        Description = context.I18n.T("description"),
        DescriptionKey = "description",
        IconKey = "Search",
        Kind = ActionKind.Background
    };

    public IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();
    public string? Validate(IReadOnlyDictionary<string, string> parameters) => null;
    public string Preview(IReadOnlyDictionary<string, string> parameters) => context.I18n.T("title");

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        if (_stopped || cancellationToken.IsCancellationRequested) return ActionResult.Empty;
        if (!context.Info.HasCapability(PluginCapability.Ui) || !context.Info.HasCapability(PluginCapability.FileSystem))
            return ActionResult.Fail(context.I18n.T("permission"));

        ActionResult outcome = ActionResult.Empty;
        await context.Dispatcher.InvokeAsync(() =>
        {
            if (_stopped || cancellationToken.IsCancellationRequested) return;
            try
            {
                if (_window is null)
                {
                    var window = new QuickSearchWindow(context, input, _search);
                    _window = window;
                    window.Closed += (_, _) => { if (ReferenceEquals(window, _window)) _window = null; };
                }
                // Stop may run off-dispatcher while the WPF tree is being constructed.
                if (_stopped || cancellationToken.IsCancellationRequested) _window.Close();
                else _window.Present(input.Context.CursorX, input.Context.CursorY);
            }
            catch (Exception ex)
            {
                _window?.Close();
                context.Log.Error("Could not show Quick Search.", ex);
                if (!_stopped) outcome = ActionResult.Fail(context.I18n.T("failure"));
            }
        }).ConfigureAwait(false);
        return outcome;
    }

    internal void RefreshLanguage()
    {
        if (_stopped || _window is null) return;
        if (context.Dispatcher.IsOnUiThread) _window?.RefreshLanguage();
        else context.Dispatcher.Post(() => { if (!_stopped) _window?.RefreshLanguage(); });
    }

    internal void Stop()
    {
        _stopped = true;
        _search.Dispose();
        if (_window is null) return;
        if (context.Dispatcher.IsOnUiThread) _window?.Close();
        else context.Dispatcher.Post(() => _window?.Close());
    }
}
