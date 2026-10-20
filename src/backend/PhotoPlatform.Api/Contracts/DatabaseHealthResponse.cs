namespace PhotoPlatform.Api.Contracts;

public sealed record DatabaseHealthResponse
{
    public required string Status { get; init; }

    public required DateTime Timestamp { get; init; }
}
