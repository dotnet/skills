namespace FullPipeline.Services;

public class SubscriptionManager
{
    public Subscription CreateTrial(string userId)
    {
        return new Subscription
        {
            UserId = userId,
            StartedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            Plan = "trial"
        };
    }

    public bool IsActive(Subscription sub)
    {
        return DateTime.UtcNow < sub.ExpiresAt;
    }

    public void ExportSubscription(Subscription sub)
    {
        var exportDir = Environment.GetEnvironmentVariable("EXPORT_DIR") ?? "/tmp/exports";
        if (!Directory.Exists(exportDir))
        {
            Directory.CreateDirectory(exportDir);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(sub);
        File.WriteAllText(Path.Combine(exportDir, $"{sub.UserId}.json"), json);
        Console.WriteLine($"Exported subscription for {sub.UserId}");
    }
}

public class Subscription
{
    public string UserId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Plan { get; set; } = "";
}
