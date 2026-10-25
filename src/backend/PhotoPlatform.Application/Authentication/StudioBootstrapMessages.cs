namespace PhotoPlatform.Application.Authentication;

public static class StudioBootstrapMessages
{
    public const string AlreadyBootstrapped =
        "Bootstrap has already been completed. It was not run again. Remove the bootstrap environment variables.";

    public const string RolledBack =
        "Bootstrap did not complete. Unfinished studio, owner, and membership changes were rolled back. Correct the problem and run the command again. Do not delete unrelated rows.";

    public const string DatabaseFailure =
        "Bootstrap could not access the database. No studio was saved.";

    public const string ShutdownFailure =
        "Bootstrap process shutdown reported a failure. The earlier bootstrap result is unchanged.";
}
