using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace PhotoPlatform.Api.Infrastructure;

public static class StudioAuthenticationExtensions
{
    public const string AuthenticationCookieName = "PhotoPlatform.Auth";
    public const string CsrfCookieName = "PhotoPlatform.Csrf";
    public const string CsrfHeaderName = "X-CSRF-TOKEN";

    public static IServiceCollection AddStudioAuthentication(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        var securePolicy = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthenticationCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.Cookie.Name = CsrfCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
        });

        services.AddAuthorization();
        return services;
    }
}
