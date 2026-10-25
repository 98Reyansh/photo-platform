using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Application.Photos;

public interface IPrivatePhotoDecoder
{
    PrivatePhotoDecodeResult Decode(ReadOnlyMemory<byte> content);
}

public abstract record PrivatePhotoDecodeResult
{
    private PrivatePhotoDecodeResult()
    {
    }

    public abstract bool ImageDecodeVerified { get; }

    public sealed record Verified(PrivatePhotoFormat Format, int Width, int Height) : PrivatePhotoDecodeResult
    {
        public override bool ImageDecodeVerified => true;
    }

    public sealed record Rejected(PrivatePhotoContentDecision Decision) : PrivatePhotoDecodeResult
    {
        public override bool ImageDecodeVerified => false;
    }
}
