using System.Security.Cryptography;
using System.Text;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed record KeyboardSchemeDocument(KeyboardSchemes Schemes, string Signature,
    bool FromFile, bool RequiresRecovery, byte[]? OriginalBytes);

/// <summary>Bounded, atomic private-file storage; failures propagate to the editor.</summary>
internal sealed class KeyboardSchemeStorage
{
    private readonly string _directory;
    private readonly string _path;
    private readonly Func<string?> _legacy;
    private readonly Action<string, byte[]> _write;

    internal KeyboardSchemeStorage(string directory, Func<string?> legacy,
        Action<string, byte[]>? write = null)
    {
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "keyboard-schemes.json");
        _legacy = legacy;
        _write = write ?? AtomicWrite;
    }

    internal KeyboardSchemeDocument Load(string defaultName)
    {
        var raw = ReadRaw();
        if (raw.Bytes == null)
            return new(KeyboardSchemes.CreateDefault(defaultName), raw.Signature, raw.FromFile, raw.Oversized, null);
        int offset = raw.Bytes.Length >= 3 && raw.Bytes[0] == 0xEF && raw.Bytes[1] == 0xBB && raw.Bytes[2] == 0xBF ? 3 : 0;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(raw.Bytes, offset, raw.Bytes.Length - offset); }
        catch (DecoderFallbackException)
        { return new(KeyboardSchemes.CreateDefault(defaultName), raw.Signature, raw.FromFile, true, raw.Bytes); }
        bool valid = KeyboardSchemes.TryDeserialize(text, out var schemes, out _) && schemes!.Items.Count > 0;
        return new(valid ? schemes! : KeyboardSchemes.CreateDefault(defaultName), raw.Signature,
            raw.FromFile, !valid, raw.Bytes);
    }

    internal void Save(KeyboardSchemeDocument original, KeyboardSchemes schemes, bool allowRecovery)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(schemes.Serialize());
        var current = ReadRaw();
        if (current.Signature != original.Signature || current.FromFile != original.FromFile)
            throw new IOException("Keyboard schemes changed while the editor was open.");
        if (original.RequiresRecovery)
        {
            if (!allowRecovery || original.OriginalBytes == null)
                throw new IOException("Explicit recovery is required; oversized files must be recovered manually.");
            string backup = Path.Combine(_directory, "keyboard-schemes.recovery-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
            _write(backup, original.OriginalBytes);
        }
        _write(_path, bytes);
    }

    private (byte[]? Bytes, string Signature, bool FromFile, bool Oversized) ReadRaw()
    {
        CheckLocalPath(_path);
        if (File.Exists(_path))
        {
            var info = new FileInfo(_path);
            if (info.Length > KeyboardSchemes.MaxJsonBytes)
                return (null, "oversized:" + info.Length + ":" + info.LastWriteTimeUtc.Ticks, true, true);
            byte[] bytes = File.ReadAllBytes(_path);
            if (bytes.Length > KeyboardSchemes.MaxJsonBytes)
                throw new IOException("Keyboard scheme file grew beyond its limit while being read.");
            return (bytes, Convert.ToHexString(SHA256.HashData(bytes)), true, false);
        }
        string? legacy = _legacy();
        if (string.IsNullOrEmpty(legacy)) return (null, "missing", false, false);
        byte[] old = Encoding.UTF8.GetBytes(legacy);
        return (old, Convert.ToHexString(SHA256.HashData(old)), false, false);
    }

    private void CheckLocalPath(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(_directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid keyboard data path.");
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected keyboard data paths are not supported.");
    }

    private void AtomicWrite(string path, byte[] bytes)
    {
        CheckLocalPath(path);
        Directory.CreateDirectory(_directory);
        string pending = Path.Combine(_directory, ".keyboard-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            CheckLocalPath(path);
            File.Move(pending, path, overwrite: string.Equals(path, _path, StringComparison.OrdinalIgnoreCase));
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
