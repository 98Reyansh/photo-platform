namespace PhotoPlatform.Application.Tenancy;

// The tenant comes from the authenticated user's StudioMembership.
// A request header, query value, route value, or body is not a source for this value.
public interface ICurrentStudioSetter
{
    void UseTenant(Guid tenantId);
}
