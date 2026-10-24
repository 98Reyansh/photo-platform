namespace PhotoPlatform.Application.Photos;

public sealed record PhotoResponse(
    Guid Id,
    Guid TenantId,
    string Title,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
