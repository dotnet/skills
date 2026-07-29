namespace OptimizingEfCoreQueries.Shared;

// Guards run once in each benchmark's [GlobalSetup]: they fail the run if the
// optimized query does not return the same data as the baseline, so a query
// that is fast only because it is wrong cannot pass the eval.
public static class EquivalenceGuard
{
    public static void SameResults<T>(
        IReadOnlyCollection<T> baseline,
        IReadOnlyCollection<T> optimized,
        Func<T, string> orderKey)
    {
        Require(
            baseline.Count == optimized.Count,
            $"row count differs: baseline={baseline.Count} optimized={optimized.Count}");

        var expected = baseline.OrderBy(orderKey, StringComparer.Ordinal).ToList();
        var actual = optimized.OrderBy(orderKey, StringComparer.Ordinal).ToList();

        for (var i = 0; i < expected.Count; i++)
        {
            Require(
                EqualityComparer<T>.Default.Equals(expected[i], actual[i]),
                $"row {i} differs: baseline={expected[i]} optimized={actual[i]}");
        }
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                "Optimized query is not equivalent to the baseline — " + message);
        }
    }
}
