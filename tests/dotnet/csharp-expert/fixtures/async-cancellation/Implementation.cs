internal static class Worker
{
    public static async Task ObserveCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
