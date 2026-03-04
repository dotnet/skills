using SkillValidator.Utilities;

namespace SkillValidator.Tests;

public class RetryHelperTests
{
    [Fact]
    public async Task SucceedsOnFirstAttempt_NoRetries()
    {
        var callCount = 0;
        var result = await RetryHelper.ExecuteWithRetry(
            () => { callCount++; return Task.FromResult(42); },
            "test");

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RetriesOnTransientFailure_ThenSucceeds()
    {
        var callCount = 0;
        var result = await RetryHelper.ExecuteWithRetry(
            () =>
            {
                callCount++;
                if (callCount < 2)
                    throw new InvalidOperationException("transient");
                return Task.FromResult("ok");
            },
            "test",
            maxRetries: 2,
            baseDelayMs: 1); // 1ms delay so test is fast

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ThrowsAfterAllRetriesExhausted()
    {
        var callCount = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                () =>
                {
                    callCount++;
                    throw new InvalidOperationException("always fails");
                },
                "test",
                maxRetries: 2,
                baseDelayMs: 1));

        Assert.Equal(3, callCount); // 1 initial + 2 retries
        Assert.Contains("all attempts failed", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task RespectsTotal_TimeoutBudget()
    {
        var callCount = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                async () =>
                {
                    callCount++;
                    // Simulate a slow operation that eats the budget
                    await Task.Delay(50);
                    throw new InvalidOperationException("timeout");
                },
                "test",
                maxRetries: 10, // many retries but budget is tiny
                baseDelayMs: 1,
                totalTimeoutMs: 100)); // 100ms total budget

        // Should have stopped before exhausting all 10 retries
        Assert.True(callCount < 10, $"Expected fewer than 10 attempts but got {callCount}");
    }

    [Fact]
    public async Task CancellationToken_StopsRetryLoop()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                () =>
                {
                    callCount++;
                    if (callCount == 1)
                        cts.Cancel(); // cancel after first failure
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 5,
                baseDelayMs: 1,
                cancellationToken: cts.Token));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExponentialBackoff_DelaysIncrease()
    {
        var timestamps = new List<long>();
        var callCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                () =>
                {
                    timestamps.Add(Environment.TickCount64);
                    callCount++;
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 2,
                baseDelayMs: 50)); // 50ms base → 50ms, 100ms

        Assert.Equal(3, callCount);
        // Second gap should be roughly double the first (with some tolerance)
        if (timestamps.Count == 3)
        {
            var gap1 = timestamps[1] - timestamps[0];
            var gap2 = timestamps[2] - timestamps[1];
            // gap2 should be larger than gap1 (exponential)
            Assert.True(gap2 > gap1, $"Expected exponential increase: gap1={gap1}ms, gap2={gap2}ms");
        }
    }

    [Fact]
    public async Task DelayClampedToRemainingBudget()
    {
        // With a very large base delay but small total budget,
        // the delay should be clamped to the remaining budget.
        var callCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                () =>
                {
                    callCount++;
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 1,
                baseDelayMs: 60_000, // 60s base delay - would be huge without clamping
                totalTimeoutMs: 200)); // 200ms total budget

        sw.Stop();
        Assert.Equal(2, callCount);
        // Should complete quickly since delay was clamped to remaining budget (~200ms), not 60s
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Took too long: {sw.ElapsedMilliseconds}ms");
    }
}
