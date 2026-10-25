namespace PhotoPlatform.Application.Photos;

public interface IPhotoOriginalUpload
{
    Task<PhotoOriginalUploadResult> UploadAsync(
        string? title,
        string? description,
        Stream content,
        CancellationToken cancellationToken);
}
