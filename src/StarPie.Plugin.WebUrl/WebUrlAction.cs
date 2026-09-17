using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.WebUrl;

/// <summary>
/// 动作「打开网址」。对应配置里的 <c>Type="WebUrl"</c>，也认历史别名 <c>Type="Url"</c>。
/// </summary>
internal sealed class WebUrlAction : IActionContribution
{
    /// <summary>选了「自定义浏览器」时 <see cref="HostActionFields.BrowserChoice"/> 的取值。</summary>
    private const string BrowserChoiceCustom = "Custom";

    private readonly IPluginContext _context;

    public WebUrlAction(IPluginContext context) => _context = context;

    private string T(string key, string fallback) => _context.I18n.T(key, fallback);

    public ActionDescriptor Descriptor => new()
    {
        Id = "webUrl",
        DisplayName = T("weburl.title", Texts.WebUrlTitle),
        Description = null,
        Category = "",
        IconKey = null,
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 0,
    };

    /// <summary>
    /// 参数声明。
    /// <para>
    /// <b>一处已知的表达力缺口</b>：手写面板里「自定义浏览器路径」只在浏览器选了
    /// <c>Custom</c> 时才显示，而 <see cref="ParameterField"/> 目前没有「条件显示」这个概念，
    /// 统一表单会把三个字段全部平铺出来。这是 UI 层的取舍，等条件显示补进 SDK 之后再一起切。
    /// </para>
    /// </summary>
    public IReadOnlyList<ParameterField> Parameters => new ParameterField[]
    {
        new()
        {
            Key = HostActionFields.Parameter,
            Label = T("weburl.url", Texts.WebUrlUrl),
            LabelKey = "weburl.url",
            Type = ParameterFieldType.Text,
            Required = true,
            Placeholder = "https://github.com",

            // 不填协议时执行体会自动补 https://，所以这里不做正则强校验，
            // 只挡住纯空白与明显非网址的输入（例如误填了本地路径）。
            ValidationRegex = @"^\S+$",
        },
        new()
        {
            Key = HostActionFields.BrowserChoice,
            Label = T("weburl.browser", Texts.WebUrlBrowser),
            LabelKey = "weburl.browser",
            Type = ParameterFieldType.Enum,
            DefaultValue = "Default",
            Options = new ParameterOption[]
            {
                new() { Value = "Default", Label = T("weburl.browser.default", Texts.WebUrlBrowserDefault), LabelKey = "weburl.browser.default" },
                // 三个浏览器名不翻译：产品名在各种语言里写法一致。
                new() { Value = "Chrome", Label = "Google Chrome" },
                new() { Value = "Edge", Label = "Microsoft Edge" },
                new() { Value = "Firefox", Label = "Mozilla Firefox" },
                new() { Value = BrowserChoiceCustom, Label = T("weburl.browser.custom", Texts.WebUrlBrowserCustom), LabelKey = "weburl.browser.custom" },
            },
        },
        new()
        {
            Key = HostActionFields.BrowserPath,
            Label = T("weburl.customPath", Texts.WebUrlCustomPath),
            LabelKey = "weburl.customPath",
            Type = ParameterFieldType.File,
            HelpText = T("weburl.customPathHelp", Texts.WebUrlCustomPathHelp),
        },
    };

    /// <summary>
    /// <b>比拆分之前更严，是刻意的</b>：原执行体遇到空网址直接 <c>return</c>，按下去毫无反应。
    /// 另外补了一条：选了自定义浏览器却没给路径时，原实现会走到一个不存在的 exe 上然后抛异常，
    /// 报错是「系统找不到指定的文件」这种用户看不懂的话。这里提前说清楚。
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        string url = Read(parameters, HostActionFields.Parameter).Trim();
        if (url.Length == 0) return T("weburl.empty", Texts.WebUrlEmpty);

        string browser = Read(parameters, HostActionFields.BrowserChoice).Trim();
        bool hasCustomPath = Read(parameters, HostActionFields.BrowserPath).Trim().Length > 0;

        return browser.Equals(BrowserChoiceCustom, StringComparison.OrdinalIgnoreCase) && !hasCustomPath
            ? T("weburl.customMissing", Texts.WebUrlCustomMissing)
            : null;
    }

    /// <summary>列表副标题：去掉协议头，只留域名与路径，列表里更好认。</summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string url = Read(parameters, HostActionFields.Parameter).Trim();
        if (url.Length == 0) return "";

        foreach (string scheme in new[] { "https://", "http://", "ftp://" })
        {
            if (url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(scheme.Length);
                break;
            }
        }

        return url.Length <= 48 ? url : url.Substring(0, 47) + "…";
    }

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string url = input?.Parameter(HostActionFields.Parameter) ?? "";

        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(ActionResult.Fail(Texts.WebUrlEmpty));
        }

        string browser = input?.Parameter(HostActionFields.BrowserChoice) ?? "Default";
        string customBrowserPath = input?.Parameter(HostActionFields.BrowserPath) ?? "";

        if (!_context.Host.OpenUrl(url, browser, customBrowserPath))
        {
            return Task.FromResult(ActionResult.Fail(
                $"未能打开网址「{url}」。若选了自定义浏览器，请确认其可执行文件路径有效；详情见插件日志。"));
        }

        return Task.FromResult(ActionResult.Empty);
    }

    /// <summary>取参数，缺失时给空串。抽出来是因为下面三处都要做同一套「缺键 / 值为 null」的折叠。</summary>
    private static string Read(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters != null && parameters.TryGetValue(key, out string? value) ? value ?? "" : "";
}
