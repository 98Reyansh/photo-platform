namespace PhotoPlatform.Application.Photos;

public enum PhotoCommandStatus
{
    Success = 1,
    UnresolvedStudio = 2,
    NotFound = 3,
    Invalid = 4,
}

public sealed record PhotoCommandResult(PhotoCommandStatus Status, PhotoResponse? Photo, string? Error)
{
    public static PhotoCommandResult Success(PhotoResponse? photo) => new(PhotoCommandStatus.Success, photo, null);

    public static PhotoCommandResult UnresolvedStudio() => new(PhotoCommandStatus.UnresolvedStudio, null, null);

    public static PhotoCommandResult NotFound() => new(PhotoCommandStatus.NotFound, null, null);

    public static PhotoCommandResult Invalid(string error) => new(PhotoCommandStatus.Invalid, null, error);
}
