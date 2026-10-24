namespace PhotoPlatform.Application.Tenancy;

public interface ICurrentStudio
{
    Guid TenantId { get; }

    bool IsResolved { get; }
}
