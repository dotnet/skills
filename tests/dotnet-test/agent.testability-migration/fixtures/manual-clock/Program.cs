using ManualClock;

var stamps = new TimestampReader().ReadPair();
Console.WriteLine($"{stamps.First:O} / {stamps.Second:O}");
