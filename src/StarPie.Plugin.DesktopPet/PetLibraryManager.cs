using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 形象库管理引擎。
/// <para>
/// 负责本地 PNG 目录的白名单提取、安全尺寸与签名校验、画布一致性检查、整画布512派生、
/// 稳定ID与内容摘要（SHA256）生成、Staging原子入库与 library-index.json 持久化。
/// 所有位图均采用 Freeze 保证跨线程安全与零锁开销。
/// </para>
/// </summary>
public sealed class PetLibraryManager
{
    public const long MaxSingleFileSize = 10 * 1024 * 1024; // 10 MiB
    public const int MaxDimensionPixels = 4096;
    public const int StandardCanvasSize = 512;

    public static readonly string[] WhitelistedFrameNames = new[]
    {
        "idle.png",
        "blink.png",
        "react.png",
        "drag.png",
        "point_E.png",
        "point_SE.png",
        "point_S.png",
        "point_SW.png",
        "point_W.png",
        "point_NW.png",
        "point_N.png",
        "point_NE.png",
    };

    private readonly string _dataDirectory;
    private readonly object _indexLock = new();
    private PetLibraryIndex? _cachedIndex;
    private string? _indexLoadError;

    public PetLibraryManager(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
    }

    public string LibraryRoot => Path.Combine(_dataDirectory, "pet-library");
    public string IndexFilePath => Path.Combine(LibraryRoot, "library-index.json");

