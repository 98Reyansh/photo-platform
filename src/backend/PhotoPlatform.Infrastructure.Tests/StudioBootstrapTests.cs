using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Infrastructure.Authentication;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public sealed class StudioBootstrapTests
{
    private const string PolicyPassword = "Abcd-1234567";

    private const string StudioName = "North Studio";

    private const string StudioSlug = "north-studio";

    private const string OwnerEmail = "owner@example.com";

    [Fact]
    public void Bootstrap_host_without_authentication_fails_service_validation()
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
        {
            using var app = CreateBootstrapHost(includeAuthentication: false);
        });
        var described = StudioBootstrapDiagnostics.Describe(exception);
        var typeNames = string.Join(" ", QuotedTypeNames(exception));

        Assert.StartsWith("System.AggregateException", described, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", described, StringComparison.Ordinal);
        Assert.Contains("IAuthenticationSchemeProvider", typeNames, StringComparison.Ordinal);
        Assert.Contains("SignInManager", typeNames, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", typeNames, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", typeNames, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_host_builds_when_studio_authentication_is_registered()
    {
        using var app = CreateBootstrapHost(includeAuthentication: true);
        using var scope = app.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IStudioBootstrap>());
    }

    [Fact]
    public void Bootstrap_is_requested_only_for_the_single_command_argument()
    {
        Assert.True(StudioBootstrapCommand.IsRequested(["bootstrap"]));
        Assert.False(StudioBootstrapCommand.IsRequested([]));
        Assert.False(StudioBootstrapCommand.IsRequested(["bootstrap", "again"]));
    }

    [Fact]
    public async Task Missing_environment_values_fail_before_any_write()
    {
        var values = ValidEnvironment().Without(StudioBootstrapEnvironment.OwnerPasswordVariable);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            new FailIfCalledBootstrap(),
            output,
            error,
            values.Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains(StudioBootstrapEnvironment.OwnerPasswordVariable, error.ToString(), StringComparison.Ordinal);
        AssertNoSecrets(output.ToString() + error);
    }

    [Fact]
    public async Task Invalid_studio_name_slug_and_email_are_rejected()
    {
        await using var database = await BootstrapDatabase.CreateAsync();

        var longName = await RunAsync(database, ValidRequest(studioName: new string('a', Tenant.NameMaxLength + 1)));
        var badSlug = await RunAsync(database, ValidRequest(studioSlug: "North Studio"));
        var badEmail = await RunAsync(database, ValidRequest(ownerEmail: "not-an-email"));

        Assert.Equal(StudioBootstrapStatus.InvalidStudio, longName.Status);
        Assert.Contains("Name must be at most", longName.Message, StringComparison.Ordinal);
        Assert.Equal(StudioBootstrapStatus.InvalidStudio, badSlug.Status);
        Assert.Contains("Slug must contain only lowercase letters", badSlug.Message, StringComparison.Ordinal);
        Assert.Equal(StudioBootstrapStatus.Failed, badEmail.Status);
        Assert.Contains("invalid", badEmail.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoSecrets(longName.Message + badSlug.Message + badEmail.Message);
        await AssertEmptyAsync(database);
    }

    [Fact]
    public async Task Password_that_fails_the_identity_policy_is_rejected_and_saved_nowhere()
    {
        await using var database = await BootstrapDatabase.CreateAsync();

        var result = await RunAsync(database, ValidRequest(ownerPassword: "Abcd-123456"));

        Assert.Equal(StudioBootstrapStatus.Failed, result.Status);
        Assert.Contains("at least 12 characters", result.Message, StringComparison.Ordinal);
        AssertNoSecrets(result.Message, "Abcd-123456");
        await AssertEmptyAsync(database);
    }

    [Fact]
    public async Task Successful_bootstrap_creates_one_studio_owner_and_membership()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            BootstrapService(database),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bootstrap completed.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(OwnerEmail, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(StudioSlug, output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
        AssertNoSecrets(output.ToString());
        var counts = await CountsAsync(database);
        Assert.Equal((1, 1, 1), counts);

        await using var scope = database.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await context.Tenants.SingleAsync();
        var user = await context.Users.SingleAsync();
        var membership = await context.Memberships.SingleAsync();
        Assert.Equal(StudioSlug, tenant.Slug);
        Assert.Equal(OwnerEmail, user.Email);
        Assert.Equal(user.Id, membership.UserId);
        Assert.Equal(tenant.Id, membership.TenantId);
        Assert.StartsWith("AQAAAA", user.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_tenant_prevents_bootstrap()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Tenants.Add(new Tenant("Existing Studio", "existing-studio", DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        var result = await RunAsync(database, ValidRequest(ownerPassword: "Abcd-123456"));

        Assert.Equal(StudioBootstrapStatus.AlreadyBootstrapped, result.Status);
        Assert.Equal(StudioBootstrapMessages.AlreadyBootstrapped, result.Message);
        AssertNoSecrets(result.Message, "Abcd-123456");
        Assert.Equal((1, 0, 0), await CountsAsync(database));
    }

    [Fact]
    public async Task Existing_identity_user_prevents_bootstrap()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        await using (var scope = database.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await users.CreateAsync(NewUser("other@example.com"), PolicyPassword);
            Assert.True(created.Succeeded);
        }

        var result = await RunAsync(database, ValidRequest());

        Assert.Equal(StudioBootstrapStatus.AlreadyBootstrapped, result.Status);
        AssertNoSecrets(result.Message);
        Assert.Equal((0, 1, 0), await CountsAsync(database));
    }

    [Fact]
    public async Task Membership_persistence_failure_rolls_back_and_does_not_report_success()
    {
        await using var database = await BootstrapDatabase.CreateAsync(failMembershipSave: true);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            BootstrapService(database),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(StudioBootstrapMessages.RolledBack, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, CountLines(error.ToString(), "System.InvalidOperationException"));
        Assert.DoesNotContain("membership persistence failed", error.ToString(), StringComparison.Ordinal);
        AssertNoSecrets(error.ToString());
        await AssertEmptyAsync(database);
    }

    [Fact]
    public async Task Second_execution_after_success_is_refused()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        var environment = ValidEnvironment();
        var output = new StringWriter();
        var error = new StringWriter();

        var first = await StudioBootstrapCommand.ExecuteAsync(
            BootstrapService(database),
            output,
            error,
            environment.Read,
            CancellationToken.None);
        var second = await RunAsync(database, ValidRequest(studioSlug: "another-studio", ownerEmail: "other@example.com"));

        Assert.Equal(0, first);
        Assert.Equal(StudioBootstrapStatus.AlreadyBootstrapped, second.Status);
        Assert.Equal(StudioBootstrapMessages.AlreadyBootstrapped, second.Message);
        Assert.Equal((1, 1, 1), await CountsAsync(database));
    }

    [Fact]
    public async Task Secrets_are_absent_from_output_and_error_messages()
    {
        const string leaked = "Host=db.example;Password=Abcd-1234567;Database=photoplatform";
        var output = new StringWriter();
        var error = new StringWriter();
        var values = ValidEnvironment();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            new ThrowingBootstrap(leaked),
            output,
            error,
            values.Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            StudioBootstrapMessages.DatabaseFailure + Environment.NewLine
            + "System.InvalidOperationException" + Environment.NewLine,
            error.ToString());
        Assert.Equal(string.Empty, output.ToString());
        Assert.DoesNotContain(leaked, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("photoplatform", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photo_platform_test", error.ToString(), StringComparison.OrdinalIgnoreCase);
        AssertNoSecrets(error.ToString());
    }

    [Fact]
    public async Task Shutdown_failure_keeps_the_bootstrap_result_and_hides_the_exception()
    {
        const string leaked = "Host=db.example;Password=Abcd-1234567;Database=photoplatform";
        var output = new StringWriter();
        var error = new StringWriter();
        await output.WriteLineAsync("Bootstrap completed. Studio id: 00000000-0000-0000-0000-000000000001. Slug: north-studio. Owner: owner@example.com.");

        var exitCode = await StudioBootstrapEntry.CompleteAsync(
            bootstrapExitCode: 0,
            () => throw new InvalidOperationException(leaked),
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bootstrap completed.", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            StudioBootstrapMessages.ShutdownFailure + Environment.NewLine
            + "System.InvalidOperationException" + Environment.NewLine,
            error.ToString());
        Assert.DoesNotContain(leaked, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_distinguish_configuration_postgres_and_other_failures_without_secrets()
    {
        const string secret = "Password=secret-value";
        var missing = InfrastructureServiceCollectionExtensions.MissingConnectionStringMessage;
        var authentication = new PostgresException(
            "password authentication failed for user \"photoapp\"; " + secret,
            "FATAL",
            "FATAL",
            "28P01");
        var connection = new InvalidOperationException(
            "Host=localhost;" + secret,
            new PostgresException("connection failed", "FATAL", "FATAL", "08001"));

        Assert.Equal(
            "System.InvalidOperationException " + missing,
            StudioBootstrapDiagnostics.Describe(new InvalidOperationException(missing)));
        Assert.Equal(
            "System.InvalidOperationException",
            StudioBootstrapDiagnostics.Describe(new InvalidOperationException(missing + " " + secret)));
        Assert.Equal("Npgsql.PostgresException 28P01", StudioBootstrapDiagnostics.Describe(authentication));
        Assert.Equal(
            "System.InvalidOperationException -> Npgsql.PostgresException 08001",
            StudioBootstrapDiagnostics.Describe(connection));
        Assert.Equal(
            "System.IO.IOException",
            StudioBootstrapDiagnostics.Describe(new IOException(secret)));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            new ThrowingBootstrap(authentication),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            StudioBootstrapMessages.DatabaseFailure + Environment.NewLine
            + "Npgsql.PostgresException 28P01" + Environment.NewLine,
            error.ToString());
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("photoapp", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerEmail, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, CountLines(error.ToString(), "Npgsql.PostgresException 28P01"));
    }

    [Fact]
    public async Task Aggregate_exception_reports_nested_types_and_sqlstate_without_secrets()
    {
        const string secret = "Password=secret-value";
        var aggregate = new AggregateException(
            "Some services are not able to be constructed. " + secret + " " + OwnerEmail,
            new InvalidOperationException("Unable to resolve service for " + secret),
            new AggregateException(
                secret,
                new PostgresException(
                    "password authentication failed for user \"photoapp\"; " + secret,
                    "FATAL",
                    "FATAL",
                    "28P01")));
        var expected =
            "System.AggregateException -> System.InvalidOperationException -> System.AggregateException -> Npgsql.PostgresException 28P01";

        Assert.Equal(expected, StudioBootstrapDiagnostics.Describe(aggregate));
        Assert.DoesNotContain(secret, StudioBootstrapDiagnostics.Describe(aggregate), StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerEmail, StudioBootstrapDiagnostics.Describe(aggregate), StringComparison.Ordinal);
        Assert.DoesNotContain("photoapp", StudioBootstrapDiagnostics.Describe(aggregate), StringComparison.Ordinal);
        Assert.DoesNotContain("authentication failed", StudioBootstrapDiagnostics.Describe(aggregate), StringComparison.Ordinal);

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            new ThrowingBootstrap(aggregate),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(
            StudioBootstrapMessages.DatabaseFailure + Environment.NewLine + expected + Environment.NewLine,
            error.ToString());
        Assert.Equal(1, CountLines(error.ToString(), expected));
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerEmail, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_chain_is_bounded()
    {
        Exception current = new IOException("Password=secret-value");
        for (var index = 0; index < 12; index++)
        {
            current = new InvalidOperationException("Password=secret-value", current);
        }

        var described = StudioBootstrapDiagnostics.Describe(current);
        Assert.Equal(8, described.Split(" -> ").Length);
        Assert.DoesNotContain("Password=", described, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Returned_diagnostic_is_written_once()
    {
        const string diagnostic = "Npgsql.PostgresException 28P01";
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            new ResultBootstrap(new StudioBootstrapResult(
                StudioBootstrapStatus.Failed,
                StudioBootstrapMessages.DatabaseFailure,
                null,
                null,
                null,
                diagnostic)),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            StudioBootstrapMessages.DatabaseFailure + Environment.NewLine + diagnostic + Environment.NewLine,
            error.ToString());
        Assert.Equal(1, CountLines(error.ToString(), diagnostic));
        Assert.DoesNotContain(OwnerEmail, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closed_database_reports_one_type_diagnostic_and_saves_nothing()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        await database.CloseAsync();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await StudioBootstrapCommand.ExecuteAsync(
            BootstrapService(database),
            output,
            error,
            ValidEnvironment().Read,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.StartsWith(
            StudioBootstrapMessages.DatabaseFailure + Environment.NewLine,
            error.ToString(),
            StringComparison.Ordinal);
        var diagnostic = error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.DoesNotContain(' ', diagnostic);
        Assert.DoesNotContain('=', diagnostic);
        Assert.DoesNotContain(":memory:", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerEmail, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, CountLines(error.ToString(), diagnostic));
    }

    private static WebApplication CreateBootstrapHost(bool includeAuthentication)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            EnvironmentName = Environments.Development,
            ApplicationName = "PhotoPlatform.Api",
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=photo_platform;Username=photoapp",
        });
        builder.Services.AddInfrastructure(builder.Configuration);
        if (includeAuthentication)
        {
            builder.Services.AddStudioAuthentication(builder.Environment);
        }

        return builder.Build();
    }

    private static IEnumerable<string> QuotedTypeNames(Exception exception)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        return Visit(exception, seen, depth: 0).Distinct(StringComparer.Ordinal);

        static IEnumerable<string> Visit(Exception current, HashSet<Exception> seen, int depth)
        {
            if (depth >= 8 || !seen.Add(current))
            {
                yield break;
            }

            foreach (Match match in TypeNamePattern.Matches(current.Message))
            {
                yield return match.Groups[1].Value;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    foreach (var name in Visit(inner, seen, depth + 1))
                    {
                        yield return name;
                    }
                }

                yield break;
            }

            if (current.InnerException is not null)
            {
                foreach (var name in Visit(current.InnerException, seen, depth + 1))
                {
                    yield return name;
                }
            }
        }
    }

    private static readonly Regex TypeNamePattern = new(
        @"'([A-Za-z_][A-Za-z0-9_`.+[\],]*)'",
        RegexOptions.CultureInvariant);

    private static int CountLines(string text, string line)
    {
        return text.Split(Environment.NewLine).Count(item => item == line);
    }

    private static async Task<StudioBootstrapResult> RunAsync(BootstrapDatabase database, StudioBootstrapRequest request)
    {
        await using var scope = database.Services.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider.GetRequiredService<IStudioBootstrap>();
        return await bootstrap.RunAsync(request, CancellationToken.None);
    }

    private static IStudioBootstrap BootstrapService(BootstrapDatabase database)
    {
        return new ScopedBootstrap(database.Services);
    }

    private static StudioBootstrapRequest ValidRequest(
        string? studioName = null,
        string? studioSlug = null,
        string? ownerEmail = null,
        string? ownerPassword = null)
    {
        return new StudioBootstrapRequest(
            studioName ?? StudioName,
            studioSlug ?? StudioSlug,
            ownerEmail ?? OwnerEmail,
            ownerPassword ?? PolicyPassword);
    }

    private static EnvironmentValues ValidEnvironment()
    {
        return new EnvironmentValues(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [StudioBootstrapEnvironment.StudioNameVariable] = StudioName,
            [StudioBootstrapEnvironment.StudioSlugVariable] = StudioSlug,
            [StudioBootstrapEnvironment.OwnerEmailVariable] = OwnerEmail,
            [StudioBootstrapEnvironment.OwnerPasswordVariable] = PolicyPassword,
        });
    }

    private static async Task AssertEmptyAsync(BootstrapDatabase database)
    {
        Assert.Equal((0, 0, 0), await CountsAsync(database));
    }

    private static async Task<(int Tenants, int Users, int Memberships)> CountsAsync(BootstrapDatabase database)
    {
        await using var scope = database.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (
            await context.Tenants.CountAsync(),
            await context.Users.CountAsync(),
            await context.Memberships.CountAsync());
    }

    private static void AssertNoSecrets(string text, string? extraSecret = null)
    {
        Assert.DoesNotContain(PolicyPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultConnection", text, StringComparison.Ordinal);
        Assert.DoesNotContain("photo_platform_test", text, StringComparison.OrdinalIgnoreCase);
        if (extraSecret is not null)
        {
            Assert.DoesNotContain(extraSecret, text, StringComparison.Ordinal);
        }
    }

    private static ApplicationUser NewUser(string email)
    {
        return new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
        };
    }

    private sealed class EnvironmentValues(Dictionary<string, string?> values)
    {
        public string? Read(string name)
        {
            return values.TryGetValue(name, out var value) ? value : null;
        }

        public EnvironmentValues Without(string name)
        {
            var copy = new Dictionary<string, string?>(values, StringComparer.Ordinal);
            copy.Remove(name);
            return new EnvironmentValues(copy);
        }
    }

    private sealed class FailIfCalledBootstrap : IStudioBootstrap
    {
        public Task<StudioBootstrapResult> RunAsync(StudioBootstrapRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Bootstrap was called without required configuration.");
        }
    }

    private sealed class ThrowingBootstrap : IStudioBootstrap
    {
        private readonly Exception exception;

        public ThrowingBootstrap(string detail)
        {
            exception = new InvalidOperationException(detail);
        }

        public ThrowingBootstrap(Exception exception)
        {
            this.exception = exception;
        }

        public Task<StudioBootstrapResult> RunAsync(StudioBootstrapRequest request, CancellationToken cancellationToken)
        {
            throw exception;
        }
    }

    private sealed class ResultBootstrap(StudioBootstrapResult result) : IStudioBootstrap
    {
        public Task<StudioBootstrapResult> RunAsync(StudioBootstrapRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class ScopedBootstrap(IServiceProvider services) : IStudioBootstrap
    {
        public async Task<StudioBootstrapResult> RunAsync(StudioBootstrapRequest request, CancellationToken cancellationToken)
        {
            await using var scope = services.CreateAsyncScope();
            var bootstrap = scope.ServiceProvider.GetRequiredService<IStudioBootstrap>();
            return await bootstrap.RunAsync(request, cancellationToken);
        }
    }

    private sealed class MembershipSaveFailureInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            RejectMembership(eventData);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            RejectMembership(eventData);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void RejectMembership(DbContextEventData eventData)
        {
            var context = eventData.Context;
            if (context is null)
            {
                return;
            }

            var addingMembership = context.ChangeTracker.Entries<StudioMembership>()
                .Any(entry => entry.State == EntityState.Added);
            if (addingMembership)
            {
                throw new InvalidOperationException("membership persistence failed");
            }
        }
    }

    private sealed class BootstrapDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;

        private BootstrapDatabase(SqliteConnection connection, ServiceProvider services)
        {
            this.connection = connection;
            this.services = services;
        }

        public IServiceProvider Services => services;

        public Task CloseAsync()
        {
            return connection.CloseAsync();
        }

        public static async Task<BootstrapDatabase> CreateAsync(bool failMembershipSave = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            Assert.DoesNotContain("photoplatform", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("photo_platform_test", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);

            var collection = new ServiceCollection();
            collection.AddScoped<CurrentStudio>();
            collection.AddScoped<ICurrentStudio>(provider => provider.GetRequiredService<CurrentStudio>());
            collection.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connection);
                if (failMembershipSave)
                {
                    options.AddInterceptors(new MembershipSaveFailureInterceptor());
                }
            });
            collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            collection.AddIdentityCore<ApplicationUser>(options =>
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
            collection.AddScoped<IStudioBootstrap, StudioBootstrap>();

            var services = collection.BuildServiceProvider();
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureCreatedAsync();
            return new BootstrapDatabase(connection, services);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
