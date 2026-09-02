namespace NeedsWrappers.Services;

public sealed class WeatherClient
{
    public async Task<string> GetForecastAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(endpoint, cancellationToken);
    }
}
