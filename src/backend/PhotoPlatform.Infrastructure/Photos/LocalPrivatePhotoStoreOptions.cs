namespace PhotoPlatform.Infrastructure.Photos;

public sealed class LocalPrivatePhotoStoreOptions
{
    public const string SectionName = "PrivatePhotoStorage";

    public string? RootPath { get; set; }
}
