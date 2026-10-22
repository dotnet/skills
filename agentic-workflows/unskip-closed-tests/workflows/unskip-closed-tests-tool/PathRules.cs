using System.Text.RegularExpressions;

namespace UnskipClosedTests.Tool;

internal static class PathRules
{
    public static string ValidateRelativePath(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ContractException($"{context} '{value}' must be a non-empty repository-relative path.");
        }

        string normalized = value.Replace('\\', '/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(static part => part is "." or ".."))
        {
            throw new ContractException($"{context} '{value}' contains traversal or an empty path.");
        }

        return string.Join('/', parts);
    }

    public static string ResolveInsideRoot(string repoRoot, string relativePath, string context)
    {
        string normalized = ValidateRelativePath(relativePath, context);
        string root = Path.GetFullPath(repoRoot);
        string fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractException($"{context} '{relativePath}' escapes the repository root.");
        }

        return fullPath;
    }

    public static void RejectReparsePoints(string repoRoot, string fullPath)
    {
        string root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar);
        string current = Path.GetFullPath(fullPath);
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                throw new ContractException($"Source path '{fullPath}' does not exist.");
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ContractException($"Source path '{fullPath}' traverses a symlink or reparse point.");
            }

            current = Path.GetDirectoryName(current)
                ?? throw new ContractException($"Source path '{fullPath}' is outside the repository.");
        }
    }

    public static bool MatchesAnyGlob(string path, IEnumerable<string> globs) =>
        globs.Any(glob => GlobToRegex(glob).IsMatch(path));

    private static Regex GlobToRegex(string glob)
    {
        string normalized = glob.Replace('\\', '/');
        string pattern = Regex.Escape(normalized)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return new Regex($"^{pattern}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }
}
