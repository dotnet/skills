using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class ConcurrencyLimiterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunAsync_ReturnsResult()
    {
        using var limiter = new ConcurrencyLimiter(2);
        var result = await limiter.RunAsync(() => Task.FromResult(42), TestContext.CancellationToken);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task RunAsync_PropagatesExceptions()
    {
        using var limiter = new ConcurrencyLimiter(2);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            limiter.RunAsync<int>(() => throw new InvalidOperationException("boom"), TestContext.CancellationToken));
    }

    [TestMethod]
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
                await Task.Delay(50, TestContext.CancellationToken);
                lock (lockObj) { concurrentCount--; }
                return 1;
            }, TestContext.CancellationToken));

        await Task.WhenAll(tasks);
        Assert.IsTrue(maxConcurrent <= 2, $"Max concurrency was {maxConcurrent}, expected ≤ 2");
        Assert.IsTrue(maxConcurrent >= 1, $"Max concurrency was {maxConcurrent}, expected ≥ 1");
    }

    [TestMethod]
    public async Task RunAsync_ConcurrentFailures_AllSurfaced()
    {
        using var limiter = new ConcurrencyLimiter(5);
        var tasks = Enumerable.Range(0, 5).Select(i =>
            limiter.RunAsync<int>(() =>
                throw new InvalidOperationException($"fail-{i}"), TestContext.CancellationToken));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Task.WhenAll(tasks));
        Assert.Contains("fail-", ex.Message);
    }

    [TestMethod]
    public async Task RunAsync_SemaphoreReleasedOnFailure()
    {
        using var limiter = new ConcurrencyLimiter(1);

        // First call throws
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            limiter.RunAsync<int>(() => throw new InvalidOperationException("first"), TestContext.CancellationToken));

        // Second call should still work (semaphore was released in finally)
        var result = await limiter.RunAsync(() => Task.FromResult(42), TestContext.CancellationToken);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task RunAsync_RespectsMinimumConcurrencyOfOne()
    {
        using var limiter = new ConcurrencyLimiter(0); // should clamp to 1
        var result = await limiter.RunAsync(() => Task.FromResult("ok"), TestContext.CancellationToken);
        Assert.AreEqual("ok", result);
    }
}
