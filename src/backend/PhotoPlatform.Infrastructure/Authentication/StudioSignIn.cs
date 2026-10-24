using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Authentication;

public sealed class StudioSignIn(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext context) : IStudioSignIn
{
    public async Task<StudioSignInStatus> SignInAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return StudioSignInStatus.Failed;
        }

        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return StudioSignInStatus.Failed;
        }

        var passwordResult = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!passwordResult.Succeeded)
        {
            return StudioSignInStatus.Failed;
        }

        var hasMembership = await context.Memberships
            .AnyAsync(membership => membership.UserId == user.Id, cancellationToken);
        if (!hasMembership)
        {
            return StudioSignInStatus.Failed;
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return StudioSignInStatus.Succeeded;
    }

    public Task SignOutAsync()
    {
        return signIn.SignOutAsync();
    }
}
