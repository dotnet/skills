using System.Diagnostics;

var stopwatch = Stopwatch.StartNew();
using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

try
{
    await Worker.ObserveCancellationAsync(cancellation.Token);
    throw new InvalidOperationException("The operation completed instead of being canceled.");
}
catch (OperationCanceledException)
{
    if (stopwatch.Elapsed >= TimeSpan.FromSeconds(1))
    {
        throw new InvalidOperationException("Cancellation was not observed promptly.");
    }
}

Console.WriteLine("cancellation-ok");

internal static class Worker
{
    public static async Task ObserveCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
