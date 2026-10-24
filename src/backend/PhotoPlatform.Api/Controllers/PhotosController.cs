using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Application.Photos;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/photos")]
[Produces("application/json")]
public sealed class PhotosController(IPhotoCommands photos) : ControllerBase
{
    public const string NotFoundDetail = "The photo was not found.";

    public const string UnresolvedDetail = "The current user does not have a studio membership.";

    [HttpPost]
    [ProducesResponseType(typeof(PhotoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PhotoResponse>> Create(
        [FromBody] CreatePhotoRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await photos.CreateAsync(request?.Title, request?.Description, cancellationToken);
        return MapWrite(result, created: true);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PhotoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PhotoResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await photos.GetAsync(id, cancellationToken);
        return MapRead(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PhotoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PhotoResponse>> Update(
        Guid id,
        [FromBody] UpdatePhotoRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await photos.UpdateAsync(id, request?.Title, request?.Description, cancellationToken);
        return MapWrite(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await photos.DeleteAsync(id, cancellationToken);
        if (result.Status == PhotoCommandStatus.Success)
        {
            return NoContent();
        }

        return MapFailure(result);
    }

    private ActionResult<PhotoResponse> MapRead(PhotoCommandResult result)
    {
        if (result.Status == PhotoCommandStatus.Success && result.Photo is not null)
        {
            return Ok(result.Photo);
        }

        return MapFailure(result);
    }

    private ActionResult<PhotoResponse> MapWrite(PhotoCommandResult result, bool created)
    {
        if (result.Status == PhotoCommandStatus.Success && result.Photo is not null)
        {
            if (created)
            {
                return Created($"/api/v1/photos/{result.Photo.Id}", result.Photo);
            }

            return Ok(result.Photo);
        }

        return MapFailure(result);
    }

    private ActionResult MapFailure(PhotoCommandResult result)
    {
        return result.Status switch
        {
            PhotoCommandStatus.UnresolvedStudio => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Studio access was not found.",
                detail: UnresolvedDetail),
            PhotoCommandStatus.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: NotFoundDetail,
                detail: NotFoundDetail),
            PhotoCommandStatus.Invalid => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The photo details are invalid.",
                detail: result.Error),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.",
                detail: "The request could not be completed."),
        };
    }
}
