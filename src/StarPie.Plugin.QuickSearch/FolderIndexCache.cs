using System.Collections.Immutable;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StarPie.Plugin.QuickSearch;

internal static class FolderIndexCache
{
    private const string FileName = "folder-index-v1.bin";
    private const int FormatVersion = 1;

    public static ImmutableSortedSet<FolderEntry>? Read(string directory, string[] roots,
        HashSet<string> online, CancellationToken token,
        Action<ImmutableSortedSet<FolderEntry>>? publish = null)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(directory, FileName), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != FormatVersion || reader.ReadInt32() != roots.Length) return null;
            foreach (string root in roots)
                if (!string.Equals(reader.ReadString(), root, StringComparison.OrdinalIgnoreCase)) return null;
            var entries = ImmutableSortedSet.CreateBuilder<FolderEntry>(FolderPathComparer.Instance);
            var accessible = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            string? priorDirectory = null;
            string priorPrefix = "";
            bool priorAllowed = false;
            int publishedCount = 0;
            long lastPublish = Stopwatch.GetTimestamp();
            int count = reader.ReadInt32();
            if (count < 0 || count > stream.Length - stream.Position) return null;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                string path = reader.ReadString();
                bool isDirectory = reader.ReadBoolean();
                if (!Path.IsPathFullyQualified(path) ||
                    !string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return null;
                if (FolderIndexSession.AtOrBelow(path, directory) || !online.Any(root => FolderIndexSession.AtOrBelow(path, root))) continue;
                var parent = isDirectory ? path.AsSpan() : Path.GetDirectoryName(path.AsSpan());
                if (!parent.Equals(priorDirectory.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    priorDirectory = parent.ToString();
                    priorAllowed = !parent.IsEmpty && CanList(priorDirectory, online, accessible, token);
                }
                if (!priorAllowed) continue;
                int split = path.LastIndexOf(Path.DirectorySeparatorChar) + 1;
                if (!path.AsSpan(0, split).SequenceEqual(priorPrefix))
                {
                    string prefix = path[..split];
                    if (!prefixes.TryGetValue(prefix, out var shared)) prefixes.Add(prefix, shared = prefix);
                    priorPrefix = shared;
                }
                entries.Add(new FolderEntry(priorPrefix, path[split..], isDirectory));
                if (publish != null && entries.Count > publishedCount && (entries.Count & 63) == 0 &&
                    (publishedCount == 0 || Stopwatch.GetElapsedTime(lastPublish) >= TimeSpan.FromMilliseconds(120)))
                {
                    publish(entries.ToImmutable());
                    publishedCount = entries.Count;
                    lastPublish = Stopwatch.GetTimestamp();
                }
            }
            return stream.Position == stream.Length ? entries.ToImmutable() : null;
        }
        catch (Exception ex) when (FolderIndexSession.IsFileError(ex) || ex is ArgumentException or FormatException or NotSupportedException)
        {
            return null;
        }
    }

    // Validate each containing directory once before offering a persisted name.
    // A formerly ordinary tree may now be inaccessible or point outside the selected root.
    private static bool CanList(string directory, HashSet<string> roots, Dictionary<string, bool> accessible, CancellationToken token)
    {
        if (accessible.TryGetValue(directory, out bool allowed)) return allowed;
        var chain = new Stack<string>();
        string? current = directory;
        while (current != null && !accessible.TryGetValue(current, out allowed))
        {
            token.ThrowIfCancellationRequested();
            chain.Push(current);
            if (roots.Contains(current)) { allowed = true; break; }
            current = Path.GetDirectoryName(current);
        }
        while (chain.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (allowed)
            {
                try
                {
                    var attributes = File.GetAttributes(item);
                    allowed = (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == FileAttributes.Directory;
                    if (allowed)
                    {
                        using var iterator = Directory.EnumerateFileSystemEntries(item).GetEnumerator();
                        iterator.MoveNext(); // Opens the directory with list access, including for empty folders.
                    }
                }
                catch (Exception ex) when (FolderIndexSession.IsFileError(ex)) { allowed = false; }
            }
            accessible[item] = allowed;
        }
        return allowed;
    }

    // A restored snapshot can be queried while reconciliation runs. Reuse a
    // single UTF-16 buffer per query, including for matches skipped by paging.
    internal static bool StillPresent(FolderEntry entry, ref char[]? buffer)
    {
        string prefix = entry.Prefix;
        bool extended = prefix.StartsWith(@"\\?\", StringComparison.Ordinal) || prefix.StartsWith(@"\\.\", StringComparison.Ordinal);
        bool unc = !extended && prefix.StartsWith(@"\\", StringComparison.Ordinal);
        string header = extended ? "" : unc ? @"\\?\UNC\" : @"\\?\";
        var rest = prefix.AsSpan(unc ? 2 : 0);
        int length = checked(header.Length + rest.Length + entry.Name.Length);
        if (buffer == null || buffer.Length <= length) buffer = new char[Math.Max(512, length + 1)];
        header.AsSpan().CopyTo(buffer);
        rest.CopyTo(buffer.AsSpan(header.Length));
        entry.Name.AsSpan().CopyTo(buffer.AsSpan(header.Length + rest.Length));
        buffer[length] = '\0';
        var attributes = GetFileAttributesW(buffer);
        return attributes != uint.MaxValue && (attributes & (uint)FileAttributes.ReparsePoint) == 0 &&
            ((attributes & (uint)FileAttributes.Directory) != 0) == entry.IsDirectory;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFileAttributesW([In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] path);

    public static void Write(string directory, string[] roots, ImmutableSortedSet<FolderEntry>.Builder index,
        CancellationToken token, Action<string, string> commit)
    {
        string temporary = Path.Combine(directory, $"folder-index-{Guid.NewGuid():N}.tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(FormatVersion);
                writer.Write(roots.Length);
                foreach (var root in roots) writer.Write(root);
                writer.Write(index.Count);
                string? previous = null;
                byte[] prefix = [];
                foreach (var entry in index)
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(previous, entry.Prefix))
                    {
                        prefix = Encoding.UTF8.GetBytes(entry.Prefix);
                        previous = entry.Prefix;
                    }
                    WritePath(writer, prefix, entry.Name);
                    writer.Write(entry.IsDirectory);
                }
            }
            commit(temporary, Path.Combine(directory, FileName));
        }
        catch (Exception ex) when (FolderIndexSession.IsFileError(ex)) { }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (FolderIndexSession.IsFileError(ex)) { }
        }
    }

    // Keep the v1 BinaryWriter string layout byte-for-byte while writing shared
    // directory and basename bytes separately, including long Unicode paths.
    private static void WritePath(BinaryWriter writer, byte[] prefix, string name)
    {
        int size = Encoding.UTF8.GetByteCount(name);
        byte[]? rented = null;
        Span<byte> bytes = size <= 512 ? stackalloc byte[512] : (rented = ArrayPool<byte>.Shared.Rent(size));
        try
        {
            Encoding.UTF8.GetBytes(name, bytes);
            writer.Write7BitEncodedInt(checked(prefix.Length + size));
            writer.Write(prefix);
            writer.Write(bytes[..size]);
        }
        finally { if (rented != null) ArrayPool<byte>.Shared.Return(rented); }
    }

    public static void Remove(string directory)
    {
        try { File.Delete(Path.Combine(directory, FileName)); }
        catch (DirectoryNotFoundException) { }
    }
}
