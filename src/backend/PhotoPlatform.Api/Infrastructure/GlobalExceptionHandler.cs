using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PhotoPlatform.Api.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            "Unhandled exception processing {Method} {Path}. ExceptionType: {ExceptionType}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            exception.GetType().Name);

        var requestTooLarge = exception is BadHttpRequestException badRequest
            && badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge;
        var status = requestTooLarge
            ? StatusCodes.Status413PayloadTooLarge
            : StatusCodes.Status500InternalServerError;
        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = requestTooLarge
                    ? PhotoOriginalUploadLimits.RequestTooLargeTitle
                    : "An unexpected error occurred.",
                Detail = requestTooLarge
                    ? PhotoOriginalUploadLimits.RequestTooLargeDetail
                    : "The request could not be completed.",
            },
            cancellationToken);

        return true;
    }
}
