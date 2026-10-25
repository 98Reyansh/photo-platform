using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoCommitClassifierTests
{
    [Fact]
    public async Task A_write_boundary_failure_is_a_confirmed_rollback_before_a_database_command()
    {
        await using var database = await ClassifierDatabase.OpenAsync();
        var classifier = new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false));

        var outcome = classifier.Classify(new PhotoWriteBoundaryException(PhotoWriteBoundaryException.UnresolvedMessage));

        Assert.Equal(PhotoCommitClass.ConfirmedRollback, outcome);
    }

    [Theory]
    [InlineData("22001")]
    [InlineData("22P02")]
    [InlineData("23000")]
    [InlineData("23503")]
    [InlineData("23505")]
    public async Task An_integrity_or_data_error_is_confirmed_only_when_no_transaction_remains(string sqlState)
    {
        await using var database = await ClassifierDatabase.OpenAsync();
        var confirmed = new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false));
        var stillAssigned = new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: true));
        var exception = PostgresUpdate(sqlState, "secret-title");

        Assert.Equal(PhotoCommitClass.ConfirmedRollback, confirmed.Classify(exception));
        Assert.Equal(PhotoCommitClass.Uncertain, stillAssigned.Classify(exception));
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [InlineData("08006")]
    [InlineData("57P01")]
    public async Task Other_postgres_states_stay_uncertain(string sqlState)
    {
        await using var database = await ClassifierDatabase.OpenAsync();
        var classifier = new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false));

        Assert.Equal(PhotoCommitClass.Uncertain, classifier.Classify(PostgresUpdate(sqlState, "secret-title")));
    }

    [Fact]
    public async Task A_database_update_without_a_postgres_error_is_not_a_confirmed_rollback()
    {
        await using var database = await ClassifierDatabase.OpenAsync();
        var classifier = new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false));

        Assert.Equal(
            PhotoCommitClass.Uncertain,
            classifier.Classify(new DbUpdateException("rolled back secret-title")));
        Assert.Equal(PhotoCommitClass.Uncertain, classifier.Classify(new TimeoutException("secret-title")));
        Assert.Equal(PhotoCommitClass.Uncertain, classifier.Classify(new OperationCanceledException("secret-title")));
    }

    [Fact]
    public async Task A_closed_connection_or_an_active_user_transaction_is_uncertain()
    {
        await using var closed = await ClassifierDatabase.CreateAsync();
        var closedClassifier = new NpgsqlPhotoCommitClassifier(closed.Context, new FakeProbe(assigned: false));
        Assert.Equal(ConnectionState.Closed, closed.Context.Database.GetDbConnection().State);
        Assert.Equal(PhotoCommitClass.Uncertain, closedClassifier.Classify(PostgresUpdate("23503", "secret-title")));

        await using var open = await ClassifierDatabase.OpenAsync();
        var transaction = await open.Context.Database.BeginTransactionAsync();
        var active = new NpgsqlPhotoCommitClassifier(open.Context, new FakeProbe(assigned: false));
        Assert.Equal(PhotoCommitClass.Uncertain, active.Classify(PostgresUpdate("23505", "secret-title")));
        await transaction.DisposeAsync();
    }

    [Fact]
    public async Task A_non_postgres_connection_cannot_confirm_that_a_transaction_ended()
    {
        await using var database = await ClassifierDatabase.OpenAsync();

        Assert.False(new NpgsqlPhotoTransactionProbe(database.Context).NoTransactionAssigned());
    }

    [Fact]
    public void Persistence_diagnostics_record_the_classification_without_the_exception_or_its_message()
    {
        var logger = new RecordingLogger();
        var diagnostics = new PhotoUploadDiagnostics(logger);
        var tenantId = Guid.CreateVersion7();
        var photoId = Guid.CreateVersion7();

        diagnostics.PersistenceOutcome(PhotoCommitClass.Uncertain, nameof(TimeoutException), tenantId, photoId);
        diagnostics.CleanupFailed(nameof(IOException), tenantId, photoId);

        var written = string.Join('\n', logger.Messages);
        Assert.Contains(nameof(PhotoCommitClass.Uncertain), written, StringComparison.Ordinal);
        Assert.Contains(nameof(TimeoutException), written, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), written, StringComparison.Ordinal);
        Assert.Contains(tenantId.ToString("D"), written, StringComparison.Ordinal);
        Assert.Contains(photoId.ToString("D"), written, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-title", written, StringComparison.Ordinal);
        Assert.Empty(logger.Exceptions);
    }

    private static DbUpdateException PostgresUpdate(string sqlState, string sensitive)
    {
        return new DbUpdateException(sensitive, new PostgresException(sensitive, "ERROR", "ERROR", sqlState));
    }

    private sealed class FakeProbe(bool assigned) : IPhotoTransactionProbe
    {
        public bool NoTransactionAssigned()
        {
            return !assigned;
        }
    }

    private sealed class ClassifierDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private ClassifierDatabase(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public static async Task<ClassifierDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var context = new ApplicationDbContext(options, new CurrentStudio());
            return new ClassifierDatabase(connection, context);
        }

        public static async Task<ClassifierDatabase> OpenAsync()
        {
            var database = await CreateAsync();
            await database.Context.Database.OpenConnectionAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingLogger : ILogger<PhotoUploadDiagnostics>
    {
        public List<string> Messages { get; } = [];

        public List<Exception> Exceptions { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }

            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
