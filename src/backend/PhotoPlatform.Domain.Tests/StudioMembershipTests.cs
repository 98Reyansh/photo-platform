using PhotoPlatform.Domain.Membership;

namespace PhotoPlatform.Domain.Tests;

public class StudioMembershipTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Membership_binds_one_user_to_one_tenant()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();

        var membership = new StudioMembership(userId, tenantId, CreatedAtUtc);

        Assert.Equal(userId, membership.UserId);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal(CreatedAtUtc, membership.CreatedAtUtc);
        Assert.NotEqual(Guid.Empty, membership.Id);
    }

    [Fact]
    public void Empty_user_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new StudioMembership(Guid.Empty, Guid.CreateVersion7(), CreatedAtUtc));

        Assert.Equal("userId", exception.ParamName);
    }

    [Fact]
    public void Empty_tenant_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new StudioMembership(Guid.CreateVersion7(), Guid.Empty, CreatedAtUtc));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void Non_utc_timestamp_is_rejected()
    {
        var local = DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Local);

        var exception = Assert.Throws<ArgumentException>(() =>
            new StudioMembership(Guid.CreateVersion7(), Guid.CreateVersion7(), local));

        Assert.Equal("createdAtUtc", exception.ParamName);
    }
}
