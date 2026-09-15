namespace Billing;

/// <summary>Parses application settings, returning a fallback when the raw value is missing or invalid.</summary>
public static class AppSettingsHelper
{
    public static int ParseIntSetting(string? raw, int fallback)
        => int.TryParse(raw, out var v) ? v : fallback;

    public static bool ParseBoolSetting(string? raw, bool fallback)
        => bool.TryParse(raw, out var v) ? v : fallback;
}

/// <summary>Reads configuration values, returning a fallback when the raw value is missing or invalid.</summary>
public static class ConfigReader
{
    public static int ReadInt(string? raw, int fallback)
        => int.TryParse(raw, out var v) ? v : fallback;

    public static bool ReadBool(string? raw, bool fallback)
        => bool.TryParse(raw, out var v) ? v : fallback;
}
