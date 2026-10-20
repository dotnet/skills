using BenchmarkDotNet.Attributes;

public class SearchBenchmarks
{
    [Params(10, 100, 1_000)]
    public int Count { get; set; }

    [Params("alpha", "omega", "missing", "ALPHA")]
    public string Query { get; set; } = "";

    [Benchmark] public int Linear() => Count + Query.Length;
    [Benchmark] public int Binary() => Count + Query.Length;
    [Benchmark] public int HashSet() => Count + Query.Length;
    [Benchmark] public int FrozenSet() => Count + Query.Length;
    [Benchmark] public int SpanSearch() => Count + Query.Length;
}
