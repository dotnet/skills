namespace BlazorComponentReadiness.Validator.IO;

public static class BoundedIO
{
    public static byte[] ReadAllBytes(string path, long maximumBytes, string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateMaximum(maximumBytes);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The {resource} file was not found.", path);
        }

        EnsureLength(file.Length, maximumBytes, resource);
        using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return ReadAllBytes(stream, maximumBytes, resource);
    }

    public static byte[] ReadAllBytes(Stream input, long maximumBytes, string resource)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateMaximum(maximumBytes);

        if (input.CanSeek)
        {
            EnsureLength(input.Length - input.Position, maximumBytes, resource);
        }

        using var output = new MemoryStream(
            capacity: maximumBytes <= int.MaxValue ? checked((int)Math.Min(maximumBytes, 64 * 1024)) : 0);
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            EnsureLength(total, maximumBytes, resource);
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public static void EnsureFileLength(string path, long maximumBytes, string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The {resource} file was not found.", path);
        }

        EnsureLength(file.Length, maximumBytes, resource);
    }

    public static void EnsureLength(long length, long maximumBytes, string resource)
    {
        ValidateMaximum(maximumBytes);
        if (length < 0)
        {
            throw new DeterministicValidationException($"{resource} reported a negative byte length.");
        }

        if (length > maximumBytes)
        {
            throw new ResourceLimitException(resource, maximumBytes, length);
        }
    }

    private static void ValidateMaximum(long maximumBytes)
    {
        if (maximumBytes < 0 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBytes),
                maximumBytes,
                "Bounded in-memory reads require a non-negative limit no larger than Int32.MaxValue.");
        }
    }
}
