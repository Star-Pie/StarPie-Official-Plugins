using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using StarPie.Plugin;
using StarPie.Plugin.Sound;

public class ContextProxy : DispatchProxy
{
    public Dictionary<string, object?> Values { get; } = new();
    public Func<MethodInfo, object?[]?, object?>? Call;
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method!.Name.StartsWith("get_") && Values.TryGetValue(method.Name[4..], out var value)) return value;
        if (Call != null) return Call(method, arguments);
        throw new NotSupportedException(method.Name);
    }
}
internal sealed class Settings : IPluginSettings
{
    private readonly Dictionary<string, string> _values = new();
    private Dictionary<string, string> _saved = new();
    private readonly Dictionary<string, List<Action<PluginSettingChanged>>> _handlers = new();
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string? value) { if (value == null) _values.Remove(key); else _values[key] = value; }
    public bool GetBool(string key, bool defaultValue = false) => bool.TryParse(Get(key), out var v) ? v : defaultValue;
    public int GetInt(string key, int defaultValue = 0) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    public double GetDouble(string key, double defaultValue = 0) => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    public void Save()
    {
        foreach (var entry in _values.ToArray())
            if (_saved.GetValueOrDefault(entry.Key) != entry.Value && _handlers.TryGetValue(entry.Key, out var handlers))
                foreach (var action in handlers.ToArray()) action(new(entry.Key, _saved.GetValueOrDefault(entry.Key), entry.Value));
        _saved = new(_values);
    }
    public IDisposable OnChanged(string key, Action<PluginSettingChanged> handler)
    { if (!_handlers.TryGetValue(key, out var list)) _handlers[key] = list = new(); list.Add(handler); return new Token(() => list.Remove(handler)); }
    internal int SubscriptionCount => _handlers.Values.Sum(v => v.Count);
}
internal sealed class Token(Action dispose) : IDisposable { private Action? _dispose = dispose; public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
internal sealed class Registry : IActionRegistry, IInteractionRegistry, ISettingsPageRegistry, II18nRegistry, IPluginLogger
{
    internal readonly List<IActionContribution> Actions = new();
    internal readonly List<IInteractionContribution> Interactions = new();
    internal SettingsPageDescriptor? Page;
    internal readonly Dictionary<string, IReadOnlyDictionary<string, string>> Languages = new();
    internal readonly ConcurrentQueue<string> Warnings = new();
    public IDisposable Register(IActionContribution contribution) { Actions.Add(contribution); return new Token(() => Actions.Remove(contribution)); }
    public IDisposable Register(IInteractionContribution contribution) { Interactions.Add(contribution); return new Token(() => Interactions.Remove(contribution)); }
    public IDisposable Register(SettingsPageDescriptor page) { Page = page; return new Token(() => Page = null); }
    public string? GetValue(string key) => Page?.Fields.FirstOrDefault(f => f.Key == key)?.DefaultValue;
    public void Register(string key, string zhCn, string? en = null) { }
    public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table) => Languages[languageCode] = table;
    public string T(string key, string? fallback = null) => Languages.GetValueOrDefault("en")?.GetValueOrDefault(key) ?? fallback ?? key;
    public void Debug(string message) { } public void Info(string message) { } public void Warn(string message) => Warnings.Enqueue(message);
    public void Error(string message, Exception? exception = null) => Warnings.Enqueue(message);
    public string LogFilePath => "";
}
internal static class Program
{
    private static int _passed, _failed;
    private static long _tick;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "StarPie-SoundModule-" + Guid.NewGuid().ToString("N"));
    private static void Check(bool value, string name) { Console.WriteLine($"[{(value ? "PASS" : "FAIL")}] {name}"); if (value) _passed++; else _failed++; }
    private static void Throws(Action action, string name) { try { action(); Check(false, name); } catch (Exception ex) when (ex is IOException or InvalidDataException or PluginCapabilityDeniedException) { Check(true, name); } }
    private static void Reset()
    {
        SoundEffectManager.Shutdown();
        foreach (Thread worker in SoundEffectManager.GetHarnessCreatedWorkers()) if (worker.IsAlive && !worker.Join(3000)) throw new TimeoutException("Worker leaked.");
        SoundEffectManager.ResetTestSeams(); SoundEffectManager.TestMode = true;
        SoundEffectManager.Preferences = new(); SoundEffectManager.PlaybackSink = (_, _) => { };
        SoundEffectManager.TimeProvider = () => Interlocked.Read(ref _tick);
    }
    private static string Case(string name) { string path = Path.Combine(Root, name); Directory.CreateDirectory(path); return path; }
    private static IInteractionPluginContext Context(Settings settings, Registry registry, string data, bool files = true)
    {
        var context = DispatchProxy.Create<IInteractionPluginContext, ContextProxy>();
        var proxy = (ContextProxy)(object)context;
        var info = DispatchProxy.Create<IHostInfo, ContextProxy>();
        ((ContextProxy)(object)info).Call = (method, _) => method.Name == "HasCapability" ? files : throw new NotSupportedException();
        foreach (var pair in new Dictionary<string, object?> { ["Settings"] = settings, ["Log"] = registry, ["I18n"] = registry, ["Actions"] = registry,
            ["Interactions"] = registry, ["SettingsPage"] = registry, ["DataDirectory"] = data, ["PluginDirectory"] = data,
            ["Info"] = info, ["Me"] = new PluginMetadata { Id = "starpie.plugin.sound", Name = "Sound" } }) proxy.Values[pair.Key] = pair.Value;
        return context;
    }
    private static void TestMigration()
    {
        string folder = Case("migration"); string source = Path.Combine(folder, "legacy.json");
        File.WriteAllText(source, "{\"EnableSoundEffects\":false,\"SoundVolume\":0.25,\"SoundOnHover\":false,\"SoundTheme\":\"Custom\",\"ActiveCustomSoundProfileId\":\"user\",\"CustomSoundProfiles\":[{\"Id\":\"user\",\"Name\":\"用户\",\"ExtraProfile\":7,\"Events\":[{\"EventType\":1,\"SourceType\":3,\"ExtraEvent\":true}]}],\"Unrelated\":\"keep\"}");
        byte[] original = File.ReadAllBytes(source);
        var settings = new Settings(); var store = new SoundStore(settings, Path.Combine(folder, "data"), source);
        var preferences = store.ReadPreferences();
        Check(!preferences.EnableSoundEffects && !preferences.SoundOnHover && preferences.SoundVolume == 0.25, "legacy explicit false and volume survive");
        Check(preferences.ActiveCustomSoundProfileId == "user" && preferences.CustomSoundProfiles.Single().Events.Single().SourceType == SoundSourceType.Mute, "legacy enum/profile identity survives");
        Check(preferences.CustomSoundProfiles.Single().Extras!.ContainsKey("ExtraProfile") && preferences.CustomSoundProfiles.Single().Events.Single().Extras!.ContainsKey("ExtraEvent"), "profile/event extension data survives migration");
        Check(original.SequenceEqual(File.ReadAllBytes(source)), "migration never writes host configuration");
        string dataPath = Path.Combine(folder, "data", "sound.json"); byte[] migrated = File.ReadAllBytes(dataPath);
        File.WriteAllText(source, "{}"); _ = new SoundStore(settings, Path.Combine(folder, "data"), source);
        Check(migrated.SequenceEqual(File.ReadAllBytes(dataPath)), "migration is idempotent and does not re-import modified legacy data");
        settings.Set("enabled", "true"); settings.Set("volume", "0.7"); settings.Set("onHover", "false");
        Check(store.ReadPreferences().EnableSoundEffects && store.ReadPreferences().SoundVolume == 0.7 && !store.ReadPreferences().SoundOnHover, "plugin settings override migrated defaults without losing false");
        string broken = Path.Combine(folder, "broken.json"); File.WriteAllText(broken, "{ broken");
        try { _ = new SoundStore(new Settings(), Path.Combine(folder, "broken-data"), broken); Check(false, "invalid legacy JSON fails safely"); } catch (JsonException) { Check(true, "invalid legacy JSON fails safely"); }
        Check(!File.Exists(Path.Combine(folder, "broken-data", "sound.json")) && File.ReadAllText(broken) == "{ broken", "failed parse leaves both source and target unchanged");
        string blocked = Path.Combine(folder, "blocked"); File.WriteAllText(blocked, "not a directory"); File.WriteAllBytes(source, original);
        Throws(() => _ = new SoundStore(new Settings(), blocked, source), "failed private write is surfaced");
        Check(original.SequenceEqual(File.ReadAllBytes(source)), "failed private write leaves legacy bytes unchanged");
    }
    private static void TestProfiles()
    {
        string folder = Case("profiles"); var store = new SoundStore(new Settings(), Path.Combine(folder, "data"), Path.Combine(folder, "missing.json"));
        string invalid = Path.Combine(folder, "recursive.json"); File.WriteAllText(invalid, "{\"Id\":\"loop\",\"Events\":[{\"EventType\":1,\"SourceType\":2,\"BuiltInTheme\":\"Custom\"}]}");
        Throws(() => store.Import(invalid, true, false, default), "self-referencing imported preset is rejected before synthesis");
        Check(!File.Exists(Path.Combine(folder, "data", "sound.json")), "rejected import does not write preferences");
        byte[] safe = SoundEffectManager.SynthesizeCustomEventSound(new() { SourceType = SoundSourceType.BuiltInPreset, BuiltInTheme = "Custom" }, 0.6);
        Check(safe.Length > 44, "synthesis last-line guard prevents Custom recursion");
        string exported = Path.Combine(folder, "profile.json"); store.Export(exported, "", false, false, default);
        string importedId = store.Import(exported, true, false, default);
        Check(store.ReadPreferences().ActiveCustomSoundProfileId == importedId && store.ReadPreferences().CustomSoundProfiles.Count == 5, "duplicate profile defaults to safe copy and activation");
        Throws(() => store.Export(exported, null, false, false, default), "export cannot overwrite without explicit request");
        store.Export(exported, null, true, true, default);
        using var bundle = JsonDocument.Parse(File.ReadAllText(exported));
        Check(bundle.RootElement.TryGetProperty("CustomSoundProfiles", out var profiles) && profiles.GetArrayLength() == 5, "bundle export preserves all profiles");
        string source = Path.Combine(folder, "host.json"); File.WriteAllText(source, "{}"); var protectedStore = new SoundStore(new Settings(), Path.Combine(folder, "safe-data"), source);
        Throws(() => protectedStore.Export(source, null, true, true, default), "export cannot replace host configuration even with overwrite enabled");
        string duplicateEvent = Path.Combine(folder, "duplicate.json"); File.WriteAllText(duplicateEvent, "{\"Id\":\"bad\",\"Events\":[{\"EventType\":1},{\"EventType\":1}]}");
        Throws(() => store.Import(duplicateEvent, true, true, default), "duplicate event configuration is rejected");
        string before = File.ReadAllText(Path.Combine(folder, "data", "sound.json"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { store.Import(exported, true, true, canceled.Token); Check(false, "canceled import fails"); } catch (OperationCanceledException) { Check(true, "canceled import fails"); }
        Check(before == File.ReadAllText(Path.Combine(folder, "data", "sound.json")), "canceled import cannot replace active document");
    }
    private static void TestImportCommitSafety()
    {
        string folder = Case("import-commit-safety"), data = Path.Combine(folder, "data");
        var settings = new Settings(); var store = new SoundStore(settings, data, Path.Combine(folder, "missing.json"));
        string seed = Path.Combine(folder, "seed.json"); File.WriteAllText(seed, "{\"Id\":\"seed\",\"Events\":[{\"EventType\":1}]}");
        store.Import(seed, true, false, default);
        string document = Path.Combine(data, "sound.json"); byte[] before = File.ReadAllBytes(document);
        var preferencesBefore = store.ReadPreferences(); string? selectedBefore = settings.Get("profileId");
        string duplicate = Path.Combine(folder, "duplicate-ids.json");
        File.WriteAllText(duplicate, "[{\"Id\":\"same\",\"Name\":\"first\",\"Events\":[{\"EventType\":1}]},{\"Id\":\"SAME\",\"Name\":\"second\",\"Events\":[{\"EventType\":1}]}]");
        Throws(() => store.Import(duplicate, true, true, default), "same-batch duplicate profile IDs rejected even with explicit replace");
        Check(before.SequenceEqual(File.ReadAllBytes(document)) && settings.Get("profileId") == selectedBefore && store.ReadPreferences().CustomSoundProfiles.Count == preferencesBefore.CustomSoundProfiles.Count, "duplicate-ID rejection leaves file, preferences and active setting unchanged");

        string incoming = Path.Combine(folder, "cancelled.json"); File.WriteAllText(incoming, "{\"Id\":\"cancelled\",\"Events\":[{\"EventType\":1}]}");
        before = File.ReadAllBytes(document); preferencesBefore = store.ReadPreferences(); selectedBefore = settings.Get("profileId");
        object gate = typeof(SoundStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        using var cancellation = new CancellationTokenSource(); using var started = new ManualResetEventSlim();
        Exception? failure = null; string? result = null;
        var importer = new Thread(() => { started.Set(); try { result = store.Import(incoming, true, true, cancellation.Token); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        Monitor.Enter(gate);
        try
        {
            importer.Start();
            Check(started.Wait(3000) && SpinWait.SpinUntil(() => (importer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, 3000), "importer is blocked waiting for actual commit lock");
            cancellation.Cancel();
        }
        finally { Monitor.Exit(gate); }
        Check(importer.Join(3000), "cancelled importer completes after commit lock release");
        Check(failure is OperationCanceledException && result == null, "cancellation during commit-lock wait rejects import");
        var after = store.ReadPreferences();
        Check(before.SequenceEqual(File.ReadAllBytes(document)) && after.ActiveCustomSoundProfileId == preferencesBefore.ActiveCustomSoundProfileId && after.CustomSoundProfiles.Count == preferencesBefore.CustomSoundProfiles.Count && settings.Get("profileId") == selectedBefore, "commit-lock cancellation leaves disk, memory and selection unchanged");
    }
    private static void TestPrivateExtensions()
    {
        string folder = Case("private-extensions"), data = Path.Combine(folder, "data");
        Directory.CreateDirectory(data);
        string document = Path.Combine(data, "sound.json");
        File.WriteAllText(document, "{\"SchemaVersion\":1,\"VendorRoot\":{\"Version\":7},\"Preferences\":{\"VendorPreference\":true,\"CustomSoundProfiles\":[{\"Id\":\"old\",\"VendorProfile\":8,\"Events\":[{\"VendorEvent\":9}]}]}}");
        var store = new SoundStore(new Settings(), data, Path.Combine(folder, "missing.json"));
        string incoming = Path.Combine(folder, "import.json"); File.WriteAllText(incoming, "{\"Id\":\"new\",\"Events\":[{\"EventType\":1}]}");
        store.Import(incoming, false, false, default);
        using var saved = JsonDocument.Parse(File.ReadAllText(document));
        var preferences = saved.RootElement.GetProperty("Preferences");
        Check(saved.RootElement.TryGetProperty("VendorRoot", out var rootExtra) && rootExtra.GetProperty("Version").GetInt32() == 7, "actual import preserves private document root extensions");
        Check(preferences.TryGetProperty("VendorPreference", out var preferenceExtra) && preferenceExtra.GetBoolean(), "actual import preserves preferences extensions");
        var old = preferences.GetProperty("CustomSoundProfiles").EnumerateArray().Single(p => p.GetProperty("Id").GetString() == "old");
        Check(old.GetProperty("VendorProfile").GetInt32() == 8 && old.GetProperty("Events")[0].GetProperty("VendorEvent").GetInt32() == 9, "actual import preserves nested profile/event extensions");
    }
    private static async Task TestShutdownDrain()
    {
        Reset(); var settings = new Settings(); var registry = new Registry();
        var plugin = new SoundPlugin(); plugin.Initialize(Context(settings, registry, Case("shutdown-drain")));
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var shutdownEntered = new ManualResetEventSlim();
        Task? stopping = null; Thread? worker = null;
        SoundEffectManager.PlaybackSink = (_, _) => { entered.Set(); release.Wait(); };
        try
        {
            var preview = plugin.AuditionAsync("ActionExecute", default);
            Check(entered.Wait(3000), "shutdown drain holds actual worker in mock playback");
            worker = SoundEffectManager.CurrentWorkerThread;
            stopping = Task.Run(() => { shutdownEntered.Set(); plugin.Shutdown(); });
            Check(shutdownEntered.Wait(3000), "shutdown runs on background stop path");
            await preview.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAny(stopping, Task.Delay(100));
            Check(!stopping.IsCompleted && worker?.IsAlive == true, "Shutdown cannot return while actual backend worker is blocked");
            release.Set(); await stopping.WaitAsync(TimeSpan.FromSeconds(3));
            Check(worker?.IsAlive == false && registry.Actions.Count == 0 && registry.Interactions.Count == 0 && settings.SubscriptionCount == 0, "Shutdown drains physical worker and revokes ownership before return");
        }
        finally { release.Set(); if (stopping != null) await stopping.WaitAsync(TimeSpan.FromSeconds(6)); plugin.Shutdown(); Reset(); }
    }
    private static void TestSilentClaim(bool mute)
    {
        Reset(); Interlocked.Exchange(ref _tick, 1000);
        var preferences = new SoundPreferences { SoundTheme = "Custom", ActiveCustomSoundProfileId = "test", CustomSoundProfiles = new() { new() { Id = "test", Events = new() { new() { EventType = SoundType.SectorHover, SourceType = mute ? SoundSourceType.Mute : SoundSourceType.BuiltInPreset, BuiltInTheme = "Mechanical" } } } } };
        SoundEffectManager.Preferences = preferences; SoundEffectManager.Initialize(force: true);
        using var first = new ManualResetEventSlim(); using var again = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int selected = 0; int played = 0;
        SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () => { if (Interlocked.Increment(ref selected) == 1) { first.Set(); release.Wait(3000); } else again.Set(); };
        SoundEffectManager.PlaybackSink = (_, _) => Interlocked.Increment(ref played);
        long session = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
        try
        {
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 0); Interlocked.Exchange(ref _tick, 1045); SoundEffectManager.TestSignalWorker();
            Check(first.Wait(3000), $"ready {(mute ? "silent" : "tone")} candidate reaches real worker"); release.Set();
            Check(!again.Wait(180), $"{(mute ? "silent" : "tone")} candidate is consumed once, without busy reselection");
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 0); SoundEffectManager.TestSignalWorker();
            Check(!again.Wait(60), "repeated same target cannot reschedule consumed candidate");
            Check(mute ? played == 0 : played == 1, "silence never calls backend; tone calls once");
            if (mute)
            {
                Check(SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture).HasValue, "silent consumption retains target identity instead of recreating it");
                Check((long)typeof(SoundEffectManager).GetField("_lastHoverPlaybackStartTick", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! == 0, "silent consumption never advances actual hover playback time");
            }
        }
        finally { release.Set(); SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, session, false); Reset(); }
    }
    private static void TestSilentInterleaving()
    {
        Reset(); Interlocked.Exchange(ref _tick, 2000); SoundEffectManager.Preferences = new() { SoundTheme = "Custom", CustomSoundProfiles = new() { new() { Id = "silent", Events = new() { new() { EventType = SoundType.SectorHover, SourceType = SoundSourceType.Mute } } } }, ActiveCustomSoundProfileId = "silent" };
        SoundEffectManager.Initialize(force: true);
        using var selected = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int count = 0;
        SoundEffectManager.OnHoverCandidateSelectedBeforeAcquireResource = () => { if (Interlocked.Increment(ref count) == 1) { selected.Set(); release.Wait(3000); } };
        long session = SoundEffectManager.BeginSession(SoundSessionSource.NormalGesture);
        try
        {
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 0); Interlocked.Exchange(ref _tick, 2045); SoundEffectManager.TestSignalWorker(); Check(selected.Wait(3000), "old silent target paused before consume");
            SoundEffectManager.ReportHover(SoundSessionSource.NormalGesture, session, 0, -1, 1); release.Set();
            Check(SpinWait.SpinUntil(() => SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture)?.SubIndex == 1, 1000), "old consume cannot remove new target");
            Interlocked.Exchange(ref _tick, 2090); SoundEffectManager.TestSignalWorker();
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref count) == 2, 1000), "new silent candidate still gets its own consume");
        }
        finally { release.Set(); SoundEffectManager.EndSession(SoundSessionSource.NormalGesture, session, false); Reset(); }
    }
    private static async Task TestPreviewCompletion()
    {
        Reset(); SoundEffectManager.Initialize("Mechanical", 0.6, true);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        SoundEffectManager.PlaybackSink = (_, _) => { entered.Set(); release.Wait(3000); };
        Task<bool> preview = SoundEffectManager.PlayAuditionAsync(SoundType.ActionExecute, default);
        Check(entered.Wait(3000) && !preview.IsCompleted, "audition awaits actual backend completion instead of acceptance");
        release.Set(); Check(await preview.WaitAsync(TimeSpan.FromSeconds(3)), "audition completes after mock playback");
        Reset(); SoundEffectManager.Initialize("Mechanical", 0.6, true); SoundEffectManager.PlaybackSink = (_, _) => throw new IOException("simulated backend failure");
        Check(!await SoundEffectManager.PlayAuditionAsync(SoundType.ActionExecute, default).WaitAsync(TimeSpan.FromSeconds(3)), "mock backend failure is reported as failure"); Reset();
        Reset(); SoundEffectManager.Initialize("Mechanical", 0.6, true);
        using var block = new ManualResetEventSlim(); using var start = new ManualResetEventSlim();
        SoundEffectManager.PlaybackSink = (_, _) => { start.Set(); block.Wait(3000); };
        using var cancel = new CancellationTokenSource();
        var pending = SoundEffectManager.PlayAuditionAsync(SoundType.WheelPopup, cancel.Token);
        Check(start.Wait(3000) && !pending.IsCompleted, "cancel test holds real mock backend");
        cancel.Cancel();
        try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); Check(false, "audition cancellation observed"); }
        catch (OperationCanceledException) { Check(true, "audition cancellation observed"); }
        finally { block.Set(); Reset(); }
    }
    private static async Task TestPlugin()
    {
        Reset(); var settings = new Settings(); var registry = new Registry(); string folder = Case("plugin");
        var plugin = new SoundPlugin(); plugin.Initialize(Context(settings, registry, folder));
        Check(registry.Actions.Count == 3 && registry.Interactions.Count == 1 && registry.Page?.Fields.Count == 9, "SDK entry registers three actions, one observer and generic settings");
        Check(!SoundEffectManager.IsRunning, "Initialize does not start playback thread or emit audio");
        Check(settings.SubscriptionCount == 9, "settings subscriptions cover explicit keys only");
        var textKeys = registry.Page!.Fields.Select(f => f.LabelKey!).Concat(registry.Actions.Select(a => a.Descriptor.DisplayNameKey!));
        foreach (string language in new[] { "zh-CN", "zh-TW", "en", "ja" }) Check(textKeys.All(k => registry.Languages[language].ContainsKey(k)), "settings/action words localized: " + language);
        foreach (var action in registry.Actions) Check(action.Descriptor.Kind == ActionKind.Background && action.Validate(new Dictionary<string, string>()) != null, "action has background kind and rejects empty parameters: " + action.Descriptor.Id);
        var observer = registry.Interactions.Single();
        await observer.OnInteractionAsync(new(InteractionEventKind.Presented, 500, 1, InteractionSource.NormalGesture, "Global", InteractionTarget.Core), default);
        await observer.OnInteractionAsync(new(InteractionEventKind.SelectionChanged, 500, 2, InteractionSource.NormalGesture, "Global", new(InteractionTargetKind.SubSector, 2, 0)), default);
        Check(SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture)?.ParentIndex == 2, "SDK selection maps parent/subsector identity into audio candidate");
        await observer.OnInteractionAsync(new(InteractionEventKind.SessionEnded, 500, 3, InteractionSource.NormalGesture, "Global", InteractionTarget.None, "Disposed"), default);
        Check(plugin.Router.SessionCount == 0 && !SoundEffectManager.TestGetHoverCandidateTarget(SoundSessionSource.NormalGesture).HasValue, "ended semantic session drains routing and hover");
        settings.Set("enabled", "false"); settings.Save();
        await observer.OnInteractionAsync(new(InteractionEventKind.Presented, 501, 1, InteractionSource.StickyWheel, "Global", InteractionTarget.Core), default);
        Check(!SoundEffectManager.IsRunning && plugin.Router.SessionCount == 0, "disabled plugin preference does not allocate/play new session");
        plugin.Shutdown(); Check(registry.Actions.Count == 0 && registry.Interactions.Count == 0 && registry.Page == null && settings.SubscriptionCount == 0, "Shutdown revokes all registrations/subscriptions");
        Reset();
        var denied = new SoundPlugin(); Throws(() => denied.Initialize(Context(new Settings(), new Registry(), Case("denied"), files: false)), "missing file capability is denied before IO/registration"); denied.Shutdown(); Reset();
    }
    private static async Task<int> Main()
    {
        Directory.CreateDirectory(Root); Environment.SetEnvironmentVariable("LOCALAPPDATA", Root); SoundEffectManager.TestMode = true;
        Console.WriteLine("No GUI, native audio, input, processes or real plugin actions; test root: " + Root);
        try { TestMigration(); TestProfiles(); TestImportCommitSafety(); TestPrivateExtensions(); TestSilentClaim(false); TestSilentClaim(true); TestSilentInterleaving(); await TestPreviewCompletion(); await TestPlugin(); await TestShutdownDrain(); }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally { Reset(); }
        Console.WriteLine($"Sound module: {_passed} passed, {_failed} failed."); return _failed == 0 ? 0 : 1;
    }
}
