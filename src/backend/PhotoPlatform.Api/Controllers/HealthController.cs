using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(new HealthResponse
        {
            Status = "healthy",
            Timestamp = DateTime.UtcNow,
        });
    }
}
