using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.IO;

public static class SafePath
{
    public static string ResolveUnderRoot(
        string root,
        string relativePath,
        bool requireExisting,
        bool requireFile = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new DeterministicValidationException("A rooted path cannot be resolved beneath an artifact root.");
        }

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new DeterministicValidationException("Artifact paths cannot contain empty, '.' or '..' segments.");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, comparison))
        {
            throw new DeterministicValidationException("The artifact path escapes its declared root.");
        }

        RejectReparsePoints(fullRoot, fullPath);

        if (requireExisting && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("The path beneath the artifact root does not exist.", fullPath);
        }

        if (requireFile && !File.Exists(fullPath))
        {
            throw new DeterministicValidationException("The path beneath the artifact root is not a regular file.");
        }

        return fullPath;
    }

    public static void EnsureRegularFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The input file does not exist.", fullPath);
        }

        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeterministicValidationException(
                $"Symbolic-link input files are not allowed: {fullPath}");
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new DeterministicValidationException("The input path is not a regular file.");
        }
    }

    public static string ResolveExistingDirectoryUnderRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var resolved = ResolveUnderRoot(
            fullRoot,
            Canonicalization.RelativePath(relative, "directory path"),
            requireExisting: true);
        if (!Directory.Exists(resolved) ||
            (File.GetAttributes(resolved) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) !=
            FileAttributes.Directory)
        {
            throw new DeterministicValidationException(
                "The path beneath the artifact root is not a regular non-link directory.");
        }

        return resolved;
    }

    public static void ValidateArchiveEntry(string entryName, bool isSymbolicLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        var normalized = entryName.EndsWith("/", StringComparison.Ordinal)
            ? entryName[..^1]
            : entryName;
        if (isSymbolicLink ||
            Path.IsPathRooted(entryName) ||
            entryName.Contains('\\', StringComparison.Ordinal) ||
            normalized.Length == 0 ||
            normalized.Split('/', StringSplitOptions.None).Any(segment => segment is "" or "." or ".."))
        {
            throw new DeterministicValidationException(
                $"Package entry '{entryName}' is not a safe regular archive path.");
        }
    }

    private static void RejectReparsePoints(string fullRoot, string fullPath)
    {
        if (Directory.Exists(fullRoot) || File.Exists(fullRoot))
        {
            RejectReparsePoint(fullRoot);
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var current = fullRoot;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DeterministicValidationException($"Symbolic links and reparse points are not allowed: {path}");
        }
    }
}
