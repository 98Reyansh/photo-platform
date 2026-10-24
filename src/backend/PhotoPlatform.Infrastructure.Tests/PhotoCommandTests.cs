using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoCommandTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused;Timeout=1";

    [Fact]
    public void Photo_commands_depend_on_the_studio_reader_only()
    {
        var parameters = typeof(PhotoCommands).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ICurrentStudio), parameters);
        Assert.DoesNotContain(typeof(ICurrentStudioSetter), parameters);
        Assert.Null(typeof(CreatePhotoRequest).GetProperty("TenantId"));
        Assert.Null(typeof(UpdatePhotoRequest).GetProperty("TenantId"));
    }

    [Fact]
    public async Task Unresolved_studio_returns_before_photo_access()
    {
        var studio = new CurrentStudio();
        await using var context = CreateContext(studio);
        var commands = new PhotoCommands(context, studio);
        var photoId = Guid.CreateVersion7();

        Assert.Equal(PhotoCommandStatus.UnresolvedStudio, (await commands.GetAsync(photoId, CancellationToken.None)).Status);
        Assert.Equal(
            PhotoCommandStatus.UnresolvedStudio,
            (await commands.CreateAsync("Harbor", null, CancellationToken.None)).Status);
        Assert.Equal(
            PhotoCommandStatus.UnresolvedStudio,
            (await commands.UpdateAsync(photoId, "Harbor", null, CancellationToken.None)).Status);
        Assert.Equal(
            PhotoCommandStatus.UnresolvedStudio,
            (await commands.DeleteAsync(photoId, CancellationToken.None)).Status);
        Assert.Empty(context.ChangeTracker.Entries<Photo>());
    }

    [Fact]
    public async Task Invalid_create_does_not_track_a_photo()
    {
        var studio = new CurrentStudio();
        studio.UseTenant(Guid.CreateVersion7());
        await using var context = CreateContext(studio);
        var commands = new PhotoCommands(context, studio);

        var result = await commands.CreateAsync("   ", "Dawn", CancellationToken.None);

        Assert.Equal(PhotoCommandStatus.Invalid, result.Status);
        Assert.Contains("Title is required.", result.Error, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries<Photo>());
    }

    private static ApplicationDbContext CreateContext(ICurrentStudio studio)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(UnreachableConnection);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        return new ApplicationDbContext(options, studio);
    }
}
