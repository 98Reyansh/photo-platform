using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Membership;

public sealed class CurrentMembershipQuery(ApplicationDbContext context) : ICurrentMembershipQuery
{
    public async Task<CurrentMembershipResponse?> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        return await context.Memberships
            .Where(membership => membership.UserId == userId)
            .Join(
                context.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (membership, tenant) => new CurrentMembershipResponse(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
