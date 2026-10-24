using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Application.Membership;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/membership")]
[Produces("application/json")]
public sealed class MembershipController(ICurrentMembershipQuery membershipQuery) : ControllerBase
{
    public const string ClientTenantHeader = "X-Tenant-Id";

    [HttpGet("current")]
    [ProducesResponseType(typeof(CurrentMembershipResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrentMembershipResponse>> Current(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        var membership = await membershipQuery.GetForUserAsync(userId, cancellationToken);
        if (membership is null)
        {
            return Forbid();
        }

        return Ok(membership);
    }
}
