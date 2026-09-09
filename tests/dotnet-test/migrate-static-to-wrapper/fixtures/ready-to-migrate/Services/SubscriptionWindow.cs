namespace ReadyToMigrate.Services;

public sealed class SubscriptionWindow
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public bool IsExpired(DateTimeOffset expiresAt)
        => DateTimeOffset.UtcNow >= expiresAt;
}
