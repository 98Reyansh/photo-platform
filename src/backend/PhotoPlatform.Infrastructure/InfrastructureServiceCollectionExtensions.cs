using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Application.Membership;
using PhotoPlatform.Infrastructure.Authentication;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Membership;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(GetRequiredConnectionString(configuration));
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, UserClaimsPrincipalFactory<ApplicationUser>>();

        services.AddScoped<DatabaseConnectivity>();
        services.AddScoped<ICurrentMembershipQuery, CurrentMembershipQuery>();
        services.AddScoped<IStudioSignIn, StudioSignIn>();
        return services;
    }

    public static string GetRequiredConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. Set the ConnectionStrings__{ConnectionStringName} environment variable or the ConnectionStrings:{ConnectionStringName} user secret.");
        }

        return connectionString;
    }
}
