using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class RuntimeBenchmarks
{
    private readonly string _value = "invoice-2026-10-01";

    [Benchmark]
    public int Parse() => _value.LastIndexOf('-');
}

public static class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<RuntimeBenchmarks>();
    }
}
