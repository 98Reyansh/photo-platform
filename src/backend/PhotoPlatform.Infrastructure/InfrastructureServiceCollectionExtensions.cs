using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddScoped<DatabaseConnectivity>();
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
