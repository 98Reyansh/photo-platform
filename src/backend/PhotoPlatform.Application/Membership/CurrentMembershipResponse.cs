namespace PhotoPlatform.Application.Membership;

public sealed record CurrentMembershipResponse(Guid TenantId, string Name, string Slug, string Status);
