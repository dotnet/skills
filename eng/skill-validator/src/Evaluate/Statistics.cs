namespace SkillValidator.Evaluate;

public static class Statistics
{
    /// <summary>
    /// Compute a bootstrap percentile confidence interval.
    /// Resamples <paramref name="data"/> with replacement <paramref name="iterations"/> times,
    /// computes the mean of each resample, and returns the percentile-based CI.
    /// </summary>
    public static ConfidenceInterval BootstrapConfidenceInterval(
        IReadOnlyList<double> data,
        double confidenceLevel = 0.95,
        int iterations = 10_000)
    {
        if (data.Count == 0)
            return new ConfidenceInterval(0, 0, confidenceLevel);

        if (data.Count == 1)
            return new ConfidenceInterval(data[0], data[0], confidenceLevel);

        var means = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            double sum = 0;
            for (int j = 0; j < data.Count; j++)
            {
                sum += data[(int)(SeededRandom(i * data.Count + j) * data.Count)];
            }
            means[i] = sum / data.Count;
        }

        Array.Sort(means);

        double alpha = 1 - confidenceLevel;
        int lowIdx = (int)(alpha / 2 * means.Length);
        int highIdx = (int)((1 - alpha / 2) * means.Length) - 1;

        return new ConfidenceInterval(
            means[Math.Max(0, lowIdx)],
            means[Math.Min(means.Length - 1, highIdx)],
            confidenceLevel);
    }

    /// <summary>
    /// Deterministic pseudo-random for reproducible bootstrap.
    /// Uses a simple splitmix-style hash.
    /// </summary>
    private static double SeededRandom(int seed)
    {
        uint s = (uint)seed;
        s = (s + 0x9e3779b9);
        s = (uint)((int)s * 0x85ebca6b) ^ (s >> 16);
        s = (uint)((int)s * 0xc2b2ae35) ^ (s >> 13);
        s = s ^ (s >> 16);
        return s / (double)0x100000000;
    }

    /// <summary>
    /// Check if the CI excludes zero, indicating statistical significance.
    /// </summary>
    public static bool IsStatisticallySignificant(ConfidenceInterval ci) =>
        ci.Low > 0 || ci.High < 0;

    /// <summary>
    /// Wilson score interval for a binomial proportion.
    /// Useful for pass/fail rates with small samples.
    /// </summary>
    public static ConfidenceInterval WilsonScoreInterval(
        int successes,
        int total,
        double confidenceLevel = 0.95)
    {
        if (total == 0)
            return new ConfidenceInterval(0, 0, confidenceLevel);

        double z = ZScore(confidenceLevel);
        double p = (double)successes / total;
        int n = total;

        double denominator = 1 + z * z / n;
        double center = (p + z * z / (2 * n)) / denominator;
        double margin = (z / denominator) * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n));

        return new ConfidenceInterval(
            Math.Max(0, center - margin),
            Math.Min(1, center + margin),
            confidenceLevel);
    }

    private static double ZScore(double confidenceLevel)
    {
        return confidenceLevel switch
        {
            0.9 => 1.645,
            0.99 => 2.576,
            _ => 1.96, // 0.95 default
        };
    }
}
