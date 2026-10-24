namespace PhotoPlatform.Application.Authentication;

public interface IStudioSignIn
{
    Task<StudioSignInStatus> SignInAsync(string? email, string? password, CancellationToken cancellationToken);

    Task SignOutAsync();
}
