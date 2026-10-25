using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Api.Infrastructure;

public sealed class PhotoOriginalMultipartReader(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptions<PhotoUploadSpoolOptions> spoolOptions,
    PhotoUploadSpoolGate spoolGate,
    ILogger<PhotoOriginalMultipartReader> logger)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<PhotoOriginalMultipartResult> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContentLength is long declared && declared > PhotoOriginalUploadLimits.MaxRequestBytes)
        {
            return new PhotoOriginalMultipartResult.RequestTooLarge();
        }

        if (!TryGetBoundary(request.ContentType, out var boundary, out var boundaryError))
        {
            return new PhotoOriginalMultipartResult.Invalid(boundaryError);
        }

        var counting = new CountingReadStream(request.Body);
        var reader = new MultipartReader(boundary, counting, PhotoOriginalUploadLimits.ReadBufferBytes)
        {
            HeadersCountLimit = PhotoOriginalUploadLimits.MaxHeadersCount,
            HeadersLengthLimit = PhotoOriginalUploadLimits.MaxHeadersLength,
            BodyLengthLimit = PhotoOriginalUploadLimits.MaxRequestBytes,
        };

        string? title = null;
        string? description = null;
        string? spool = null;
        var hasTitle = false;
        var hasDescription = false;
        var hasFile = false;
        var keepSpool = false;
        var sections = 0;

        try
        {
            while (true)
            {
                MultipartSection? section;
                try
                {
                    section = await reader.ReadNextSectionAsync(cancellationToken);
                }
                catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
                {
                    return new PhotoOriginalMultipartResult.RequestTooLarge();
                }
                catch (InvalidDataException)
                {
                    return FailureForRead(counting.Count);
                }

                if (section is null)
                {
                    break;
                }

                if (counting.Count > PhotoOriginalUploadLimits.MaxRequestBytes)
                {
                    return new PhotoOriginalMultipartResult.RequestTooLarge();
                }

                sections++;
                if (sections > PhotoOriginalUploadLimits.MaxSections || !TryGetPartName(section, out var name))
                {
                    return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.UnsupportedPartDetail);
                }

                switch (name)
                {
                    case "title":
                        if (hasTitle)
                        {
                            return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.DuplicatePartDetail);
                        }

                        var titleRead = await ReadTextAsync(
                            section.Body,
                            counting,
                            PhotoOriginalUploadLimits.MaxTitleBytes,
                            cancellationToken);
                        if (titleRead.Failure is not null)
                        {
                            return titleRead.Failure;
                        }

                        title = titleRead.Text;
                        hasTitle = true;
                        break;
                    case "description":
                        if (hasDescription)
                        {
                            return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.DuplicatePartDetail);
                        }

                        var descriptionRead = await ReadTextAsync(
                            section.Body,
                            counting,
                            PhotoOriginalUploadLimits.MaxDescriptionBytes,
                            cancellationToken);
                        if (descriptionRead.Failure is not null)
                        {
                            return descriptionRead.Failure;
                        }

                        description = descriptionRead.Text;
                        hasDescription = true;
                        break;
                    case "file":
                        if (hasFile)
                        {
                            return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.DuplicatePartDetail);
                        }

                        spool = CreateSpoolPath();
                        var fileRead = await ReadFileAsync(section.Body, counting, spool, cancellationToken);
                        if (fileRead is not null)
                        {
                            return fileRead;
                        }

                        hasFile = true;
                        break;
                    default:
                        return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.UnsupportedPartDetail);
                }
            }

            if (counting.Count > PhotoOriginalUploadLimits.MaxRequestBytes)
            {
                return new PhotoOriginalMultipartResult.RequestTooLarge();
            }

            if (!hasFile || spool is null)
            {
                return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.MissingFileDetail);
            }

            if (!hasTitle)
            {
                return new PhotoOriginalMultipartResult.Invalid("Title is required.");
            }

            if (spoolGate.BeforeRelease is not null)
            {
                await spoolGate.BeforeRelease(cancellationToken);
            }

            keepSpool = true;
            return new PhotoOriginalMultipartResult.Ready(title, description, spool);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new PhotoOriginalMultipartResult.RequestTooLarge();
        }
        catch (InvalidDataException)
        {
            return FailureForRead(counting.Count);
        }
        catch (IOException)
        {
            return FailureForRead(counting.Count);
        }
        finally
        {
            if (!keepSpool)
            {
                DeleteQuietly(spool);
            }
        }
    }

    private async Task<TextRead> ReadTextAsync(
        Stream body,
        CountingReadStream counting,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        while (read <= maxBytes)
        {
            int count;
            try
            {
                count = await body.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return new TextRead(null, new PhotoOriginalMultipartResult.RequestTooLarge());
            }

            if (count == 0)
            {
                break;
            }

            read += count;
            if (counting.Count > PhotoOriginalUploadLimits.MaxRequestBytes)
            {
                return new TextRead(null, new PhotoOriginalMultipartResult.RequestTooLarge());
            }

            if (read > maxBytes)
            {
                var tooLong = maxBytes == PhotoOriginalUploadLimits.MaxTitleBytes
                    ? $"Title must be at most {Photo.TitleMaxLength} characters."
                    : $"Description must be at most {Photo.DescriptionMaxLength} characters.";
                return new TextRead(null, new PhotoOriginalMultipartResult.Invalid(tooLong));
            }
        }

        try
        {
            return new TextRead(StrictUtf8.GetString(buffer.AsSpan(0, read)), null);
        }
        catch (DecoderFallbackException)
        {
            return new TextRead(null, new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.InvalidTextDetail));
        }
    }

    private static async Task<PhotoOriginalMultipartResult?> ReadFileAsync(
        Stream body,
        CountingReadStream counting,
        string path,
        CancellationToken cancellationToken)
    {
        long written = 0;
        var buffer = new byte[PhotoOriginalUploadLimits.ReadBufferBytes];
        await using var file = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            PhotoOriginalUploadLimits.ReadBufferBytes,
            FileOptions.Asynchronous);
        while (true)
        {
            int count;
            try
            {
                count = await body.ReadAsync(buffer, cancellationToken);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return new PhotoOriginalMultipartResult.RequestTooLarge();
            }

            if (count == 0)
            {
                break;
            }

            written += count;
            if (counting.Count > PhotoOriginalUploadLimits.MaxRequestBytes)
            {
                return new PhotoOriginalMultipartResult.RequestTooLarge();
            }

            if (written > PhotoOriginalUploadLimits.MaxFileBytes)
            {
                return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.FileTooLargeDetail);
            }

            await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        if (written == 0)
        {
            return new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.EmptyFileDetail);
        }

        return null;
    }

    private string CreateSpoolPath()
    {
        var configured = spoolOptions.Value.Directory;
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "photo-platform-upload")
            : configured;
        directory = Path.GetFullPath(directory);
        if (Overlaps(environment.ContentRootPath, directory)
            || Overlaps(environment.WebRootPath, directory)
            || Overlaps(configuration["PrivatePhotoStorage:RootPath"], directory))
        {
            throw new InvalidOperationException(PhotoOriginalUploadLimits.SpoolUnavailable);
        }

        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part"));
        if (!IsInside(directory, path))
        {
            throw new InvalidOperationException(PhotoOriginalUploadLimits.SpoolUnavailable);
        }

        return path;
    }

    private void DeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "Upload spool cleanup failed. ExceptionType: {ExceptionType}",
                exception.GetType().Name);
        }
    }

    private static PhotoOriginalMultipartResult FailureForRead(long requestBytes)
    {
        return requestBytes > PhotoOriginalUploadLimits.MaxRequestBytes
            ? new PhotoOriginalMultipartResult.RequestTooLarge()
            : new PhotoOriginalMultipartResult.Invalid(PhotoOriginalUploadLimits.UnreadableUploadDetail);
    }

    private static bool TryGetBoundary(string? contentType, out string boundary, out string error)
    {
        boundary = string.Empty;
        error = PhotoOriginalUploadLimits.MultipartRequiredDetail;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var media)
            || !string.Equals(media.MediaType.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(media.Boundary).ToString();
        if (string.IsNullOrEmpty(value)
            || value.Length > PhotoOriginalUploadLimits.MaxBoundaryLength
            || value.Any(character => char.IsWhiteSpace(character) || character is '"' or '\r' or '\n'))
        {
            error = PhotoOriginalUploadLimits.UnreadableUploadDetail;
            return false;
        }

        boundary = value;
        return true;
    }

    private static bool TryGetPartName(MultipartSection section, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrEmpty(section.ContentDisposition)
            || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(disposition.Name).ToString();
        if (value.Length == 0 || value.Length > PhotoOriginalUploadLimits.MaxKeyLength)
        {
            return false;
        }

        name = value;
        return true;
    }

    private static bool Overlaps(string? root, string directory)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var full = Path.GetFullPath(root);
        return IsInside(full, directory) || IsInside(directory, full);
    }

    private static bool IsInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(root, path, comparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private readonly record struct TextRead(string? Text, PhotoOriginalMultipartResult? Failure);

    private sealed class CountingReadStream(Stream inner) : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count += read;
            if (Count > PhotoOriginalUploadLimits.MaxRequestBytes)
            {
                throw new InvalidDataException();
            }

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count += read;
            if (Count > PhotoOriginalUploadLimits.MaxRequestBytes)
            {
                throw new InvalidDataException();
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
