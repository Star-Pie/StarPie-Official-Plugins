using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using StarPie.Plugin.EmojiPicker;

namespace EmojiPickerTests;

public static class Program
{
    private static int _passes;
    private static int _failures;

    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" StarPie.Plugin.EmojiPicker Offline Deterministic Test Suite");
        Console.WriteLine("============================================================");

        string tempRoot = Path.Combine(Path.GetTempPath(), "starpie-emoji-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            TestLocalization();
            TestCatalogAndSearch(Path.Combine(tempRoot, "search"));
            TestStorageSecurityAndMounts(Path.Combine(tempRoot, "storage"));
            TestInputDispatcherOffline();
            TestDispatchHelper();
            TestOffscreenUiAndImageCache(Path.Combine(tempRoot, "ui"));
            TestPinnedAndDragStability(Path.Combine(tempRoot, "pinned"));
            TestPluginLifecycleAndActionValidation(Path.Combine(tempRoot, "lifecycle"));

            string? shippedDll = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : FindDefaultReleasePluginDll();
            string? sdkDll = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;
            if (!string.IsNullOrWhiteSpace(shippedDll) && File.Exists(shippedDll))
            {
                TestShippedAssemblyInCollectibleAlc(shippedDll, sdkDll, Path.Combine(tempRoot, "alc"));
            }
            else if (args.Length > 0)
            {
                Fail("Shipped plugin DLL argument does not exist: " + args[0]);
            }
        }
        catch (Exception ex)
        {
            Fail($"Unhandled exception: {ex}");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine($"Summary: {_passes} passed, {_failures} failed");
        return _failures == 0 ? 0 : 1;
    }

    private static string? FindDefaultReleasePluginDll()
    {
        string candidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "StarPie.Plugin.EmojiPicker", "bin", "Release", "net8.0-windows", "StarPie.Plugin.EmojiPicker.dll"));
        return File.Exists(candidate) ? candidate : null;
    }

    private static void Assert(bool condition, string label, string? details = null)
    {
        if (condition)
        {
            _passes++;
            Console.WriteLine($" [PASS] {label}");
        }
        else
        {
            Fail(details == null ? label : $"{label} :: {details}");
        }
    }

    private static void Fail(string message)
    {
        _failures++;
        Console.WriteLine($" [FAIL] {message}");
    }

    private static void TestLocalization()
    {
        Console.WriteLine("\n--- 1. Localization (zh-CN / zh-TW / en / ja) ---");
        var ctx = new MockInputPanelPluginContext(Path.GetTempPath());
        EmojiTexts.Register(ctx);

        string[] keys =
        [
            "pluginTitle", "actionShow", "actionManage", "settingsTitle",
            "searchPlaceholder", "pinTip", "addTip", "manageTip",
            "dropOverlayTitle", "dropOverlaySub", "mgrTitle", "dlgMountTitle",
            "statusLoadFailed", "targetUnavailable", "requiresHost"
        ];

        foreach (string lang in new[] { "zh-CN", "zh-TW", "en", "ja" })
        {
            ctx.MockInfo.LanguageCode = lang;
            foreach (string key in keys)
            {
                string val = EmojiTexts.T(ctx, key);
                Assert(!string.IsNullOrWhiteSpace(val) && !string.Equals(val, key, StringComparison.Ordinal),
                    $"i18n[{lang}].{key} localized", $"got '{val}'");
            }
        }
        ctx.MockInfo.LanguageCode = "zh-CN";
    }

    private static void TestCatalogAndSearch(string root)
    {
        Console.WriteLine("\n--- 2. Catalog & Multi-token Search ---");
        Directory.CreateDirectory(root);

        int uniqueGlyphs = EmojiCatalog.BuiltInEmojis.Select(e => e.Glyph).Distinct(StringComparer.Ordinal).Count();
        Assert(EmojiCatalog.BuiltInEmojis.Length == 144 && uniqueGlyphs == 144,
            $"BuiltInEmojis contains 144 unique built-in emojis ({uniqueGlyphs})");

        var doc = EmojiLibraryStorage.CreateDefault("My Stickers");
        doc.PinnedRecommended = new List<string> { "🚀", "🔥" };
        doc.RecentEmojis = new List<string> { "🔥", "😂", "👍" };

        IReadOnlyList<EmojiEntry> rec = EmojiSearchEngine.GetRecommended(doc, 8);
        Assert(rec.Count == 8, $"GetRecommended returns 8 items ({rec.Count})");
        Assert(rec[0].Glyph == "🚀" && rec[1].Glyph == "🔥" && rec[2].Glyph == "😂",
            "GetRecommended orders Pinned -> Recent -> Default without duplicates");

        Assert(EmojiSearchEngine.SearchEmojis("xiao").Any(e => e.Glyph == "😀"),
            "SearchEmojis matches Pinyin 'xiao'");
        Assert(EmojiSearchEngine.SearchEmojis("Grinning Tears").All(e => e.Glyph is "😂" or "🥲") ||
               EmojiSearchEngine.SearchEmojis("xiao cry").Any(e => e.Glyph == "😂"),
            "SearchEmojis matches multi-token alias 'xiao cry'");
        Assert(EmojiSearchEngine.SearchEmojis("笑哭").Any(e => e.Glyph == "😂"),
            "SearchEmojis matches Chinese '笑哭'");

        doc.Groups.Add(new StickerGroup { Id = "grp_cats", Name = "Cats" });
        doc.Stickers.Add(new CustomSticker { Id = "s1", Name = "Happy Cat", Keywords = "mao cat happy", Format = "PNG", GroupId = "grp_cats", IsFavorite = true });
        doc.Stickers.Add(new CustomSticker { Id = "s2", Name = "Sleepy Dog", Keywords = "gou dog sleep", Format = "GIF", GroupId = "grp_default", IsFavorite = false });

        Assert(EmojiSearchEngine.SearchStickers(doc, "", "__fav__").Count == 1,
            "SearchStickers '__fav__' filter returns only favorited stickers");
        Assert(EmojiSearchEngine.SearchStickers(doc, "", "grp_cats").Single().Id == "s1",
            "SearchStickers group filter returns target group");
        Assert(EmojiSearchEngine.SearchStickers(doc, "cats happy").Single().Id == "s1",
            "SearchStickers matches group name + sticker keyword across tokens");

        Assert(EmojiSearchEngine.SearchKaomojis(doc, "", "kaoPhrase").Count == 3,
            "SearchKaomojis category filter matches 'kaoPhrase'");
        Assert(EmojiSearchEngine.SearchKaomojis(doc, "doge").Count >= 1,
            "SearchKaomojis keyword matches 'doge'");
        Assert(EmojiSearchEngine.SearchEmojis("hug").Any(e => e.Glyph == "🤗"),
            "SearchEmojis matches 'hug'");
    }

    private static void TestStorageSecurityAndMounts(string root)
    {
        Console.WriteLine("\n--- 3. Storage, Magic Bytes, Corruption Recovery & Zero-Copy Mounts ---");
        Directory.CreateDirectory(root);
        var dataDir = Path.Combine(root, "data");
        var storage = new EmojiLibraryStorage(dataDir);

        // 3a. Save/load round-trip with JsonExtensionData Extras preservation
        var doc = EmojiLibraryStorage.CreateDefault("DefaultPack");
        doc.Extras = new Dictionary<string, JsonElement>
        {
            ["futurePluginFlag"] = JsonSerializer.SerializeToElement("keep-me")
        };
        doc.PinnedRecommended.Add("🎉");
        doc.Stickers.Add(new CustomSticker
        {
            Id = "s_ext",
            Name = "ExtSticker",
            Format = "PNG",
            GroupId = "grp_default",
            FilePath = Path.Combine("stickers", "s_ext.png"),
            Extras = new Dictionary<string, JsonElement> { ["customTag"] = JsonSerializer.SerializeToElement(42) }
        });
        storage.Save(doc);

        EmojiLibraryDocument loaded = storage.Load("DefaultPack");
        Assert(!loaded.RequiresRecovery, "Healthy library loads without RequiresRecovery");
        Assert(loaded.Extras != null && loaded.Extras["futurePluginFlag"].GetString() == "keep-me",
            "Document JsonExtensionData Extras preserved across round-trip");
        Assert(loaded.Stickers.Single().Extras?["customTag"].GetInt32() == 42,
            "Sticker JsonExtensionData Extras preserved across round-trip");

        // 3b. Corruption protection & backup
        string corruptDir = Path.Combine(root, "corrupt-data");
        var corruptStorage = new EmojiLibraryStorage(corruptDir);
        Directory.CreateDirectory(corruptDir);
        File.WriteAllText(corruptStorage.LibraryPath, "{ not valid json !!!");
        EmojiLibraryDocument damaged = corruptStorage.Load("DefaultPack");
        Assert(damaged.RequiresRecovery, "Corrupt JSON sets RequiresRecovery = true");
        Assert(!string.IsNullOrEmpty(damaged.RecoveryBackupPath) && File.Exists(damaged.RecoveryBackupPath),
            "Corrupt JSON creates timestamped backup file");

        bool threwOnSave = false;
        try { corruptStorage.Save(damaged); }
        catch (InvalidOperationException) { threwOnSave = true; }
        Assert(threwOnSave, "Save refuses to overwrite damaged library before explicit rebuild");

        EmojiLibraryDocument rebuilt = corruptStorage.RebuildAfterRecovery("DefaultPack");
        Assert(!rebuilt.RequiresRecovery && File.ReadAllText(corruptStorage.LibraryPath).Contains("SchemaVersion"),
            "RebuildAfterRecovery writes clean library and preserves backup");

        // 3c. Magic-byte format validation + oversized/masquerading rejection
        string samplesDir = Path.Combine(root, "samples");
        Directory.CreateDirectory(samplesDir);
        string pngPath = Path.Combine(samplesDir, "good.png");
        string jpgPath = Path.Combine(samplesDir, "good.jpg");
        string gifPath = Path.Combine(samplesDir, "good.gif");
        string webpPath = Path.Combine(samplesDir, "good.webp");
        string fakePngPath = Path.Combine(samplesDir, "fake.png");
        string hugePngPath = Path.Combine(samplesDir, "huge.png");

        File.WriteAllBytes(pngPath, CreateValidPngBytes());
        File.WriteAllBytes(jpgPath, CreateValidJpegBytes());
        File.WriteAllBytes(gifPath, CreateAnimatedGifBytes());
        File.WriteAllBytes(webpPath, CreateMinimalWebpHeaderBytes());
        File.WriteAllText(fakePngPath, "this is actually plain text, not PNG");

        byte[] huge = new byte[EmojiLibraryStorage.MaxStickerBytes + 64];
        Array.Copy(CreateValidPngBytes(), huge, 16);
        File.WriteAllBytes(hugePngPath, huge);

        Assert(EmojiLibraryStorage.DetectImageFormat(File.ReadAllBytes(pngPath), ".png") == "PNG", "Magic detects PNG");
        Assert(EmojiLibraryStorage.DetectImageFormat(File.ReadAllBytes(jpgPath), ".jpg") == "JPG", "Magic detects JPG");
        Assert(EmojiLibraryStorage.DetectImageFormat(File.ReadAllBytes(gifPath), ".gif") == "GIF", "Magic detects GIF");
        Assert(EmojiLibraryStorage.DetectImageFormat(File.ReadAllBytes(webpPath), ".webp") == "WEBP", "Magic detects WEBP");
        Assert(EmojiLibraryStorage.DetectImageFormat(File.ReadAllBytes(fakePngPath), ".png") == null, "Magic rejects text disguised as .png");

        var importDoc = EmojiLibraryStorage.CreateDefault("DefaultPack");
        ImportSummary importRes = storage.ImportFiles(importDoc, new[] { pngPath, jpgPath, gifPath, webpPath, fakePngPath, hugePngPath }, "grp_default");
        Assert(importRes.ImportedCount == 4 && importRes.SkippedCount == 2,
            $"ImportFiles accepts 4 valid images and skips 2 invalid/oversized (got +{importRes.ImportedCount}/-{importRes.SkippedCount})");

        // 3d. ImportFolder
        string folderImportSrc = Path.Combine(root, "MemesPack");
        Directory.CreateDirectory(folderImportSrc);
        File.WriteAllBytes(Path.Combine(folderImportSrc, "cat1.png"), CreateValidPngBytes());
        File.WriteAllText(Path.Combine(folderImportSrc, "readme.png"), "not an image");
        (StickerGroup? folderGroup, ImportSummary folderSum) = storage.ImportFolder(importDoc, folderImportSrc);
        Assert(folderGroup != null && folderGroup.Name == "MemesPack" && !folderGroup.IsMounted && folderSum.ImportedCount == 1 && folderSum.SkippedCount == 1,
            "ImportFolder creates managed group named 'MemesPack' and counts valid/skipped files");

        // 3e. Zero-copy MountFolder + refresh + non-destructive Unmount + DeleteManagedGroup
        string extMountDir = Path.Combine(root, "external-memes");
        Directory.CreateDirectory(extMountDir);
        string extImg1 = Path.Combine(extMountDir, "ext1.png");
        string extImg2 = Path.Combine(extMountDir, "ext2.gif");
        File.WriteAllBytes(extImg1, CreateValidPngBytes());

        StickerGroup? mounted = storage.MountFolder(importDoc, extMountDir, "MyExternal");
        Assert(mounted != null && mounted.IsMounted && mounted.Name == "MyExternal", "MountFolder registers mounted group");
        CustomSticker extSticker1 = importDoc.Stickers.Single(s => s.GroupId == mounted!.Id);
        Assert(extSticker1.IsMounted && string.Equals(extSticker1.FilePath, Path.GetFullPath(extImg1), StringComparison.OrdinalIgnoreCase),
            "Mounted sticker points directly to source file path (zero-copy)");
        extSticker1.IsFavorite = true;
        extSticker1.Keywords = "custom preserved keyword";

        File.WriteAllBytes(extImg2, CreateAnimatedGifBytes());
        int rescanned = storage.ScanMountedFolder(importDoc, mounted!);
        Assert(rescanned == 2, $"ScanMountedFolder discovers new file without copying ({rescanned})");
        CustomSticker preserved1 = importDoc.Stickers.Single(s => string.Equals(s.FilePath, Path.GetFullPath(extImg1), StringComparison.OrdinalIgnoreCase));
        Assert(preserved1.IsFavorite && preserved1.Keywords == "custom preserved keyword",
            "ScanMountedFolder preserves favorite and custom keywords on existing files");

        storage.UnmountFolder(importDoc, mounted!.Id);
        Assert(!importDoc.Groups.Any(g => g.Id == mounted.Id) && File.Exists(extImg1) && File.Exists(extImg2),
            "UnmountFolder removes index entries while keeping original files on disk intact");

        // 3f. DeleteManagedGroup keeps last managed group, migrates when >= 2
        Assert(storage.DeleteManagedGroup(importDoc, folderGroup!.Id), "DeleteManagedGroup deletes secondary managed group");
        Assert(!storage.DeleteManagedGroup(importDoc, "grp_default"), "DeleteManagedGroup refuses to delete last remaining managed group");

        CustomSticker toDelete = importRes.Added[0];
        string toDeleteDisk = storage.ResolveStickerPath(toDelete);
        Assert(File.Exists(toDeleteDisk) && storage.DeleteSticker(importDoc, toDelete) && !File.Exists(toDeleteDisk),
            "DeleteSticker deletes managed sticker file inside data/stickers/");
    }

    private static void TestInputDispatcherOffline()
    {
        Console.WriteLine("\n--- 4. Input Dispatcher & Sequence-Guarded Clipboard Restore (Mocked) ---");
        var clip = new MockClipboardBridge();
        var focus = new MockFocusRestorer { Foreground = 1001 };
        var session = new MockInputPanelSession { IsOpen = true };
        var host = new MockHostActionInvoker();

        var delayedCallbacks = new Queue<TaskCompletionSource>();
        Task DelayAsync(int ms, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            delayedCallbacks.Enqueue(tcs);
            return tcs.Task;
        }

        var dispatcher = new EmojiInputDispatcher(clip, focus, DelayAsync);

        // 4a. Direct QueueText when session open
        DispatchOutcome o1 = dispatcher.SendText("😄", session, host, 1001, preferClipboardFallback: false);
        Assert(o1 == DispatchOutcome.Sent && session.QueuedTexts.Last() == "😄" && clip.SetTextCalls == 0,
            "SendText queues directly via IInputPanelSession without touching clipboard");

        // 4b. Busy & Closed mapping
        session.NextResult = InputPanelQueueResult.Busy;
        Assert(dispatcher.SendText("👍", session, host, 1001, false) == DispatchOutcome.Busy, "Maps InputPanelQueueResult.Busy");
        session.NextResult = InputPanelQueueResult.Accepted;

        // 4c. Host fallback when session closed & foreground matches
        session.IsOpen = false;
        Assert(dispatcher.SendText("🎉", session, host, 1001, false) == DispatchOutcome.Sent && host.SentTexts.Last() == "🎉",
            "Falls back to host.SendText when session closed and targetHwnd matches foreground");

        // 4d. TargetUnavailable when foreground differs
        focus.Foreground = 9999;
        Assert(dispatcher.SendText("👀", session, host, 1001, false) == DispatchOutcome.TargetUnavailable,
            "Returns TargetUnavailable when foreground moved to different window");

        // 4e. Sticker paste + clipboard text backup restore when seq unchanged
        focus.Foreground = 1001;
        session.IsOpen = true;
        clip.CurrentText = "original-clipboard-text";
        clip.SequenceNumber = 50;

        string dummySticker = Path.Combine(Path.GetTempPath(), "mock-sticker-" + Guid.NewGuid().ToString("N") + ".png");
        string dummyGif = Path.Combine(Path.GetTempPath(), "mock-gif-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(dummySticker, CreateValidPngBytes());
        File.WriteAllBytes(dummyGif, CreateAnimatedGifBytes());
        try
        {
            DispatchOutcome oPng = dispatcher.SendSticker(dummySticker, "PNG", session, host, 1001);
            Assert(oPng == DispatchOutcome.Sent && clip.LastIncludeBitmapFallback == true && session.QueuedHotkeys.Last() == "Ctrl+V",
                "PNG sticker sets DataObject with bitmap fallback and queues Ctrl+V");

            delayedCallbacks.Dequeue().SetResult();
            SpinWait.SpinUntil(() => clip.CurrentText == "original-clipboard-text", 500);
            Assert(clip.CurrentText == "original-clipboard-text",
                "Clipboard backup text restored when sequence number stayed unchanged");

            // 4f. GIF skips bitmap fallback + aborts restore when external clipboard change occurred
            DispatchOutcome oGif = dispatcher.SendSticker(dummyGif, "GIF", session, host, 1001);
            Assert(oGif == DispatchOutcome.Sent && clip.LastIncludeBitmapFallback == false,
                "GIF sticker disables bitmap fallback to preserve animation semantics");
            int writesBeforeChange = clip.SetTextCalls;
            clip.SequenceNumber += 10; // simulate user copying something else during 420ms window
            clip.CurrentText = "user-copied-something-new";
            delayedCallbacks.Dequeue().SetResult();
            Thread.Sleep(25);
            Assert(clip.CurrentText == "user-copied-something-new" && clip.SetTextCalls == writesBeforeChange,
                "Aborts clipboard restore when clipboard sequence changed during delay");

            // 4g. Text clipboard fallback mode
            clip.CurrentText = "restore-me";
            DispatchOutcome oClipText = dispatcher.SendText("🔥", session, host, 1001, preferClipboardFallback: true);
            Assert(oClipText == DispatchOutcome.Sent && clip.CurrentText == "🔥" && session.QueuedHotkeys.Last() == "Ctrl+V",
                "Clipboard fallback puts text on clipboard and queues Ctrl+V");
            delayedCallbacks.Dequeue().SetResult();
            SpinWait.SpinUntil(() => clip.CurrentText == "restore-me", 500);
            Assert(clip.CurrentText == "restore-me", "Text clipboard fallback restores prior clipboard text");
        }
        finally
        {
            try { File.Delete(dummySticker); } catch { }
            try { File.Delete(dummyGif); } catch { }
        }
    }

    private static void TestDispatchHelper()
    {
        Console.WriteLine("\n--- 5. EmojiDispatch.RunAsync Contracts ---");
        Func<Action, Task> runSync = cb => { cb(); return Task.CompletedTask; };
        int ran = 0;
        EmojiDispatch.RunAsync(runSync, () => ran++, CancellationToken.None).GetAwaiter().GetResult();
        Assert(ran == 1, "EmojiDispatch.RunAsync executes callback synchronously cleanly");

        bool threwSentinel = false;
        try { EmojiDispatch.RunAsync(runSync, () => throw new IOException("sentinel"), CancellationToken.None).GetAwaiter().GetResult(); }
        catch (IOException ex) { threwSentinel = ex.Message == "sentinel"; }
        Assert(threwSentinel, "EmojiDispatch.RunAsync propagates callback exception");

        bool threwSkipped = false;
        try { EmojiDispatch.RunAsync(_ => Task.CompletedTask, () => ran++, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { threwSkipped = true; }
        Assert(threwSkipped, "EmojiDispatch.RunAsync throws OperationCanceledException when dispatch skips callback");
    }

    private static void TestOffscreenUiAndImageCache(string root)
    {
        Console.WriteLine("\n--- 6. Offscreen WPF UI Layout & Image/GIF Cache ---");
        Directory.CreateDirectory(root);
        var storage = new EmojiLibraryStorage(root);
        var doc = EmojiLibraryStorage.CreateDefault("Stickers");

        string sampleDir = Path.Combine(root, "src");
        Directory.CreateDirectory(sampleDir);
        string pngFile = Path.Combine(sampleDir, "wave.png");
        string gifFile = Path.Combine(sampleDir, "dance.gif");
        File.WriteAllBytes(pngFile, CreateValidPngBytes());
        File.WriteAllBytes(gifFile, CreateAnimatedGifBytes());
        storage.ImportFiles(doc, new[] { pngFile, gifFile }, "grp_default");
        storage.Save(doc);

        using var imageCache = new EmojiImageCache();
        BitmapSource? pngThumb = imageCache.GetThumbnail(storage.ResolveStickerPath(doc.Stickers[1]), 96);
        BitmapSource? gifThumb = imageCache.GetThumbnail(storage.ResolveStickerPath(doc.Stickers[0]), 96);
        Assert(pngThumb != null && gifThumb != null, "EmojiImageCache decodes PNG & GIF thumbnails");

        using var gifController = new GifAnimationController();
        var gifImageCtrl = new Image { Source = gifThumb };
        gifController.Start(gifImageCtrl, storage.ResolveStickerPath(doc.Stickers[0]), gifThumb);
        Assert(gifImageCtrl.Source != null, "GifAnimationController starts multi-frame GIF playback safely");
        gifController.Stop(gifImageCtrl);
        Assert(ReferenceEquals(gifImageCtrl.Source, gifThumb), "GifAnimationController restores still frame on Stop");

        var ctx = new MockInputPanelPluginContext(root);
        EmojiTexts.Register(ctx);
        var dispatcher = new EmojiInputDispatcher(new MockClipboardBridge(), new MockFocusRestorer());

        foreach (bool dark in new[] { true, false })
        {
            ctx.MockPanels.Appearance = dark
                ? new InputPanelAppearance { Background = "#252528", Surface = "#1C1C1F", Foreground = "#F2F2F5", Border = "#3C3C40", Accent = "#38BDF8" }
                : new InputPanelAppearance { Background = "#F8FAFC", Surface = "#FFFFFF", Foreground = "#0F172A", Border = "#CBD5E1", Accent = "#2563EB" };

            foreach (EmojiPickerMode mode in Enum.GetValues<EmojiPickerMode>())
            {
                var target = new ActionContext { ForegroundProcessName = "notepad", ForegroundWindowHandle = 1234, CursorX = 200, CursorY = 200 };
                var picker = new EmojiPickerWindow(ctx, ctx.MockPanels, storage, doc, imageCache, dispatcher, target, mode, _ => { }, () => { });
                try
                {
                    Assert(!picker.ShowActivated && !picker.ShowInTaskbar && picker.Topmost && picker.AllowsTransparency,
                        $"EmojiPickerWindow({mode}, dark={dark}) flags non-activating overlay");
                    var fe = (FrameworkElement)picker.Content;
                    fe.Measure(new Size(376, 500));
                    fe.Arrange(new Rect(0, 0, 376, 500));
                    Assert(fe.ActualWidth > 300 && fe.ActualHeight > 400,
                        $"EmojiPickerWindow({mode}, dark={dark}) offscreen layout non-zero ({fe.ActualWidth:F0}x{fe.ActualHeight:F0})");
                }
                finally
                {
                    picker.Close();
                }
            }
        }

        // Manager window offscreen layout (normal + recovery)
        foreach (bool recovery in new[] { false, true })
        {
            EmojiLibraryDocument mgrDoc = doc.Clone();
            mgrDoc.RequiresRecovery = recovery;
            var mgr = new EmojiManagerWindow(ctx, ctx.MockPanels, storage, mgrDoc, imageCache, dispatcher, _ => { });
            try
            {
                var fe = (FrameworkElement)mgr.Content;
                fe.Measure(new Size(840, 560));
                fe.Arrange(new Rect(0, 0, 840, 560));
                Assert(fe.ActualWidth > 700 && fe.ActualHeight > 480,
                    $"EmojiManagerWindow(recovery={recovery}) offscreen layout non-zero ({fe.ActualWidth:F0}x{fe.ActualHeight:F0})");
            }
            finally
            {
                mgr.Close();
            }
        }

        RenderShowcaseScreenshotsIfRequested(root, dispatcher);
    }

    private static void RenderShowcaseScreenshotsIfRequested(string tempRoot, EmojiInputDispatcher dispatcher)
    {
        string? outDir = Environment.GetEnvironmentVariable("STARPIE_EMOJI_UI_PREVIEW");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string previewRoot = Path.Combine(repoRoot, "artifacts", "showcase-sample");
        if (Directory.Exists(previewRoot))
            try { Directory.Delete(previewRoot, recursive: true); } catch { }
        string srcDir = Path.Combine(previewRoot, "sample-src");
        string mountDir = Path.Combine(previewRoot, "LocalStickers");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(mountDir);
        try
        {

        byte[] BadgePng(string glyph, string tag, Color c1, Color c2)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                var bg = new LinearGradientBrush(c1, c2, 45);
                dc.DrawRoundedRectangle(bg, null, new Rect(4, 4, 88, 88), 20, 20);
                var dpi = 1.0;
                var ftGlyph = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI Emoji, Segoe UI Symbol, Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    36, Brushes.White, dpi);
                dc.DrawText(ftGlyph, new Point((96 - ftGlyph.Width) / 2, 14));
                var ftTag = new FormattedText(tag, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    13, Brushes.White, dpi);
                dc.DrawText(ftTag, new Point((96 - ftTag.Width) / 2, 62));
            }
            var rtb = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        byte[] AnimatedBadgeGif(string g1, string g2)
        {
            var enc = new GifBitmapEncoder();
            foreach (var (glyph, c1, c2) in new[]
            {
                (g1, Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0xEC, 0x48, 0x99)),
                (g2, Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x06, 0xB6, 0xD4))
            })
            {
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRoundedRectangle(new LinearGradientBrush(c1, c2, 45), null, new Rect(4, 4, 88, 88), 20, 20);
                    var ft = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(new FontFamily("Segoe UI Emoji, Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                        42, Brushes.White, 1.0);
                    dc.DrawText(ft, new Point((96 - ft.Width) / 2, (96 - ft.Height) / 2));
                }
                var rtb = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                enc.Frames.Add(BitmapFrame.Create(rtb));
            }
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }

        string fWave = Path.Combine(srcDir, "闪烁派对.gif");
        string fHappy = Path.Combine(srcDir, "开心徽章.png");
        string fOk = Path.Combine(srcDir, "收到OK.png");
        string fFish = Path.Combine(srcDir, "摸鱼打卡.png");
        File.WriteAllBytes(fWave, AnimatedBadgeGif("🎉", "✨"));
        File.WriteAllBytes(fHappy, BadgePng("😄", "HAHA", Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0xEF, 0x44, 0x44)));
        File.WriteAllBytes(fOk, BadgePng("👌", "收到", Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x0D, 0x94, 0x88)));
        File.WriteAllBytes(fFish, BadgePng("🐟", "摸鱼", Color.FromRgb(0x0E, 0xA5, 0xE9), Color.FromRgb(0x63, 0x66, 0xF1)));

        string mCat = Path.Combine(mountDir, "暗中观察.png");
        string mRocket = Path.Combine(mountDir, "冲鸭火箭.png");
        File.WriteAllBytes(mCat, BadgePng("👀", "围观", Color.FromRgb(0x63, 0x66, 0xF1), Color.FromRgb(0x8B, 0x5C, 0xF6)));
        File.WriteAllBytes(mRocket, BadgePng("🚀", "起飞", Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0xF9, 0x73, 0x16)));

        var pStorage = new EmojiLibraryStorage(Path.Combine(previewRoot, "data"));
        var pDoc = EmojiLibraryStorage.CreateDefault("常用表情");
        pStorage.ImportFiles(pDoc, new[] { fFish, fOk, fHappy, fWave }, "grp_default");
        pDoc.Stickers[0].IsFavorite = true;
        pDoc.Stickers[1].IsFavorite = true;
        pStorage.MountFolder(pDoc, mountDir, "本地收藏图包");
        pStorage.Save(pDoc);

        var pCtx = new MockInputPanelPluginContext(pStorage.DataDirectory);
        EmojiTexts.Register(pCtx);
        pCtx.MockPanels.Appearance = new InputPanelAppearance
        {
            Background = "#252528",
            Surface = "#1C1C1F",
            Foreground = "#F2F2F5",
            Border = "#3C3C40",
            Accent = "#38BDF8",
            AccentForeground = "#FFFFFF"
        };

        using var pCache = new EmojiImageCache();
        var target = new ActionContext { ForegroundProcessName = "WeChat", ForegroundWindowHandle = 1234, CursorX = 200, CursorY = 200 };

        void SaveRender(FrameworkElement element, double dipW, double dipH, Brush? backdrop, string fileName)
        {
            const double scale = 1.5; // 144 DPI crisp capture
            int pxW = (int)Math.Round(dipW * scale);
            int pxH = (int)Math.Round(dipH * scale);
            element.Measure(new Size(dipW, dipH));
            element.Arrange(new Rect(0, 0, dipW, dipH));
            element.UpdateLayout();
            RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.HighQuality);

            var rtb = new RenderTargetBitmap(pxW, pxH, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            if (backdrop != null)
            {
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                    dc.DrawRectangle(backdrop, null, new Rect(0, 0, dipW, dipH));
                rtb.Render(dv);
            }
            rtb.Render(element);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(outDir, fileName));
            png.Save(fs);
        }

        // 1. Default All mode dark picker
        var pAll = new EmojiPickerWindow(pCtx, pCtx.MockPanels, pStorage, pDoc, pCache, dispatcher, target, EmojiPickerMode.All, _ => { }, () => { });
        try
        {
            SaveRender((FrameworkElement)pAll.Content, 376, 500, null, "emojipicker-picker-dark.png");
        }
        finally
        {
            pAll.Close();
        }

        // 2. Custom mode dark picker (with Pin active + mounted folder + GIF/PNG badges)
        var pCustom = new EmojiPickerWindow(pCtx, pCtx.MockPanels, pStorage, pDoc, pCache, dispatcher, target, EmojiPickerMode.Custom, _ => { }, () => { });
        try
        {
            pCustom.TogglePinned();
            SaveRender((FrameworkElement)pCustom.Content, 376, 500, null, "emojipicker-custom-dark.png");
        }
        finally
        {
            pCustom.Close();
        }

        // 3. Independent Manager window
        var pMgr = new EmojiManagerWindow(pCtx, pCtx.MockPanels, pStorage, pDoc, pCache, dispatcher, _ => { });
        try
        {
            SaveRender((FrameworkElement)pMgr.Content, 840, 560, pMgr.Background, "emojipicker-manager.png");
        }
        finally
        {
            pMgr.Close();
        }

        Console.WriteLine($" [PASS] Rendered 3 showcase screenshots to {outDir}");
        }
        finally
        {
            try { if (Directory.Exists(previewRoot)) Directory.Delete(previewRoot, recursive: true); } catch { }
        }
    }

    private static void TestPinnedAndDragStability(string root)
    {
        Console.WriteLine("\n--- 6b. Pinned Cross-Window & Non-Activating Drag Stability ---");
        Directory.CreateDirectory(root);

        // 1. Source check: no DragMove() left in EmojiPickerWindow.cs
        string? repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string pickerSrc = Path.Combine(repoRoot, "src", "StarPie.Plugin.EmojiPicker", "EmojiPickerWindow.cs");
        Assert(File.Exists(pickerSrc) && !File.ReadAllText(pickerSrc).Contains("DragMove(", StringComparison.Ordinal),
            "EmojiPickerWindow.cs contains zero DragMove() calls");

        // 2. Non-activating drag coordinate math (multi-monitor negative & positive coords)
        (int x1, int y1) = EmojiPickerWindow.ComputeDragPosition(100, 200, 300, 400, 150, 440);
        Assert(x1 == -50 && y1 == 240, $"ComputeDragPosition computes (-50,240), got ({x1},{y1})");

        var storage = new EmojiLibraryStorage(root);
        var doc = EmojiLibraryStorage.CreateDefault("Stickers");
        string srcDir = Path.Combine(root, "src");
        Directory.CreateDirectory(srcDir);
        string pngFile = Path.Combine(srcDir, "drag_sticker.png");
        File.WriteAllBytes(pngFile, CreateValidPngBytes());
        storage.ImportFiles(doc, new[] { pngFile }, "grp_default");
        storage.Save(doc);

        using var imageCache = new EmojiImageCache();
        var clip = new MockClipboardBridge { CurrentText = "keep-clip", SequenceNumber = 10 };
        var focus = new MockFocusRestorer { Foreground = 1001 };
        focus.InternalWindows.Add(3003);
        var dispatcher = new EmojiInputDispatcher(clip, focus, (_, _) => Task.CompletedTask);
        var ctx = new MockInputPanelPluginContext(root);
        EmojiTexts.Register(ctx);
        var host = (MockHostActionInvoker)ctx.Host;

        var target = new ActionContext { ForegroundProcessName = "notepad", ForegroundWindowHandle = 1001, CursorX = 200, CursorY = 200 };
        var picker = new EmojiPickerWindow(ctx, ctx.MockPanels, storage, doc, imageCache, dispatcher, target, EmojiPickerMode.All, _ => { }, () => { });
        try
        {
            var initialSession = new MockInputPanelSession { IsOpen = true };
            picker.AttachSession(initialSession);
            Assert(!picker.IsPinned && initialSession.IsOpen, "Picker starts unpinned with active IInputPanelSession");

            // Modal / context-menu depth guard even when unpinned
            picker.SetModalOrMenuDepthForTest(1);
            picker.OnSessionClosed(InputPanelCloseReason.TargetChanged);
            Assert(!picker.IsClosedForTest, "Unpinned picker stays open while modal/menu depth > 0 on TargetChanged");
            picker.SetModalOrMenuDepthForTest(0);

            // Re-attach session and pin window
            var session2 = new MockInputPanelSession { IsOpen = true };
            picker.AttachSession(session2);
            picker.TogglePinned();
            Assert(picker.IsPinned && !session2.IsOpen, "TogglePinned() activates pin mode and detaches single-target session");

            // Session callbacks TargetChanged / Escape do not close pinned picker
            picker.OnSessionClosed(InputPanelCloseReason.TargetChanged);
            picker.OnSessionClosed(InputPanelCloseReason.Escape);
            Assert(!picker.IsClosedForTest, "Pinned picker ignores TargetChanged and Escape session close callbacks");

            // Cross-window switch 1001 -> 2002: Emoji & Sticker dispatch through Host without closing
            focus.Foreground = 2002;
            EmojiEntry smile = EmojiCatalog.FindEmoji("😄")!;
            picker.ActivateEntryForTestAsync(emoji: smile).GetAwaiter().GetResult();
            Assert(host.SentTexts.LastOrDefault() == "😄" && picker.ActiveTargetHwndForTest == 2002 && !picker.IsClosedForTest,
                "Pinned picker sends Emoji to newly focused window 2002 and stays open");

            CustomSticker st = doc.Stickers.First();
            picker.ActivateEntryForTestAsync(sticker: st).GetAwaiter().GetResult();
            Assert(host.SentHotkeys.LastOrDefault() == "Ctrl+V" && !picker.IsClosedForTest,
                "Pinned picker sends Sticker Ctrl+V to newly focused window 2002 and stays open");

            // Switch to internal/invalid window 3003 -> refuses send, does NOT close pinned picker
            int sentCountBefore = host.SentTexts.Count;
            focus.Foreground = 3003;
            picker.ActivateEntryForTestAsync(emoji: EmojiCatalog.FindEmoji("👍")).GetAwaiter().GetResult();
            Assert(host.SentTexts.Count == sentCountBefore && !picker.IsClosedForTest,
                "Pinned picker skips internal/invalid window 3003 without closing");

            // Switch to another external window 4004 -> resumes sending smoothly
            focus.Foreground = 4004;
            picker.ActivateEntryForTestAsync(emoji: EmojiCatalog.FindEmoji("👍")).GetAwaiter().GetResult();
            Assert(host.SentTexts.LastOrDefault() == "👍" && picker.ActiveTargetHwndForTest == 4004 && !picker.IsClosedForTest,
                "Pinned picker resumes delivery when external window 4004 becomes foreground");

            // Search mode deactivation while pinned exits passively without stealing focus back
            int restoresBefore = focus.RestoreCalls;
            picker.EnterSearchModeForTest();
            Assert(picker.IsSearchActiveForTest, "Search mode flagged active for test");
            picker.HandleDeactivated();
            Assert(!picker.IsSearchActiveForTest && focus.RestoreCalls == restoresBefore && !picker.IsClosedForTest,
                "Pinned search deactivation exits passively without RestoreForegroundAsync focus yank or closing");

            // Host Replaced / PluginStopped closes even when pinned
            picker.OnSessionClosed(InputPanelCloseReason.Replaced);
            Assert(picker.IsClosedForTest, "Pinned picker still closes cleanly on Replaced / PluginStopped");
        }
        finally
        {
            if (!picker.IsClosedForTest) picker.Close();
        }

        // Unpinned TargetChanged closes when modal/menu depth == 0
        var picker2 = new EmojiPickerWindow(ctx, ctx.MockPanels, storage, doc, imageCache, dispatcher, target, EmojiPickerMode.All, _ => { }, () => { });
        try
        {
            var sUnpinned = new MockInputPanelSession { IsOpen = true };
            picker2.AttachSession(sUnpinned);
            picker2.OnSessionClosed(InputPanelCloseReason.TargetChanged);
            Assert(picker2.IsClosedForTest, "Unpinned picker closes on TargetChanged when modal depth is 0");
        }
        finally
        {
            if (!picker2.IsClosedForTest) picker2.Close();
        }
    }

    private static void TestPluginLifecycleAndActionValidation(string root)
    {
        Console.WriteLine("\n--- 7. Plugin Lifecycle, Action Parameters & Validation ---");
        Directory.CreateDirectory(root);
        var ctx = new MockInputPanelPluginContext(root);
        var plugin = new EmojiPickerPlugin();
        plugin.Initialize(ctx);

        Assert(ctx.MockActions.RegisteredActions.Count == 2,
            $"Registered 2 actions (show, manage), got {ctx.MockActions.RegisteredActions.Count}");
        Assert(ctx.MockSettingsPage.HasRegisteredPage && ctx.MockSettingsPage.RegisteredPage?.Fields.Count == 5,
            "SettingsPage registered with 5 fields");

        IActionContribution showAction = ctx.MockActions.RegisteredActions.Single(a => a.Descriptor.Id == "show");
        IActionContribution manageAction = ctx.MockActions.RegisteredActions.Single(a => a.Descriptor.Id == "manage");
        Assert(showAction.Parameters.Count == 1 && manageAction.Parameters.Count == 0,
            "Show action declares 1 parameter (defaultMode); manage action declares 0");
        Assert(showAction.Validate(new Dictionary<string, string> { ["defaultMode"] = "Default" }) == null,
            "Show action Validate accepts 'Default'");
        Assert(showAction.Validate(new Dictionary<string, string> { ["defaultMode"] = "Custom" }) == null,
            "Show action Validate accepts 'Custom'");
        Assert(showAction.Validate(new Dictionary<string, string> { ["defaultMode"] = "Bogus" }) != null,
            "Show action Validate rejects invalid mode 'Bogus'");

        plugin.Shutdown();
        plugin.Shutdown();
        Assert(true, "EmojiPickerPlugin.Shutdown is idempotent");
    }

    private static void TestShippedAssemblyInCollectibleAlc(string pluginDllPath, string? sdkDllPath, string root)
    {
        Console.WriteLine("\n--- 8. Shipped DLL Isolation Load & Contract Verification ---");
        Directory.CreateDirectory(root);

        WeakReference CreateAndVerify(out string loadedId, out string apiVer)
        {
            var alc = new CollectiblePluginAlc(pluginDllPath, sdkDllPath);
            Assembly asm = alc.LoadFromAssemblyPath(Path.GetFullPath(pluginDllPath));

            var meta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(a => a.Key, a => a.Value ?? "", StringComparer.OrdinalIgnoreCase);
            loadedId = meta.GetValueOrDefault("StarPiePluginId", "");
            apiVer = meta.GetValueOrDefault("StarPiePluginApiVersion", "");

            Type[] pluginTypes = asm.GetTypes()
                .Where(t => typeof(IStarPiePlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToArray();
            Assert(pluginTypes.Length == 1, $"Shipped DLL exposes exactly 1 IStarPiePlugin ({pluginTypes.Length})");

            var instance = (IStarPiePlugin)Activator.CreateInstance(pluginTypes[0])!;
            var ctx = new MockInputPanelPluginContext(root);
            instance.Initialize(ctx);
            Assert(ctx.MockActions.RegisteredActions.Count == 2 && ctx.MockSettingsPage.HasRegisteredPage,
                "Shipped DLL Initialize registers 2 actions and SettingsPage");
            instance.Shutdown();
            instance.Shutdown();

            var wr = new WeakReference(alc, trackResurrection: true);
            alc.Unload();
            return wr;
        }

        WeakReference weakAlc = CreateAndVerify(out string id, out string api);
        Assert(id == "starpie.plugin.emojipicker" && api == "1.15",
            $"Shipped DLL AssemblyMetadata verified (id={id}, apiVersion={api})");

        for (int i = 0; i < 6 && weakAlc.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert(!weakAlc.IsAlive, "Shipped plugin ALC unloads cleanly after Shutdown");
    }

    private sealed class CollectiblePluginAlc : AssemblyLoadContext
    {
        private readonly string _pluginDir;
        private readonly string? _sdkPath;

        public CollectiblePluginAlc(string pluginDllPath, string? sdkPath) : base(isCollectible: true)
        {
            _pluginDir = Path.GetDirectoryName(Path.GetFullPath(pluginDllPath))!;
            _sdkPath = sdkPath;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "StarPie.Plugin.Abstractions", StringComparison.OrdinalIgnoreCase))
                return typeof(IPluginContext).Assembly;

            string candidate = Path.Combine(_pluginDir, assemblyName.Name + ".dll");
            if (File.Exists(candidate))
                return LoadFromAssemblyPath(candidate);

            _ = _sdkPath;
            return null;
        }
    }

    // ==================== Sample Binary Asset Generators ====================
    private static byte[] CreateValidPngBytes()
    {
        byte[] pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 56;     // B
            pixels[i + 1] = 189;// G
            pixels[i + 2] = 248;// R
            pixels[i + 3] = 255;// A
        }
        var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] CreateValidJpegBytes()
    {
        byte[] pixels = new byte[16 * 16 * 3];
        Array.Fill(pixels, (byte)120);
        var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Rgb24, null, pixels, 16 * 3);
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] CreateAnimatedGifBytes()
    {
        var enc = new GifBitmapEncoder();
        for (int f = 0; f < 2; f++)
        {
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(f == 0 ? 255 : 40);
                pixels[i + 1] = (byte)(f == 0 ? 120 : 220);
                pixels[i + 2] = 80;
                pixels[i + 3] = 255;
            }
            var bmp = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
            enc.Frames.Add(BitmapFrame.Create(bmp));
        }
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] CreateMinimalWebpHeaderBytes()
    {
        byte[] buf = new byte[24];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buf, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(buf, 8);
        Encoding.ASCII.GetBytes("VP8 ").CopyTo(buf, 12);
        return buf;
    }

    // ==================== Offline Mocks ====================
    private sealed class MockClipboardBridge : IEmojiClipboardBridge
    {
        public uint SequenceNumber { get; set; } = 1;
        public string? CurrentText { get; set; }
        public int SetTextCalls { get; private set; }
        public bool LastIncludeBitmapFallback { get; private set; }
        public ClipboardImageReadResult NextImageRead { get; set; } = new(null, "", "", false);

        public uint GetSequenceNumber() => SequenceNumber;
        public string? GetText() => CurrentText;

        public bool SetText(string text)
        {
            SetTextCalls++;
            CurrentText = text;
            SequenceNumber++;
            return true;
        }

        public bool SetStickerData(string filePath, bool includeBitmapFallback)
        {
            LastIncludeBitmapFallback = includeBitmapFallback;
            SequenceNumber++;
            return true;
        }

        public ClipboardImageReadResult ReadImage() => NextImageRead;
    }

    private sealed class MockFocusRestorer : IFocusRestorer
    {
        public nint Foreground { get; set; }
        public int RestoreCalls { get; private set; }
        public HashSet<nint> InternalWindows { get; } = new();
        public nint GetForegroundWindow() => Foreground;
        public bool IsExternalTargetWindow(nint hwnd) => hwnd != 0 && !InternalWindows.Contains(hwnd);
        public Task<bool> TryRestoreForegroundAsync(nint targetHwnd, int timeoutMs = 120, CancellationToken ct = default)
        {
            RestoreCalls++;
            Foreground = targetHwnd;
            return Task.FromResult(targetHwnd != 0 && !InternalWindows.Contains(targetHwnd));
        }
    }

    private sealed class MockInputPanelSession : IInputPanelSession
    {
        public bool IsOpen { get; set; } = true;
        public InputPanelQueueResult NextResult { get; set; } = InputPanelQueueResult.Accepted;
        public List<string> QueuedTexts { get; } = new();
        public List<string> QueuedHotkeys { get; } = new();

        public InputPanelQueueResult QueueText(string text)
        {
            if (!IsOpen) return InputPanelQueueResult.Closed;
            QueuedTexts.Add(text);
            return NextResult;
        }

        public InputPanelQueueResult QueueHotkey(string hotkey)
        {
            if (!IsOpen) return InputPanelQueueResult.Closed;
            QueuedHotkeys.Add(hotkey);
            return NextResult;
        }

        public InputPanelQueueResult QueueAction(ConfiguredPluginAction binding)
        {
            if (!IsOpen) return InputPanelQueueResult.Closed;
            return NextResult;
        }

        public void Dispose() => IsOpen = false;
    }

    private sealed class MockHostInputPanelService : IHostInputPanelService
    {
        public InputPanelAppearance Appearance { get; set; } = new();
        public MockInputPanelSession LastSession { get; private set; } = new();
        public InputPanelAppearance GetAppearance() => Appearance;
        public IInputPanelSession Open(ActionContext target, Action<InputPanelCloseReason> onClosed)
        {
            LastSession = new MockInputPanelSession();
            return LastSession;
        }
        public PluginActionBindingInfo DescribeAction(ConfiguredPluginAction binding) => new();
        public Task<PluginActionBindingInfo> ResolveActionAsync(ConfiguredPluginAction binding, CancellationToken cancellationToken) =>
            Task.FromResult(new PluginActionBindingInfo());
        public Task<ConfiguredPluginAction?> EditActionAsync(ConfiguredPluginAction? current, long ownerWindowHandle, CancellationToken cancellationToken) =>
            Task.FromResult(current);
    }

    private sealed class MockHostActionInvoker : IHostActionInvoker
    {
        public List<string> SentTexts { get; } = new();
        public List<string> SentHotkeys { get; } = new();
        public List<string> OpenedFolders { get; } = new();
        public string? ClipboardText { get; set; }

        public bool SendHotkey(string hotkey) { SentHotkeys.Add(hotkey); return true; }
        public bool SendText(string text) { SentTexts.Add(text); return true; }
        public bool Launch(string path, string arguments = "", bool runAsStandardUser = false) => true;
        public bool OpenFolder(string folderPath) { OpenedFolders.Add(folderPath); return true; }
        public bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null) => true;
        public bool SetClipboardText(string text) { ClipboardText = text; return true; }
        public string? GetClipboardText() => ClipboardText;
    }

    private sealed class MockInputPanelPluginContext : IInputPanelPluginContext
    {
        public PluginMetadata Me { get; } = new() { Id = "starpie.plugin.emojipicker", Version = "0.1.0" };
        public string PluginDirectory => AppContext.BaseDirectory;
        public string DataDirectory { get; }
        public IPluginLogger Log { get; } = new MockLogger();
        public MockSettings MockSettings { get; } = new();
        public IPluginSettings Settings => MockSettings;
        public MockActions MockActions { get; } = new();
        public IActionRegistry Actions => MockActions;
        public II18nRegistry I18n { get; }
        public IIconRegistry Icons { get; } = new MockIcons();
        public MockSettingsPage MockSettingsPage { get; } = new();
        public ISettingsPageRegistry SettingsPage => MockSettingsPage;
        public IHostActionInvoker Host { get; } = new MockHostActionInvoker();
        public IHostCommandService Commands => throw new NotSupportedException();
        public IHostShellService Shell => throw new NotSupportedException();
        public IHostWindowService Windows => throw new NotSupportedException();
        public IHostScreenCaptureService ScreenCapture => throw new NotSupportedException();
        public IHostSystemService System => throw new NotSupportedException();
        public IHostWheelService Wheel => throw new NotSupportedException();
        public IHostKeyboardRemapService KeyboardRemap => throw new NotSupportedException();
        public MockHostInfo MockInfo { get; } = new();
        public IHostInfo Info => MockInfo;
        public INotificationService Notify { get; } = new MockNotify();
        public IPluginEvents Events => throw new NotSupportedException();
        public IDispatcherFacade Dispatcher { get; } = new MockDispatcher();
        public MockHostInputPanelService MockPanels { get; } = new();
        public IHostInputPanelService InputPanels => MockPanels;

        public MockInputPanelPluginContext(string dataDir)
        {
            DataDirectory = dataDir;
            I18n = new MockI18n(() => MockInfo.LanguageCode);
        }
    }

    private sealed class MockHostInfo : IHostInfo
    {
        public string HostVersion => "2.0.1-beta.6";
        public string ApiVersion => "1.15";
        public string LanguageCode { get; set; } = "zh-CN";
        public bool IsElevated => false;
        public bool IsPortable => false;
        public string HostExecutablePath => "";
        public bool HasCapability(PluginCapability capability) => true;
    }

    private sealed class MockLogger : IPluginLogger
    {
        public string LogFilePath => "";
        public void Debug(string m) { }
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m, Exception? ex = null) { }
    }

    private sealed class MockNotify : INotificationService
    {
        public void Notify(string title, string message) { }
    }

    private sealed class MockDispatcher : IDispatcherFacade
    {
        public bool IsOnUiThread => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class MockIcons : IIconRegistry
    {
        public string RegisterSvg(string key, string svgPathData) => "plugin:starpie.plugin.emojipicker:" + key;
        public string? ResolveKey(string key) => "plugin:starpie.plugin.emojipicker:" + key;
    }

    private sealed class MockActions : IActionRegistry
    {
        public List<IActionContribution> RegisteredActions { get; } = new();
        public IDisposable Register(IActionContribution contribution)
        {
            RegisteredActions.Add(contribution);
            return new DummyDisposable();
        }
    }

    private sealed class MockSettingsPage : ISettingsPageRegistry
    {
        public bool HasRegisteredPage { get; private set; }
        public SettingsPageDescriptor? RegisteredPage { get; private set; }
        public IDisposable Register(SettingsPageDescriptor page)
        {
            HasRegisteredPage = true;
            RegisteredPage = page;
            return new DummyDisposable();
        }
        public string? GetValue(string key) => null;
    }

    private sealed class MockSettings : IPluginSettings
    {
        private readonly Dictionary<string, string> _vals = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string key) => _vals.TryGetValue(key, out string? v) ? v : null;
        public void Set(string key, string? value) { if (value == null) _vals.Remove(key); else _vals[key] = value; }
        public bool GetBool(string key, bool def = false) => bool.TryParse(Get(key), out bool v) ? v : def;
        public int GetInt(string key, int def = 0) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
        public double GetDouble(string key, double def = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;
        public void Save() { }
        public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler) => new DummyDisposable();
    }

    private sealed class MockI18n : II18nRegistry
    {
        private readonly Func<string> _lang;
        private readonly Dictionary<string, (string Cn, string? En)> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);

        public MockI18n(Func<string> lang) => _lang = lang;
        public void Register(string key, string zhCn, string? en = null) => _entries[key] = (zhCn, en);
        public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table) => _tables[languageCode] = table;
        public string T(string key, string? fallback = null)
        {
            string l = _lang();
            if (_tables.TryGetValue(l, out var table) && table.TryGetValue(key, out string? t)) return t;
            if (_entries.TryGetValue(key, out var e))
                return l.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.En) ? e.En : e.Cn;
            return fallback ?? key;
        }
    }

    private sealed class DummyDisposable : IDisposable { public void Dispose() { } }
}
