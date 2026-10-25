namespace PhotoPlatform.Application.Authentication;

public static class StudioBootstrapEnvironment
{
    public const string StudioNameVariable = "PHOTO_PLATFORM_BOOTSTRAP_STUDIO_NAME";

    public const string StudioSlugVariable = "PHOTO_PLATFORM_BOOTSTRAP_STUDIO_SLUG";

    public const string OwnerEmailVariable = "PHOTO_PLATFORM_BOOTSTRAP_OWNER_EMAIL";

    public const string OwnerPasswordVariable = "PHOTO_PLATFORM_BOOTSTRAP_OWNER_PASSWORD";

    public static bool TryCreateRequest(
        Func<string, string?> read,
        out StudioBootstrapRequest? request,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(read);
        var missing = new List<string>();
        var studioName = Required(read, StudioNameVariable, missing);
        var studioSlug = Required(read, StudioSlugVariable, missing);
        var ownerEmail = Required(read, OwnerEmailVariable, missing);
        var ownerPassword = Required(read, OwnerPasswordVariable, missing);
        if (missing.Count > 0)
        {
            request = null;
            message = "Bootstrap is missing required environment variables: " + string.Join(", ", missing) + ".";
            return false;
        }

        request = new StudioBootstrapRequest(studioName, studioSlug, ownerEmail, ownerPassword);
        message = string.Empty;
        return true;
    }

    private static string Required(Func<string, string?> read, string name, List<string> missing)
    {
        var value = read(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(name);
            return string.Empty;
        }

        return name == OwnerPasswordVariable ? value : value.Trim();
    }
}
