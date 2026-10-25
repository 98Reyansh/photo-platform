using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Application.Photos;

public sealed class PhotoOriginalUpload(
    ICurrentStudio studio,
    IPrivatePhotoStore store,
    IPhotoOriginalRecords records,
    IPhotoCommitClassifier commits,
    IPhotoUploadDiagnostics diagnostics) : IPhotoOriginalUpload
{
    public async Task<PhotoOriginalUploadResult> UploadAsync(
        string? title,
        string? description,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!studio.IsResolved || studio.TenantId == Guid.Empty)
        {
            return new PhotoOriginalUploadResult.UnresolvedStudio();
        }

        Photo photo;
        try
        {
            photo = new Photo(studio.TenantId, title ?? string.Empty, description, DateTime.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return new PhotoOriginalUploadResult.Invalid(exception.Message);
        }

        PrivatePhotoStoreResult stored;
        try
        {
            stored = await store.StoreOriginalAsync(photo.TenantId, photo.Id, content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PrivatePhotoStorageException)
        {
            return new PhotoOriginalUploadResult.NotStored();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            if (stored is PrivatePhotoStoreResult.Stored)
            {
                await DeleteStoredObjectAsync(photo);
            }

            throw new OperationCanceledException(cancellationToken);
        }

        if (stored is not PrivatePhotoStoreResult.Stored)
        {
            var rejected = (PrivatePhotoStoreResult.Rejected)stored;
            return new PhotoOriginalUploadResult.Rejected(rejected.Decision);
        }

        try
        {
            await records.SaveNewAsync(photo, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            diagnostics.PersistenceOutcome(
                PhotoCommitClass.Uncertain,
                exception.GetType().Name,
                photo.TenantId,
                photo.Id);
            throw;
        }
        catch (Exception exception)
        {
            var outcome = commits.Classify(exception);
            diagnostics.PersistenceOutcome(outcome, exception.GetType().Name, photo.TenantId, photo.Id);
            if (outcome == PhotoCommitClass.ConfirmedRollback)
            {
                await DeleteStoredObjectAsync(photo);
            }

            throw new PhotoOriginalPersistenceException(outcome);
        }

        return new PhotoOriginalUploadResult.Created(new PhotoResponse(
            photo.Id,
            photo.TenantId,
            photo.Title,
            photo.Description,
            photo.CreatedAtUtc,
            photo.UpdatedAtUtc));
    }

    private async Task DeleteStoredObjectAsync(Photo photo)
    {
        try
        {
            await store.DeleteOriginalAsync(photo.TenantId, photo.Id, CancellationToken.None);
        }
        catch (Exception cleanup)
        {
            diagnostics.CleanupFailed(cleanup.GetType().Name, photo.TenantId, photo.Id);
        }
    }
}
