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
using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoEndpointTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused;Timeout=1";

    private static readonly DateTime CreatedAtUtc = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Anonymous_photo_request_is_rejected()
    {
        await using var factory = new PhotoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/photos/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Unresolved_studio_is_rejected_before_photo_access(string method)
    {
        var clientTenant = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = Guid.CreateVersion7().ToString();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var unsafeMethod = method != "GET";
        var url = method == "POST"
            ? "/api/v1/photos"
            : $"/api/v1/photos/{Guid.CreateVersion7()}";

        var response = await SendAsync(
            client,
            new HttpMethod(method),
            url,
            new { title = "Harbor", description = "Dawn", tenantId = clientTenant },
            csrf: unsafeMethod);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(PhotosController.UnresolvedDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(PhotosController.NotFoundDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain(clientTenant.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_photo_details_are_rejected_without_using_a_client_tenant()
    {
        var userId = Guid.CreateVersion7();
        var studioTenant = Guid.CreateVersion7();
        var clientTenant = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory();
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = studioTenant;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/photos",
            new { title = "   ", description = "Dawn", tenantId = clientTenant },
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title is required.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(clientTenant.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task State_changing_photo_request_without_csrf_is_rejected()
    {
        var userId = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory { UseRecordingCommands = true };
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = Guid.CreateVersion7();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/photos",
            new { title = "Harbor", description = "Dawn" },
            csrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Recording.Calls);
    }

    [Fact]
    public async Task Create_returns_the_studio_tenant_and_ignores_a_client_tenant()
    {
        var userId = Guid.CreateVersion7();
        var studioTenant = Guid.CreateVersion7();
        var clientTenant = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory { UseRecordingCommands = true };
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = studioTenant;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        factory.Recording.PhotoId = photoId;
        factory.Recording.StudioTenantId = studioTenant;

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/photos",
            new { title = "  Harbor  ", description = " Dawn ", tenantId = clientTenant },
            csrf: true);
        var photo = await response.Content.ReadFromJsonAsync<PhotoResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(photo);
        Assert.Equal(photoId, photo.Id);
        Assert.Equal(studioTenant, photo.TenantId);
        Assert.Equal("  Harbor  ", factory.Recording.Title);
        Assert.Equal(" Dawn ", factory.Recording.Description);
        Assert.EndsWith($"/api/v1/photos/{photoId}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain(clientTenant, new[] { photo.TenantId });
    }

    [Fact]
    public async Task Unknown_and_other_studio_photo_ids_return_the_same_not_found_response()
    {
        var userId = Guid.CreateVersion7();
        var studioTenant = Guid.CreateVersion7();
        var ownedId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory { UseRecordingCommands = true };
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = studioTenant;
        using var client = factory.CreateClient();
        factory.Recording.PhotoId = ownedId;
        factory.Recording.StudioTenantId = studioTenant;
        factory.Recording.Title = "Harbor";
        factory.Recording.Description = "Dawn";

        var owned = await client.GetAsync($"/api/v1/photos/{ownedId}");
        var other = await client.GetAsync($"/api/v1/photos/{otherId}");
        var unknown = await client.GetAsync($"/api/v1/photos/{Guid.CreateVersion7()}");
        var ownedPhoto = await owned.Content.ReadFromJsonAsync<PhotoResponse>();
        var otherBody = await other.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        Assert.NotNull(ownedPhoto);
        Assert.Equal(studioTenant, ownedPhoto.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains(PhotosController.NotFoundDetail, otherBody, StringComparison.Ordinal);
        Assert.Contains(PhotosController.NotFoundDetail, unknownBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Harbor", otherBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Harbor", unknownBody, StringComparison.Ordinal);
        Assert.DoesNotContain(otherId.ToString(), otherBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(studioTenant.ToString(), otherBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_and_delete_use_the_route_id_and_request_text_only()
    {
        var userId = Guid.CreateVersion7();
        var studioTenant = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        var clientTenant = Guid.CreateVersion7();
        await using var factory = new PhotoApiFactory { UseRecordingCommands = true };
        factory.Principal.IsAuthenticated = true;
        factory.Principal.NameIdentifier = userId.ToString();
        factory.Membership.TenantsByUser[userId] = studioTenant;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        factory.Recording.PhotoId = photoId;
        factory.Recording.StudioTenantId = studioTenant;

        var updated = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/photos/{photoId}?tenantId={clientTenant}",
            new { title = "Evening", description = "Night", tenantId = clientTenant },
            csrf: true);
        var photo = await updated.Content.ReadFromJsonAsync<PhotoResponse>();
        var deleted = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/photos/{photoId}",
            body: null,
            csrf: true);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotNull(photo);
        Assert.Equal(photoId, photo.Id);
        Assert.Equal(studioTenant, photo.TenantId);
        Assert.Equal("Evening", photo.Title);
        Assert.Equal("Night", photo.Description);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["update", "delete"], factory.Recording.Calls);
        Assert.Equal(photoId, factory.Recording.LastPhotoId);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        bool csrf)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrf)
        {
            request.Headers.Add(StudioAuthenticationExtensions.CsrfHeaderName, await ReadCsrfAsync(client));
        }

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

    private sealed class PhotoApiFactory : WebApplicationFactory<Program>
    {
        public TestPrincipal Principal { get; } = new();

        public MembershipDouble Membership { get; } = new();

        public RecordingPhotoCommands Recording { get; } = new();

        public bool UseRecordingCommands { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                PostgreSqlTestGuard.EnsureSafeToUse(UnreachableConnection);
                foreach (var descriptor in services.Where(service =>
                    service.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                    || service.ServiceType == typeof(DbContextOptions)
                    || service.ServiceType == typeof(ApplicationDbContext)
                    || service.ServiceType == typeof(ICurrentMembershipQuery)
                    || (UseRecordingCommands && service.ServiceType == typeof(IPhotoCommands))).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(UnreachableConnection));
                services.AddSingleton(Principal);
                services.AddSingleton(Membership);
                services.AddSingleton(Recording);
                services.AddScoped<ICurrentMembershipQuery>(provider =>
                    new FakeMembershipQuery(provider.GetRequiredService<MembershipDouble>()));
                if (UseRecordingCommands)
                {
                    services.AddSingleton<IPhotoCommands>(provider => provider.GetRequiredService<RecordingPhotoCommands>());
                }

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

    private sealed class MembershipDouble
    {
        public Dictionary<Guid, Guid> TenantsByUser { get; } = [];
    }

    private sealed class FakeMembershipQuery(MembershipDouble state) : ICurrentMembershipQuery
    {
        public Task<CurrentMembershipResponse?> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!state.TenantsByUser.TryGetValue(userId, out var tenantId))
            {
                return Task.FromResult<CurrentMembershipResponse?>(null);
            }

            return Task.FromResult<CurrentMembershipResponse?>(
                new CurrentMembershipResponse(tenantId, "North Studio", "north-studio", "Active"));
        }
    }

    private sealed class RecordingPhotoCommands : IPhotoCommands
    {
        public List<string> Calls { get; } = [];

        public Guid PhotoId { get; set; }

        public Guid StudioTenantId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public Guid LastPhotoId { get; private set; }

        public Task<PhotoCommandResult> CreateAsync(
            string? title,
            string? description,
            CancellationToken cancellationToken)
        {
            Calls.Add("create");
            Title = title;
            Description = description;
            return Task.FromResult(PhotoCommandResult.Success(Sample(PhotoId, title, description)));
        }

        public Task<PhotoCommandResult> GetAsync(Guid photoId, CancellationToken cancellationToken)
        {
            Calls.Add("get");
            LastPhotoId = photoId;
            if (photoId != PhotoId)
            {
                return Task.FromResult(PhotoCommandResult.NotFound());
            }

            return Task.FromResult(PhotoCommandResult.Success(Sample(photoId, Title, Description)));
        }

        public Task<PhotoCommandResult> UpdateAsync(
            Guid photoId,
            string? title,
            string? description,
            CancellationToken cancellationToken)
        {
            Calls.Add("update");
            LastPhotoId = photoId;
            Title = title;
            Description = description;
            return Task.FromResult(PhotoCommandResult.Success(Sample(photoId, title, description)));
        }

        public Task<PhotoCommandResult> DeleteAsync(Guid photoId, CancellationToken cancellationToken)
        {
            Calls.Add("delete");
            LastPhotoId = photoId;
            return Task.FromResult(PhotoCommandResult.Success(photo: null));
        }

        private PhotoResponse Sample(Guid photoId, string? title, string? description)
        {
            return new PhotoResponse(
                photoId,
                StudioTenantId,
                title ?? string.Empty,
                description,
                CreatedAtUtc,
                CreatedAtUtc);
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
