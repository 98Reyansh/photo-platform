using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public sealed class OwnerSessionTests : IAsyncLifetime
{
    private const string Password = "Studio-Test-Password-1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private OwnerSessionApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        Assert.DoesNotContain("photo_platform", _connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
        await _connection.OpenAsync();
        _factory = new OwnerSessionApiFactory(_connection);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Contains("Sqlite", context.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Owner_cookie_session_survives_requests_and_sign_out_clears_it()
    {
        var owner = await CreateOwnerAsync("Lensora Studio", "lensora-studio", "North Light");
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/membership/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/photos")).StatusCode);

        var wrong = await PostSignInAsync(client, owner.Email, "Wrong-Password-1");
        AssertGenericFailure(wrong, await wrong.Content.ReadAsStringAsync(), "Wrong-Password-1");

        var unknown = await PostSignInAsync(client, $"missing-{Guid.NewGuid():N}@example.test", Password);
        AssertGenericFailure(unknown, await unknown.Content.ReadAsStringAsync(), Password);

        var signIn = await PostSignInAsync(client, owner.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.DoesNotContain(Password, await signIn.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var authCookie = signIn.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("PhotoPlatform.Auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", authCookie, StringComparison.OrdinalIgnoreCase);

        using var copied = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var copiedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/membership/current");
        copiedRequest.Headers.TryAddWithoutValidation("Cookie", authCookie.Split(';', 2)[0]);
        var copiedResponse = await copied.SendAsync(copiedRequest);
        var copiedMembership = await copiedResponse.Content.ReadFromJsonAsync<CurrentMembershipResponse>();
        Assert.Equal(HttpStatusCode.OK, copiedResponse.StatusCode);
        Assert.Equal(owner.TenantId, copiedMembership?.TenantId);

        using var suppliedTenant = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/membership/current?tenantId={Guid.CreateVersion7()}&slug=other-studio");
        suppliedTenant.Headers.Add(MembershipController.ClientTenantHeader, Guid.CreateVersion7().ToString());
        var current = await client.SendAsync(suppliedTenant);
        var currentBody = await current.Content.ReadAsStringAsync();
        var membership = JsonSerializer.Deserialize<CurrentMembershipResponse>(currentBody, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(owner.TenantId, membership?.TenantId);
        Assert.Equal("Lensora Studio", membership?.Name);
        Assert.DoesNotContain("PasswordHash", currentBody, StringComparison.Ordinal);

        var again = await client.GetFromJsonAsync<CurrentMembershipResponse>("/api/v1/membership/current");
        Assert.Equal(owner.TenantId, again?.TenantId);

        var rejectedSignOut = await client.PostAsync("/api/v1/auth/sign-out", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedSignOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/membership/current")).StatusCode);

        var csrf = await ReadCsrfAsync(client);
        using var signOut = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out");
        signOut.Headers.Add("X-CSRF-TOKEN", csrf);
        var signedOut = await client.SendAsync(signOut);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/membership/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/photos")).StatusCode);
    }

    [Fact]
    public async Task User_without_membership_cannot_sign_in_or_read_photos()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await users.CreateAsync(new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
        }, Password);
        Assert.True(created.Succeeded);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await PostSignInAsync(client, email, Password);
        AssertGenericFailure(response, await response.Content.ReadAsStringAsync(), Password);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/photos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/membership/current")).StatusCode);
    }

    [Fact]
    public async Task Signed_in_owner_cannot_read_another_studios_photo()
    {
        var owner = await CreateOwnerAsync("Lensora Studio", "lensora-studio", "North Light");
        var other = await CreateOwnerAsync("Other Studio", "other-studio", "South Ridge");
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var signIn = await PostSignInAsync(client, owner.Email, Password);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/photos?tenantId={other.TenantId}");
        listRequest.Headers.Add(MembershipController.ClientTenantHeader, other.TenantId.ToString());
        var list = await client.SendAsync(listRequest);
        var listBody = await list.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<PhotoListResponse>(listBody, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.NotNull(page);
        Assert.Equal([owner.PhotoId], page.Items.Select(item => item.Id).ToArray());
        Assert.Equal(owner.TenantId, Assert.Single(page.Items).TenantId);
        Assert.DoesNotContain("South Ridge", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(other.TenantId.ToString(), listBody, StringComparison.OrdinalIgnoreCase);

        var hidden = await client.GetAsync($"/api/v1/photos/{other.PhotoId}");
        var hiddenBody = await hidden.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Contains(PhotosController.NotFoundDetail, hiddenBody, StringComparison.Ordinal);
        Assert.DoesNotContain("South Ridge", hiddenBody, StringComparison.Ordinal);
        Assert.DoesNotContain(other.TenantId.ToString(), hiddenBody, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OwnerFixture> CreateOwnerAsync(string studioName, string slug, string photoTitle)
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var studio = scope.ServiceProvider.GetRequiredService<ICurrentStudioSetter>();
        var tenant = new Tenant(studioName, slug, DateTime.UtcNow);
        context.Tenants.Add(tenant);
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
        context.Memberships.Add(new StudioMembership(user.Id, tenant.Id, DateTime.UtcNow));
        studio.UseTenant(tenant.Id);
        var photo = new Photo(tenant.Id, photoTitle, null, DateTime.UtcNow);
        context.Photos.Add(photo);
        await context.SaveChangesAsync();
        return new OwnerFixture(email, tenant.Id, photo.Id);
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

    private static void AssertGenericFailure(HttpResponseMessage response, string body, string submittedPassword)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("The email or password is incorrect.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(submittedPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("membership", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.Ordinal);
    }

    private sealed record OwnerFixture(string Email, Guid TenantId, Guid PhotoId);

    private sealed class OwnerSessionApiFactory(SqliteConnection connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                var replaced = new[]
                {
                    typeof(DbContextOptions<ApplicationDbContext>),
                    typeof(DbContextOptions),
                    typeof(ApplicationDbContext),
                    typeof(IDbContextOptionsConfiguration<ApplicationDbContext>),
                };
                foreach (var descriptor in services.Where(service => replaced.Contains(service.ServiceType)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            });
        }
    }
}
