using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var context = CreateContext(connectionString!);

        Assert.True(await context.Database.CanConnectAsync());
    }

    [PostgreSqlFact]
    public async Task Normalized_slug_is_unique_in_postgresql()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        await ExecuteUniqueSlugCheck(
            connectionString,
            context => context.Database.MigrateAsync());
    }

    internal static async Task ExecuteUniqueSlugCheck(
        string? connectionString,
        Func<ApplicationDbContext, Task> migrate)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var context = CreateContext(connectionString!);
        await migrate(context);

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
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}

public static class PostgreSqlTestGuard
{
    public const string RequiredDatabaseName = "photo_platform_test";

    public static void EnsureSafeToUse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{PostgreSqlTenantTests.ConnectionVariable} is not set.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                $"{PostgreSqlTenantTests.ConnectionVariable} is not a valid PostgreSQL connection string.");
        }

        if (!string.Equals(builder.Database, RequiredDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL tests only allow the database name '{RequiredDatabaseName}'.");
        }
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
