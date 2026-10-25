using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Infrastructure.Identity;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Authentication;

public sealed class StudioBootstrap(ApplicationDbContext context, UserManager<ApplicationUser> users) : IStudioBootstrap
{
    public async Task<StudioBootstrapResult> RunAsync(
        StudioBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (await AlreadyExistsAsync(cancellationToken))
            {
                return Failure(StudioBootstrapStatus.AlreadyBootstrapped, StudioBootstrapMessages.AlreadyBootstrapped);
            }
        }
        catch (Exception exception)
        {
            return Failure(
                StudioBootstrapStatus.Failed,
                StudioBootstrapMessages.DatabaseFailure,
                StudioBootstrapDiagnostics.Describe(exception));
        }

        Tenant tenant;
        try
        {
            tenant = new Tenant(request.StudioName, request.StudioSlug, DateTime.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return Failure(
                StudioBootstrapStatus.InvalidStudio,
                WithoutSecret(exception.Message, request.OwnerPassword));
        }

        // Read committed keeps the existence check after the advisory lock on a new snapshot.
        // A serializable snapshot taken when the lock call starts waiting would miss a studio committed by the attempt that ran first.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            await AcquireBootstrapLockAsync(cancellationToken);
            if (await AlreadyExistsAsync(cancellationToken))
            {
                return Failure(StudioBootstrapStatus.AlreadyBootstrapped, StudioBootstrapMessages.AlreadyBootstrapped);
            }

            context.Tenants.Add(tenant);
            var ownerEmail = request.OwnerEmail.Trim();
            var user = new ApplicationUser
            {
                Id = Guid.CreateVersion7(),
                UserName = ownerEmail,
                Email = ownerEmail,
                EmailConfirmed = true,
                LockoutEnabled = true,
            };
            var created = await users.CreateAsync(user, request.OwnerPassword);
            if (!created.Succeeded || user.Id == Guid.Empty)
            {
                return Failure(StudioBootstrapStatus.Failed, WithoutSecret(Describe(created), request.OwnerPassword));
            }

            context.Memberships.Add(new StudioMembership(user.Id, tenant.Id, DateTime.UtcNow));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new StudioBootstrapResult(
                StudioBootstrapStatus.Completed,
                "Bootstrap completed.",
                tenant.Id,
                tenant.Slug,
                ownerEmail);
        }
        catch (Exception exception)
        {
            return Failure(
                StudioBootstrapStatus.Failed,
                StudioBootstrapMessages.RolledBack,
                StudioBootstrapDiagnostics.Describe(exception));
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // The result is already a failure. A rollback error must not replace it with success.
                }
            }
        }
    }

    private async Task AcquireBootstrapLockAsync(CancellationToken cancellationToken)
    {
        var provider = context.Database.ProviderName ?? string.Empty;
        if (!provider.Contains("Npgsql", StringComparison.Ordinal))
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        var current = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The bootstrap lock requires the open bootstrap transaction.");
        var dbTransaction = current.GetDbTransaction();
        if (!ReferenceEquals(dbTransaction.Connection, connection))
        {
            throw new InvalidOperationException("The bootstrap lock is not using the bootstrap connection.");
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandTimeout = 120;
        command.CommandText = "SELECT pg_advisory_xact_lock(@key1, @key2)";
        var key1 = command.CreateParameter();
        key1.ParameterName = "key1";
        key1.DbType = DbType.Int32;
        key1.Value = StudioBootstrapLock.Key1;
        command.Parameters.Add(key1);
        var key2 = command.CreateParameter();
        key2.ParameterName = "key2";
        key2.DbType = DbType.Int32;
        key2.Value = StudioBootstrapLock.Key2;
        command.Parameters.Add(key2);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<bool> AlreadyExistsAsync(CancellationToken cancellationToken)
    {
        return await context.Tenants.AnyAsync(cancellationToken)
            || await context.Users.AnyAsync(cancellationToken);
    }

    private static StudioBootstrapResult Failure(StudioBootstrapStatus status, string message, string? diagnostic = null)
    {
        return new StudioBootstrapResult(status, message, null, null, null, diagnostic);
    }

    private static string Describe(IdentityResult result)
    {
        if (result.Errors.Any())
        {
            return string.Join(" ", result.Errors.Select(error => error.Description));
        }

        return "The owner account could not be created.";
    }

    private static string WithoutSecret(string message, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return message;
        }

        return message.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }
}
