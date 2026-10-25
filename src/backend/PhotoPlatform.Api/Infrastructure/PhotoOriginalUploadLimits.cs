using Microsoft.AspNetCore.Server.Kestrel.Core;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Api.Infrastructure;

public static class PhotoOriginalUploadLimits
{
    public const long MaxFileBytes = PrivatePhotoContentPolicy.MaxBytes;

    public const long MaxRequestBytes = 26_279_936;

    public const int MaxBoundaryLength = 70;

    public const int MaxHeadersCount = 8;

    public const int MaxHeadersLength = 4_096;

    public const int MaxSections = 3;

    public const int MaxKeyLength = 32;

    public const int MaxTitleBytes = Photo.TitleMaxLength * 4;

    public const int MaxDescriptionBytes = Photo.DescriptionMaxLength * 4;

    public const int ReadBufferBytes = 16 * 1024;

    public const string InvalidTitle = "The upload is invalid.";

    public const string MultipartRequiredDetail = "The upload must use multipart form data.";

    public const string UnreadableUploadDetail = "The upload could not be read.";

    public const string InvalidTextDetail = "The upload contains invalid text.";

    public const string MissingFileDetail = "A file is required.";

    public const string EmptyFileDetail = "The file is empty.";

    public const string FileTooLargeDetail = "The file is too large.";

    public const string DuplicatePartDetail = "The upload contains a duplicate part.";

    public const string UnsupportedPartDetail = "The upload contains an unsupported part.";

    public const string UnsupportedFormatDetail = "The file format is not supported.";

    public const string UnreadableImageDetail = "The image could not be read.";

    public const string DimensionsDetail = "The image dimensions are not supported.";

    public const string RequestTooLargeTitle = "The upload is too large.";

    public const string RequestTooLargeDetail = "The request exceeds the maximum upload size.";

    public const string SpoolUnavailable = "The upload spool is not available.";

    public static void ConfigureKestrel(KestrelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Limits.MaxRequestBodySize = MaxRequestBytes;
    }
}
