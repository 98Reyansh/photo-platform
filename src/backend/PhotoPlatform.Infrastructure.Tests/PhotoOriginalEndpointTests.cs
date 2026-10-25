using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoPlatform.Api.Contracts;
using PhotoPlatform.Api.Controllers;
using PhotoPlatform.Api.Infrastructure;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;
using SkiaSharp;

namespace PhotoPlatform.Infrastructure.Tests;

[CollectionDefinition("PhotoOriginalUploadEndpoints")]
public sealed class PhotoOriginalUploadEndpointsCollection;

[Collection("PhotoOriginalUploadEndpoints")]
public class PhotoOriginalEndpointTests
{
    private const string Boundary = "photo-boundary";

    [Fact]
    public async Task Upload_limits_are_explicit_on_kestrel_and_the_endpoint()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        var kestrel = fixture.Factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var method = typeof(PhotosController).GetMethod(nameof(PhotosController.UploadOriginal));
        Assert.NotNull(method);
        var requestLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>()!.As<IRequestSizeLimitMetadata>();
        var formLimit = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.NotNull(formLimit);

        Assert.Equal(26_214_400, PhotoOriginalUploadLimits.MaxFileBytes);
        Assert.Equal(26_279_936, PhotoOriginalUploadLimits.MaxRequestBytes);
        Assert.Equal(PhotoOriginalUploadLimits.MaxRequestBytes, kestrel.Limits.MaxRequestBodySize);
        Assert.Equal(PhotoOriginalUploadLimits.MaxRequestBytes, requestLimit.MaxRequestBodySize);
        Assert.Equal(PhotoOriginalUploadLimits.MaxRequestBytes, formLimit.MultipartBodyLengthLimit);
        Assert.Equal(PhotoOriginalUploadLimits.MaxBoundaryLength, formLimit.MultipartBoundaryLengthLimit);
        Assert.Equal(PhotoOriginalUploadLimits.MaxHeadersCount, formLimit.MultipartHeadersCountLimit);
        Assert.Equal(PhotoOriginalUploadLimits.MaxHeadersLength, formLimit.MultipartHeadersLengthLimit);
    }

    [Fact]
    public async Task A_stored_original_is_created_for_the_membership_tenant_and_listed_afterward()
    {
        var clientTenant = Guid.CreateVersion7();
        var fileName = "https://cdn.example/secret-original.jpg";
        await using var fixture = await UploadFixture.CreateAsync();
        var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
        fixture.Factory.Principal.IsAuthenticated = true;
        fixture.Factory.Principal.NameIdentifier = Guid.CreateVersion7().ToString();
        fixture.Factory.Membership.TenantsByUser[Guid.Parse(fixture.Factory.Principal.NameIdentifier)] = tenantId;
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var before = await client.GetFromJsonAsync<PhotoListResponse>("/api/v1/photos");
        var response = await fixture.SendAsync(
            client,
            $"/api/v1/photos/originals?tenantId={clientTenant}&photoId={Guid.CreateVersion7()}&url=https://public.example/hidden.jpg",
            Multipart(fileFirst: true, title: "  Harbor  ", description: null, file: Encode(SKEncodedImageFormat.Jpeg, 2, 2), fileName: fileName),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();
        var photo = JsonSerializer.Deserialize<PhotoResponse>(body, JsonOptions);
        var after = await client.GetFromJsonAsync<PhotoListResponse>("/api/v1/photos");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(before);
        Assert.Empty(before.Items);
        Assert.NotNull(photo);
        Assert.NotNull(after);
        var listed = Assert.Single(after.Items);
        Assert.Equal(photo.Id, listed.Id);
        Assert.Equal(tenantId, photo.TenantId);
        Assert.Equal(tenantId, listed.TenantId);
        Assert.Equal("Harbor", photo.Title);
        Assert.Null(photo.Description);
        Assert.EndsWith($"/api/v1/photos/{photo.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(
            ["createdAtUtc", "description", "id", "tenantId", "title", "updatedAtUtc"],
            PropertyNames(body));
        Assert.DoesNotContain(fileName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(clientTenant.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-original", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/original", body, StringComparison.Ordinal);
        Assert.Empty(SpoolFiles(fixture));
        Assert.Contains(StoredFiles(fixture), path => path.EndsWith($"{Path.DirectorySeparatorChar}original", StringComparison.Ordinal));
        Assert.DoesNotContain(StoredFiles(fixture), path => path.Contains(".partial", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Png_and_webp_originals_return_the_existing_photo_response(SKEncodedImageFormat format)
    {
        await using var fixture = await UploadFixture.CreateAsync();
        var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
        Authenticate(fixture, tenantId);
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            Multipart(false, "North light", "Quiet", Encode(format, 2, 2), "client-name.bin"),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();
        var photo = JsonSerializer.Deserialize<PhotoResponse>(body, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(photo);
        Assert.Equal(tenantId, photo.TenantId);
        Assert.Equal("North light", photo.Title);
        Assert.Equal("Quiet", photo.Description);
        Assert.EndsWith($"/api/v1/photos/{photo.Id}", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("client-name", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/original", body, StringComparison.Ordinal);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unrecognized")]
    [InlineData("malformed")]
    [InlineData("dimensions")]
    [InlineData("inconsistent")]
    public async Task Rejected_image_content_returns_400_and_creates_no_row(string kind)
    {
        await using var fixture = await UploadFixture.CreateAsync(maxWidth: kind == "dimensions" ? 8 : null, inconsistentDecoder: kind == "inconsistent");
        var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
        Authenticate(fixture, tenantId);
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var file = kind switch
        {
            "empty" => [],
            "unrecognized" => new byte[] { 0x00, 0x01, 0x02, 0x03 },
            "malformed" => new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x00, 0x00 },
            "dimensions" => Encode(SKEncodedImageFormat.Png, 9, 1),
            _ => new byte[] { 0xFF, 0xD8, 0xFF, 0x00 },
        };
        var expected = kind switch
        {
            "empty" => PhotoOriginalUploadLimits.EmptyFileDetail,
            "unrecognized" => PhotoOriginalUploadLimits.UnsupportedFormatDetail,
            "dimensions" => PhotoOriginalUploadLimits.DimensionsDetail,
            _ => PhotoOriginalUploadLimits.UnreadableImageDetail,
        };

        var response = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(fileFirst: false, "Harbor", null, file, "plain.jpg"), csrf: true);
        var body = await response.Content.ReadAsStringAsync();
        var list = await client.GetFromJsonAsync<PhotoListResponse>("/api/v1/photos");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, body, StringComparison.Ordinal);
        Assert.DoesNotContain("plain.jpg", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Skia", body, StringComparison.Ordinal);
        Assert.NotNull(list);
        Assert.Empty(list.Items);
        Assert.Empty(SpoolFiles(fixture));
        Assert.DoesNotContain(StoredFiles(fixture), path => path.Contains("original", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_or_oversized_metadata_and_unsupported_parts_are_rejected()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
        var clientTenant = Guid.CreateVersion7();
        Authenticate(fixture, tenantId);
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var image = Encode(SKEncodedImageFormat.Jpeg, 2, 2);

        var missingTitle = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(false, null, null, image, "a.jpg"), csrf: true);
        var longTitle = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(false, new string('a', 201), null, image, "a.jpg"), csrf: true);
        var longDescription = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(false, "Harbor", new string('b', 2_001), image, "a.jpg"), csrf: true);
        var missingFile = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(false, "Harbor", null, null, null), csrf: true);
        var twoFiles = await fixture.SendAsync(client, "/api/v1/photos/originals", Multipart(false, "Harbor", null, image, "a.jpg", extraFile: image), csrf: true);
        var tenantPart = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            Multipart(false, "Harbor", null, image, "a.jpg", extraFieldName: "tenantId", extraFieldValue: clientTenant.ToString()),
            csrf: true);
        var notMultipart = await fixture.SendAsync(client, "/api/v1/photos/originals", new StringContent("{}", Encoding.UTF8, "application/json"), csrf: true);
        var list = await client.GetFromJsonAsync<PhotoListResponse>("/api/v1/photos");

        Assert.Contains("Title is required.", await missingTitle.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Title must be at most 200 characters.", await longTitle.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Description must be at most 2000 characters.", await longDescription.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(PhotoOriginalUploadLimits.MissingFileDetail, await missingFile.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(PhotoOriginalUploadLimits.DuplicatePartDetail, await twoFiles.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var tenantBody = await tenantPart.Content.ReadAsStringAsync();
        Assert.Contains(PhotoOriginalUploadLimits.UnsupportedPartDetail, tenantBody, StringComparison.Ordinal);
        Assert.DoesNotContain(clientTenant.ToString(), tenantBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PhotoOriginalUploadLimits.MultipartRequiredDetail, await notMultipart.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.All(
            new[] { missingTitle, longTitle, longDescription, missingFile, twoFiles, tenantPart, notMultipart },
            response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.NotNull(list);
        Assert.Empty(list.Items);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task A_file_over_the_encoded_limit_returns_400_before_the_upload_operation()
    {
        await using var fixture = await UploadFixture.CreateAsync(rejectUpload: true);
        Authenticate(fixture, Guid.CreateVersion7());
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            new StreamingMultipart(PhotoOriginalUploadLimits.MaxFileBytes + 1, chunked: false),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PhotoOriginalUploadLimits.FileTooLargeDetail, body, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Factory.UploadCalls);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task A_declared_content_length_over_the_request_limit_returns_413()
    {
        await using var fixture = await UploadFixture.CreateAsync(rejectUpload: true);
        Authenticate(fixture, Guid.CreateVersion7());
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var content = new DeclaredLengthContent(new byte[] { 1, 2, 3, 4 }, PhotoOriginalUploadLimits.MaxRequestBytes + 1);

        var response = await fixture.SendAsync(client, "/api/v1/photos/originals", content, csrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains(PhotoOriginalUploadLimits.RequestTooLargeTitle, body, StringComparison.Ordinal);
        Assert.Contains(PhotoOriginalUploadLimits.RequestTooLargeDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Request body too large", body, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Factory.UploadCalls);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task A_chunked_body_over_the_request_limit_returns_413()
    {
        await using var fixture = await UploadFixture.CreateAsync(rejectUpload: true);
        Authenticate(fixture, Guid.CreateVersion7());
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            new ChunkedByteContent(PhotoOriginalUploadLimits.MaxRequestBytes + 1),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains(PhotoOriginalUploadLimits.RequestTooLargeDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("BadHttpRequest", body, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Factory.UploadCalls);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task Anonymous_upload_is_rejected()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        using var client = fixture.Factory.CreateClient();

        var response = await client.PostAsync("/api/v1/photos/originals", Multipart(false, "Harbor", null, [1, 2, 3], "a.jpg"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task An_authenticated_user_without_a_membership_is_forbidden()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        fixture.Factory.Principal.IsAuthenticated = true;
        fixture.Factory.Principal.NameIdentifier = Guid.CreateVersion7().ToString();
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            Multipart(false, "Harbor", null, Encode(SKEncodedImageFormat.Jpeg, 2, 2), "a.jpg"),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(PhotosController.UnresolvedDetail, body, StringComparison.Ordinal);
        Assert.Empty(SpoolFiles(fixture));
        Assert.Empty(StoredFiles(fixture));
    }

    [Fact]
    public async Task Upload_without_csrf_preserves_the_empty_bad_request()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        Authenticate(fixture, Guid.CreateVersion7());
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            Multipart(false, "Harbor", null, Encode(SKEncodedImageFormat.Jpeg, 2, 2), "secret.jpg"),
            csrf: false);
        var metadata = await fixture.SendAsync(
            client,
            "/api/v1/photos",
            JsonContent.Create(new { title = "Harbor", description = "Dawn" }),
            csrf: false);

        var csrfBody = await response.Content.ReadAsStringAsync();
        var metadataBody = await metadata.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, metadata.StatusCode);
        Assert.Equal(WithoutTrace(metadataBody), WithoutTrace(csrfBody));
        Assert.DoesNotContain("secret.jpg", csrfBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Harbor", csrfBody, StringComparison.Ordinal);
        Assert.Empty(SpoolFiles(fixture));
        Assert.Empty(StoredFiles(fixture));
    }

    [Fact]
    public async Task Storage_failure_returns_the_generic_error_and_removes_the_spool()
    {
        await using var fixture = await UploadFixture.CreateAsync(photoRoot: "relative/photos");
        var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
        Authenticate(fixture, tenantId);
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await fixture.SendAsync(
            client,
            "/api/v1/photos/originals",
            Multipart(false, "Harbor", "Dawn", Encode(SKEncodedImageFormat.Jpeg, 2, 2), "secret.jpg"),
            csrf: true);
        var body = await response.Content.ReadAsStringAsync();
        var list = await client.GetFromJsonAsync<PhotoListResponse>("/api/v1/photos");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("An unexpected error occurred.", body, StringComparison.Ordinal);
        Assert.Contains("The request could not be completed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.jpg", body, StringComparison.Ordinal);
        Assert.DoesNotContain("relative/photos", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Harbor", body, StringComparison.Ordinal);
        Assert.NotNull(list);
        Assert.Empty(list.Items);
        Assert.Empty(SpoolFiles(fixture));
    }

    [Fact]
    public async Task Cancellation_after_the_spool_is_created_removes_it()
    {
        await using var fixture = await UploadFixture.CreateAsync();
        Authenticate(fixture, Guid.CreateVersion7());
        using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var stop = new CancellationTokenSource();
        var during = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Factory.Services.GetRequiredService<PhotoUploadSpoolGate>().BeforeRelease = async cancellationToken =>
        {
            during.AddRange(SpoolFiles(fixture));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, linked.Token);
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/photos/originals")
        {
            Content = Multipart(false, "Harbor", null, Encode(SKEncodedImageFormat.Jpeg, 2, 2), "secret.jpg"),
        };
        request.Headers.Add(StudioAuthenticationExtensions.CsrfHeaderName, await fixture.ReadCsrfAsync(client));

        var send = client.SendAsync(request, stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);

        Assert.NotEmpty(during);
        Assert.Empty(SpoolFiles(fixture));
        Assert.Empty(StoredFiles(fixture));
    }

    [Fact]
    public async Task A_spool_inside_the_photo_root_is_refused_without_writing_a_part()
    {
        var root = Directory.CreateTempSubdirectory("photo-platform-root-").FullName;
        try
        {
            var spool = Path.Combine(root, "spool");
            Directory.CreateDirectory(spool);
            await using var fixture = await UploadFixture.CreateAsync(photoRoot: root, spoolRoot: spool);
            var tenantId = await fixture.SeedTenantAsync("North Studio", "north-studio");
            Authenticate(fixture, tenantId);
            using var client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

            var response = await fixture.SendAsync(
                client,
                "/api/v1/photos/originals",
                Multipart(false, "Harbor", null, Encode(SKEncodedImageFormat.Jpeg, 2, 2), "secret.jpg"),
                csrf: true);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(root, body, StringComparison.Ordinal);
            Assert.DoesNotContain("secret.jpg", body, StringComparison.Ordinal);
            Assert.DoesNotContain(PhotoOriginalUploadLimits.SpoolUnavailable, body, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(root, "*.part", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Authenticate(UploadFixture fixture, Guid tenantId)
    {
        var userId = Guid.CreateVersion7();
        fixture.Factory.Principal.IsAuthenticated = true;
        fixture.Factory.Principal.NameIdentifier = userId.ToString();
        fixture.Factory.Membership.TenantsByUser[userId] = tenantId;
    }

    private static List<string> SpoolFiles(UploadFixture fixture)
    {
        return Directory.Exists(fixture.SpoolRoot)
            ? Directory.GetFiles(fixture.SpoolRoot, "*", SearchOption.AllDirectories).ToList()
            : [];
    }

    private static List<string> StoredFiles(UploadFixture fixture)
    {
        return Directory.Exists(fixture.PhotoRoot)
            ? Directory.GetFiles(fixture.PhotoRoot, "*", SearchOption.AllDirectories).ToList()
            : [];
    }

    private static string WithoutTrace(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject().Where(property => property.Name != "traceId"))
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<string> PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
    }

    private static ByteArrayContent Multipart(
        bool fileFirst,
        string? title,
        string? description,
        byte[]? file,
        string? fileName,
        byte[]? extraFile = null,
        string? extraFieldName = null,
        string? extraFieldValue = null)
    {
        using var body = new MemoryStream();
        if (fileFirst && file is not null)
        {
            WriteFile(body, file, fileName);
        }

        if (title is not null)
        {
            WriteText(body, "title", title);
        }

        if (description is not null)
        {
            WriteText(body, "description", description);
        }

        if (!fileFirst && file is not null)
        {
            WriteFile(body, file, fileName);
        }

        if (extraFile is not null)
        {
            WriteFile(body, extraFile, "second.jpg");
        }

        if (extraFieldName is not null)
        {
            WriteText(body, extraFieldName, extraFieldValue ?? string.Empty);
        }

        var ending = Encoding.UTF8.GetBytes($"--{Boundary}--\r\n");
        body.Write(ending);
        var content = new ByteArrayContent(body.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        return content;
    }

    private static void WriteText(Stream body, string name, string value)
    {
        var text = $"--{Boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n";
        body.Write(Encoding.UTF8.GetBytes(text));
    }

    private static void WriteFile(Stream body, byte[] file, string? fileName)
    {
        var header = $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: application/octet-stream\r\n\r\n";
        body.Write(Encoding.UTF8.GetBytes(header));
        body.Write(file);
        body.Write("\r\n"u8);
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        Assert.NotNull(data);
        return data.ToArray();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class UploadFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private UploadFixture(SqliteConnection connection, UploadApiFactory factory, string photoRoot, string spoolRoot)
        {
            _connection = connection;
            Factory = factory;
            PhotoRoot = photoRoot;
            SpoolRoot = spoolRoot;
        }

        public UploadApiFactory Factory { get; }

        public string PhotoRoot { get; }

        public string SpoolRoot { get; }

        public static async Task<UploadFixture> CreateAsync(
            int? maxWidth = null,
            bool inconsistentDecoder = false,
            bool rejectUpload = false,
            string? photoRoot = null,
            string? spoolRoot = null)
        {
            var deletePhotoRoot = photoRoot is null;
            photoRoot ??= Directory.CreateTempSubdirectory("photo-platform-originals-").FullName;
            spoolRoot ??= Directory.CreateTempSubdirectory("photo-platform-spool-").FullName;
            var connection = new SqliteConnection("Data Source=:memory:");
            Assert.DoesNotContain("photoplatform", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("photo_platform", connection.ConnectionString, StringComparison.OrdinalIgnoreCase);
            await connection.OpenAsync();
            var factory = new UploadApiFactory(connection, photoRoot, spoolRoot, maxWidth, inconsistentDecoder, rejectUpload, deletePhotoRoot);
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Contains("Sqlite", context.Database.ProviderName, StringComparison.Ordinal);
            await context.Database.EnsureCreatedAsync();
            return new UploadFixture(connection, factory, photoRoot, spoolRoot);
        }

        public async Task<Guid> SeedTenantAsync(string name, string slug)
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = new Tenant(name, slug, DateTime.UtcNow);
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
            return tenant.Id;
        }

        public async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, HttpContent content, bool csrf)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (csrf)
            {
                request.Headers.Add(StudioAuthenticationExtensions.CsrfHeaderName, await ReadCsrfAsync(client));
            }

            return await client.SendAsync(request);
        }

        public async Task<string> ReadCsrfAsync(HttpClient client)
        {
            var response = await client.GetAsync("/api/v1/auth/csrf");
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
            Assert.NotNull(token);
            return token.Token;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class UploadApiFactory(
        SqliteConnection connection,
        string photoRoot,
        string spoolRoot,
        int? maxWidth,
        bool inconsistentDecoder,
        bool rejectUpload,
        bool deletePhotoRoot) : WebApplicationFactory<Program>
    {
        public TestPrincipal Principal { get; } = new();

        public MembershipDouble Membership { get; } = new();

        public int UploadCalls { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("PrivatePhotoStorage:RootPath", photoRoot);
            if (maxWidth is int width)
            {
                builder.UseSetting("PrivatePhotoDecode:MaxWidth", width.ToString());
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;Port=1;Database=photo_platform_test;Username=photoplatform;Password=unused;Timeout=1",
                });
            });
            builder.ConfigureServices(services =>
            {
                var replaced = new[]
                {
                    typeof(DbContextOptions<ApplicationDbContext>),
                    typeof(DbContextOptions),
                    typeof(ApplicationDbContext),
                    typeof(IDbContextOptionsConfiguration<ApplicationDbContext>),
                    typeof(ICurrentMembershipQuery),
                };
                foreach (var descriptor in services.Where(service => replaced.Contains(service.ServiceType)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton(Principal);
                services.AddSingleton(Membership);
                services.AddScoped<ICurrentMembershipQuery>(provider =>
                    new FakeMembershipQuery(provider.GetRequiredService<MembershipDouble>()));
                services.Configure<PhotoUploadSpoolOptions>(options => options.Directory = spoolRoot);
                if (inconsistentDecoder)
                {
                    var decoder = services.Single(service => service.ServiceType == typeof(IPrivatePhotoDecoder));
                    services.Remove(decoder);
                    services.AddSingleton<IPrivatePhotoDecoder, InconsistentDecoder>();
                }

                if (rejectUpload)
                {
                    var upload = services.Single(service => service.ServiceType == typeof(IPhotoOriginalUpload));
                    services.Remove(upload);
                    services.AddScoped<IPhotoOriginalUpload>(_ => new RecordingUpload(this));
                }

                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestUserAuthenticationHandler>(
                        TestUserAuthenticationHandler.SchemeName,
                        _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = TestUserAuthenticationHandler.SchemeName;
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (deletePhotoRoot && Directory.Exists(photoRoot))
            {
                Directory.Delete(photoRoot, recursive: true);
            }

            if (Directory.Exists(spoolRoot))
            {
                Directory.Delete(spoolRoot, recursive: true);
            }
        }

        private sealed class RecordingUpload(UploadApiFactory factory) : IPhotoOriginalUpload
        {
            public Task<PhotoOriginalUploadResult> UploadAsync(
                string? title,
                string? description,
                Stream content,
                CancellationToken cancellationToken)
            {
                factory.UploadCalls++;
                throw new InvalidOperationException("The upload operation was not expected.");
            }
        }
    }

    private sealed class InconsistentDecoder : IPrivatePhotoDecoder
    {
        public PrivatePhotoDecodeResult Decode(ReadOnlyMemory<byte> content)
        {
            return new PrivatePhotoDecodeResult.Rejected(PrivatePhotoContentDecision.Inconsistent);
        }
    }

    private sealed class TestPrincipal
    {
        public bool IsAuthenticated { get; set; }

        public string? NameIdentifier { get; set; }
    }

    private sealed class MembershipDouble
    {
        public Dictionary<Guid, Guid> TenantsByUser { get; } = [];
    }

    private sealed class FakeMembershipQuery(MembershipDouble state) : ICurrentMembershipQuery
    {
        public Task<CurrentMembershipResponse?> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!state.TenantsByUser.TryGetValue(userId, out var tenantId))
            {
                return Task.FromResult<CurrentMembershipResponse?>(null);
            }

            return Task.FromResult<CurrentMembershipResponse?>(
                new CurrentMembershipResponse(tenantId, "North Studio", "north-studio", "Active"));
        }
    }

    private sealed class TestUserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestUser";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!principal.IsAuthenticated || principal.NameIdentifier is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, principal.NameIdentifier)],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class DeclaredLengthContent : HttpContent
    {
        private readonly byte[] _body;
        private readonly long _declaredLength;

        public DeclaredLengthContent(byte[] body, long declaredLength)
        {
            _body = body;
            _declaredLength = declaredLength;
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _declaredLength;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_body).AsTask();
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            return stream.WriteAsync(_body, cancellationToken).AsTask();
        }
    }

    private sealed class ChunkedByteContent : HttpContent
    {
        private readonly long _count;

        public ChunkedByteContent(long count)
        {
            _count = count;
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[81_920];
            var remaining = _count;
            while (remaining > 0)
            {
                var size = (int)Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, size), cancellationToken);
                remaining -= size;
            }
        }
    }

    private sealed class StreamingMultipart : HttpContent
    {
        private readonly long _count;
        private readonly long? _length;
        private readonly Stream? _body;

        public StreamingMultipart(long count, bool chunked, Stream? body = null)
        {
            _count = count;
            _body = body;
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
            if (!chunked)
            {
                _length = Preamble().Length + count + Epilogue().Length;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_length is long contentLength)
            {
                length = contentLength;
                return true;
            }

            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Preamble(), cancellationToken);
            if (_body is not null)
            {
                await _body.CopyToAsync(stream, cancellationToken);
                return;
            }

            var buffer = new byte[81_920];
            var remaining = _count;
            while (remaining > 0)
            {
                var count = (int)Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                remaining -= count;
            }

            await stream.WriteAsync(Epilogue(), cancellationToken);
        }

        private static byte[] Preamble()
        {
            return Encoding.UTF8.GetBytes(
                $"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"large.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        }

        private static byte[] Epilogue()
        {
            return Encoding.UTF8.GetBytes($"\r\n--{Boundary}--\r\n");
        }
    }

}

internal static class RequestLimitMetadata
{
    public static T As<T>(this object value)
        where T : class
    {
        return Assert.IsAssignableFrom<T>(value);
    }
}
