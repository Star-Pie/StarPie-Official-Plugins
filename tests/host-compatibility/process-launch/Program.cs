using StarPie.Plugin;
using StarPie.OfficialPlugins;
using StarPie.Plugin.Launch;
using StarPie.Plugin.Command;
using StarPie.Plugin.ShellTool;

internal static class Program
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main()
    {
        var context = new Context();
        ProcessLaunchParameter.RegisterTexts(context);
        foreach (IActionContribution action in new IActionContribution[] { new LaunchAction(context), new CommandAction(context), new ShellToolAction(context) })
        {
            var field = action.Parameters.Single(p => p.Key == ProcessLaunchParameter.Key);
            Check(field.Type == ParameterFieldType.Enum && field.Options!.Count == 3, "all three plugins declare three-state UI");
            Check(field.DefaultValue == nameof(ProcessLaunchMode.Default), "default does not force permissions");
            Check(!HostActionFields.All.Contains(field.Key), "parameter remains plugin owned");
            foreach (ProcessLaunchMode mode in Enum.GetValues<ProcessLaunchMode>())
            {
                var values = Values(mode.ToString());
                Check(action.Validate(values) == null, "valid mode accepted " + mode);
                var result = action.ExecuteAsync(new PluginActionInput { Parameters = values }, default).GetAwaiter().GetResult();
                Check(result.Success && context.Mode == mode, "mode reaches corresponding host endpoint " + mode);
            }
            foreach (string invalid in new[] { "", "Bogus", "99", "1", "true" })
            {
                int before = context.Calls;
                var values = Values(invalid);
                Check(action.Validate(values) != null, "unknown mode rejected");
                Check(!action.ExecuteAsync(new PluginActionInput { Parameters = values }, default).GetAwaiter().GetResult().Success, "execution also rejects unknown mode");
                Check(context.Calls == before, "invalid mode never calls host");
            }
            context.Accept = false;
            var failed = action.ExecuteAsync(new PluginActionInput { Parameters = Values("Administrator") }, default).GetAwaiter().GetResult();
            Check(failed.Success && !failed.Silent && !string.IsNullOrEmpty(failed.Message), "cancel/unsupported environment informs user without quarantining plugin");
            context.Accept = true;
        }
        var launch = new LaunchAction(context);
        var legacy = Values(null); legacy[HostActionFields.RunAsStandardUser] = "true";
        launch.ExecuteAsync(new PluginActionInput { Parameters = legacy }, default).GetAwaiter().GetResult();
        Check(context.Mode == ProcessLaunchMode.StandardUser, "legacy Launch flag maps to strict standard-user mode");
        legacy[ProcessLaunchParameter.Key] = "Default";
        launch.ExecuteAsync(new PluginActionInput { Parameters = legacy }, default).GetAwaiter().GetResult();
        Check(context.Mode == ProcessLaunchMode.Default, "explicit default wins over stale legacy flag");
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            context.Translations.Language = language;
            Check(context.I18n.T("processLaunch.label") != "processLaunch.label", "translated field label " + language);
            foreach (var option in ProcessLaunchParameter.Field().Options!)
                Check(context.I18n.T(option.LabelKey!) != option.LabelKey, "translated option " + language);
        }
        IHostActionInvoker oldHost = new LegacyHost();
        IHostCommandService oldCommand = new LegacyCommand();
        IHostShellService oldShell = new LegacyShell();
        Check(oldHost.LaunchWithMode("app", ProcessLaunchMode.Default), "old host adapter keeps default launch");
        Check(oldCommand.RunWithMode("command", ProcessLaunchMode.Default), "old command adapter keeps default run");
        Check(oldShell.InvokeWithMode("verb", ProcessLaunchMode.Default), "old shell adapter keeps default invoke");
        foreach (ProcessLaunchMode mode in new[] { ProcessLaunchMode.Administrator, ProcessLaunchMode.StandardUser })
        {
            Reject(() => oldHost.LaunchWithMode("app", mode));
            Reject(() => oldCommand.RunWithMode("command", mode));
            Reject(() => oldShell.InvokeWithMode("verb", mode));
        }
        Console.WriteLine($"PASS: {checks} plugin/SDK compatibility checks; all host operations were mocked.");
    }
    static void Reject(Action call) { try { call(); throw new Exception("old adapter accepted unsupported explicit mode"); } catch (NotSupportedException) { checks++; } }
    static Dictionary<string,string> Values(string? mode)
    {
        var values = new Dictionary<string,string> { [HostActionFields.Parameter] = "example", [HostActionFields.Arguments] = "--test", [HostActionFields.CommandTerminal] = "cmd" };
        if (mode != null) values[ProcessLaunchParameter.Key] = mode;
        return values;
    }
}

