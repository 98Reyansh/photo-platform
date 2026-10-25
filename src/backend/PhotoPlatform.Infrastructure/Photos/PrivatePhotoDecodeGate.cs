using PhotoPlatform.Application.Photos;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class PrivatePhotoDecodeGate(IPrivatePhotoDecoder decoder) : IPrivatePhotoDecodeGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PrivatePhotoDecodeResult> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return decoder.Decode(content);
        }
        finally
        {
            _gate.Release();
        }
    }
}
