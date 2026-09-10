namespace InstanceExpiration;

public sealed class ExpirationPolicy
{
    public bool IsExpired(DateTimeOffset expiresAt)
        => DateTimeOffset.UtcNow >= expiresAt;
}
