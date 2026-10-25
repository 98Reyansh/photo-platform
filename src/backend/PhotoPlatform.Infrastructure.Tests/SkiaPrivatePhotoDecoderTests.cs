using Microsoft.Extensions.Options;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Photos;
using SkiaSharp;

namespace PhotoPlatform.Infrastructure.Tests;

public class SkiaPrivatePhotoDecoderTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, PrivatePhotoFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png, PrivatePhotoFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp, PrivatePhotoFormat.WebP)]
    public void Valid_jpeg_png_and_webp_are_decoded(SKEncodedImageFormat encoded, PrivatePhotoFormat format)
    {
        var decoder = CreateDecoder();

        var result = decoder.Decode(Encode(encoded, 2, 2));

        var verified = Assert.IsType<PrivatePhotoDecodeResult.Verified>(result);
        Assert.Equal(format, verified.Format);
        Assert.Equal(2, verified.Width);
        Assert.Equal(2, verified.Height);
        Assert.True(verified.ImageDecodeVerified);
    }

    [Fact]
    public void Empty_input_is_rejected()
    {
        var result = CreateDecoder().Decode(ReadOnlyMemory<byte>.Empty);

        AssertRejected(result, PrivatePhotoContentDecision.Empty);
    }

    [Fact]
    public void Encoded_content_over_25_mebibytes_is_rejected_before_decoding()
    {
        var oversized = new byte[PrivatePhotoContentPolicy.MaxBytes + 1];
        oversized[0] = 0xFF;
        oversized[1] = 0xD8;
        oversized[2] = 0xFF;

        var result = CreateDecoder().Decode(oversized);

        AssertRejected(result, PrivatePhotoContentDecision.TooLarge);
    }

    [Fact]
    public void An_unknown_format_is_rejected()
    {
        var result = CreateDecoder().Decode(new byte[] { 0x00, 0x01, 0x02, 0x03 });

        AssertRejected(result, PrivatePhotoContentDecision.Unrecognized);
    }

    [Fact]
    public void A_signature_and_decoded_format_must_agree()
    {
        Assert.Equal(
            PrivatePhotoContentDecision.Inconsistent,
            SkiaPrivatePhotoDecoder.ConflictingFormat(PrivatePhotoFormat.Jpeg, SKEncodedImageFormat.Png));
        Assert.Null(SkiaPrivatePhotoDecoder.ConflictingFormat(PrivatePhotoFormat.WebP, SKEncodedImageFormat.Webp));
    }

    [Fact]
    public void A_valid_looking_signature_with_malformed_content_is_rejected()
    {
        var result = CreateDecoder().Decode(new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x00, 0x00 });

        AssertRejected(result, PrivatePhotoContentDecision.Malformed);
    }

    [Fact]
    public void A_truncated_image_is_rejected()
    {
        var encoded = Encode(SKEncodedImageFormat.Jpeg, 32, 32);
        var truncated = encoded[..^1];

        var result = CreateDecoder().Decode(truncated);

        AssertRejected(result, PrivatePhotoContentDecision.Truncated);
    }

    [Fact]
    public void Width_above_the_configured_maximum_is_rejected()
    {
        var result = CreateDecoder(maxWidth: 8).Decode(Encode(SKEncodedImageFormat.Png, 9, 1));

        AssertRejected(result, PrivatePhotoContentDecision.Dimensions);
    }

    [Fact]
    public void Height_above_the_configured_maximum_is_rejected()
    {
        var result = CreateDecoder(maxHeight: 8).Decode(Encode(SKEncodedImageFormat.Png, 1, 9));

        AssertRejected(result, PrivatePhotoContentDecision.Dimensions);
    }

    [Fact]
    public void Pixel_count_above_the_configured_maximum_is_rejected()
    {
        var result = CreateDecoder(maxWidth: 8, maxHeight: 8, maxPixels: 20)
            .Decode(Encode(SKEncodedImageFormat.Png, 5, 5));

        AssertRejected(result, PrivatePhotoContentDecision.Dimensions);
    }

    [Fact]
    public void Configuration_cannot_raise_the_hard_dimension_ceiling()
    {
        var limits = SkiaPrivatePhotoDecoder.EffectiveLimits(new PrivatePhotoDecodeOptions
        {
            MaxWidth = 1_000_000,
            MaxHeight = 0,
            MaxPixels = long.MaxValue,
        });

        Assert.Equal(PrivatePhotoContentPolicy.MaxLongEdgePixels, limits.MaxWidth);
        Assert.Equal(PrivatePhotoContentPolicy.MaxLongEdgePixels, limits.MaxHeight);
        Assert.Equal(PrivatePhotoContentPolicy.MaxDecodedPixels, limits.MaxPixels);
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(
            PrivatePhotoContentPolicy.MaxLongEdgePixels + 1,
            1,
            limits.MaxWidth,
            limits.MaxHeight,
            limits.MaxPixels));
    }

    private static SkiaPrivatePhotoDecoder CreateDecoder(int? maxWidth = null, int? maxHeight = null, long? maxPixels = null)
    {
        return new SkiaPrivatePhotoDecoder(Options.Create(new PrivatePhotoDecodeOptions
        {
            MaxWidth = maxWidth ?? PrivatePhotoContentPolicy.MaxLongEdgePixels,
            MaxHeight = maxHeight ?? PrivatePhotoContentPolicy.MaxLongEdgePixels,
            MaxPixels = maxPixels ?? PrivatePhotoContentPolicy.MaxDecodedPixels,
        }));
    }

    private static void AssertRejected(PrivatePhotoDecodeResult result, PrivatePhotoContentDecision decision)
    {
        var rejected = Assert.IsType<PrivatePhotoDecodeResult.Rejected>(result);
        Assert.Equal(decision, rejected.Decision);
        Assert.False(rejected.ImageDecodeVerified);
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        Assert.NotNull(data);
        var bytes = data.ToArray();
        Assert.NotEmpty(bytes);
        return bytes;
    }
}
