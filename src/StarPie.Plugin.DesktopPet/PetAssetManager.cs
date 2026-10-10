using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.DesktopPet;

/// <summary>
/// 素材载入结果。
/// </summary>
public sealed class PetAssetLoadResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? LoadedDirectory { get; init; }
    public bool HasBlinkFrame { get; init; }
    public bool HasReactFrame { get; init; }
    public bool HasDragFrame { get; init; }
    public int PointingFrameCount { get; init; }

    public static PetAssetLoadResult Ok(string dir, bool hasBlink, bool hasReact, bool hasDrag = false, int pointingCount = 0) =>
        new()
        {
            Success = true,
            LoadedDirectory = dir,
            HasBlinkFrame = hasBlink,
            HasReactFrame = hasReact,
            HasDragFrame = hasDrag,
            PointingFrameCount = pointingCount,
            Message = "素材加载成功",
        };

    public static PetAssetLoadResult Fail(string message) =>
        new()
        {
            Success = false,
            Message = message,
        };
}

/// <summary>
/// 桌宠素材与图像帧管理。
/// <para>
/// 负责本地 PNG 帧的安全解码、尺寸限制、FormatConverted 规整与 Freeze 冻结。
/// 提取 Alpha 通道图供微秒级无缝鼠标穿透（WM_NCHITTEST）判定。
/// </para>
/// </summary>
public sealed class PetAssetManager
{
    public const long MaxFileSizeLimitBytes = 10 * 1024 * 1024; // 10 MB 上限
    public const int MaxDimensionLimitPixels = 4096;            // 4096 像素上限
    public const byte DefaultAlphaThreshold = 24;               // 判定为实体的透明度阈值

    private BitmapSource? _idleBitmap;
    private BitmapSource? _blinkBitmap;
    private BitmapSource? _reactBitmap;
    private BitmapSource? _dragBitmap;
    private BitmapSource? _pointE;
    private BitmapSource? _pointSE;
    private BitmapSource? _pointS;
    private BitmapSource? _pointSW;
    private BitmapSource? _pointW;
    private BitmapSource? _pointNW;
    private BitmapSource? _pointN;
    private BitmapSource? _pointNE;

    private byte[]? _idleAlphaMap;
    private int _pixelWidth;
    private int _pixelHeight;
    private string? _currentDirectory;

    public BitmapSource? IdleBitmap => _idleBitmap;
    public BitmapSource? BlinkBitmap => _blinkBitmap ?? _idleBitmap;
    public BitmapSource? ReactBitmap => _reactBitmap ?? _idleBitmap;
    public BitmapSource? DragBitmap => _dragBitmap ?? _idleBitmap;

    public BitmapSource? PointEBitmap => _pointE ?? _idleBitmap;
    public BitmapSource? PointSEBitmap => _pointSE ?? _idleBitmap;
    public BitmapSource? PointSBitmap => _pointS ?? _idleBitmap;
    public BitmapSource? PointSWBitmap => _pointSW ?? _idleBitmap;
    public BitmapSource? PointWBitmap => _pointW ?? _idleBitmap;
    public BitmapSource? PointNWBitmap => _pointNW ?? _idleBitmap;
    public BitmapSource? PointNBitmap => _pointN ?? _idleBitmap;
    public BitmapSource? PointNEBitmap => _pointNE ?? _idleBitmap;

    public byte[]? IdleAlphaMap => _idleAlphaMap;
    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public string? CurrentDirectory => _currentDirectory;
    public bool HasIdleImage => _idleBitmap != null;
    public bool HasBlinkFrame { get; private set; }
    public bool HasReactFrame { get; private set; }
    public bool HasDragFrame { get; private set; }
    public int PointingFrameCount { get; private set; }

    /// <summary>
    /// 从指定目录加载桌宠素材。
    /// 必须包含 idle.png；blink.png、react.png、drag.png 以及 8 方向 point_*.png 可选，缺失时回退 idle.png。
    /// </summary>
    public PetAssetLoadResult LoadFromDirectory(string? directoryPath)
    {
        var (result, snapshot) = LoadSnapshotFromDirectory(directoryPath);
        if (result.Success && snapshot != null)
        {
            RestoreSnapshot(snapshot);
        }
        return result;
    }

