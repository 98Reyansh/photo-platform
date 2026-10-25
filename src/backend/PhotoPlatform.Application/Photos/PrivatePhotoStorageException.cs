namespace PhotoPlatform.Application.Photos;

public sealed class PrivatePhotoStorageException : InvalidOperationException
{
    public const string InvalidIdentityMessage = "The photo storage identity is invalid.";

    public const string RootMessage = "The private photo storage root is not available.";

    public const string LocationMessage = "The private photo location is not available.";

    public const string StoreFailedMessage = "The private photo could not be stored.";

    public const string ReadFailedMessage = "The private photo could not be read.";

    public const string DeleteFailedMessage = "The private photo could not be deleted.";

    public PrivatePhotoStorageException(string message)
        : base(message)
    {
    }
}
