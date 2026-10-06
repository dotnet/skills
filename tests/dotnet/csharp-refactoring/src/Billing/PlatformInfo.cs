namespace Billing;

/// <summary>Reports a platform label for the current target framework.</summary>
public static class PlatformInfo
{
    private const string ModernTag = "net10";
    private const string LegacyTag = "net8";

    public static string Current()
    {
#if NET8_0
        return Label(LegacyTag);
#else
        return Label(ModernTag);
#endif
    }

    private static string Label(string tag) => $"platform:{tag}";
}
