using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Application.Authentication;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IAntiforgery antiforgery, IStudioSignIn studioSignIn) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CsrfTokenResponse), StatusCodes.Status200OK)]
    public ActionResult<CsrfTokenResponse> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrEmpty(tokens.RequestToken))
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok(new CsrfTokenResponse(tokens.RequestToken));
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SignIn(
        [FromBody] SignInRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await studioSignIn.SignInAsync(request?.Email, request?.Password, cancellationToken);
        if (result != StudioSignInStatus.Succeeded)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed.",
                detail: "The email or password is incorrect.");
        }

        return NoContent();
    }

    [HttpPost("sign-out")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> EndSession()
    {
        await studioSignIn.SignOutAsync();
        return NoContent();
    }
}
