using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Identity;

namespace PhotoPlatform.Infrastructure.Persistence;

public sealed class ApplicationDbContext : IdentityUserContext<ApplicationUser, Guid>
{
    private readonly ICurrentStudio _currentStudio;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentStudio currentStudio)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(currentStudio);
        _currentStudio = currentStudio;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<StudioMembership> Memberships => Set<StudioMembership>();

    public DbSet<Photo> Photos => Set<Photo>();

    public Guid CurrentTenantId => _currentStudio.TenantId;

    public bool IsStudioResolved => _currentStudio.IsResolved;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        modelBuilder.Entity<Photo>().HasQueryFilter(photo =>
            IsStudioResolved && photo.TenantId == CurrentTenantId);
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforcePhotoWriteBoundary();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforcePhotoWriteBoundary();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    internal void EnforcePhotoWriteBoundary()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries<Photo>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (!IsStudioResolved || CurrentTenantId == Guid.Empty)
            {
                throw new PhotoWriteBoundaryException(PhotoWriteBoundaryException.UnresolvedMessage);
            }

            var tenant = entry.Property(photo => photo.TenantId);
            var allowed = entry.State switch
            {
                EntityState.Added => tenant.CurrentValue == CurrentTenantId,
                EntityState.Modified => tenant.OriginalValue == CurrentTenantId
                    && tenant.CurrentValue == CurrentTenantId,
                EntityState.Deleted => tenant.OriginalValue == CurrentTenantId
                    && tenant.CurrentValue == CurrentTenantId,
                _ => false,
            };

            if (!allowed)
            {
                throw new PhotoWriteBoundaryException(PhotoWriteBoundaryException.MismatchMessage);
            }
        }
    }
}
