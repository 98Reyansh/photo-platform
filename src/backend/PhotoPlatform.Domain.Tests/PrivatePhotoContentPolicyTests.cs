using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Domain.Tests;

public class PrivatePhotoContentPolicyTests
{
    [Fact]
    public void Empty_content_is_rejected()
    {
        var verdict = PrivatePhotoContentPolicy.Evaluate(0, ReadOnlySpan<byte>.Empty);

        Assert.Equal(PrivatePhotoContentDecision.Empty, verdict.Decision);
        Assert.Null(verdict.Format);
        Assert.False(verdict.ImageDecodeVerified);
    }

    [Fact]
    public void Content_over_25_mebibytes_is_rejected_before_a_signature_can_pass()
    {
        var verdict = PrivatePhotoContentPolicy.Evaluate(
            PrivatePhotoContentPolicy.MaxBytes + 1,
            [0xFF, 0xD8, 0xFF]);

        Assert.Equal(26_214_400, PrivatePhotoContentPolicy.MaxBytes);
        Assert.Equal(PrivatePhotoContentDecision.TooLarge, verdict.Decision);
        Assert.Null(verdict.Format);
        Assert.False(verdict.ImageDecodeVerified);
    }

    [Theory]
    [MemberData(nameof(RecognizedSignatures))]
    public void Recognized_signatures_are_not_decoded_images(byte[] prefix, PrivatePhotoFormat format)
    {
        var verdict = PrivatePhotoContentPolicy.Evaluate(prefix.Length, prefix);

        Assert.Equal(PrivatePhotoContentDecision.SignatureRecognized, verdict.Decision);
        Assert.Equal(format, verdict.Format);
        Assert.False(verdict.ImageDecodeVerified);
        Assert.Equal(40_000_000, PrivatePhotoContentPolicy.MaxDecodedPixels);
        Assert.Equal(12_000, PrivatePhotoContentPolicy.MaxLongEdgePixels);
    }

    [Theory]
    [MemberData(nameof(MalformedContent))]
    public void Malformed_or_unknown_bytes_are_unrecognized(byte[] content)
    {
        var verdict = PrivatePhotoContentPolicy.Evaluate(content.Length, content);

        Assert.Equal(PrivatePhotoContentDecision.Unrecognized, verdict.Decision);
        Assert.Null(verdict.Format);
        Assert.False(verdict.ImageDecodeVerified);
    }

    public static TheoryData<byte[], PrivatePhotoFormat> RecognizedSignatures()
    {
        return new TheoryData<byte[], PrivatePhotoFormat>
        {
            { [0xFF, 0xD8, 0xFF, 0xD9], PrivatePhotoFormat.Jpeg },
            { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], PrivatePhotoFormat.Png },
            { [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50], PrivatePhotoFormat.WebP },
        };
    }

    [Fact]
    public void Dimension_limits_use_overflow_safe_arithmetic()
    {
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(0, 1, 12_000, 12_000, 40_000_000));
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(1, -1, 12_000, 12_000, 40_000_000));
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(12_001, 1, 12_000, 12_000, 40_000_000));
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(1, 12_001, 12_000, 12_000, 40_000_000));
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(8_000, 8_000, 12_000, 12_000, 40_000_000));
        Assert.True(PrivatePhotoContentPolicy.TryAcceptDimensions(2, 2, 12_000, 12_000, 40_000_000));
        Assert.False(PrivatePhotoContentPolicy.TryAcceptDimensions(
            int.MaxValue,
            int.MaxValue,
            12_000,
            12_000,
            40_000_000));
    }

    public static TheoryData<byte[]> MalformedContent()
    {
        return new TheoryData<byte[]>
        {
            new byte[] { 0xFF, 0xD8 },
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A },
            new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x58 },
            new byte[] { 0x00, 0x01, 0x02, 0x03 },
        };
    }
}
