namespace Features;

public static class FeatureFlags
{
    public static bool IsEnabled(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
