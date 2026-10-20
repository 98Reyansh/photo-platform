using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure;

public sealed class DatabaseConnectivity(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseConnectivity> logger)
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await context.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is PostgresException postgresException)
            {
                logger.LogWarning(
                    "Database connectivity check failed with SQL state {SqlState}.",
                    postgresException.SqlState);
            }
            else
            {
                logger.LogWarning(
                    "Database connectivity check failed with {ExceptionType}.",
                    exception.GetType().Name);
            }

            return false;
        }
    }
}
