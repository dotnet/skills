namespace Licensing;

public sealed class ExpirationPolicy
{
    public bool IsExpired(DateTime expiresAtUtc) => DateTime.UtcNow >= expiresAtUtc;

    public DateTime CurrentUtc() => DateTime.UtcNow;
}
