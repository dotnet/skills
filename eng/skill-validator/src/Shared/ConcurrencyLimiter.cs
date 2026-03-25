namespace SkillValidator.Shared;

/// <summary>
/// Concurrency limiter equivalent to p-limit in Node.js.
/// Wraps a SemaphoreSlim to limit concurrent async operations.
/// </summary>
public sealed class ConcurrencyLimiter(int maxConcurrency) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(1, maxConcurrency));

    public async Task<T> RunAsync<T>(Func<Task<T>> fn)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await fn();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
