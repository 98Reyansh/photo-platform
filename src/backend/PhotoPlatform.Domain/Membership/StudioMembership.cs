namespace PhotoPlatform.Domain.Membership;

public sealed class StudioMembership
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private StudioMembership()
    {
    }

    public StudioMembership(Guid userId, Guid tenantId, DateTime createdAtUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.", nameof(createdAtUtc));
        }

        Id = Guid.CreateVersion7(new DateTimeOffset(createdAtUtc));
        UserId = userId;
        TenantId = tenantId;
        CreatedAtUtc = createdAtUtc;
    }
}
