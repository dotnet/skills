using Resilience;
using Xunit;

namespace Resilience.Tests;

public sealed class RetryPolicyTests
{
    private readonly RetryPolicy _policy = new();

    [Fact]
    public void ShouldRetry_TimeoutOnFirstAttempt_ReturnsTrue()
    {
        Assert.True(_policy.ShouldRetry(new TimeoutException(), 1));
    }

    [Fact]
    public void ShouldRetry_NonTransientError_ReturnsFalse()
    {
        Assert.False(_policy.ShouldRetry(new InvalidOperationException(), 1));
    }

    [Fact]
    public void ShouldRetry_ThirdAttempt_ReturnsFalse()
    {
        Assert.False(_policy.ShouldRetry(new TimeoutException(), 3));
    }
}
