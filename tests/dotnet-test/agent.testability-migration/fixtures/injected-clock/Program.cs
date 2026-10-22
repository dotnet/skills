using InjectedClock;

var instant = new TimestampReader(TimeProvider.System).Read();
Console.WriteLine(instant.ToString("O"));
