namespace PhotoPlatform.Infrastructure.Photos;

internal static class PrivatePhotoPaths
{
    public const string ObjectFileName = "original";

    public static bool TryResolveObject(string rootPath, Guid tenantId, Guid photoId, out string objectPath)
    {
        objectPath = string.Empty;
        if (tenantId == Guid.Empty || photoId == Guid.Empty || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        if (!Path.IsPathRooted(rootPath))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath);
        var relative = Path.Combine(tenantId.ToString("D"), photoId.ToString("D"), ObjectFileName);
        var combined = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsInsideRoot(root, combined))
        {
            return false;
        }

        objectPath = combined;
        return true;
    }

    public static bool IsInsideRoot(string rootPath, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }
}
