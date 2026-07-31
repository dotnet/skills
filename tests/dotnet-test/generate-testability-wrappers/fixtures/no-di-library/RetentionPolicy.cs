namespace Contoso.Retention;

/// <summary>
/// Shipped as a NuGet library. Every member below is part of the released,
/// static public API surface — there is nothing here for a caller to construct
/// and nothing for a container to resolve.
/// </summary>
public static class RetentionPolicy
{
    public static bool IsExpired(DateTimeOffset createdAt, TimeSpan retention)
        => DateTime.UtcNow - createdAt > retention;

    public static DateTimeOffset NextSweep(DateTimeOffset lastSweep)
        => lastSweep.AddDays(1) < DateTime.UtcNow
            ? DateTime.UtcNow
            : lastSweep.AddDays(1);

    public static string StampFileName(string prefix)
        => $"{prefix}_{DateTime.UtcNow:yyyyMMddHHmmss}.log";
}
