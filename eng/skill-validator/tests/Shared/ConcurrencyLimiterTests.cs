using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class ConcurrencyLimiterTests
{
    [Fact]
    public async Task RunAsync_ReturnsResult()
    {
        using var limiter = new ConcurrencyLimiter(2);
        var result = await limiter.RunAsync(() => Task.FromResult(42), TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_PropagatesExceptions()
    {
        using var limiter = new ConcurrencyLimiter(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.RunAsync<int>(() => throw new InvalidOperationException("boom"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_LimitsConcurrency()
    {
        using var limiter = new ConcurrencyLimiter(2);
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            limiter.RunAsync(async () =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }
                await Task.Delay(50, TestContext.Current.CancellationToken);
                lock (lockObj) { concurrentCount--; }
                return 1;
            }, TestContext.Current.CancellationToken));

        await Task.WhenAll(tasks);
        Assert.True(maxConcurrent <= 2, $"Max concurrency was {maxConcurrent}, expected ≤ 2");
        Assert.True(maxConcurrent >= 1, $"Max concurrency was {maxConcurrent}, expected ≥ 1");
    }

    [Fact]
    public async Task RunAsync_ConcurrentFailures_AllSurfaced()
    {
        using var limiter = new ConcurrencyLimiter(5);
        var tasks = Enumerable.Range(0, 5).Select(i =>
            limiter.RunAsync<int>(() =>
                throw new InvalidOperationException($"fail-{i}"), TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.WhenAll(tasks));
        Assert.Contains("fail-", ex.Message);
    }

    [Fact]
    public async Task RunAsync_SemaphoreReleasedOnFailure()
    {
        using var limiter = new ConcurrencyLimiter(1);

        // First call throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.RunAsync<int>(() => throw new InvalidOperationException("first"), TestContext.Current.CancellationToken));

        // Second call should still work (semaphore was released in finally)
        var result = await limiter.RunAsync(() => Task.FromResult(42), TestContext.Current.CancellationToken);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_RespectsMinimumConcurrencyOfOne()
    {
        using var limiter = new ConcurrencyLimiter(0); // should clamp to 1
        var result = await limiter.RunAsync(() => Task.FromResult("ok"), TestContext.Current.CancellationToken);
        Assert.Equal("ok", result);
    }
}
