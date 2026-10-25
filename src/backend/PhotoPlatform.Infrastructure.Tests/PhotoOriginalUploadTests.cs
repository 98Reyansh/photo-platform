using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoOriginalUploadTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 25, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_stored_original_and_a_successful_save_create_one_row_for_the_resolved_tenant()
    {
        await using var database = await UploadDatabase.CreateAsync();
        var tenant = new Tenant("Lensora Studio", "lensora-studio", CreatedAtUtc);
        database.Context.Tenants.Add(tenant);
        await database.Context.SaveChangesAsync();
        database.Studio.UseTenant(tenant.Id);
        var store = StoredStore();
        var diagnostics = new RecordingDiagnostics();
        var upload = Upload(database, store, new PhotoOriginalRecords(database.Context), diagnostics);

        var result = await upload.UploadAsync("  North window  ", "  Dawn  ", new MemoryStream([1, 2, 3, 4]), CancellationToken.None);

        var created = Assert.IsType<PhotoOriginalUploadResult.Created>(result);
        Assert.Equal(tenant.Id, created.Photo.TenantId);
        Assert.Equal("North window", created.Photo.Title);
        Assert.Equal("Dawn", created.Photo.Description);
        Assert.Equal(tenant.Id, store.TenantId);
        Assert.Equal(created.Photo.Id, store.PhotoId);
        Assert.NotEqual(Guid.Empty, store.PhotoId);
        Assert.Equal(DateTimeKind.Utc, created.Photo.CreatedAtUtc.Kind);
        var rows = await database.Context.Photos.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(created.Photo.Id, row.Id);
        Assert.Equal(tenant.Id, row.TenantId);
        Assert.Empty(diagnostics.Entries);
    }

    [Fact]
    public async Task Rejected_image_content_creates_no_row()
    {
        await using var database = UnresolvedDatabase();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = new RecordingStore
        {
            Result = new PrivatePhotoStoreResult.Rejected(PrivatePhotoContentDecision.Unrecognized),
        };
        var records = new CountingRecords();
        var upload = Upload(database, store, records, new RecordingDiagnostics());

        var result = await upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None);

        var rejected = Assert.IsType<PhotoOriginalUploadResult.Rejected>(result);
        Assert.Equal(PrivatePhotoContentDecision.Unrecognized, rejected.Decision);
        Assert.Equal(0, records.Calls);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public async Task Storage_failure_creates_no_row()
    {
        await using var database = UnresolvedDatabase();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = new RecordingStore
        {
            StoreFailure = new PrivatePhotoStorageException(PrivatePhotoStorageException.StoreFailedMessage),
        };
        var records = new CountingRecords();
        var upload = Upload(database, store, records, new RecordingDiagnostics());

        var result = await upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None);

        Assert.IsType<PhotoOriginalUploadResult.NotStored>(result);
        Assert.Equal(0, records.Calls);
        Assert.Equal(0, store.Deletes);
        Assert.Equal(PrivatePhotoStorageException.StoreFailedMessage, store.StoreFailure.Message);
    }

    [Fact]
    public void The_upload_operation_does_not_accept_a_caller_tenant_or_photo_id()
    {
        var parameters = typeof(IPhotoOriginalUpload)
            .GetMethod(nameof(IPhotoOriginalUpload.UploadAsync))!
            .GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(Guid));
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.Name?.Contains("tenant", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.Name?.Contains("photo", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Null(typeof(PhotoResponse).GetProperty("StorageKey"));
        Assert.Null(typeof(PhotoResponse).GetProperty("Url"));
    }

    [Fact]
    public async Task Confirmed_rollback_deletes_the_stored_object_and_does_not_retry_the_insert()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords
        {
            Failure = new PhotoWriteBoundaryException(PhotoWriteBoundaryException.MismatchMessage),
        };
        var diagnostics = new RecordingDiagnostics();
        var upload = Upload(database, store, records, diagnostics);

        var exception = await Assert.ThrowsAsync<PhotoOriginalPersistenceException>(() =>
            upload.UploadAsync("Harbor", "secret-title", new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(PhotoCommitClass.ConfirmedRollback, exception.Outcome);
        Assert.Equal(PhotoOriginalPersistenceException.SaveFailedMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(1, records.Calls);
        Assert.Equal(1, store.Deletes);
        Assert.DoesNotContain("secret-title", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-title", string.Join('\n', diagnostics.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_integrity_error_with_no_assigned_transaction_deletes_the_stored_object()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords
        {
            Failure = new DbUpdateException("secret-title", new PostgresException("secret-title", "ERROR", "ERROR", "23503")),
        };
        var diagnostics = new RecordingDiagnostics();
        var upload = new PhotoOriginalUpload(
            database.Studio,
            store,
            records,
            new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false)),
            diagnostics);

        var exception = await Assert.ThrowsAsync<PhotoOriginalPersistenceException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(PhotoCommitClass.ConfirmedRollback, exception.Outcome);
        Assert.Equal(1, store.Deletes);
        Assert.Equal(1, records.Calls);
        Assert.DoesNotContain("secret-title", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-title", string.Join('\n', diagnostics.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uncertain_persistence_failure_preserves_the_object_and_does_not_retry()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords { Failure = new TimeoutException("secret-title") };
        var diagnostics = new RecordingDiagnostics();
        var upload = Upload(database, store, records, diagnostics);

        var exception = await Assert.ThrowsAsync<PhotoOriginalPersistenceException>(() =>
            upload.UploadAsync("Harbor", "secret-description", new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(PhotoCommitClass.Uncertain, exception.Outcome);
        Assert.Equal(PhotoOriginalPersistenceException.SaveFailedMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(0, store.Deletes);
        Assert.Equal(1, records.Calls);
        Assert.DoesNotContain("secret-title", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-description", string.Join('\n', diagnostics.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_database_update_exception_without_a_classified_postgres_error_preserves_the_object()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords { Failure = new DbUpdateException("secret-title") };
        var upload = Upload(database, store, records, new RecordingDiagnostics());

        var exception = await Assert.ThrowsAsync<PhotoOriginalPersistenceException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(PhotoCommitClass.Uncertain, exception.Outcome);
        Assert.Equal(0, store.Deletes);
        Assert.Equal(1, records.Calls);
        Assert.DoesNotContain("secret-title", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cleanup_failure_does_not_replace_the_original_persistence_failure()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        store.DeleteFailure = new InvalidOperationException("secret-title C:\\library\\original");
        var records = new CountingRecords
        {
            Failure = new PhotoWriteBoundaryException(PhotoWriteBoundaryException.UnresolvedMessage),
        };
        var diagnostics = new RecordingDiagnostics();
        var upload = Upload(database, store, records, diagnostics);

        var exception = await Assert.ThrowsAsync<PhotoOriginalPersistenceException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(PhotoCommitClass.ConfirmedRollback, exception.Outcome);
        Assert.Equal(PhotoOriginalPersistenceException.SaveFailedMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("secret-title", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("library", exception.ToString(), StringComparison.Ordinal);
        var written = string.Join('\n', diagnostics.Entries);
        Assert.Contains(nameof(InvalidOperationException), written, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-title", written, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\library", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_before_the_store_finishes_creates_no_row_and_deletes_nothing()
    {
        await using var database = UnresolvedDatabase();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords();
        var upload = Upload(database, store, records, new RecordingDiagnostics());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), cancellation.Token));

        Assert.Equal(0, store.StoreCalls);
        Assert.Equal(0, records.Calls);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public async Task Cancellation_after_the_original_is_stored_and_before_save_creates_no_row()
    {
        await using var database = UnresolvedDatabase();
        database.Studio.UseTenant(Guid.CreateVersion7());
        using var cancellation = new CancellationTokenSource();
        var store = StoredStore();
        store.AfterStore = cancellation.Cancel;
        var records = new CountingRecords();
        var upload = Upload(database, store, records, new RecordingDiagnostics());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), cancellation.Token));

        Assert.Equal(1, store.StoreCalls);
        Assert.Equal(0, records.Calls);
        Assert.Equal(1, store.Deletes);
    }

    [Fact]
    public async Task Cancellation_during_save_preserves_the_object_and_does_not_retry()
    {
        await using var database = await UploadDatabase.OpenAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var store = StoredStore();
        var records = new CountingRecords { Failure = new OperationCanceledException("secret-title") };
        var diagnostics = new RecordingDiagnostics();
        var upload = Upload(database, store, records, diagnostics);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None));

        Assert.Equal(0, store.Deletes);
        Assert.Equal(1, records.Calls);
        Assert.Contains(nameof(PhotoCommitClass.Uncertain), string.Join('\n', diagnostics.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-title", string.Join('\n', diagnostics.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_save_detaches_the_photo_so_a_later_save_does_not_insert_it()
    {
        await using var database = await UploadDatabase.CreateAsync();
        await database.Context.Database.EnsureCreatedAsync();
        database.Studio.UseTenant(Guid.CreateVersion7());
        var records = new PhotoOriginalRecords(database.Context);
        var photo = new Photo(Guid.CreateVersion7(), "Harbor", null, CreatedAtUtc);

        await Assert.ThrowsAsync<PhotoWriteBoundaryException>(() => records.SaveNewAsync(photo, CancellationToken.None));

        Assert.Empty(database.Context.ChangeTracker.Entries<Photo>());
        Assert.Equal(0, await database.Context.SaveChangesAsync());
        Assert.Empty(database.Context.ChangeTracker.Entries<Photo>());
    }

    [Fact]
    public async Task An_unresolved_studio_or_invalid_title_does_not_store_or_save()
    {
        await using var database = UnresolvedDatabase();
        var store = StoredStore();
        var records = new CountingRecords();
        var upload = Upload(database, store, records, new RecordingDiagnostics());

        var unresolved = await upload.UploadAsync("Harbor", null, new MemoryStream([1]), CancellationToken.None);
        Assert.IsType<PhotoOriginalUploadResult.UnresolvedStudio>(unresolved);

        database.Studio.UseTenant(Guid.CreateVersion7());
        var invalid = await upload.UploadAsync("   ", "Dawn", new MemoryStream([1]), CancellationToken.None);

        var failure = Assert.IsType<PhotoOriginalUploadResult.Invalid>(invalid);
        Assert.Contains("Title is required.", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, store.StoreCalls);
        Assert.Equal(0, records.Calls);
    }

    private static RecordingStore StoredStore()
    {
        return new RecordingStore
        {
            Result = new PrivatePhotoStoreResult.Stored(PrivatePhotoFormat.Jpeg, 4, 2, 2),
        };
    }

    private static PhotoOriginalUpload Upload(
        UploadDatabase database,
        RecordingStore store,
        IPhotoOriginalRecords records,
        RecordingDiagnostics diagnostics)
    {
        return new PhotoOriginalUpload(
            database.Studio,
            store,
            records,
            new NpgsqlPhotoCommitClassifier(database.Context, new FakeProbe(assigned: false)),
            diagnostics);
    }

    private static UploadDatabase UnresolvedDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var studio = new CurrentStudio();
        return new UploadDatabase(connection, new ApplicationDbContext(options, studio), studio);
    }

    private sealed class UploadDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public UploadDatabase(SqliteConnection connection, ApplicationDbContext context, CurrentStudio studio)
        {
            _connection = connection;
            Context = context;
            Studio = studio;
        }

        public ApplicationDbContext Context { get; }

        public CurrentStudio Studio { get; }

        public static async Task<UploadDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var studio = new CurrentStudio();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var context = new ApplicationDbContext(options, studio);
            await context.Database.EnsureCreatedAsync();
            return new UploadDatabase(connection, context, studio);
        }

        public static async Task<UploadDatabase> OpenAsync()
        {
            var database = await CreateAsync();
            if (database.Context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            {
                await database.Context.Database.OpenConnectionAsync();
            }

            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingStore : IPrivatePhotoStore
    {
        public PrivatePhotoStoreResult Result { get; set; } =
            new PrivatePhotoStoreResult.Rejected(PrivatePhotoContentDecision.Empty);

        public Exception? StoreFailure { get; set; }

        public Exception? DeleteFailure { get; set; }

        public Action? AfterStore { get; set; }

        public int StoreCalls { get; private set; }

        public int Deletes { get; private set; }

        public Guid TenantId { get; private set; }

        public Guid PhotoId { get; private set; }

        public Task<PrivatePhotoStoreResult> StoreOriginalAsync(
            Guid tenantId,
            Guid photoId,
            Stream content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoreCalls++;
            TenantId = tenantId;
            PhotoId = photoId;
            AfterStore?.Invoke();
            if (StoreFailure is not null)
            {
                return Task.FromException<PrivatePhotoStoreResult>(StoreFailure);
            }

            return Task.FromResult(Result);
        }

        public Task<PrivatePhotoContent?> OpenOriginalAsync(
            Guid tenantId,
            Guid photoId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PrivatePhotoContent?>(null);
        }

        public Task DeleteOriginalAsync(Guid tenantId, Guid photoId, CancellationToken cancellationToken)
        {
            Deletes++;
            if (DeleteFailure is not null)
            {
                throw DeleteFailure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CountingRecords : IPhotoOriginalRecords
    {
        public int Calls { get; private set; }

        public Exception? Failure { get; set; }

        public Task SaveNewAsync(Photo photo, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDiagnostics : IPhotoUploadDiagnostics
    {
        public List<string> Entries { get; } = [];

        public void PersistenceOutcome(PhotoCommitClass outcome, string exceptionType, Guid tenantId, Guid photoId)
        {
            Entries.Add($"persistence|{outcome}|{exceptionType}|{tenantId:D}|{photoId:D}");
        }

        public void CleanupFailed(string exceptionType, Guid tenantId, Guid photoId)
        {
            Entries.Add($"cleanup|{exceptionType}|{tenantId:D}|{photoId:D}");
        }
    }

    private sealed class FakeProbe(bool assigned) : IPhotoTransactionProbe
    {
        public bool NoTransactionAssigned()
        {
            return !assigned;
        }
    }
}
