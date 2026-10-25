using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Application.Photos;

public interface IPrivatePhotoStore
{
    Task<PrivatePhotoStoreResult> StoreOriginalAsync(
        Guid tenantId,
        Guid photoId,
        Stream content,
        CancellationToken cancellationToken);

    Task<PrivatePhotoContent?> OpenOriginalAsync(
        Guid tenantId,
        Guid photoId,
        CancellationToken cancellationToken);

    Task DeleteOriginalAsync(
        Guid tenantId,
        Guid photoId,
        CancellationToken cancellationToken);
}

public abstract record PrivatePhotoStoreResult
{
    private PrivatePhotoStoreResult()
    {
    }

    public abstract bool ImageDecodeVerified { get; }

    public sealed record Stored(PrivatePhotoFormat Format, long ByteCount, int Width, int Height) : PrivatePhotoStoreResult
    {
        public override bool ImageDecodeVerified => true;
    }

    public sealed record Rejected(PrivatePhotoContentDecision Decision) : PrivatePhotoStoreResult
    {
        public override bool ImageDecodeVerified => false;
    }
}

public sealed class PrivatePhotoContent : IAsyncDisposable, IDisposable
{
    private readonly Stream _body;

    public PrivatePhotoContent(Stream body, PrivatePhotoFormat format, long byteCount, bool imageDecodeVerified)
    {
        ArgumentNullException.ThrowIfNull(body);
        _body = body;
        Format = format;
        ByteCount = byteCount;
        ImageDecodeVerified = imageDecodeVerified;
    }

    public Stream Body => _body;

    public PrivatePhotoFormat Format { get; }

    public long ByteCount { get; }

    public bool ImageDecodeVerified { get; }

    public void Dispose()
    {
        _body.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _body.DisposeAsync();
    }
}
