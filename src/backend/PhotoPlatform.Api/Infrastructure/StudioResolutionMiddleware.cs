using System.Security.Claims;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Application.Tenancy;

namespace PhotoPlatform.Api.Infrastructure;

public sealed class StudioResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentMembershipQuery membershipQuery,
        ICurrentStudioSetter studio)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdValue, out var userId) && userId != Guid.Empty)
            {
                var membership = await membershipQuery.GetForUserAsync(userId, context.RequestAborted);
                if (membership is { TenantId: var tenantId } && tenantId != Guid.Empty)
                {
                    studio.UseTenant(tenantId);
                }
            }
        }

        await next(context);
    }
}
