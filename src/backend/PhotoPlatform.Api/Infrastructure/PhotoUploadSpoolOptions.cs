namespace PhotoPlatform.Api.Infrastructure;

public sealed class PhotoUploadSpoolOptions
{
    public string? Directory { get; set; }
}

public sealed class PhotoUploadSpoolGate
{
    public Func<CancellationToken, Task>? BeforeRelease { get; set; }
}
