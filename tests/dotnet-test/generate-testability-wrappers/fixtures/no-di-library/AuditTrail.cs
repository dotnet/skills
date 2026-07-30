namespace AuditLibrary;

// Consumed by a WinForms desktop app that news up this type directly.
// There is no host builder, no service collection and no container.
public static class AuditTrail
{
    private static readonly List<string> s_entries = [];

    public static void Record(string action)
    {
        s_entries.Add($"{DateTime.UtcNow:O}\t{action}");
    }

    public static bool IsWithinRetentionWindow(DateTime recordedAtUtc, int retentionDays)
        => DateTime.UtcNow - recordedAtUtc <= TimeSpan.FromDays(retentionDays);

    public static IReadOnlyList<string> Entries => s_entries;
}
