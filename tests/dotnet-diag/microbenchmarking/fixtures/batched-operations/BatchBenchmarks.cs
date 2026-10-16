using BenchmarkDotNet.Attributes;

public class BatchBenchmarks
{
    private readonly Guid[] _values = Enumerable.Range(0, 1_000)
        .Select(_ => Guid.NewGuid())
        .ToArray();

    [Benchmark]
    public int FormatBatch()
    {
        var total = 0;
        for (var index = 0; index < _values.Length; index++)
        {
            total += _values[index].ToString("N").Length;
        }
        return total;
    }
}
