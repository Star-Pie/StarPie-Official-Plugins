using StarPie.Plugin;
using ZXing;
using ZXing.Common;

namespace StarPie.Plugin.QrScanner;

internal sealed record QrPoint(float X, float Y);
internal sealed record QrResult(string Text, IReadOnlyList<QrPoint> Points);

/// <summary>Pure managed, offline QR decoder. Each attempt owns a reader; no global decoder caches.</summary>
internal sealed class QrDecoder
{
    internal const int MaxResults = 32;
    internal static readonly double[] Scales = [1, 1.5, 2, 3];

    internal IReadOnlyList<QrResult> Decode(CapturedImage image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] original = ToLuminance(image);
        foreach (double scale in GetScales(image.Width, image.Height))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int width = (int)Math.Round(image.Width * scale);
            int height = (int)Math.Round(image.Height * scale);
            byte[] pixels = scale == 1 ? original : Resize(original, image.Width, image.Height, width, height, cancellationToken);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
            };
            var source = new PlanarYUVLuminanceSource(pixels, width, height, 0, 0, width, height, false);
            Result[] normal = reader.DecodeMultiple(source) ?? [];
            cancellationToken.ThrowIfCancellationRequested();
            // Decode both polarities even when ordinary codes exist in the same image.
            Result[] inverted = reader.DecodeMultiple(source.invert()) ?? [];
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<QrResult>();
            foreach (Result result in normal.Concat(inverted))
            {
                // ZXing owns ECI/charset decoding. Do not reinterpret Unicode text as Latin-1.
                if (result.Text == null || result.Text.Length > 8192) continue;
                var points = (result.ResultPoints ?? []).Select(point =>
                    new QrPoint((float)(point.X / scale + image.ScreenX), (float)(point.Y / scale + image.ScreenY))).ToArray();
                var candidate = new QrResult(result.Text, points);
                if (results.Any(existing => SameDetection(existing, candidate))) continue;
                results.Add(candidate);
                if (results.Count == MaxResults) break;
            }
            if (results.Count != 0) return results;
        }
        return Array.Empty<QrResult>();
    }

    private static bool SameDetection(QrResult left, QrResult right)
    {
        if (left.Text != right.Text || left.Points.Count == 0 || right.Points.Count == 0) return false;
        float dx = left.Points.Average(point => point.X) - right.Points.Average(point => point.X);
        float dy = left.Points.Average(point => point.Y) - right.Points.Average(point => point.Y);
        return dx * dx + dy * dy <= 16;
    }

    internal static IEnumerable<double> GetScales(int width, int height)
    {
        foreach (double scale in Scales)
        {
            int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
            if (w <= CapturedImage.MaxDimension && h <= CapturedImage.MaxDimension &&
                (long)w * h <= CapturedImage.MaxPixels) yield return scale;
        }
    }

    private static byte[] ToLuminance(CapturedImage image)
    {
        byte[] result = new byte[image.Width * image.Height];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = i * 4;
            result[i] = (byte)((image.Bgra32[offset + 2] * 77 + image.Bgra32[offset + 1] * 150 +
                image.Bgra32[offset] * 29) >> 8);
        }
        return result;
    }

    private static byte[] Resize(byte[] source, int sourceWidth, int sourceHeight, int width, int height, CancellationToken token)
    {
        byte[] output = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            double sy = Math.Max(0, (y + 0.5) * sourceHeight / height - 0.5);
            int y0 = Math.Min((int)sy, sourceHeight - 1), y1 = Math.Min(y0 + 1, sourceHeight - 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Max(0, (x + 0.5) * sourceWidth / width - 0.5);
                int x0 = Math.Min((int)sx, sourceWidth - 1), x1 = Math.Min(x0 + 1, sourceWidth - 1);
                double fx = sx - x0;
                double top = source[y0 * sourceWidth + x0] * (1 - fx) + source[y0 * sourceWidth + x1] * fx;
                double bottom = source[y1 * sourceWidth + x0] * (1 - fx) + source[y1 * sourceWidth + x1] * fx;
                output[y * width + x] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
            }
        }
        return output;
    }
}
