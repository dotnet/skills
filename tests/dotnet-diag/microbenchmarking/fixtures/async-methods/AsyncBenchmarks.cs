using BenchmarkDotNet.Attributes;

public class AsyncBenchmarks
{
    private readonly HttpClient _client = new();

    [Benchmark]
    public int FetchLength()
    {
        return _client.GetStringAsync("https://example.test/data").Result.Length;
    }
}
