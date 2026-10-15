using System.Diagnostics;
using System.Reflection;
using System.IO;
using StarPie.Plugin;
using StarPie.Plugin.QrScanner;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static CapturedImage Fixture(string text, int size = 180, bool inverted = false)
    {
        BitMatrix matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, size, size,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" });
        byte[] bytes = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = (y * size + x) * 4;
            byte shade = matrix[x, y] != inverted ? (byte)0 : (byte)255;
            bytes[offset] = bytes[offset + 1] = bytes[offset + 2] = shade; bytes[offset + 3] = 255;
        }
        return new CapturedImage(size, size, -1800, -200, bytes);
    }
    private static CapturedImage Rotate(CapturedImage image)
    {
        byte[] bytes = new byte[image.Bgra32.Length];
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            Array.Copy(image.Bgra32, (y * image.Width + x) * 4, bytes,
                (x * image.Height + image.Height - 1 - y) * 4, 4);
        return new CapturedImage(image.Height, image.Width, image.ScreenX, image.ScreenY, bytes);
    }
    private static CapturedImage Multiple(bool mixed = false, bool sameText = false)
    {
        CapturedImage left = Fixture("https://example.com/one"),
            right = Fixture(sameText ? "https://example.com/one" : "StarPie 二维码", inverted: mixed);
        int width = left.Width * 2 + 40, height = left.Height + 20;
        byte[] pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        for (int row = 0; row < left.Height; row++)
        {
            Array.Copy(left.Bgra32, row * left.Width * 4, pixels, ((row + 10) * width + 10) * 4, left.Width * 4);
            Array.Copy(right.Bgra32, row * right.Width * 4, pixels, ((row + 10) * width + left.Width + 30) * 4, right.Width * 4);
        }
        return new CapturedImage(width, height, -800, 50, pixels);
    }
    private static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-fixtures")
        {
            WriteFixtures(Path.GetFullPath(args[1]));
            return;
        }
        var decoder = new QrDecoder();
        var watch = Stopwatch.StartNew();
        foreach (string text in new[] { "https://example.com/path?q=1", "StarPie 中文測試 日本語", "WIFI:T:WPA;S:demo;P:secret;;", "plain text" })
        {
            CapturedImage image = Fixture(text);
            foreach (CapturedImage variant in new[] { image, Rotate(image), Fixture(text, inverted: true) })
            {
                IReadOnlyList<QrResult> decoded = decoder.Decode(variant, default);
                Check(decoded.Count == 1 && decoded[0].Text == text, "Decode original/rotated/inverted: " + text);
                Check(decoded[0].Points.Count >= 3 && decoded[0].Points.All(p =>
                    p.X >= image.ScreenX && p.X <= image.ScreenX + image.Width &&
                    p.Y >= image.ScreenY && p.Y <= image.ScreenY + image.Height), "Physical coordinates preserve negative origin");
            }
        }
        Check(decoder.Decode(Multiple(), default).Count == 2, "Two QR codes in one image");
        Check(decoder.Decode(Multiple(mixed: true), default).Count == 2, "Mixed ordinary/inverted QR codes");
        Check(decoder.Decode(Multiple(sameText: true), default).Count == 2, "Same payload at different positions is retained");
        Check(decoder.Decode(Multiple(mixed: true, sameText: true), default).Count == 2, "Same payload in mixed polarities is retained");
        Check(decoder.Decode(new CapturedImage(64, 64, 0, 0, Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray()), default).Count == 0, "Blank image");
        Check(decoder.Decode(Fixture("tiny", 45), default).Count == 1, "Small QR code");
        Check(QrDecoder.GetScales(2048, 2048).SequenceEqual(new[] { 1d }), "Pixel budget prevents large retries");
        Check(QrDecoder.GetScales(100, 100).Count() == 4, "Small image gets bounded retry scales");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { decoder.Decode(Fixture("cancel"), cancel.Token); Check(false, "Cancelled decode"); }
            catch (OperationCanceledException) { Check(true, "Cancelled decode before work"); }
        }
        foreach (string url in new[] { "https://example.com/a", "http://localhost:1234/path", "https://例子.测试/路径" })
            Check(TextResultUrl.TryGetWebUrl(url, out _), "Allows web address");
        foreach (string text in new[] { "javascript:alert(1)", "file:///C:/test", "ms-settings:", "ftp://example.com",
            "example.com", "https://user:pass@example.com", "https://example.com/\nrun", "plain text", "WIFI:S:demo;;" })
            Check(!TextResultUrl.TryGetWebUrl(text, out _), "Text-only address policy: " + text);

        var proxy = DispatchProxy.Create<IPluginContext, TestContextProxy>();
        var images = new FakeImages { Image = Fixture("https://example.com/confirmed") };
        var results = new FakeResults();
        var session = new QrScanSession(proxy, images, results);
        Check(new QrScanAction(proxy, session, false, "").Descriptor.TimeoutSeconds == 300 &&
            new QrScanAction(proxy, session, true, "").Descriptor.Kind == ActionKind.Background,
            "Interactive background scan uses explicit five-minute timeout");
        results.Selection = new() { Operation = TextResultOperation.Close };
        Check((await session.ScanAsync(false, 400, default)).Success && results.Shown == 1 &&
            results.Copied == 0 && results.Opened == 0, "No side effects before explicit choice");
        Check(results.LastTitle == proxy.I18n.T("title"), "Legacy result service receives translated title instead of a key");
        var localized = new FakeLocalizedResults();
        var enhancedPresentation = new QrTextPresentation(localized);
        TextResultSelection enhancedChoice = await enhancedPresentation.ShowAsync(proxy,
            [new() { Text = "text", Format = "QR Code" }], default);
        Check(localized.TitleKey == "plugin.user.qrscanner.tests.title" &&
            enhancedChoice.Operation == TextResultOperation.Close, "Optional localized service is bound by exact contract");
        using (var cancellation = new CancellationTokenSource())
        {
            localized.Pause = true;
            Task<TextResultSelection> waiting = enhancedPresentation.ShowAsync(proxy, [new() { Text = "text" }], cancellation.Token);
            cancellation.Cancel();
            try { await waiting; Check(false, "Enhanced cancellation"); }
            catch (OperationCanceledException) { Check(true, "Enhanced delegate preserves cancellation"); }
        }
        results.Selection = new() { Operation = TextResultOperation.Copy, Index = 0 };
        Check((await session.ScanAsync(false, 400, default)).Success && results.Copied == 1, "Explicit copy uses fake host service");
        results.Selection = new() { Operation = TextResultOperation.Open, Index = 0 };
        Check((await session.ScanAsync(true, 400, default)).Success && results.Opened == 1 && images.PointerCalls == 1,
            "Explicit open and pointer capture use host services");
        images.Image = Fixture("file:///C:/unsafe");
        Check(!(await session.ScanAsync(false, 400, default)).Success && results.Opened == 1, "Invalid scheme cannot reach open service");
        images.Image = null;
        int shown = results.Shown;
        Check((await session.ScanAsync(false, 400, default)).Success && results.Shown == shown, "Cancelled selection never opens results");
        images.Image = Fixture("another");
        results.Pause = new(TaskCreationOptions.RunContinuationsAsynchronously);
        results.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ActionResult> running = session.ScanAsync(false, 400, default);
        await results.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!(await session.ScanAsync(false, 400, default)).Success, "Repeated scan rejected while results active");
        session.Stop();
        Check((await running.WaitAsync(TimeSpan.FromSeconds(5))).Success, "Stop cancels pending presentation");
        Check((await session.ScanAsync(false, 400, default)).Success && results.Shown == shown + 1, "Stopped session cannot capture or reopen");
        watch.Stop();
        Console.WriteLine($"{_passed} assertions passed. Total fixture run: {watch.ElapsedMilliseconds} ms. No live capture, GUI, clipboard or browser.");
    }

    private static void WriteFixtures(string directory)
    {
        Directory.CreateDirectory(directory);
        var samples = new Dictionary<string, CapturedImage>
        {
            ["link"] = Fixture("https://example.com/one", 240),
            ["text"] = Fixture("StarPie 中文測試 日本語", 240),
            ["mixed"] = Multiple(mixed: true),
            ["same-content"] = Multiple(mixed: true, sameText: true),
            ["text-only"] = Fixture("WIFI:T:WPA;S:demo;P:example;;", 240)
        };
        foreach ((string name, CapturedImage image) in samples)
        {
            using var writer = new BinaryWriter(File.Create(Path.Combine(directory, name + ".bmp")));
            writer.Write((ushort)0x4D42); writer.Write(54 + image.Bgra32.Length);
            writer.Write(0); writer.Write(54); writer.Write(40);
            writer.Write(image.Width); writer.Write(-image.Height);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
            writer.Write(image.Bgra32.Length); writer.Write(2835); writer.Write(2835);
            writer.Write(0); writer.Write(0); writer.Write(image.Bgra32);
        }
        string cards = string.Join("", samples.Keys.Select(name =>
            $"<section><h2>{name}</h2><img src='{name}.bmp' alt='{name} QR fixture'></section>"));
        File.WriteAllText(Path.Combine(directory, "qr-fixtures.html"),
            "<!doctype html><meta charset='utf-8'><title>StarPie QR acceptance fixtures</title>" +
            "<style>body{font:16px sans-serif;margin:32px;background:#eee}main{display:flex;flex-wrap:wrap;gap:24px}" +
            "section{padding:20px;background:white}img{image-rendering:pixelated}</style>" +
            "<h1>StarPie 扫码验收样例</h1><p>网页链接、中文、普通与反色混合、同内容不同位置、Wi-Fi文本。所有图片仅供本地测试。</p><main>" + cards + "</main>");
        Console.WriteLine("Local fixtures written to " + directory);
    }

    private sealed class FakeImages : IHostImageCaptureService
    {
        internal CapturedImage? Image;
        internal int PointerCalls;
        public Task<CapturedImage?> SelectRegionAsync(CancellationToken cancellationToken) => Task.FromResult(Image);
        public Task<CapturedImage> CaptureAroundPointerAsync(int radius, CancellationToken cancellationToken)
        { PointerCalls++; return Task.FromResult(Image!); }
    }
    private sealed class FakeResults : IHostTextResultService
    {
        internal TextResultSelection Selection = new();
        internal int Shown, Copied, Opened;
        internal string? LastTitle;
        internal TaskCompletionSource<TextResultSelection>? Pause;
        internal TaskCompletionSource<bool> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TextResultSelection> ShowAsync(string title, IReadOnlyList<TextResultItem> items, CancellationToken token)
        {
            Shown++; LastTitle = title; Entered.TrySetResult(true);
            return Pause == null ? Selection : await Pause.Task.WaitAsync(token);
        }
        public Task<bool> CopyAsync(string text, CancellationToken token) { Copied++; return Task.FromResult(true); }
        public Task<bool> OpenWebUrlAsync(string text, CancellationToken token) { Opened++; return Task.FromResult(true); }
    }
    private sealed class FakeLocalizedResults : IHostLocalizedTextResultService
    {
        internal string? TitleKey;
        internal bool Pause;
        public async Task<TextResultSelection> ShowLocalizedAsync(string key, IReadOnlyList<TextResultItem> items, CancellationToken token)
        {
            TitleKey = key;
            if (Pause) await Task.Delay(Timeout.Infinite, token);
            return new() { Operation = TextResultOperation.Close };
        }
        public Task<TextResultSelection> ShowAsync(string title, IReadOnlyList<TextResultItem> items, CancellationToken token) =>
            throw new InvalidOperationException("Enhanced host must use its live title service.");
        public Task<bool> CopyAsync(string text, CancellationToken token) => throw new InvalidOperationException("No copy during presentation.");
        public Task<bool> OpenWebUrlAsync(string text, CancellationToken token) => throw new InvalidOperationException("No open during presentation.");
    }
}

public class TestContextProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "get_I18n" => DispatchProxy.Create<II18nRegistry, I18nProxy>(),
        "get_Me" => new PluginMetadata { Id = "user.qrscanner.tests" },
        "get_Log" => DispatchProxy.Create<IPluginLogger, LogProxy>(),
        _ => throw new NotSupportedException(method.Name)
    };
}
public class I18nProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => args?[0]?.ToString();
}
public class LogProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Unexpected log: " + args?[0]);
}
