using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Infrastructure.Persistence;

namespace PhotoPlatform.Infrastructure.Photos;

public sealed class NpgsqlPhotoCommitClassifier : IPhotoCommitClassifier
{
    private readonly ApplicationDbContext _context;
    private readonly IPhotoTransactionProbe _transactions;

    public NpgsqlPhotoCommitClassifier(ApplicationDbContext context)
        : this(context, new NpgsqlPhotoTransactionProbe(context))
    {
    }

    internal NpgsqlPhotoCommitClassifier(ApplicationDbContext context, IPhotoTransactionProbe transactions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(transactions);
        _context = context;
        _transactions = transactions;
    }

    public PhotoCommitClass Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // PhotoWriteBoundaryException is thrown before a database command. A class 22 or 23
        // Postgres error counts only when the connection is open, no caller transaction is active,
        // and the session reports that no transaction is assigned. Every other failure is uncertain:
        // the private object stays, and the insert is not repeated.
        if (exception is PhotoWriteBoundaryException)
        {
            return PhotoCommitClass.ConfirmedRollback;
        }

        if (exception is not DbUpdateException)
        {
            return PhotoCommitClass.Uncertain;
        }

        var postgres = FindPostgres(exception);
        if (postgres is null || postgres.IsTransient || !IsDataOrIntegrity(postgres.SqlState))
        {
            return PhotoCommitClass.Uncertain;
        }

        if (_context.Database.CurrentTransaction is not null)
        {
            return PhotoCommitClass.Uncertain;
        }

        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            return PhotoCommitClass.Uncertain;
        }

        return _transactions.NoTransactionAssigned()
            ? PhotoCommitClass.ConfirmedRollback
            : PhotoCommitClass.Uncertain;
    }

    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static bool IsDataOrIntegrity(string? sqlState)
    {
        return sqlState is ['2', '2', ..] or ['2', '3', ..];
    }
}

internal interface IPhotoTransactionProbe
{
    bool NoTransactionAssigned();
}

internal sealed class NpgsqlPhotoTransactionProbe(ApplicationDbContext context) : IPhotoTransactionProbe
{
    internal const string NoTransactionAssignedSql = "SELECT pg_current_xact_id_if_assigned()";

    public bool NoTransactionAssigned()
    {
        var connection = context.Database.GetDbConnection();
        if (connection is not NpgsqlConnection || connection.State != ConnectionState.Open)
        {
            return false;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = NoTransactionAssignedSql;
            var assigned = command.ExecuteScalar();
            return assigned is null or DBNull;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or InvalidCastException)
        {
            return false;
        }
    }
}
