namespace BlazorComponentReadiness.Validator.IO;

public static class AtomicDirectory
{
    public static void WriteNew(string root, string relativeDirectory, Action<string> populate)
    {
        ArgumentNullException.ThrowIfNull(populate);
        Directory.CreateDirectory(root);
        var destination = SafePath.ResolveUnderRoot(root, relativeDirectory, requireExisting: false);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new DeterministicValidationException(
                $"Immutable revision already exists and cannot be overwritten: {destination}");
        }

        var staging = Path.Combine(
            root,
            $".{Path.GetFileName(relativeDirectory)}.{Guid.NewGuid():N}.staging");
        Directory.CreateDirectory(staging);
        try
        {
            populate(staging);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
