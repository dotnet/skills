using GitHub.Copilot.SDK.Rpc;

namespace SkillValidator.Evaluate;

/// <summary>
/// A local-filesystem implementation of <see cref="ISessionFsHandler"/> that
/// maps SDK session-state I/O requests to physical files under a given root
/// directory.  Required since Copilot SDK 0.2.x no longer ships a built-in
/// default; without this handler, <c>events.jsonl</c> files are never written.
/// </summary>
internal sealed class LocalSessionFsHandler : ISessionFsHandler
{
    private readonly string _rootDir;

    public LocalSessionFsHandler(string rootDir)
    {
        _rootDir = Path.GetFullPath(rootDir);
        if (!Path.EndsInDirectorySeparator(_rootDir))
            _rootDir += Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_rootDir);
    }

    /// <summary>Resolve an SDK-provided path to an absolute local path, guarding against traversal.</summary>
    private string ResolvePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_rootDir, relativePath));
        if (!full.StartsWith(_rootDir, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path traversal blocked: {relativePath}");
        return full;
    }

    public async Task<SessionFsReadFileResult> ReadFileAsync(SessionFsReadFileParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return new SessionFsReadFileResult { Content = content };
    }

    public async Task WriteFileAsync(SessionFsWriteFileParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, request.Content, cancellationToken);
    }

    public async Task AppendFileAsync(SessionFsAppendFileParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.AppendAllTextAsync(path, request.Content, cancellationToken);
    }

    public Task<SessionFsExistsResult> ExistsAsync(SessionFsExistsParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var exists = File.Exists(path) || Directory.Exists(path);
        return Task.FromResult(new SessionFsExistsResult { Exists = exists });
    }

    public Task<SessionFsStatResult> StatAsync(SessionFsStatParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return Task.FromResult(new SessionFsStatResult
            {
                IsFile = true,
                IsDirectory = false,
                Size = info.Length,
                Mtime = info.LastWriteTimeUtc.ToString("O"),
                Birthtime = info.CreationTimeUtc.ToString("O"),
            });
        }

        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return Task.FromResult(new SessionFsStatResult
            {
                IsFile = false,
                IsDirectory = true,
                Size = 0,
                Mtime = info.LastWriteTimeUtc.ToString("O"),
                Birthtime = info.CreationTimeUtc.ToString("O"),
            });
        }

        throw new FileNotFoundException($"Not found: {request.Path}");
    }

    public Task MkdirAsync(SessionFsMkdirParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task<SessionFsReaddirResult> ReaddirAsync(SessionFsReaddirParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var entries = new List<string>();
        if (Directory.Exists(path))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                entries.Add(Path.GetFileName(entry));
        }
        return Task.FromResult(new SessionFsReaddirResult { Entries = entries });
    }

    public Task<SessionFsReaddirWithTypesResult> ReaddirWithTypesAsync(SessionFsReaddirWithTypesParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        var entries = new List<Entry>();
        if (Directory.Exists(path))
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                entries.Add(new Entry
                {
                    Name = entry.Name,
                    Type = entry is DirectoryInfo ? EntryType.Directory : EntryType.File,
                });
            }
        }
        return Task.FromResult(new SessionFsReaddirWithTypesResult { Entries = entries });
    }

    public Task RmAsync(SessionFsRmParams request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.Path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: request.Recursive ?? false);
        }
        return Task.CompletedTask;
    }

    public Task RenameAsync(SessionFsRenameParams request, CancellationToken cancellationToken)
    {
        var src = ResolvePath(request.Src);
        var dest = ResolvePath(request.Dest);
        var destDir = Path.GetDirectoryName(dest);
        if (destDir is not null) Directory.CreateDirectory(destDir);

        if (File.Exists(src))
            File.Move(src, dest, overwrite: true);
        else if (Directory.Exists(src))
            Directory.Move(src, dest);
        return Task.CompletedTask;
    }
}
