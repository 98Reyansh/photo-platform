namespace PhotoPlatform.Application.Photos;

public sealed class PhotoOriginalPersistenceException : InvalidOperationException
{
    public const string SaveFailedMessage = "The photo could not be saved.";

    public PhotoOriginalPersistenceException(PhotoCommitClass outcome)
        : base(SaveFailedMessage)
    {
        Outcome = outcome;
    }

    public PhotoCommitClass Outcome { get; }
}
