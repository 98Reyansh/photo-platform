using System.Reflection;
using Microsoft.Extensions.Options;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Photos;
using SkiaSharp;

namespace PhotoPlatform.Infrastructure.Tests;

public class LocalPrivatePhotoStoreTests
{
    [Fact]
    public void The_store_does_not_accept_a_caller_path_or_studio_setter()
    {
        var parameters = typeof(LocalPrivatePhotoStore).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(ICurrentStudio));
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(ICurrentStudioSetter));
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(string));

        foreach (var method in typeof(IPrivatePhotoStore).GetMethods())
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(string));
        }
    }

    [Fact]
    public void Resolved_locations_stay_inside_the_root_and_reject_escape_and_prefix_paths()
    {
        using var root = new TempRoot();
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();

        Assert.True(PrivatePhotoPaths.TryResolveObject(root.Path, tenantId, photoId, out var objectPath));
        Assert.Equal(
            Path.Combine(root.Path, tenantId.ToString("D"), photoId.ToString("D"), PrivatePhotoPaths.ObjectFileName),
            objectPath);
        Assert.True(PrivatePhotoPaths.IsInsideRoot(root.Path, objectPath));
        Assert.False(PrivatePhotoPaths.TryResolveObject(root.Path, Guid.Empty, photoId, out _));
        Assert.False(PrivatePhotoPaths.TryResolveObject(root.Path, tenantId, Guid.Empty, out _));
        Assert.False(PrivatePhotoPaths.IsInsideRoot(root.Path, Path.Combine(root.Path, "..", "outside")));

        var sibling = root.Path + "-evil";
        Directory.CreateDirectory(sibling);
        try
        {
            Assert.False(PrivatePhotoPaths.IsInsideRoot(root.Path, Path.Combine(sibling, PrivatePhotoPaths.ObjectFileName)));
        }
        finally
        {
            Directory.Delete(sibling);
        }
    }

    [Fact]
    public async Task Invalid_identity_and_unavailable_root_are_rejected()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var photoId = Guid.CreateVersion7();

        var identity = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.StoreOriginalAsync(Guid.Empty, photoId, new MemoryStream(Jpeg()), CancellationToken.None));
        Assert.Equal(PrivatePhotoStorageException.InvalidIdentityMessage, identity.Message);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));

        var relative = CreateStore("relative/photos");
        var unavailable = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            relative.StoreOriginalAsync(Guid.CreateVersion7(), photoId, new MemoryStream(Jpeg()), CancellationToken.None));
        Assert.Equal(PrivatePhotoStorageException.RootMessage, unavailable.Message);
        Assert.DoesNotContain("relative/photos", unavailable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_oversized_and_unrecognized_content_is_not_stored()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();

        var empty = await store.StoreOriginalAsync(tenantId, photoId, new MemoryStream(), CancellationToken.None);
        var oversized = await store.StoreOriginalAsync(
            tenantId,
            photoId,
            new OversizedStream(),
            CancellationToken.None);
        var unrecognized = await store.StoreOriginalAsync(
            tenantId,
            photoId,
            new MemoryStream([0x00, 0x01, 0x02, 0x03]),
            CancellationToken.None);

        Assert.IsType<PrivatePhotoStoreResult.Rejected>(empty);
        Assert.Equal(PrivatePhotoContentDecision.Empty, ((PrivatePhotoStoreResult.Rejected)empty).Decision);
        Assert.Equal(PrivatePhotoContentDecision.TooLarge, ((PrivatePhotoStoreResult.Rejected)oversized).Decision);
        Assert.Equal(PrivatePhotoContentDecision.Unrecognized, ((PrivatePhotoStoreResult.Rejected)unrecognized).Decision);
        Assert.False(empty.ImageDecodeVerified);
        Assert.False(oversized.ImageDecodeVerified);
        Assert.Null(await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(StoredSamples))]
    public async Task A_decoded_image_is_stored_only_after_verification(
        byte[] bytes,
        PrivatePhotoFormat format)
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();

        var stored = await store.StoreOriginalAsync(tenantId, photoId, new MemoryStream(bytes), CancellationToken.None);

        var saved = Assert.IsType<PrivatePhotoStoreResult.Stored>(stored);
        Assert.Equal(format, saved.Format);
        Assert.Equal(bytes.Length, saved.ByteCount);
        Assert.True(saved.ImageDecodeVerified);
        Assert.True(saved.Width > 0);
        Assert.True(saved.Height > 0);

        await using var opened = await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None);
        Assert.NotNull(opened);
        Assert.Equal(format, opened.Format);
        Assert.True(opened.ImageDecodeVerified);
        using var copy = new MemoryStream();
        await opened.Body.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task A_signature_without_a_decodable_image_is_not_committed()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();

        var stored = await store.StoreOriginalAsync(
            tenantId,
            photoId,
            new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]),
            CancellationToken.None);

        var rejected = Assert.IsType<PrivatePhotoStoreResult.Rejected>(stored);
        Assert.Equal(PrivatePhotoContentDecision.Malformed, rejected.Decision);
        Assert.False(rejected.ImageDecodeVerified);
        Assert.Null(await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None));
        Assert.DoesNotContain(
            Directory.GetFiles(root.Path, "*", SearchOption.AllDirectories),
            path => path.Contains(".partial", StringComparison.Ordinal) || path.EndsWith("original", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_replacement_keeps_the_previous_object()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        var original = Encode(SKEncodedImageFormat.Jpeg);
        await store.StoreOriginalAsync(tenantId, photoId, new MemoryStream(original), CancellationToken.None);

        var failure = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.StoreOriginalAsync(tenantId, photoId, new FailingStream(), CancellationToken.None));
        var rejected = await store.StoreOriginalAsync(
            tenantId,
            photoId,
            new MemoryStream([0x00, 0x01, 0x02]),
            CancellationToken.None);

        Assert.Equal(PrivatePhotoStorageException.StoreFailedMessage, failure.Message);
        Assert.DoesNotContain(root.Path, failure.Message, StringComparison.Ordinal);
        Assert.IsType<PrivatePhotoStoreResult.Rejected>(rejected);
        await using var opened = await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None);
        Assert.NotNull(opened);
        using var copy = new MemoryStream();
        await opened.Body.CopyToAsync(copy);
        Assert.Equal(original, copy.ToArray());
    }

    [Fact]
    public async Task Cancellation_does_not_replace_the_previous_object()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        var original = Encode(SKEncodedImageFormat.Png);
        await store.StoreOriginalAsync(tenantId, photoId, new MemoryStream(original), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.StoreOriginalAsync(tenantId, photoId, new CancelStream(), cancellation.Token));

        await using var opened = await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None);
        Assert.NotNull(opened);
        using var copy = new MemoryStream();
        await opened.Body.CopyToAsync(copy);
        Assert.Equal(original, copy.ToArray());
    }

    [Fact]
    public async Task Cancellation_after_decode_starts_does_not_publish_the_original()
    {
        using var root = new TempRoot();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new LocalPrivatePhotoStore(
            Options.Create(new LocalPrivatePhotoStoreOptions { RootPath = root.Path }),
            new PrivatePhotoDecodeGate(new HoldingDecoder(entered, release)));
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        var storeTask = Task.Run(() => store.StoreOriginalAsync(
            tenantId,
            photoId,
            new MemoryStream([0xFF, 0xD8, 0xFF, 0x00]),
            cancellation.Token));

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storeTask);
        Assert.Null(await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None));
        Assert.DoesNotContain(
            Directory.GetFiles(root.Path, "*", SearchOption.AllDirectories),
            path => path.EndsWith(PrivatePhotoPaths.ObjectFileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_removes_a_stored_original_without_exposing_a_path()
    {
        using var root = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        await store.StoreOriginalAsync(
            tenantId,
            photoId,
            new MemoryStream(Encode(SKEncodedImageFormat.Jpeg)),
            CancellationToken.None);

        await store.DeleteOriginalAsync(tenantId, photoId, CancellationToken.None);
        await store.DeleteOriginalAsync(tenantId, photoId, CancellationToken.None);

        Assert.Null(await store.OpenOriginalAsync(tenantId, photoId, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.DeleteOriginalAsync(Guid.Empty, photoId, CancellationToken.None));
        Assert.Equal(PrivatePhotoStorageException.InvalidIdentityMessage, missing.Message);
        Assert.DoesNotContain(root.Path, missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_does_not_follow_a_symlink_at_the_object_path()
    {
        using var root = new TempRoot();
        using var outside = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        var outsideFile = Path.Combine(outside.Path, "secret");
        await File.WriteAllBytesAsync(outsideFile, "keep"u8.ToArray());
        var photoDirectory = Path.Combine(root.Path, tenantId.ToString("D"), photoId.ToString("D"));
        Directory.CreateDirectory(photoDirectory);
        File.CreateSymbolicLink(Path.Combine(photoDirectory, PrivatePhotoPaths.ObjectFileName), outsideFile);

        var exception = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.DeleteOriginalAsync(tenantId, photoId, CancellationToken.None));

        Assert.Equal(PrivatePhotoStorageException.LocationMessage, exception.Message);
        Assert.DoesNotContain(outside.Path, exception.Message, StringComparison.Ordinal);
        Assert.Equal("keep"u8.ToArray(), await File.ReadAllBytesAsync(outsideFile));
    }

    [Fact]
    public async Task A_symlink_inside_the_root_is_not_followed()
    {
        using var root = new TempRoot();
        using var outside = new TempRoot();
        var store = CreateStore(root.Path);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();
        var outsideFile = Path.Combine(outside.Path, "secret");
        await File.WriteAllBytesAsync(outsideFile, "keep"u8.ToArray());
        var photoDirectory = Path.Combine(root.Path, tenantId.ToString("D"), photoId.ToString("D"));
        Directory.CreateDirectory(photoDirectory);
        File.CreateSymbolicLink(Path.Combine(photoDirectory, PrivatePhotoPaths.ObjectFileName), outsideFile);

        var throughFile = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.StoreOriginalAsync(tenantId, photoId, new MemoryStream(Jpeg()), CancellationToken.None));
        var linkedTenant = Guid.CreateVersion7();
        Directory.CreateSymbolicLink(Path.Combine(root.Path, linkedTenant.ToString("D")), outside.Path);
        var throughDirectory = await Assert.ThrowsAsync<PrivatePhotoStorageException>(() =>
            store.StoreOriginalAsync(linkedTenant, photoId, new MemoryStream(Jpeg()), CancellationToken.None));

        Assert.Equal(PrivatePhotoStorageException.LocationMessage, throughFile.Message);
        Assert.Equal(PrivatePhotoStorageException.LocationMessage, throughDirectory.Message);
        Assert.DoesNotContain(outside.Path, throughFile.Message, StringComparison.Ordinal);
        Assert.Equal("keep"u8.ToArray(), await File.ReadAllBytesAsync(outsideFile));
        Assert.Single(Directory.GetFileSystemEntries(outside.Path));
    }

    public static TheoryData<byte[], PrivatePhotoFormat> StoredSamples()
    {
        return new TheoryData<byte[], PrivatePhotoFormat>
        {
            { Encode(SKEncodedImageFormat.Jpeg), PrivatePhotoFormat.Jpeg },
            { Encode(SKEncodedImageFormat.Png), PrivatePhotoFormat.Png },
            { Encode(SKEncodedImageFormat.Webp), PrivatePhotoFormat.WebP },
        };
    }

    private static LocalPrivatePhotoStore CreateStore(string root)
    {
        return new LocalPrivatePhotoStore(
            Options.Create(new LocalPrivatePhotoStoreOptions { RootPath = root }),
            new PrivatePhotoDecodeGate(new SkiaPrivatePhotoDecoder(Options.Create(new PrivatePhotoDecodeOptions()))));
    }

    private static byte[] Jpeg()
    {
        return [0xFF, 0xD8, 0xFF, 0xD9];
    }

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        Assert.NotNull(data);
        return data.ToArray();
    }

    private sealed class HoldingDecoder(TaskCompletionSource entered, TaskCompletionSource release) : IPrivatePhotoDecoder
    {
        public PrivatePhotoDecodeResult Decode(ReadOnlyMemory<byte> content)
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return new PrivatePhotoDecodeResult.Verified(PrivatePhotoFormat.Jpeg, 2, 2);
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = Directory.CreateTempSubdirectory("photo-platform-storage-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }

    private sealed class OversizedStream : Stream
    {
        private long _remaining = PrivatePhotoContentPolicy.MaxBytes + 1;

        public override bool CanRead => true;

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
            if (_remaining == 0)
            {
                return 0;
            }

            var take = (int)Math.Min(count, _remaining);
            _remaining -= take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;

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
            if (_reads++ == 0)
            {
                buffer[offset] = 0x01;
                return 1;
            }

            throw new IOException("disk");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancelStream : Stream
    {
        public override bool CanRead => true;

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
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
