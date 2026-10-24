using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class StudioResolutionTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused";

    [Fact]
    public async Task Authenticated_member_is_resolved_before_the_membership_action()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = tenantId;
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/membership/current");
        var membership = await response.Content.ReadFromJsonAsync<CurrentMembershipResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(membership);
        Assert.Equal(tenantId, membership.TenantId);
        Assert.Equal([tenantId], factory.Probe.TenantIds);
        Assert.Equal(2, factory.Membership.Calls.Count);
        Assert.Equal(userId, factory.Membership.Calls[0].UserId);
        Assert.False(factory.Membership.Calls[0].StudioResolved);
        Assert.Equal(userId, factory.Membership.Calls[1].UserId);
        Assert.True(factory.Membership.Calls[1].StudioResolved);
        Assert.Equal(tenantId, factory.Membership.Calls[1].TenantId);
    }

    [Fact]
    public async Task User_without_membership_stays_unresolved()
    {
        var userId = Guid.CreateVersion7();
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Probe.TenantIds);
        Assert.All(factory.Membership.Calls, call => Assert.False(call.StudioResolved));
        Assert.Contains(factory.Membership.Calls, call => call.UserId == userId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Missing_or_malformed_identity_does_not_resolve_a_tenant(string? nameIdentifier)
    {
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = nameIdentifier;
        factory.Membership.TenantsByUser[Guid.CreateVersion7()] = Guid.CreateVersion7();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Probe.TenantIds);
        Assert.Empty(factory.Membership.Calls);
    }

    [Fact]
    public async Task Empty_user_identifier_does_not_resolve_a_tenant()
    {
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = Guid.Empty.ToString();
        factory.Membership.TenantsByUser[Guid.CreateVersion7()] = Guid.CreateVersion7();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(factory.Probe.TenantIds);
        var call = Assert.Single(factory.Membership.Calls);
        Assert.Equal(Guid.Empty, call.UserId);
        Assert.False(call.StudioResolved);
    }

    [Fact]
    public async Task Client_supplied_tenant_values_do_not_change_the_resolved_studio()
    {
        var userId = Guid.CreateVersion7();
        var membershipTenant = Guid.CreateVersion7();
        var clientTenant = Guid.CreateVersion7();
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = membershipTenant;
        using var client = factory.CreateClient();

        using var mismatched = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/membership/{clientTenant}?tenantId={clientTenant}&slug=other-studio")
        {
            Content = JsonContent.Create(new { tenantId = clientTenant, slug = "other-studio" }),
        };
        mismatched.Headers.Add(MembershipController.ClientTenantHeader, clientTenant.ToString());
        var ignored = await client.SendAsync(mismatched);

        using var current = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/membership/current?tenantId={clientTenant}&slug=other-studio");
        current.Headers.Add(MembershipController.ClientTenantHeader, clientTenant.ToString());
        var response = await client.SendAsync(current);
        var membership = await response.Content.ReadFromJsonAsync<CurrentMembershipResponse>();

        Assert.Equal(HttpStatusCode.NotFound, ignored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(membership);
        Assert.Equal(membershipTenant, membership.TenantId);
        Assert.All(factory.Probe.TenantIds, tenantId => Assert.Equal(membershipTenant, tenantId));
        Assert.DoesNotContain(clientTenant, factory.Probe.TenantIds);
    }

    [Fact]
    public async Task Anonymous_health_sign_in_and_csrf_do_not_resolve_a_studio()
    {
        await using var factory = new ResolutionApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var health = await client.GetAsync("/api/v1/health");
        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        using var signIn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-in")
        {
            Content = JsonContent.Create(new SignInRequest("member@example.test", "Studio-Test-Password-1")),
        };
        var rejected = await client.SendAsync(signIn);
        var membership = await client.GetAsync("/api/v1/membership/current");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = await csrf.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(token);
        Assert.False(string.IsNullOrWhiteSpace(token.Token));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, membership.StatusCode);
        Assert.Empty(factory.Probe.TenantIds);
        Assert.Empty(factory.Membership.Calls);
    }

    [Fact]
    public async Task Resolved_tenant_does_not_leak_into_the_next_request()
    {
        var firstUser = Guid.CreateVersion7();
        var secondUser = Guid.CreateVersion7();
        var firstTenant = Guid.CreateVersion7();
        var secondTenant = Guid.CreateVersion7();
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Membership.TenantsByUser[firstUser] = firstTenant;
        factory.Membership.TenantsByUser[secondUser] = secondTenant;
        using var client = factory.CreateClient();

        factory.Principal.NameIdentifier = firstUser.ToString();
        var first = await client.GetFromJsonAsync<CurrentMembershipResponse>("/api/v1/membership/current");
        factory.Principal.NameIdentifier = secondUser.ToString();
        var second = await client.GetFromJsonAsync<CurrentMembershipResponse>("/api/v1/membership/current");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(firstTenant, first.TenantId);
        Assert.Equal(secondTenant, second.TenantId);
        Assert.Equal([firstTenant, secondTenant], factory.Probe.TenantIds);
    }

    [Fact]
    public async Task Membership_infrastructure_failure_is_not_treated_as_a_missing_membership()
    {
        const string failure = "database unavailable";
        var userId = Guid.CreateVersion7();
        await using var factory = new ResolutionApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.Error = new InvalidOperationException(failure);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/membership/current");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("The request could not be completed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(failure, body, StringComparison.Ordinal);
        Assert.Empty(factory.Probe.TenantIds);
        var call = Assert.Single(factory.Membership.Calls);
        Assert.Equal(userId, call.UserId);
        Assert.False(call.StudioResolved);
    }

    private sealed class ResolutionApiFactory : WebApplicationFactory<Program>
    {
        public TestPrincipal Principal { get; } = new();

        public MembershipDouble Membership { get; } = new();

        public ResolutionProbe Probe { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                PostgreSqlTestGuard.EnsureSafeToUse(UnreachableConnection);
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

                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(UnreachableConnection));

                foreach (var descriptor in services.Where(service =>
                    service.ServiceType == typeof(ICurrentMembershipQuery)
                    || service.ServiceType == typeof(ICurrentStudioSetter)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton(Principal);
                services.AddSingleton(Membership);
                services.AddSingleton(Probe);
                services.AddScoped<ICurrentMembershipQuery>(provider => new ObservingMembershipQuery(
                    provider.GetRequiredService<ICurrentStudio>(),
                    provider.GetRequiredService<MembershipDouble>()));
                services.AddScoped<ICurrentStudioSetter>(provider => new RecordingStudioSetter(
                    provider.GetRequiredService<CurrentStudio>(),
                    provider.GetRequiredService<ResolutionProbe>()));
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestUserAuthenticationHandler>(
                        TestUserAuthenticationHandler.SchemeName,
                        _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestUserAuthenticationHandler.SchemeName;
                });
            });
        }
    }

    private sealed class TestPrincipal
    {
        public bool IsAuthenticated { get; set; }

        public string? NameIdentifier { get; set; }
    }

    private sealed class MembershipCall(Guid userId, bool studioResolved, Guid tenantId)
    {
        public Guid UserId { get; } = userId;

        public bool StudioResolved { get; } = studioResolved;

        public Guid TenantId { get; } = tenantId;
    }

    private sealed class MembershipDouble
    {
        public Dictionary<Guid, Guid> TenantsByUser { get; } = [];

        public List<MembershipCall> Calls { get; } = [];

        public Exception? Error { get; set; }
    }

    private sealed class ResolutionProbe
    {
        public List<Guid> TenantIds { get; } = [];
    }

    private sealed class ObservingMembershipQuery(ICurrentStudio studio, MembershipDouble state)
        : ICurrentMembershipQuery
    {
        public Task<CurrentMembershipResponse?> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            state.Calls.Add(new MembershipCall(userId, studio.IsResolved, studio.TenantId));
            if (state.Error is not null)
            {
                throw state.Error;
            }

            if (!state.TenantsByUser.TryGetValue(userId, out var tenantId))
            {
                return Task.FromResult<CurrentMembershipResponse?>(null);
            }

            return Task.FromResult<CurrentMembershipResponse?>(
                new CurrentMembershipResponse(tenantId, "North Studio", "north-studio", "Active"));
        }
    }

    private sealed class RecordingStudioSetter(CurrentStudio inner, ResolutionProbe probe) : ICurrentStudioSetter
    {
        public void UseTenant(Guid tenantId)
        {
            inner.UseTenant(tenantId);
            probe.TenantIds.Add(tenantId);
        }
    }

    private sealed class TestUserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!principal.IsAuthenticated)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();
            if (principal.NameIdentifier is not null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, principal.NameIdentifier));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
