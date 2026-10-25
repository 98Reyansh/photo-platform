using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Application.Photos;

public abstract record PhotoOriginalUploadResult
{
    private PhotoOriginalUploadResult()
    {
    }

    public sealed record Created(PhotoResponse Photo) : PhotoOriginalUploadResult;

    public sealed record UnresolvedStudio : PhotoOriginalUploadResult;

    public sealed record Invalid(string Message) : PhotoOriginalUploadResult;

    public sealed record Rejected(PrivatePhotoContentDecision Decision) : PhotoOriginalUploadResult;

    public sealed record NotStored : PhotoOriginalUploadResult;
}
