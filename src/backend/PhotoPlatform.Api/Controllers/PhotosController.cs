using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/photos")]
[Produces("application/json")]
public sealed class PhotosController(
    IPhotoCommands photos,
    IPhotoOriginalUpload originals,
    PhotoOriginalMultipartReader multipart) : ControllerBase
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

    [HttpPost("originals")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(PhotoOriginalUploadLimits.MaxRequestBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = PhotoOriginalUploadLimits.MaxRequestBytes,
        MultipartBoundaryLengthLimit = PhotoOriginalUploadLimits.MaxBoundaryLength,
        MultipartHeadersCountLimit = PhotoOriginalUploadLimits.MaxHeadersCount,
        MultipartHeadersLengthLimit = PhotoOriginalUploadLimits.MaxHeadersLength,
        ValueCountLimit = PhotoOriginalUploadLimits.MaxSections,
        ValueLengthLimit = PhotoOriginalUploadLimits.MaxDescriptionBytes,
        KeyLengthLimit = PhotoOriginalUploadLimits.MaxKeyLength)]
    [ProducesResponseType(typeof(PhotoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<PhotoResponse>> UploadOriginal(CancellationToken cancellationToken)
    {
        string? spool = null;
        try
        {
            var read = await multipart.ReadAsync(Request, cancellationToken);
            switch (read)
            {
                case PhotoOriginalMultipartResult.RequestTooLarge:
                    return RequestTooLarge();
                case PhotoOriginalMultipartResult.Invalid invalid:
                    return UploadInvalid(invalid.Detail);
                case PhotoOriginalMultipartResult.Ready ready:
                    spool = ready.SpoolPath;
                    await using (var content = new FileStream(
                        ready.SpoolPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        PhotoOriginalUploadLimits.ReadBufferBytes,
                        FileOptions.Asynchronous))
                    {
                        var uploaded = await originals.UploadAsync(
                            ready.Title,
                            ready.Description,
                            content,
                            cancellationToken);
                        return MapUpload(uploaded);
                    }
                default:
                    return Unexpected();
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return RequestTooLarge();
        }
        finally
        {
            DeleteSpool(spool);
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(PhotoListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PhotoListResponse>> List(
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await photos.ListAsync(limit, cursor, cancellationToken);
        if (result.Status == PhotoCommandStatus.Success && result.Page is not null)
        {
            return Ok(new PhotoListResponse(result.Page.Items, result.Page.ContinuationCursor));
        }

        return MapListFailure(result);
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

    private ActionResult MapListFailure(PhotoListResult result)
    {
        return ProblemFor(result.Status, result.Error, "The photo list request is invalid.");
    }

    private ActionResult MapFailure(PhotoCommandResult result)
    {
        return ProblemFor(result.Status, result.Error, "The photo details are invalid.");
    }

    private ActionResult<PhotoResponse> MapUpload(PhotoOriginalUploadResult result)
    {
        return result switch
        {
            PhotoOriginalUploadResult.Created created => Created($"/api/v1/photos/{created.Photo.Id}", created.Photo),
            PhotoOriginalUploadResult.UnresolvedStudio => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Studio access was not found.",
                detail: UnresolvedDetail),
            PhotoOriginalUploadResult.Invalid invalid => UploadInvalid(invalid.Message),
            PhotoOriginalUploadResult.Rejected rejected => UploadInvalid(DetailFor(rejected.Decision)),
            _ => Unexpected(),
        };
    }

    private static string DetailFor(PrivatePhotoContentDecision decision)
    {
        return decision switch
        {
            PrivatePhotoContentDecision.Empty => PhotoOriginalUploadLimits.EmptyFileDetail,
            PrivatePhotoContentDecision.TooLarge => PhotoOriginalUploadLimits.FileTooLargeDetail,
            PrivatePhotoContentDecision.Unrecognized => PhotoOriginalUploadLimits.UnsupportedFormatDetail,
            PrivatePhotoContentDecision.Dimensions => PhotoOriginalUploadLimits.DimensionsDetail,
            _ => PhotoOriginalUploadLimits.UnreadableImageDetail,
        };
    }

    private ActionResult UploadInvalid(string detail)
    {
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: PhotoOriginalUploadLimits.InvalidTitle,
            detail: detail);
    }

    private ActionResult RequestTooLarge()
    {
        return Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: PhotoOriginalUploadLimits.RequestTooLargeTitle,
            detail: PhotoOriginalUploadLimits.RequestTooLargeDetail);
    }

    private ActionResult Unexpected()
    {
        return Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.",
            detail: "The request could not be completed.");
    }

    private static void DeleteSpool(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            return;
        }

        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private ActionResult ProblemFor(PhotoCommandStatus status, string? error, string invalidTitle)
    {
        return status switch
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
                title: invalidTitle,
                detail: error),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.",
                detail: "The request could not be completed."),
        };
    }
}
