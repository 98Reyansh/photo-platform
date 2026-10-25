using PhotoPlatform.Application.Authentication;
using PhotoPlatform.Infrastructure.Authentication;

namespace PhotoPlatform.Api.Infrastructure;

public static class StudioBootstrapCommand
{
    public static bool IsRequested(string[] args)
    {
        return args is ["bootstrap"];
    }

    public static async Task<int> ExecuteAsync(
        IStudioBootstrap bootstrap,
        TextWriter output,
        TextWriter error,
        Func<string, string?> readEnvironment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(readEnvironment);

        if (!StudioBootstrapEnvironment.TryCreateRequest(readEnvironment, out var request, out var configurationMessage)
            || request is null)
        {
            await error.WriteLineAsync(configurationMessage);
            return 1;
        }

        StudioBootstrapResult result;
        try
        {
            result = await bootstrap.RunAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(StudioBootstrapMessages.DatabaseFailure);
            await error.WriteLineAsync(StudioBootstrapDiagnostics.Describe(exception));
            return 1;
        }

        if (!result.Succeeded || result.StudioId is null || result.StudioSlug is null || result.OwnerEmail is null)
        {
            await error.WriteLineAsync(WithoutPassword(result.Message, request.OwnerPassword));
            if (!string.IsNullOrWhiteSpace(result.Diagnostic))
            {
                await error.WriteLineAsync(result.Diagnostic);
            }

            return 1;
        }

        var confirmation =
            $"Bootstrap completed. Studio id: {result.StudioId:D}. Slug: {result.StudioSlug}. Owner: {result.OwnerEmail}.";
        await output.WriteLineAsync(WithoutPassword(confirmation, request.OwnerPassword));
        return 0;
    }

    private static string WithoutPassword(string message, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return message;
        }

        return message.Replace(password, "[redacted]", StringComparison.Ordinal);
    }
}
