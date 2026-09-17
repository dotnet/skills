namespace Orders;

public sealed class ReservationWindow(TimeSpan holdDuration)
{
    public TimeSpan HoldDuration { get; } = holdDuration > TimeSpan.Zero
        ? holdDuration
        : throw new ArgumentOutOfRangeException(nameof(holdDuration));

    public bool IsActive(DateTimeOffset reservedAt, DateTimeOffset now)
        => now >= reservedAt && now < reservedAt + HoldDuration;
}
