var tracker = new WorkTracker();
var result = await tracker.RunWithTimeoutAsync(
    TimeSpan.FromMilliseconds(25),
    CancellationToken.None);

await Task.Delay(400);
Console.WriteLine($"{result}:{tracker.CompletedWorkCount}");
