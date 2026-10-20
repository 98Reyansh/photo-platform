using System.Text.RegularExpressions;

namespace PhotoPlatform.Domain.Entities;

public sealed partial class Tenant
{
    public const int NameMaxLength = 200;
    public const int SlugMaxLength = 80;

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private Tenant()
    {
    }

    public Tenant(string name, string slug, DateTime createdAtUtc)
    {
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.", nameof(createdAtUtc));
        }

        Name = NormalizeName(name);
        Slug = NormalizeSlug(slug);
        Id = Guid.CreateVersion7(new DateTimeOffset(createdAtUtc));
        Status = TenantStatus.Active;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var normalized = name.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (normalized.Length > NameMaxLength)
        {
            throw new ArgumentException($"Name must be at most {NameMaxLength} characters.", nameof(name));
        }

        return normalized;
    }

    public static string NormalizeSlug(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var normalized = slug.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Slug is required.", nameof(slug));
        }

        if (normalized.Length > SlugMaxLength)
        {
            throw new ArgumentException($"Slug must be at most {SlugMaxLength} characters.", nameof(slug));
        }

        if (!SlugPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Slug must contain only lowercase letters, digits, and single hyphens.",
                nameof(slug));
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
