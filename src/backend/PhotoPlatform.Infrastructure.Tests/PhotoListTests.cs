using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PhotoPlatform.Application.Photos;
using PhotoPlatform.Application.Tenancy;
using PhotoPlatform.Domain.Photos;
using PhotoPlatform.Infrastructure.Persistence;
using PhotoPlatform.Infrastructure.Photos;

namespace PhotoPlatform.Infrastructure.Tests;

public class PhotoListTests
{
    private const string ModelOnlyConnection =
        "Host=127.0.0.1;Port=1;Database=model_only;Username=photoplatform;Password=unused;Timeout=1";

    [Theory]
    [InlineData(null, 20)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    public void Limit_uses_the_default_and_accepts_the_closed_range(int? limit, int expected)
    {
        Assert.True(PhotoCursor.TryNormalizeLimit(limit, out var normalized, out var error));

        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void Limit_outside_the_range_is_rejected(int limit)
    {
        Assert.False(PhotoCursor.TryNormalizeLimit(limit, out var normalized, out var error));

        Assert.Equal(0, normalized);
        Assert.Equal(PhotoCursor.LimitError, error);
    }

    [Fact]
    public void Cursor_round_trip_preserves_ticks_and_id()
    {
        var timestamp = new DateTime(2026, 10, 25, 13, 4, 5, DateTimeKind.Utc).AddTicks(1234);
        var id = Guid.Parse("6f1e1c0a-1b2c-4d3e-8f4a-112233445566");

        var cursor = PhotoCursor.Encode(timestamp, id);
        var decoded = AssertDecoded(cursor);

        Assert.StartsWith("v1.", cursor, StringComparison.Ordinal);
        Assert.DoesNotContain('+', cursor);
        Assert.DoesNotContain('/', cursor);
        Assert.Equal(timestamp.Ticks, decoded.CreatedAtUtc.Ticks);
        Assert.Equal(DateTimeKind.Utc, decoded.CreatedAtUtc.Kind);
        Assert.Equal(timestamp, decoded.CreatedAtUtc);
        Assert.Equal(id, decoded.Id);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("v1")]
    [InlineData("v1.****")]
    public void Malformed_cursor_is_rejected(string cursor)
    {
        Assert.False(PhotoCursor.TryDecode(cursor, out _, out var error));
        Assert.Equal(PhotoCursor.FormatError, error);
    }

    [Fact]
    public void Unsupported_cursor_version_is_rejected()
    {
        var cursor = "v2." + PhotoCursor.Encode(DateTime.UnixEpoch, Guid.CreateVersion7())["v1.".Length..];

        Assert.False(PhotoCursor.TryDecode(cursor, out _, out var error));
        Assert.Equal(PhotoCursor.VersionError, error);
    }

    [Fact]
    public void Invalid_cursor_values_are_rejected()
    {
        Assert.False(PhotoCursor.TryDecode(Cursor(-1, Guid.CreateVersion7()), out _, out var ticksError));
        Assert.Equal(PhotoCursor.ValueError, ticksError);
        Assert.False(PhotoCursor.TryDecode(Cursor(DateTime.UnixEpoch.Ticks, Guid.Empty), out _, out var idError));
        Assert.Equal(PhotoCursor.ValueError, idError);
    }

    [Fact]
    public void Equal_timestamps_sort_by_id_and_pages_do_not_overlap()
    {
        var same = new DateTime(2026, 10, 25, 12, 0, 1, DateTimeKind.Utc);
        var rows = new (DateTime CreatedAtUtc, Guid Id)[]
        {
            (same.AddSeconds(1), Guid.Parse("00000000-0000-0000-0000-000000000004")),
            (same, Guid.Parse("00000000-0000-0000-0000-000000000003")),
            (same, Guid.Parse("00000000-0000-0000-0000-000000000001")),
            (same.AddSeconds(-1), Guid.Parse("00000000-0000-0000-0000-000000000002")),
        };
        var ordered = rows
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .ToArray();
        var tied = ordered.Where(row => row.CreatedAtUtc == same).ToArray();

        Assert.Equal(2, tied.Length);
        Assert.True(tied[0].Id.CompareTo(tied[1].Id) > 0);
        Assert.True(PhotoCursor.SortsAfterCursor(tied[1].CreatedAtUtc, tied[1].Id, new PhotoCursorPosition(tied[0].CreatedAtUtc, tied[0].Id)));
        Assert.False(PhotoCursor.SortsAfterCursor(tied[0].CreatedAtUtc, tied[0].Id, new PhotoCursorPosition(tied[0].CreatedAtUtc, tied[0].Id)));

        var seen = new List<Guid>();
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < 4; pageNumber++)
        {
            PhotoCursorPosition? position = cursor is null ? null : AssertDecoded(cursor);
            var page = ordered
                .Where(row => position is null || PhotoCursor.SortsAfterCursor(row.CreatedAtUtc, row.Id, position.Value))
                .Take(2)
                .ToArray();
            Assert.NotEmpty(page);
            Assert.All(page, row => Assert.DoesNotContain(row.Id, seen));
            seen.AddRange(page.Select(row => row.Id));
            cursor = page.Length == 2 && seen.Count < ordered.Length
                ? PhotoCursor.Encode(page[^1].CreatedAtUtc, page[^1].Id)
                : null;
            if (cursor is null)
            {
                break;
            }
        }

        Assert.Equal(ordered.Select(row => row.Id), seen);
        Assert.Null(cursor);
    }

    [Fact]
    public void Empty_slice_has_no_continuation_cursor()
    {
        var page = PhotoCommands.Slice([], limit: 20);

        Assert.Empty(page.Items);
        Assert.Null(page.ContinuationCursor);
    }

    [Fact]
    public void Slice_keeps_one_extra_row_as_the_continuation_cursor()
    {
        var tenantId = Guid.CreateVersion7();
        var createdAtUtc = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new Photo(tenantId, "Newest", null, createdAtUtc.AddMinutes(2)),
            new Photo(tenantId, "Middle", null, createdAtUtc.AddMinutes(1)),
            new Photo(tenantId, "Oldest", null, createdAtUtc),
        };

        var page = PhotoCommands.Slice(rows, limit: 2);
        var position = AssertDecoded(page.ContinuationCursor!);

        Assert.Equal(rows.Take(2).Select(photo => photo.Id), page.Items.Select(item => item.Id));
        Assert.Equal(rows[1].CreatedAtUtc, position.CreatedAtUtc);
        Assert.Equal(rows[1].Id, position.Id);
        Assert.Null(PhotoCommands.Slice(rows.Take(2).ToArray(), limit: 2).ContinuationCursor);
    }

