using StarPie.Plugin;

namespace StarPie.Plugin.QrScanner;

public sealed class QrScannerPlugin : IStarPiePlugin
{
    private readonly List<IDisposable> _registrations = new();
    private QrScanSession? _session;

    public void Initialize(IPluginContext context)
    {
        if (context is not IImageCapturePluginContext images || context is not ITextResultPluginContext results)
            throw new PluginContractException("QR scanning requires host SDK 1.12 image and text result services.");
        QrTexts.Register(context.I18n);
        string icon = context.Icons.RegisterSvg("qr", "M2,2 L9,2 L9,9 L2,9 Z M15,2 L22,2 L22,9 L15,9 Z M2,15 L9,15 L9,22 L2,22 Z M15,15 L18,15 L18,18 L22,18 L22,22 L18,22 L18,20 L15,20 Z");
        _session = new QrScanSession(context, images.Images, results.TextResults);
        _registrations.Add(context.Actions.Register(new QrScanAction(context, _session, false, icon)));
        _registrations.Add(context.Actions.Register(new QrScanAction(context, _session, true, icon)));
    }

    public void Shutdown()
    {
        _session?.Stop();
        _session = null;
        foreach (IDisposable registration in _registrations) registration.Dispose();
        _registrations.Clear();
    }
}

internal sealed class QrScanSession(IPluginContext context, IHostImageCaptureService images, IHostTextResultService results)
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly QrTextPresentation _presentation = new(results);
    private int _busy;
    internal void Stop() => _shutdown.Cancel();

    internal async Task<ActionResult> ScanAsync(bool aroundPointer, int radius, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return ActionResult.Fail(context.I18n.T("busy"));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        CancellationToken token = linked.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            CapturedImage? image = aroundPointer
                ? await images.CaptureAroundPointerAsync(radius, token).ConfigureAwait(false)
                : await images.SelectRegionAsync(token).ConfigureAwait(false);
            if (image == null) return ActionResult.Empty;
            IReadOnlyList<QrResult> decoded = await Task.Run(() => new QrDecoder().Decode(image, token), token).ConfigureAwait(false);
            // Only text and small position data survive into the result window.
            image = null;
            if (decoded.Count == 0) return ActionResult.Ok(context.I18n.T("notfound"), silent: false);
            TextResultItem[] items = decoded.Select(item => new TextResultItem { Text = item.Text, Format = "QR Code" }).ToArray();
            TextResultSelection choice = await _presentation.ShowAsync(context, items, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (choice.Operation == TextResultOperation.Close) return ActionResult.Empty;
            if (choice.Index < 0 || choice.Index >= items.Length) return ActionResult.Fail(context.I18n.T("failed"));
            string text = items[choice.Index].Text;
            bool success = choice.Operation switch
            {
                TextResultOperation.Copy => await results.CopyAsync(text, token).ConfigureAwait(false),
                TextResultOperation.Open => TextResultUrl.TryGetWebUrl(text, out string url) &&
                    await results.OpenWebUrlAsync(url, token).ConfigureAwait(false),
                _ => false
            };
            return success ? ActionResult.Empty : ActionResult.Fail(context.I18n.T("operation.failed"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return ActionResult.Empty; }
        catch (Exception ex)
        {
            // Never log decoded payloads: login/payment QR data can be private.
            context.Log.Error("QR scan failed.", ex);
            return ActionResult.Fail(context.I18n.T("failed"));
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}
