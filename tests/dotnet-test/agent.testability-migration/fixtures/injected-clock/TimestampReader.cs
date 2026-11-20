namespace InjectedClock;

public sealed class TimestampReader(TimeProvider timeProvider)
{
    public DateTime Read() => timeProvider.GetUtcNow().UtcDateTime;
}
