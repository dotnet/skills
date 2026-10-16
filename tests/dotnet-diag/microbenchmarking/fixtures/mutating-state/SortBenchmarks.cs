using BenchmarkDotNet.Attributes;

public class SortBenchmarks
{
    private List<int> _values = null!;

    [GlobalSetup]
    public void Setup()
    {
        _values = Enumerable.Range(0, 10_000).Reverse().ToList();
    }

    [Benchmark(Baseline = true)]
    public void ListSort() => _values.Sort();

    [Benchmark]
    public void LinqOrderBy() => _values = _values.OrderBy(value => value).ToList();
}
