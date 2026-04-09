using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class RetryHelperTests
{
    [Fact]
    public async Task SucceedsOnFirstAttempt_NoRetries()
    {
        var callCount = 0;
        var result = await RetryHelper.ExecuteWithRetry(
            (_) => { callCount++; return Task.FromResult(42); },
            "test",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task RetriesOnTransientFailure_ThenSucceeds()
    {
        var callCount = 0;
        var result = await RetryHelper.ExecuteWithRetry(
            (_) =>
            {
                callCount++;
                if (callCount < 2)
                    throw new InvalidOperationException("transient");
                return Task.FromResult("ok");
            },
            "test",
            maxRetries: 2,
            baseDelayMs: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ThrowsAfterAllRetriesExhausted()
    {
        var callCount = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetry<int>(
                (_) =>
                {
                    callCount++;
                    throw new InvalidOperationException("always fails");
                },
                "test",
                maxRetries: 2,
                baseDelayMs: 1,
                cancellationToken: TestContext.Current.CancellationToken));

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
                async (_) =>
                {
                    callCount++;
                    // Simulate a slow operation that eats the budget
                    await Task.Delay(50);
                    throw new InvalidOperationException("timeout");
                },
                "test",
                maxRetries: 10, // many retries but budget is tiny
                baseDelayMs: 1,
                totalTimeoutMs: 100,
                cancellationToken: TestContext.Current.CancellationToken));

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
                (_) =>
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
        // Uses injected clock/delay to avoid real-time sensitivity on Windows
        // where JIT/scheduler jitter can inflate the first measured gap (see dotnet/skills#288).
        var callCount = 0;
        var fakeTimeMs = 0L;
        var recordedDelays = new List<int>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetryCore<int>(
                (_) =>
                {
                    callCount++;
                    fakeTimeMs += 5; // simulate 5ms per attempt
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 2,
                baseDelayMs: 200,
                totalTimeoutMs: 60_000,
                cancellationToken: TestContext.Current.CancellationToken,
                clock: () => fakeTimeMs,
                delayFunc: (ms, _) => { recordedDelays.Add(ms); fakeTimeMs += ms; return Task.CompletedTask; },
                jitterFunc: () => 0.5)); // 0.5 → zero jitter offset ((0.5*2-1)*range = 0)

        Assert.Equal(3, callCount);
        // With baseDelayMs=200, expected delays are: 200ms (attempt 1), 400ms (attempt 2)
        Assert.Equal(2, recordedDelays.Count);
        Assert.True(recordedDelays[1] > recordedDelays[0],
            $"Expected exponential increase: delay1={recordedDelays[0]}ms, delay2={recordedDelays[1]}ms");
        Assert.Equal(200, recordedDelays[0]); // baseDelayMs * 2^0
        Assert.Equal(400, recordedDelays[1]); // baseDelayMs * 2^1
    }

    [Fact]
    public async Task DelayClampedToRemainingBudget()
    {
        // With a very large base delay but small total budget,
        // the delay should be clamped to the remaining budget.
        // Uses injected clock/delay to avoid real-time sensitivity (see dotnet/skills#168).
        var callCount = 0;
        var fakeTimeMs = 0L;
        var recordedDelays = new List<int>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetryCore<int>(
                (_) =>
                {
                    callCount++;
                    fakeTimeMs += 10; // simulate 10ms per attempt
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 1,
                baseDelayMs: 60_000, // 60s base delay - would be huge without clamping
                totalTimeoutMs: 2000,
                cancellationToken: TestContext.Current.CancellationToken,
                clock: () => fakeTimeMs,
                delayFunc: (ms, _) => { recordedDelays.Add(ms); return Task.CompletedTask; },
                jitterFunc: () => 0.5)); // zero jitter offset

        Assert.Equal(2, callCount);
        // The retry delay should be clamped to remaining budget, not the raw 60s.
        Assert.Single(recordedDelays);
        Assert.True(recordedDelays[0] <= 2000,
            $"Delay should be clamped to remaining budget, got {recordedDelays[0]}ms");
        Assert.True(recordedDelays[0] >= 0,
            $"Delay should be non-negative, got {recordedDelays[0]}ms");
    }

    [Fact]
    public async Task Jitter_AdjustsDelayWithinRange()
    {
        var fakeTimeMs = 0L;
        var recordedDelays = new List<int>();

        // jitterFunc returns 1.0 → max positive jitter offset
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetryCore<int>(
                (_) =>
                {
                    fakeTimeMs += 5;
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 1,
                baseDelayMs: 1000,
                totalTimeoutMs: 60_000,
                cancellationToken: TestContext.Current.CancellationToken,
                clock: () => fakeTimeMs,
                delayFunc: (ms, _) => { recordedDelays.Add(ms); fakeTimeMs += ms; return Task.CompletedTask; },
                jitterFunc: () => 1.0)); // max positive: (1.0*2-1)*250 = +250

        Assert.Single(recordedDelays);
        // baseDelay=1000, jitter=25% of 1000=250, offset=+250 → 1250
        Assert.Equal(1250, recordedDelays[0]);
    }

    [Fact]
    public async Task Jitter_NegativeOffset_ReducesDelay()
    {
        var fakeTimeMs = 0L;
        var recordedDelays = new List<int>();

        // jitterFunc returns 0.0 → max negative jitter offset
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetryHelper.ExecuteWithRetryCore<int>(
                (_) =>
                {
                    fakeTimeMs += 5;
                    throw new InvalidOperationException("fail");
                },
                "test",
                maxRetries: 1,
                baseDelayMs: 1000,
                totalTimeoutMs: 60_000,
                cancellationToken: TestContext.Current.CancellationToken,
                clock: () => fakeTimeMs,
                delayFunc: (ms, _) => { recordedDelays.Add(ms); fakeTimeMs += ms; return Task.CompletedTask; },
                jitterFunc: () => 0.0)); // max negative: (0.0*2-1)*250 = -250

        Assert.Single(recordedDelays);
        // baseDelay=1000, jitter=-250 → 750
        Assert.Equal(750, recordedDelays[0]);
    }

    [Fact]
    public async Task BudgetToken_FlowsToAction()
    {
        // Verify that the CancellationToken passed to the action is functional.
        CancellationToken capturedToken = default;
        await RetryHelper.ExecuteWithRetry(
            (ct) =>
            {
                capturedToken = ct;
                return Task.FromResult(true);
            },
            "test",
            cancellationToken: TestContext.Current.CancellationToken);

        // The token should be a real linked token, not CancellationToken.None.
        Assert.True(capturedToken.CanBeCanceled);
    }
}
