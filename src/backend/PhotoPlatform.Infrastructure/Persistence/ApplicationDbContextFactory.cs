using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PhotoPlatform.Application.Tenancy;

namespace PhotoPlatform.Infrastructure.Persistence;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    internal const string ApiUserSecretsId = "bc09f679-ba1f-4242-9aa9-61cfa8e2c362";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = InfrastructureServiceCollectionExtensions.GetRequiredConnectionString(
            CreateDesignTimeConfiguration());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options, new CurrentStudio());
    }

    internal static IConfiguration CreateDesignTimeConfiguration()
    {
        return CreateDesignTimeConfiguration(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            includeProcessEnvironment: true,
            environmentConnectionString: null);
    }

    internal static IConfiguration CreateDesignTimeConfiguration(
        string? aspNetCoreEnvironment,
        string? dotnetEnvironment,
        bool includeProcessEnvironment,
        string? environmentConnectionString)
    {
        var builder = new ConfigurationBuilder();
        if (LoadsApiUserSecrets(aspNetCoreEnvironment, dotnetEnvironment))
        {
            builder.AddUserSecrets(ApiUserSecretsId);
        }

        if (includeProcessEnvironment)
        {
            builder.AddEnvironmentVariables();
        }

        if (environmentConnectionString is not null)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}"] =
                    environmentConnectionString,
            });
        }

        return builder.Build();
    }

    internal static bool LoadsApiUserSecrets(string? aspNetCoreEnvironment, string? dotnetEnvironment)
    {
        var environment = string.IsNullOrWhiteSpace(aspNetCoreEnvironment)
            ? dotnetEnvironment
            : aspNetCoreEnvironment;

        return string.IsNullOrWhiteSpace(environment)
            || environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
    }
}
