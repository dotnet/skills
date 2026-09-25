using System.Collections.Concurrent;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

/// <summary>
/// A local-filesystem implementation of <see cref="SessionFsProvider"/> that
/// maps SDK session-state I/O requests to physical files under a given root
/// directory.  Required since Copilot SDK no longer ships a built-in
/// default; without this handler, <c>events.jsonl</c> files are never written.
/// </summary>
internal sealed class LocalSessionFsHandler : SessionFsProvider
{
    private readonly string _stateRoot;
    private readonly string _workspaceRoot;
    private readonly string[] _allowedAbsoluteRoots;
    // The SDK can report "timeout while waiting for mutex to become available"
    // when multiple session-state writes race on the same JSONL file, so serialize
    // writes per resolved path inside the handler as well.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public LocalSessionFsHandler(
        string stateRoot,
        string workspaceRoot,
        IEnumerable<string> allowedAbsoluteRoots)
    {
        _stateRoot = NormalizeRoot(stateRoot);
        _workspaceRoot = NormalizeRoot(workspaceRoot);
        _allowedAbsoluteRoots = allowedAbsoluteRoots
            .Select(NormalizeRoot)
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        if (_allowedAbsoluteRoots.Length == 0)
            throw new ArgumentException("At least one absolute-path root is required.", nameof(allowedAbsoluteRoots));
        Directory.CreateDirectory(_stateRoot);
        Directory.CreateDirectory(_workspaceRoot);
    }

    private static string NormalizeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.EndsInDirectorySeparator(full)
            ? full
            : full + Path.DirectorySeparatorChar;
    }

    internal static bool IsSessionStatePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return false;

        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Equals("session-state", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(
                "session-state" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolve an SDK-provided path to an absolute local path, guarding against traversal.</summary>
    internal string ResolvePath(string path) => ResolvePathInfo(path).FullPath;

    private ResolvedPath ResolvePathInfo(string path)
    {
        string root;
        string full;
        if (Path.IsPathFullyQualified(path))
        {
            full = Path.GetFullPath(path);
            root = FindAllowedAbsoluteRoot(full)
                ?? throw new UnauthorizedAccessException($"Path outside allowed roots: {path}");
        }
        else
        {
            root = IsSessionStatePath(path) ? _stateRoot : _workspaceRoot;
            full = Path.GetFullPath(Path.Combine(root, path));
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.Equals(Path.TrimEndingDirectorySeparator(root), comparison)
            && !full.StartsWith(root, comparison))
        {
            throw new UnauthorizedAccessException($"Path traversal blocked: {path}");
        }
        if (PathSafety.ContainsReparsePoint(
            Path.TrimEndingDirectorySeparator(root),
            Path.TrimEndingDirectorySeparator(full),
            missingPathIsUnsafe: false))
        {
            throw new UnauthorizedAccessException($"Symbolic-link traversal blocked: {path}");
        }
        return new ResolvedPath(root, full);
    }

    private string? FindAllowedAbsoluteRoot(string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return _allowedAbsoluteRoots.FirstOrDefault(root =>
            fullPath.Equals(Path.TrimEndingDirectorySeparator(root), comparison)
            || fullPath.StartsWith(root, comparison));
    }

    private async Task ExecuteWithPathLockAsync(string path, Func<Task> action, CancellationToken cancellationToken)
    {
        var pathLock = _pathLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            pathLock.Release();
        }
    }

    protected override async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        return await SecureFileSystem.ReadAllTextAsync(
            resolved.Root,
            resolved.FullPath,
            cancellationToken);
    }

    protected override Task WriteFileAsync(string path, string content, int? mode, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        return ExecuteWithPathLockAsync(resolved.FullPath, () =>
            SecureFileSystem.WriteAllTextAsync(
                resolved.Root,
                resolved.FullPath,
                content,
                append: false,
                cancellationToken), cancellationToken);
    }

    protected override Task AppendFileAsync(string path, string content, int? mode, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        return ExecuteWithPathLockAsync(resolved.FullPath, () =>
            SecureFileSystem.WriteAllTextAsync(
                resolved.Root,
                resolved.FullPath,
                content,
                append: true,
                cancellationToken), cancellationToken);
    }

    protected override Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        var exists = SecureFileSystem.Exists(
            resolved.Root,
            resolved.FullPath);
        return Task.FromResult(exists);
    }

    protected override Task<SessionFsStatResult> StatAsync(string path, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        var status = SecureFileSystem.GetStatus(
            resolved.Root,
            resolved.FullPath);
        return Task.FromResult(new SessionFsStatResult
        {
            IsFile = status.IsFile,
            IsDirectory = status.IsDirectory,
            Size = status.Size,
            Mtime = status.Mtime,
            Birthtime = status.Birthtime,
        });
    }

    protected override Task MakeDirectoryAsync(string path, bool recursive, int? mode, CancellationToken cancellationToken)
    {
        var resolved = ResolvePathInfo(path);
        SecureFileSystem.CreateDirectory(resolved.Root, resolved.FullPath);
        return Task.CompletedTask;
    }

    protected override Task<IList<string>> ReadDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(path);
        var entries = new List<string>();
        if (Directory.Exists(resolved))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(resolved))
                entries.Add(Path.GetFileName(entry));
        }
        return Task.FromResult<IList<string>>(entries);
    }

    protected override Task<IList<SessionFsReaddirWithTypesEntry>> ReadDirectoryWithTypesAsync(string path, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(path);
        var entries = new List<SessionFsReaddirWithTypesEntry>();
        if (Directory.Exists(resolved))
        {
            foreach (var entry in new DirectoryInfo(resolved).EnumerateFileSystemInfos())
            {
                entries.Add(new SessionFsReaddirWithTypesEntry
                {
                    Name = entry.Name,
                    Type = entry is DirectoryInfo ? SessionFsReaddirWithTypesEntryType.Directory : SessionFsReaddirWithTypesEntryType.File,
                });
            }
        }
        return Task.FromResult<IList<SessionFsReaddirWithTypesEntry>>(entries);
    }

    protected override Task RemoveAsync(string path, bool recursive, bool force, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved))
        {
            File.Delete(resolved);
        }
        else if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: recursive);
        }
        return Task.CompletedTask;
    }

    protected override Task RenameAsync(string src, string dest, CancellationToken cancellationToken)
    {
        var resolvedSrc = ResolvePath(src);
        var resolvedDest = ResolvePath(dest);
        var destDir = Path.GetDirectoryName(resolvedDest);
        if (destDir is not null) Directory.CreateDirectory(destDir);

        if (File.Exists(resolvedSrc))
            File.Move(resolvedSrc, resolvedDest, overwrite: true);
        else if (Directory.Exists(resolvedSrc))
            Directory.Move(resolvedSrc, resolvedDest);
        return Task.CompletedTask;
    }

    private readonly record struct ResolvedPath(string Root, string FullPath);
}
