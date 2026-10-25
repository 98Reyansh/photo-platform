using System.Buffers.Binary;

namespace PhotoPlatform.Application.Photos;

public readonly record struct PhotoCursorPosition(DateTime CreatedAtUtc, Guid Id);

public static class PhotoCursor
{
    public const int DefaultLimit = 20;

    public const int MaximumLimit = 50;

    public const string LimitError = "Limit must be from 1 through 50.";

    public const string FormatError = "The continuation cursor is invalid.";

    public const string VersionError = "The continuation cursor version is not supported.";

    public const string ValueError = "The continuation cursor value is invalid.";

    private const string Version = "v1";

    private const int PayloadLength = 24;

    public static bool TryNormalizeLimit(int? limit, out int normalized, out string? error)
    {
        if (limit is null)
        {
            normalized = DefaultLimit;
            error = null;
            return true;
        }

        if (limit < 1 || limit > MaximumLimit)
        {
            normalized = 0;
            error = LimitError;
            return false;
        }

        normalized = limit.Value;
        error = null;
        return true;
    }

    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.", nameof(createdAtUtc));
        }

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Photo id is required.", nameof(id));
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        BinaryPrimitives.WriteInt64BigEndian(payload[..8], createdAtUtc.Ticks);
        id.ToByteArray().CopyTo(payload[8..]);
        return Version + "." + ToBase64Url(payload);
    }

    public static bool TryDecode(string? cursor, out PhotoCursorPosition position, out string? error)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(cursor) || cursor != cursor.Trim())
        {
            error = FormatError;
            return false;
        }

        var separator = cursor.IndexOf('.');
        if (separator <= 0 || separator == cursor.Length - 1)
        {
            error = FormatError;
            return false;
        }

        if (!string.Equals(cursor[..separator], Version, StringComparison.Ordinal))
        {
            error = VersionError;
            return false;
        }

        if (!TryReadPayload(cursor[(separator + 1)..], out var payload))
        {
            error = FormatError;
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(payload);
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            error = ValueError;
            return false;
        }

        var id = new Guid(payload.AsSpan(8));
        if (id == Guid.Empty)
        {
            error = ValueError;
            return false;
        }

        position = new PhotoCursorPosition(new DateTime(ticks, DateTimeKind.Utc), id);
        error = null;
        return true;
    }

    public static bool SortsAfterCursor(DateTime createdAtUtc, Guid id, PhotoCursorPosition cursor)
    {
        return createdAtUtc < cursor.CreatedAtUtc
            || (createdAtUtc == cursor.CreatedAtUtc && id.CompareTo(cursor.Id) < 0);
    }

    private static string ToBase64Url(ReadOnlySpan<byte> payload)
    {
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryReadPayload(string encoded, out byte[] payload)
    {
        payload = [];
        var text = encoded.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (text.Length % 4)) % 4;
        if (padding == 3)
        {
            return false;
        }

        text = text.PadRight(text.Length + padding, '=');
        try
        {
            payload = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return false;
        }

        return payload.Length == PayloadLength;
    }
}
