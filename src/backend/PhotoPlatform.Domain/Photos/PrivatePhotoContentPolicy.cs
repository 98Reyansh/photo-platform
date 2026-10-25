namespace PhotoPlatform.Domain.Photos;

public enum PrivatePhotoFormat
{
    Jpeg,
    Png,
    WebP,
}

public enum PrivatePhotoContentDecision
{
    Empty,
    TooLarge,
    Unrecognized,
    SignatureRecognized,
    Malformed,
    Truncated,
    Inconsistent,
    Dimensions,
}

public readonly record struct PrivatePhotoContentVerdict(
    PrivatePhotoContentDecision Decision,
    PrivatePhotoFormat? Format)
{
    public bool ImageDecodeVerified => false;
}

public static class PrivatePhotoContentPolicy
{
    public const long MaxBytes = 26_214_400;

    public const long MaxDecodedPixels = 40_000_000;

    public const int MaxLongEdgePixels = 12_000;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PrivatePhotoContentVerdict Evaluate(long byteLength, ReadOnlySpan<byte> prefix)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (byteLength == 0)
        {
            return new PrivatePhotoContentVerdict(PrivatePhotoContentDecision.Empty, null);
        }

        if (byteLength > MaxBytes)
        {
            return new PrivatePhotoContentVerdict(PrivatePhotoContentDecision.TooLarge, null);
        }

        if (TryMatchSignature(prefix, byteLength, out var format))
        {
            return new PrivatePhotoContentVerdict(PrivatePhotoContentDecision.SignatureRecognized, format);
        }

        return new PrivatePhotoContentVerdict(PrivatePhotoContentDecision.Unrecognized, null);
    }

    public static bool TryAcceptDimensions(
        int width,
        int height,
        int maxWidth,
        int maxHeight,
        long maxPixels)
    {
        if (width <= 0 || height <= 0 || maxWidth <= 0 || maxHeight <= 0 || maxPixels <= 0)
        {
            return false;
        }

        if (width > maxWidth || height > maxHeight)
        {
            return false;
        }

        return (long)width * height <= maxPixels;
    }

    private static bool TryMatchSignature(
        ReadOnlySpan<byte> prefix,
        long byteLength,
        out PrivatePhotoFormat format)
    {
        if (byteLength >= 3
            && prefix.Length >= 3
            && prefix[0] == 0xFF
            && prefix[1] == 0xD8
            && prefix[2] == 0xFF)
        {
            format = PrivatePhotoFormat.Jpeg;
            return true;
        }

        if (byteLength >= PngSignature.Length && prefix.StartsWith(PngSignature))
        {
            format = PrivatePhotoFormat.Png;
            return true;
        }

        if (byteLength >= 12
            && prefix.Length >= 12
            && prefix[0] == (byte)'R'
            && prefix[1] == (byte)'I'
            && prefix[2] == (byte)'F'
            && prefix[3] == (byte)'F'
            && prefix[8] == (byte)'W'
            && prefix[9] == (byte)'E'
            && prefix[10] == (byte)'B'
            && prefix[11] == (byte)'P')
        {
            format = PrivatePhotoFormat.WebP;
            return true;
        }

        format = default;
        return false;
    }
}
