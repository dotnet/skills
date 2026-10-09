using BenchmarkDotNet.Attributes;

public class FlawedBenchmarks
{
    [Benchmark]
    public void Normalize()
    {
        var values = Enumerable.Range(0, 1_000).ToArray();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            _ = string.Join(",", values);
        }
    }
}