    [Fact]
    public void List_query_requests_one_extra_row_without_a_count()
    {
        var tenantId = Guid.CreateVersion7();
        var studio = new CurrentStudio();
        studio.UseTenant(tenantId);
        using var context = CreateContext(studio);
        var position = new PhotoCursorPosition(
            new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc).AddTicks(25),
            Guid.CreateVersion7());

        var query = PhotoCommands.PageQuery(context.Photos, limit: 20, position);
        var sql = query.ToQueryString();

        Assert.Equal(21, TakeCount(query.Expression));
        Assert.DoesNotContain("Count", MethodNames(query.Expression), StringComparer.Ordinal);
        Assert.Contains("CreatedAtUtc", sql, StringComparison.Ordinal);
        Assert.Contains("DESC", sql, StringComparison.Ordinal);
        Assert.Contains(tenantId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void List_query_for_an_unresolved_studio_stays_fail_closed()
    {
        using var context = CreateContext(new CurrentStudio());

        var sql = PhotoCommands.PageQuery(context.Photos, limit: 50, position: null).ToQueryString();

        Assert.Contains("FALSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DESC", sql, StringComparison.Ordinal);
        Assert.Equal(51, TakeCount(PhotoCommands.PageQuery(context.Photos, limit: 50, position: null).Expression));
    }

    private static PhotoCursorPosition AssertDecoded(string cursor)
    {
        Assert.True(PhotoCursor.TryDecode(cursor, out var position, out var error));
        Assert.Null(error);
        return position;
    }

    private static string Cursor(long ticks, Guid id)
    {
        Span<byte> payload = stackalloc byte[24];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload[..8], ticks);
        id.ToByteArray().CopyTo(payload[8..]);
        return "v1." + Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int TakeCount(Expression expression)
    {
        var take = MethodCalls(expression).Single(call => call.Method.Name == "Take");
        var argument = take.Arguments[1];
        if (argument is UnaryExpression unary)
        {
            argument = unary.Operand;
        }

        return Assert.IsType<ConstantExpression>(argument).Value is int count
            ? count
            : throw new InvalidOperationException("The page query does not use a constant take count.");
    }

    private static IEnumerable<string> MethodNames(Expression expression)
    {
        return MethodCalls(expression).Select(call => call.Method.Name);
    }

    private static IEnumerable<MethodCallExpression> MethodCalls(Expression expression)
    {
        var visitor = new MethodCallVisitor();
        visitor.Visit(expression);
        return visitor.Calls;
    }

    private static ApplicationDbContext CreateContext(ICurrentStudio studio)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ModelOnlyConnection)
            .Options;
        return new ApplicationDbContext(options, studio);
    }

    private sealed class MethodCallVisitor : ExpressionVisitor
    {
        public List<MethodCallExpression> Calls { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Calls.Add(node);
            return base.VisitMethodCall(node);
        }
    }
}
