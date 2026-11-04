using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StarPie.Plugin.TemporaryKeyboard;

internal sealed record KeySchemeDocument(
    KeySchemes Schemes,
    string Signature,
    bool FromFile,
    bool RequiresRecovery,
    byte[]? OriginalBytes);

/// <summary>Bounded atomic storage for key-based keyboard schemes.</summary>
internal sealed class KeySchemeStorage
{
    private readonly string _directory;
    private readonly string _path;
    private readonly Action<string, byte[]> _write;

    internal KeySchemeStorage(string directory, Action<string, byte[]>? write = null)
    {
        _directory = Path.GetFullPath(directory);
        _path = Path.Combine(_directory, "keyboard-key-schemes.json");
        _write = write ?? AtomicWrite;
    }

    internal KeySchemeDocument Load(string defaultName)
    {
        RawDocument raw = ReadRaw();
        if (raw.Bytes == null)
        {
            return new(
                KeySchemes.CreateDefault(defaultName),
                raw.Signature,
                raw.FromFile,
                raw.Oversized,
                null);
        }

        int offset = raw.Bytes.Length >= 3 &&
            raw.Bytes[0] == 0xEF && raw.Bytes[1] == 0xBB && raw.Bytes[2] == 0xBF
            ? 3
            : 0;
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(
                raw.Bytes,
                offset,
                raw.Bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return new(
                KeySchemes.CreateDefault(defaultName),
                raw.Signature,
                raw.FromFile,
                true,
                raw.Bytes);
        }

        bool valid = KeySchemes.TryDeserialize(text, out KeySchemes? schemes, out _) &&
            schemes!.Items.Count > 0;
        return new(
            valid ? schemes! : KeySchemes.CreateDefault(defaultName),
            raw.Signature,
            raw.FromFile,
            !valid,
            raw.Bytes);
    }

    internal void Save(
        KeySchemeDocument original,
        KeySchemes schemes,
        bool allowRecovery)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(schemes.Serialize());
        RawDocument current = ReadRaw();
        if (current.Signature != original.Signature || current.FromFile != original.FromFile)
        {
            throw new IOException("Keyboard key schemes changed while the editor was open.");
        }

        if (original.RequiresRecovery)
        {
            if (!allowRecovery || original.OriginalBytes == null)
            {
                throw new IOException(
                    "Explicit recovery is required; oversized files must be recovered manually.");
            }

            string backup = Path.Combine(
                _directory,
                "keyboard-key-schemes.recovery-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N") + ".json");
            _write(backup, original.OriginalBytes);
        }

        _write(_path, bytes);
    }

    private RawDocument ReadRaw()
    {
        CheckLocalPath(_path);
        if (File.Exists(_path))
        {
            var info = new FileInfo(_path);
            if (info.Length > KeySchemes.MaxJsonBytes)
            {
                return new(
                    null,
                    "oversized:" + info.Length + ":" + info.LastWriteTimeUtc.Ticks,
                    true,
                    true);
            }

            byte[] bytes = File.ReadAllBytes(_path);
            if (bytes.Length > KeySchemes.MaxJsonBytes)
            {
                throw new IOException("Keyboard key scheme file grew beyond its limit while being read.");
            }

            return new(
                bytes,
                Convert.ToHexString(SHA256.HashData(bytes)),
                true,
                false);
        }

        return new(null, "missing", false, false);
    }

    private void CheckLocalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(
                _directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Invalid keyboard key scheme data path.");
        }

        for (string? current = fullPath;
             current != null;
             current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Redirected keyboard key scheme data paths are not supported.");
            }
        }
    }

    private void AtomicWrite(string path, byte[] bytes)
    {
        CheckLocalPath(path);
        Directory.CreateDirectory(_directory);
        string pending = Path.Combine(
            _directory,
            ".keyboard-key-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                pending,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            CheckLocalPath(path);
            File.Move(
                pending,
                path,
                overwrite: string.Equals(path, _path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private sealed record RawDocument(
        byte[]? Bytes,
        string Signature,
        bool FromFile,
        bool Oversized);
}
