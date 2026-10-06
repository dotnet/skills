namespace SkillValidator.Shared;

internal static class PathSafety
{
    internal static bool ContainsReparsePoint(
        string allowedRoot,
        string path,
        bool missingPathIsUnsafe = true)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        var fullPath = Path.GetFullPath(path);
        if (IsReparsePointOrUnreadable(root, missingPathIsUnsafe))
            return true;

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".")
            return false;
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return true;
        }

        var current = root;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePointOrUnreadable(current, missingPathIsUnsafe))
                return true;
        }
        return false;
    }

    private static bool IsReparsePointOrUnreadable(string path, bool missingPathIsUnsafe)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return missingPathIsUnsafe;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
