using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StarPie.Plugin;
using StarPie.Plugin.DesktopPet;

namespace DesktopPetTests;

internal static class DesktopPetUiTests
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "starpie-ui-" + Guid.NewGuid().ToString("N"));
        var context = new MockPluginContext(dataDirectory: Path.Combine(root, "data"));
        DesktopPetPlugin.RegisterLocalization(context);
        var controller = new PetController(context) { IsHeadlessTestMode = true };
        try
        {
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            byte[] pixels = new byte[16 * 16 * 4];
            for (int p = 0; p < pixels.Length; p += 4) { pixels[p] = 150; pixels[p + 3] = 255; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64)));
            using (var stream = File.Create(Path.Combine(source, "idle.png"))) encoder.Save(stream);
            var item = controller.LibraryManager.ImportFolderAsync(source).GetAwaiter().GetResult().Item!;
            controller.OpenManagerWindow();
            var window = controller.LibraryWindow!;
            window.SelectItem(item);
            var details = (Grid)typeof(PetLibraryWindow).GetField("_detailsPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var content = (Grid)window.Content;
            content.Measure(new Size(800, 520));
            content.Arrange(new Rect(0, 0, 800, 520));
            if (!content.IsAncestorOf(details) || details.ActualWidth <= 0 || details.ActualHeight <= 0)
                throw new InvalidOperationException("selected character details must belong to rendered layout and have nonzero size");
            var apply = (Button)typeof(PetLibraryWindow).GetField("_applyButton", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            if (!content.IsAncestorOf(apply) || apply.ActualWidth <= 0)
                throw new InvalidOperationException("apply button must be reachable in the actual content tree");
            Console.WriteLine(" [PASS] DesktopPet UI: selected character preview and apply are in rendered content tree");
            content.Measure(new Size(720, 470));
            content.Arrange(new Rect(0, 0, 720, 470));
            var position = apply.TranslatePoint(new Point(), content);
            Check(position.Y >= 0 && position.Y + apply.ActualHeight <= 470, "small-window apply button remains visible outside preview scrolling");
            TestToggle(context, controller, source);
            foreach (string language in new[] { "zh-CN", "zh-TW", "en-US", "ja-JP" })
            {
                context.MockInfo.LanguageCode = language;
                Check(context.I18n.T("action.toggle-pet.name") != "action.toggle-pet.name", "toggle label localized: " + language);
                Check(context.I18n.T("field.size.help") != "field.size.help", "configuration help localized: " + language);
                Check(context.I18n.T("frame.drag") != "frame.drag", "pose label localized: " + language);
            }
            context.MockInfo.LanguageCode = "zh-CN";
            RenderPreviewIfRequested(controller, window, content);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(" [FAIL] DesktopPet UI: " + ex.Message);
            return 1;
        }
        finally
        {
            controller.Shutdown();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestToggle(MockPluginContext context, PetController controller, string source)
    {
        context.Settings.Set(PetPreference.AssetDirectoryKey, source);
        var toggle = new TogglePetContribution(context, controller, null);
        var input = new PluginActionInput { Parameters = new Dictionary<string, string> { [PetPreference.AssetDirectoryKey] = "old-invalid-sector-override" } };
        Check(toggle.ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult().Success && controller.HasWindow, "first toggle shows using global settings, ignoring obsolete sector override");
        Check(toggle.ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult().Success && !controller.HasWindow, "second toggle closes pet");
        Check(toggle.ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult().Success && controller.HasWindow, "third toggle shows again");
        var existing = controller.Window;
        var show = new ShowPetContribution(context, controller, null);
        var legacyInput = new PluginActionInput();
        show.ExecuteAsync(legacyInput, CancellationToken.None).GetAwaiter().GetResult();
        Check(ReferenceEquals(existing, controller.Window), "legacy show remains idempotent, without toggling");
        var hide = new HidePetContribution(context, controller);
        hide.ExecuteAsync(legacyInput, CancellationToken.None).GetAwaiter().GetResult();
        hide.ExecuteAsync(legacyInput, CancellationToken.None).GetAwaiter().GetResult();
        Check(!controller.HasWindow, "legacy hide remains idempotent");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(!toggle.ExecuteAsync(legacyInput, cancelled.Token).GetAwaiter().GetResult().Success && !controller.HasWindow, "canceled toggle does not change visibility");
    }

    private static void RenderPreviewIfRequested(PetController controller, PetLibraryWindow window, Grid content)
    {
        string? previewFolder = Environment.GetEnvironmentVariable("STARPIE_PET_UI_PREVIEW");
        if (string.IsNullOrEmpty(previewFolder)) return;
        string? sampleFolder = Environment.GetEnvironmentVariable("STARPIE_PET_UI_SAMPLE");
        if (!string.IsNullOrEmpty(sampleFolder))
        {
            var sample = controller.LibraryManager.ImportFolderAsync(sampleFolder).GetAwaiter().GetResult().Item;
            if (sample != null) { window.RefreshLibraryList(sample.Id); window.SelectItem(sample); }
        }
        var image = (Image)typeof(PetLibraryWindow).GetField("_previewImage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (image.Source == null && timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        Check(image.Source != null, "selected preview actually decodes into the image control");
        content.Measure(new Size(860, 600));
        content.Arrange(new Rect(0, 0, 860, 600));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(860, 600, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 860, 600));
        bitmap.Render(backdrop);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(previewFolder);
        using var output = File.Create(Path.Combine(previewFolder, "character-library.png"));
        encoder.Save(output);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine(" [PASS] DesktopPet controls: " + message);
    }
}
