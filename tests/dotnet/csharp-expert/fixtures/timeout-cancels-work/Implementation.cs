internal sealed class WorkTracker
{
    private int _completedWorkCount;

    public int CompletedWorkCount => Volatile.Read(ref _completedWorkCount);

    public async Task<string> RunWithTimeoutAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var work = DoWorkAsync(CancellationToken.None);

        try
        {
            return await work.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return "timed-out";
        }
    }

    private async Task<string> DoWorkAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
        Interlocked.Increment(ref _completedWorkCount);
        return "completed";
    }
}
