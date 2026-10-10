using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using StarPie.Plugin.DesktopPet;

namespace DesktopPetTests;

internal static class ExternalSettingsTests
{
    internal static int Run()
    {
        int failures = 0;
        foreach (bool usePrevious in new[] { true, false })
        foreach (bool drainBeforeCommit in new[] { true, false })
        foreach (bool cancel in new[] { false, true })
        {
            RunCase($"external {(usePrevious ? "A" : "C")}, drain {(drainBeforeCommit ? "before" : "after")}, cancel={cancel}", () =>
                TestExternalSwitch(usePrevious, drainBeforeCommit, cancel));
        }
        RunCase("synchronous save with reentrant external A", TestSynchronousReentrantSave);
        RunCase("reentrant external A then B during own B save", () => TestSynchronousReentrantSave(returnToTarget: true));
        RunCase("external A then B drained before old commit", TestExternalReturnToTarget);
        RunCase("own apply and compensation do not enqueue reload", TestOwnNotifications);
        RunCase("external cleared directory supersedes apply", TestClearedDirectory);
        return failures;

        void RunCase(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($" [PASS] Settings ownership: {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($" [FAIL] Settings ownership: {name}: {ex.Message}");
            }
        }
    }

    private static void TestExternalSwitch(bool usePrevious, bool drainBeforeCommit, bool cancel)
    {
        using var fixture = new Fixture();
        using var pump = new ContinuationPump();
        using var cancellation = new CancellationTokenSource();
        var external = fixture.Items[usePrevious ? 0 : 2];
        string externalDirectory = fixture.DirectoryFor(external);
        bool injected = false;
        int savesAfterExternal = 0;
        pump.BeforeContinuation = () =>
        {
            if (injected || fixture.Settings.Saved(PetPreference.ActiveLibraryIdKey) != fixture.Items[1].Id) return;
            injected = true;
            Require(fixture.Dispatcher.QueuedCount == 0, "own B save must not enqueue reload");
            fixture.SaveExternal(external);
            savesAfterExternal = fixture.Settings.SaveCount;
            Require(fixture.Dispatcher.QueuedCount > 0, "external save must enqueue production callback");
            if (drainBeforeCommit) fixture.Dispatcher.Drain();
            if (cancel) cancellation.Cancel();
        };
        var task = fixture.Controller.ApplyLibraryItemAsync(fixture.Items[1], cancellation.Token);
        pump.WaitFor(task);
        PetApplyResult? result = null;
        bool canceled = false;
        try { result = task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        Require(injected, "must inject after B persisted and before UI commit");
        Require(cancel ? canceled : result?.MessageKey == "controller.apply.superseded", "old apply must yield to external save");
        fixture.Dispatcher.Drain();
        Require(fixture.Settings.SaveCount == savesAfterExternal, "compensation must not overwrite external save");
        fixture.AssertSelection(external, externalDirectory);
    }

    private static void TestSynchronousReentrantSave() => TestSynchronousReentrantSave(returnToTarget: false);

    private static void TestSynchronousReentrantSave(bool returnToTarget)
    {
        using var fixture = new Fixture();
        bool injected = false;
        fixture.Settings.AfterPersisted = () =>
        {
            if (injected || fixture.Settings.Saved(PetPreference.ActiveLibraryIdKey) != fixture.Items[1].Id) return;
            injected = true;
            fixture.SaveExternal(fixture.Items[0]);
            if (returnToTarget) fixture.SaveExternal(fixture.Items[1]);
        };
        var result = fixture.Controller.ApplyLibraryItem(fixture.Items[1]);
        Require(injected, "nested external save must actually execute");
        Require(!result.IsSuccess && result.MessageKey == "controller.apply.superseded", "synchronous apply must not commit stale B");
        fixture.Dispatcher.Drain();
        var external = fixture.Items[returnToTarget ? 1 : 0];
        fixture.AssertSelection(external, fixture.DirectoryFor(external));
    }

    private static void TestExternalReturnToTarget()
    {
        using var fixture = new Fixture();
        using var pump = new ContinuationPump();
        bool injected = false;
        int savesAfterExternal = 0;
        pump.BeforeContinuation = () =>
        {
            if (injected || fixture.Settings.Saved(PetPreference.ActiveLibraryIdKey) != fixture.Items[1].Id) return;
            injected = true;
            fixture.SaveExternal(fixture.Items[0]);
            fixture.SaveExternal(fixture.Items[1]);
            savesAfterExternal = fixture.Settings.SaveCount;
            fixture.Dispatcher.Drain();
        };
        var task = fixture.Controller.ApplyLibraryItemAsync(fixture.Items[1]);
        pump.WaitFor(task);
        Require(injected && task.GetAwaiter().GetResult().MessageKey == "controller.apply.superseded", "old transaction must remain superseded after external return to B");
        fixture.Dispatcher.Drain();
        Require(fixture.Settings.SaveCount == savesAfterExternal, "no compensation over final external B");
        fixture.AssertSelection(fixture.Items[1], fixture.DirectoryFor(fixture.Items[1]));
    }

    private static void TestOwnNotifications()
    {
        using var fixture = new Fixture();
        using var pump = new ContinuationPump();
        using var cancellation = new CancellationTokenSource();
        bool persisted = false;
        pump.BeforeContinuation = () =>
        {
            if (persisted || fixture.Settings.Saved(PetPreference.ActiveLibraryIdKey) != fixture.Items[1].Id) return;
            persisted = true;
            cancellation.Cancel();
        };
        var task = fixture.Controller.ApplyLibraryItemAsync(fixture.Items[1], cancellation.Token);
        pump.WaitFor(task);
        bool canceled = false;
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        Require(persisted && canceled, "must cancel saved B and finish compensation");
        Require(fixture.Dispatcher.QueuedCount == 0, "own apply and compensation callbacks must remain suppressed");
        fixture.AssertSelection(fixture.Items[0], fixture.DirectoryFor(fixture.Items[0]));
        fixture.SaveExternal(fixture.Items[1]);
        fixture.Dispatcher.Drain();
        fixture.AssertSelection(fixture.Items[1], fixture.DirectoryFor(fixture.Items[1]));
    }

    private static void TestClearedDirectory()
    {
        using var fixture = new Fixture();
        using var pump = new ContinuationPump();
        bool injected = false;
        pump.BeforeContinuation = () =>
        {
            if (injected || fixture.Settings.Saved(PetPreference.ActiveLibraryIdKey) != fixture.Items[1].Id) return;
            injected = true;
            fixture.SaveExternal(fixture.Items[2]);
            fixture.Settings.Set(PetPreference.AssetDirectoryKey, null);
            fixture.Settings.Set(PetPreference.ActiveLibraryIdKey, null);
            fixture.Settings.Save();
        };
        var task = fixture.Controller.ApplyLibraryItemAsync(fixture.Items[1]);
        pump.WaitFor(task);
        var result = task.GetAwaiter().GetResult();
        Require(injected && result.MessageKey == "controller.apply.superseded", "clearing directory must stop stale apply");
        fixture.Dispatcher.Drain();
        Require(fixture.Settings.Saved(PetPreference.AssetDirectoryKey) == null, "clear must remain persisted");
        Require(fixture.Controller.Assets.CurrentDirectory == fixture.DirectoryFor(fixture.Items[0]), "keep prior valid image when external directory cleared");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "starpie-settings-test-" + Guid.NewGuid().ToString("N"));
        private readonly DesktopPetPlugin _plugin = new();
        internal SnapshotSettings Settings { get; } = new();
        internal QueueableFakeDispatcher Dispatcher { get; } = new();
        internal PetController Controller { get; }
        internal List<PetLibraryItem> Items { get; } = new();

        internal Fixture()
        {
            var context = new MockPluginContext(settings: Settings, dispatcher: Dispatcher, dataDirectory: Path.Combine(_root, "data"));
            _plugin.Initialize(context);
            Controller = (PetController)typeof(DesktopPetPlugin).GetField("_controller", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_plugin)!;
            Controller.IsHeadlessTestMode = true;
            for (int i = 0; i < 3; i++)
            {
                string source = Path.Combine(_root, "source-" + i);
                Directory.CreateDirectory(source);
                byte[] pixels = new byte[16 * 16 * 4];
                for (int p = 0; p < pixels.Length; p += 4) { pixels[p] = (byte)(30 + i * 70); pixels[p + 3] = 255; }
                var image = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.Combine(source, "idle.png"))) encoder.Save(stream);
                var import = Controller.LibraryManager.ImportFolderAsync(source).GetAwaiter().GetResult();
                Require(import.Item != null, "temporary fixture import must succeed");
                Items.Add(import.Item!);
            }
            Require(Controller.ApplyLibraryItem(Items[0]).IsSuccess, "initial A apply must succeed");
            Require(Dispatcher.QueuedCount == 0, "initial own save must not enqueue reload");
        }

        internal string DirectoryFor(PetLibraryItem item) => Path.Combine(Controller.LibraryManager.LibraryRoot, item.RelativeDirectory);
        internal void SaveExternal(PetLibraryItem item)
        {
            Settings.Set(PetPreference.ActiveLibraryIdKey, item.Id);
            Settings.Set(PetPreference.AssetDirectoryKey, DirectoryFor(item));
            Settings.Save();
        }
        internal void AssertSelection(PetLibraryItem item, string directory)
        {
            Require(Settings.Get(PetPreference.AssetDirectoryKey) == directory && Settings.Saved(PetPreference.AssetDirectoryKey) == directory,
                "in-memory and persisted directory must match external selection");
            Require(Settings.Get(PetPreference.ActiveLibraryIdKey) == item.Id && Settings.Saved(PetPreference.ActiveLibraryIdKey) == item.Id,
                "in-memory and persisted library ID must match external selection");
            Require(Controller.Assets.CurrentDirectory == directory, "loaded directory must match external selection");
            byte[] actual = new byte[4];
            var bitmap = Controller.Assets.IdleBitmap!;
            bitmap.CopyPixels(new System.Windows.Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), actual, 4, 0);
            Require(actual[0] == (byte)(30 + Items.IndexOf(item) * 70), "loaded bitmap pixels must match external selection");
        }
        public void Dispose()
        {
            _plugin.Shutdown();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ContinuationPump : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext? _previous = Current;
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        internal Action? BeforeContinuation { get; set; }
        internal ContinuationPump() => SetSynchronizationContext(this);
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        internal void WaitFor(Task task)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (!_queue.TryTake(out var continuation, 100)) continue;
                BeforeContinuation?.Invoke();
                continuation.Callback(continuation.State);
            }
            Require(task.IsCompleted, "async apply must complete without deadlock");
        }
        public void Dispose() { SetSynchronizationContext(_previous); _queue.Dispose(); }
    }

    // Mirrors the host's snapshot-before-notify and synchronous, reentrant Save contract.
    private sealed class SnapshotSettings : IPluginSettings
    {
        private readonly object _gate = new();
        private readonly object _saveGate = new();
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private Dictionary<string, string> _persisted = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<PluginSettingChanged>>> _handlers = new(StringComparer.Ordinal);
        internal Action? AfterPersisted { get; set; }
        internal int SaveCount { get; private set; }
        public string? Get(string key) { lock (_gate) return _values.GetValueOrDefault(key); }
        internal string? Saved(string key) { lock (_gate) return _persisted.GetValueOrDefault(key); }
        public void Set(string key, string? value)
        {
            lock (_gate) { if (value == null) _values.Remove(key); else _values[key] = value; }
        }
        public bool GetBool(string key, bool defaultValue = false) => bool.TryParse(Get(key), out var value) ? value : defaultValue;
        public int GetInt(string key, int defaultValue = 0) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
        public double GetDouble(string key, double defaultValue = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;
        public void Save()
        {
            lock (_saveGate)
            {
                List<PluginSettingChanged> changes;
                lock (_gate)
                {
                    changes = _persisted.Keys.Union(_values.Keys).Where(key => _persisted.GetValueOrDefault(key) != _values.GetValueOrDefault(key))
                        .Select(key => new PluginSettingChanged(key, _persisted.GetValueOrDefault(key), _values.GetValueOrDefault(key))).ToList();
                    _persisted = new Dictionary<string, string>(_values, StringComparer.Ordinal);
                    SaveCount++;
                }
                AfterPersisted?.Invoke();
                foreach (var change in changes)
                {
                    Action<PluginSettingChanged>[] handlers;
                    lock (_gate) handlers = _handlers.TryGetValue(change.Key, out var list) ? list.ToArray() : [];
                    foreach (var handler in handlers) handler(change);
                }
            }
        }
        public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler)
        {
            lock (_gate)
            {
                if (!_handlers.TryGetValue(key, out var list)) _handlers[key] = list = new();
                list.Add(handler);
            }
            return new Subscription(() => { lock (_gate) _handlers[key].Remove(handler); });
        }
        private sealed class Subscription(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
