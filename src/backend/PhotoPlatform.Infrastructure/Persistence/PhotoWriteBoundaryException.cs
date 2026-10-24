namespace PhotoPlatform.Infrastructure.Persistence;

public sealed class PhotoWriteBoundaryException : InvalidOperationException
{
    public const string UnresolvedMessage = "A photo cannot be saved without a resolved studio.";

    public const string MismatchMessage = "The photo write does not belong to the resolved studio.";

    public PhotoWriteBoundaryException(string message)
        : base(message)
    {
    }
}
