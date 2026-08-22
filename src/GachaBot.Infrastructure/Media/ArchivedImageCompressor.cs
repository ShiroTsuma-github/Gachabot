using SkiaSharp;

namespace GachaBot.Infrastructure.Media;

public sealed record CompressedImage(
    byte[] Bytes,
    string ContentType,
    string Extension,
    bool WasCompressed);

public static class ArchivedImageCompressor
{
    private const long MaximumDecodedPixels = 80_000_000;
    private static readonly int[] WebpQualities = [85, 75, 65, 55, 45, 35];

    public static CompressedImage Compress(
        string inputPath,
        string contentType,
        long maximumBytes,
        int maximumDimension)
    {
        var original = File.ReadAllBytes(inputPath);
        var originalExtension = ExtensionFor(contentType);
        if (contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase))
        {
            if (original.LongLength > maximumBytes)
            {
                throw new MediaCompressionException(
                    "Animated GIF exceeds the Discord-safe limit and cannot be recompressed without losing animation.");
            }

            return new CompressedImage(original, contentType, originalExtension, false);
        }

        using var encodedStream = new SKMemoryStream(original);
        using var codec = SKCodec.Create(encodedStream)
            ?? throw new UnsupportedMediaTypeException(contentType);
        if ((long)codec.Info.Width * codec.Info.Height > MaximumDecodedPixels)
        {
            throw new MediaCompressionException("Decoded image dimensions exceed the safe processing limit.");
        }

        using var bitmap = SKBitmap.Decode(original)
            ?? throw new UnsupportedMediaTypeException(contentType);

        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
        byte[]? best = null;
        while (scale >= 0.08d)
        {
            using var scaled = Scale(bitmap, scale);
            foreach (var quality in WebpQualities)
            {
                using var image = SKImage.FromBitmap(scaled);
                using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
                var candidate = data.ToArray();
                if (best is null || candidate.Length < best.Length)
                {
                    best = candidate;
                }

                if (candidate.LongLength <= maximumBytes)
                {
                    if (scale >= 0.999d && original.LongLength <= maximumBytes &&
                        candidate.LongLength >= original.LongLength)
                    {
                        return new CompressedImage(original, contentType, originalExtension, false);
                    }

                    return new CompressedImage(candidate, "image/webp", ".webp", true);
                }
            }

            scale *= 0.75d;
        }

        if (original.LongLength <= maximumBytes)
        {
            return new CompressedImage(original, contentType, originalExtension, false);
        }

        throw new MediaCompressionException(
            $"Image could not be compressed below {maximumBytes} bytes; smallest result was {best?.LongLength ?? 0} bytes.");
    }

    private static SKBitmap Scale(SKBitmap bitmap, double scale)
    {
        if (scale >= 0.999d)
        {
            return bitmap.Copy();
        }

        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var resized = new SKBitmap(width, height, bitmap.ColorType, bitmap.AlphaType);
        if (!bitmap.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.Mitchell)))
        {
            resized.Dispose();
            throw new MediaCompressionException("Image resize failed.");
        }

        return resized;
    }

    internal static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => throw new UnsupportedMediaTypeException(contentType),
    };
}

public sealed class MediaCompressionException(string message) : InvalidOperationException(message);
