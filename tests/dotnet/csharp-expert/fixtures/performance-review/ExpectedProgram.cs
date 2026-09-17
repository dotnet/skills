using System.Diagnostics;

var values = Enumerable.Range(1, 1_000).ToArray();

Measure(() => SumLoop(values), 5_000);
Measure(() => SumSorted(values), 5_000);

var loop = Measure(() => SumLoop(values), 20_000);
var sorted = Measure(() => SumSorted(values), 20_000);

if (loop.Result != sorted.Result)
{
    throw new InvalidOperationException("The candidate changed the result.");
}

Console.WriteLine($"loop-ms={loop.Elapsed.TotalMilliseconds:F2}");
Console.WriteLine($"candidate-ms={sorted.Elapsed.TotalMilliseconds:F2}");
Console.WriteLine($"ratio={sorted.Elapsed.TotalMilliseconds / loop.Elapsed.TotalMilliseconds:F2}");

static (long Result, TimeSpan Elapsed) Measure(Func<int> action, int iterations)
{
    var stopwatch = Stopwatch.StartNew();
    long result = 0;

    for (var index = 0; index < iterations; index++)
    {
        result += action();
    }

    stopwatch.Stop();
    return (result, stopwatch.Elapsed);
}

static int SumLoop(int[] values)
{
    var sum = 0;

    foreach (var value in values)
    {
        sum += value;
    }

    return sum;
}

static int SumSorted(int[] values)
{
    return values.Order().ToArray().Sum();
}
