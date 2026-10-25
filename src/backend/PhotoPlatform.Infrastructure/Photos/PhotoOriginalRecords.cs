using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class PhotoOriginalRecords(ApplicationDbContext context) : IPhotoOriginalRecords
{
    public async Task SaveNewAsync(Photo photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        context.Photos.Add(photo);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            context.Entry(photo).State = EntityState.Detached;
            throw;
        }
    }
}
