namespace PhotoPlatform.Application.Photos;

public interface IPhotoUploadDiagnostics
{
    void PersistenceOutcome(PhotoCommitClass outcome, string exceptionType, Guid tenantId, Guid photoId);

    void CleanupFailed(string exceptionType, Guid tenantId, Guid photoId);
}
