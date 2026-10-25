using Microsoft.Extensions.Logging;
using PhotoPlatform.Application.Photos;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class PhotoUploadDiagnostics(ILogger<PhotoUploadDiagnostics> logger) : IPhotoUploadDiagnostics
{
    public void PersistenceOutcome(PhotoCommitClass outcome, string exceptionType, Guid tenantId, Guid photoId)
    {
        logger.LogError(
            "Photo original persistence outcome {Outcome}. ExceptionType: {ExceptionType}. TenantId: {TenantId}. PhotoId: {PhotoId}.",
            outcome,
            exceptionType,
            tenantId,
            photoId);
    }

    public void CleanupFailed(string exceptionType, Guid tenantId, Guid photoId)
    {
        logger.LogError(
            "Photo original cleanup failed. ExceptionType: {ExceptionType}. TenantId: {TenantId}. PhotoId: {PhotoId}.",
            exceptionType,
            tenantId,
            photoId);
    }
}
