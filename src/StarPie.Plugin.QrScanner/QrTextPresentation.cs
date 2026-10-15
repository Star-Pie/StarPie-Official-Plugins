using System.Reflection;
using StarPie.Plugin;

namespace StarPie.Plugin.QrScanner;

/// <summary>Uses the minimum 1.12 contract, discovering live titles only when advertised by the host.</summary>
internal sealed class QrTextPresentation
{
    private readonly IHostTextResultService _results;
    private readonly Func<string, IReadOnlyList<TextResultItem>, CancellationToken, Task<TextResultSelection>>? _showLocalized;

    internal QrTextPresentation(IHostTextResultService results)
    {
        _results = results;
        // A direct reference to the 1.13 interface/member would fail to JIT against the 1.12 SDK.
        Type? localized = typeof(IHostTextResultService).Assembly.GetType("StarPie.Plugin.IHostLocalizedTextResultService");
        if (localized == null || !localized.IsInterface || !localized.IsInstanceOfType(results))
        {
            return;
        }
        MethodInfo? show = localized.GetMethod("ShowLocalizedAsync", BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.DeclaredOnly, binder: null,
            types: [typeof(string), typeof(IReadOnlyList<TextResultItem>), typeof(CancellationToken)], modifiers: null);
        if (show?.ReturnType == typeof(Task<TextResultSelection>))
        {
            _showLocalized = show.CreateDelegate<Func<string, IReadOnlyList<TextResultItem>, CancellationToken,
                Task<TextResultSelection>>>(results);
        }
    }

    internal Task<TextResultSelection> ShowAsync(IPluginContext context, IReadOnlyList<TextResultItem> items,
        CancellationToken token) => _showLocalized == null
            ? _results.ShowAsync(context.I18n.T("title"), items, token)
            : _showLocalized($"{PluginApi.I18nKeyPrefix}{context.Me.Id}.title", items, token);
}