internal class LegacyHost : IHostActionInvoker
{
    public bool Launch(string path, string arguments = "", bool runAsStandardUser = false) => true;
    public bool SendHotkey(string hotkey) => true;
    public bool SendText(string text) => true;
    public bool OpenFolder(string folderPath) => true;
    public bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null) => true;
    public bool SetClipboardText(string text) => true;
    public string? GetClipboardText() => null;
}
internal class LegacyCommand : IHostCommandService
{
    public IReadOnlyList<CommandTerminalOption> Terminals => new[] { new CommandTerminalOption { Id = "cmd", DisplayName = "CMD" } };
    public bool Run(string command, string terminal = "cmd") => true;
}
internal class LegacyShell : IHostShellService
{
    public IReadOnlyList<ShellVerbOption> Verbs => Array.Empty<ShellVerbOption>();
    public bool Invoke(string verb) => true;
}
internal sealed class Host : LegacyHost, IHostActionInvoker
{
    readonly Context context; public Host(Context context) => this.context = context;
    public bool LaunchWithMode(string path, ProcessLaunchMode mode, string arguments = "") => context.Record(mode);
}
internal sealed class Commands : LegacyCommand, IHostCommandService
{
    readonly Context context; public Commands(Context context) => this.context = context;
    public bool RunWithMode(string command, ProcessLaunchMode mode, string terminal = "cmd") => context.Record(mode);
}
internal sealed class Shell : LegacyShell, IHostShellService
{
    readonly Context context; public Shell(Context context) => this.context = context;
    public bool InvokeWithMode(string verb, ProcessLaunchMode mode) => context.Record(mode);
}
internal sealed class Translations : II18nRegistry
{
    readonly Dictionary<string,Dictionary<string,string>> tables = new();
    public string Language = "zh-CN";
    public void Register(string key, string zhCn, string? en = null) { RegisterTable("zh-CN",new Dictionary<string,string>{{key,zhCn}}); if(en!=null)RegisterTable("en",new Dictionary<string,string>{{key,en}}); }
    public void RegisterTable(string languageCode, IReadOnlyDictionary<string,string> table) { if(!tables.TryGetValue(languageCode,out var values)) tables[languageCode]=values=new(); foreach(var value in table)values[value.Key]=value.Value; }
    public string T(string key, string? fallback = null) => tables.TryGetValue(Language,out var values)&&values.TryGetValue(key,out var value)?value:fallback??key;
}
internal sealed class Logger : IPluginLogger
{
    public string LogFilePath => "";
    public void Debug(string message) {} public void Info(string message) {} public void Warn(string message) {} public void Error(string message, Exception? exception = null) {}
}
internal sealed class Context : IPluginContext
{
    public ProcessLaunchMode Mode; public int Calls; public bool Accept = true;
    public bool Record(ProcessLaunchMode mode) { Mode=mode; Calls++; return Accept; }
    public Translations Translations = new();
    public Context() { Host=new Host(this); Commands=new Commands(this); Shell=new Shell(this); }
    public IHostActionInvoker Host { get; }
    public IHostCommandService Commands { get; }
    public IHostShellService Shell { get; }
    public II18nRegistry I18n => Translations;
    public IPluginLogger Log => new Logger();
    public PluginMetadata Me => throw new NotSupportedException();
    public string PluginDirectory => ""; public string DataDirectory => "";
    public IPluginSettings Settings => throw new NotSupportedException();
    public IActionRegistry Actions => throw new NotSupportedException();
    public IIconRegistry Icons => throw new NotSupportedException();
    public ISettingsPageRegistry SettingsPage => throw new NotSupportedException();
    public IHostWindowService Windows => throw new NotSupportedException();
    public IHostScreenCaptureService ScreenCapture => throw new NotSupportedException();
    public IHostSystemService System => throw new NotSupportedException();
    public IHostWheelService Wheel => throw new NotSupportedException();
    public IHostKeyboardRemapService KeyboardRemap => throw new NotSupportedException();
    public IHostInfo Info => throw new NotSupportedException();
    public INotificationService Notify => throw new NotSupportedException();
    public IPluginEvents Events => throw new NotSupportedException();
    public IDispatcherFacade Dispatcher => throw new NotSupportedException();
}
