namespace PhotoPlatform.Application.Membership;

public interface ICurrentMembershipQuery
{
    Task<CurrentMembershipResponse?> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
}
