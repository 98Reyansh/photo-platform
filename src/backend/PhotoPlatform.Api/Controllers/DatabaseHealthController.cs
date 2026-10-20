using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Infrastructure;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/health/database")]
[Produces("application/json")]
public sealed class DatabaseHealthController(DatabaseConnectivity connectivity) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DatabaseHealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DatabaseHealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DatabaseHealthResponse>> Get(CancellationToken cancellationToken)
    {
        var connected = await connectivity.CanConnectAsync(cancellationToken);
        var response = new DatabaseHealthResponse
        {
            Status = connected ? "healthy" : "unhealthy",
            Timestamp = DateTime.UtcNow,
        };

        if (!connected)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, response);
        }

        return Ok(response);
    }
}
