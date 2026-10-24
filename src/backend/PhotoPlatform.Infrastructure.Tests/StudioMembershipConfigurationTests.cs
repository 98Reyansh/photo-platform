using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class StudioMembershipConfigurationTests
{
    [Fact]
    public void Membership_maps_one_user_to_one_tenant()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StudioMembership));

        Assert.NotNull(entity);
        Assert.Equal("StudioMemberships", entity.GetTableName());

        var userIndex = entity.GetIndexes().Single(index =>
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(StudioMembership.UserId));
        Assert.True(userIndex.IsUnique);

        var tenant = entity.GetForeignKeys().Single(key =>
            key.Properties.Count == 1 && key.Properties[0].Name == nameof(StudioMembership.TenantId));
        Assert.Equal(DeleteBehavior.Restrict, tenant.DeleteBehavior);
        Assert.Equal("Tenants", tenant.PrincipalEntityType.GetTableName());
        Assert.Contains("20261024110924_AddStudioMembership", context.Database.GetMigrations());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=photoplatform;Username=photoplatform")
            .Options;

        return new ApplicationDbContext(options);
    }
}
