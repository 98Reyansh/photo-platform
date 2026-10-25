using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class PrivatePhotoDecodeOptions
{
    public const string SectionName = "PrivatePhotoDecode";

    public int MaxWidth { get; set; } = PrivatePhotoContentPolicy.MaxLongEdgePixels;

    public int MaxHeight { get; set; } = PrivatePhotoContentPolicy.MaxLongEdgePixels;

    public long MaxPixels { get; set; } = PrivatePhotoContentPolicy.MaxDecodedPixels;
}
