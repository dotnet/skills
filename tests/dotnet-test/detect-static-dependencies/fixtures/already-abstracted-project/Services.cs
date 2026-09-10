namespace AbstractedServices;

public interface IFileStore
{
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);
}

public sealed class ReportPublisher(
    TimeProvider timeProvider,
    IFileStore fileStore,
    HttpClient httpClient)
{
    public async Task PublishAsync(
        string directory,
        string report,
        CancellationToken cancellationToken)
    {
        var timestamp = timeProvider.GetUtcNow();
        var path = Path.Combine(directory, $"{timestamp:yyyyMMddHHmmss}.txt");

        await fileStore.WriteAsync(path, report, cancellationToken);
        await httpClient.PostAsync(
            "reports",
            new StringContent(report),
            cancellationToken);
    }
}
