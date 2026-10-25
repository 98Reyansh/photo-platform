namespace PhotoPlatform.Application.Photos;

public sealed record PhotoPage(IReadOnlyList<PhotoResponse> Items, string? ContinuationCursor);

public sealed record PhotoListResult(PhotoCommandStatus Status, PhotoPage? Page, string? Error)
{
    public static PhotoListResult Success(PhotoPage page) => new(PhotoCommandStatus.Success, page, null);

    public static PhotoListResult UnresolvedStudio() => new(PhotoCommandStatus.UnresolvedStudio, null, null);

    public static PhotoListResult Invalid(string error) => new(PhotoCommandStatus.Invalid, null, error);
}
