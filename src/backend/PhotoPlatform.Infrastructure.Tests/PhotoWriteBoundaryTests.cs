using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoWriteBoundaryTests
{
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused;Timeout=1";

    private static readonly DateTime CreatedAtUtc = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Save_rejects_a_photo_when_the_studio_is_unresolved()
    {
        await using var context = CreateContext(new CurrentStudio());
        var photo = new Photo(Guid.CreateVersion7(), "Harbor", null, CreatedAtUtc);
        context.Photos.Add(photo);

        var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(() => context.SaveChangesAsync());

        Assert.Equal(PhotoWriteBoundaryException.UnresolvedMessage, exception.Message);
        Assert.Equal(EntityState.Added, context.Entry(photo).State);
    }

    [Fact]
    public void Empty_studio_id_rejects_a_photo_write()
    {
        var studio = new FixedStudio { IsResolved = true, TenantId = Guid.Empty };
        using var context = CreateContext(studio);
        context.Photos.Add(new Photo(Guid.CreateVersion7(), "Harbor", null, CreatedAtUtc));

        var exception = Assert.Throws<PhotoWriteBoundaryException>(() => context.EnforcePhotoWriteBoundary());

        Assert.Equal(PhotoWriteBoundaryException.UnresolvedMessage, exception.Message);
    }

    [Fact]
    public void Added_photo_must_use_the_resolved_studio()
    {
        var studioId = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(studioId);
        using var context = CreateContext(studio);
        var photo = new Photo(otherTenant, "Harbor", null, CreatedAtUtc);
        context.Photos.Add(photo);

        var exception = Assert.Throws<PhotoWriteBoundaryException>(() => context.EnforcePhotoWriteBoundary());

        Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
        Assert.Equal(otherTenant, photo.TenantId);
    }

    [Fact]
    public void Matching_photo_changes_are_allowed_without_changing_ownership()
    {
        var studioId = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(studioId);
        using var context = CreateContext(studio);
        var photo = new Photo(studioId, "Harbor", "Dawn", CreatedAtUtc);
        context.Photos.Attach(photo);
        photo.UpdateDetails("Evening", "Night", CreatedAtUtc.AddHours(1));

        context.EnforcePhotoWriteBoundary();

        Assert.Equal(studioId, photo.TenantId);
        Assert.Equal(CreatedAtUtc, photo.CreatedAtUtc);
        Assert.Equal("Evening", photo.Title);
        Assert.Equal(EntityState.Modified, context.Entry(photo).State);
    }

    [Fact]
    public void Changing_photo_ownership_is_rejected_and_not_rewritten()
    {
        var studioId = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(studioId);
        using var context = CreateContext(studio);
        var photo = new Photo(studioId, "Harbor", null, CreatedAtUtc);
        context.Photos.Attach(photo);
        var tenant = context.Entry(photo).Property(item => item.TenantId);
        tenant.CurrentValue = otherTenant;

        var exception = Assert.Throws<PhotoWriteBoundaryException>(() => context.EnforcePhotoWriteBoundary());

        Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
        Assert.Equal(otherTenant, tenant.CurrentValue);
        Assert.Equal(studioId, tenant.OriginalValue);
    }

    [Fact]
    public void Delete_rejects_a_photo_from_another_studio()
    {
        var studioId = Guid.CreateVersion7();
        var otherTenant = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(studioId);
        using var context = CreateContext(studio);
        var photo = new Photo(otherTenant, "Harbor", null, CreatedAtUtc);
        context.Photos.Attach(photo);
        context.Photos.Remove(photo);

        var exception = Assert.Throws<PhotoWriteBoundaryException>(() => context.EnforcePhotoWriteBoundary());

        Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
        Assert.Equal(otherTenant, photo.TenantId);
        Assert.Equal(EntityState.Deleted, context.Entry(photo).State);
    }

    [Fact]
    public void Delete_allows_a_photo_owned_by_the_resolved_studio()
    {
        var studioId = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(studioId);
        using var context = CreateContext(studio);
        var photo = new Photo(studioId, "Harbor", null, CreatedAtUtc);
        context.Photos.Attach(photo);
        context.Photos.Remove(photo);

        context.EnforcePhotoWriteBoundary();

        Assert.Equal(EntityState.Deleted, context.Entry(photo).State);
    }

    [Fact]
    public void Tenant_and_membership_changes_are_not_blocked_by_the_photo_boundary()
    {
        using var context = CreateContext(new CurrentStudio());
        context.Tenants.Add(new Tenant("North Studio", "north-studio", CreatedAtUtc));
        context.Memberships.Add(new StudioMembership(Guid.CreateVersion7(), Guid.CreateVersion7(), CreatedAtUtc));

        context.EnforcePhotoWriteBoundary();

        Assert.Equal(2, context.ChangeTracker.Entries().Count());
    }

    private static ApplicationDbContext CreateContext(ICurrentStudio studio)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(UnreachableConnection);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(UnreachableConnection)
            .Options;
        return new ApplicationDbContext(options, studio);
    }

    private sealed class FixedStudio : ICurrentStudio
    {
        public Guid TenantId { get; set; }

        public bool IsResolved { get; set; }
    }
}
