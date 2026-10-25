namespace PhotoPlatform.Application.Photos;

public interface IPrivatePhotoDecodeGate
{
    Task<PrivatePhotoDecodeResult> DecodeAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}
