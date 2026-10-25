using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Application.Photos;

public interface IPhotoOriginalRecords
{
    Task SaveNewAsync(Photo photo, CancellationToken cancellationToken);
}
