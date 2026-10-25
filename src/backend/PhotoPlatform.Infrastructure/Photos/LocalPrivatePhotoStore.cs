using Microsoft.Extensions.Options;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Photos;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class LocalPrivatePhotoStore(
    IOptions<LocalPrivatePhotoStoreOptions> options,
    IPrivatePhotoDecodeGate decodeGate) : IPrivatePhotoStore
{
    private const int BufferSize = 81_920;

    public async Task<PrivatePhotoStoreResult> StoreOriginalAsync(
        Guid tenantId,
        Guid photoId,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var objectPath = ResolveObjectPath(tenantId, photoId);
        var directory = Path.GetDirectoryName(objectPath)
            ?? throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        EnsureDirectory(RequireRoot(), directory);

        var tempPath = Path.Combine(directory, $".{PrivatePhotoPaths.ObjectFileName}-{Guid.NewGuid():N}.partial");
        if (!PrivatePhotoPaths.IsInsideRoot(RequireRoot(), tempPath))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        if (IsReparsePoint(objectPath))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        var moved = false;
        try
        {
            var (written, prefixLength, prefix) = await WriteTempAsync(tempPath, content, cancellationToken);
            var verdict = PrivatePhotoContentPolicy.Evaluate(written, prefix.AsSpan(0, prefixLength));
            if (verdict.Decision != PrivatePhotoContentDecision.SignatureRecognized || verdict.Format is null)
            {
                return new PrivatePhotoStoreResult.Rejected(verdict.Decision);
            }

            if (written > int.MaxValue)
            {
                return new PrivatePhotoStoreResult.Rejected(PrivatePhotoContentDecision.TooLarge);
            }

            var encoded = new byte[(int)written];
            await using (var writtenFile = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous))
            {
                await writtenFile.ReadExactlyAsync(encoded.AsMemory(), cancellationToken);
            }

            var decoded = await decodeGate.DecodeAsync(encoded, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded is not PrivatePhotoDecodeResult.Verified verified)
            {
                var decision = decoded is PrivatePhotoDecodeResult.Rejected failure
                    ? failure.Decision
                    : PrivatePhotoContentDecision.Malformed;
                return new PrivatePhotoStoreResult.Rejected(decision);
            }

            if (IsReparsePoint(objectPath))
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
            }

            // The temporary file is in the same directory as the object, so the rename stays on one volume.
            // The previous object remains until that rename succeeds. One local disk is not a multi-instance store.
            File.Move(tempPath, objectPath, overwrite: true);
            moved = true;
            return new PrivatePhotoStoreResult.Stored(verified.Format, written, verified.Width, verified.Height);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not PrivatePhotoStorageException
            and not DllNotFoundException
            and not TypeInitializationException)
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.StoreFailedMessage);
        }
        finally
        {
            if (!moved)
            {
                DeleteFileIfPresent(tempPath);
            }
        }
    }

    public async Task<PrivatePhotoContent?> OpenOriginalAsync(
        Guid tenantId,
        Guid photoId,
        CancellationToken cancellationToken)
    {
        var objectPath = ResolveObjectPath(tenantId, photoId);
        if (IsReparsePoint(objectPath))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        if (!File.Exists(objectPath))
        {
            return null;
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                objectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous);
            var prefix = new byte[12];
            var prefixLength = await stream.ReadAsync(prefix.AsMemory(0, prefix.Length), cancellationToken);
            var verdict = PrivatePhotoContentPolicy.Evaluate(stream.Length, prefix.AsSpan(0, prefixLength));
            if (verdict.Decision != PrivatePhotoContentDecision.SignatureRecognized || verdict.Format is null)
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.ReadFailedMessage);
            }

            if (stream.Length > int.MaxValue)
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.ReadFailedMessage);
            }

            var encoded = new byte[stream.Length];
            stream.Position = 0;
            await stream.ReadExactlyAsync(encoded.AsMemory(), cancellationToken);
            var decoded = await decodeGate.DecodeAsync(encoded, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded is not PrivatePhotoDecodeResult.Verified verified || verified.Format != verdict.Format)
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.ReadFailedMessage);
            }

            stream.Position = 0;
            var content = new PrivatePhotoContent(stream, verified.Format, stream.Length, imageDecodeVerified: true);
            stream = null;
            return content;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not PrivatePhotoStorageException
            and not DllNotFoundException
            and not TypeInitializationException)
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.ReadFailedMessage);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public Task DeleteOriginalAsync(Guid tenantId, Guid photoId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var objectPath = ResolveObjectPath(tenantId, photoId);
        if (IsReparsePoint(objectPath))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        try
        {
            if (File.Exists(objectPath))
            {
                File.Delete(objectPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.DeleteFailedMessage);
        }

        return Task.CompletedTask;
    }

    private string ResolveObjectPath(Guid tenantId, Guid photoId)
    {
        if (tenantId == Guid.Empty || photoId == Guid.Empty)
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.InvalidIdentityMessage);
        }

        var root = RequireRoot();
        if (!PrivatePhotoPaths.TryResolveObject(root, tenantId, photoId, out var objectPath))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        return objectPath;
    }

    private string RequireRoot()
    {
        var configured = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathRooted(configured))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.RootMessage);
        }

        var root = Path.GetFullPath(configured);
        if (!Directory.Exists(root) || IsReparsePoint(root))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.RootMessage);
        }

        return root;
    }

    private static void EnsureDirectory(string root, string directory)
    {
        var rootFull = Path.GetFullPath(root);
        var target = Path.GetFullPath(directory);
        if (!PrivatePhotoPaths.IsInsideRoot(rootFull, target))
        {
            throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
        }

        var relative = Path.GetRelativePath(rootFull, target);
        var current = rootFull;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
            }

            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
            }

            if (File.Exists(current))
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
            }

            if (!Directory.Exists(current))
            {
                Directory.CreateDirectory(current);
            }

            if (IsReparsePoint(current))
            {
                throw new PrivatePhotoStorageException(PrivatePhotoStorageException.LocationMessage);
            }
        }
    }

    private static async Task<(long Written, int PrefixLength, byte[] Prefix)> WriteTempAsync(
        string tempPath,
        Stream content,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[12];
        var prefixLength = 0;
        long written = 0;
        await using var temp = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[BufferSize];
        while (true)
        {
            var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read > PrivatePhotoContentPolicy.MaxBytes - written)
            {
                written = PrivatePhotoContentPolicy.MaxBytes + 1;
                break;
            }

            if (prefixLength < prefix.Length)
            {
                var take = Math.Min(prefix.Length - prefixLength, read);
                buffer.AsSpan(0, take).CopyTo(prefix.AsSpan(prefixLength));
                prefixLength += take;
            }

            await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
        }

        await temp.FlushAsync(cancellationToken);
        return (written, prefixLength, prefix);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or FileNotFoundException
            or DirectoryNotFoundException)
        {
        }
    }
}
