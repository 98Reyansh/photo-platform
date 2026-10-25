using PhotoPlatform.Application.Photos;

namespace PhotoPlatform.Api.Contracts;

public sealed record CreatePhotoRequest(string? Title, string? Description);

public sealed record UpdatePhotoRequest(string? Title, string? Description);

public sealed record PhotoListResponse(IReadOnlyList<PhotoResponse> Items, string? ContinuationCursor);
