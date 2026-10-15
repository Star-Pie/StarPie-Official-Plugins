using System.Reflection;
using System.Runtime.Loader;
using StarPie.Plugin;
using ZXing;
using ZXing.QrCode;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static async Task Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the candidate plugin directory.");
        string output = Path.GetFullPath(args[0]);
        Assembly sdk = typeof(IPluginContext).Assembly;
        Check(sdk.GetName().Version == new Version(1, 0, 0, 0), "Shared SDK identity retained");
        Check(sdk.GetType("StarPie.Plugin.IHostLocalizedTextResultService") == null &&
            typeof(IHostTextResultService).GetMethod("ShowLocalizedAsync") == null,
            "Actual loaded 1.12 binary has no 1.13 interface or member");
        Check((string)typeof(PluginApi).GetField("ApiVersion")!.GetRawConstantValue()! == "1.12", "Actual SDK API is 1.12");
        var proxy = DispatchProxy.Create<IProbeContext, ProbeContext>();
        var state = (ProbeContext)(object)proxy;
        state.Images.Image = Fixture("https://example.com/compatibility");
        var loader = new CandidateLoadContext(output);
        Assembly assembly = loader.LoadFromAssemblyPath(Path.Combine(output, "StarPie.Plugin.QrScanner.dll"));
        var plugin = (IStarPiePlugin)Activator.CreateInstance(assembly.GetType("StarPie.Plugin.QrScanner.QrScannerPlugin")!)!;
        plugin.Initialize(proxy);
        Check(state.Registry.Actions.Count == 2, "New candidate initializes and registers both actions on SDK 1.12");
        object session = plugin.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
        MethodInfo scan = session.GetType().GetMethod("ScanAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<ActionResult> Scan(bool pointer = false, CancellationToken token = default) =>
            (Task<ActionResult>)scan.Invoke(session, [pointer, 400, token])!;

        state.Results.Selection = new() { Operation = TextResultOperation.Close };
        Check((await Scan()).Success && state.Results.Shown == 1 && state.Results.Copied == 0 && state.Results.Opened == 0,
            "Complete fake capture/decode/presentation closes without side effects");
        Check(state.Results.Title == "旧宿主结果" && state.Results.Text == "https://example.com/compatibility",
            "Fallback title is translated and decoded content is intact");
        state.Results.Selection = new() { Operation = TextResultOperation.Copy, Index = 0 };
        Check((await Scan()).Success && state.Results.Copied == 1 && state.Results.Opened == 0,
            "Explicit copy reaches fake service only");
        state.Results.Selection = new() { Operation = TextResultOperation.Open, Index = 0 };
        Check((await Scan(pointer: true)).Success && state.Results.Opened == 1 && state.Images.PointerCalls == 1,
            "Pointer capture and explicit web choice work through old contract");
        {
            state.Results.Pause = true;
            Task<ActionResult> pending = Scan();
            await state.Results.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            plugin.Shutdown();
            Check((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Success,
                "Shutdown cancels old-service presentation without invoking a system operation");
        }
        loader.Unload();
        Check(state.Registry.Disposed == 2, "Shutdown revokes both fake registration tokens");
        Console.WriteLine($"{_passed} SDK 1.12 binary compatibility assertions passed. Only fake capture/results; no GUI or live actions.");
    }

    private static CapturedImage Fixture(string text)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 180, 180);
        byte[] pixels = new byte[matrix.Width * matrix.Height * 4];
        for (int y = 0; y < matrix.Height; y++)
        for (int x = 0; x < matrix.Width; x++)
        {
            int index = (y * matrix.Width + x) * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = matrix[x, y] ? (byte)0 : (byte)255;
            pixels[index + 3] = 255;
        }
        return new CapturedImage(matrix.Width, matrix.Height, -300, 40, pixels);
    }

    private sealed class CandidateLoadContext(string output) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == PluginApi.AbstractionsAssemblyName) return typeof(IPluginContext).Assembly;
            string file = Path.Combine(output, name.Name + ".dll");
            return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
        }
    }
}

public interface IProbeContext : IImageCapturePluginContext, ITextResultPluginContext { }

public class ProbeContext : DispatchProxy
{
    internal FakeImages Images { get; } = new();
    internal FakeResults Results { get; } = new();
    internal FakeActions Registry { get; } = new();
    private readonly FakeI18n _texts = new();
    private readonly FakeIcons _icons = new();
    private readonly FakeLog _log = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_Me" => new PluginMetadata { Id = "starpie.plugin.qrscanner" },
        "get_Images" => Images,
        "get_TextResults" => Results,
        "get_Actions" => Registry,
        "get_I18n" => _texts,
        "get_Icons" => _icons,
        "get_Log" => _log,
        _ => throw new InvalidOperationException("Unexpected host service: " + method.Name)
    };
}

internal sealed class FakeImages : IHostImageCaptureService
{
    internal CapturedImage? Image;
    internal int PointerCalls;
    public Task<CapturedImage?> SelectRegionAsync(CancellationToken token) => Task.FromResult(Image);
    public Task<CapturedImage> CaptureAroundPointerAsync(int radius, CancellationToken token)
    {
        PointerCalls++;
        return Task.FromResult(Image!);
    }
}

internal sealed class FakeResults : IHostTextResultService
{
    internal TextResultSelection Selection = new();
    internal string? Title, Text;
    internal int Shown, Copied, Opened;
    internal bool Pause;
    internal TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<TextResultSelection> ShowAsync(string title, IReadOnlyList<TextResultItem> items, CancellationToken token)
    {
        Shown++; Title = title; Text = items[0].Text;
        if (!Pause) return Selection;
        Entered.TrySetResult(true);
        await Task.Delay(Timeout.Infinite, token);
        return Selection;
    }
    public Task<bool> CopyAsync(string text, CancellationToken token) { Copied++; return Task.FromResult(true); }
    public Task<bool> OpenWebUrlAsync(string text, CancellationToken token) { Opened++; return Task.FromResult(true); }
}

internal sealed class FakeActions : IActionRegistry
{
    internal List<IActionContribution> Actions { get; } = new();
    internal int Disposed;
    public IDisposable Register(IActionContribution action)
    {
        Actions.Add(action);
        return new Registration(this);
    }
    private sealed class Registration(FakeActions owner) : IDisposable
    {
        public void Dispose() => owner.Disposed++;
    }
}

internal sealed class FakeI18n : II18nRegistry
{
    public void Register(string key, string zhCn, string? en = null) { }
    public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table) { }
    public string T(string key, string? fallback = null) => key == "title" ? "旧宿主结果" : fallback ?? key;
}

internal sealed class FakeIcons : IIconRegistry
{
    public string RegisterSvg(string key, string svgPathData) => "plugin:starpie.plugin.qrscanner:" + key;
    public string? ResolveKey(string key) => null;
}

internal sealed class FakeLog : IPluginLogger
{
    public string LogFilePath => "";
    public void Debug(string message) => throw new InvalidOperationException("Unexpected log: " + message);
    public void Info(string message) => throw new InvalidOperationException("Unexpected log: " + message);
    public void Warn(string message) => throw new InvalidOperationException("Unexpected log: " + message);
    public void Error(string message, Exception? exception = null) => throw new InvalidOperationException(message, exception);
}
