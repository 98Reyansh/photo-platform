using Microsoft.Extensions.Options;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;
using SkiaSharp;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class SkiaPrivatePhotoDecoder(IOptions<PrivatePhotoDecodeOptions> options) : IPrivatePhotoDecoder
{
    public PrivatePhotoDecodeResult Decode(ReadOnlyMemory<byte> content)
    {
        var byteLength = content.Length;
        if (byteLength == 0)
        {
            return Reject(PrivatePhotoContentDecision.Empty);
        }

        if (byteLength > PrivatePhotoContentPolicy.MaxBytes)
        {
            return Reject(PrivatePhotoContentDecision.TooLarge);
        }

        var prefixLength = Math.Min(12, byteLength);
        var verdict = PrivatePhotoContentPolicy.Evaluate(byteLength, content.Span[..prefixLength]);
        if (verdict.Decision != PrivatePhotoContentDecision.SignatureRecognized || verdict.Format is not PrivatePhotoFormat format)
        {
            return Reject(verdict.Decision);
        }

        try
        {
            using var encoded = SKData.CreateCopy(content.Span);
            using var codec = SKCodec.Create(encoded);
            if (codec is null)
            {
                return Reject(PrivatePhotoContentDecision.Malformed);
            }

            if (ConflictingFormat(format, codec.EncodedFormat) is PrivatePhotoContentDecision conflict)
            {
                return Reject(conflict);
            }

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            var limits = EffectiveLimits(options.Value);
            if (!PrivatePhotoContentPolicy.TryAcceptDimensions(
                    width,
                    height,
                    limits.MaxWidth,
                    limits.MaxHeight,
                    limits.MaxPixels))
            {
                return Reject(PrivatePhotoContentDecision.Dimensions);
            }

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rowBytes = info.RowBytes;
            var pixelBytes = (long)rowBytes * height;
            if (rowBytes < width * 4
                || pixelBytes <= 0
                || pixelBytes > PrivatePhotoContentPolicy.MaxDecodedPixels * 4L)
            {
                return Reject(PrivatePhotoContentDecision.Dimensions);
            }

            var pixels = new byte[pixelBytes];
            var decoded = codec.GetPixels(info, pixels);
            return decoded switch
            {
                SKCodecResult.Success => new PrivatePhotoDecodeResult.Verified(format, width, height),
                SKCodecResult.IncompleteInput => Reject(PrivatePhotoContentDecision.Truncated),
                _ => Reject(PrivatePhotoContentDecision.Malformed),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return Reject(PrivatePhotoContentDecision.Malformed);
        }
    }

    internal static PrivatePhotoContentDecision? ConflictingFormat(
        PrivatePhotoFormat signature,
        SKEncodedImageFormat encoded)
    {
        var agrees = signature switch
        {
            PrivatePhotoFormat.Jpeg => encoded == SKEncodedImageFormat.Jpeg,
            PrivatePhotoFormat.Png => encoded == SKEncodedImageFormat.Png,
            PrivatePhotoFormat.WebP => encoded == SKEncodedImageFormat.Webp,
            _ => false,
        };
        return agrees ? null : PrivatePhotoContentDecision.Inconsistent;
    }

    internal static (int MaxWidth, int MaxHeight, long MaxPixels) EffectiveLimits(PrivatePhotoDecodeOptions configured)
    {
        return (
            Clamp(configured.MaxWidth, PrivatePhotoContentPolicy.MaxLongEdgePixels),
            Clamp(configured.MaxHeight, PrivatePhotoContentPolicy.MaxLongEdgePixels),
            Clamp(configured.MaxPixels, PrivatePhotoContentPolicy.MaxDecodedPixels));
    }

    private static PrivatePhotoDecodeResult Reject(PrivatePhotoContentDecision decision)
    {
        return new PrivatePhotoDecodeResult.Rejected(decision);
    }

    private static int Clamp(int configured, int ceiling)
    {
        return configured >= 1 && configured <= ceiling ? configured : ceiling;
    }

    private static long Clamp(long configured, long ceiling)
    {
        return configured >= 1 && configured <= ceiling ? configured : ceiling;
    }
}
