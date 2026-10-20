namespace PhotoPlatform.Infrastructure.Tests;

public class PostgreSqlTestGuardTests
{
    [Fact]
    public void Accepts_only_the_exact_test_database_name()
    {
        var exception = Record.Exception(() =>
            PostgreSqlTestGuard.EnsureSafeToUse(
                "Host=localhost;Username=photoplatform;Database=photo_platform_test"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Host=localhost;Username=photoplatform;Password=should-not-leak;Database=photoplatform")]
    [InlineData("Host=localhost;Username=photoplatform;Password=should-not-leak;Database=photo_platform_test_extra")]
    [InlineData("Host=localhost;Username=photoplatform;Password=should-not-leak;Database=PHOTO_PLATFORM_TEST")]
    [InlineData("Host=localhost;Username=photoplatform;Password=should-not-leak")]
    public async Task Unsafe_database_name_is_rejected_before_migration(string connectionString)
    {
        var migrated = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgreSqlTenantTests.ExecuteUniqueSlugCheck(
                connectionString,
                _ =>
                {
                    migrated = true;
                    return Task.CompletedTask;
                }));

        Assert.False(migrated);
        Assert.DoesNotContain("should-not-leak", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Malformed_connection_string_is_rejected_without_disclosing_it()
    {
        const string connectionString = "Host=localhost;Port=not-a-port;Password=should-not-leak;Database=photo_platform_test";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgreSqlTestGuard.EnsureSafeToUse(connectionString));

        Assert.DoesNotContain("should-not-leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }
}
