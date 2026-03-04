namespace SkillValidator.Utilities;

/// <summary>
/// Shared retry-with-exponential-backoff helper used by both Judge and PairwiseJudge.
/// Caps total elapsed time so that repeated timeouts don't blow past the workflow budget.
/// </summary>
public static class RetryHelper
{
    /// <summary>Default base delay between retries (doubles each attempt).</summary>
    public const int DefaultBaseDelayMs = 5_000;

    /// <summary>Default maximum number of retries after the initial attempt.</summary>
    public const int DefaultMaxRetries = 2;

    /// <summary>
    /// Maximum total wall-clock time (across all attempts + backoff) before giving up.
    /// This prevents a chain of timeout→retry→timeout from exceeding the job-level budget.
    /// Default: 10 minutes.
    /// </summary>
    public const int DefaultTotalTimeoutMs = 10 * 60 * 1000;

    /// <summary>Absolute upper bound on any single backoff delay (60 seconds).</summary>
    public const int MaxSingleDelayMs = 60_000;

    /// <summary>
    /// Executes <paramref name="action"/> with retries and exponential backoff.
    /// </summary>
    /// <param name="action">The async operation to attempt.</param>
    /// <param name="label">Human-readable label for log messages (e.g. "Judge for \"scenario X\"").</param>
    /// <param name="maxRetries">Maximum retries after the first attempt.</param>
    /// <param name="baseDelayMs">Base delay in milliseconds (doubles each retry).</param>
    /// <param name="totalTimeoutMs">
    /// Hard cap on total elapsed time. When exceeded, the helper stops retrying even if
    /// <paramref name="maxRetries"/> has not been exhausted.
    /// </param>
    /// <param name="cancellationToken">Optional token to cancel the retry loop.</param>
    public static async Task<T> ExecuteWithRetry<T>(
        Func<Task<T>> action,
        string label,
        int maxRetries = DefaultMaxRetries,
        int baseDelayMs = DefaultBaseDelayMs,
        int totalTimeoutMs = DefaultTotalTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        var overallStart = Environment.TickCount64;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check total elapsed time before starting a new attempt
            var elapsed = Environment.TickCount64 - overallStart;
            if (attempt > 0 && elapsed >= totalTimeoutMs)
            {
                Console.Error.WriteLine(
                    $"      ⏱️  {label}: total retry budget exhausted ({elapsed / 1000}s elapsed, cap {totalTimeoutMs / 1000}s). Giving up.");
                break;
            }

            try
            {
                if (attempt > 0)
                {
                    // Use long arithmetic to avoid int overflow for large retry counts.
                    var rawDelay = (long)baseDelayMs * (1L << (attempt - 1));
                    var remaining = totalTimeoutMs - (Environment.TickCount64 - overallStart);
                    // Clamp: cap to MaxSingleDelayMs and remaining budget so we don't overshoot.
                    var delay = (int)Math.Min(rawDelay, Math.Min(MaxSingleDelayMs, Math.Max(0, remaining)));
                    Console.Error.WriteLine($"      🔄 {label}: retry {attempt}/{maxRetries} (waiting {delay / 1000}s)");
                    await Task.Delay(delay, cancellationToken);
                }
                return await action();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Don't retry on explicit cancellation.
            }
            catch (Exception error)
            {
                lastError = error;
                Console.Error.WriteLine(
                    $"      ⚠️  {label}: attempt {attempt + 1} failed: " +
                    $"{error.Message[..Math.Min(200, error.Message.Length)]}");
            }
        }

        throw new InvalidOperationException(
            $"{label}: all attempts failed after {(Environment.TickCount64 - overallStart) / 1000}s: {lastError?.Message}", lastError);
    }
}
