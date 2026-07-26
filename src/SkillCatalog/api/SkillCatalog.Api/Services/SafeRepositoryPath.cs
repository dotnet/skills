namespace SkillCatalog.Api.Services;

public static class SafeRepositoryPath
{
    public static string Resolve(string root, params string[] parts)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(new[] { canonicalRoot }.Concat(parts).ToArray()));
        if (!candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the repository boundary.");
        return candidate;
    }

    public static bool IsSafeRegularFile(string root, string path, long maxBytes)
    {
        var full = Resolve(root, Path.GetRelativePath(root, path));
        var info = new FileInfo(full);
        return info.Exists && info.Length <= maxBytes && (info.Attributes & FileAttributes.ReparsePoint) == 0;
    }
}
