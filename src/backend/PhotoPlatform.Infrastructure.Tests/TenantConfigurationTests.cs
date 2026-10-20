using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Persistence.Configurations;

namespace PhotoPlatform.Infrastructure.Tests;

public class TenantConfigurationTests
{
    [Fact]
    public void Tenant_mapping_requires_fields_and_unique_normalized_slug()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Tenant));

        Assert.NotNull(entity);
        Assert.Equal("Tenants", entity.GetTableName());

        var id = entity.FindProperty(nameof(Tenant.Id));
        Assert.NotNull(id);
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);

        AssertRequired(entity, nameof(Tenant.Name), Tenant.NameMaxLength);
        AssertRequired(entity, nameof(Tenant.Slug), Tenant.SlugMaxLength);
        AssertRequired(entity, nameof(Tenant.Status), 32);
        AssertRequired(entity, nameof(Tenant.CreatedAtUtc), maxLength: null);
        AssertRequired(entity, nameof(Tenant.UpdatedAtUtc), maxLength: null);

        var slugIndex = entity.GetIndexes().Single(index =>
            index.Properties.Count == 1 && index.Properties[0].Name == nameof(Tenant.Slug));
        Assert.True(slugIndex.IsUnique);

        var statusConstraint = entity.GetCheckConstraints()
            .Single(constraint => constraint.Name == TenantConfiguration.StatusCheckConstraintName);
        Assert.Contains("'Active'", statusConstraint.Sql, StringComparison.Ordinal);
        Assert.Contains("'Suspended'", statusConstraint.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_migration_is_present()
    {
        using var context = CreateContext();

        Assert.Contains(
            "20261020135016_InitialTenants",
            context.Database.GetMigrations());
    }

    [Fact]
    public void Missing_connection_string_has_a_clear_error_without_credentials()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            InfrastructureServiceCollectionExtensions.GetRequiredConnectionString(configuration));

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_connection_string_is_returned_to_the_caller()
    {
        const string configured = "Host=localhost;Database=photoplatform;Username=photoplatform";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}"] = configured,
            })
            .Build();

        var connectionString = InfrastructureServiceCollectionExtensions.GetRequiredConnectionString(configuration);

        Assert.Equal(configured, connectionString);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=photoplatform;Username=photoplatform")
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void AssertRequired(IEntityType entity, string propertyName, int? maxLength)
    {
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.False(property.IsNullable);

        if (maxLength is not null)
        {
            Assert.Equal(maxLength, property.GetMaxLength());
        }
    }
}
