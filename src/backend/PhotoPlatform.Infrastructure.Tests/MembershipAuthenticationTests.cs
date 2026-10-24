using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class MembershipAuthenticationTests
{
    private const string Password = "Studio-Test-Password-1";
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused";

    [Fact]
    public async Task Health_stays_available_and_anonymous_membership_is_rejected()
    {
        await using var factory = new MembershipApiFactory(UnreachableConnection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var health = await client.GetAsync("/api/v1/health");
        var membership = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, membership.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Anonymous_membership_request_is_rejected()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Wrong_password_does_not_report_success_or_echo_the_password()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        var email = UniqueEmail();
        await factory.CreateMemberAsync(email, "North Studio");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await PostSignInAsync(client, email, "Wrong-Password-1");

        AssertGenericSignInFailure(response, await response.Content.ReadAsStringAsync(), "Wrong-Password-1");
    }

    [PostgreSqlFact]
    public async Task Unknown_account_returns_the_same_generic_failure()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        const string submitted = "Wrong-Password-1";

        var response = await PostSignInAsync(client, UniqueEmail(), submitted);

        AssertGenericSignInFailure(response, await response.Content.ReadAsStringAsync(), submitted);
    }

    [PostgreSqlFact]
    public async Task Lockout_returns_the_same_generic_failure()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        var email = UniqueEmail();
        await factory.CreateMemberAsync(email, "North Studio");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        const string submitted = "Wrong-Password-1";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await PostSignInAsync(client, email, submitted);
            AssertGenericSignInFailure(failed, await failed.Content.ReadAsStringAsync(), submitted);
        }

        var locked = await PostSignInAsync(client, email, Password);
        AssertGenericSignInFailure(locked, await locked.Content.ReadAsStringAsync(), Password);
    }

    [PostgreSqlFact]
    public async Task Signed_in_member_receives_only_their_tenant()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        var email = UniqueEmail();
        var tenantId = await factory.CreateMemberAsync(email, "North Studio");
        var otherTenantId = await factory.CreateTenantAsync("Other Studio");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var signIn = await PostSignInAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/membership/current?tenantId={otherTenantId}&slug=other-studio");
        request.Headers.Add(MembershipController.ClientTenantHeader, otherTenantId.ToString());
        var response = await client.SendAsync(request);
        var membership = await response.Content.ReadFromJsonAsync<CurrentMembershipResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(membership);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.NotEqual(otherTenantId, membership.TenantId);

        var csrf = await ReadCsrfAsync(client);
        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out");
        signOut.Headers.Add("X-CSRF-TOKEN", csrf);
        var signedOut = await client.SendAsync(signOut);
        var afterSignOut = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterSignOut.StatusCode);
    }

    [PostgreSqlFact]
    public async Task User_without_membership_receives_the_same_sign_in_failure()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        var email = UniqueEmail();
        await factory.CreateUserWithoutMembershipAsync(email);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await PostSignInAsync(client, email, Password);

        AssertGenericSignInFailure(response, await response.Content.ReadAsStringAsync(), Password);
    }

    [Fact]
    public async Task Sign_in_without_csrf_token_is_rejected()
    {
        await using var factory = new MembershipApiFactory(UnreachableConnection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-in")
        {
            Content = JsonContent.Create(new SignInRequest("member@example.test", Password)),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(Password, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_sign_out_is_rejected()
    {
        await using var factory = new MembershipApiFactory(UnreachableConnection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await client.PostAsync("/api/v1/auth/sign-out", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Signed_in_sign_out_without_csrf_keeps_the_session()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);

        await using var factory = new MembershipApiFactory(connectionString!);
        await factory.MigrateAsync();
        var email = UniqueEmail();
        await factory.CreateMemberAsync(email, "North Studio");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var signIn = await PostSignInAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        var rejected = await client.PostAsync("/api/v1/auth/sign-out", new StringContent(string.Empty));
        var membership = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, membership.StatusCode);
    }

    [Fact]
    public async Task Credentialed_cors_allows_only_the_two_development_origins()
    {
        await using var factory = new MembershipApiFactory(UnreachableConnection);
        using var client = factory.CreateClient();
        string[] allowedOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"];

        foreach (var origin in allowedOrigins)
        {
            using var health = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
            health.Headers.Add("Origin", origin);
            var healthResponse = await client.SendAsync(health);
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            AssertCredentialedOrigin(healthResponse, origin);

            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/sign-in");
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
            var preflightResponse = await client.SendAsync(preflight);
            AssertCredentialedOrigin(preflightResponse, origin);
        }

        using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
        denied.Headers.Add("Origin", "https://example.invalid");
        var deniedResponse = await client.SendAsync(denied);

        Assert.Equal(HttpStatusCode.OK, deniedResponse.StatusCode);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public void Development_cookie_stays_http_only_on_the_local_http_api()
    {
        using var factory = new MembershipApiFactory(UnreachableConnection);
        using var scope = factory.Services.CreateScope();
        var cookie = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var antiforgery = scope.ServiceProvider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.Equal(StudioAuthenticationExtensions.AuthenticationCookieName, cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, cookie.Cookie.SecurePolicy);
        Assert.Equal(StudioAuthenticationExtensions.CsrfHeaderName, antiforgery.HeaderName);
        Assert.Equal(StudioAuthenticationExtensions.CsrfCookieName, antiforgery.Cookie.Name);
        Assert.True(antiforgery.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, antiforgery.Cookie.SameSite);
    }

    [Fact]
    public void Non_development_cookie_requires_secure()
    {
        using var factory = new MembershipApiFactory(UnreachableConnection, "Production");
        using var scope = factory.Services.CreateScope();
        var cookie = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.True(cookie.Cookie.HttpOnly);
    }

    [Fact]
    public void Test_host_uses_only_the_guarded_database_name()
    {
        using var factory = new MembershipApiFactory(UnreachableConnection);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var databaseName = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).Database;

        Assert.Equal(PostgreSqlTestGuard.RequiredDatabaseName, databaseName);
    }

    [Fact]
    public async Task Development_database_is_rejected_before_migration()
    {
        await using var factory = new MembershipApiFactory(
            "Host=127.0.0.1;Port=1;Database=photoplatform;Username=photoplatform;Password=should-not-leak");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.MigrateAsync());

        Assert.DoesNotContain("should-not-leak", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostSignInAsync(HttpClient client, string email, string password)
    {
        var csrf = await ReadCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-in")
        {
            Content = JsonContent.Create(new SignInRequest(email, password)),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static async Task<string> ReadCsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.Token));
        return token.Token;
    }

    private static string UniqueEmail()
    {
        return $"member-{Guid.NewGuid():N}@example.test";
    }

    private static void AssertGenericSignInFailure(HttpResponseMessage response, string body, string submittedPassword)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("The email or password is incorrect.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(submittedPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("lock", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("membership", body, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertCredentialedOrigin(HttpResponseMessage response, string origin)
    {
        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.DoesNotContain(
            "*",
            response.Headers.GetValues("Access-Control-Allow-Origin"),
            StringComparer.Ordinal);
    }

    private sealed class MembershipApiFactory(string connectionString, string environmentName = "Development")
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureServices(services =>
            {
                PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
                var replaced = new[]
                {
                    typeof(DbContextOptions<ApplicationDbContext>),
                    typeof(DbContextOptions),
                    typeof(ApplicationDbContext),
                };
                foreach (var descriptor in services.Where(service => replaced.Contains(service.ServiceType)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
            });
        }

        public async Task MigrateAsync()
        {
            PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();
        }

        public async Task<Guid> CreateMemberAsync(string email, string studioName)
        {
            PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
            var tenantId = await CreateTenantAsync(studioName);
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                LockoutEnabled = true,
            };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded);
            context.Memberships.Add(new StudioMembership(user.Id, tenantId, DateTime.UtcNow));
            await context.SaveChangesAsync();
            return tenantId;
        }

        public async Task CreateUserWithoutMembershipAsync(string email)
        {
            PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded);
        }

        public async Task<Guid> CreateTenantAsync(string studioName)
        {
            PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var tenant = new Tenant(studioName, $"studio-{suffix}", DateTime.UtcNow);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            return tenant.Id;
        }
    }
}
