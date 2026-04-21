namespace StaticHeavy.Services;

public class NotificationService
{
    public void SendReminder(Order order)
    {
        var daysUntilExpiry = (order.ExpiresAt - DateTime.UtcNow).TotalDays;

        if (daysUntilExpiry <= 7 && daysUntilExpiry > 0)
        {
            var message = $"Order {order.Id} expires in {daysUntilExpiry:F0} days";
            Console.WriteLine(message);

            var logPath = Path.Combine(
                Environment.GetEnvironmentVariable("LOG_PATH") ?? "/tmp/logs",
                $"reminder_{DateTime.UtcNow:yyyyMMdd}.log");

            File.AppendAllText(logPath, $"{DateTime.UtcNow:O} - {message}\n");
        }
    }

    public async Task<bool> CheckServiceHealthAsync(string healthUrl)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            var response = await client.GetAsync(healthUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
