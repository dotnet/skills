namespace SkillValidator.Shared;

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
    /// A linked <see cref="CancellationToken"/> that honours both <paramref name="cancellationToken"/>
    /// and <paramref name="totalTimeoutMs"/> is passed into the action so that in-progress async
    /// operations (e.g. SDK / HTTP calls) are cancelled promptly when the budget expires.
    /// </summary>
    /// <param name="action">The async operation to attempt. Receives a budget-linked token.</param>
    /// <param name="label">Human-readable label for log messages (e.g. "Judge for \"scenario X\"").</param>
    /// <param name="maxRetries">Maximum retries after the first attempt.</param>
    /// <param name="baseDelayMs">Base delay in milliseconds (doubles each retry).</param>
    /// <param name="totalTimeoutMs">
    /// Hard cap on total elapsed time. When exceeded, the helper stops retrying even if
    /// <paramref name="maxRetries"/> has not been exhausted.
    /// </param>
    /// <param name="cancellationToken">Optional token to cancel the retry loop.</param>
    public static Task<T> ExecuteWithRetry<T>(
        Func<CancellationToken, Task<T>> action,
        string label,
        int maxRetries = DefaultMaxRetries,
        int baseDelayMs = DefaultBaseDelayMs,
        int totalTimeoutMs = DefaultTotalTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetryCore(action, label, maxRetries, baseDelayMs, totalTimeoutMs,
            cancellationToken, clock: null, delayFunc: null);
    }

    /// <summary>
    /// Internal overload with injectable <paramref name="clock"/> and <paramref name="delayFunc"/>
    /// seams so that tests can run without real wall-clock timing.
    /// </summary>
    internal static async Task<T> ExecuteWithRetryCore<T>(
        Func<CancellationToken, Task<T>> action,
        string label,
        int maxRetries,
        int baseDelayMs,
        int totalTimeoutMs,
        CancellationToken cancellationToken,
        Func<long>? clock,
        Func<int, CancellationToken, Task>? delayFunc)
    {
        var getClock = clock ?? (() => Environment.TickCount64);
        var doDelay = delayFunc ?? Task.Delay;

        // Linked CTS fires when either the caller-supplied token is cancelled
        // or the total retry budget expires — whichever comes first.
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgetCts.CancelAfter(totalTimeoutMs);

        Exception? lastError = null;
        var overallStart = getClock();

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            // Distinguish external cancellation from budget exhaustion.
            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            if (budgetCts.IsCancellationRequested)
            {
                var elapsed = getClock() - overallStart;
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
                    var remaining = totalTimeoutMs - (getClock() - overallStart);
                    // Clamp: cap to MaxSingleDelayMs and remaining budget so we don't overshoot.
                    var delay = (int)Math.Min(rawDelay, Math.Min(MaxSingleDelayMs, Math.Max(0, remaining)));
                    Console.Error.WriteLine($"      🔄 {label}: retry {attempt}/{maxRetries} (waiting {delay / 1000}s)");
                    await doDelay(delay, budgetCts.Token);
                }
                return await action(budgetCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // External cancellation — propagate immediately.
            }
            catch (OperationCanceledException) when (budgetCts.IsCancellationRequested)
            {
                // Budget exhausted — stop retrying.
                var elapsed = getClock() - overallStart;
                Console.Error.WriteLine(
                    $"      ⏱️  {label}: total retry budget exhausted ({elapsed / 1000}s elapsed, cap {totalTimeoutMs / 1000}s). Giving up.");
                break;
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
            $"{label}: all attempts failed after {(getClock() - overallStart) / 1000}s: {lastError?.Message}", lastError);
    }
}
