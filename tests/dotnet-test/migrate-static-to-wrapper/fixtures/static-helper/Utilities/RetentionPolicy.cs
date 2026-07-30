namespace StaticHelper.Utilities;

/// <summary>
/// Called from ~40 call sites across the codebase as <c>RetentionPolicy.IsExpired(...)</c>.
/// Callers must keep working unchanged.
/// </summary>
public static class RetentionPolicy
{
    public static bool IsExpired(DateTime createdAtUtc, int retentionDays)
    {
        return DateTime.UtcNow > createdAtUtc.AddDays(retentionDays);
    }

    public static DateTime NextPurgeWindow()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1).AddHours(2);
    }

    public static string DescribeAge(DateTime createdAtUtc)
    {
        var age = DateTime.UtcNow - createdAtUtc;
        return age.TotalDays >= 1
            ? $"{(int)age.TotalDays} day(s) old"
            : $"{(int)age.TotalHours} hour(s) old";
    }
}
