using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhotoPlatform.Api.Infrastructure;

namespace PhotoPlatform.Infrastructure.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Response_is_generic_and_logs_omit_exception_details()
    {
        const string secret = "Password=should-not-leak";
        Exception thrown;
        try
        {
            throw new InvalidOperationException($"{secret};Host=db.internal");
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        var logger = new CollectingLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(logger);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/health";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, thrown, CancellationToken.None);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Contains("An unexpected error occurred.", body, StringComparison.Ordinal);
        Assert.Contains("The request could not be completed.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.StackTrace ?? "at ", body, StringComparison.Ordinal);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains("InvalidOperationException", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/v1/health", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.StackTrace ?? "at ", entry.Message, StringComparison.Ordinal);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
