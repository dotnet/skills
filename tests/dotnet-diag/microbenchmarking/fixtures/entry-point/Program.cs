using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

public class ParserBenchmarks
{
    private readonly string _value = "42";

    [Benchmark]
    public int Parse() => int.Parse(_value);
}

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run();
    }
}
