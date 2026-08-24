namespace LegacyConfiguration;

public static class LegacyConfig
{
    public static string? Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;
}
