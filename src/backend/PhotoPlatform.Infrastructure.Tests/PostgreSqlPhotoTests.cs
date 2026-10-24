using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Entities;
using PhotoPlatform.Domain.Membership;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PostgreSqlPhotoTests
{
    private static readonly string[] ExpectedMigrations =
    [
        "20261020135016_InitialTenants",
        "20261024110924_AddStudioMembership",
        "20261024122355_AddPrivatePhotoMetadata",
    ];

    [PostgreSqlFact]
    public async Task Applied_migrations_include_photo_metadata_and_the_photos_table_matches_the_contract()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);

        await using var context = CreateContext(connectionString!, new CurrentStudio());
        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        foreach (var migration in ExpectedMigrations)
        {
            Assert.Contains(migration, applied);
        }

        await context.Database.OpenConnectionAsync(CancellationToken.None);
        try
        {
            var columns = await ReadColumnsAsync(context);
            Assert.Equal(
                ["CreatedAtUtc", "Description", "Id", "TenantId", "Title", "UpdatedAtUtc"],
                columns.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
            AssertColumn(columns["Id"], "uuid", nullable: false, maxLength: null);
            AssertColumn(columns["TenantId"], "uuid", nullable: false, maxLength: null);
            AssertColumn(columns["Title"], "character varying", nullable: false, maxLength: Photo.TitleMaxLength);
            AssertColumn(columns["Description"], "character varying", nullable: true, maxLength: Photo.DescriptionMaxLength);
            AssertColumn(columns["CreatedAtUtc"], "timestamp with time zone", nullable: false, maxLength: null);
            AssertColumn(columns["UpdatedAtUtc"], "timestamp with time zone", nullable: false, maxLength: null);

            var primaryKey = await ReadStringsAsync(
                context,
                """
                SELECT attribute.attname
                FROM pg_index index
                JOIN pg_class table_name ON table_name.oid = index.indrelid
                JOIN pg_namespace namespace ON namespace.oid = table_name.relnamespace
                JOIN pg_attribute attribute
                    ON attribute.attrelid = table_name.oid
                    AND attribute.attnum = ANY(index.indkey)
                WHERE namespace.nspname = 'public'
                    AND table_name.relname = 'Photos'
                    AND index.indisprimary
                """);
            Assert.Equal(["Id"], primaryKey);

            var foreignKey = await ReadStringAsync(
                context,
                """
                SELECT pg_get_constraintdef(constraint_row.oid)
                FROM pg_constraint constraint_row
                JOIN pg_class table_name ON table_name.oid = constraint_row.conrelid
                JOIN pg_namespace namespace ON namespace.oid = table_name.relnamespace
                WHERE namespace.nspname = 'public'
                    AND table_name.relname = 'Photos'
                    AND constraint_row.conname = 'FK_Photos_Tenants_TenantId'
                """);
            Assert.Contains("Tenants", foreignKey, StringComparison.Ordinal);
            Assert.Contains("ON DELETE RESTRICT", foreignKey, StringComparison.Ordinal);

            var uniqueFlags = await ReadStringsAsync(
                context,
                """
                SELECT CASE WHEN index.indisunique THEN 'unique' ELSE 'not-unique' END
                FROM pg_index index
                JOIN pg_class table_name ON table_name.oid = index.indrelid
                JOIN pg_class index_name ON index_name.oid = index.indexrelid
                JOIN pg_namespace namespace ON namespace.oid = table_name.relnamespace
                WHERE namespace.nspname = 'public'
                    AND table_name.relname = 'Photos'
                    AND index_name.relname = 'IX_Photos_TenantId'
                """);
            Assert.Equal(["not-unique"], uniqueFlags);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [PostgreSqlFact]
    public async Task Photo_commands_persist_create_update_and_delete()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            var studio = new CurrentStudio();
            await using var context = CreateContext(connectionString!, studio);
            var tenant = await CreateTenantAsync(context, owned, "Harbor Studio");
            studio.UseTenant(tenant.Id);
            var commands = new PhotoCommands(context, studio);

            var created = await commands.CreateAsync("  Harbor  ", " Dawn ", CancellationToken.None);

            Assert.Equal(PhotoCommandStatus.Success, created.Status);
            Assert.NotNull(created.Photo);
            owned.PhotoIds.Add((tenant.Id, created.Photo.Id));
            Assert.Equal(tenant.Id, created.Photo.TenantId);
            Assert.Equal("Harbor", created.Photo.Title);
            Assert.Equal("Dawn", created.Photo.Description);
            Assert.Equal(DateTimeKind.Utc, created.Photo.CreatedAtUtc.Kind);
            Assert.Equal(created.Photo.CreatedAtUtc, created.Photo.UpdatedAtUtc);

            var read = await ReadPhotoAsync(connectionString!, tenant.Id, created.Photo.Id);
            Assert.NotNull(read);
            Assert.Equal(created.Photo.Id, read.Id);
            Assert.Equal(tenant.Id, read.TenantId);
            Assert.Equal("Harbor", read.Title);
            Assert.Equal("Dawn", read.Description);
            Assert.Equal(DateTimeKind.Utc, read.CreatedAtUtc.Kind);
            Assert.Equal(read.CreatedAtUtc, read.UpdatedAtUtc);

            var updated = await commands.UpdateAsync(
                created.Photo.Id,
                " Evening harbor ",
                "  Night watch  ",
                CancellationToken.None);

            Assert.Equal(PhotoCommandStatus.Success, updated.Status);
            Assert.NotNull(updated.Photo);
            Assert.Equal(created.Photo.Id, updated.Photo.Id);
            Assert.Equal(tenant.Id, updated.Photo.TenantId);
            Assert.Equal("Evening harbor", updated.Photo.Title);
            Assert.Equal("Night watch", updated.Photo.Description);
            Assert.Equal(created.Photo.CreatedAtUtc, updated.Photo.CreatedAtUtc);
            Assert.True(updated.Photo.UpdatedAtUtc >= created.Photo.CreatedAtUtc);

            var reread = await ReadPhotoAsync(connectionString!, tenant.Id, created.Photo.Id);
            Assert.NotNull(reread);
            Assert.Equal(created.Photo.Id, reread.Id);
            Assert.Equal(tenant.Id, reread.TenantId);
            Assert.Equal("Evening harbor", reread.Title);
            Assert.Equal("Night watch", reread.Description);
            Assert.Equal(read.CreatedAtUtc, reread.CreatedAtUtc);
            Assert.True(reread.UpdatedAtUtc >= reread.CreatedAtUtc);
            Assert.Equal(DateTimeKind.Utc, reread.UpdatedAtUtc.Kind);

            var deleted = await commands.DeleteAsync(created.Photo.Id, CancellationToken.None);

            Assert.Equal(PhotoCommandStatus.Success, deleted.Status);
            Assert.Null(await ReadPhotoAsync(connectionString!, tenant.Id, created.Photo.Id));
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Resolved_studios_can_read_only_their_own_photos()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            Guid firstTenantId;
            Guid secondTenantId;
            var setup = new CurrentStudio();
            await using (var context = CreateContext(connectionString!, setup))
            {
                firstTenantId = (await CreateTenantAsync(context, owned, "North Studio")).Id;
                secondTenantId = (await CreateTenantAsync(context, owned, "South Studio")).Id;
            }

            var firstPhotoId = await CreatePhotoAsync(connectionString!, owned, firstTenantId, "North harbor", "Dawn");
            var secondPhotoId = await CreatePhotoAsync(connectionString!, owned, secondTenantId, "South harbor", "Dusk");

            await using (var first = CreateContext(connectionString!, StudioFor(firstTenantId)))
            await using (var second = CreateContext(connectionString!, StudioFor(secondTenantId)))
            {
                Assert.Equal([firstPhotoId], await first.Photos.Select(photo => photo.Id).ToListAsync(CancellationToken.None));
                Assert.Equal([secondPhotoId], await second.Photos.Select(photo => photo.Id).ToListAsync(CancellationToken.None));
            }

            await using var reread = CreateContext(connectionString!, StudioFor(firstTenantId));
            await using var unresolved = CreateContext(connectionString!, new CurrentStudio());
            Assert.Equal([firstPhotoId], await reread.Photos.Select(photo => photo.Id).ToListAsync(CancellationToken.None));
            Assert.Empty(await unresolved.Photos.Select(photo => photo.Id).ToListAsync(CancellationToken.None));
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Mismatched_tenant_insert_does_not_persist()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            Guid studioId;
            Guid otherTenantId;
            var setup = new CurrentStudio();
            await using (var context = CreateContext(connectionString!, setup))
            {
                studioId = (await CreateTenantAsync(context, owned, "Owner Studio")).Id;
                otherTenantId = (await CreateTenantAsync(context, owned, "Other Studio")).Id;
            }

            var photo = new Photo(otherTenantId, "Rejected harbor", null, DateTime.UtcNow);
            owned.PhotoIds.Add((otherTenantId, photo.Id));
            await using var contextForStudio = CreateContext(connectionString!, StudioFor(studioId));
            contextForStudio.Photos.Add(photo);

            var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                () => contextForStudio.SaveChangesAsync(CancellationToken.None));

            Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
            Assert.Equal(otherTenantId, photo.TenantId);
            Assert.Null(await ReadPhotoAsync(connectionString!, otherTenantId, photo.Id));
            Assert.Null(await ReadPhotoAsync(connectionString!, studioId, photo.Id));
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Changing_photo_tenant_does_not_persist()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            Guid studioId;
            Guid otherTenantId;
            var setup = new CurrentStudio();
            await using (var context = CreateContext(connectionString!, setup))
            {
                studioId = (await CreateTenantAsync(context, owned, "Owner Studio")).Id;
                otherTenantId = (await CreateTenantAsync(context, owned, "Other Studio")).Id;
            }

            var photoId = await CreatePhotoAsync(connectionString!, owned, studioId, "Harbor", "Dawn");
            Photo loaded;
            await using (var source = CreateContext(connectionString!, StudioFor(studioId)))
            {
                loaded = await source.Photos.SingleAsync(photo => photo.Id == photoId, CancellationToken.None);
            }

            await using var contextForStudio = CreateContext(connectionString!, StudioFor(studioId));
            contextForStudio.Photos.Attach(loaded);
            var tenant = contextForStudio.Entry(loaded).Property(photo => photo.TenantId);
            tenant.CurrentValue = otherTenantId;

            var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                () => contextForStudio.SaveChangesAsync(CancellationToken.None));

            Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
            Assert.Equal(otherTenantId, tenant.CurrentValue);
            Assert.Equal(studioId, tenant.OriginalValue);
            var stored = await ReadPhotoAsync(connectionString!, studioId, photoId);
            Assert.NotNull(stored);
            Assert.Equal(studioId, stored.TenantId);
            Assert.Equal("Harbor", stored.Title);
            Assert.Equal("Dawn", stored.Description);
            Assert.Null(await ReadPhotoAsync(connectionString!, otherTenantId, photoId));
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Deleting_another_studios_photo_does_not_persist()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            Guid studioId;
            Guid otherTenantId;
            var setup = new CurrentStudio();
            await using (var context = CreateContext(connectionString!, setup))
            {
                studioId = (await CreateTenantAsync(context, owned, "Owner Studio")).Id;
                otherTenantId = (await CreateTenantAsync(context, owned, "Other Studio")).Id;
            }

            var photoId = await CreatePhotoAsync(connectionString!, owned, otherTenantId, "Other harbor", null);
            Photo loaded;
            await using (var source = CreateContext(connectionString!, StudioFor(otherTenantId)))
            {
                loaded = await source.Photos.SingleAsync(photo => photo.Id == photoId, CancellationToken.None);
            }

            await using var contextForStudio = CreateContext(connectionString!, StudioFor(studioId));
            contextForStudio.Photos.Attach(loaded);
            contextForStudio.Photos.Remove(loaded);

            var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                () => contextForStudio.SaveChangesAsync(CancellationToken.None));

            Assert.Equal(PhotoWriteBoundaryException.MismatchMessage, exception.Message);
            var stored = await ReadPhotoAsync(connectionString!, otherTenantId, photoId);
            Assert.NotNull(stored);
            Assert.Equal(otherTenantId, stored.TenantId);
            Assert.Equal("Other harbor", stored.Title);
            Assert.Null(stored.Description);
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Unresolved_studio_photo_writes_do_not_persist()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            Guid tenantId;
            var setup = new CurrentStudio();
            await using (var context = CreateContext(connectionString!, setup))
            {
                tenantId = (await CreateTenantAsync(context, owned, "Harbor Studio")).Id;
            }

            var photoId = await CreatePhotoAsync(connectionString!, owned, tenantId, "Harbor", "Dawn");
            var original = await ReadPhotoAsync(connectionString!, tenantId, photoId);
            Assert.NotNull(original);

            var rejected = new Photo(tenantId, "Rejected", null, DateTime.UtcNow);
            owned.PhotoIds.Add((tenantId, rejected.Id));
            await using (var unresolved = CreateContext(connectionString!, new CurrentStudio()))
            {
                unresolved.Photos.Add(rejected);
                var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                    () => unresolved.SaveChangesAsync(CancellationToken.None));
                Assert.Equal(PhotoWriteBoundaryException.UnresolvedMessage, exception.Message);
            }

            Assert.Null(await ReadPhotoAsync(connectionString!, tenantId, rejected.Id));

            Photo loaded;
            await using (var source = CreateContext(connectionString!, StudioFor(tenantId)))
            {
                loaded = await source.Photos.SingleAsync(photo => photo.Id == photoId, CancellationToken.None);
            }

            await using (var unresolved = CreateContext(connectionString!, new CurrentStudio()))
            {
                unresolved.Photos.Attach(loaded);
                loaded.UpdateDetails("Changed", "Changed", DateTime.UtcNow);
                var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                    () => unresolved.SaveChangesAsync(CancellationToken.None));
                Assert.Equal(PhotoWriteBoundaryException.UnresolvedMessage, exception.Message);
            }

            var afterModification = await ReadPhotoAsync(connectionString!, tenantId, photoId);
            Assert.NotNull(afterModification);
            Assert.Equal(original.Title, afterModification.Title);
            Assert.Equal(original.Description, afterModification.Description);
            Assert.Equal(original.CreatedAtUtc, afterModification.CreatedAtUtc);
            Assert.Equal(original.UpdatedAtUtc, afterModification.UpdatedAtUtc);
            Assert.Equal(original.TenantId, afterModification.TenantId);

            await using (var source = CreateContext(connectionString!, StudioFor(tenantId)))
            {
                loaded = await source.Photos.SingleAsync(photo => photo.Id == photoId, CancellationToken.None);
            }

            await using (var unresolved = CreateContext(connectionString!, new CurrentStudio()))
            {
                unresolved.Photos.Attach(loaded);
                unresolved.Photos.Remove(loaded);
                var exception = await Assert.ThrowsAsync<PhotoWriteBoundaryException>(
                    () => unresolved.SaveChangesAsync(CancellationToken.None));
                Assert.Equal(PhotoWriteBoundaryException.UnresolvedMessage, exception.Message);
            }

            var afterDelete = await ReadPhotoAsync(connectionString!, tenantId, photoId);
            Assert.NotNull(afterDelete);
            Assert.Equal(original.Title, afterDelete.Title);
            Assert.Equal(original.TenantId, afterDelete.TenantId);
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    [PostgreSqlFact]
    public async Task Tenant_and_membership_persistence_is_not_blocked()
    {
        var connectionString = Environment.GetEnvironmentVariable(PostgreSqlTenantTests.ConnectionVariable);
        await MigrateAsync(connectionString);
        var owned = new OwnedData();
        try
        {
            var createdAtUtc = DateTime.UtcNow;
            var userId = Guid.CreateVersion7();
            await using var context = CreateContext(connectionString!, new CurrentStudio());
            var tenant = await CreateTenantAsync(context, owned, "Membership Studio");
            var membership = new StudioMembership(userId, tenant.Id, createdAtUtc);
            context.Memberships.Add(membership);
            await context.SaveChangesAsync(CancellationToken.None);
            owned.MembershipIds.Add(membership.Id);

            await using var reread = CreateContext(connectionString!, new CurrentStudio());
            var storedTenant = await reread.Tenants.SingleAsync(item => item.Id == tenant.Id, CancellationToken.None);
            var storedMembership = await reread.Memberships.SingleAsync(
                item => item.Id == membership.Id,
                CancellationToken.None);
            Assert.Equal(tenant.Slug, storedTenant.Slug);
            Assert.Equal(userId, storedMembership.UserId);
            Assert.Equal(tenant.Id, storedMembership.TenantId);
            Assert.Empty(await reread.Photos.Select(photo => photo.Id).ToListAsync(CancellationToken.None));
        }
        finally
        {
            await DeleteOwnedAsync(connectionString, owned);
        }
    }

    private static async Task MigrateAsync(string? connectionString)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
        await using var context = CreateContext(connectionString!, new CurrentStudio());
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreatePhotoAsync(
        string connectionString,
        OwnedData owned,
        Guid tenantId,
        string title,
        string? description)
    {
        var studio = StudioFor(tenantId);
        await using var context = CreateContext(connectionString, studio);
        var commands = new PhotoCommands(context, studio);
        var created = await commands.CreateAsync(title, description, CancellationToken.None);
        Assert.Equal(PhotoCommandStatus.Success, created.Status);
        Assert.NotNull(created.Photo);
        owned.PhotoIds.Add((tenantId, created.Photo.Id));
        return created.Photo.Id;
    }

    private static async Task<PhotoResponse?> ReadPhotoAsync(string connectionString, Guid tenantId, Guid photoId)
    {
        var studio = StudioFor(tenantId);
        await using var context = CreateContext(connectionString, studio);
        var result = await new PhotoCommands(context, studio).GetAsync(photoId, CancellationToken.None);
        Assert.NotEqual(PhotoCommandStatus.UnresolvedStudio, result.Status);
        return result.Photo;
    }

    private static async Task<Tenant> CreateTenantAsync(ApplicationDbContext context, OwnedData owned, string name)
    {
        var tenant = new Tenant(name, $"photo-{Guid.NewGuid():N}", DateTime.UtcNow);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync(CancellationToken.None);
        owned.TenantIds.Add(tenant.Id);
        return tenant;
    }

    private static CurrentStudio StudioFor(Guid tenantId)
    {
        var studio = new CurrentStudio();
        studio.UseTenant(tenantId);
        return studio;
    }

    private static ApplicationDbContext CreateContext(string connectionString, ICurrentStudio studio)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ApplicationDbContext(options, studio);
    }

    private static async Task DeleteOwnedAsync(string? connectionString, OwnedData owned)
    {
        PostgreSqlTestGuard.EnsureSafeToUse(connectionString);
        foreach (var tenantId in owned.PhotoIds.Select(item => item.TenantId).Distinct())
        {
            await using var context = CreateContext(connectionString!, StudioFor(tenantId));
            foreach (var photoId in owned.PhotoIds.Where(item => item.TenantId == tenantId).Select(item => item.PhotoId))
            {
                var photo = await context.Photos.SingleOrDefaultAsync(item => item.Id == photoId, CancellationToken.None);
                if (photo is not null)
                {
                    context.Photos.Remove(photo);
                }
            }

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var cleanup = CreateContext(connectionString!, new CurrentStudio());
        foreach (var membershipId in owned.MembershipIds)
        {
            var membership = await cleanup.Memberships.SingleOrDefaultAsync(
                item => item.Id == membershipId,
                CancellationToken.None);
            if (membership is not null)
            {
                cleanup.Memberships.Remove(membership);
            }
        }

        foreach (var tenantId in owned.TenantIds)
        {
            var tenant = await cleanup.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, CancellationToken.None);
            if (tenant is not null)
            {
                cleanup.Tenants.Remove(tenant);
            }
        }

        await cleanup.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Dictionary<string, ColumnInfo>> ReadColumnsAsync(ApplicationDbContext context)
    {
        var columns = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT column_name, data_type, character_maximum_length, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Photos'
            """;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            columns[reader.GetString(0)] = new ColumnInfo(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetString(3) == "YES");
        }

        return columns;
    }

    private static async Task<string> ReadStringAsync(ApplicationDbContext context, string sql)
    {
        var values = await ReadStringsAsync(context, sql);
        return Assert.Single(values);
    }

    private static async Task<List<string>> ReadStringsAsync(ApplicationDbContext context, string sql)
    {
        var values = new List<string>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static void AssertColumn(ColumnInfo column, string dataType, bool nullable, int? maxLength)
    {
        Assert.Equal(dataType, column.DataType);
        Assert.Equal(nullable, column.Nullable);
        Assert.Equal(maxLength, column.MaxLength);
    }

    private sealed record ColumnInfo(string DataType, int? MaxLength, bool Nullable);

    private sealed class OwnedData
    {
        public List<Guid> TenantIds { get; } = [];

        public List<Guid> MembershipIds { get; } = [];

        public List<(Guid TenantId, Guid PhotoId)> PhotoIds { get; } = [];
    }
}
