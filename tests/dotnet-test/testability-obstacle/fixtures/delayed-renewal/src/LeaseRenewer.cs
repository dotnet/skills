namespace Leasing;

public sealed class LeaseRenewer
{
    public async Task<DateTimeOffset> RenewAfterAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken);
        return DateTimeOffset.UtcNow;
    }
}
