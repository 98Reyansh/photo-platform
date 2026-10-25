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

    public async Task<PhotoListResult> ListAsync(int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (DenyUnresolvedStudio() is not null)
        {
            return PhotoListResult.UnresolvedStudio();
        }

        if (!PhotoCursor.TryNormalizeLimit(limit, out var pageSize, out var limitError))
        {
            return PhotoListResult.Invalid(limitError!);
        }

        PhotoCursorPosition? position = null;
        if (cursor is not null)
        {
            if (!PhotoCursor.TryDecode(cursor, out var decoded, out var cursorError))
            {
                return PhotoListResult.Invalid(cursorError!);
            }

            position = decoded;
        }

        var rows = await PageQuery(context.Photos, pageSize, position).ToListAsync(cancellationToken);
        return PhotoListResult.Success(Slice(rows, pageSize));
    }

    internal static IQueryable<Photo> PageQuery(IQueryable<Photo> photos, int limit, PhotoCursorPosition? position)
    {
        if (position is PhotoCursorPosition cursor)
        {
            var createdAtUtc = cursor.CreatedAtUtc;
            var id = cursor.Id;
            photos = photos.Where(photo =>
                photo.CreatedAtUtc < createdAtUtc
                || (photo.CreatedAtUtc == createdAtUtc && photo.Id < id));
        }

        return photos
            .OrderByDescending(photo => photo.CreatedAtUtc)
            .ThenByDescending(photo => photo.Id)
            .Take(limit + 1);
    }

    internal static PhotoPage Slice(IReadOnlyList<Photo> rows, int limit)
    {
        var count = Math.Min(rows.Count, limit);
        var items = new PhotoResponse[count];
        for (var index = 0; index < count; index++)
        {
            items[index] = ToResponse(rows[index]);
        }

        string? continuation = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            continuation = PhotoCursor.Encode(last.CreatedAtUtc, last.Id);
        }

        return new PhotoPage(items, continuation);
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
