namespace ReadyToMigrate.Services;

public class AuditLogger
{
    public void LogAction(string userId, string action)
    {
        var timestamp = DateTime.UtcNow;
        var entry = $"[{timestamp:O}] User={userId} Action={action}";
        Console.WriteLine(entry);
    }
}
