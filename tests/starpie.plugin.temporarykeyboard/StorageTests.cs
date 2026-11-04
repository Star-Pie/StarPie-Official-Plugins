using System.Text;
using StarPie.Plugin.TemporaryKeyboard;

internal static class StorageTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("private file roundtrip exceeds old settings limit", LargeRoundTrip);
        run("legacy scheme migration preserves original settings", LegacyMigration);
        run("corrupt file requires explicit recovery and exact backup", CorruptRecovery);
        run("invalid UTF8 inside valid JSON requires lossless recovery", InvalidUtf8);
        run("backup and primary write failures preserve old data", WriteFailures);
        run("changed file rejects stale editor", ConcurrentEdit);
        run("oversized file cannot be implicitly replaced", Oversized);
        run("UTF8 BOM is accepted and atomic write leaves no temp", Bom);
    }

    private static string NewDirectory()
    {
        string root = Environment.GetEnvironmentVariable("KEYBOARD_TEST_ROOT")
            ?? throw new InvalidOperationException("Set KEYBOARD_TEST_ROOT to an isolated evidence directory.");
        string path = Path.Combine(Path.GetFullPath(root), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static string DataPath(string directory) => Path.Combine(directory, "keyboard-schemes.json");
    private static KeyboardSchemes Large()
    {
        var schemes = KeyboardSchemes.CreateDefault("Large");
        schemes.Items[0].Keys[0] = new CustomKeyBinding { Label = "A", ActionId = "missing.plugin.action",
            Parameters = new() { ["text"] = new string('x', 8192) } };
        schemes.Items.Add(new CustomKeyboardScheme { Id = "second", Name = "Second",
            Keys = Enumerable.Repeat<CustomKeyBinding?>(null, 12).ToList() });
        return schemes;
    }
    private static void LargeRoundTrip()
    {
        string directory = NewDirectory(); var storage = new KeyboardSchemeStorage(directory, () => null);
        var original = storage.Load("Default"); var expected = Large();
        string json = expected.Serialize(); Check(json.Length > 8192, "Boundary fixture must exceed old setting limit");
        Check(!KeyboardSchemes.TryDeserialize(json[..8192], out _, out _), "Old truncating path must fail this fixture");
        storage.Save(original, expected, false);
        var loaded = storage.Load("Default");
        Check(!loaded.RequiresRecovery && loaded.FromFile, "Saved private file should be valid");
        Check(loaded.Schemes.Serialize() == json, "All parameters and schemes must roundtrip");
    }
    private static void LegacyMigration()
    {
        string directory = NewDirectory(); string legacy = KeyboardSchemes.CreateDefault("Legacy").Serialize();
        var storage = new KeyboardSchemeStorage(directory, () => legacy); var doc = storage.Load("Default");
        Check(!doc.FromFile && !doc.RequiresRecovery && doc.Schemes.Items[0].Name == "Legacy", "Read legacy");
        storage.Save(doc, doc.Schemes, false);
        Check(storage.Load("Default").FromFile, "Explicit save should migrate to private file");
        Check(legacy == doc.Schemes.Serialize(), "Legacy setting remains untouched");
    }
    private static void CorruptRecovery()
    {
        string directory = NewDirectory(); byte[] bad = [0x7B, 0xFF, 0x00, 0x7D];
        File.WriteAllBytes(DataPath(directory), bad);
        var storage = new KeyboardSchemeStorage(directory, () => null); var doc = storage.Load("Fallback");
        Check(doc.RequiresRecovery, "Damage must be explicit");
        Throws(() => storage.Save(doc, doc.Schemes, false));
        Check(File.ReadAllBytes(DataPath(directory)).SequenceEqual(bad), "Ordinary save must not overwrite damage");
        storage.Save(doc, doc.Schemes, true);
        string backup = Directory.GetFiles(directory, "*.recovery-*.json").Single();
        Check(File.ReadAllBytes(backup).SequenceEqual(bad), "Backup must preserve exact bytes");
        Check(!storage.Load("Default").RequiresRecovery, "Explicit recovery saves valid replacement");
    }
    private static void WriteFailures()
    {
        foreach (bool failBackup in new[] { true, false })
        {
            string directory = NewDirectory(); byte[] old = Encoding.UTF8.GetBytes(failBackup ? "broken" : Large().Serialize());
            File.WriteAllBytes(DataPath(directory), old); int attempts = 0;
            var storage = new KeyboardSchemeStorage(directory, () => null, (_, _) => { attempts++; throw new IOException("Simulated disk failure"); });
            var doc = storage.Load("Default"); Throws(() => storage.Save(doc, doc.Schemes, true));
            Check(attempts == 1, "Failure must stop, never report success");
            Check(File.ReadAllBytes(DataPath(directory)).SequenceEqual(old), "Write failure must preserve old bytes");
        }
        string path = NewDirectory(); File.WriteAllText(DataPath(path), "broken");
        int writes = 0;
        var partial = new KeyboardSchemeStorage(path, () => null, (target, bytes) =>
        { if (++writes == 2) throw new IOException("Main write failed after backup"); File.WriteAllBytes(target, bytes); });
        var original = partial.Load("Default"); Throws(() => partial.Save(original, original.Schemes, true));
        Check(File.ReadAllText(DataPath(path)) == "broken", "Failed replacement retains original");
        Check(File.ReadAllText(Directory.GetFiles(path, "*.recovery-*.json").Single()) == "broken", "Backup retained after replacement failure");
    }
    private static void InvalidUtf8()
    {
        string directory = NewDirectory(); byte[] bad = Encoding.UTF8.GetBytes(KeyboardSchemes.CreateDefault("NAME").Serialize());
        int offset = Encoding.UTF8.GetString(bad).IndexOf("NAME", StringComparison.Ordinal);
        bad[offset] = 0xFF; File.WriteAllBytes(DataPath(directory), bad);
        // A permissive decoder would silently make this malformed source look valid.
        Check(KeyboardSchemes.TryDeserialize(Encoding.UTF8.GetString(bad), out _, out _), "Counterexample must bypass permissive decode");
        var storage = new KeyboardSchemeStorage(directory, () => null); var doc = storage.Load("Fallback");
        Check(doc.RequiresRecovery, "Strict UTF8 decoding must detect corruption");
        Throws(() => storage.Save(doc, doc.Schemes, false));
        storage.Save(doc, doc.Schemes, true);
        Check(File.ReadAllBytes(Directory.GetFiles(directory, "*.recovery-*.json").Single()).SequenceEqual(bad), "Retain exact invalid UTF8 bytes");
    }
    private static void ConcurrentEdit()
    {
        string directory = NewDirectory(); var storage = new KeyboardSchemeStorage(directory, () => null);
        var doc = storage.Load("Default"); File.WriteAllText(DataPath(directory), "changed externally");
        Throws(() => storage.Save(doc, doc.Schemes, false));
        Check(File.ReadAllText(DataPath(directory)) == "changed externally", "Stale editor must not replace newer file");
    }
    private static void Oversized()
    {
        string directory = NewDirectory(); byte[] old = new byte[KeyboardSchemes.MaxJsonBytes + 1];
        File.WriteAllBytes(DataPath(directory), old); var storage = new KeyboardSchemeStorage(directory, () => null);
        var doc = storage.Load("Default"); Check(doc.RequiresRecovery && doc.OriginalBytes == null, "Oversize is bounded");
        Throws(() => storage.Save(doc, doc.Schemes, true));
        Check(new FileInfo(DataPath(directory)).Length == old.Length, "Do not overwrite oversized source");
    }
    private static void Bom()
    {
        string directory = NewDirectory(); string json = KeyboardSchemes.CreateDefault("BOM").Serialize();
        File.WriteAllBytes(DataPath(directory), Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray());
        var storage = new KeyboardSchemeStorage(directory, () => null); var doc = storage.Load("Default");
        Check(!doc.RequiresRecovery && doc.Schemes.Items[0].Name == "BOM", "BOM load");
        storage.Save(doc, doc.Schemes, false);
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "No temporary remains");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    { try { action(); } catch (IOException) { return; } throw new InvalidOperationException("Expected IO failure"); }
}
