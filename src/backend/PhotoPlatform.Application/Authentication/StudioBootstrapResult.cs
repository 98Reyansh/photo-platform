namespace PhotoPlatform.Application.Authentication;

public enum StudioBootstrapStatus
{
    Completed,
    InvalidConfiguration,
    InvalidStudio,
    AlreadyBootstrapped,
    Failed,
}

public sealed record StudioBootstrapResult(
    StudioBootstrapStatus Status,
    string Message,
    Guid? StudioId,
    string? StudioSlug,
    string? OwnerEmail,
    string? Diagnostic = null)
{
    public bool Succeeded => Status == StudioBootstrapStatus.Completed;
}
