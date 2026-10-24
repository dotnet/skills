namespace BlazorComponentReadiness.Validator.IO;

public static class AtomicFile
{
    public static void WriteNew(string root, string relativePath, byte[] content) =>
        WriteNew(root, relativePath, stream => stream.Write(content));

    public static void WriteNew(string root, string relativePath, Action<Stream> write)
    {
        var destination = PrepareDestination(root, relativePath);
        WriteThroughTemporary(destination, write, temporary => File.Move(temporary, destination, overwrite: false));
    }

    public static void Replace(string root, string relativePath, byte[] content) =>
        Replace(root, relativePath, stream => stream.Write(content));

    public static void Replace(string root, string relativePath, Action<Stream> write)
    {
        var destination = PrepareDestination(root, relativePath);
        WriteThroughTemporary(destination, write, temporary => File.Move(temporary, destination, overwrite: true));
    }

    public static void ReplacePair(
        string root,
        string firstRelativePath,
        byte[] firstContent,
        string secondRelativePath,
        byte[] secondContent)
    {
        var first = PrepareDestination(root, firstRelativePath);
        var second = PrepareDestination(root, secondRelativePath);
        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "A coherent output pair requires two distinct destinations.");
        }

        var firstTemporary = WriteTemporary(first, firstContent);
        var secondTemporary = WriteTemporary(second, secondContent);
        try
        {
            File.Move(firstTemporary, first, overwrite: true);
            firstTemporary = string.Empty;
            File.Move(secondTemporary, second, overwrite: true);
            secondTemporary = string.Empty;
        }
        finally
        {
            if (firstTemporary.Length != 0 && File.Exists(firstTemporary))
            {
                File.Delete(firstTemporary);
            }

            if (secondTemporary.Length != 0 && File.Exists(secondTemporary))
            {
                File.Delete(secondTemporary);
            }
        }
    }

    private static string PrepareDestination(string root, string relativePath)
    {
        var destination = SafePath.ResolveUnderRoot(root, relativePath, requireExisting: false);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new DeterministicValidationException("An atomic output must have a parent directory.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException($"The atomic output parent does not exist: {parent}");
        }

        if (Directory.Exists(destination))
        {
            throw new DeterministicValidationException("An atomic file destination cannot be a directory.");
        }

        if (File.Exists(destination))
        {
            SafePath.EnsureRegularFile(destination);
        }

        return destination;
    }

    private static void WriteThroughTemporary(
        string destination,
        Action<Stream> write,
        Action<string> commit)
    {
        ArgumentNullException.ThrowIfNull(write);
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            commit(temporary);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string WriteTemporary(string destination, byte[] content)
    {
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(
                   temporary,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 64 * 1024,
                   FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        return temporary;
    }
}
