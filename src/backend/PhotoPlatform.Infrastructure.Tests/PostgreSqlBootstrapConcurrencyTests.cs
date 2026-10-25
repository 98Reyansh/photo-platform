using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Infrastructure.Authentication;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

// SQLite bootstrap tests do not take pg_advisory_xact_lock and do not prove this race.
[Collection("PostgreSqlBootstrapConcurrency")]
public sealed class PostgreSqlBootstrapConcurrencyTests
{
    [Fact]
    public void Concurrency_harness_rejects_other_databases_before_connecting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BootstrapConcurrencyHarness.RequireApprovedDatabase(
                "Host=localhost;Username=photoplatform;Password=should-not-leak;Database=photoplatform"));

        Assert.DoesNotContain("should-not-leak", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", exception.Message, StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task Overlapping_bootstraps_with_different_identities_leave_one_studio()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await BootstrapConcurrencyHarness.ExecuteAsync(connectionString);
    }
}

internal static class BootstrapConcurrencyHarness
{
    private const string Password = "Abcd-1234567";

    public static void RequireApprovedDatabase(string? connectionString)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
    }

    public static async Task ExecuteAsync(string? connectionString)
    {
        RequireApprovedDatabase(connectionString);
        var approved = connectionString ?? throw new InvalidOperationException("The approved test connection is required.");
        await MigrateAsync(approved);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var slugA = $"bootstrap-{suffix}-a";
        var slugB = $"bootstrap-{suffix}-b";
        var emailA = $"owner-a-{suffix}@example.test";
        var emailB = $"owner-b-{suffix}@example.test";
        var slugs = new[] { slugA, slugB };
        var emails = new[] { emailA, emailB };
        ServiceProvider? services = null;
        NpgsqlConnection? gate = null;
        NpgsqlTransaction? gateTransaction = null;
        var gateReleased = false;
        var resultsObserved = false;
        Task<StudioBootstrapResult>? first = null;
        Task<StudioBootstrapResult>? second = null;
        try
        {
            var before = await CountsAsync(approved);
            if (before != (0, 0, 0, 0))
            {
                throw new InvalidOperationException(
                    "The concurrency test did not start because photo_platform_test already contains tenant, user, membership, or photo rows. Those rows were not deleted.");
            }

            services = CreateServices(approved);
            gate = new NpgsqlConnection(approved);
            await gate.OpenAsync();
            gateTransaction = await gate.BeginTransactionAsync();
            await TakeLockAsync(gate, gateTransaction);

            first = RunAsync(services, new StudioBootstrapRequest($"Studio {suffix} A", slugA, emailA, Password));
            second = RunAsync(services, new StudioBootstrapRequest($"Studio {suffix} B", slugB, emailB, Password));
            var waitingSessions = await WaitForWaitersAsync(approved, TimeSpan.FromSeconds(20));
            Assert.True(waitingSessions >= 2);
            await gateTransaction.RollbackAsync();
            gateReleased = true;

            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60));
            resultsObserved = true;
            Assert.Equal(1, results.Count(result => result.Succeeded));
            Assert.Equal(1, results.Count(result => result.Status == StudioBootstrapStatus.AlreadyBootstrapped));
            foreach (var result in results)
            {
                Assert.DoesNotContain(Password, result.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Host=", result.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("Password=", result.Message, StringComparison.Ordinal);
            }

            var after = await CountsAsync(approved);
            Assert.Equal((1, 1, 1, 0), after);
        }
        finally
        {
            if (gateTransaction is not null && !gateReleased)
            {
                try
                {
                    await gateTransaction.RollbackAsync();
                }
                catch (Exception)
                {
                    // Releasing the gate must not replace the test result with a connection error.
                }
            }

            if (!resultsObserved)
            {
                try
                {
                    var pending = new List<Task>();
                    if (first is not null)
                    {
                        pending.Add(first);
                    }

                    if (second is not null)
                    {
                        pending.Add(second);
                    }

                    if (pending.Count > 0)
                    {
                        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(60));
                    }
                }
                catch (Exception)
                {
                    // The original assertion remains the test result.
                }
            }

            if (gate is not null)
            {
                await gate.DisposeAsync();
            }

            if (services is not null)
            {
                await services.DisposeAsync();
            }

            await CleanupOwnedAsync(approved, slugs, emails);
        }
    }

    private static async Task MigrateAsync(string connectionString)
    {
        RequireApprovedDatabase(connectionString);
        await using var services = CreateServices(connectionString);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
    }

    private static async Task<StudioBootstrapResult> RunAsync(ServiceProvider services, StudioBootstrapRequest request)
    {
        await using var scope = services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IStudioBootstrap>();
        return await bootstrap.RunAsync(request, CancellationToken.None);
    }

    private static async Task TakeLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@key1, @key2)";
        command.Parameters.Add(new NpgsqlParameter("key1", StudioBootstrapLock.Key1));
        command.Parameters.Add(new NpgsqlParameter("key2", StudioBootstrapLock.Key2));
        await command.ExecuteScalarAsync();
    }

    private static async Task<int> WaitForWaitersAsync(string connectionString, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        var waiting = 0;
        while (DateTime.UtcNow < deadline)
        {
            waiting = await ScalarAsync(
                connectionString,
                """
                SELECT COUNT(DISTINCT pid)
                FROM pg_locks
                WHERE locktype = 'advisory'
                    AND classid = CAST(@key1 AS oid)
                    AND objid = CAST(@key2 AS oid)
                    AND objsubid = 1
                    AND NOT granted
                """,
                new NpgsqlParameter("key1", StudioBootstrapLock.Key1),
                new NpgsqlParameter("key2", StudioBootstrapLock.Key2));
            if (waiting >= 2)
            {
                return waiting;
            }

            await Task.Delay(100);
        }

        return waiting;
    }

    private static async Task<(int Tenants, int Users, int Memberships, int Photos)> CountsAsync(string connectionString)
    {
        return (
            await ScalarAsync(connectionString, """SELECT COUNT(*) FROM "Tenants" """),
            await ScalarAsync(connectionString, """SELECT COUNT(*) FROM "AspNetUsers" """),
            await ScalarAsync(connectionString, """SELECT COUNT(*) FROM "StudioMemberships" """),
            await ScalarAsync(connectionString, """SELECT COUNT(*) FROM "Photos" """));
    }

    private static async Task<int> ScalarAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        RequireApprovedDatabase(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static ServiceProvider CreateServices(string connectionString)
    {
        RequireApprovedDatabase(connectionString);
        var services = new ServiceCollection();
        services.AddScoped<CurrentStudio>();
        services.AddScoped<ICurrentStudio>(provider => provider.GetRequiredService<CurrentStudio>());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<IStudioBootstrap, StudioBootstrap>();
        return services.BuildServiceProvider();
    }

    private static async Task CleanupOwnedAsync(string connectionString, IReadOnlyCollection<string> slugs, IReadOnlyCollection<string> emails)
    {
        RequireApprovedDatabase(connectionString);
        await using var services = CreateServices(connectionString);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var slugList = slugs.ToArray();
        var emailList = emails.ToArray();
        var tenants = await context.Tenants.Where(tenant => slugList.Contains(tenant.Slug)).ToListAsync();
        foreach (var tenant in tenants)
        {
            var photos = await ScalarAsync(
                connectionString,
                """SELECT COUNT(*) FROM "Photos" WHERE "TenantId" = @tenantId""",
                new NpgsqlParameter("tenantId", tenant.Id));
            if (photos != 0)
            {
                throw new InvalidOperationException("Cleanup stopped because a bootstrap tenant has photos.");
            }
        }

        var accounts = await context.Users.Where(user => user.Email != null && emailList.Contains(user.Email)).ToListAsync();
        var tenantIds = tenants.Select(tenant => tenant.Id).ToArray();
        var userIds = accounts.Select(user => user.Id).ToArray();
        var memberships = await context.Memberships
            .Where(membership => tenantIds.Contains(membership.TenantId) || userIds.Contains(membership.UserId))
            .ToListAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Memberships.RemoveRange(memberships);
        await context.SaveChangesAsync();
        foreach (var account in accounts)
        {
            var deleted = await users.DeleteAsync(account);
            if (!deleted.Succeeded)
            {
                throw new InvalidOperationException("Cleanup could not remove the bootstrap user.");
            }
        }

        context.Tenants.RemoveRange(tenants);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
