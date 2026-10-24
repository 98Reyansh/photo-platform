using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class PhotoCommands(ApplicationDbContext context, ICurrentStudio studio) : IPhotoCommands
{
    public async Task<PhotoCommandResult> CreateAsync(
        string? title,
        string? description,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnresolvedStudio();
        if (denied is not null)
        {
            return denied;
        }

        Photo photo;
        try
        {
            photo = new Photo(studio.TenantId, title!, description, DateTime.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return PhotoCommandResult.Invalid(exception.Message);
        }

        context.Photos.Add(photo);
        await context.SaveChangesAsync(cancellationToken);
        return PhotoCommandResult.Success(ToResponse(photo));
    }

    public async Task<PhotoCommandResult> GetAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var denied = DenyUnresolvedStudio();
        if (denied is not null)
        {
            return denied;
        }

        var photo = await context.Photos.SingleOrDefaultAsync(item => item.Id == photoId, cancellationToken);
        return photo is null
            ? PhotoCommandResult.NotFound()
            : PhotoCommandResult.Success(ToResponse(photo));
    }

    public async Task<PhotoCommandResult> UpdateAsync(
        Guid photoId,
        string? title,
        string? description,
        CancellationToken cancellationToken)
    {
        var denied = DenyUnresolvedStudio();
        if (denied is not null)
        {
            return denied;
        }

        var photo = await context.Photos.SingleOrDefaultAsync(item => item.Id == photoId, cancellationToken);
        if (photo is null)
        {
            return PhotoCommandResult.NotFound();
        }

        try
        {
            photo.UpdateDetails(title!, description, DateTime.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return PhotoCommandResult.Invalid(exception.Message);
        }

        await context.SaveChangesAsync(cancellationToken);
        return PhotoCommandResult.Success(ToResponse(photo));
    }

    public async Task<PhotoCommandResult> DeleteAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var denied = DenyUnresolvedStudio();
        if (denied is not null)
        {
            return denied;
        }

        var photo = await context.Photos.SingleOrDefaultAsync(item => item.Id == photoId, cancellationToken);
        if (photo is null)
        {
            return PhotoCommandResult.NotFound();
        }

        context.Photos.Remove(photo);
        await context.SaveChangesAsync(cancellationToken);
        return PhotoCommandResult.Success(photo: null);
    }

    private PhotoCommandResult? DenyUnresolvedStudio()
    {
        if (studio.IsResolved && studio.TenantId != Guid.Empty)
        {
            return null;
        }

        return PhotoCommandResult.UnresolvedStudio();
    }

    private static PhotoResponse ToResponse(Photo photo)
    {
        return new PhotoResponse(
            photo.Id,
            photo.TenantId,
            photo.Title,
            photo.Description,
            photo.CreatedAtUtc,
            photo.UpdatedAtUtc);
    }
}