    public Task<PetLibraryEditResult> MoveItemAsync(string id, int offset, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        lock (_indexLock)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureIndexLoaded();
                int current = _cachedIndex!.Items.FindIndex(item => item.Id == id);
                if (current < 0) return new PetLibraryEditResult(false, "library.edit.missing");
                int target = current + Math.Sign(offset);
                if (offset == 0 || target < 0 || target >= _cachedIndex.Items.Count) return new PetLibraryEditResult(false, "library.edit.boundary");
                var items = new List<PetLibraryItem>(_cachedIndex.Items);
                (items[current], items[target]) = (items[target], items[current]);
                var candidate = new PetLibraryIndex { Version = 2, Items = items, RemovedItems = new(_cachedIndex.RemovedItems) };
                SaveIndexObjectLocked(candidate);
                _cachedIndex = candidate;
                return new PetLibraryEditResult(true, "library.edit.moved");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return new PetLibraryEditResult(false, "library.edit.failed", ex.Message); }
        }
    }, cancellationToken);

    public Task<PetLibraryEditResult> RemoveItemAsync(string id, Func<PetLibraryItem, bool> isProtected, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        lock (_indexLock)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureIndexLoaded();
                var item = _cachedIndex!.Items.FirstOrDefault(item => item.Id == id);
                if (item == null) return new PetLibraryEditResult(false, "library.edit.missing");
                if (!IsOwnedDirectory(item)) return new PetLibraryEditResult(false, "library.edit.invalid_path");
                if (isProtected(item)) return new PetLibraryEditResult(false, "library.edit.active");
                var items = _cachedIndex.Items.Where(candidate => candidate.Id != id).ToList();
                var removed = _cachedIndex.RemovedItems.Where(existing => existing.Id != id).ToList();
                removed.Add(item);
                cancellationToken.ThrowIfCancellationRequested();
                if (isProtected(item)) return new PetLibraryEditResult(false, "library.edit.active");
                var candidate = new PetLibraryIndex { Version = 2, Items = items, RemovedItems = removed };
                SaveIndexObjectLocked(candidate);
                _cachedIndex = candidate;
                return new PetLibraryEditResult(true, "library.edit.removed");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return new PetLibraryEditResult(false, "library.edit.failed", ex.Message); }
        }
    }, cancellationToken);

    private bool IsOwnedDirectory(PetLibraryItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.RelativeDirectory) || Path.IsPathRooted(item.RelativeDirectory) || item.RelativeDirectory.StartsWith('.')) return false;
            string root = Path.GetFullPath(LibraryRoot).TrimEnd(Path.DirectorySeparatorChar);
            string directory = Path.GetFullPath(Path.Combine(root, item.RelativeDirectory));
            if (!string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase)) return false;
            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            return !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// 读取形象库所有已登记的形象。若磁盘目录丢失，过滤或标记。
    /// </summary>
    public IReadOnlyList<PetLibraryItem> GetItems()
    {
        lock (_indexLock)
        {
            EnsureIndexLoaded();
            return _cachedIndex!.Items.ToList();
        }
    }

    /// <summary>
    /// 根据 ID 查找形象。
    /// </summary>
    public PetLibraryItem? FindItemById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (_indexLock)
        {
            EnsureIndexLoaded();
            return _cachedIndex!.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 根据目录绝对路径匹配形象。
    /// </summary>
    public PetLibraryItem? FindItemByDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        string normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        lock (_indexLock)
        {
            EnsureIndexLoaded();
            return _cachedIndex!.Items.FirstOrDefault(i =>
            {
                string itemFull = Path.GetFullPath(Path.Combine(LibraryRoot, i.RelativeDirectory))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(itemFull, normalized, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    /// <summary>
    /// 异步导入外部 PNG 文件夹（在后台线程执行校验、派生与入库）。
    /// </summary>
    public Task<PetLibraryImportResult> ImportFolderAsync(string sourceFolder, CancellationToken cancellationToken = default) =>
        Task.Run(() => ImportFolderInternal(sourceFolder, cancellationToken), cancellationToken);

    private PetLibraryImportResult ImportFolderInternal(string sourceFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return PetLibraryImportResult.Fail("指定的素材文件夹路径为空。", "library.import.empty_source");
        }

        string fullSourcePath;
        try
        {
            fullSourcePath = Path.GetFullPath(sourceFolder);
        }
        catch (Exception ex)
        {
            return PetLibraryImportResult.Fail($"路径格式无效：{ex.Message}", "library.import.invalid_path", ex.Message);
        }

        if (!Directory.Exists(fullSourcePath))
        {
            return PetLibraryImportResult.Fail($"素材文件夹不存在：{fullSourcePath}", "library.import.not_found", fullSourcePath);
        }

        // 拒绝重解析点（符号链接/连接点）
        try
        {
            var dirInfo = new DirectoryInfo(fullSourcePath);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return PetLibraryImportResult.Fail("安全限制：禁止导入符号链接或目录连接点（Reparse Point）。", "library.import.reparse_point", fullSourcePath);
            }
        }
        catch (Exception ex)
        {
            return PetLibraryImportResult.Fail($"检查目录属性失败：{ex.Message}", "library.import.check_attributes_failed", ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 1. 检查 idle.png
        string idlePath = Path.Combine(fullSourcePath, "idle.png");
        if (!File.Exists(idlePath))
        {
            return PetLibraryImportResult.Fail($"素材文件夹缺少必需的 idle.png 关键帧：{fullSourcePath}", "library.import.missing_idle", fullSourcePath);
        }

        // 2. 扫描并验证所有存在的白名单 PNG 文件
        var presentFrames = new Dictionary<string, (string SourcePath, int Width, int Height, byte[] RawBytes)>();
        int baseCanvasW = 0;
        int baseCanvasH = 0;

        foreach (string frameName in WhitelistedFrameNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string filePath = Path.Combine(fullSourcePath, frameName);
            if (!File.Exists(filePath)) continue;

            var fi = new FileInfo(filePath);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return PetLibraryImportResult.Fail($"安全限制：图帧文件 {frameName} 为重解析点或符号链接。", "library.import.reparse_point", frameName);
            }

            if (fi.Length > MaxSingleFileSize)
            {
                return PetLibraryImportResult.Fail($"图帧文件 {frameName} 超过单文件大小上限（{fi.Length} 字节，上限 {MaxSingleFileSize} 字节）。", "library.import.file_size_exceeded", frameName, fi.Length, MaxSingleFileSize);
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return PetLibraryImportResult.Fail($"读取图帧文件 {frameName} 失败：{ex.Message}", "library.import.read_file_failed", frameName, ex.Message);
            }

            int w, h;
            try
            {
                using var ms = new MemoryStream(bytes);
                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return PetLibraryImportResult.Fail($"图帧文件 {frameName} 中未找到有效 PNG 帧。", "library.import.no_valid_png", frameName);
                }
                BitmapSource frame = decoder.Frames[0];
                w = frame.PixelWidth;
                h = frame.PixelHeight;
            }
            catch (Exception ex)
            {
                return PetLibraryImportResult.Fail($"解码图帧文件 {frameName} 失败，可能已损坏：{ex.Message}", "library.import.corrupt_png", frameName, ex.Message);
            }

            if (w <= 0 || h <= 0 || w > MaxDimensionPixels || h > MaxDimensionPixels)
            {
                return PetLibraryImportResult.Fail($"图帧文件 {frameName} 尺寸超出合法范围（{w}x{h}，单边上限 {MaxDimensionPixels}）。", "library.import.dimension_exceeded", frameName, w, h, MaxDimensionPixels);
            }

            if (frameName == "idle.png")
            {
                baseCanvasW = w;
                baseCanvasH = h;
            }
            else
            {
                if (w != baseCanvasW || h != baseCanvasH)
                {
                    return PetLibraryImportResult.Fail($"画布尺寸不一致：idle.png 为 {baseCanvasW}x{baseCanvasH}，而 {frameName} 为 {w}x{h}。所有图帧必须共用相同画布尺寸。", "library.import.canvas_mismatch", baseCanvasW, baseCanvasH, frameName, w, h);
                }
            }

            presentFrames[frameName] = (filePath, w, h, bytes);
        }

        if (baseCanvasW <= 0 || baseCanvasH <= 0)
        {
            return PetLibraryImportResult.Fail("无法确定有效的主图画布尺寸。", "library.import.no_canvas_size");
        }

        // 3. 计算规范内容摘要（SHA256）
        string contentHash;
        using (var sha = SHA256.Create())
        {
            var sortedKeys = presentFrames.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (var key in sortedKeys)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                sha.TransformBlock(keyBytes, 0, keyBytes.Length, null, 0);

                var item = presentFrames[key];
                byte[] lenBytes = BitConverter.GetBytes(item.RawBytes.Length);
                sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);

                sha.TransformBlock(item.RawBytes, 0, item.RawBytes.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            contentHash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        // 4. 重复检测：若库中已有完全同内容形象，复用已有条目并返回 Duplicate
        lock (_indexLock)
        {
            EnsureIndexLoaded();
            var removed = _cachedIndex!.RemovedItems.FirstOrDefault(item => item.ContentHash == contentHash);
            if (removed != null && IsOwnedDirectory(removed) && File.Exists(Path.Combine(LibraryRoot, removed.RelativeDirectory, "idle.png")))
            {
                try
                {
                    var restored = new PetLibraryIndex { Version = 2, Items = new(_cachedIndex.Items), RemovedItems = _cachedIndex.RemovedItems.Where(item => item.Id != removed.Id).ToList() };
                    restored.Items.Add(removed);
                    SaveIndexObjectLocked(restored);
                    _cachedIndex = restored;
                    return PetLibraryImportResult.Ok(removed, "Restored", "library.import.restored", removed.Name);
                }
                catch (Exception ex) { return PetLibraryImportResult.Fail(ex.Message, "library.import.index_failed", ex.Message); }
            }
            var existing = _cachedIndex!.Items.FirstOrDefault(i => string.Equals(i.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                string existingDir = Path.Combine(LibraryRoot, existing.RelativeDirectory);
                if (Directory.Exists(existingDir) && File.Exists(Path.Combine(existingDir, "idle.png")))
                {
                    return PetLibraryImportResult.Duplicate(existing, $"形象库中已存在相同内容的形象「{existing.Name}」。", "library.import.duplicate", existing.Name);
                }
            }
        }

        // 5. 提取显示名称与元数据
        string? declaredName = null;
        string? declaredId = null;
        string assetInfoPath = Path.Combine(fullSourcePath, "asset-info.json");
        if (File.Exists(assetInfoPath))
        {
            try
            {
                var fi = new FileInfo(assetInfoPath);
                if (fi.Length <= 1024 * 1024)
                {
                    byte[] infoBytes = File.ReadAllBytes(assetInfoPath);
                    using var doc = JsonDocument.Parse(infoBytes);
                    if (doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        declaredName = nameProp.GetString()?.Trim();
                    }
                    if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        declaredId = idProp.GetString()?.Trim();
                    }
                }
            }
            catch
            {
                // 元数据解析失败静默降级到文件夹名称
            }
        }

        string displayName = !string.IsNullOrWhiteSpace(declaredName)
            ? declaredName
            : new DirectoryInfo(fullSourcePath).Name;

        // 6. 生成稳定、路径安全的唯一 ID
        string rawSlug = !string.IsNullOrWhiteSpace(declaredId) ? declaredId : displayName;
        string safeSlug = SanitizeSlug(rawSlug);
        string shortHash = contentHash.Length >= 8 ? contentHash[..8] : contentHash;
        string itemId = $"{safeSlug}-{shortHash}";

        cancellationToken.ThrowIfCancellationRequested();

        // 7. Staging 准备：整画布等比例派生至 512px（若原图尺寸 <= 512 则保持原画布不放大；若 > 512 则全画布等比缩至 512）
        EnsureDirectory(LibraryRoot);
        string stagingDir = Path.Combine(LibraryRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        EnsureDirectory(stagingDir);

        int finalCanvasW = baseCanvasW;
        int finalCanvasH = baseCanvasH;

        try
        {
            bool needScale = (baseCanvasW > StandardCanvasSize || baseCanvasH > StandardCanvasSize);
            double scale = 1.0;
            if (needScale)
            {
                scale = (double)StandardCanvasSize / Math.Max(baseCanvasW, baseCanvasH);
                finalCanvasW = (int)Math.Round(baseCanvasW * scale);
                finalCanvasH = (int)Math.Round(baseCanvasH * scale);
            }

            foreach (var kv in presentFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destFile = Path.Combine(stagingDir, kv.Key);

                if (!needScale)
                {
                    // 尺寸已在 512px 以内（如 512x512 沃雅妮莎），直接按已校验 RawBytes 写入，禁止重读外部源路径
                    File.WriteAllBytes(destFile, kv.Value.RawBytes);
                }
                else
                {
                    // 整画布等比缩放并保存为 PNG
                    using var ms = new MemoryStream(kv.Value.RawBytes);
                    var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapSource originalFrame = decoder.Frames[0];

                    var transformed = new TransformedBitmap(originalFrame, new ScaleTransform(scale, scale));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(transformed));

                    using var fs = File.Create(destFile);
                    encoder.Save(fs);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var newItem = new PetLibraryItem
            {
                Id = itemId,
                Name = displayName,
                ContentHash = contentHash,
                RelativeDirectory = itemId,
                CanvasWidth = finalCanvasW,
                CanvasHeight = finalCanvasH,
                AvailableFrames = presentFrames.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                ImportedAtUtc = DateTime.UtcNow,
            };

            // 8. 互斥区内再次防冲突、移动 staging 到正式目录并事务提交 index
            string targetDir = Path.Combine(LibraryRoot, itemId);
            bool targetDirCreated = false;

            lock (_indexLock)
            {
                EnsureIndexLoaded();

                // 在最终提交区重新检查同内容重复（防止并发重复导入）
                var existingByHash = _cachedIndex!.Items.FirstOrDefault(i => string.Equals(i.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase));
                if (existingByHash != null)
                {
                    TryCleanupDirectory(stagingDir);
                    return PetLibraryImportResult.Duplicate(existingByHash, $"该形象与现有「{existingByHash.Name}」内容完全相同。", "library.import.duplicate", existingByHash.Name);
                }

                var candidateItems = new List<PetLibraryItem>(_cachedIndex.Items);
                candidateItems.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
                candidateItems.Add(newItem);
                var candidateIndex = new PetLibraryIndex
                {
                    Version = 2,
                    Items = candidateItems,
                    RemovedItems = _cachedIndex.RemovedItems.Where(item => item.Id != newItem.Id).ToList(),
                };

                try
                {
                    if (Directory.Exists(targetDir))
                    {
                        string existingIdle = Path.Combine(targetDir, "idle.png");
                        if (File.Exists(existingIdle))
                        {
                            TryCleanupDirectory(stagingDir);
                            var existingItem = _cachedIndex.Items.FirstOrDefault(i => string.Equals(i.RelativeDirectory, itemId, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
                            if (existingItem != null)
                            {
                                return PetLibraryImportResult.Duplicate(existingItem, $"形象库中已存在相同内容的形象「{existingItem.Name}」。", "library.import.duplicate", existingItem.Name);
                            }
                            return PetLibraryImportResult.Fail($"形象目录已存在有效内容且未被当前索引引用，已拒绝覆盖：{targetDir}", "library.import.dir_exists", targetDir);
                        }
                        else
                        {
                            Directory.Delete(targetDir, true);
                        }
                    }
                    Directory.Move(stagingDir, targetDir);
                    targetDirCreated = true;

                    // 磁盘索引文件原子写入（若写入失败，抛出异常并在 catch 中清理本次目标目录）
                    SaveIndexObjectLocked(candidateIndex);

                    // 仅当磁盘提交成功后才发布到内存缓存
                    _cachedIndex = candidateIndex;
                }
                catch (Exception ex)
                {
                    if (targetDirCreated)
                    {
                        TryCleanupDirectory(targetDir);
                    }
                    TryCleanupDirectory(stagingDir);
                    return PetLibraryImportResult.Fail($"形象库索引保存失败：{ex.Message}", "library.import.index_failed", ex.Message);
                }
            }

            return PetLibraryImportResult.Ok(newItem, $"形象「{displayName}」导入成功。", "library.import.success", displayName);
        }
        catch (OperationCanceledException)
        {
            TryCleanupDirectory(stagingDir);
            throw;
        }
        catch (Exception ex)
        {
            TryCleanupDirectory(stagingDir);
            return PetLibraryImportResult.Fail($"导入形象时发生异常：{ex.Message}", "library.import.failed_exception", ex.Message);
        }
    }

    /// <summary>
    /// 加载小尺寸静态缩略图（默认宽 96px，规整且冻结）。
    /// </summary>
    public BitmapSource? LoadThumbnail(PetLibraryItem item, int decodeWidth = 96)
    {
        if (item == null) return null;
        string idlePath = Path.Combine(LibraryRoot, item.RelativeDirectory, "idle.png");
        if (!File.Exists(idlePath)) return null;

        try
        {
            byte[] bytes = File.ReadAllBytes(idlePath);
            using var ms = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.DecodePixelWidth = Math.Clamp(decodeWidth, 32, 256);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 加载指定形象的特定状态预览位图（冻结）。
    /// </summary>
    public BitmapSource? LoadPreviewFrame(PetLibraryItem item, string frameName)
    {
        if (item == null) return null;
        string targetFile = Path.Combine(LibraryRoot, item.RelativeDirectory, frameName);
        if (!File.Exists(targetFile))
        {
            // 回退到 idle.png
            targetFile = Path.Combine(LibraryRoot, item.RelativeDirectory, "idle.png");
        }
        if (!File.Exists(targetFile)) return null;

        try
        {
            byte[] bytes = File.ReadAllBytes(targetFile);
            using var ms = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = ms;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureIndexLoaded()
    {
        if (_cachedIndex != null) return;

        if (!File.Exists(IndexFilePath))
        {
            _cachedIndex = new PetLibraryIndex();
            return;
        }

        try
        {
            string json = File.ReadAllText(IndexFilePath, Encoding.UTF8);
            _cachedIndex = JsonSerializer.Deserialize<PetLibraryIndex>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new PetLibraryIndex();
            _cachedIndex.Items ??= new();
            _cachedIndex.RemovedItems ??= new();
        }
        catch (Exception ex)
        {
            _indexLoadError = ex.Message;
            _cachedIndex = new PetLibraryIndex();
        }
    }

    private void SaveIndexLocked()
    {
        SaveIndexObjectLocked(_cachedIndex ?? new PetLibraryIndex());
    }

    private void SaveIndexObjectLocked(PetLibraryIndex index)
    {
        if (_indexLoadError != null || (_cachedIndex?.Version ?? 1) > 2) throw new InvalidDataException(_indexLoadError ?? "Unsupported future library index");
        EnsureDirectory(LibraryRoot);
        string tmpFile = Path.Combine(LibraryRoot, "library-index.json.tmp");
        string json = JsonSerializer.Serialize(index, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(tmpFile, json, Encoding.UTF8);
        File.Move(tmpFile, IndexFilePath, overwrite: true);
    }

    private static string SanitizeSlug(string raw)
    {
        var sb = new StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append('-');
            }
        }
        string result = sb.ToString().Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(result) ? "pet" : result.ToLowerInvariant();
    }

    private static void EnsureDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void TryCleanupDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch { }
    }
}
