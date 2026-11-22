using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StarPie.Plugin.EmojiPicker;

internal sealed class EmojiImageCache : IDisposable
{
    private const int MaxEntries = 64;
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, BitmapSource Image)> _map = new(StringComparer.OrdinalIgnoreCase);

    internal BitmapSource? GetThumbnail(string filePath, int decodePixelWidth = 96)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string key = $"{decodePixelWidth}|{filePath}";
        if (_map.TryGetValue(key, out var hit))
        {
            _lru.Remove(hit.Node);
            _lru.AddFirst(hit.Node);
            return hit.Image;
        }

        BitmapSource? decoded = DecodeThumbnail(filePath, decodePixelWidth);
        if (decoded == null) return null;

        while (_map.Count >= MaxEntries && _lru.Last != null)
        {
            string victim = _lru.Last.Value;
            _lru.RemoveLast();
            _map.Remove(victim);
        }

        LinkedListNode<string> node = _lru.AddFirst(key);
        _map[key] = (node, decoded);
        return decoded;
    }

    internal void Clear()
    {
        _lru.Clear();
        _map.Clear();
    }

    public void Dispose() => Clear();

    internal static BitmapSource? DecodeThumbnail(string filePath, int decodePixelWidth)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (info.Length <= 0 || info.Length > EmojiLibraryStorage.MaxStickerBytes)
                return null;

            byte[] bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class GifAnimationController : IDisposable
{
    private const int MaxFrames = 90;
    private sealed record GifFrameItem(BitmapSource Frame, int DelayMs);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private Image? _targetImage;
    private BitmapSource? _restoreStill;
    private List<GifFrameItem>? _frames;
    private int _frameIndex;
    private string? _activePath;

    internal GifAnimationController()
    {
        _timer.Tick += OnTick;
    }

    internal void Start(Image imageControl, string gifPath, BitmapSource? stillFallback)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(gifPath) || !File.Exists(gifPath))
            return;

        List<GifFrameItem>? decoded = DecodeGifFrames(gifPath);
        if (decoded == null || decoded.Count <= 1)
            return;

        _targetImage = imageControl;
        _restoreStill = stillFallback ?? imageControl.Source as BitmapSource;
        _frames = decoded;
        _frameIndex = 0;
        _activePath = gifPath;
        imageControl.Source = _frames[0].Frame;
        _timer.Interval = TimeSpan.FromMilliseconds(_frames[0].DelayMs);
        _timer.Start();
    }

    internal void Stop(Image? onlyIfTarget = null)
    {
        if (onlyIfTarget != null && !ReferenceEquals(_targetImage, onlyIfTarget))
            return;

        _timer.Stop();
        if (_targetImage != null && _restoreStill != null)
            _targetImage.Source = _restoreStill;
        _targetImage = null;
        _restoreStill = null;
        _frames = null;
        _frameIndex = 0;
        _activePath = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_targetImage == null || _frames == null || _frames.Count == 0 || string.IsNullOrEmpty(_activePath))
        {
            Stop();
            return;
        }

        _frameIndex = (_frameIndex + 1) % _frames.Count;
        GifFrameItem next = _frames[_frameIndex];
        _targetImage.Source = next.Frame;
        _timer.Interval = TimeSpan.FromMilliseconds(next.DelayMs);
    }

    private static List<GifFrameItem>? DecodeGifFrames(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > EmojiLibraryStorage.MaxStickerBytes)
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count <= 1) return null;

            int canvasW = decoder.Frames[0].PixelWidth;
            int canvasH = decoder.Frames[0].PixelHeight;
            if (decoder.Metadata is BitmapMetadata globalMeta)
            {
                if (TryGetInt(globalMeta, "/logscrdesc/Width", out int lw) && lw > 0) canvasW = lw;
                if (TryGetInt(globalMeta, "/logscrdesc/Height", out int lh) && lh > 0) canvasH = lh;
            }
            if (canvasW <= 0 || canvasH <= 0) return null;

            double scale = Math.Min(1.0, 96.0 / Math.Max(canvasW, canvasH));
            int outW = Math.Max(16, (int)Math.Round(canvasW * scale));
            int outH = Math.Max(16, (int)Math.Round(canvasH * scale));

            var result = new List<GifFrameItem>();
            BitmapSource? accumulated = null;
            int count = Math.Min(decoder.Frames.Count, MaxFrames);

            for (int i = 0; i < count; i++)
            {
                BitmapFrame raw = decoder.Frames[i];
                int left = 0, top = 0, width = raw.PixelWidth, height = raw.PixelHeight;
                int delayCs = 10;
                int disposal = 0;

                if (raw.Metadata is BitmapMetadata meta)
                {
                    if (TryGetInt(meta, "/imgdesc/Left", out int l)) left = l;
                    if (TryGetInt(meta, "/imgdesc/Top", out int t)) top = t;
                    if (TryGetInt(meta, "/imgdesc/Width", out int w) && w > 0) width = w;
                    if (TryGetInt(meta, "/imgdesc/Height", out int h) && h > 0) height = h;
                    if (TryGetInt(meta, "/grctlext/Delay", out int d) && d > 0) delayCs = d;
                    if (TryGetInt(meta, "/grctlext/Disposal", out int disp)) disposal = disp;
                }

                var visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    if (accumulated != null)
                        dc.DrawImage(accumulated, new Rect(0, 0, outW, outH));
                    dc.DrawImage(raw, new Rect(left * scale, top * scale, width * scale, height * scale));
                }

                var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();

                result.Add(new GifFrameItem(rtb, Math.Clamp(delayCs * 10, 30, 1000)));
                accumulated = disposal == 2 ? null : rtb;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetInt(BitmapMetadata meta, string query, out int value)
    {
        value = 0;
        try
        {
            if (meta.ContainsQuery(query))
            {
                object? obj = meta.GetQuery(query);
                if (obj != null)
                {
                    value = Convert.ToInt32(obj);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void Dispose() => Stop();
}
