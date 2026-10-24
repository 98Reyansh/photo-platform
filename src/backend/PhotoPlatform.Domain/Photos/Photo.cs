namespace PhotoPlatform.Domain.Photos;

public sealed class Photo
{
    public const int TitleMaxLength = 200;

    public const int DescriptionMaxLength = 2_000;

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private Photo()
    {
    }

    public Photo(Guid tenantId, string title, string? description, DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        Id = Guid.CreateVersion7(new DateTimeOffset(createdAtUtc));
        TenantId = tenantId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public void UpdateDetails(string title, string? description, DateTime updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);

        Title = normalizedTitle;
        Description = normalizedDescription;
        UpdatedAtUtc = updatedAtUtc;
    }

    public static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var normalized = title.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (normalized.Length > TitleMaxLength)
        {
            throw new ArgumentException($"Title must be at most {TitleMaxLength} characters.", nameof(title));
        }

        return normalized;
    }

    public static string? NormalizeDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var normalized = description.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Description must be at most {DescriptionMaxLength} characters.",
                nameof(description));
        }

        return normalized;
    }

    private static void EnsureUtc(DateTime timestamp, string parameterName)
    {
        if (timestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.", parameterName);
        }
    }
}
