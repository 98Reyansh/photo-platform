using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoConfigurationTests
{
    [Fact]
    public void Photo_mapping_stores_private_metadata_for_one_tenant()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Photo));

        Assert.NotNull(entity);
        Assert.Equal("Photos", entity.GetTableName());
        Assert.Equal(
            ["CreatedAtUtc", "Description", "Id", "TenantId", "Title", "UpdatedAtUtc"],
            entity.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var id = entity.FindProperty(nameof(Photo.Id));
        Assert.NotNull(id);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        Assert.Equal("uuid", id.GetColumnType());

        var tenantId = entity.FindProperty(nameof(Photo.TenantId));
        Assert.NotNull(tenantId);
        Assert.False(tenantId.IsNullable);
        Assert.Equal("uuid", tenantId.GetColumnType());

        AssertRequired(entity, nameof(Photo.Title), Photo.TitleMaxLength);

        var description = entity.FindProperty(nameof(Photo.Description));
        Assert.NotNull(description);
        Assert.True(description.IsNullable);
        Assert.Equal(Photo.DescriptionMaxLength, description.GetMaxLength());

        AssertTimestamp(entity, nameof(Photo.CreatedAtUtc));
        AssertTimestamp(entity, nameof(Photo.UpdatedAtUtc));

        var tenant = entity.GetForeignKeys().Single(key =>
            key.Properties.Count == 1 && key.Properties[0].Name == nameof(Photo.TenantId));
        Assert.Equal(DeleteBehavior.Restrict, tenant.DeleteBehavior);
        Assert.Equal(typeof(Tenant), tenant.PrincipalEntityType.ClrType);

        var tenantIndex = entity.GetIndexes().Single(index =>
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(Photo.TenantId));
        Assert.False(tenantIndex.IsUnique);

        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Empty(context.Model.FindEntityType(typeof(Tenant))!.GetDeclaredQueryFilters());
        Assert.Empty(context.Model.FindEntityType(typeof(StudioMembership))!.GetDeclaredQueryFilters());
        Assert.Contains(
            context.Database.GetMigrations(),
            name => name.EndsWith("_AddPrivatePhotoMetadata", StringComparison.Ordinal));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=photoplatform;Username=photoplatform")
            .Options;

        return new ApplicationDbContext(options, new CurrentStudio());
    }

    private static void AssertRequired(IEntityType entity, string propertyName, int maxLength)
    {
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.False(property.IsNullable);
        Assert.Equal(maxLength, property.GetMaxLength());
    }

    private static void AssertTimestamp(IEntityType entity, string propertyName)
    {
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.False(property.IsNullable);
        Assert.Equal("timestamp with time zone", property.GetColumnType());
    }
}