    /// <summary>
    /// 从指定目录安全解码并生成完全冻结的素材快照（可在任意后台线程无锁执行，不修改当前实例状态）。
    /// </summary>
    public static (PetAssetLoadResult Result, PetAssetSnapshot? Snapshot) LoadSnapshotFromDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return (PetAssetLoadResult.Fail("未配置本地素材目录，请在插件设置中指定。"), null);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directoryPath);
        }
        catch (Exception ex)
        {
            return (PetAssetLoadResult.Fail($"素材目录路径无效：{ex.Message}"), null);
        }

        if (!Directory.Exists(fullPath))
        {
            return (PetAssetLoadResult.Fail($"素材目录不存在：{fullPath}"), null);
        }

        string idlePath = Path.Combine(fullPath, "idle.png");
        if (!File.Exists(idlePath))
        {
            return (PetAssetLoadResult.Fail($"素材目录缺少必需的 idle.png：{fullPath}"), null);
        }

        // 1. 解码主帧 idle.png 并生成 Alpha 掩膜
        BitmapSource idleSource;
        byte[] idleAlpha;
        int width;
        int height;
        try
        {
            (idleSource, idleAlpha, width, height) = DecodeAndFreezePng(idlePath, extractAlpha: true);
        }
        catch (Exception ex)
        {
            return (PetAssetLoadResult.Fail($"idle.png 解码失败：{ex.Message}"), null);
        }

        // 2. 解码可选帧 blink.png
        var (blinkSource, hasBlink) = LoadOptionalFrame(fullPath, "blink.png");

        // 3. 解码可选帧 react.png
        var (reactSource, hasReact) = LoadOptionalFrame(fullPath, "react.png");

        // 4. 解码可选帧 drag.png
        var (dragSource, hasDrag) = LoadOptionalFrame(fullPath, "drag.png");

        // 5. 解码可选 8 方向指向帧 point_*.png
        var (pE, hasPE) = LoadOptionalFrame(fullPath, "point_E.png");
        var (pSE, hasPSE) = LoadOptionalFrame(fullPath, "point_SE.png");
        var (pS, hasPS) = LoadOptionalFrame(fullPath, "point_S.png");
        var (pSW, hasPSW) = LoadOptionalFrame(fullPath, "point_SW.png");
        var (pW, hasPW) = LoadOptionalFrame(fullPath, "point_W.png");
        var (pNW, hasPNW) = LoadOptionalFrame(fullPath, "point_NW.png");
        var (pN, hasPN) = LoadOptionalFrame(fullPath, "point_N.png");
        var (pNE, hasPNE) = LoadOptionalFrame(fullPath, "point_NE.png");

        int pointingCount = (hasPE ? 1 : 0) + (hasPSE ? 1 : 0) + (hasPS ? 1 : 0) + (hasPSW ? 1 : 0) +
                            (hasPW ? 1 : 0) + (hasPNW ? 1 : 0) + (hasPN ? 1 : 0) + (hasPNE ? 1 : 0);

        var snapshot = new PetAssetSnapshot
        {
            IdleBitmap = idleSource,
            BlinkBitmap = blinkSource ?? idleSource,
            ReactBitmap = reactSource ?? idleSource,
            DragBitmap = dragSource ?? idleSource,
            PointE = pE,
            PointSE = pSE,
            PointS = pS,
            PointSW = pSW,
            PointW = pW,
            PointNW = pNW,
            PointN = pN,
            PointNE = pNE,
            IdleAlphaMap = idleAlpha,
            PixelWidth = width,
            PixelHeight = height,
            CurrentDirectory = fullPath,
            HasBlinkFrame = hasBlink,
            HasReactFrame = hasReact,
            HasDragFrame = hasDrag,
            PointingFrameCount = pointingCount,
        };

        return (PetAssetLoadResult.Ok(fullPath, hasBlink, hasReact, hasDrag, pointingCount), snapshot);
    }

    private static (BitmapSource? Bitmap, bool Exists) LoadOptionalFrame(string fullPath, string fileName)
    {
        string p = Path.Combine(fullPath, fileName);
        if (File.Exists(p))
        {
            try
            {
                (BitmapSource? bmp, _, _, _) = DecodeAndFreezePng(p, extractAlpha: false);
                return (bmp, true);
            }
            catch
            {
                return (null, false);
            }
        }
        return (null, false);
    }

    /// <summary>
    /// 根据当前帧状态返回对应的 BitmapSource。
    /// </summary>
    public BitmapSource? GetBitmapForFrame(PetFrameKind frame) => frame switch
    {
        PetFrameKind.Blink => BlinkBitmap,
        PetFrameKind.React => ReactBitmap,
        PetFrameKind.Drag => DragBitmap,
        PetFrameKind.PointE => PointEBitmap,
        PetFrameKind.PointSE => PointSEBitmap,
        PetFrameKind.PointS => PointSBitmap,
        PetFrameKind.PointSW => PointSWBitmap,
        PetFrameKind.PointW => PointWBitmap,
        PetFrameKind.PointNW => PointNWBitmap,
        PetFrameKind.PointN => PointNBitmap,
        PetFrameKind.PointNE => PointNEBitmap,
        _ => IdleBitmap,
    };

    /// <summary>
    /// 判断窗口内某物理/相对坐标是否命中桌宠的非透明区域（用于 WM_NCHITTEST 穿透判断）。
    /// </summary>
    public bool HitTest(
        int relX,
        int relY,
        int winWidth,
        int winHeight,
        double breathingScaleY = 1.0,
        byte alphaThreshold = DefaultAlphaThreshold)
    {
        if (_idleAlphaMap == null || _pixelWidth <= 0 || _pixelHeight <= 0 || winWidth <= 0 || winHeight <= 0)
        {
            return false;
        }

        // 逆向解算底部锚定呼吸缩放
        double scaleY = Math.Max(0.1, breathingScaleY);
        double unscaledRelY = winHeight - (winHeight - relY) / scaleY;

        if (relX < 0 || relX >= winWidth || unscaledRelY < 0 || unscaledRelY >= winHeight)
        {
            return false;
        }

        int px = (int)((double)relX / winWidth * _pixelWidth);
        int py = (int)(unscaledRelY / winHeight * _pixelHeight);

        if (px < 0 || px >= _pixelWidth || py < 0 || py >= _pixelHeight)
        {
            return false;
        }

        int index = py * _pixelWidth + px;
        if (index < 0 || index >= _idleAlphaMap.Length) return false;

        return _idleAlphaMap[index] >= alphaThreshold;
    }

    /// <summary>
    /// 解码单张 PNG 图片并冻结。若 extractAlpha 为 true，提取其 Alpha 通道数组。
    /// </summary>
    private static (BitmapSource Bitmap, byte[] AlphaMap, int Width, int Height) DecodeAndFreezePng(
        string filePath,
        bool extractAlpha)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSizeLimitBytes)
        {
            throw new InvalidOperationException($"图片文件过大（{fileInfo.Length} 字节，上限 {MaxFileSizeLimitBytes} 字节）");
        }

        byte[] rawBytes = File.ReadAllBytes(filePath);
        using var ms = new MemoryStream(rawBytes);

        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("PNG 图像中未找到有效图帧。");
        }

        BitmapSource originalFrame = decoder.Frames[0];
        int w = originalFrame.PixelWidth;
        int h = originalFrame.PixelHeight;

        if (w <= 0 || h <= 0 || w > MaxDimensionLimitPixels || h > MaxDimensionLimitPixels)
        {
            throw new InvalidOperationException($"图片尺寸不合法（{w}x{h}，单边上限 {MaxDimensionLimitPixels}）。");
        }

        // 规整为预乘 BGRA32 格式，确保跨格式一致性与高性能渲染
        FormatConvertedBitmap converted = new();
        converted.BeginInit();
        converted.Source = originalFrame;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();

        // 提取 Alpha 掩膜供命中穿透检测
        byte[] alphaMap = Array.Empty<byte>();
        if (extractAlpha)
        {
            alphaMap = new byte[w * h];
            int stride = w * 4;
            byte[] pixelBytes = new byte[stride * h];
            converted.CopyPixels(pixelBytes, stride, 0);

            for (int i = 0; i < alphaMap.Length; i++)
            {
                alphaMap[i] = pixelBytes[i * 4 + 3];
            }
        }

        converted.Freeze();
        return (converted, alphaMap, w, h);
    }

    /// <summary>
    /// 创建当前已载入内存素材的只读快照。
    /// </summary>
    public PetAssetSnapshot CreateSnapshot() => new()
    {
        IdleBitmap = _idleBitmap,
        BlinkBitmap = _blinkBitmap,
        ReactBitmap = _reactBitmap,
        DragBitmap = _dragBitmap,
        PointE = _pointE,
        PointSE = _pointSE,
        PointS = _pointS,
        PointSW = _pointSW,
        PointW = _pointW,
        PointNW = _pointNW,
        PointN = _pointN,
        PointNE = _pointNE,
        IdleAlphaMap = _idleAlphaMap,
        PixelWidth = _pixelWidth,
        PixelHeight = _pixelHeight,
        CurrentDirectory = _currentDirectory,
        HasBlinkFrame = HasBlinkFrame,
        HasReactFrame = HasReactFrame,
        HasDragFrame = HasDragFrame,
        PointingFrameCount = PointingFrameCount,
    };

    /// <summary>
    /// 从快照恢复已载入内存素材，不依赖重新读取磁盘。
    /// </summary>
    public void RestoreSnapshot(PetAssetSnapshot snapshot)
    {
        _idleBitmap = snapshot.IdleBitmap;
        _blinkBitmap = snapshot.BlinkBitmap;
        _reactBitmap = snapshot.ReactBitmap;
        _dragBitmap = snapshot.DragBitmap;
        _pointE = snapshot.PointE;
        _pointSE = snapshot.PointSE;
        _pointS = snapshot.PointS;
        _pointSW = snapshot.PointSW;
        _pointW = snapshot.PointW;
        _pointNW = snapshot.PointNW;
        _pointN = snapshot.PointN;
        _pointNE = snapshot.PointNE;
        _idleAlphaMap = snapshot.IdleAlphaMap;
        _pixelWidth = snapshot.PixelWidth;
        _pixelHeight = snapshot.PixelHeight;
        _currentDirectory = snapshot.CurrentDirectory;
        HasBlinkFrame = snapshot.HasBlinkFrame;
        HasReactFrame = snapshot.HasReactFrame;
        HasDragFrame = snapshot.HasDragFrame;
        PointingFrameCount = snapshot.PointingFrameCount;
    }
}

/// <summary>
/// 内存素材快照，供事务失败时完全回滚使用。
/// </summary>
public sealed class PetAssetSnapshot
{
    public BitmapSource? IdleBitmap { get; init; }
    public BitmapSource? BlinkBitmap { get; init; }
    public BitmapSource? ReactBitmap { get; init; }
    public BitmapSource? DragBitmap { get; init; }
    public BitmapSource? PointE { get; init; }
    public BitmapSource? PointSE { get; init; }
    public BitmapSource? PointS { get; init; }
    public BitmapSource? PointSW { get; init; }
    public BitmapSource? PointW { get; init; }
    public BitmapSource? PointNW { get; init; }
    public BitmapSource? PointN { get; init; }
    public BitmapSource? PointNE { get; init; }
    public byte[]? IdleAlphaMap { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public string? CurrentDirectory { get; init; }
    public bool HasBlinkFrame { get; init; }
    public bool HasReactFrame { get; init; }
    public bool HasDragFrame { get; init; }
    public int PointingFrameCount { get; init; }
}
