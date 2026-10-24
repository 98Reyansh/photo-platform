namespace PhotoPlatform.Api.Contracts;

public sealed record CsrfTokenResponse(string Token);

public sealed record SignInRequest(string? Email, string? Password);
