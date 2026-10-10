using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using StarPie.Plugin.DesktopPet;

namespace DesktopPetTests;

internal static class PetEnhancementTests
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "starpie-enhancements-" + Guid.NewGuid().ToString("N"));
        var context = new MockPluginContext(dataDirectory: Path.Combine(root, "data"));
        var controller = new PetController(context) { IsHeadlessTestMode = true };
        try
        {
            var manager = controller.LibraryManager;
            var sources = new[] { MakeSource(root, "A", 50), MakeSource(root, "B", 100), MakeSource(root, "C", 150) };
            var items = sources.Select(source => manager.ImportFolderAsync(source).GetAwaiter().GetResult().Item!).ToArray();
            string[] initial = items.Select(item => item.Id).ToArray();
            Check(manager.GetItems().Select(item => item.Id).SequenceEqual(initial), "import preserves existing array order");
            var index = JsonSerializer.Deserialize<PetLibraryIndex>(File.ReadAllText(manager.IndexFilePath))!;
            index.Version = 1;
            File.WriteAllText(manager.IndexFilePath, JsonSerializer.Serialize(index));
            string oldHash = Hash(manager.IndexFilePath);
            manager = new PetLibraryManager(context.DataDirectory);
            Check(manager.GetItems().Select(item => item.Id).SequenceEqual(initial) && Hash(manager.IndexFilePath) == oldHash, "legacy index browse preserves order without write");
            Check(manager.MoveItemAsync(items[2].Id, -1).GetAwaiter().GetResult().Success, "move selection up commits");
            string[] expected = [items[0].Id, items[2].Id, items[1].Id];
            Check(new PetLibraryManager(context.DataDirectory).GetItems().Select(item => item.Id).SequenceEqual(expected), "order survives reopen");
            string reorderedHash = Hash(manager.IndexFilePath);
            Check(!manager.MoveItemAsync(items[0].Id, -1).GetAwaiter().GetResult().Success && Hash(manager.IndexFilePath) == reorderedHash, "boundary move does not write");
            string sourceHash = Hash(Path.Combine(sources[1], "idle.png"));
            Check(manager.RemoveItemAsync(items[1].Id, _ => false).GetAwaiter().GetResult().Success, "inactive character removed from list");
            Check(manager.GetItems().Count == 2 && Directory.Exists(Path.Combine(manager.LibraryRoot, items[1].RelativeDirectory)), "removal retains derived asset directory");
            Check(Hash(Path.Combine(sources[1], "idle.png")) == sourceHash, "removal never touches original assets");
            var restored = manager.ImportFolderAsync(sources[1]).GetAwaiter().GetResult();
            Check(restored.Success && restored.Item!.Id == items[1].Id && restored.MessageKey == "library.import.restored", "reimport restores same stable character ID");
            Check(manager.GetItems().Count == 3, "restoring does not create duplicate records");

            // Controller protects both loaded and persisted references, even when ID is absent.
            Check(controller.ApplyLibraryItem(items[0]).IsSuccess, "active fixture applied safely");
            Check(!controller.RemoveLibraryItemAsync(items[0].Id).GetAwaiter().GetResult().Success, "active character cannot be removed");
            context.Settings.Set(PetPreference.ActiveLibraryIdKey, null);
            context.Settings.Set(PetPreference.AssetDirectoryKey, Path.Combine(manager.LibraryRoot, items[2].RelativeDirectory) + Path.DirectorySeparatorChar);
            Check(!controller.RemoveLibraryItemAsync(items[2].Id).GetAwaiter().GetResult().Success, "normalized persisted directory protects selection");
            context.Settings.Set(PetPreference.AssetDirectoryKey, Path.Combine(manager.LibraryRoot, items[0].RelativeDirectory));
            context.Settings.Set(PetPreference.ActiveLibraryIdKey, items[0].Id);
            Check(controller.RemoveLibraryItemAsync(items[1].Id).GetAwaiter().GetResult().Success, "controller removes inactive selection under apply gate");
            Check(!controller.ApplyLibraryItem(items[1]).IsSuccess, "removed stale item cannot be applied synchronously");
            Check(!controller.ApplyLibraryItemAsync(items[1]).GetAwaiter().GetResult().IsSuccess, "removed stale item cannot be applied asynchronously");
            Check(controller.Assets.CurrentDirectory == Path.Combine(manager.LibraryRoot, items[0].RelativeDirectory), "stale apply preserves active assets");

            // An index write failure cannot publish a new order or removal into the cache.
            var before = manager.GetItems().Select(item => item.Id).ToArray();
            File.Move(manager.IndexFilePath, manager.IndexFilePath + ".saved");
            Directory.CreateDirectory(manager.IndexFilePath);
            Check(!manager.MoveItemAsync(items[2].Id, -1).GetAwaiter().GetResult().Success && manager.GetItems().Select(item => item.Id).SequenceEqual(before), "failed reorder preserves cached order");
            Check(!manager.RemoveItemAsync(items[1].Id, _ => false).GetAwaiter().GetResult().Success && manager.GetItems().Count == 3, "failed removal preserves cached entries");
            Directory.Delete(manager.IndexFilePath);
            File.Move(manager.IndexFilePath + ".saved", manager.IndexFilePath);
            var unsafeItem = manager.FindItemById(items[1].Id)!;
            string originalDirectory = unsafeItem.RelativeDirectory;
            unsafeItem.RelativeDirectory = "../outside";
            Check(!manager.RemoveItemAsync(unsafeItem.Id, _ => false).GetAwaiter().GetResult().Success, "outside-library metadata is rejected");
            unsafeItem.RelativeDirectory = originalDirectory;
            TestAnimationAndMetadata();
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(" [FAIL] Pet enhancements: " + ex); return 1; }
        finally { controller.Shutdown(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void TestAnimationAndMetadata()
    {
        var normal = new PetAnimationStateMachine();
        var fast = new PetAnimationStateMachine { SpeedMultiplier = 2 };
        normal.Tick(TimeSpan.FromSeconds(0.4)); fast.Tick(TimeSpan.FromSeconds(0.2));
        Check(Math.Abs(normal.BreathingScaleY - fast.BreathingScaleY) < 0.000001, "2x speed advances animation twice as fast");
        var slow = new PetAnimationStateMachine { SpeedMultiplier = 0.5 };
        slow.Tick(TimeSpan.FromSeconds(0.8));
        Check(Math.Abs(normal.BreathingScaleY - slow.BreathingScaleY) < 0.000001, "half speed preserves animation phase over twice the time");
        fast.TriggerReact(); fast.Tick(TimeSpan.FromMilliseconds(250));
        Check(fast.CurrentFrame == PetFrameKind.Idle, "reaction duration follows animation speed");
        fast.SpeedMultiplier = double.NaN;
        Check(fast.SpeedMultiplier == 1, "NaN speed falls back to original speed");
        fast.SpeedMultiplier = double.PositiveInfinity;
        Check(fast.SpeedMultiplier == 1, "infinite speed rejected");
        fast.SpeedMultiplier = 100;
        Check(fast.SpeedMultiplier == 3, "speed clamped at upper bound");
        fast.SpeedMultiplier = -5;
        Check(fast.SpeedMultiplier == 0.25, "speed clamped at lower bound");
        fast.SetPointing(PetFrameKind.PointNW); fast.Tick(TimeSpan.FromSeconds(1));
        Check(fast.CurrentFrame == PetFrameKind.PointNW, "speed does not change pointing direction");
        fast.EnableAnimation = false; fast.Tick(TimeSpan.FromSeconds(1));
        Check(fast.CurrentFrame == PetFrameKind.Idle && fast.DragSwayAngle == 0, "animation off remains static at any speed");
        var fields = PetSettingsFields.Build();
        Check(fields.Single(field => field.Key == PetPreference.SizeKey).Type == ParameterFieldType.Slider, "size uses declared slider");
        Check(fields.Single(field => field.Key == PetPreference.LongPressDurationKey).Step == 10, "duration uses 10ms steps");
        Check(fields.Single(field => field.Key == PetPreference.AnimationSpeedKey).DefaultValue == "1", "old settings retain original speed by default");
        var context = new MockPluginContext();
        var plugin = new DesktopPetPlugin();
        plugin.Initialize(context);
        try
        {
            var page = context.MockSettingsPage.RegisteredPage!;
            var section = page.Sections.Single();
            Check(section.Id == "animation" && section.FieldKeys.Contains(PetPreference.EnableAnimationKey) && section.FieldKeys.Contains(PetPreference.AnimationSpeedKey), "animation section retains existing keys and adds speed");
        }
        finally { plugin.Shutdown(); }
    }

    private static string MakeSource(string root, string name, byte color)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        byte[] pixels = new byte[16 * 16 * 4];
        for (int p = 0; p < pixels.Length; p += 4) { pixels[p] = color; pixels[p + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64)));
        using var output = File.Create(Path.Combine(folder, "idle.png"));
        encoder.Save(output);
        return folder;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine(" [PASS] Pet enhancements: " + message);
    }
}
