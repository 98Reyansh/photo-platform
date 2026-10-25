using System.Runtime.CompilerServices;

namespace PhotoPlatform.Infrastructure.Tests;

internal static class TestProcessLimits
{
    [ModuleInitializer]
    internal static void PreferPollingFileWatchers()
    {
        Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
    }
}
