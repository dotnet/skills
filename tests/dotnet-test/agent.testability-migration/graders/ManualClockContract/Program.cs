using ManualClock;

var clock = new SteppingClock();
var reader = new TimestampReader(clock);
var pair = reader.ReadPair();
Require(clock.Reads == 2, "ReadPair must read twice");
Require(pair.First == clock.Start.UtcDateTime, "First timestamp must use the first clock read");
Require(pair.Second == clock.Start.AddTicks(1).UtcDateTime, "Second timestamp must use the second clock read");
Require(pair.First.Kind == DateTimeKind.Utc && pair.Second.Kind == DateTimeKind.Utc,
    "Both timestamps must preserve UTC kind");
Console.WriteLine("Manual construction and independent UTC reads passed.");

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class SteppingClock : TimeProvider
{
    public DateTimeOffset Start { get; } = new(2045, 6, 7, 8, 9, 10, TimeSpan.FromHours(3));
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow() => Start.AddTicks(Reads++);
}
