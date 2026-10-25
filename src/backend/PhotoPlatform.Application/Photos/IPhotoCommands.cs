namespace PhotoPlatform.Application.Photos;

public interface IPhotoCommands
{
    Task<PhotoCommandResult> CreateAsync(string? title, string? description, CancellationToken cancellationToken);

    Task<PhotoCommandResult> GetAsync(Guid photoId, CancellationToken cancellationToken);

    Task<PhotoCommandResult> UpdateAsync(
        Guid photoId,
        string? title,
        string? description,
        CancellationToken cancellationToken);

    Task<PhotoCommandResult> DeleteAsync(Guid photoId, CancellationToken cancellationToken);

    Task<PhotoListResult> ListAsync(int? limit, string? cursor, CancellationToken cancellationToken);
}
