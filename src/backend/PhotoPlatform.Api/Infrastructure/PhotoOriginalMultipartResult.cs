namespace PhotoPlatform.Api.Infrastructure;

public abstract record PhotoOriginalMultipartResult
{
    private PhotoOriginalMultipartResult()
    {
    }

    public sealed record Ready(string? Title, string? Description, string SpoolPath) : PhotoOriginalMultipartResult;

    public sealed record Invalid(string Detail) : PhotoOriginalMultipartResult;

    public sealed record RequestTooLarge : PhotoOriginalMultipartResult;
}
