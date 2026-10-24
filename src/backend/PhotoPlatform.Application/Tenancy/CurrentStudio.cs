namespace PhotoPlatform.Application.Tenancy;

public sealed class CurrentStudio : ICurrentStudio, ICurrentStudioSetter
{
    public Guid TenantId { get; private set; }

    public bool IsResolved { get; private set; }

    public void UseTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (IsResolved)
        {
            throw new InvalidOperationException("The current studio has already been resolved.");
        }

        TenantId = tenantId;
        IsResolved = true;
    }
}
