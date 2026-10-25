using Microsoft.Extensions.DependencyInjection;
using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Infrastructure;
using PhotoPlatform.Infrastructure.Authentication;

namespace PhotoPlatform.Api.Infrastructure;

public static class StudioBootstrapEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!StudioBootstrapCommand.IsRequested(args))
        {
            await WriteLineAsync(Console.Error, "Bootstrap was not requested.");
            return 1;
        }

        WebApplication? app = null;
        AsyncServiceScope? scope = null;
        var exitCode = 1;
        var completed = false;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
            });
            builder.Logging.ClearProviders();
            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddStudioAuthentication(builder.Environment);
            app = builder.Build();
            scope = app.Services.CreateAsyncScope();
            var bootstrap = scope.Value.ServiceProvider.GetRequiredService<IStudioBootstrap>();
            exitCode = await StudioBootstrapCommand.ExecuteAsync(
                bootstrap,
                Console.Out,
                Console.Error,
                Environment.GetEnvironmentVariable,
                CancellationToken.None);
            completed = true;
        }
        catch (Exception exception)
        {
            await WriteLineAsync(
                Console.Error,
                completed ? StudioBootstrapMessages.ShutdownFailure : StudioBootstrapMessages.DatabaseFailure);
            await WriteLineAsync(Console.Error, StudioBootstrapDiagnostics.Describe(exception));
            exitCode = 1;
        }

        return await CompleteAsync(
            exitCode,
            async () =>
            {
                if (scope is not null)
                {
                    await scope.Value.DisposeAsync();
                }

                if (app is not null)
                {
                    await app.DisposeAsync();
                }
            },
            Console.Error);
    }

    public static async Task<int> CompleteAsync(int bootstrapExitCode, Func<Task> disposeAsync, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(disposeAsync);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            await disposeAsync();
            return bootstrapExitCode;
        }
        catch (Exception exception)
        {
            await WriteLineAsync(error, StudioBootstrapMessages.ShutdownFailure);
            await WriteLineAsync(error, StudioBootstrapDiagnostics.Describe(exception));
            return 1;
        }
    }

    private static async Task WriteLineAsync(TextWriter writer, string message)
    {
        try
        {
            await writer.WriteLineAsync(message);
        }
        catch (Exception)
        {
            // A writer failure must not become an unhandled exception.
        }
    }
}
