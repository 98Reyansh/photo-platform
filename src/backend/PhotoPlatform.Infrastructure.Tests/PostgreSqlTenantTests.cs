using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class PostgreSqlTenantTests
{
    public const string ConnectionVariable = "PHOTO_PLATFORM_TEST_CONNECTION";

    [PostgreSqlFact]
    public async Task Database_is_reachable()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        await using var context = CreateContext(connectionString!);

        Assert.True(await context.Database.CanConnectAsync());
    }

    [PostgreSqlFact]
    public async Task Normalized_slug_is_unique_in_postgresql()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        await using var context = CreateContext(connectionString!);
        await context.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var slug = $"studio-{suffix}";
        var createdAtUtc = DateTime.UtcNow;

        context.Tenants.Add(new Tenant("Studio", slug, createdAtUtc));
        await context.SaveChangesAsync();

        context.Tenants.Add(new Tenant("Other Studio", slug.ToUpperInvariant(), createdAtUtc));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable)))
        {
            Skip = $"{PostgreSqlTenantTests.ConnectionVariable} is not set, so this PostgreSQL test was not run.";
        }
    }
}
