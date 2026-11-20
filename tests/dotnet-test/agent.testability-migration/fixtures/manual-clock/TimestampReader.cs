namespace ManualClock;

public sealed class TimestampReader
{
    public (DateTime First, DateTime Second) ReadPair() =>
        (DateTime.UtcNow, DateTime.UtcNow);
}
