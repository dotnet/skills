namespace Billing;

/// <summary>Reports a platform label for the current target framework.</summary>
public static class PlatformInfo
{
    private const string ModernTag = "net10";
    private const string LegacyTag = "net8";

    public static string Current() => Label(Tag());

    public static string Tag()
    {
#if NET8_0
        return LegacyTag;
#else
        return ModernTag;
#endif
    }

    private static string Label(string tag) => $"platform:{tag}";
}
