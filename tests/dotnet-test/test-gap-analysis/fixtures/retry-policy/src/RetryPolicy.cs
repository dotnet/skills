namespace Resilience;

public sealed class RetryPolicy
{
    public bool ShouldRetry(Exception error, int attempt)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        if (attempt >= 3)
        {
            return false;
        }

        return error is TimeoutException or IOException;
    }
}
