using BenchmarkDotNet.Attributes;

public class AllocationBenchmarks
{
    private readonly string[] _values = Enumerable.Repeat(" telemetry ", 1_000).ToArray();

    [Benchmark(Baseline = true)]
    public string[] TrimWithArray() => _values.Select(value => value.Trim()).ToArray();

    [Benchmark]
    public List<string> TrimWithList() => _values.Select(value => value.Trim()).ToList();
}
