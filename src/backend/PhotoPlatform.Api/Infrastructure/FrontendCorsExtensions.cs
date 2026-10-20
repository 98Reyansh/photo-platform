namespace PhotoPlatform.Api.Infrastructure;

public static class FrontendCorsExtensions
{
    public const string PolicyName = "Frontend";

    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var explicitOrigins = configuredOrigins
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (explicitOrigins.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins must list explicit origins. A wildcard origin is not allowed.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (explicitOrigins.Length > 0)
                {
                    policy.WithOrigins(explicitOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
            });
        });

        return services;
    }
}
