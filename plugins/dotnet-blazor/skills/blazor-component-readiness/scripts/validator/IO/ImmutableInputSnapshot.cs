namespace BlazorComponentReadiness.Validator.IO;

public sealed record ImmutableInputSnapshot(
    string Path,
    string Resource,
    byte[] Bytes)
{
    public static ImmutableInputSnapshot Capture(string path, string resource) =>
        Capture(path, ResourceLimits.SerializedArtifactBytes, resource);

    public static ImmutableInputSnapshot Capture(string path, long maximumBytes, string resource) =>
        new(
            System.IO.Path.GetFullPath(path),
            resource,
            BoundedIO.ReadAllBytes(path, maximumBytes, resource));

    public void EnsureUnchanged()
    {
        var actual = BoundedIO.ReadAllBytes(
            Path,
            Math.Max(Bytes.LongLength, 1),
            Resource);
        if (!actual.AsSpan().SequenceEqual(Bytes))
        {
            throw new DeterministicValidationException(
                $"{Resource} changed while the report revision was being rendered.");
        }
    }
}
