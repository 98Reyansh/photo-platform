namespace PhotoPlatform.Application.Authentication;

public sealed record StudioBootstrapRequest(string StudioName, string StudioSlug, string OwnerEmail, string OwnerPassword);
